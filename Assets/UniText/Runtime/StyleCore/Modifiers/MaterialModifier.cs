using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies a user-authored <see cref="Material"/> to a text range by emitting a dedicated sub-mesh
    /// with its own <c>CanvasRenderer</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The custom shader must include <c>UniText_Custom.cginc</c> and declare <c>_MainTex</c> as a
    /// 2DArray — see <c>Assets/Create/UniText/Custom Material Shader</c> for a working template. The
    /// atlas <c>Texture2DArray</c> is bound automatically; per-glyph user data is written into
    /// <c>TEXCOORD2</c> / <c>TEXCOORD3</c> (see <see cref="constantUv2"/>, <see cref="constantUv3"/>,
    /// <see cref="glyphDataWriter"/> and <see cref="OnWriteGlyphUV"/>).
    /// </para>
    /// <para>
    /// Default <see cref="renderOrder"/> is <see cref="MaterialRenderOrder.Replace"/>: the base SDF pass is
    /// suppressed on the range (face alpha zeroed in the main mesh, which also hides EffectModifier
    /// contributions for those glyphs). Use <see cref="MaterialRenderOrder.Over"/> /
    /// <see cref="MaterialRenderOrder.Under"/> to keep the base pass and layer the custom material on top/behind.
    /// </para>
    /// <para>
    /// Parameter: optional tint color. Format: <c>&lt;mat=#RRGGBBAA&gt;</c> or <c>&lt;mat=red&gt;</c>.
    /// The tint multiplies the vertex color that the shader receives.
    /// </para>
    /// <para>
    /// <b>Ordering note (Replace mode):</b> <c>Replace</c> works by zeroing the face-alpha of affected
    /// glyphs in the base mesh during the <c>onGlyph</c> callback. UniText invokes <c>onGlyph</c>
    /// subscribers in the order the corresponding <see cref="Style"/>s appear in the component's Styles
    /// list. If a <see cref="ColorModifier"/> / <see cref="GradientModifier"/> entry comes <i>after</i>
    /// <see cref="MaterialModifier"/>, it will overwrite the zeroed alpha and the base face will become
    /// visible again, defeating Replace. Place <see cref="MaterialModifier"/> <b>after</b> any
    /// color-writing modifiers in the Styles list.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Appearance", 10)]
    [TypeDescription("Applies a custom Material to a text range via a dedicated sub-mesh and CanvasRenderer.")]
    [ParameterField(0, "Tint", "color", "#FFFFFFFF")]
    public class MaterialModifier : SubMeshModifier
    {
        /// <summary>Controls how the custom material composes with the base text pass on the range.</summary>
        public enum MaterialRenderOrder : byte
        {
            /// <summary>Base text pass is suppressed on the range (face alpha zeroed in the main mesh).</summary>
            Replace = 0,
            /// <summary>Custom material renders in front of the base text pass.</summary>
            Over = 1,
            /// <summary>Custom material renders behind the base text pass.</summary>
            Under = 2,
        }

        /// <summary>Per-glyph context passed to <see cref="glyphDataWriter"/> and <see cref="OnWriteGlyphUV"/>.</summary>
        public readonly ref struct MaterialGlyphContext
        {
            public readonly int cluster;
            /// <summary>
            /// Monotonic 0-based index of this glyph across <b>all</b> ranges selected by this modifier in
            /// the current rebuild. Useful for staggered per-glyph animations. Not reset between ranges.
            /// </summary>
            public readonly int glyphIndexInSelection;
            public readonly float cursorX;
            public readonly float baselineY;
            public readonly float glyphHeight;
            public readonly string parameter;

            public MaterialGlyphContext(int cluster, int glyphIndexInSelection, float cursorX, float baselineY, float glyphHeight, string parameter)
            {
                this.cluster = cluster;
                this.glyphIndexInSelection = glyphIndexInSelection;
                this.cursorX = cursorX;
                this.baselineY = baselineY;
                this.glyphHeight = glyphHeight;
                this.parameter = parameter;
            }
        }

        /// <summary>Delegate signature for writing per-glyph custom UV data at sub-mesh build time.</summary>
        public delegate void MaterialGlyphWriter(in MaterialGlyphContext ctx, out Vector4 uv2, out Vector4 uv3);

        [Tooltip("Custom material to apply to the range. The shader must include UniText_Custom.cginc.")]
        [SerializeField] private Material material;

        [Tooltip("How the custom material composes with the base text pass. Replace suppresses the base pass on the range.")]
        [SerializeField] private MaterialRenderOrder renderOrder = MaterialRenderOrder.Replace;

        [Tooltip("Stable sort key within the same render order group (lower renders first).")]
        [SerializeField] private int sortIndex;

        [Tooltip("Constant value written to TEXCOORD2 of every glyph vertex in the sub-mesh.")]
        [SerializeField] private Vector4 constantUv2;

        [Tooltip("Constant value written to TEXCOORD3 of every glyph vertex in the sub-mesh.")]
        [SerializeField] private Vector4 constantUv3;

        [Tooltip("Optional override material for emoji glyphs in the range. When null, emoji glyphs in the range are rendered by the base emoji pass (MaterialModifier does nothing for them).")]
        [SerializeField] private Material emojiMaterial;

        [Tooltip("Expand each glyph quad outward by this many em-units so shader effects (glow, edge, etc.) " +
                 "aren't clipped by the glyph's tight bounding box. Negative = read from shader's " +
                 "_UniTextMeshPadding property (shader-driven default).")]
        [SerializeField] private float quadPaddingOverride = -1f;

        [Tooltip("When true (default) the material is cloned via UniTextCustomMaterialCache so runtime " +
                 "keyword / texture changes are isolated from the source asset and other usages. " +
                 "When false the source material is used directly — shader property edits (material.SetColor " +
                 "etc.) are reflected immediately across all MaterialModifiers that reference it, but the " +
                 "caller is responsible for setting MSDF/emoji keywords correctly (via material toggles or " +
                 "Material.EnableKeyword). The atlas texture is still bound automatically in both modes.")]
        [SerializeField] private bool cloneMaterial = true;

        /// <summary>Optional per-glyph data writer. Takes precedence over <see cref="OnWriteGlyphUV"/>.</summary>
        [NonSerialized] public MaterialGlyphWriter glyphDataWriter;

        /// <summary>Gets or sets the custom material. Triggers a mesh rebuild.</summary>
        public Material Material
        {
            get => material;
            set
            {
                if (material == value) return;
                material = value;
                shaderValidationLogged = false;
                ValidateShader();
                uniText?.SetDirty(UniTextDirtyFlags.Material);
            }
        }

        /// <summary>
        /// Constant value written to TEXCOORD2 of every glyph vertex in this modifier's sub-mesh.
        /// Use this to animate per-text shader parameters (e.g. dissolve progress, hue offset,
        /// flicker phase) without going through <c>Material.Set*</c>, which would affect all texts
        /// sharing the cached runtime clone of the material.
        /// </summary>
        /// <remarks>
        /// Setting triggers a mesh rebuild on the owning UniText so the new UV value reaches the GPU.
        /// XYZW layout is defined by the target shader — see its docs.
        /// </remarks>
        public Vector4 ConstantUv2
        {
            get => constantUv2;
            set
            {
                if (constantUv2 == value) return;
                constantUv2 = value;
                uniText?.SetDirty(UniTextDirtyFlags.Color);
            }
        }

        /// <summary>
        /// Constant value written to TEXCOORD3 of every glyph vertex in this modifier's sub-mesh.
        /// See <see cref="ConstantUv2"/> — same semantics, second channel.
        /// </summary>
        public Vector4 ConstantUv3
        {
            get => constantUv3;
            set
            {
                if (constantUv3 == value) return;
                constantUv3 = value;
                uniText?.SetDirty(UniTextDirtyFlags.Color);
            }
        }

        /// <summary>
        /// When <see langword="true"/> (default), this modifier uses a cached runtime clone of
        /// <see cref="Material"/> — shader property / keyword writes are isolated from the source
        /// asset, batching dedup is preserved, and runtime edits to <c>source.SetColor(...)</c> do
        /// not propagate to already-rendered text. When <see langword="false"/> the source material
        /// is used directly: runtime edits are visible immediately, but the source's keyword state
        /// and <c>_MainTex</c> binding become shared with every other consumer — set keywords via
        /// the Toggle properties in the shader or <c>Material.EnableKeyword</c> manually.
        /// </summary>
        public bool CloneMaterial
        {
            get => cloneMaterial;
            set
            {
                if (cloneMaterial == value) return;
                cloneMaterial = value;
                uniText?.SetDirty(UniTextDirtyFlags.Material);
            }
        }

        private struct EffectRange
        {
            public int start;
            public int end;
            public Color32 tint;
            public bool hasTint;
            public string parameter;
        }

        private PooledBuffer<EffectRange> ranges;

        private bool shaderValid;
        private bool shaderValidationLogged;

        
        private int glyphIndexInSelection;
        private int emittedCountThisFrame;
        private EffectRange currentActiveRange;
        private bool hasCurrentActiveRange;

        private bool writesUv2;
        private bool writesUv3;

        private float activePadding;

        private static readonly int mainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int meshPaddingId = Shader.PropertyToID("_UniTextMeshPadding");

        protected override bool ShouldIncludeCurrentGlyph()
        {
            if (!shaderValid) return false;

            var gen = uniText.MeshGenerator;
            var cluster = gen.currentCluster;
            var count = ranges.count;
            var data = ranges.data;

            for (var i = 0; i < count; i++)
            {
                ref var r = ref data[i];
                if (cluster < r.start || cluster >= r.end) continue;

                var isEmoji = gen.font != null && gen.font.IsColor;
                if (isEmoji && emojiMaterial == null)
                {
                    hasCurrentActiveRange = false;
                    return false;
                }

                currentActiveRange = r;
                hasCurrentActiveRange = true;
                glyphIndexInSelection = emittedCountThisFrame;
                return true;
            }

            hasCurrentActiveRange = false;
            return false;
        }

        protected override Material GetMaterialForSlot(Slot slot)
        {
            if (!shaderValid) return null;

            var gen = uniText.MeshGenerator;

            if (slot == Slot.Emoji)
            {
                if (emojiMaterial == null) return null;
                var emojiAtlas = GlyphAtlas.Emoji;
                return cloneMaterial
                    ? UniTextCustomMaterialCache.Acquire(emojiMaterial, isMsdf: false, isEmoji: true, emojiAtlas)
                    : UniTextCustomMaterialCache.BindSourceDirect(emojiMaterial, emojiAtlas);
            }

            var textAtlas = GlyphAtlas.GetInstance(gen.RenderMode);
            return cloneMaterial
                ? UniTextCustomMaterialCache.Acquire(
                    material,
                    isMsdf: gen.RenderMode == UniTextRenderMode.MSDF,
                    isEmoji: false,
                    textAtlas)
                : UniTextCustomMaterialCache.BindSourceDirect(material, textAtlas);
        }

        protected override RenderOrder GetRenderOrder()
        {
            return renderOrder switch
            {
                MaterialRenderOrder.Under => RenderOrder.Under,
                MaterialRenderOrder.Over => RenderOrder.Over,
                MaterialRenderOrder.Replace => RenderOrder.Base,
                _ => RenderOrder.Over,
            };
        }

        protected override int GetSortIndex() => sortIndex;

        protected override void OnQuadAppended(Slot slot, int vertexStart, UniTextMeshGenerator gen)
        {
            if (activePadding > 0f)
                ExpandSubMeshQuad(slot, vertexStart, activePadding);

            if (hasCurrentActiveRange && currentActiveRange.hasTint)
                TintSubMeshQuad(slot, vertexStart, currentActiveRange.tint);

            if (renderOrder == MaterialRenderOrder.Replace)
                ZeroFaceAlpha(gen);
            else if (hasCurrentActiveRange && currentActiveRange.hasTint)
                TintFaceQuad(gen, currentActiveRange.tint);

            if (writesUv2 || writesUv3)
            {
                Vector4 uv2 = constantUv2;
                Vector4 uv3 = constantUv3;

                if (glyphDataWriter != null || UseVirtualWriter)
                {
                    var ctx = new MaterialGlyphContext(
                        cluster: gen.currentCluster,
                        glyphIndexInSelection: glyphIndexInSelection,
                        cursorX: gen.cursorX,
                        baselineY: gen.baselineY,
                        glyphHeight: gen.height,
                        parameter: hasCurrentActiveRange ? currentActiveRange.parameter : null);

                    if (glyphDataWriter != null)
                        glyphDataWriter(in ctx, out uv2, out uv3);
                    else
                        OnWriteGlyphUV(in ctx, out uv2, out uv3);
                }

                if (writesUv2) SetSubMeshUv2Quad(slot, vertexStart, uv2);
                if (writesUv3) SetSubMeshUv3Quad(slot, vertexStart, uv3);
            }

            emittedCountThisFrame++;
        }

        /// <summary>
        /// Override to write per-glyph data into the sub-mesh's TEXCOORD2 / TEXCOORD3. Called once per
        /// included glyph. Default implementation returns <see cref="constantUv2"/> / <see cref="constantUv3"/>.
        /// </summary>
        /// <remarks>
        /// Ignored when <see cref="glyphDataWriter"/> is set. Marked <c>virtual</c> instead of <c>abstract</c>
        /// so that a basic <see cref="MaterialModifier"/> with only inspector-level configuration is usable
        /// as-is without subclassing.
        /// </remarks>
        protected virtual void OnWriteGlyphUV(in MaterialGlyphContext ctx, out Vector4 uv2, out Vector4 uv3)
        {
            uv2 = constantUv2;
            uv3 = constantUv3;
        }

        /// <summary>When true, <see cref="OnQuadAppended"/> calls <see cref="OnWriteGlyphUV"/> even if
        /// <see cref="glyphDataWriter"/> is null. Set to <see langword="true"/> in subclasses that override
        /// <see cref="OnWriteGlyphUV"/>.</summary>
        protected virtual bool UseVirtualWriter => false;

        protected override void OnEnable()
        {
            ranges.FakeClear();
            base.OnEnable();
        }

        protected override void OnBeforeRebuild()
        {
            emittedCountThisFrame = 0;
            hasCurrentActiveRange = false;

            var useWriter = glyphDataWriter != null || UseVirtualWriter;
            writesUv2 = useWriter || IsNonZero(constantUv2);
            writesUv3 = useWriter || IsNonZero(constantUv3);
        }

        public override void PrepareForParallel()
        {
            base.PrepareForParallel();
            ValidateShader();
            activePadding = ResolveQuadPadding();
        }

        private float ResolveQuadPadding()
        {
            if (quadPaddingOverride >= 0f) return quadPaddingOverride;
            if (material != null && material.HasProperty(meshPaddingId))
                return Mathf.Max(0f, material.GetFloat(meshPaddingId));
            return 0f;
        }

        private static bool IsNonZero(Vector4 v) =>
            v.x != 0f || v.y != 0f || v.z != 0f || v.w != 0f;

        protected override void OnDestroy()
        {
            ranges.Return();
            base.OnDestroy();
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            var range = new EffectRange
            {
                start = start,
                end = end,
                parameter = parameter,
            };

            if (!string.IsNullOrEmpty(parameter))
            {
                var reader = new ParameterReader(parameter);
                if (reader.NextColor(out var tint))
                {
                    range.tint = tint;
                    range.hasTint = true;
                }
            }

            ranges.Add(range);
        }

        private static void ZeroFaceAlpha(UniTextMeshGenerator gen)
        {
            var baseIdx = gen.faceBaseIdx;
            if (baseIdx < 0) return;

            var colors = gen.Colors;
            colors[baseIdx].a     = 0;
            colors[baseIdx + 1].a = 0;
            colors[baseIdx + 2].a = 0;
            colors[baseIdx + 3].a = 0;
        }

        private static void TintFaceQuad(UniTextMeshGenerator gen, Color32 tint)
        {
            var baseIdx = gen.faceBaseIdx;
            if (baseIdx < 0) return;

            MultiplyQuadColor(gen.Colors, baseIdx, tint);
        }

        private void TintSubMeshQuad(Slot slot, int vertexStart, Color32 tint)
        {
            MultiplyQuadColor(GetSlotColors(slot), vertexStart, tint);
        }

        private static void MultiplyQuadColor(Color32[] colors, int baseIdx, Color32 tint)
        {
            for (var i = 0; i < 4; i++)
            {
                ref var c = ref colors[baseIdx + i];
                c.r = (byte)(c.r * tint.r / 255);
                c.g = (byte)(c.g * tint.g / 255);
                c.b = (byte)(c.b * tint.b / 255);
                c.a = (byte)(c.a * tint.a / 255);
            }
        }

        private void ValidateShader()
        {
            shaderValid = false;

            if (material == null)
            {
                if (!shaderValidationLogged)
                {
                    Debug.LogError($"[MaterialModifier] material is not assigned on {uniText?.name}.");
                    shaderValidationLogged = true;
                }
                return;
            }

            if (!material.HasProperty(mainTexId))
            {
                if (!shaderValidationLogged)
                {
                    Debug.LogError(
                        $"[MaterialModifier] Shader '{material.shader?.name}' is missing required property " +
                        "'_MainTex'. Include UniText_Custom.cginc and declare " +
                        "'_MainTex (\"Font Atlas\", 2DArray) = \"\" {}' in your Properties block.");
                    shaderValidationLogged = true;
                }
                return;
            }

            shaderValid = true;
            shaderValidationLogged = false;
        }
    }
}
