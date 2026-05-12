using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LightSide
{
    /// <summary>
    /// Per-font extraction of glyph outlines as quadratic Bézier segments.
    /// Curves are extracted via native FreeType, normalized to [0,1] glyph space
    /// (height-based), and stored directly (no flattening) for GPU upload.
    /// Includes a face pool for parallel extraction across threads.
    /// </summary>
    internal sealed unsafe class GlyphCurveCache : IDisposable
    {
        private const int MaxCurvesPerGlyph = 2048;
        private const int MaxContoursPerGlyph = 256;

        /// <summary>
        /// One quadratic Bézier segment: p0 (start), p1 (control), p2 (end).
        /// Degenerate lines have p1 = midpoint(p0, p2).
        /// channelMask: R=1, G=2, B=4. Set by EdgeColoring for MSDF; ignored by SdfJob.
        /// </summary>
        public struct Segment
        {
            public float p0x, p0y, p1x, p1y, p2x, p2y;
            public byte channelMask;
            public byte contourIndex;
            /// <summary>Bit 0: endpoint A (p0) is a corner. Bit 1: endpoint B (p2) is a corner.
            /// Bits 2-4: channels exclusive to this segment at A. Bits 5-7: exclusive at B.</summary>
            public byte cornerFlags;
            /// <summary>1 if both sides of the segment are inside the glyph (internal edge). Excluded from SDF distance.</summary>
            public byte isInternal;
        }

        /// <summary>
        /// Glyph metrics extracted from FreeType.
        /// </summary>
        public struct GlyphCurveData
        {
            public float bboxMinX, bboxMinY, bboxMaxX, bboxMaxY;
            public float bearingX, bearingY;
            public float advanceX;
            public int designWidth, designHeight;
            public bool isEmpty;
        }

        private readonly byte[] fontData;
        private readonly int faceIndex;
        private readonly int unitsPerEm;
        private readonly ConcurrentBag<IntPtr> availableFaces = new();
        private readonly List<IntPtr> createdFaces = new();
        private readonly object poolLock = new();
        private readonly int maxPoolSize;

        private PooledBuffer<Segment> segmentBuffer;


        public GlyphCurveCache(IntPtr primaryFace, byte[] fontData, int faceIndex, int unitsPerEm)
        {
            this.fontData = fontData;
            this.faceIndex = faceIndex;
            this.unitsPerEm = unitsPerEm;
            maxPoolSize = Environment.ProcessorCount;

            availableFaces.Add(primaryFace);
        }

        /// <summary>
        /// Segment texels from the last <see cref="Extract"/> call.
        /// Valid until the next Extract call (backed by reusable buffer).
        /// </summary>
        public Span<Segment> LastSegments => segmentBuffer.Span;

        #region Single-threaded API (uses shared segmentBuffer)

        /// <summary>
        /// Extracts glyph outline from FreeType as quadratic Bézier curves.
        /// Clears the buffer first. Segment data available via <see cref="LastSegments"/>.
        /// </summary>
        public GlyphCurveData Extract(uint glyphIndex)
        {
            var face = RentFace();
            try
            {
                segmentBuffer.FakeClear();
                return ExtractCore(face, glyphIndex, ref segmentBuffer);
            }
            finally
            {
                ReturnFace(face);
            }
        }

        /// <summary>
        /// Resets the segment buffer. Call before a batch of <see cref="ExtractAppend"/> calls.
        /// </summary>
        public void ResetSegmentBuffer()
        {
            segmentBuffer.FakeClear();
        }

        /// <summary>
        /// Extracts glyph outline, APPENDING Bézier curves to the existing buffer (no clear).
        /// Returns metrics and the offset/count of this glyph's curves in <see cref="LastSegments"/>.
        /// </summary>
        public GlyphCurveData ExtractAppend(uint glyphIndex, out int segOffset, out int segCount)
        {
            var face = RentFace();
            try
            {
                int startOffset = segmentBuffer.count;
                var data = ExtractCore(face, glyphIndex, ref segmentBuffer);
                segOffset = startOffset;
                segCount = segmentBuffer.count - startOffset;
                return data;
            }
            finally
            {
                ReturnFace(face);
            }
        }

        #endregion

        #region Face Pool

        /// <summary>
        /// Rent a FreeType face handle for thread-safe extraction.
        /// Creates additional faces on demand up to ProcessorCount.
        /// </summary>
        public IntPtr RentFace()
        {
            if (availableFaces.TryTake(out var face))
                return face;

            lock (poolLock)
            {
                if (availableFaces.TryTake(out face))
                    return face;

                if (createdFaces.Count < maxPoolSize - 1)
                {
                    face = FT.LoadFace(fontData, faceIndex);
                    if (face != IntPtr.Zero)
                    {
                        createdFaces.Add(face);
                        return face;
                    }
                }
            }

            SpinWait spin = default;
            while (!availableFaces.TryTake(out face))
                spin.SpinOnce();
            return face;
        }

        /// <summary>
        /// Return a rented face handle to the pool.
        /// </summary>
        public void ReturnFace(IntPtr face)
        {
            if (face != IntPtr.Zero)
                availableFaces.Add(face);
        }

        #endregion

        #region Thread-safe extraction

        /// <summary>
        /// Thread-safe extraction: uses the provided face and output buffer (no shared state).
        /// Caller must rent face via <see cref="RentFace"/> and provide a per-thread buffer.
        /// </summary>
        public GlyphCurveData ExtractWithFace(IntPtr face, uint glyphIndex, ref PooledBuffer<Segment> output)
        {
            return ExtractCore(face, glyphIndex, ref output);
        }

        #endregion

        internal static long ftTicks;
        internal static long normalizeTicks;
        internal static long edgeColorTicks;
        internal static long markInternalTicks;

        internal static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        [System.Diagnostics.Conditional("UNITEXT_DEBUG")]
        internal static void ResetTimers()
        {
            Interlocked.Exchange(ref ftTicks, 0);
            Interlocked.Exchange(ref normalizeTicks, 0);
            Interlocked.Exchange(ref edgeColorTicks, 0);
            Interlocked.Exchange(ref markInternalTicks, 0);
        }

        [System.Diagnostics.Conditional("UNITEXT_DEBUG")]
        private static void BeginTicks(ref long t0) { t0 = Stopwatch.GetTimestamp(); }

        [System.Diagnostics.Conditional("UNITEXT_DEBUG")]
        private static void AddTicks(ref long counter, long t0) { Interlocked.Add(ref counter, Stopwatch.GetTimestamp() - t0); }

        private GlyphCurveData ExtractCore(IntPtr face, uint glyphIndex, ref PooledBuffer<Segment> output)
        {
            var rawCurves = stackalloc float[MaxCurvesPerGlyph * 8];
            var rawTypes = stackalloc int[MaxCurvesPerGlyph];
            var rawContours = stackalloc int[MaxContoursPerGlyph];
            int curveCount, contourCount;
            long t0 = 0;
            BeginTicks(ref t0);
            int err = FT.OutlineDecompose(face, glyphIndex,
                rawCurves, rawTypes, &curveCount, MaxCurvesPerGlyph,
                rawContours, &contourCount, MaxContoursPerGlyph,
                out int bearingX, out int bearingY, out int advanceX,
                out int width, out int height);
            AddTicks(ref ftTicks, t0);

            if (err != 0 || curveCount == 0)
            {
                return new GlyphCurveData
                {
                    isEmpty = true,
                    bearingX = bearingX / (float)unitsPerEm,
                    bearingY = bearingY / (float)unitsPerEm,
                    advanceX = advanceX / (float)unitsPerEm,
                    designWidth = width,
                    designHeight = height
                };
            }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < curveCount; i++)
            {
                float* c = rawCurves + i * 8;
                for (int j = 0; j < 3; j++)
                {
                    float x = c[j * 2];
                    float y = c[j * 2 + 1];
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            float bboxH = maxY - minY;
            if (bboxH < 1e-6f) bboxH = 1f;
            float invScale = 1f / bboxH;

            output.EnsureCapacity(output.count + curveCount);
            int segStart = output.count;

            for (int i = 0; i < curveCount; i++)
            {
                float* c = rawCurves + i * 8;
                var seg = new Segment
                {
                    p0x = (c[0] - minX) * invScale, p0y = (c[1] - minY) * invScale,
                    p1x = (c[2] - minX) * invScale, p1y = (c[3] - minY) * invScale,
                    p2x = (c[4] - minX) * invScale, p2y = (c[5] - minY) * invScale
                };
                output.Add(seg);
            }


            BeginTicks(ref t0);
            curveCount = NormalizeContours(ref output, segStart, curveCount, rawContours, contourCount);
            AddTicks(ref normalizeTicks, t0);

            BeginTicks(ref t0);
            EdgeColoring.ColorAllContours(output.data, segStart, curveCount, rawContours, contourCount);
            AddTicks(ref edgeColorTicks, t0);

            int cStart = 0;
            for (int c = 0; c < contourCount; c++)
            {
                int cEnd = rawContours[c];
                for (int i = cStart; i <= cEnd; i++)
                    output.data[segStart + i].contourIndex = (byte)c;
                cStart = cEnd + 1;
            }

            BeginTicks(ref t0);
            MarkInternalSegments(output.data, segStart, curveCount);
            AddTicks(ref markInternalTicks, t0);

            return new GlyphCurveData
            {
                bboxMinX = minX, bboxMinY = minY,
                bboxMaxX = maxX, bboxMaxY = maxY,
                bearingX = bearingX / (float)unitsPerEm,
                bearingY = bearingY / (float)unitsPerEm,
                advanceX = advanceX / (float)unitsPerEm,
                designWidth = width,
                designHeight = height,
                isEmpty = false
            };
        }

        /// <summary>
        /// Port of msdfgen's Shape::normalize(): splits single-edge contours into 3 parts
        /// so EdgeColoring can assign distinct channel masks (instead of WHITE = all identical).
        /// Processes back-to-front to expand in-place without overwriting unprocessed data.
        /// </summary>
        private static int NormalizeContours(ref PooledBuffer<Segment> output, int segStart, int segCount,
            int* rawContours, int contourCount)
        {
            int singleCount = 0;
            int cStart = 0;
            for (int c = 0; c < contourCount; c++)
            {
                int cEnd = rawContours[c];
                if (cEnd == cStart) singleCount++;
                cStart = cEnd + 1;
            }
            if (singleCount == 0) return segCount;

            int extra = singleCount * 2;
            int newSegCount = segCount + extra;
            output.EnsureCapacity(segStart + newSegCount);

            int writePos = newSegCount - 1;
            for (int c = contourCount - 1; c >= 0; c--)
            {
                int cEnd = rawContours[c];
                int cStartSeg = c > 0 ? rawContours[c - 1] + 1 : 0;
                int edgeCount = cEnd - cStartSeg + 1;

                if (edgeCount == 1)
                {
                    Segment seg = output.data[segStart + cStartSeg];
                    SplitSegmentInThirds(in seg, out var p0, out var p1, out var p2);
                    output.data[segStart + writePos] = p2;
                    output.data[segStart + writePos - 1] = p1;
                    output.data[segStart + writePos - 2] = p0;
                    rawContours[c] = writePos;
                    writePos -= 3;
                }
                else
                {
                    for (int i = edgeCount - 1; i >= 0; i--)
                        output.data[segStart + writePos - (edgeCount - 1 - i)] = output.data[segStart + cStartSeg + i];
                    rawContours[c] = writePos;
                    writePos -= edgeCount;
                }
            }

            output.count = segStart + newSegCount;
            return newSegCount;
        }

        /// <summary>
        /// Exact port of msdfgen's splitInThirds for quadratic Bézier: subdivides at t=1/3 and t=2/3
        /// using de Casteljau algorithm, producing 3 sub-segments.
        /// </summary>
        private static void SplitSegmentInThirds(in Segment seg, out Segment part0, out Segment part1, out Segment part2)
        {
            part0 = default;
            part1 = default;
            part2 = default;

            float p0x = seg.p0x, p0y = seg.p0y;
            float p1x = seg.p1x, p1y = seg.p1y;
            float p2x = seg.p2x, p2y = seg.p2y;

            float m01x = Mix(p0x, p1x, 1f / 3f), m01y = Mix(p0y, p1y, 1f / 3f);
            float m12x = Mix(p1x, p2x, 1f / 3f), m12y = Mix(p1y, p2y, 1f / 3f);
            float pt13x = Mix(m01x, m12x, 1f / 3f), pt13y = Mix(m01y, m12y, 1f / 3f);

            float n01x = Mix(p0x, p1x, 2f / 3f), n01y = Mix(p0y, p1y, 2f / 3f);
            float n12x = Mix(p1x, p2x, 2f / 3f), n12y = Mix(p1y, p2y, 2f / 3f);
            float pt23x = Mix(n01x, n12x, 2f / 3f), pt23y = Mix(n01y, n12y, 2f / 3f);

            part0.p0x = p0x; part0.p0y = p0y;
            part0.p1x = m01x; part0.p1y = m01y;
            part0.p2x = pt13x; part0.p2y = pt13y;

            float a59x = Mix(p0x, p1x, 5f / 9f), a59y = Mix(p0y, p1y, 5f / 9f);
            float b49x = Mix(p1x, p2x, 4f / 9f), b49y = Mix(p1y, p2y, 4f / 9f);
            part1.p0x = pt13x; part1.p0y = pt13y;
            part1.p1x = Mix(a59x, b49x, 0.5f); part1.p1y = Mix(a59y, b49y, 0.5f);
            part1.p2x = pt23x; part1.p2y = pt23y;

            part2.p0x = pt23x; part2.p0y = pt23y;
            part2.p1x = n12x; part2.p1y = n12y;
            part2.p2x = p2x; part2.p2y = p2y;
        }

        private static float Mix(float a, float b, float t) => a + (b - a) * t;

        private static void MarkInternalSegments(Segment[] data, int segStart, int segCount)
        {
            const float normalEps = 1e-3f;

            int numContours = 0;
            for (int i = 0; i < segCount; i++)
            {
                int ci = data[segStart + i].contourIndex;
                if (ci >= numContours) numContours = ci + 1;
            }
            if (numContours <= 1) return;

            Span<float> sMinX = stackalloc float[segCount];
            Span<float> sMinY = stackalloc float[segCount];
            Span<float> sMaxX = stackalloc float[segCount];
            Span<float> sMaxY = stackalloc float[segCount];
            Span<byte> isSplit = stackalloc byte[segCount];
            Span<float> csM01x = stackalloc float[segCount];
            Span<float> csM01y = stackalloc float[segCount];
            Span<float> csMxc = stackalloc float[segCount];
            Span<float> csMyc = stackalloc float[segCount];
            Span<float> csM12x = stackalloc float[segCount];
            Span<float> csM12y = stackalloc float[segCount];

            for (int i = 0; i < segCount; i++)
            {
                ref var s = ref data[segStart + i];
                float p0x = s.p0x, p0y = s.p0y, p1x = s.p1x, p1y = s.p1y, p2x = s.p2x, p2y = s.p2y;
                sMinX[i] = Math.Min(p0x, Math.Min(p1x, p2x));
                sMinY[i] = Math.Min(p0y, Math.Min(p1y, p2y));
                sMaxX[i] = Math.Max(p0x, Math.Max(p1x, p2x));
                sMaxY[i] = Math.Max(p0y, Math.Max(p1y, p2y));

                float denom = p0y - 2f * p1y + p2y;
                if (Math.Abs(denom) > 1e-10f)
                {
                    float tSplit = (p0y - p1y) / denom;
                    if (tSplit > 1e-6f && tSplit < 1f - 1e-6f)
                    {
                        float t = tSplit, mt = 1f - t;
                        float m01x = mt * p0x + t * p1x, m01y = mt * p0y + t * p1y;
                        float m12x = mt * p1x + t * p2x, m12y = mt * p1y + t * p2y;
                        float mx = mt * m01x + t * m12x, my = mt * m01y + t * m12y;
                        isSplit[i] = 1;
                        csM01x[i] = m01x; csM01y[i] = m01y;
                        csMxc[i] = mx; csMyc[i] = my;
                        csM12x[i] = m12x; csM12y[i] = m12y;
                    }
                }
            }

            Span<float> cMinX = stackalloc float[numContours];
            Span<float> cMinY = stackalloc float[numContours];
            Span<float> cMaxX = stackalloc float[numContours];
            Span<float> cMaxY = stackalloc float[numContours];
            for (int c = 0; c < numContours; c++)
            {
                cMinX[c] = float.MaxValue; cMinY[c] = float.MaxValue;
                cMaxX[c] = float.MinValue; cMaxY[c] = float.MinValue;
            }
            for (int i = 0; i < segCount; i++)
            {
                int ci = data[segStart + i].contourIndex;
                if (sMinX[i] < cMinX[ci]) cMinX[ci] = sMinX[i];
                if (sMinY[i] < cMinY[ci]) cMinY[ci] = sMinY[i];
                if (sMaxX[i] > cMaxX[ci]) cMaxX[ci] = sMaxX[i];
                if (sMaxY[i] > cMaxY[ci]) cMaxY[ci] = sMaxY[i];
            }

            Span<bool> contourHasOverlap = stackalloc bool[numContours];
            for (int a = 0; a < numContours; a++)
            {
                bool over = false;
                for (int b = 0; b < numContours && !over; b++)
                {
                    if (a == b) continue;
                    if (cMinX[a] <= cMaxX[b] && cMaxX[a] >= cMinX[b] &&
                        cMinY[a] <= cMaxY[b] && cMaxY[a] >= cMinY[b])
                        over = true;
                }
                contourHasOverlap[a] = over;
            }

            for (int i = 0; i < segCount; i++)
            {
                ref var seg = ref data[segStart + i];
                int myContour = seg.contourIndex;
                if (!contourHasOverlap[myContour]) continue;

                float ssMinX = sMinX[i], ssMinY = sMinY[i], ssMaxX = sMaxX[i], ssMaxY = sMaxY[i];

                bool segOverlapsOther = false;
                for (int b = 0; b < numContours && !segOverlapsOther; b++)
                {
                    if (b == myContour) continue;
                    if (ssMinX <= cMaxX[b] && ssMaxX >= cMinX[b] &&
                        ssMinY <= cMaxY[b] && ssMaxY >= cMinY[b])
                        segOverlapsOther = true;
                }
                if (!segOverlapsOther) continue;

                float midX = 0.25f * seg.p0x + 0.5f * seg.p1x + 0.25f * seg.p2x;
                float midY = 0.25f * seg.p0y + 0.5f * seg.p1y + 0.25f * seg.p2y;

                float tanX = seg.p2x - seg.p0x;
                float tanY = seg.p2y - seg.p0y;
                float normLen = (float)Math.Sqrt(tanX * tanX + tanY * tanY);
                if (normLen < 1e-10f) continue;

                float invLen = normalEps / normLen;
                float nx = -tanY * invLen;
                float ny = tanX * invLen;

                int windA = PointWindingBanded(data, segStart, segCount, sMinY, sMaxY, sMaxX,
                    isSplit, csM01x, csM01y, csMxc, csMyc, csM12x, csM12y, midX + nx, midY + ny);
                if (windA == 0) continue;
                int windB = PointWindingBanded(data, segStart, segCount, sMinY, sMaxY, sMaxX,
                    isSplit, csM01x, csM01y, csMxc, csMyc, csM12x, csM12y, midX - nx, midY - ny);

                if (windB != 0)
                    seg.isInternal = 1;
            }
        }

        private static int PointWindingBanded(Segment[] data, int segStart, int segCount,
            ReadOnlySpan<float> sMinY, ReadOnlySpan<float> sMaxY, ReadOnlySpan<float> sMaxX,
            ReadOnlySpan<byte> isSplit,
            ReadOnlySpan<float> csM01x, ReadOnlySpan<float> csM01y,
            ReadOnlySpan<float> csMxc, ReadOnlySpan<float> csMyc,
            ReadOnlySpan<float> csM12x, ReadOnlySpan<float> csM12y,
            float px, float py)
        {
            int winding = 0;
            for (int i = 0; i < segCount; i++)
            {
                if (sMaxX[i] <= px) continue;
                if (py < sMinY[i] || py >= sMaxY[i]) continue;
                ref var seg = ref data[segStart + i];
                if (isSplit[i] == 0)
                {
                    winding += MonoRayCrossing(seg.p0x, seg.p0y, seg.p1x, seg.p1y, seg.p2x, seg.p2y, px, py);
                }
                else
                {
                    float mx = csMxc[i], my = csMyc[i];
                    winding += MonoRayCrossing(seg.p0x, seg.p0y, csM01x[i], csM01y[i], mx, my, px, py)
                             + MonoRayCrossing(mx, my, csM12x[i], csM12y[i], seg.p2x, seg.p2y, px, py);
                }
            }
            return winding;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int MonoRayCrossing(float p0x, float p0y, float p1x, float p1y,
            float p2x, float p2y, float px, float py)
        {
            float yMin, yMax;
            int dir;
            if (p2y > p0y) { yMin = p0y; yMax = p2y; dir = 1; }
            else if (p0y > p2y) { yMin = p2y; yMax = p0y; dir = -1; }
            else return 0;

            if (py < yMin || py >= yMax) return 0;

            float d01x = p1x - p0x, d01y = p1y - p0y;
            float d02x = p2x - p0x, d02y = p2y - p0y;

            float xHit;
            if (Math.Abs(d01x * d02y - d01y * d02x) < 1e-5f)
            {
                float dy = p2y - p0y;
                float t = (py - p0y) / dy;
                xHit = p0x + t * (p2x - p0x);
            }
            else
            {
                float a = p0y - 2f * p1y + p2y;
                float b = 2f * (p1y - p0y);
                float c = p0y - py;
                float disc = b * b - 4f * a * c;
                if (disc < 0f) return 0;
                float sqrtDisc = (float)Math.Sqrt(disc);
                float t0 = (-b - sqrtDisc) / (2f * a);
                float t1 = (-b + sqrtDisc) / (2f * a);
                float t;
                if (t0 >= 0f && t0 <= 1f) t = t0;
                else if (t1 >= 0f && t1 <= 1f) t = t1;
                else return 0;
                float mt = 1f - t;
                xHit = mt * mt * p0x + 2f * mt * t * p1x + t * t * p2x;
            }

            return (xHit > px) ? dir : 0;
        }

        public void Dispose()
        {
            segmentBuffer.Return();

            lock (poolLock)
            {
                foreach (var face in createdFaces)
                    if (face != IntPtr.Zero) FT.UnloadFace(face);
                createdFaces.Clear();
            }

            while (availableFaces.TryTake(out _)) { }
        }
    }
}
