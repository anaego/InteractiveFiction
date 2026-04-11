#if UNITY_IOS && !UNITY_EDITOR
using System;

namespace LightSide
{
    internal static class CoreTextEmojiShaper
    {
        [ThreadStatic] private static uint[] glyphIdBuf;
        [ThreadStatic] private static int[] advanceBuf;
        [ThreadStatic] private static int[] clusterBuf;

        private static void EnsureBuffers(int capacity)
        {
            if (glyphIdBuf == null || glyphIdBuf.Length < capacity)
            {
                glyphIdBuf = new uint[Math.Max(capacity, 64)];
                advanceBuf = new int[Math.Max(capacity, 64)];
                clusterBuf = new int[Math.Max(capacity, 64)];
            }
        }

        internal static unsafe int Shape(
            ReadOnlySpan<int> context,
            int itemOffset,
            int itemLength,
            int upem,
            float scale,
            ref PooledBuffer<ShapedGlyph> output,
            out float totalAdvance)
        {
            totalAdvance = 0;
            if (itemLength <= 0) return 0;

            EnsureBuffers(itemLength);

            int glyphCount;
            fixed (int* cpPtr = context)
            fixed (uint* gPtr = glyphIdBuf)
            fixed (int* aPtr = advanceBuf)
            fixed (int* cPtr = clusterBuf)
            {
                glyphCount = NativeFontReader.ShapeEmojiRun(
                    cpPtr + itemOffset, itemLength, upem,
                    gPtr, aPtr, cPtr, itemLength);
            }

            if (glyphCount <= 0) return 0;

            var writeStart = output.count;
            output.EnsureCapacity(writeStart + glyphCount);
            var data = output.data;

            for (int i = 0; i < glyphCount; i++)
            {
                float advX = advanceBuf[i] * scale;
                data[writeStart + i] = new ShapedGlyph
                {
                    glyphId = (int)glyphIdBuf[i],
                    cluster = clusterBuf[i] + itemOffset,
                    advanceX = advX,
                    advanceY = 0,
                    offsetX = 0,
                    offsetY = 0
                };
                totalAdvance += advX;
            }

            output.count = writeStart + glyphCount;
            return glyphCount;
        }
    }
}
#endif
