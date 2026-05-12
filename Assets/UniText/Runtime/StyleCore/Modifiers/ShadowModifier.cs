using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies a shadow effect by appending displaced duplicate glyph geometry behind the face.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shadow is rendered via a duplicate quad offset from the face and tagged with effect
    /// UV data. All shadow modifiers render in the same CanvasRenderer as the face (back-first
    /// in the index buffer), enabling unlimited stacking with no extra submeshes.
    /// </para>
    /// <para>
    /// All parameters come from the tag/rule parameter string.
    /// Format: <c>&lt;shadow=#color&gt;</c>, <c>&lt;shadow=dilate,#color&gt;</c>,
    /// or <c>&lt;shadow=dilate,#color,offsetX,offsetY,softness&gt;</c>.
    /// Defaults: dilate = 0, color = black 50% (#00000080), offset = (0.1,-0.1), softness = 0.1.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Appearance", 4)]
    [TypeDescription("Adds a shadow effect behind the text.")]
    [ParameterField(0, "Dilate", "float", "0.1")]
    [ParameterField(1, "Color", "color", "#00000080")]
    [ParameterField(2, "Offset X", "float", "0.1")]
    [ParameterField(3, "Offset Y", "float", "-0.1")]
    [ParameterField(4, "Softness", "float", "0.1")]
    public class ShadowModifier : EffectModifier
    {
        private static readonly Color32 defaultColor = new(0, 0, 0, 128);

        /// <summary>When true, dilate/offset/softness are in fixed pixels and compensated by gradientScale per glyph.</summary>
        [SerializeField] public bool fixedPixelSize;

        private struct EffectRange
        {
            public int start;
            public int end;
            public float dilate;
            public Vector2 packedColor;
            public float offsetX;
            public float offsetY;
            public float softness;
        }

        private PooledBuffer<EffectRange> ranges;
        private bool isWorldText;
        private Quaternion inverseRotation;

        protected override void OnEnable()
        {
            isWorldText = uniText is UniTextWorld;
            ranges.FakeClear();
            base.OnEnable();
        }

        public override void PrepareForParallel()
        {
            if (isWorldText)
                inverseRotation = Quaternion.Inverse(uniText.transform.rotation);
        }

        protected override void OnDestroy()
        {
            ranges.Return();
            base.OnDestroy();
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            var dilate = 0.1f;
            var color = defaultColor;
            var ox = 0.1f;
            var oy = -0.1f;
            var soft = 0.1f;

            if (!string.IsNullOrEmpty(parameter))
                ParseParameter(parameter, ref dilate, ref color, ref ox, ref oy, ref soft);

            ranges.Add(new EffectRange
            {
                start = start,
                end = end,
                dilate = dilate,
                packedColor = EffectPacking.PackColor(color),
                offsetX = ox,
                offsetY = oy,
                softness = soft
            });
        }

        protected override void OnGlyphEffect()
        {
            var gen = uniText.MeshGenerator;
            if (gen.font.IsColor) return;

            var cluster = gen.currentCluster;
            var count = ranges.count;
            var data = ranges.data;

            for (var i = 0; i < count; i++)
            {
                ref var range = ref data[i];
                if (cluster < range.start || cluster >= range.end) continue;

                var baseIdx = gen.faceBaseIdx;
                var glyphH = gen.Uvs0[baseIdx].w;
                if (glyphH < 1e-6f) return;

                var faceDilate = gen.Uvs1[baseIdx].y;
                var padGlyph = GlyphAtlas.Pad / glyphH;

                float dilate, softness, meshOffX, meshOffY;

                if (fixedPixelSize)
                {
                    var mf = gen.fontMetricFactor;
                    dilate = range.dilate / (GlyphAtlas.Pad * mf);
                    softness = range.softness / mf;
                    var of = mf / gen.height;
                    meshOffX = range.offsetX * of;
                    meshOffY = range.offsetY * of;
                }
                else
                {
                    dilate = range.dilate;
                    softness = range.softness;
                    meshOffX = range.offsetX * gen.fontMetricFactor;
                    meshOffY = range.offsetY * gen.fontMetricFactor;
                }

                if (isWorldText)
                {
                    var localDir = inverseRotation * new Vector3(meshOffX, meshOffY, 0f);
                    meshOffX = localDir.x;
                    meshOffY = localDir.y;
                }

                var extent = (faceDilate + dilate) * padGlyph + softness / glyphH;
                var effectiveExtent = extent < padGlyph ? extent : padGlyph;
                if (effectiveExtent > gen.currentMaxGlyphExtent)
                    gen.currentMaxGlyphExtent = effectiveExtent;

                var currentPad = UniTextMeshGenerator.DefaultSdfPadding;
                var facePad = faceDilate * padGlyph;
                if (facePad > currentPad) currentPad = facePad;
                var delta = effectiveExtent - currentPad;

                EnqueueEffectQuad(
                    baseIdx,
                    new Vector4(dilate, range.packedColor.x, range.packedColor.y, softness),
                    meshOffX, meshOffY,
                    expandDelta: delta > 0f ? delta : 0f);
                return;
            }
        }

        private static void ParseParameter(ReadOnlySpan<char> param, ref float dilate, ref Color32 color,
            ref float ox, ref float oy, ref float soft)
        {
            var reader = new ParameterReader(param);
            var numIdx = 0;

            while (reader.Next(out var token))
            {
                if (token.IsEmpty) continue;

                if (ColorParsing.TryParse(token, out var c))
                {
                    color = c;
                }
                else if (ParameterReader.ParseFloat(token, out var f))
                {
                    switch (numIdx)
                    {
                        case 0: dilate = f; break;
                        case 1: ox = f; break;
                        case 2: oy = f; break;
                        case 3: soft = f; break;
                    }
                    numIdx++;
                }
            }
        }
    }
}
