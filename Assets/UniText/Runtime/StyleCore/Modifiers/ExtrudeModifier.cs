using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Adds a 3D extrude/bevel effect by appending a stack of progressively offset duplicate
    /// quads behind the face.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Layers are flushed back-to-front in <i>layer-major</i> order across all glyphs (every
    /// glyph's deepest slice first, then every glyph's next-deeper slice, …, then face). Naïve
    /// per-glyph fanout would invert that order at glyph boundaries when the cumulative offset
    /// exceeds glyph advance, leaving farther slices of glyph N+1 covering closer slices of
    /// glyph N. Per-layer buffers preserve the painter order regardless of overlap.
    /// </para>
    /// <para>
    /// Format: <c>&lt;extrude=offsetX,offsetY,#nearColor,#farColor,dilate,softness&gt;</c>.
    /// Defaults: offset = (3,-3), near = white, far = black, dilate = 0, softness = 0.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Appearance", 5)]
    [TypeDescription("Adds a 3D extrude effect behind the text.")]
    [ParameterField(0, "Offset X", "float", "3")]
    [ParameterField(1, "Offset Y", "float", "-3")]
    [ParameterField(2, "Near Color", "color", "#FFFFFFFF")]
    [ParameterField(3, "Far Color", "color", "#000000FF")]
    [ParameterField(4, "Dilate", "float", "0")]
    [ParameterField(5, "Softness", "float", "0")]
    public class ExtrudeModifier : EffectModifier
    {
        private static readonly Color32 defaultNearColor = new(255, 255, 255, 255);
        private static readonly Color32 defaultFarColor = new(0, 0, 0, 255);

        [SerializeField] public int steps = 10;
        [SerializeField] public bool bevel;
        [SerializeField] public bool fixedPixelSize;

        private struct ExtrudeRange
        {
            public int start, end;
            public float dilate, softness;
            public float offsetX, offsetY;
            public Color32 nearColor, farColor;
        }

        private struct LayerRequest
        {
            public int sourceBaseIdx;
            public Vector4 effectUv;
            public float offsetX;
            public float offsetY;
            public float expandDelta;
            public EffectPass pass;
        }

        private PooledBuffer<ExtrudeRange> ranges;
        private PooledBuffer<LayerRequest>[] layers;
        private Action onRebuildStartLayersCallback;
        private int activeSteps;
        private int activeLayerCount;
        private bool isWorldText;
        private Quaternion inverseRotation;

        protected override void OnEnable()
        {
            isWorldText = uniText is UniTextWorld;
            ranges.FakeClear();

            activeSteps = Mathf.Max(1, steps);
            activeLayerCount = bevel ? activeSteps * 2 : activeSteps;
            EnsureLayerBuffers(activeLayerCount);
            for (var i = 0; i < activeLayerCount; i++)
                layers[i].FakeClear();

            onRebuildStartLayersCallback ??= ResetLayerBuffers;
            base.OnEnable();
            uniText.MeshGenerator.onRebuildStart += onRebuildStartLayersCallback;
        }

        protected override void OnDisable()
        {
            uniText.MeshGenerator.onRebuildStart -= onRebuildStartLayersCallback;
            base.OnDisable();
        }

        public override void PrepareForParallel()
        {
            if (isWorldText)
                inverseRotation = Quaternion.Inverse(uniText.transform.rotation);
        }

        protected override void OnDestroy()
        {
            ranges.Return();
            if (layers != null)
            {
                for (var i = 0; i < layers.Length; i++)
                    layers[i].Return();
                layers = null;
            }
            onRebuildStartLayersCallback = null;
            base.OnDestroy();
        }

        protected override void OnApply(int start, int end, string parameter)
        {
            var ox = 3f;
            var oy = -3f;
            var nearColor = defaultNearColor;
            var farColor = defaultFarColor;
            var dilate = 0f;
            var soft = 0f;

            if (!string.IsNullOrEmpty(parameter))
                ParseParameter(parameter, ref ox, ref oy, ref nearColor, ref farColor, ref dilate, ref soft);

            ranges.Add(new ExtrudeRange
            {
                start = start,
                end = end,
                dilate = dilate,
                softness = soft,
                offsetX = ox,
                offsetY = oy,
                nearColor = nearColor,
                farColor = farColor
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
                var expandDelta = delta > 0f ? delta : 0f;

                var s = (float)activeSteps;

                var currentPass = gen.currentEffectPass;

                if (!bevel)
                {
                    for (var layer = 0; layer < activeSteps; layer++)
                    {
                        var t = (activeSteps - layer) / s;
                        var color = Color32.Lerp(range.nearColor, range.farColor, t);
                        var packedColor = EffectPacking.PackColor(color);
                        layers[layer].Add(new LayerRequest
                        {
                            sourceBaseIdx = baseIdx,
                            effectUv = new Vector4(dilate, packedColor.x, packedColor.y, softness),
                            offsetX = meshOffX * t,
                            offsetY = meshOffY * t,
                            expandDelta = expandDelta,
                            pass = currentPass
                        });
                    }
                }
                else
                {
                    var nearPacked = EffectPacking.PackColor(range.nearColor);
                    var farPacked = EffectPacking.PackColor(range.farColor);

                    for (var layer = 0; layer < activeLayerCount; layer++)
                    {
                        var pair = layer / 2;
                        var step = activeSteps - 1 - pair;
                        var isXFace = (layer % 2) == 1;
                        var tMain = (step + 1) / s;
                        var tSide = step / s;

                        float offX, offY;
                        Vector2 packedColor;
                        if (isXFace)
                        {
                            offX = meshOffX * tMain;
                            offY = meshOffY * tSide;
                            packedColor = farPacked;
                        }
                        else
                        {
                            offX = meshOffX * tSide;
                            offY = meshOffY * tMain;
                            packedColor = nearPacked;
                        }

                        layers[layer].Add(new LayerRequest
                        {
                            sourceBaseIdx = baseIdx,
                            effectUv = new Vector4(dilate, packedColor.x, packedColor.y, softness),
                            offsetX = offX,
                            offsetY = offY,
                            expandDelta = expandDelta,
                            pass = currentPass
                        });
                    }
                }

                return;
            }
        }

        protected override void ApplyOwnRequests()
        {
            var gen = uniText.MeshGenerator;
            for (var l = 0; l < activeLayerCount; l++)
            {
                var count = layers[l].count;
                if (count == 0) continue;

                var data = layers[l].data;
                for (var i = 0; i < count; i++)
                {
                    ref var r = ref data[i];
                    var destIdx = AppendSharedEffectQuad(gen, r.sourceBaseIdx, r.effectUv, r.offsetX, r.offsetY, r.pass);
                    if (r.expandDelta > 0f)
                        gen.ExpandQuad(destIdx, r.expandDelta);
                }
            }
        }

        private void ResetLayerBuffers()
        {
            if (layers == null) return;
            for (var i = 0; i < layers.Length; i++)
                layers[i].FakeClear();
        }

        private void EnsureLayerBuffers(int count)
        {
            if (layers != null && layers.Length == count) return;

            if (layers != null)
                for (var i = 0; i < layers.Length; i++)
                    layers[i].Return();

            layers = new PooledBuffer<LayerRequest>[count];
        }

        private static void ParseParameter(ReadOnlySpan<char> param, ref float ox, ref float oy,
            ref Color32 nearColor, ref Color32 farColor, ref float dilate, ref float soft)
        {
            var reader = new ParameterReader(param);
            var numIdx = 0;
            var colorIdx = 0;

            while (reader.Next(out var token))
            {
                if (token.IsEmpty) continue;

                if (ColorParsing.TryParse(token, out var c))
                {
                    switch (colorIdx)
                    {
                        case 0: nearColor = c; break;
                        case 1: farColor = c; break;
                    }
                    colorIdx++;
                }
                else if (ParameterReader.ParseFloat(token, out var f))
                {
                    switch (numIdx)
                    {
                        case 0: ox = f; break;
                        case 1: oy = f; break;
                        case 2: dilate = f; break;
                        case 3: soft = f; break;
                    }
                    numIdx++;
                }
            }
        }
    }
}
