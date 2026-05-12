using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Visual style of decoration lines (CSS Text Decoration Module Level 4: text-decoration-style).
    /// </summary>
    public enum LineStyle : byte
    {
        Solid = 0,
        Double = 1,
        Dotted = 2,
        Dashed = 3,
        Wavy = 4,
    }

    /// <summary>
    /// Base class for modifiers that render horizontal lines across text (underline, strikethrough).
    /// </summary>
    /// <remarks>
    /// Subclasses define the vertical offset of the line relative to the baseline. Lines break
    /// across wrapped text lines automatically. Decoration quads run through the standard
    /// <see cref="UniTextMeshGenerator.onGlyph"/> pipeline with
    /// <see cref="UniTextMeshGenerator.isVirtualGlyph"/> set, so per-glyph modifiers (color,
    /// gradient, outline, shadow) apply uniformly to face glyphs and decoration lines.
    /// </remarks>
    /// <seealso cref="UnderlineModifier"/>
    /// <seealso cref="StrikethroughModifier"/>
    [Serializable]
    public abstract class BaseLineModifier : BaseModifier
    {
        /// <summary>
        /// Per-range visual parameters for a single decoration tag. Stored once per tag in <see cref="paramsList"/>;
        /// the per-codepoint <see cref="flagsAttribute"/> byte holds <c>(index + 1)</c> so codepoints
        /// outside any tag stay at 0.
        /// </summary>
        /// <remarks>
        /// <c>thicknessPx</c> / <c>offsetPx</c>: <c>NaN</c> means "use font's metric (auto)".
        /// </remarks>
        protected struct LineParams
        {
            public float thicknessPx;
            public float offsetPx;
            public LineStyle style;
            public bool skipInk;
            public bool overlay;
        }

        protected struct LineSegment
        {
            public float startX;
            public float endX;
            public float baselineY;
            public long varHash48;
            public int cluster;
            public float uvLeft;
            public float uvRight;
            public byte paramIndex;
            /// <summary>X origin of the pattern rhythm for this stripe (mark <c>k</c> starts at
            /// <c>patternStartX + k * step</c>); negative for non-pattern segments.</summary>
            public float patternStartX;
        }

        protected PooledArrayAttribute<byte> flagsAttribute;
        protected PooledList<LineParams> paramsList;

        private LineSegment[] lineSegments;
        private int lineSegmentsCapacity;
        private int lineSegmentCount;

        private bool segmentsComputed;
        private float underscoreScale;
        private float cachedGlyphHeightLocal;
        private UniTextFont cachedUnderscoreFont;


        protected abstract string AttributeKey { get; }

        protected abstract float GetLineOffset(FaceInfo faceInfo, float scale);

        protected sealed override void OnEnable()
        {
            buffers.PrepareAttribute(ref flagsAttribute, AttributeKey);

            paramsList ??= new PooledList<LineParams>(8);
            paramsList.FakeClear();

            if (lineSegments == null)
            {
                lineSegments = UniTextArrayPool<LineSegment>.Rent(64);
                lineSegmentsCapacity = 64;
            }
            lineSegmentCount = 0;
            segmentsComputed = false;

            uniText.Rebuilding += OnRebuilding;
            uniText.MeshGenerator.onMainPassComplete += OnMainPassComplete;
        }

        protected sealed override void OnDisable()
        {
            uniText.Rebuilding -= OnRebuilding;
            uniText.MeshGenerator.onMainPassComplete -= OnMainPassComplete;
        }

        protected sealed override void OnDestroy()
        {
            buffers?.ReleaseAttributeData(AttributeKey);
            flagsAttribute = null;

            paramsList?.Return();
            paramsList = null;

            rangeEntriesScratch?.Return();
            rangeEntriesScratch = null;

            if (lineSegments != null)
            {
                UniTextArrayPool<LineSegment>.Return(lineSegments);
                lineSegments = null;
            }
        }

        protected sealed override void OnApply(int start, int end, string parameter)
        {
            var lineParams = ParseLineParams(parameter);

            if (paramsList.Count >= 255)
            {
                paramsList[254] = lineParams;
            }
            else
            {
                paramsList.Add(lineParams);
            }
            var paramIndex = (byte)Math.Min(paramsList.Count, 255);

            var cpCount = buffers.codepoints.count;
            var clampedEnd = Math.Min(end, cpCount);
            var buffer = flagsAttribute.buffer.data;
            for (var i = start; i < clampedEnd; i++)
                buffer[i] = paramIndex;

            buffers.virtualCodepoints.Add('_');
            if (lineParams.style == LineStyle.Dotted)
                buffers.virtualCodepoints.Add('•');
        }

        private LineParams ParseLineParams(string parameter)
        {
            var p = new LineParams
            {
                thicknessPx = float.NaN,
                offsetPx = float.NaN,
                style = LineStyle.Solid,
                skipInk = false,
                overlay = false,
            };

            if (string.IsNullOrEmpty(parameter))
                return p;

            var reader = new ParameterReader(parameter);
            var baseSize = buffers.shapingFontSize > 0 ? buffers.shapingFontSize : uniText.FontSize;

            if (reader.NextUnitFloat(out var thickness, out var thicknessUnit))
            {
                var pixels = thicknessUnit == ParameterReader.UnitKind.Em ? thickness * baseSize : thickness;
                if (pixels > 0f) p.thicknessPx = pixels;
            }

            if (reader.NextUnitFloat(out var offset, out var offsetUnit))
            {
                var pixels = offsetUnit == ParameterReader.UnitKind.Em ? offset * baseSize : offset;
                if (pixels != 0f) p.offsetPx = pixels;
            }

            if (reader.Next(out var styleToken) && !styleToken.IsEmpty)
                p.style = ParseStyle(styleToken);

            if (reader.Next(out var skipInkToken) && !skipInkToken.IsEmpty)
                p.skipInk = ParseBool(skipInkToken, defaultValue: false);

            if (reader.Next(out var overlayToken) && !overlayToken.IsEmpty)
                p.overlay = ParseBool(overlayToken, defaultValue: false);

            return p;
        }

        private static LineStyle ParseStyle(ReadOnlySpan<char> token)
        {
            if (Equals(token, "double")) return LineStyle.Double;
            if (Equals(token, "dotted")) return LineStyle.Dotted;
            if (Equals(token, "dashed")) return LineStyle.Dashed;
            if (Equals(token, "wavy")) return LineStyle.Wavy;
            return LineStyle.Solid;
        }

        private static bool ParseBool(ReadOnlySpan<char> token, bool defaultValue)
        {
            if (Equals(token, "true") || Equals(token, "1") || Equals(token, "yes") || Equals(token, "on"))
                return true;
            if (Equals(token, "false") || Equals(token, "0") || Equals(token, "no") || Equals(token, "off"))
                return false;
            return defaultValue;
        }

        private static bool Equals(ReadOnlySpan<char> a, string b)
        {
            if (a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
            {
                var ca = a[i];
                var cb = b[i];
                if (ca >= 'A' && ca <= 'Z') ca = (char)(ca + 32);
                if (cb >= 'A' && cb <= 'Z') cb = (char)(cb + 32);
                if (ca != cb) return false;
            }
            return true;
        }

        private void OnRebuilding()
        {
            flagsAttribute = buffers.GetAttributeData<PooledArrayAttribute<byte>>(AttributeKey);
            segmentsComputed = false;
        }

        private void AddSegment(float startX, float endX, float baselineY, long varHash48, int cluster, float uvLeft, float uvRight, byte paramIndex, float patternStartX = -1f)
        {
            UniTextArrayPool<LineSegment>.GrowDouble(ref lineSegments, ref lineSegmentsCapacity, lineSegmentCount);

            lineSegments[lineSegmentCount] = new LineSegment
            {
                startX = startX,
                endX = endX,
                baselineY = baselineY,
                varHash48 = varHash48,
                cluster = cluster,
                uvLeft = uvLeft,
                uvRight = uvRight,
                paramIndex = paramIndex,
                patternStartX = patternStartX
            };
            lineSegmentCount++;
        }

        private void OnMainPassComplete()
        {
            var gen = uniText.MeshGenerator;
            if (gen == null) return;

            if (!segmentsComputed)
            {
                ComputeLineSegments(gen);
                segmentsComputed = true;
            }

            if (lineSegmentCount == 0) return;

            if (gen.postFaceInsertPoint < 0)
                gen.postFaceInsertPoint = gen.triangleCount;

            var fontProvider = uniText.FontProvider;
            var faceInfo = cachedUnderscoreFont.FaceInfo;
            var fontLineOffset = GetLineOffset(faceInfo, underscoreScale);
            var autoLineThickness = cachedGlyphHeightLocal > 0f ? cachedGlyphHeightLocal : gen.FontSize * 0.05f;
            for (var i = 0; i < lineSegmentCount; i++)
            {
                ref var seg = ref lineSegments[i];

                var thicknessOverride = float.NaN;
                var lineOffset = fontLineOffset;
                var style = LineStyle.Solid;
                var overlay = false;
                if (seg.paramIndex > 0 && seg.paramIndex - 1 < paramsList.Count)
                {
                    var p = paramsList[seg.paramIndex - 1];
                    if (!float.IsNaN(p.thicknessPx))
                        thicknessOverride = p.thicknessPx;
                    if (!float.IsNaN(p.offsetPx)) lineOffset = fontLineOffset + p.offsetPx;
                    style = p.style;
                    overlay = p.overlay;
                }

                var resolvedThickness = float.IsNaN(thicknessOverride) ? autoLineThickness : thicknessOverride;

                if (overlay) gen.currentEffectPass = EffectPass.PostFace;
                RenderSegmentForStyle(gen, fontProvider, ref seg, lineOffset, resolvedThickness, style);
                if (overlay) gen.currentEffectPass = EffectPass.PreFace;
            }
        }

        private static void RenderSegmentForStyle(UniTextMeshGenerator gen, UniTextFontProvider fontProvider,
            ref LineSegment seg, float lineOffset, float thickness, LineStyle style)
        {
            switch (style)
            {
                case LineStyle.Double:
                    RenderDouble(gen, fontProvider, ref seg, lineOffset, thickness);
                    break;
                case LineStyle.Dotted:
                    RenderPattern(gen, fontProvider, ref seg, lineOffset, thickness, dotMode: true);
                    break;
                case LineStyle.Dashed:
                    RenderPattern(gen, fontProvider, ref seg, lineOffset, thickness, dotMode: false);
                    break;
                case LineStyle.Wavy:
                case LineStyle.Solid:
                default:
                    LineRenderHelper.DrawLine(gen, fontProvider, seg.startX, seg.endX, seg.baselineY, lineOffset,
                        seg.cluster, seg.varHash48, seg.uvLeft, seg.uvRight, thickness);
                    break;
            }
        }

        /// <summary>
        /// Draws two parallel solid lines. Each sub-line has full thickness <paramref name="perLineThickness"/>;
        /// top center stays at <paramref name="lineOffset"/> (default single-line position) and the bottom
        /// drops below by <c>2 * perLineThickness</c>, leaving a visible gap equal to per-line thickness.
        /// </summary>
        private static void RenderDouble(UniTextMeshGenerator gen, UniTextFontProvider fontProvider,
            ref LineSegment seg, float lineOffset, float perLineThickness)
        {
            var topOffset = lineOffset;
            var bottomOffset = lineOffset - perLineThickness * 2f;

            LineRenderHelper.DrawLine(gen, fontProvider, seg.startX, seg.endX, seg.baselineY, topOffset,
                seg.cluster, seg.varHash48, seg.uvLeft, seg.uvRight, perLineThickness);
            LineRenderHelper.DrawLine(gen, fontProvider, seg.startX, seg.endX, seg.baselineY, bottomOffset,
                seg.cluster, seg.varHash48, seg.uvLeft, seg.uvRight, perLineThickness);
        }

        /// <summary>
        /// Draws repeating short marks along the segment. <paramref name="dotMode"/> = true emits
        /// near-square dots; false emits longer dashes. Pattern length scales with thickness so the
        /// rhythm stays proportional across font sizes.
        /// </summary>
        private static void RenderPattern(UniTextMeshGenerator gen, UniTextFontProvider fontProvider,
            ref LineSegment seg, float lineOffset, float thickness, bool dotMode)
        {
            var lineThickness = Math.Max(thickness, 1f);
            var markLen = Math.Max(dotMode ? lineThickness * 2.0f : lineThickness * 3.0f, 1f);
            var step = Math.Max(lineThickness * 4.5f, markLen + 1f);
            if (seg.endX <= seg.startX) return;
            var rhythmStartX = seg.patternStartX;
            var rel = seg.startX - rhythmStartX;
            var k = rel <= 0f ? 0 : (int)Math.Ceiling(rel / step);
            var x = rhythmStartX + k * step;

            while (x + markLen <= seg.endX)
            {
                if (dotMode)
                {
                    LineRenderHelper.DrawDot(gen, fontProvider, x + markLen * 0.5f, seg.baselineY, lineOffset,
                        seg.cluster, markLen);
                }
                else
                {
                    LineRenderHelper.DrawLine(gen, fontProvider, x, x + markLen, seg.baselineY, lineOffset,
                        seg.cluster, seg.varHash48, seg.uvLeft, seg.uvRight, thickness);
                }
                x += step;
            }
        }

        private PooledList<LineRangeEntry> rangeEntriesScratch;

        private void ComputeLineSegments(UniTextMeshGenerator gen)
        {
            lineSegmentCount = 0;

            var fontProvider = uniText.FontProvider;
            cachedUnderscoreFont = fontProvider.GetFontAsset(fontProvider.FindFontForCodepoint('_'));
            underscoreScale = gen.FontSize * cachedUnderscoreFont.FontScale / cachedUnderscoreFont.UnitsPerEm;

            var underscoreGlyphIndex = cachedUnderscoreFont.GetGlyphIndexForUnicode('_');
            if (underscoreGlyphIndex == 0) return;

            var flagsBuffer = flagsAttribute?.buffer.data;
            if (flagsBuffer == null || !flagsBuffer.HasAnyFlags()) return;

            var allGlyphs = buffers.positionedGlyphs.data;
            if (buffers.positionedGlyphs.count == 0) return;
            if (buffers.lines.count == 0) return;

            var defaultVarHash = cachedUnderscoreFont.DefaultVarHash48;
            var underscoreFontHash = cachedUnderscoreFont.FontDataHash;
            var glyphLookup = cachedUnderscoreFont.GlyphLookupTable;

            const float sdfPadding = UniTextMeshGenerator.DefaultSdfPadding;
            var aspect = 1f;
            var glyphHeightLocal = gen.FontSize * 0.05f;
            if (glyphLookup != null &&
                glyphLookup.TryGetValue(UniTextFont.GlyphKey(defaultVarHash, underscoreGlyphIndex), out var underscoreData) &&
                underscoreData.metrics.height > 0)
            {
                aspect = underscoreData.metrics.width / underscoreData.metrics.height;
                glyphHeightLocal = underscoreData.metrics.height * underscoreScale;
            }
            const float capFraction = 0.2f;
            var centerX = aspect * 0.5f;
            var capLeftEnd = aspect * capFraction;
            var capRightStart = aspect * (1f - capFraction);
            var uvRightCap = aspect + sdfPadding;
            var capWidthPerThickness = capLeftEnd + sdfPadding;
            cachedGlyphHeightLocal = glyphHeightLocal;
            var autoLineThickness = glyphHeightLocal;
            var skipInkThreshold = -GetLineOffset(cachedUnderscoreFont.FaceInfo, underscoreScale) - glyphHeightLocal * 0.5f;

            var offsetX = gen.offsetX;
            var offsetY = gen.offsetY;
            var fontSize = gen.FontSize;

            bool IsSkipInk(int idx)
            {
                ref readonly var g = ref allGlyphs[idx];
                var glyphFont = fontProvider.GetFontAsset(g.fontId);
                if (glyphFont == null) return false;
                var varHash = (buffers.variationMap != null && buffers.variationMap.TryGetValue(g.fontId, out var vi))
                    ? vi.varHash48 : glyphFont.DefaultVarHash48;
                if (!gen.TryGetCachedGlyphEntry(GlyphAtlas.MakeKey(varHash, (uint)g.glyphId), out var entry))
                    return false;
                var glyphScale = fontSize * glyphFont.FontScale / glyphFont.UnitsPerEm;
                var descentPx = (entry.metrics.height - entry.metrics.horizontalBearingY) * glyphScale;
                return descentPx > skipInkThreshold;
            }

            rangeEntriesScratch ??= new PooledList<LineRangeEntry>(8);

            var flagsLength = flagsBuffer.Length;
            var lastPatternLineIdx = -1;
            var lastPatternStartX = 0f;

            var c = 0;
            while (c < flagsLength)
            {
                var p = flagsBuffer[c];
                if (p == 0) { c++; continue; }
                var stripeStart = c;
                while (c < flagsLength && flagsBuffer[c] == p) c++;
                var stripeEnd = c;

                if (p - 1 >= paramsList.Count) continue;
                var lp = paramsList[p - 1];
                var stripeThickness = float.IsNaN(lp.thicknessPx) ? autoLineThickness : lp.thicknessPx;
                var capWidth = capWidthPerThickness * stripeThickness;
                var isPattern = lp.style == LineStyle.Dotted || lp.style == LineStyle.Dashed;

                uniText.CollectRangeEntries(stripeStart, stripeEnd, rangeEntriesScratch);

                for (var ei = 0; ei < rangeEntriesScratch.Count; ei++)
                {
                    var entry = rangeEntriesScratch[ei];
                    var visualLeft = offsetX + entry.minX;
                    var visualRight = offsetX + entry.maxX;
                    if (visualRight <= visualLeft) continue;

                    var firstG = entry.firstGlyphIdx;
                    var lastG = entry.lastGlyphIdx;
                    ref readonly var firstGlyph = ref allGlyphs[firstG];
                    ref readonly var lastGlyph = ref allGlyphs[lastG];

                    if (lastPatternLineIdx != entry.lineIdx)
                    {
                        lastPatternStartX = visualLeft;
                        lastPatternLineIdx = entry.lineIdx;
                    }

                    if (isPattern)
                    {
                        var runStartX = -1f;
                        var runEndX = 0f;
                        var runCluster = 0;
                        var runVarHash = 0L;
                        var runBaselineY = 0f;
                        var firstRunIdx = -1;
                        var lastRunIdx = -1;

                        for (var k = firstG; k <= lastG; k++)
                        {
                            ref readonly var gk = ref allGlyphs[k];
                            if (lp.skipInk && IsSkipInk(k))
                            {
                                if (runStartX >= 0f)
                                {
                                    AddSegment(runStartX, runEndX, runBaselineY, runVarHash, runCluster,
                                        -sdfPadding, uvRightCap, p, lastPatternStartX);
                                    if (firstRunIdx < 0) firstRunIdx = lineSegmentCount - 1;
                                    lastRunIdx = lineSegmentCount - 1;
                                    runStartX = -1f;
                                }
                                continue;
                            }
                            if (runStartX < 0f)
                            {
                                runStartX = offsetX + gk.left;
                                runCluster = gk.cluster;
                                runVarHash = ResolveLineVarHash(fontProvider, gk.fontId, underscoreFontHash, defaultVarHash);
                                runBaselineY = offsetY - gk.y;
                            }
                            runEndX = offsetX + gk.right;
                        }
                        if (runStartX >= 0f)
                        {
                            AddSegment(runStartX, runEndX, runBaselineY, runVarHash, runCluster,
                                -sdfPadding, uvRightCap, p, lastPatternStartX);
                            if (firstRunIdx < 0) firstRunIdx = lineSegmentCount - 1;
                            lastRunIdx = lineSegmentCount - 1;
                        }

                        if (firstRunIdx >= 0)
                        {
                            lineSegments[firstRunIdx].startX = visualLeft;
                            lineSegments[lastRunIdx].endX = visualRight;
                        }
                    }
                    else
                    {
                        var effCap = capWidth > 0f ? Math.Min(capWidth, (visualRight - visualLeft) * 0.5f) : 0f;
                        var bodyLeft = visualLeft + effCap;
                        var bodyRight = visualRight - effCap;

                        if (effCap > 0f)
                        {
                            var firstVh = ResolveLineVarHash(fontProvider, firstGlyph.fontId, underscoreFontHash, defaultVarHash);
                            AddSegment(visualLeft, bodyLeft, offsetY - firstGlyph.y, firstVh, firstGlyph.cluster,
                                -sdfPadding, capLeftEnd, p);
                        }

                        for (var k = firstG; k <= lastG; k++)
                        {
                            ref readonly var gk = ref allGlyphs[k];
                            if (lp.skipInk && IsSkipInk(k)) continue;
                            var bL = Math.Max(offsetX + gk.left, bodyLeft);
                            var bR = Math.Min(offsetX + gk.right, bodyRight);
                            if (bL >= bR) continue;
                            var vh = ResolveLineVarHash(fontProvider, gk.fontId, underscoreFontHash, defaultVarHash);
                            AddSegment(bL, bR, offsetY - gk.y, vh, gk.cluster, centerX, centerX, p);
                        }

                        if (effCap > 0f)
                        {
                            var lastVh = ResolveLineVarHash(fontProvider, lastGlyph.fontId, underscoreFontHash, defaultVarHash);
                            AddSegment(bodyRight, visualRight, offsetY - lastGlyph.y, lastVh, lastGlyph.cluster,
                                capRightStart, uvRightCap, p);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Resolves varHash48 for a line segment. If the text glyph's font matches the
        /// underscore font (same base font), uses the text's variation directly.
        /// Otherwise finds a companion variation of the underscore font with matching axes.
        /// </summary>
        private long ResolveLineVarHash(UniTextFontProvider fontProvider, int glyphFontId,
            int underscoreFontHash, long defaultVarHash)
        {
            var glyphFont = fontProvider.GetFontAsset(glyphFontId);
            if (glyphFont == null) return defaultVarHash;

            if (glyphFont.FontDataHash == underscoreFontHash)
                return buffers.ResolveVarHash48(glyphFontId, glyphFont);

            var companion = buffers.FindCompanionVarHash(glyphFontId, underscoreFontHash);
            return companion != 0 ? companion : defaultVarHash;
        }
    }

}
