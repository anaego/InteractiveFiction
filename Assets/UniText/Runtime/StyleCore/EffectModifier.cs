using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Base class for modifiers that render an additional effect pass behind the face (outline, shadow, glow).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lifecycle per frame:
    /// <list type="number">
    /// <item>
    /// <c>onGlyph</c> — subclass <see cref="OnGlyphEffect"/> calls
    /// <see cref="EnqueueEffectQuad"/> to record a per-glyph request into its own instance buffer.
    /// The current value of <see cref="UniTextMeshGenerator.currentEffectPass"/> is captured into
    /// the request, deciding whether the duplicate eventually lands before or after the face.
    /// No mesh is written yet.
    /// </item>
    /// <item>
    /// <c>onMainPassFinalize</c> — each <see cref="EffectModifier"/> applies its queued requests in
    /// registration order, appending duplicate quads to the mesh generator's vertex buffer and
    /// queueing their triangle indices into the generator's shared pre- or post-face buffer
    /// (<see cref="UniTextMeshGenerator.QueueEffectTriangle"/>) according to the captured pass.
    /// </item>
    /// <item>
    /// After <c>onMainPassFinalize</c> — the generator flushes both shared buffers in one shot:
    /// pre-face tris are prepended, post-face tris are inserted at
    /// <see cref="UniTextMeshGenerator.postFaceInsertPoint"/>, producing the final order
    /// <c>[pre-face 1..N, face, post-face 1..N, line]</c>.
    /// </item>
    /// </list>
    /// </para>
    /// <para>
    /// Grouping per modifier (not per glyph) is what gives a consistent painter order when
    /// multiple effects overlap across glyph boundaries — outline of glyph N+1 never covers
    /// shadow of glyph N, because all outline tris precede all shadow tris.
    /// </para>
    /// </remarks>
    [Serializable]
    public abstract class EffectModifier : BaseModifier
    {
        protected struct EffectRequest
        {
            public int sourceBaseIdx;
            public Vector4 effectUv;
            public float offsetX;
            public float offsetY;
            public float expandDelta;
            public EffectPass pass;
        }

        private PooledBuffer<EffectRequest> requests;

        private Action onGlyphCallback;
        private Action onRebuildStartCallback;
        private Action applyCallback;

        /// <summary>
        /// Called for each glyph during mesh generation. Subclass checks cluster ranges
        /// and calls <see cref="EnqueueEffectQuad"/> for matching glyphs. Emoji glyphs must be
        /// skipped (<c>uniText.MeshGenerator.font.IsColor</c>).
        /// </summary>
        protected abstract void OnGlyphEffect();

        protected override void OnEnable()
        {
            requests.FakeClear();

            onGlyphCallback ??= OnGlyph;
            onRebuildStartCallback ??= ResetOwnRequests;
            applyCallback ??= ApplyOwnRequests;

            var gen = uniText.MeshGenerator;
            gen.onGlyph += onGlyphCallback;
            gen.onRebuildStart += onRebuildStartCallback;
            gen.onMainPassFinalize += applyCallback;
        }

        protected override void OnDisable()
        {
            var gen = uniText.MeshGenerator;
            gen.onGlyph -= onGlyphCallback;
            gen.onRebuildStart -= onRebuildStartCallback;
            gen.onMainPassFinalize -= applyCallback;
        }

        protected override void OnDestroy()
        {
            requests.Return();
            onGlyphCallback = null;
            onRebuildStartCallback = null;
            applyCallback = null;
        }

        private void OnGlyph() => OnGlyphEffect();

        private void ResetOwnRequests() => requests.FakeClear();

        /// <summary>
        /// Queues an effect duplicate for the current glyph. The actual quad append happens
        /// later in <c>onMainPassFinalize</c>, grouped per modifier so that all duplicates of this
        /// effect are contiguous in the final index buffer. The current value of
        /// <see cref="UniTextMeshGenerator.currentEffectPass"/> is captured into the request and
        /// determines whether the duplicate eventually lands before or after the face.
        /// </summary>
        /// <param name="sourceBaseIdx">Index of the source face quad's first vertex (use <c>gen.faceBaseIdx</c>).</param>
        /// <param name="effectUv">UV2 written to all 4 new vertices.</param>
        /// <param name="offsetX">X displacement applied to the duplicate.</param>
        /// <param name="offsetY">Y displacement applied to the duplicate.</param>
        /// <param name="expandDelta">Optional quad expansion on all sides (UV-space), applied after copy.</param>
        protected void EnqueueEffectQuad(int sourceBaseIdx, Vector4 effectUv, float offsetX = 0f, float offsetY = 0f, float expandDelta = 0f)
        {
            requests.Add(new EffectRequest
            {
                sourceBaseIdx = sourceBaseIdx,
                effectUv = effectUv,
                offsetX = offsetX,
                offsetY = offsetY,
                expandDelta = expandDelta,
                pass = uniText.MeshGenerator.currentEffectPass
            });
        }

        /// <summary>
        /// Default per-frame application of queued requests. Subclasses with multi-layer effects
        /// (e.g. extrude) may override to flush their own per-layer buffers in render-order
        /// rather than the per-glyph order that <see cref="EnqueueEffectQuad"/> implies.
        /// </summary>
        protected virtual void ApplyOwnRequests()
        {
            var count = requests.count;
            if (count == 0) return;

            var gen = uniText.MeshGenerator;
            var data = requests.data;
            for (var i = 0; i < count; i++)
            {
                ref var r = ref data[i];
                var destIdx = AppendSharedEffectQuad(gen, r.sourceBaseIdx, r.effectUv, r.offsetX, r.offsetY, r.pass);
                if (r.expandDelta > 0f)
                    gen.ExpandQuad(destIdx, r.expandDelta);
            }
        }

        /// <summary>
        /// Appends one duplicate quad to the generator's vertex buffer and queues its triangle
        /// indices into the thread-static shared buffer matching <paramref name="pass"/>. Returns the
        /// destination vertex index of the new quad's first vertex (use it with
        /// <see cref="UniTextMeshGenerator.ExpandQuad"/>).
        /// </summary>
        protected static int AppendSharedEffectQuad(UniTextMeshGenerator gen, int sourceBaseIdx, Vector4 effectUv, float offsetX, float offsetY, EffectPass pass = EffectPass.PreFace)
        {
            gen.EnsureCapacity(4, 0);
            gen.EnsureUvBuffer(2);

            var destIdx = gen.vertexCount;
            var verts = gen.Vertices;
            var uvs0 = gen.Uvs0;
            var uvs1 = gen.Uvs1;
            var uvs2 = gen.Uvs2;
            var cols = gen.Colors;

            Array.Copy(verts, sourceBaseIdx, verts, destIdx, 4);
            Array.Copy(uvs0, sourceBaseIdx, uvs0, destIdx, 4);
            Array.Copy(uvs1, sourceBaseIdx, uvs1, destIdx, 4);
            Array.Copy(cols, sourceBaseIdx, cols, destIdx, 4);

            uvs2[destIdx]     = effectUv;
            uvs2[destIdx + 1] = effectUv;
            uvs2[destIdx + 2] = effectUv;
            uvs2[destIdx + 3] = effectUv;

            if (offsetX != 0f || offsetY != 0f)
            {
                verts[destIdx].x     += offsetX; verts[destIdx].y     += offsetY;
                verts[destIdx + 1].x += offsetX; verts[destIdx + 1].y += offsetY;
                verts[destIdx + 2].x += offsetX; verts[destIdx + 2].y += offsetY;
                verts[destIdx + 3].x += offsetX; verts[destIdx + 3].y += offsetY;
            }

            gen.QueueEffectTriangle(pass, destIdx);

            gen.vertexCount += 4;
            return destIdx;
        }
    }
}
