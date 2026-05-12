using System;
using UnityEngine;


namespace LightSide
{
    public static class LineRenderHelper
    {
        [ThreadStatic] private static Glyph? cachedUnderscoreGlyph;
        [ThreadStatic] private static UniTextFont cachedUnderscoreFont;
        [ThreadStatic] private static int cachedFontProviderId;
        [ThreadStatic] private static long cachedVarHash;

        [ThreadStatic] private static Glyph? cachedBulletGlyph;
        [ThreadStatic] private static UniTextFont cachedBulletFont;
        [ThreadStatic] private static long cachedBulletVarHash;
        [ThreadStatic] private static int cachedBulletProviderId;


        /// <summary>
        /// Emits one decoration-line quad spanning [startX, endX] at <paramref name="baselineY"/> +
        /// <paramref name="lineYOffset"/>, sampling the underscore SDF column described by
        /// [<paramref name="uvLeft"/>, <paramref name="uvRight"/>] (use centerX..centerX for stretch
        /// segments and -sdfPadding..centerX / centerX..aspect+sdfPadding for end caps).
        /// </summary>
        /// <param name="thickness">Full visible line thickness in pixels (post.underlineThickness or
        /// OS/2.yStrikeoutSize at this font size). <c>NaN</c> falls back to the underscore glyph's
        /// natural rendered height. The quad height equals this thickness; SDF samples the full ink
        /// span vertically with a small padding margin for AA at top/bottom edges, so visible
        /// thickness scales linearly with the parameter regardless of underscore aspect.</param>
        public static void DrawLine(UniTextMeshGenerator gen, UniTextFontProvider fontProvider,
            float startX, float endX, float baselineY, float lineYOffset, int cluster, long varHash48,
            float uvLeft, float uvRight, float thickness)
        {
            if (gen == null || fontProvider == null)
                return;

            var maybeGlyph = GetUnderscoreGlyph(fontProvider, varHash48, out var glyphFont);
            if (!maybeGlyph.HasValue) return;
            var underscoreGlyph = maybeGlyph.Value;

            var atlas = GlyphAtlas.GetInstance(gen.RenderMode);
            if (!atlas.TryGetEntry(varHash48, underscoreGlyph.index, out var entry) || entry.encodedTile < 0)
                return;

            gen.TrackGlyphKey(GlyphAtlas.MakeKey(varHash48, underscoreGlyph.index));

            var metrics = underscoreGlyph.metrics;
            var upem = (float)glyphFont.UnitsPerEm;
            var metricsFactor = gen.FontSize * glyphFont.FontScale;
            var scale = metricsFactor / upem;

            var glyphH = metrics.height / upem;
            if (glyphH < 1e-6f) return;
            var glyphW = metrics.width / upem;
            var aspect = glyphW / glyphH;

            var glyphHeightLocal = metrics.height * scale;
            var resolvedThickness = float.IsNaN(thickness)
                ? Math.Max(glyphHeightLocal, gen.FontSize * 0.02f)
                : Math.Max(thickness, gen.FontSize * 0.005f);

            var y = baselineY + lineYOffset;

            const float sdfPadding = UniTextMeshGenerator.DefaultSdfPadding;
            var maxDim = Math.Max(aspect, 1f);
            var marginY = (maxDim - 1f) * 0.5f;
            var uvBottom = -marginY - sdfPadding;
            var uvTop = 1f + marginY + sdfPadding;
            var halfH = resolvedThickness * (uvTop - uvBottom) * 0.5f;

            gen.EnsureCapacity(4, 6);

            var verts = gen.Vertices;
            var uvData = gen.Uvs0;
            var uv1Data = gen.Uvs1;
            var cols = gen.Colors;
            var tris = gen.Triangles;

            var vertIdx = gen.vertexCount;
            var triIdx = gen.triangleCount;

            var tileIdx = (float)(entry.encodedTile + entry.pageIndex * GlyphAtlas.PageStride);

            verts[vertIdx] = new Vector3(startX, y - halfH, 0);
            verts[vertIdx + 1] = new Vector3(startX, y + halfH, 0);
            verts[vertIdx + 2] = new Vector3(endX, y + halfH, 0);
            verts[vertIdx + 3] = new Vector3(endX, y - halfH, 0);

            uvData[vertIdx] = new Vector4(uvLeft, uvBottom, tileIdx, glyphH);
            uvData[vertIdx + 1] = new Vector4(uvLeft, uvTop, tileIdx, glyphH);
            uvData[vertIdx + 2] = new Vector4(uvRight, uvTop, tileIdx, glyphH);
            uvData[vertIdx + 3] = new Vector4(uvRight, uvBottom, tileIdx, glyphH);

            var uv1Val = new Vector2(aspect, 0);
            uv1Data[vertIdx] = uv1Val;
            uv1Data[vertIdx + 1] = uv1Val;
            uv1Data[vertIdx + 2] = uv1Val;
            uv1Data[vertIdx + 3] = uv1Val;

            var defaultColor = gen.defaultColor;
            cols[vertIdx] = defaultColor;
            cols[vertIdx + 1] = defaultColor;
            cols[vertIdx + 2] = defaultColor;
            cols[vertIdx + 3] = defaultColor;

            var localI0 = vertIdx - gen.CurrentSegmentVertexStart;
            tris[triIdx] = localI0;
            tris[triIdx + 1] = localI0 + 1;
            tris[triIdx + 2] = localI0 + 2;
            tris[triIdx + 3] = localI0 + 2;
            tris[triIdx + 4] = localI0 + 3;
            tris[triIdx + 5] = localI0;

            gen.vertexCount += 4;
            gen.triangleCount += 6;

            gen.font = glyphFont;
            gen.fontMetricFactor = metricsFactor;
            gen.height = halfH * 2f;
            gen.currentCluster = cluster;
            gen.cursorX = startX;
            gen.baselineY = baselineY;
            gen.faceBaseIdx = vertIdx;
            gen.currentMaxGlyphExtent = 0f;
            gen.isVirtualGlyph = true;
            gen.onGlyph?.Invoke();

            gen.RequestBandUpgradeIfNeeded(GlyphAtlas.MakeKey(varHash48, underscoreGlyph.index), underscoreGlyph.index, in entry,
                glyphFont, varHash48, null, glyphH, aspect);
        }


        private static Glyph? GetUnderscoreGlyph(UniTextFontProvider fontProvider, long varHash48, out UniTextFont font)
        {
            var providerId = fontProvider.GetHashCode();

            if (cachedUnderscoreGlyph.HasValue && cachedFontProviderId == providerId && cachedVarHash == varHash48)
            {
                font = cachedUnderscoreFont;
                return cachedUnderscoreGlyph;
            }

            cachedUnderscoreGlyph = null;
            cachedUnderscoreFont = null;
            cachedFontProviderId = providerId;
            cachedVarHash = varHash48;

            const uint underscoreCodepoint = '_';

            var fontId = fontProvider.FindFontForCodepoint((int)underscoreCodepoint);
            font = fontProvider.GetFontAsset(fontId);

            var glyphIndex = font.GetGlyphIndexForUnicode(underscoreCodepoint);
            if (glyphIndex == 0)
                return null;

            var glyphLookup = font.GlyphLookupTable;
            if (glyphLookup != null && glyphLookup.TryGetValue(UniTextFont.GlyphKey(varHash48, glyphIndex), out var glyph))
            {
                cachedUnderscoreGlyph = glyph;
                cachedUnderscoreFont = font;
            }

            return cachedUnderscoreGlyph;
        }

        /// <summary>
        /// Emits one dot quad centered at (<paramref name="centerX"/>, <paramref name="baselineY"/> +
        /// <paramref name="lineYOffset"/>), sized <c>dotSize * aspect × dotSize</c> where
        /// <c>aspect</c> is the bullet glyph's natural width/height ratio (typically ≈1). Samples
        /// the bullet glyph (U+2022) so the SDF gives a true round shape with antialiased edge.
        /// Falls back to a square <paramref name="dotSize"/>×<paramref name="dotSize"/>
        /// stretched-underscore mark when the resolved font has no bullet glyph or the atlas entry
        /// is missing.
        /// </summary>
        public static void DrawDot(UniTextMeshGenerator gen, UniTextFontProvider fontProvider,
            float centerX, float baselineY, float lineYOffset, int cluster, float dotSize)
        {
            if (gen == null || fontProvider == null) return;
            if (dotSize <= 0f) return;

            var maybeGlyph = GetBulletGlyph(fontProvider, out var glyphFont, out var bulletVarHash);
            if (!maybeGlyph.HasValue || glyphFont == null)
            {
                DrawDotFallback(gen, fontProvider, centerX, baselineY, lineYOffset, cluster, dotSize);
                return;
            }
            var bulletGlyph = maybeGlyph.Value;

            var atlas = GlyphAtlas.GetInstance(gen.RenderMode);
            if (!atlas.TryGetEntry(bulletVarHash, bulletGlyph.index, out var entry) || entry.encodedTile < 0)
            {
                DrawDotFallback(gen, fontProvider, centerX, baselineY, lineYOffset, cluster, dotSize);
                return;
            }

            gen.TrackGlyphKey(GlyphAtlas.MakeKey(bulletVarHash, bulletGlyph.index));

            var metrics = bulletGlyph.metrics;
            var upem = (float)glyphFont.UnitsPerEm;
            var metricsFactor = gen.FontSize * glyphFont.FontScale;

            var glyphH = metrics.height / upem;
            if (glyphH < 1e-6f) return;
            var glyphW = metrics.width / upem;
            var aspect = glyphW / glyphH;

            var halfH = dotSize * 0.5f;
            var halfW = halfH * aspect;
            var y = baselineY + lineYOffset;

            const float sdfPadding = UniTextMeshGenerator.DefaultSdfPadding;
            var uvL = -sdfPadding;
            var uvR = aspect + sdfPadding;
            var uvB = -sdfPadding;
            var uvT = 1f + sdfPadding;

            gen.EnsureCapacity(4, 6);

            var verts = gen.Vertices;
            var uvData = gen.Uvs0;
            var uv1Data = gen.Uvs1;
            var cols = gen.Colors;
            var tris = gen.Triangles;

            var vertIdx = gen.vertexCount;
            var triIdx = gen.triangleCount;

            var tileIdx = (float)(entry.encodedTile + entry.pageIndex * GlyphAtlas.PageStride);

            var leftX = centerX - halfW;
            var rightX = centerX + halfW;
            var bottomY = y - halfH;
            var topY = y + halfH;

            verts[vertIdx]     = new Vector3(leftX, bottomY, 0);
            verts[vertIdx + 1] = new Vector3(leftX, topY, 0);
            verts[vertIdx + 2] = new Vector3(rightX, topY, 0);
            verts[vertIdx + 3] = new Vector3(rightX, bottomY, 0);

            uvData[vertIdx]     = new Vector4(uvL, uvB, tileIdx, glyphH);
            uvData[vertIdx + 1] = new Vector4(uvL, uvT, tileIdx, glyphH);
            uvData[vertIdx + 2] = new Vector4(uvR, uvT, tileIdx, glyphH);
            uvData[vertIdx + 3] = new Vector4(uvR, uvB, tileIdx, glyphH);

            var uv1Val = new Vector2(aspect, 0);
            uv1Data[vertIdx]     = uv1Val;
            uv1Data[vertIdx + 1] = uv1Val;
            uv1Data[vertIdx + 2] = uv1Val;
            uv1Data[vertIdx + 3] = uv1Val;

            var defaultColor = gen.defaultColor;
            cols[vertIdx]     = defaultColor;
            cols[vertIdx + 1] = defaultColor;
            cols[vertIdx + 2] = defaultColor;
            cols[vertIdx + 3] = defaultColor;

            var localI0 = vertIdx - gen.CurrentSegmentVertexStart;
            tris[triIdx]     = localI0;
            tris[triIdx + 1] = localI0 + 1;
            tris[triIdx + 2] = localI0 + 2;
            tris[triIdx + 3] = localI0 + 2;
            tris[triIdx + 4] = localI0 + 3;
            tris[triIdx + 5] = localI0;

            gen.vertexCount += 4;
            gen.triangleCount += 6;

            gen.font = glyphFont;
            gen.fontMetricFactor = metricsFactor;
            gen.height = dotSize;
            gen.currentCluster = cluster;
            gen.cursorX = leftX;
            gen.baselineY = baselineY;
            gen.faceBaseIdx = vertIdx;
            gen.currentMaxGlyphExtent = 0f;
            gen.isVirtualGlyph = true;
            gen.onGlyph?.Invoke();

            gen.RequestBandUpgradeIfNeeded(GlyphAtlas.MakeKey(bulletVarHash, bulletGlyph.index), bulletGlyph.index, in entry,
                glyphFont, bulletVarHash, null, glyphH, aspect);
        }

        private static void DrawDotFallback(UniTextMeshGenerator gen, UniTextFontProvider fontProvider,
            float centerX, float baselineY, float lineYOffset, int cluster, float dotSize)
        {
            var underscoreFontId = fontProvider.FindFontForCodepoint('_');
            var underscoreFont = fontProvider.GetFontAsset(underscoreFontId);
            if (underscoreFont == null) return;

            var defaultVarHash = underscoreFont.DefaultVarHash48;
            var maybeUnderscore = GetUnderscoreGlyph(fontProvider, defaultVarHash, out _);
            if (!maybeUnderscore.HasValue) return;
            var underscore = maybeUnderscore.Value;
            var um = underscore.metrics;
            var underscoreAspect = um.height > 0 ? (float)um.width / um.height : 1f;

            var halfDot = dotSize * 0.5f;
            const float sdfPadding = UniTextMeshGenerator.DefaultSdfPadding;
            DrawLine(gen, fontProvider, centerX - halfDot, centerX + halfDot,
                baselineY, lineYOffset, cluster, defaultVarHash,
                -sdfPadding, underscoreAspect + sdfPadding, dotSize);
        }

        private static Glyph? GetBulletGlyph(UniTextFontProvider fontProvider, out UniTextFont font, out long varHash48)
        {
            var providerId = fontProvider.GetHashCode();

            if (cachedBulletGlyph.HasValue && cachedBulletProviderId == providerId)
            {
                font = cachedBulletFont;
                varHash48 = cachedBulletVarHash;
                return cachedBulletGlyph;
            }

            cachedBulletGlyph = null;
            cachedBulletFont = null;
            cachedBulletVarHash = 0;
            cachedBulletProviderId = providerId;

            const uint bulletCodepoint = 0x2022;

            var fontId = fontProvider.FindFontForCodepoint((int)bulletCodepoint);
            font = fontProvider.GetFontAsset(fontId);
            if (font == null)
            {
                varHash48 = 0;
                return null;
            }

            var glyphIndex = font.GetGlyphIndexForUnicode(bulletCodepoint);
            if (glyphIndex == 0)
            {
                varHash48 = 0;
                return null;
            }

            varHash48 = font.DefaultVarHash48;

            var glyphLookup = font.GlyphLookupTable;
            if (glyphLookup != null && glyphLookup.TryGetValue(UniTextFont.GlyphKey(varHash48, glyphIndex), out var glyph))
            {
                cachedBulletGlyph = glyph;
                cachedBulletFont = font;
                cachedBulletVarHash = varHash48;
            }

            return cachedBulletGlyph;
        }
    }

}
