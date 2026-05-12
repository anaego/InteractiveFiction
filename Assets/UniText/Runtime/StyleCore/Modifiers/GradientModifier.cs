using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies gradient coloring to text ranges using named gradients.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parameter format: <c>name[,shape][,angle]</c>
    /// <list type="bullet">
    /// <item><c>rainbow</c> — linear gradient, angle 0</item>
    /// <item><c>rainbow,linear,45</c> — linear at 45°</item>
    /// <item><c>rainbow,radial</c> — radial gradient from center</item>
    /// <item><c>rainbow,angular</c> — angular (conic) sweep from top</item>
    /// <item><c>rainbow,angular,90</c> — angular sweep rotated 90°</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Shape</b> controls the gradient form: <c>linear</c> (projection onto an axis), <c>radial</c> (distance from center), or <c>angular</c> (conic sweep).
    /// </para>
    /// <para>
    /// The set of named gradients available to <c>&lt;gradient=name&gt;</c> tags is supplied by
    /// an <see cref="IGradientProvider"/>. The default provider resolves names through the
    /// project-wide <see cref="UniTextSettings.Gradients"/> asset; alternative providers allow
    /// per-component asset references or inline catalogs edited on the modifier itself.
    /// </para>
    /// </remarks>
    /// <seealso cref="IGradientProvider"/>
    /// <seealso cref="UniTextGradients"/>
    /// <seealso cref="IParseRule"/>
    [Serializable]
    [TypeGroup("Appearance", 2)]
    [TypeDescription("Applies a gradient color effect to the text.")]
    [ParameterField(0, "Name", "enum:@gradients")]
    [ParameterField(1, "Shape", "enum:linear|radial|angular", "linear")]
    [ParameterField(2, "Angle", "float(0,360)", "0")]
    public sealed class GradientModifier : BaseModifier
    {
        [SerializeReference, TypeSelector]
        [Tooltip("Source of named gradients for <gradient=name> tags handled by this modifier.")]
        private IGradientProvider provider = new GlobalSettingsGradientProvider();

        /// <summary>
        /// Gets or sets the gradient source used by this modifier. <see langword="null"/> disables
        /// resolution and causes <c>&lt;gradient&gt;</c> tags to be skipped with a warning.
        /// </summary>
        public IGradientProvider Provider
        {
            get => provider;
            set => provider = value;
        }

        internal enum GradientShape : byte
        {
            Linear,
            Radial,
            Angular
        }

        internal struct GradientDef
        {
            public int startCluster;
            public int endCluster;
            public Gradient gradient;
            public float angleDeg;
            public float minProj;
            public float maxProj;
            public float cosAngle;
            public float sinAngle;
            public float centerX;
            public float centerY;
            public float radius;
            public GradientShape shape;
        }

        private GradientAttributeData shared;

        protected override void OnEnable()
        {
            shared = buffers.GetOrCreateAttributeData<GradientAttributeData>(AttributeKeys.Gradient);
            shared.Acquire(uniText);
            shared.EnsureForCycle(buffers.codepoints.count);

            GradientNotifier.AnyChanged += OnGradientSourceChanged;
        }

        protected override void OnDisable()
        {
            GradientNotifier.AnyChanged -= OnGradientSourceChanged;

            if (shared != null)
            {
                shared.ReleaseRef();
                shared = null;
            }
        }

        private void OnGradientSourceChanged()
        {
            uniText?.SetDirty(UniTextDirtyFlags.Color);
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            if (string.IsNullOrEmpty(parameter))
                return;

            if (!TryParse(parameter, out var gradientName, out var angle, out var shape))
                return;

            if (provider == null)
            {
                Debug.LogWarning("[GradientModifier] No gradient provider assigned");
                return;
            }

            if (!provider.TryGetGradient(gradientName, out var gradient))
            {
                Debug.LogWarning($"[GradientModifier] Gradient '{gradientName}' not found");
                return;
            }

            var defIndex = shared.AddDef(new GradientDef
            {
                startCluster = start,
                endCluster = end,
                gradient = gradient,
                angleDeg = angle,
                shape = shape
            });

            if (defIndex == 0)
                return;

            var buffer = shared.indexBuffer.buffer.data;
            var cpCount = buffers.codepoints.count;
            var actualEnd = Math.Min(end, cpCount);
            var byteIndex = (byte)defIndex;
            for (var i = start; i < actualEnd; i++)
                buffer[i] = byteIndex;
        }

        private static bool TryParse(ReadOnlySpan<char> param, out string name, out float angle,
            out GradientShape shape)
        {
            name = null;
            angle = 0f;
            shape = GradientShape.Linear;

            var reader = new ParameterReader(param);
            if (!reader.NextString(out name))
                return false;

            if (reader.Next(out var shapeToken) && !shapeToken.IsEmpty)
            {
                if (shapeToken.Equals("radial".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    shape = GradientShape.Radial;
                else if (shapeToken.Equals("angular".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    shape = GradientShape.Angular;
            }

            reader.NextFloat(out angle);

            return true;
        }
    }

    /// <summary>
    /// Per-<see cref="UniTextBase"/> gradient state shared by all <see cref="GradientModifier"/>
    /// instances on the same component. Owns the cluster-index byte buffer, the global gradient
    /// definition list, and the single <c>onGlyph</c> / <c>LayoutComplete</c> subscription pair.
    /// </summary>
    /// <remarks>
    /// Two design points worth keeping in mind:
    /// <list type="bullet">
    /// <item>The byte buffer stores 1-based indices into the shared <see cref="defs"/> list, so
    /// modifiers cooperate on a single global index space — multiple modifiers on the same
    /// <see cref="UniTextBase"/> never collide on the index <c>1</c> as they did when each
    /// instance maintained its own definitions list.</item>
    /// <item>Only one delegate handles <c>onGlyph</c> per component regardless of how many
    /// <see cref="GradientModifier"/> instances are installed; per-instance work is limited to
    /// registration, which keeps the per-glyph cost flat as styles are added.</item>
    /// </list>
    /// </remarks>
    internal sealed class GradientAttributeData : IAttributeData
    {
        public readonly PooledArrayAttribute<byte> indexBuffer = new();
        public readonly PooledList<GradientModifier.GradientDef> defs = new();
        private readonly PooledList<Rect> boundsCache = new();

        /// <summary>
        /// Number of <see cref="GradientModifier"/> instances currently keeping this shared state
        /// alive. Direct references to <see cref="TextProcessor"/> / <see cref="UniTextMeshGenerator"/>
        /// are captured at the 0→1 transition because <see cref="UniTextBase.DeInit"/> nulls those
        /// fields before <see cref="UniTextBuffers.ReleaseAllAttributeData"/> runs — reading
        /// <c>uniText.TextProcessor</c> from <see cref="Release"/> would NRE.
        /// </summary>
        private int activeCount;

        private UniTextBase uniText;
        private TextProcessor textProcessor;
        private UniTextMeshGenerator meshGenerator;

        public void Acquire(UniTextBase ut)
        {
            if (activeCount++ != 0) return;

            uniText = ut;
            textProcessor = ut.TextProcessor;
            meshGenerator = ut.MeshGenerator;
            textProcessor.LayoutComplete += OnLayoutComplete;
            meshGenerator.onGlyph += OnGlyph;
        }

        public void ReleaseRef()
        {
            if (activeCount == 0) return;
            if (--activeCount != 0) return;

            Unsubscribe();
            indexBuffer.EnsureCountAndClear(0);
            defs.Clear();
        }

        private void Unsubscribe()
        {
            if (textProcessor != null)
            {
                textProcessor.LayoutComplete -= OnLayoutComplete;
                textProcessor = null;
            }
            if (meshGenerator != null)
            {
                meshGenerator.onGlyph -= OnGlyph;
                meshGenerator = null;
            }
            uniText = null;
        }

        public void EnsureForCycle(int codepointCount)
        {
            indexBuffer.EnsureCountAndClear(codepointCount);
            defs.Clear();
        }

        /// <summary>
        /// Registers a gradient definition and returns its 1-based index for storage in
        /// <see cref="indexBuffer"/>. Returns <c>0</c> when the per-cycle capacity (255 ranges,
        /// limited by the byte index) is exhausted; callers must skip writing the buffer in that case.
        /// </summary>
        public int AddDef(GradientModifier.GradientDef def)
        {
            if (defs.Count >= 255)
            {
                Debug.LogWarning("[GradientModifier] More than 255 gradient ranges in one text cycle; extra ranges will not be colored.");
                return 0;
            }
            defs.Add(def);
            return defs.Count;
        }

        private void OnLayoutComplete()
        {
            if (defs.Count == 0) return;

            for (var i = 0; i < defs.Count; i++)
            {
                ref var g = ref defs[i];

                uniText.GetRangeBounds(g.startCluster, g.endCluster, boundsCache);
                if (boundsCache.Count == 0) continue;

                if (g.shape == GradientModifier.GradientShape.Linear)
                {
                    var rad = g.angleDeg * Mathf.Deg2Rad;
                    g.cosAngle = Mathf.Cos(rad);
                    g.sinAngle = Mathf.Sin(rad);

                    g.minProj = float.MaxValue;
                    g.maxProj = float.MinValue;

                    for (var j = 0; j < boundsCache.Count; j++)
                    {
                        ref readonly var rect = ref boundsCache[j];
                        UpdateProj(rect.xMin, rect.yMin, ref g);
                        UpdateProj(rect.xMax, rect.yMin, ref g);
                        UpdateProj(rect.xMin, rect.yMax, ref g);
                        UpdateProj(rect.xMax, rect.yMax, ref g);
                    }
                }
                else
                {
                    var minX = float.MaxValue;
                    var maxX = float.MinValue;
                    var minY = float.MaxValue;
                    var maxY = float.MinValue;

                    for (var j = 0; j < boundsCache.Count; j++)
                    {
                        ref readonly var rect = ref boundsCache[j];
                        if (rect.xMin < minX) minX = rect.xMin;
                        if (rect.xMax > maxX) maxX = rect.xMax;
                        if (rect.yMin < minY) minY = rect.yMin;
                        if (rect.yMax > maxY) maxY = rect.yMax;
                    }

                    g.centerX = (minX + maxX) * 0.5f;
                    g.centerY = (minY + maxY) * 0.5f;

                    var dx = (maxX - minX) * 0.5f;
                    var dy = (maxY - minY) * 0.5f;
                    g.radius = Mathf.Sqrt(dx * dx + dy * dy);
                }
            }
        }

        private static void UpdateProj(float x, float y, ref GradientModifier.GradientDef g)
        {
            var proj = x * g.cosAngle + y * g.sinAngle;
            if (proj < g.minProj) g.minProj = proj;
            if (proj > g.maxProj) g.maxProj = proj;
        }

        private void OnGlyph()
        {
            if (defs.Count == 0) return;

            var gen = uniText.MeshGenerator;
            if (gen.font.IsColor) return;

            var buffer = indexBuffer.buffer.data;
            var cluster = gen.currentCluster;

            var gradientIndex = buffer[cluster];
            if (gradientIndex == 0) return;

            ref readonly var g = ref defs[gradientIndex - 1];

            var baseIdx = gen.faceBaseIdx;
            var colors = gen.Colors;
            var defaultAlpha = gen.defaultColor.a;

            if (g.shape == GradientModifier.GradientShape.Radial)
            {
                if (g.radius <= 0) return;

                var verts = gen.Vertices;
                var invRadius = 1f / g.radius;

                for (var i = 0; i < 4; i++)
                {
                    ref readonly var v = ref verts[baseIdx + i];
                    var dx = v.x - g.centerX;
                    var dy = v.y - g.centerY;
                    var t = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) * invRadius);

                    var color = g.gradient.Evaluate(t);
                    colors[baseIdx + i] = new Color32(
                        (byte)(color.r * 255),
                        (byte)(color.g * 255),
                        (byte)(color.b * 255),
                        (byte)(color.a * defaultAlpha)
                    );
                }
            }
            else if (g.shape == GradientModifier.GradientShape.Angular)
            {
                var verts = gen.Vertices;
                var angleOffset = g.angleDeg * Mathf.Deg2Rad;
                var invTwoPi = 1f / (Mathf.PI * 2f);

                for (var i = 0; i < 4; i++)
                {
                    ref readonly var v = ref verts[baseIdx + i];
                    var dx = v.x - g.centerX;
                    var dy = v.y - g.centerY;
                    var a = Mathf.Atan2(dx, dy) + angleOffset;
                    var t = a * invTwoPi + 0.5f;
                    t -= Mathf.Floor(t);

                    var color = g.gradient.Evaluate(t);
                    colors[baseIdx + i] = new Color32(
                        (byte)(color.r * 255),
                        (byte)(color.g * 255),
                        (byte)(color.b * 255),
                        (byte)(color.a * defaultAlpha)
                    );
                }
            }
            else
            {
                var range = g.maxProj - g.minProj;
                if (range <= 0) return;

                var verts = gen.Vertices;

                for (var i = 0; i < 4; i++)
                {
                    ref readonly var v = ref verts[baseIdx + i];
                    var proj = v.x * g.cosAngle + v.y * g.sinAngle;
                    var t = Mathf.Clamp01((proj - g.minProj) / range);

                    var color = g.gradient.Evaluate(t);
                    colors[baseIdx + i] = new Color32(
                        (byte)(color.r * 255),
                        (byte)(color.g * 255),
                        (byte)(color.b * 255),
                        (byte)(color.a * defaultAlpha)
                    );
                }
            }
        }

        public void Release()
        {
            Unsubscribe();
            activeCount = 0;
            indexBuffer.Release();
            defs.Return();
            boundsCache.Return();
        }
    }
}
