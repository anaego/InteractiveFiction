using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>
    /// Abstract base class for UniText text rendering components.
    /// Contains all shared text processing logic (Unicode, BiDi, shaping, line breaking,
    /// modifiers, emoji, font fallback, variable fonts).
    /// </summary>
    /// <remarks>
    /// Concrete subclasses provide the rendering backend:
    /// <see cref="UniText"/> for Canvas (CanvasRenderer), <see cref="UniTextWorld"/> for world-space (MeshRenderer).
    /// </remarks>
    [ExecuteAlways]
    public abstract partial class UniTextBase : MaskableGraphic
#if UNITY_EDITOR
        , ISerializationCallbackReceiver
#endif
    {

        #region Serialized Fields

        [TextArea(3, 10)]
        [SerializeField]
        [Tooltip("The text content to display. Supports Unicode, emoji, and custom markup.")]
        private string text = "";

        [NonSerialized] protected ReadOnlyMemory<char> sourceText;
        [NonSerialized] private bool isTextFromBuffer;

        [NonSerialized] private IUniTextResolver textResolver;
        [NonSerialized] private ReadOnlyMemory<char> resolvedText;
        [NonSerialized] private bool hasResolvedText;

        [SerializeField]
        [Tooltip("Font collection with primary font and fallback chain.")]
        private UniTextFontStack fontStack;

        [SerializeField]
        [Tooltip("Base font size in points.")]
        protected float fontSize = 36f;

        [SerializeField]
        [Tooltip("Base text direction. Auto detects from first strong directional character.")]
        protected TextDirection baseDirection = TextDirection.Auto;

        [SerializeField]
        [Tooltip("Enable word wrapping at container boundaries.")]
        protected bool wordWrap = true;

        [SerializeField]
        [Tooltip("Horizontal text alignment within the container.")]
        private HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left;

        [SerializeField]
        [Tooltip("Vertical text alignment within the container.")]
        private VerticalAlignment verticalAlignment = VerticalAlignment.Top;

        [SerializeField]
        [Tooltip("Top edge metric for text box trimming. CapHeight removes space above capital letters.")]
        protected TextOverEdge overEdge = TextOverEdge.Ascent;

        [SerializeField]
        [Tooltip("Bottom edge metric for text box trimming. Baseline removes space below the last line.")]
        protected TextUnderEdge underEdge = TextUnderEdge.Descent;

        [SerializeField]
        [Tooltip("How extra leading from line-height is distributed: HalfLeading (CSS), LeadingAbove (Figma), LeadingBelow (Android).")]
        protected LeadingDistribution leadingDistribution = LeadingDistribution.HalfLeading;

        [SerializeField]
        [Tooltip("Automatically adjust font size to fit container.")]
        protected bool autoSize;

        [SerializeField]
        [Tooltip("Minimum font size when auto-sizing.")]
        protected float minFontSize = 10f;

        [SerializeField]
        [Tooltip("Maximum font size when auto-sizing.")]
        protected float maxFontSize = 72f;

        [SerializeField]
        [Tooltip("Modifier/rule pairs that define how markup is parsed and applied (e.g., color, bold, links).")]
        private StyledList<Style> styles = new();

        [SerializeField]
        [Tooltip("Shared modifier configurations (ScriptableObjects) to apply in addition to local styles.")]
        private StyledList<StylePreset> stylePresets = new();

        /// <summary>Runtime copies of stylePresets to avoid ownership conflicts.</summary>
        private readonly List<StylePreset> runtimeStylePresetCopies = new();

        [SerializeField]
        [Tooltip("SDF: rounded corners on outline/underlay effects. MSDF: sharp corners.")]
        private UniTextRenderMode renderMode = UniTextRenderMode.SDF;

        [SerializeReference]
        [TypeSelector]
        [Tooltip("Text highlighter for visual feedback (click, hover, selection). Set to null to disable.")]
        private TextHighlighter highlighter = new DefaultTextHighlighter();

        #endregion

        #region Runtime State

        protected TextProcessor textProcessor;
        private UniTextFontProvider fontProvider;
        protected UniTextMeshGenerator meshGenerator;
        private AttributeParser attributeParser;
        protected UniTextBuffers buffers;

        private UniTextDirtyFlags dirtyFlags = UniTextDirtyFlags.All;

        /// <summary>Gets the current dirty flags indicating what needs rebuilding.</summary>
        public UniTextDirtyFlags CurrentDirtyFlags => dirtyFlags;
        private bool textIsParsed;
        private bool isRegisteredDirty;

        private float resultWidth;
        private float resultHeight;

        private struct RefCountTracker
        {
            private PooledBuffer<long> current;
            private PooledBuffer<long> previous;

            public int Count => current.count;

            public void Update(GlyphAtlas atlas, ref PooledBuffer<long> newKeys)
            {
                (previous, current) = (current, previous);
                current.FakeClear();
                current.EnsureCapacity(newKeys.count);
                newKeys.Span.CopyTo(current.data);
                current.count = newKeys.count;
                for (int i = 0; i < current.count; i++)
                    atlas.AddRef(current.data[i]);
                for (int i = 0; i < previous.count; i++)
                    atlas.Release(previous.data[i]);
            }

            public void ReleaseAll(GlyphAtlas atlas)
            {
                for (int i = 0; i < current.count; i++)
                    atlas.Release(current.data[i]);
                current.FakeClear();
            }

            public void Return()
            {
                current.Return();
                previous.Return();
            }
        }

        private RefCountTracker glyphRefs;
        private RefCountTracker emojiRefs;

        protected List<UniTextRenderData> renderData;

        private float lastKnownWidth = -1;
        private float lastKnownHeight = -1;

        /// <summary>Raised before text is rebuilt.</summary>
        public event Action Rebuilding;

        /// <summary>Raised after glyph positioning but before mesh generation. Modifiers inject virtual PositionedGlyphs here.</summary>
        public event Action BeforeGenerateMesh;

        /// <summary>Raised when the RectTransform height changes.</summary>
        public event Action RectHeightChanged;

        /// <summary>Raised when dirty flags change, indicating what needs rebuilding.</summary>
        public event Action<UniTextDirtyFlags> DirtyFlagsChanged;

        #endregion

        #region Public API

        /// <summary>Gets the text processor instance handling shaping and layout.</summary>
        public TextProcessor TextProcessor => textProcessor;

        /// <summary>Gets the mesh generator instance.</summary>
        public UniTextMeshGenerator MeshGenerator => meshGenerator;

        /// <summary>Gets the font provider managing font assets and fallbacks.</summary>
        public UniTextFontProvider FontProvider => fontProvider;

        /// <summary>Gets the buffer container for text processing.</summary>
        public UniTextBuffers Buffers => buffers;

        /// <summary>
        /// Gets the runtime source text — the last value assigned via <see cref="Text"/>
        /// or any <c>SetText</c> overload, before any resolver substitution. Zero-alloc.
        /// Backing is either a <see cref="string"/> or a <see cref="char"/> buffer supplied
        /// to <see cref="SetText(char[],int,int)"/>.
        /// </summary>
        public ReadOnlyMemory<char> RawText => sourceText;

        /// <summary>
        /// Gets the substitute produced by the attached <see cref="TextResolver"/> on the
        /// last rebuild, or an empty memory when no resolver is attached or
        /// <see cref="IUniTextResolver.TryResolve"/> returned <see langword="false"/>.
        /// Zero-alloc. Test <see cref="TextOverride"/> for
        /// <see cref="TextOverrideSource.Resolver"/> to know if this value is in use.
        /// </summary>
        public ReadOnlyMemory<char> ResolvedText => hasResolvedText ? resolvedText : default;

        /// <summary>
        /// Gets the text actually fed into the parsing / shaping / layout pipeline: the
        /// resolver's output if one is active, otherwise <see cref="RawText"/>. Zero-alloc.
        /// Still contains markup; for the markup-stripped form use <see cref="CleanText"/>.
        /// </summary>
        public ReadOnlyMemory<char> RenderedText => hasResolvedText ? resolvedText : sourceText;

        /// <summary>
        /// Gets <see cref="RenderedText"/> with parsed markup removed. Zero-alloc. The
        /// backing buffer is pooled and may be rewritten on the next parse — do not store
        /// the span; call <c>new string(span)</c> if you need a stable string.
        /// </summary>
        public ReadOnlySpan<char> CleanText =>
            attributeParser != null ? attributeParser.CleanTextSpan : RenderedText.Span;

        /// <summary>
        /// Combination of flags describing which runtime source(s) are currently overriding
        /// the serialized <see cref="Text"/>. Flags may combine — for example,
        /// <see cref="TextOverrideSource.SetText"/> | <see cref="TextOverrideSource.Resolver"/>
        /// when a <c>SetText</c> buffer feeds an attached resolver that further substitutes
        /// the text.
        /// </summary>
        public TextOverrideSource TextOverride =>
            (isTextFromBuffer ? TextOverrideSource.SetText : 0) |
            (hasResolvedText ? TextOverrideSource.Resolver : 0);

        /// <summary>
        /// Gets or sets a resolver that may override the source text before parsing without
        /// modifying the serialized <c>text</c> field. Useful for editor-time localization
        /// preview and runtime text-binding without dirtying scenes or prefabs.
        /// See <see cref="IUniTextResolver"/> for the contract.
        /// </summary>
        public IUniTextResolver TextResolver
        {
            get => textResolver;
            set
            {
                if (textResolver == value) return;
                var previous = textResolver;
                textResolver = value;
                hasResolvedText = false;
                resolvedText = default;
                previous?.OnDetached(this);
                value?.OnAttached(this);
                SetDirty(UniTextDirtyFlags.Text);
            }
        }

        /// <summary>Gets the computed size of the rendered text.</summary>
        public Vector2 ResultSize => new(resultWidth, resultHeight);

        /// <summary>Gets the positioned glyphs after processing.</summary>
        public ReadOnlySpan<PositionedGlyph> ResultGlyphs => textProcessor != null ? textProcessor.PositionedGlyphs : ReadOnlySpan<PositionedGlyph>.Empty;

        /// <summary>Gets the primary font from the font collection.</summary>
        public UniTextFont PrimaryFont => fontStack?.PrimaryFont;

        /// <summary>Gets the current effective font size (accounts for auto-sizing).</summary>
        public float CurrentFontSize => autoSize
            ? (cachedEffectiveFontSize > 0 ? cachedEffectiveFontSize : maxFontSize)
            : fontSize;

        /// <summary>Gets the list of registered modifiers.</summary>
        public IReadOnlyList<Style> Styles => styles;

        /// <summary>Gets the list of modifier configuration assets.</summary>
        public IReadOnlyList<StylePreset> StylePresets => stylePresets;

        /// <summary>
        /// Gets or sets the serialized source text. The getter returns the serialized field
        /// as-is and has no side effects — use <see cref="RenderedText"/> to observe what is
        /// actually being rendered when an override (<c>SetText</c> buffer or
        /// <see cref="TextResolver"/>) is active.
        /// </summary>
        /// <remarks>
        /// The setter normalizes CRLF to LF, writes both the serialized field and the runtime
        /// source buffer, and clears any prior <c>SetText</c> override. It does not affect an
        /// attached <see cref="TextResolver"/>.
        /// </remarks>
        public string Text
        {
            get => text;
            set
            {
                if (value != null && value.IndexOf('\r') >= 0)
                    value = NormalizeLineEndings(value);

                if (!isTextFromBuffer && text == value) return;
                text = value;
                sourceText = (value ?? "").AsMemory();
                isTextFromBuffer = false;
                if (sourceText.IsEmpty)
                {
                    DeInit();
                }
                else
                {
                    SetDirty(UniTextDirtyFlags.Text);
                }
            }
        }

        /// <summary>
        /// Sets text content from a char array without allocating a string.
        /// Ideal for frequently updated text (timers, scores, etc.).
        /// </summary>
        public void SetText(char[] source, int start, int length)
        {
            sourceText = new ReadOnlyMemory<char>(source, start, length);
            isTextFromBuffer = true;
            if (length == 0)
            {
                DeInit();
            }
            else
            {
                SetDirty(UniTextDirtyFlags.Text);
            }
        }

        /// <summary>
        /// Sets the text to render without writing to the serialized <c>text</c> field.
        /// The change is visible at runtime and in edit mode without marking the scene or
        /// prefab as dirty — suitable for editor-time preview (localization) or transient
        /// runtime substitution.
        /// </summary>
        /// <param name="source">The text buffer to render. Must remain valid until the next
        /// text assignment on this component.</param>
        /// <remarks>
        /// Unlike the <see cref="Text"/> setter, this method does not normalize line endings
        /// and does not persist the value. For derived text reacting to an external signal,
        /// consider <see cref="TextResolver"/> instead.
        /// </remarks>
        public void SetText(ReadOnlyMemory<char> source)
        {
            sourceText = source;
            isTextFromBuffer = true;
            if (sourceText.IsEmpty)
            {
                DeInit();
            }
            else
            {
                SetDirty(UniTextDirtyFlags.Text);
            }
        }

        /// <summary>
        /// Sets the text to render without writing to the serialized <c>text</c> field.
        /// Convenience overload equivalent to <c>SetText(source.AsMemory())</c>.
        /// The change does not mark the scene or prefab as dirty.
        /// </summary>
        /// <param name="source">The text to render. <see langword="null"/> is treated as empty.</param>
        public void SetText(string source) => SetText((source ?? "").AsMemory());

        private static string NormalizeLineEndings(string input)
        {
            var crlfCount = 0;
            for (var i = 0; i < input.Length - 1; i++)
            {
                if (input[i] == '\r' && input[i + 1] == '\n')
                    crlfCount++;
            }

            return string.Create(input.Length - crlfCount, input, static (span, src) =>
            {
                var writePos = 0;
                for (var i = 0; i < src.Length; i++)
                {
                    var c = src[i];
                    if (c == '\r')
                    {
                        if (i + 1 < src.Length && src[i + 1] == '\n')
                            continue;
                        span[writePos++] = '\n';
                    }
                    else
                    {
                        span[writePos++] = c;
                    }
                }
            });
        }

        /// <summary>Gets or sets the text highlighter for visual feedback on interactions.</summary>
        public TextHighlighter Highlighter
        {
            get => highlighter;
            set
            {
                if (highlighter == value) return;
                highlighter?.Destroy();
                highlighter = value;
                highlighter?.Initialize(this);
            }
        }

        /// <summary>Gets or sets the font collection.</summary>
        public UniTextFontStack FontStack
        {
            get => fontStack;
            set
            {
                if (fontStack == value) return;

#if UNITY_EDITOR
                UnlistenConfigChanged();
#endif
                if (fontStack != null) fontStack.Changed -= OnConfigChanged;
                fontStack = value;
                if (fontStack != null) fontStack.Changed += OnConfigChanged;

#if UNITY_EDITOR
                ListenConfigChanged();
#endif
                SetDirty(UniTextDirtyFlags.Font);
            }
        }

        /// <summary>Gets or sets the base font size in points.</summary>
        public float FontSize
        {
            get => fontSize;
            set
            {
                if (Mathf.Approximately(fontSize, value)) return;
                fontSize = Mathf.Max(0.01f, value);
                SetDirty(UniTextDirtyFlags.FontSize);
            }
        }

        /// <summary>
        /// Gets or sets the default BCP 47 language tag for this text (e.g. <c>zh-Hans</c>,
        /// <c>zh-Hant</c>, <c>ja</c>, <c>ko</c>, <c>en-US</c>). Activates the OpenType <c>locl</c>
        /// feature and drives <see cref="FontFamily.preferredLanguage"/> font selection.
        /// Per-range overrides via <c>&lt;lang=...&gt;...&lt;/lang&gt;</c> take priority.
        /// </summary>
        /// <remarks>
        /// Runtime convenience on top of <see cref="LanguageModifier"/>. The setter uses
        /// <see cref="SetWholeText{T}"/> / <see cref="ClearWholeText{T}"/> — shared presets
        /// are never modified; only the local <see cref="Styles"/> list is touched.
        /// </remarks>
        public string Language
        {
            get => GetWholeTextParameter<LanguageModifier>();
            set
            {
                if (string.IsNullOrEmpty(value)) ClearWholeText<LanguageModifier>();
                else SetWholeText<LanguageModifier>(value);
            }
        }

        /// <summary>Gets or sets the base text direction (LTR, RTL, or Auto-detect).</summary>
        public TextDirection BaseDirection
        {
            get => baseDirection;
            set
            {
                if (baseDirection == value) return;
                baseDirection = value;
                SetDirty(UniTextDirtyFlags.Direction);
            }
        }

        /// <summary>Gets or sets whether word wrapping is enabled.</summary>
        public bool WordWrap
        {
            get => wordWrap;
            set
            {
                if (wordWrap == value) return;
                wordWrap = value;
                SetDirty(UniTextDirtyFlags.Layout);
            }
        }

        /// <summary>Gets or sets the horizontal text alignment.</summary>
        public HorizontalAlignment HorizontalAlignment
        {
            get => horizontalAlignment;
            set
            {
                if (horizontalAlignment == value) return;
                horizontalAlignment = value;
                SetDirty(UniTextDirtyFlags.Alignment);
            }
        }

        /// <summary>Gets or sets the vertical text alignment.</summary>
        public VerticalAlignment VerticalAlignment
        {
            get => verticalAlignment;
            set
            {
                if (verticalAlignment == value) return;
                verticalAlignment = value;
                SetDirty(UniTextDirtyFlags.Alignment);
            }
        }

        /// <summary>Gets or sets the top edge metric for text box trimming.</summary>
        public TextOverEdge OverEdge
        {
            get => overEdge;
            set
            {
                if (overEdge == value) return;
                overEdge = value;
                SetDirty(UniTextDirtyFlags.Layout);
            }
        }

        /// <summary>Gets or sets the bottom edge metric for text box trimming.</summary>
        public TextUnderEdge UnderEdge
        {
            get => underEdge;
            set
            {
                if (underEdge == value) return;
                underEdge = value;
                SetDirty(UniTextDirtyFlags.Layout);
            }
        }

        /// <summary>Gets or sets how extra leading from line-height is distributed.</summary>
        public LeadingDistribution LeadingDistribution
        {
            get => leadingDistribution;
            set
            {
                if (leadingDistribution == value) return;
                leadingDistribution = value;
                SetDirty(UniTextDirtyFlags.Layout);
            }
        }

        /// <summary>Gets or sets the text rendering mode (SDF for rounded, MSDF for sharp corner effects).</summary>
        public UniTextRenderMode RenderMode
        {
            get => renderMode;
            set
            {
                if (renderMode == value) return;
                Cat.MeowFormat("[UniText] RenderMode switch '{0}': {1}→{2}", name, renderMode, value);
                ReleaseAllGlyphAtlasRefs();
                renderMode = value;
                if (textProcessor != null)
                    textProcessor.HasValidGlyphsInAtlas = false;
                SetDirty(UniTextDirtyFlags.Material);
            }
        }

        /// <summary>Gets or sets whether automatic font sizing is enabled.</summary>
        public bool AutoSize
        {
            get => autoSize;
            set
            {
                if (autoSize == value) return;
                autoSize = value;
                SetDirty(UniTextDirtyFlags.Layout);
            }
        }

        /// <summary>Gets or sets the minimum font size for auto-sizing.</summary>
        public float MinFontSize
        {
            get => minFontSize;
            set
            {
                value = Mathf.Max(0.01f, value);
                if (Mathf.Approximately(minFontSize, value)) return;
                minFontSize = value;
                if (autoSize) SetDirty(UniTextDirtyFlags.Layout);
            }
        }

        /// <summary>Gets or sets the maximum font size for auto-sizing.</summary>
        public float MaxFontSize
        {
            get => maxFontSize;
            set
            {
                value = Mathf.Max(0.01f, value);
                if (Mathf.Approximately(maxFontSize, value)) return;
                maxFontSize = value;
                if (autoSize) SetDirty(UniTextDirtyFlags.Layout);
            }
        }

        /// <inheritdoc/>
        public override Color color
        {
            get => base.color;
            set
            {
                if (base.color == value) return;
                base.color = value;
                SetDirty(UniTextDirtyFlags.Color);
            }
        }

        /// <summary>Marks the specified aspects of the text as needing rebuild.</summary>
        public void SetDirty(UniTextDirtyFlags flags)
        {
            if (flags == UniTextDirtyFlags.None) return;
            Cat.MeowFormat("[UniText] SetDirty: {0}, {1}", flags, name);
            dirtyFlags |= flags;

            if ((flags & UniTextDirtyFlags.Font) != 0)
            {
                DeinitializeAllStyles();
                fontProvider = null;
                meshGenerator?.Dispose();
                meshGenerator = null;
            }

            if ((flags & UniTextDirtyFlags.FullRebuild) != 0)
            {
                textIsParsed = false;
                textProcessor?.InvalidateFirstPassData();
                InvalidateLayoutCache();
            }
            else if ((flags & UniTextDirtyFlags.LayoutRebuild) != 0)
            {
                textProcessor?.InvalidateLayoutData();
                InvalidateLayoutCache();
            }
            else if ((flags & UniTextDirtyFlags.Alignment) != 0)
            {
                textProcessor?.InvalidatePositionedGlyphs();
            }

            RegisterDirty(this);

            DirtyFlagsChanged?.Invoke(flags);
            OnSetDirty(flags);
        }

        /// <inheritdoc/>
        public override void SetVerticesDirty() { }

        /// <inheritdoc/>
        public override void SetMaterialDirty() { }

        #endregion

        #region Modifiers

        /// <summary>Adds a style to this component at runtime.</summary>
        public void AddStyle(Style style)
        {
            if (!style.IsValid) return;

            if (style.IsRegistered && style.Owner == this) return;

            if (style.Owner != null && style.Owner != this)
            {
                Debug.LogError($"[UniText] Style already owned by {style.Owner.name}. Cannot add to {name}.");
                return;
            }

            styles.Add(style);

            if (textProcessor != null)
            {
                EnsureAttributeParserCreated();
                style.Register(this, attributeParser);
                SetDirty(UniTextDirtyFlags.Text);
            }
        }
        
#if UNITY_EDITOR
        /// <summary>Editor-only: adds a style without requiring IsValid. Allows adding empty styles for configuration.</summary>
        internal void AddStyle_Editor(Style style)
        {
            styles.Add(style);
            if (style.IsValid && textProcessor != null)
            {
                EnsureAttributeParserCreated();
                style.Register(this, attributeParser);
            }
            SetDirty(UniTextDirtyFlags.Text);
        }
#endif
        
        /// <summary>Removes a style from this component at runtime.</summary>
        public bool RemoveStyle(Style style)
        {
            var removed = styles.Remove(style);
            if (!removed) return false;

            if (style.IsRegistered && style.Owner == this)
            {
                style.Unregister(attributeParser);
                SetDirty(UniTextDirtyFlags.Text);
            }

            if (styles.Count == 0 && !HasAnyStylePresets())
            {
                DestroyAttributeParser();
            }

            return true;
        }

        /// <summary>Removes all styles from this component.</summary>
        public void ClearStyles()
        {
            for (var i = 0; i < styles.Count; i++)
            {
                styles[i].Unregister(attributeParser);
            }
            styles.Clear();
            DestroyAttributeParser();
        }

        /// <summary>
        /// Adds a standalone parse rule (one that operates without a modifier, e.g. &lt;noparse&gt;).
        /// The rule must report <see cref="IParseRule.IsStandalone"/> as <see langword="true"/>.
        /// </summary>
        /// <param name="rule">The standalone rule to add.</param>
        public void AddRule(IParseRule rule)
        {
            if (rule == null) return;
            if (!rule.IsStandalone)
            {
                Debug.LogError($"[UniText] Rule {rule.GetType().Name} is not standalone. Use AddStyle with a modifier instead.");
                return;
            }

            AddStyle(new Style { Rule = rule });
        }

        /// <summary>Removes a standalone rule previously added via <see cref="AddRule"/>.</summary>
        /// <param name="rule">The rule to remove.</param>
        /// <returns><see langword="true"/> if the rule was found and removed.</returns>
        public bool RemoveRule(IParseRule rule)
        {
            if (rule == null) return false;

            for (var i = 0; i < styles.Count; i++)
            {
                if (styles[i].Rule == rule && styles[i].Modifier == null)
                    return RemoveStyle(styles[i]);
            }
            return false;
        }

        /// <summary>Adds a shared style preset to this component at runtime.</summary>
        public void AddStylePreset(StylePreset preset)
        {
            if (preset == null) return;

            for (var i = 0; i < stylePresets.Count; i++)
            {
                if (stylePresets[i] == preset) return;
            }

            stylePresets.Add(preset);

            if (textProcessor != null)
                ReInitStyles();
        }

        /// <summary>Removes a shared style preset from this component at runtime.</summary>
        public bool RemoveStylePreset(StylePreset preset)
        {
            if (preset == null) return false;

            var removed = stylePresets.Remove(preset);
            if (!removed) return false;

            if (textProcessor != null)
                ReInitStyles();

            return true;
        }

        /// <summary>Removes all shared style presets from this component.</summary>
        public void ClearStylePresets()
        {
            if (stylePresets.Count == 0) return;

            stylePresets.Clear();

            if (textProcessor != null)
                ReInitStyles();
        }

        /// <summary>
        /// Registers a Style with the parser. Called by Style during hot-swap.
        /// </summary>
        internal void RegisterStyleWithParser(Style style)
        {
            if (attributeParser == null) return;
            style.Register(this, attributeParser);
        }

        /// <summary>
        /// Unregisters a Style from the parser. Called by Style during hot-swap.
        /// </summary>
        internal void UnregisterStyleFromParser(Style style)
        {
            style.Unregister(attributeParser);
        }

        /// <summary>Reinitializes all registered modifiers (used by Editor/OnValidate).</summary>
        private void ReInitStyles()
        {
            DestroyAttributeParser();
            EnsureAttributeParserCreated();
        }

        /// <summary>Deinitializes all modifiers but keeps them registered (for font changes).</summary>
        private void DeinitializeAllStyles()
        {
            for (var i = 0; i < styles.Count; i++)
            {
                styles[i].DeinitializeModifier();
            }
            for (var i = 0; i < runtimeStylePresetCopies.Count; i++)
            {
                var config = runtimeStylePresetCopies[i];
                for (var j = 0; j < config.styles.Count; j++)
                {
                    config.styles[j].DeinitializeModifier();
                }
            }
        }

        /// <summary>Resets all Style states (for deserialization/Editor reload).</summary>
        private void ResetAllStyleStates()
        {
            for (var i = 0; i < styles.Count; i++)
            {
                styles[i].ResetState();
            }
            for (var i = 0; i < runtimeStylePresetCopies.Count; i++)
            {
                var config = runtimeStylePresetCopies[i];
                for (var j = 0; j < config.styles.Count; j++)
                {
                    config.styles[j].ResetState();
                }
            }
        }

        private void EnsureAttributeParserCreated()
        {
            if (attributeParser != null) return;
            if (textProcessor == null) return;

            if (styles is { Count: > 0 } || HasAnyStylePresets())
            {
                EnsureRuntimeConfigCopiesCreated();

                attributeParser = new AttributeParser();
                RegisterStylesWithParser(styles);
                for (var i = 0; i < runtimeStylePresetCopies.Count; i++)
                {
                    RegisterStylesWithParser(runtimeStylePresetCopies[i].styles);
                }
                textProcessor.Parsed += attributeParser.Apply;
                SetDirty(UniTextDirtyFlags.Text);
            }
        }

        private void EnsureRuntimeConfigCopiesCreated()
        {
            if (runtimeStylePresetCopies.Count > 0) return;

            for (var i = 0; i < stylePresets.Count; i++)
            {
                var config = stylePresets[i];
                if (config != null)
                {
                    runtimeStylePresetCopies.Add(Instantiate(config));
                }
            }
        }

        private bool HasAnyStylePresets()
        {
            for (var i = 0; i < stylePresets.Count; i++)
            {
                var config = stylePresets[i];
                if (config != null && config.styles is { Count: > 0 })
                    return true;
            }
            return false;
        }

        private void RegisterStylesWithParser(StyledList<Style> mods)
        {
            for (var i = 0; i < mods.Count; i++)
            {
                var mod = mods[i];
                if (mod is { IsValid: true })
                {
                    mod.Register(this, attributeParser);
                }
            }
        }

        private void DestroyAttributeParser()
        {
            if (attributeParser == null) return;

            attributeParser.DeinitializeModifiers();
            ResetAllStyleStates();
            DestroyRuntimeConfigCopies();

            attributeParser.Release();
            if (textProcessor != null)
            {
                textProcessor.Parsed -= attributeParser.Apply;
            }

            attributeParser = null;
            SetDirty(UniTextDirtyFlags.Text);
        }

        #endregion

        #region Lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();
            Cat.Meow($"[UniText] OnEnable, {name}", this);
            sourceText = (text ?? "").AsMemory();
            Sub();
            SetDirty(UniTextDirtyFlags.All);
            highlighter?.Initialize(this);
        }

        protected virtual void Update()
        {
            highlighter?.Update();
            InteractiveRangeRegistry.Get(buffers)?.UpdateProviderHighlighters();
        }

        protected override void OnDisable()
        {
            UnSub();
            base.OnDisable();
            DeInit();
        }

        protected override void OnDestroy()
        {
            highlighter?.Destroy();
            rangeEntriesScratch?.Return();
            rangeEntriesScratch = null;
            base.OnDestroy();
            if (textResolver != null)
            {
                var r = textResolver;
                textResolver = null;
                hasResolvedText = false;
                resolvedText = default;
                r.OnDetached(this);
            }
            DeInit(true);
            DestroyRuntimeConfigCopies();
        }

        protected virtual void Sub()
        {
            if (fontStack != null) fontStack.Changed += OnConfigChanged;
#if UNITY_EDITOR
            ListenConfigChanged();
            UnityEditor.SceneVisibilityManager.visibilityChanged += OnSceneVisibilityChanged;
            SceneVisibilityOverlay.Changed += OnSceneVisibilityChanged;
#endif
            EmojiFont.DisableChanged += OnEmojiFontDisableChanged;
            GlyphAtlas.AnyAtlasCompacted += OnAtlasCompacted;
        }

        protected virtual void UnSub()
        {
            if (fontStack != null) fontStack.Changed -= OnConfigChanged;
#if UNITY_EDITOR
            UnlistenConfigChanged();
            UnityEditor.SceneVisibilityManager.visibilityChanged -= OnSceneVisibilityChanged;
            SceneVisibilityOverlay.Changed -= OnSceneVisibilityChanged;
#endif
            EmojiFont.DisableChanged -= OnEmojiFontDisableChanged;
            GlyphAtlas.AnyAtlasCompacted -= OnAtlasCompacted;
        }

        private void OnAtlasCompacted(GlyphAtlas compactedAtlas)
        {
            bool isMyAtlas = compactedAtlas == GlyphAtlas.GetInstance(RenderMode);
            bool isEmojiAtlas = compactedAtlas == GlyphAtlas.Emoji;
            if (!isMyAtlas && !isEmojiAtlas) return;
            if (isMyAtlas && glyphRefs.Count == 0) return;
            if (isEmojiAtlas && emojiRefs.Count == 0) return;

            Cat.MeowFormat("[UniText] OnAtlasCompacted '{0}': regen mesh, glyphRefs={1}, emojiRefs={2}",
                name, glyphRefs.Count, emojiRefs.Count);

            if (textProcessor != null)
                textProcessor.buf.hasValidGlyphCache = false;
            SetDirty(UniTextDirtyFlags.Material);
        }

        protected void DeInit(bool isDestroying = false)
        {
            Cat.MeowFormat("[UniText] DeInit '{0}': isDestroying={1}, heldKeys={2}+{3}e",
                name, isDestroying, glyphRefs.Count, emojiRefs.Count);
            ReleaseAllGlyphAtlasRefs();
            glyphRefs.Return();
            emojiRefs.Return();
            if (!isDestroying)
            {
                ClearAllRenderers();
            }
            DestroyAttributeParser();
            MeshApplied?.Invoke();

            textProcessor = null;
            fontProvider = null;
            meshGenerator?.Dispose();
            meshGenerator = null;

            OnDeInit();
            buffers?.EnsureReturnBuffers();
            UnregisterDirty(this);
        }

        /// <summary>
        /// Updates glyph atlas reference counts. AddRef all new keys first, then Release
        /// all old keys.
        /// </summary>
        private void UpdateGlyphAtlasRefCounts()
        {
            if (meshGenerator == null) return;

            glyphRefs.Update(GlyphAtlas.GetInstance(RenderMode), ref meshGenerator.usedGlyphKeys);

            var emojiAtlas = GlyphAtlas.Emoji;
            if (emojiAtlas != null)
                emojiRefs.Update(emojiAtlas, ref meshGenerator.usedEmojiKeys);

            Cat.MeowFormat("[UniText] UpdateRefCounts '{0}': glyph={1}, emoji={2}",
                name, glyphRefs.Count, emojiRefs.Count);
        }

        private void ReleaseAllGlyphAtlasRefs()
        {
            if (glyphRefs.Count > 0)
                glyphRefs.ReleaseAll(GlyphAtlas.GetInstance(RenderMode));

            var emojiAtlas = GlyphAtlas.Emoji;
            if (emojiAtlas != null && emojiRefs.Count > 0)
                emojiRefs.ReleaseAll(emojiAtlas);
        }

        private void OnEmojiFontDisableChanged()
        {
            SetDirty(UniTextDirtyFlags.All);
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            var rect = rectTransform.rect;
            var width = rect.width;
            var height = rect.height;

            var widthChanged = !Mathf.Approximately(width, lastKnownWidth);
            var heightChanged = !Mathf.Approximately(height, lastKnownHeight);

            if (heightChanged)
            {
                lastKnownHeight = height;
                RectHeightChanged?.Invoke();
            }

            if (widthChanged)
            {
                lastKnownWidth = width;

                var effectiveFontSize = autoSize ? maxFontSize : fontSize;
                var canReuse = textProcessor != null && textProcessor.CanReuseLines(width, effectiveFontSize, wordWrap);

                if (canReuse)
                {
                    SetDirty(UniTextDirtyFlags.Alignment);
                }
                else
                {
                    SetDirty(UniTextDirtyFlags.Layout);
                }
            }
            else
            {
                SetDirty(UniTextDirtyFlags.Alignment);
            }
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            RecalculateMasking();
            SetDirty(UniTextDirtyFlags.Layout);
        }

        private void OnConfigChanged()
        {
            if (UniTextFont.IsAtlasClearing)
                ReleaseAllGlyphAtlasRefs();
            SetDirty(UniTextDirtyFlags.All);
        }

        private void DestroyRuntimeConfigCopies()
        {
            for (var i = 0; i < runtimeStylePresetCopies.Count; i++)
            {
                ObjectUtils.SafeDestroy(runtimeStylePresetCopies[i]);
            }
            runtimeStylePresetCopies.Clear();
        }

        #endregion

        #region Rebuild

        /// <inheritdoc/>
        public override void Rebuild(CanvasUpdate update) { }

        /// <inheritdoc/>
        protected override void UpdateMaterial() { }

        protected virtual bool ValidateAndInitialize()
        {
            UniTextDebug.BeginSample("UniText.ValidateAndInitialize");

#if UNITY_EDITOR
            if (!TryInitFonts())
            {
                UniTextDebug.EndSample();
                return false;
            }
#endif

            buffers ??= new UniTextBuffers();
            buffers.EnsureRentBuffers(sourceText.Length);

            if (textProcessor == null)
            {
                textProcessor = new TextProcessor(buffers);
                Cat.Meow("[UniText] TextProcessor created", this);
            }

            EnsureAttributeParserCreated();

            if (fontProvider == null)
            {
                fontProvider = new UniTextFontProvider(fontStack);
                meshGenerator = new UniTextMeshGenerator(fontProvider, buffers);
                textProcessor.SetFontProvider(fontProvider);
                Cat.Meow("[UniText] FontProvider created", this);
            }

            UniTextDebug.EndSample();
            return true;
        }

        private ReadOnlySpan<char> ParseOrGetParsedAttributes()
        {
            if (!textIsParsed)
            {
                UniTextDebug.BeginSample("UniText.ParseAttributes");

                if (textResolver != null)
                    hasResolvedText = textResolver.TryResolve(sourceText, out resolvedText);
                else
                    hasResolvedText = false;

                var textToParse = hasResolvedText ? resolvedText.Span : sourceText.Span;

                attributeParser?.ResetModifiers();
                attributeParser?.Parse(textToParse);
                textIsParsed = true;
                UniTextDebug.EndSample();
            }

            if (attributeParser != null) return attributeParser.CleanTextSpan;
            return hasResolvedText ? resolvedText.Span : sourceText.Span;
        }

        private TextProcessSettings CreateProcessSettings(Rect rect, float effectiveFontSize) => new()
        {
            MaxWidth = rect.width,
            MaxHeight = rect.height,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            OverEdge = overEdge,
            UnderEdge = underEdge,
            LeadingDistribution = leadingDistribution,
            fontSize = effectiveFontSize,
            baseDirection = baseDirection,
            enableWordWrap = wordWrap
        };

        #endregion

        #region Abstract / Virtual Contract

        /// <summary>Applies generated mesh data to the rendering backend (CanvasRenderer or MeshRenderer sub-meshes).</summary>
        protected abstract void UpdateRendering();

        /// <summary>Clears all sub-mesh renderers (without destroying GameObjects).</summary>
        protected abstract void ClearAllRenderers();

        /// <summary>Called after SetDirty. Override to trigger Canvas layout rebuild.</summary>
        protected virtual void OnSetDirty(UniTextDirtyFlags flags) { }

        /// <summary>Called during DeInit for subclass-specific cleanup (e.g., stencil materials).</summary>
        protected virtual void OnDeInit() { }

#if UNITEXT_TESTS
        /// <summary>Called after meshes are applied, before ReturnInstanceBuffers. Override for test mesh copying.</summary>
        protected virtual void CopyMeshesForTests() { }
#endif

        #endregion

        #region Glyph Query

        /// <summary>
        /// Collects per-line geometric runs of positioned glyphs whose clusters fall inside
        /// <c>[<paramref name="startCluster"/>, <paramref name="endCluster"/>)</c>. Output bounds
        /// are in mesh-local coordinates with X clamped to each line's visible content extent
        /// (trailing whitespace excluded via <see cref="TextLine.width"/>). One <see cref="LineRangeEntry"/>
        /// is emitted per contiguous run within a line — multiple entries per line are possible if the
        /// matched clusters are non-contiguous in visual order.
        /// </summary>
        /// <param name="startCluster">Cluster start (inclusive).</param>
        /// <param name="endCluster">Cluster end (exclusive).</param>
        /// <param name="output">Pooled list to receive entries (cleared before use).</param>
        public void CollectRangeEntries(int startCluster, int endCluster, PooledList<LineRangeEntry> output)
        {
            output.FakeClear();

            if (textProcessor == null) return;

            var lines = buffers.lines;
            var lineCount = lines.count;
            if (lineCount == 0) return;

            var glyphs = textProcessor.PositionedGlyphs;

            for (var li = 0; li < lineCount; li++)
            {
                ref readonly var line = ref lines[li];
                if (line.range.End <= startCluster || line.range.start >= endCluster) continue;
                if (line.glyphCount == 0) continue;

                var firstG = line.glyphStart;
                var lastG = firstG + line.glyphCount - 1;

                float contentLeft, contentRight;
                if (line.IsRtl)
                {
                    contentRight = glyphs[lastG].right;
                    contentLeft = contentRight - line.widthPx;
                }
                else
                {
                    contentLeft = glyphs[firstG].left;
                    contentRight = contentLeft + line.widthPx;
                }

                var emitFirstG = -1;
                var emitLastG = -1;
                float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;

                for (var g = firstG; g <= lastG; g++)
                {
                    ref readonly var glyph = ref glyphs[g];
                    var inRange = glyph.cluster >= startCluster && glyph.cluster < endCluster;

                    if (inRange)
                    {
                        if (emitFirstG < 0)
                        {
                            emitFirstG = g;
                            minX = glyph.left;
                            maxX = glyph.right;
                            minY = glyph.top;
                            maxY = glyph.bottom;
                        }
                        else
                        {
                            if (glyph.left < minX) minX = glyph.left;
                            if (glyph.right > maxX) maxX = glyph.right;
                            if (glyph.top < minY) minY = glyph.top;
                            if (glyph.bottom > maxY) maxY = glyph.bottom;
                        }
                        emitLastG = g;
                    }
                    else if (emitFirstG >= 0)
                    {
                        var clampedMinX = minX < contentLeft ? contentLeft : minX;
                        var clampedMaxX = maxX > contentRight ? contentRight : maxX;
                        if (clampedMaxX > clampedMinX)
                        {
                            output.Add(new LineRangeEntry
                            {
                                lineIdx = li,
                                firstGlyphIdx = emitFirstG,
                                lastGlyphIdx = emitLastG,
                                minX = clampedMinX, maxX = clampedMaxX,
                                minY = minY, maxY = maxY
                            });
                        }
                        emitFirstG = -1;
                    }
                }

                if (emitFirstG >= 0)
                {
                    var clampedMinX = minX < contentLeft ? contentLeft : minX;
                    var clampedMaxX = maxX > contentRight ? contentRight : maxX;
                    if (clampedMaxX > clampedMinX)
                    {
                        output.Add(new LineRangeEntry
                        {
                            lineIdx = li,
                            firstGlyphIdx = emitFirstG,
                            lastGlyphIdx = emitLastG,
                            minX = clampedMinX, maxX = clampedMaxX,
                            minY = minY, maxY = maxY
                        });
                    }
                }
            }
        }

        private PooledList<LineRangeEntry> rangeEntriesScratch;

        /// <summary>
        /// Gets bounding rectangles for a cluster range. One <see cref="Rect"/> per contiguous run
        /// of glyphs within a line that falls inside <c>[<paramref name="startCluster"/>, <paramref name="endCluster"/>)</c>.
        /// Trailing whitespace at line ends is excluded (CSS Text §4.1.3). Empty wrapped lines whose
        /// break codepoint lies inside the range receive a synthetic narrow rect for caret/selection rendering.
        /// </summary>
        public void GetRangeBounds(int startCluster, int endCluster, IList<Rect> results)
        {
            results.Clear();

            if (textProcessor == null) return;

            var lines = buffers.lines;
            if (lines.count == 0) return;

            rangeEntriesScratch ??= new PooledList<LineRangeEntry>(8);
            CollectRangeEntries(startCluster, endCluster, rangeEntriesScratch);

            var rect = cachedTransformData.rect;
            var glyphs = textProcessor.PositionedGlyphs;
            var referenceLineHeight = glyphs.Length > 0
                ? glyphs[0].bottom - glyphs[0].top
                : CurrentFontSize;

            var entryIdx = 0;
            var entryCount = rangeEntriesScratch.Count;
            float prevLineBottom = 0f;

            for (var li = 0; li < lines.count; li++)
            {
                ref readonly var line = ref lines[li];

                if (line.glyphCount == 0)
                {
                    if (line.range.start >= startCluster && line.range.start < endCluster)
                    {
                        var advances = buffers.perLineAdvances;
                        var emptyTop = prevLineBottom;
                        var emptyH = referenceLineHeight;
                        if (li < advances.count && advances[li] > 0)
                            emptyH = advances[li];
                        else if (li > 0 && li - 1 < advances.count)
                            emptyH = advances[li - 1];

                        var spaceW = emptyH * 0.25f;
                        results.Add(new Rect(rect.xMin, rect.yMax - emptyTop - emptyH, spaceW, emptyH));
                        prevLineBottom = emptyTop + emptyH;
                    }
                    continue;
                }

                while (entryIdx < entryCount && rangeEntriesScratch[entryIdx].lineIdx == li)
                {
                    var e = rangeEntriesScratch[entryIdx];
                    results.Add(new Rect(rect.xMin + e.minX, rect.yMax - e.maxY, e.maxX - e.minX, e.maxY - e.minY));
                    if (e.maxY > prevLineBottom) prevLineBottom = e.maxY;
                    entryIdx++;
                }

                if (li + 1 < lines.count)
                {
                    var lineBottom = glyphs[line.glyphStart].bottom;
                    if (lineBottom > prevLineBottom) prevLineBottom = lineBottom;
                }
            }
        }

        /// <summary>Gets the total number of glyphs.</summary>
        public int GlyphCount => textProcessor?.PositionedGlyphs.Length ?? 0;

        #endregion

#if UNITY_EDITOR

        internal bool sceneVisibilityHidden;

        private void OnSceneVisibilityChanged()
        {
            if (this == null) return;
            var hidden = SceneVisibilityOverlay.Respect
                         && UnityEditor.SceneVisibilityManager.instance.IsHidden(gameObject);
            if (hidden == sceneVisibilityHidden) return;
            sceneVisibilityHidden = hidden;
            if (hidden) ClearAllRenderers();
            else SetDirty(UniTextDirtyFlags.Color);
        }

        /// <summary>Style presets we subscribed to Changed event (for correct unsubscription).</summary>
        private readonly List<StylePreset> subscribedConfigs = new();

        private void ListenConfigChanged()
        {
            UniTextSettings.Changed += OnConfigChanged;
            ListenStylePresetChanged();
        }

        private void UnlistenConfigChanged()
        {
            UniTextSettings.Changed -= OnConfigChanged;
            UnlistenStylePresetChanged();
        }

        internal void ListenStylePresetChanged()
        {
            for (var i = 0; i < stylePresets.Count; i++)
            {
                var config = stylePresets[i];
                if (config != null)
                {
                    config.Changed += OnStylePresetChanged;
                    subscribedConfigs.Add(config);
                }
            }
        }

        internal void UnlistenStylePresetChanged()
        {
            for (var i = 0; i < subscribedConfigs.Count; i++)
            {
                var config = subscribedConfigs[i];
                if (config != null)
                    config.Changed -= OnStylePresetChanged;
            }
            subscribedConfigs.Clear();
        }

        private void OnStylePresetChanged()
        {
            ReInitStyles();
        }

        private bool TryInitFonts()
        {
            var changed = false;

            if (fontStack == null)
            {
                fontStack = UniTextSettings.DefaultFontStack;
                changed = true;
            }

            if (changed) UnityEditor.EditorUtility.SetDirty(this);

            return fontStack != null;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            for (var i = componentsBuffer.count - 1; i >= 0; i--)
            {
                var comp = componentsBuffer[i];
                if (comp == null || comp == this)
                {
                    if (comp != null)
                        comp.isRegisteredDirty = false;
                    componentsBuffer.SwapRemoveAt(i);
                }
            }

            UnregisterDirty(this);

            UnityEditor.EditorApplication.update += OnUpdate;

            void OnUpdate()
            {
                UnityEditor.EditorApplication.update -= OnUpdate;
                if (this == null) return;
                UnlistenStylePresetChanged();
                ListenStylePresetChanged();
                ReInitStyles();
            }
        }

#endif
    }
}
