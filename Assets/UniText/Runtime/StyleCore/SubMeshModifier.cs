using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Base class for modifiers that emit a separate, owner-controlled sub-mesh rendered by its own
    /// <c>CanvasRenderer</c> with a user-supplied material (see <see cref="MaterialModifier"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lifecycle per frame:
    /// <list type="number">
    /// <item><c>onRebuildStart</c> — own buffers are cleared; any persistent state (e.g. Replace-mode
    /// alpha zero'd into base mesh) will be re-applied in the next <c>onGlyph</c> pass.</item>
    /// <item><c>onGlyph</c> — subclass overrides <see cref="ShouldIncludeCurrentGlyph"/> and, when true,
    /// the base class copies the face quad (4 verts) from the generator into its own per-atlas buffer
    /// (SDF atlas vs emoji atlas are kept separate).</item>
    /// <item><c>onCollectSubMeshes</c> — the base class uploads owned buffers into private
    /// <see cref="Mesh"/> instances and appends up to two entries (text + emoji) to the render-data list,
    /// with the material, atlas, order and sort index chosen by the subclass.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Compared to <see cref="EffectModifier"/>, which bakes its duplicates into the same mesh as the
    /// face pass, <see cref="SubMeshModifier"/> keeps its vertices in isolated local buffers because
    /// each sub-mesh is bound to a distinct material and CanvasRenderer.
    /// </para>
    /// </remarks>
    [Serializable]
    public abstract class SubMeshModifier : BaseModifier
    {
        protected enum Slot : byte { Text = 0, Emoji = 1 }

        /// <summary>Accumulated raw geometry for a single slot. Emitted as <see cref="UniTextRenderData"/>
        /// per frame; consumers (canvas / world batcher) read directly from these pooled buffers.</summary>
        private struct SlotBuffers
        {
            public PooledBuffer<Vector3> vertices;
            public PooledBuffer<Vector4> uvs0;
            public PooledBuffer<Vector4> uvs1;
            public PooledBuffer<Vector4> uvs2;
            public PooledBuffer<Vector4> uvs3;
            public PooledBuffer<Color32> colors;
            public PooledBuffer<int> triangles;
            public bool hasUvs2;
            public bool hasUvs3;

            public int VertexCount => vertices.count;
            public int TriangleCount => triangles.count;

            public void Clear()
            {
                vertices.FakeClear();
                uvs0.FakeClear();
                uvs1.FakeClear();
                uvs2.FakeClear();
                uvs3.FakeClear();
                colors.FakeClear();
                triangles.FakeClear();
                hasUvs2 = false;
                hasUvs3 = false;
            }

            public void Return()
            {
                vertices.Return();
                uvs0.Return();
                uvs1.Return();
                uvs2.Return();
                uvs3.Return();
                colors.Return();
                triangles.Return();
            }
        }

        private SlotBuffers textSlot;
        private SlotBuffers emojiSlot;

        private Action onGlyphCallback;
        private Action onRebuildStartCallback;
        private Action<List<UniTextRenderData>> onCollectCallback;

        /// <summary>
        /// Tells the base class whether the glyph currently being emitted by the generator should be
        /// copied into this modifier's sub-mesh. Inspect <c>uniText.MeshGenerator</c> fields
        /// (<c>currentCluster</c>, <c>faceBaseIdx</c>, <c>font.IsColor</c>, etc.) for the decision.
        /// </summary>
        protected abstract bool ShouldIncludeCurrentGlyph();

        /// <summary>
        /// Resolves the material for a given slot. Return <see langword="null"/> to skip emitting that slot.
        /// </summary>
        /// <param name="slot">Text or emoji atlas slot.</param>
        protected abstract Material GetMaterialForSlot(Slot slot);

        protected virtual GlyphAtlas GetAtlasForSlot(Slot slot) =>
            slot == Slot.Emoji
                ? GlyphAtlas.Emoji
                : GlyphAtlas.GetInstance(uniText.MeshGenerator.RenderMode);

        protected virtual RenderOrder GetRenderOrder() => RenderOrder.Over;

        /// <summary>Stable sort key within the same <see cref="RenderOrder"/> group (lower renders first).</summary>
        protected virtual int GetSortIndex() => 0;

        /// <summary>
        /// Called after a glyph's quad is copied into the slot buffers. Override to write custom UV2/UV3
        /// data for the just-appended four vertices, or to modify colors/positions. The four vertices
        /// occupy indices <c>[vertexStart, vertexStart + 4)</c> in the slot buffer identified by <paramref name="slot"/>.
        /// </summary>
        /// <param name="slot">Text or emoji slot the quad was written to.</param>
        /// <param name="vertexStart">Index of the first of the four appended vertices in the slot buffer.</param>
        /// <param name="gen">The current mesh generator (same as <c>uniText.MeshGenerator</c>, passed for convenience).</param>
        protected virtual void OnQuadAppended(Slot slot, int vertexStart, UniTextMeshGenerator gen) { }

        /// <summary>Writes a value into UV2 channel for a vertex in the slot buffer (auto-allocates channel).</summary>
        protected void SetSubMeshUv2(Slot slot, int vertexIndex, Vector4 value)
        {
            ref var s = ref SlotRef(slot);
            EnsureSlotUvBuffer(ref s, 2, s.vertices.count);
            s.uvs2.data[vertexIndex] = value;
            s.hasUvs2 = true;
        }

        /// <summary>Writes a value into UV3 channel for a vertex in the slot buffer (auto-allocates channel).</summary>
        protected void SetSubMeshUv3(Slot slot, int vertexIndex, Vector4 value)
        {
            ref var s = ref SlotRef(slot);
            EnsureSlotUvBuffer(ref s, 3, s.vertices.count);
            s.uvs3.data[vertexIndex] = value;
            s.hasUvs3 = true;
        }

        /// <summary>Writes the same value to UV2 for all 4 vertices of a quad (indices <c>[vertexStart..+4)</c>).</summary>
        protected void SetSubMeshUv2Quad(Slot slot, int vertexStart, Vector4 value)
        {
            ref var s = ref SlotRef(slot);
            EnsureSlotUvBuffer(ref s, 2, s.vertices.count);
            var data = s.uvs2.data;
            data[vertexStart]     = value;
            data[vertexStart + 1] = value;
            data[vertexStart + 2] = value;
            data[vertexStart + 3] = value;
            s.hasUvs2 = true;
        }

        /// <summary>Writes the same value to UV3 for all 4 vertices of a quad (indices <c>[vertexStart..+4)</c>).</summary>
        protected void SetSubMeshUv3Quad(Slot slot, int vertexStart, Vector4 value)
        {
            ref var s = ref SlotRef(slot);
            EnsureSlotUvBuffer(ref s, 3, s.vertices.count);
            var data = s.uvs3.data;
            data[vertexStart]     = value;
            data[vertexStart + 1] = value;
            data[vertexStart + 2] = value;
            data[vertexStart + 3] = value;
            s.hasUvs3 = true;
        }

        /// <summary>Direct access to the slot color buffer for read/write on already-appended vertices.</summary>
        protected Color32[] GetSlotColors(Slot slot) => SlotRef(slot).colors.data;

        /// <summary>Direct access to the slot vertex buffer for read/write on already-appended vertices.</summary>
        protected Vector3[] GetSlotVertices(Slot slot) => SlotRef(slot).vertices.data;

        /// <summary>
        /// Expands a 4-vertex quad outward on all sides by <paramref name="delta"/> em-units.
        /// Updates both positions and UV0 in sync so the atlas sample stays aligned.
        /// Mirror of <see cref="UniTextMeshGenerator.ExpandQuad"/> but operates on the sub-mesh slot
        /// buffers. No-op for non-positive deltas or when UV0 has not been written yet.
        /// </summary>
        /// <param name="slot">Target slot (Text / Emoji).</param>
        /// <param name="vertexStart">Index of the first of the four quad vertices in the slot buffer.</param>
        /// <param name="delta">Padding to add on each side, in em-units (matches glyph-UV scale).</param>
        protected void ExpandSubMeshQuad(Slot slot, int vertexStart, float delta)
        {
            if (delta <= 0f) return;
            ref var s = ref SlotRef(slot);
            if (s.uvs0.data == null) return;

            var glyphH = s.uvs0.data[vertexStart].w;
            var deltaPixels = delta * glyphH * uniText.MeshGenerator.fontMetricFactor;

            var verts = s.vertices.data;
            verts[vertexStart].x     -= deltaPixels;
            verts[vertexStart].y     -= deltaPixels;
            verts[vertexStart + 1].x -= deltaPixels;
            verts[vertexStart + 1].y += deltaPixels;
            verts[vertexStart + 2].x += deltaPixels;
            verts[vertexStart + 2].y += deltaPixels;
            verts[vertexStart + 3].x += deltaPixels;
            verts[vertexStart + 3].y -= deltaPixels;

            var uvData = s.uvs0.data;
            uvData[vertexStart].x     -= delta;
            uvData[vertexStart].y     -= delta;
            uvData[vertexStart + 1].x -= delta;
            uvData[vertexStart + 1].y += delta;
            uvData[vertexStart + 2].x += delta;
            uvData[vertexStart + 2].y += delta;
            uvData[vertexStart + 3].x += delta;
            uvData[vertexStart + 3].y -= delta;
        }

        protected override void OnEnable()
        {
            textSlot.Clear();
            emojiSlot.Clear();

            onGlyphCallback ??= OnGlyph;
            onRebuildStartCallback ??= OnRebuildStart;
            onCollectCallback ??= OnCollectSubMeshes;

            var gen = uniText.MeshGenerator;
            gen.onGlyph += onGlyphCallback;
            gen.onRebuildStart += onRebuildStartCallback;
            gen.onCollectSubMeshes += onCollectCallback;
        }

        protected override void OnDisable()
        {
            var gen = uniText.MeshGenerator;
            gen.onGlyph -= onGlyphCallback;
            gen.onRebuildStart -= onRebuildStartCallback;
            gen.onCollectSubMeshes -= onCollectCallback;
        }

        protected override void OnDestroy()
        {
            textSlot.Return();
            emojiSlot.Return();
            onGlyphCallback = null;
            onRebuildStartCallback = null;
            onCollectCallback = null;
        }

        private ref SlotBuffers SlotRef(Slot slot)
        {
            return ref slot == Slot.Emoji ? ref emojiSlot : ref textSlot;
        }

        private void OnRebuildStart()
        {
            textSlot.Clear();
            emojiSlot.Clear();
            OnBeforeRebuild();
        }

        /// <summary>Called at the start of every rebuild (before any <c>onGlyph</c>). Override to refresh
        /// derived state (e.g. runtime material clones) that depends on the generator's current mode.</summary>
        protected virtual void OnBeforeRebuild() { }

        private void OnGlyph()
        {
            if (!ShouldIncludeCurrentGlyph()) return;

            var gen = uniText.MeshGenerator;
            var srcBase = gen.faceBaseIdx;
            if (srcBase < 0) return;

            var slot = gen.font != null && gen.font.IsColor ? Slot.Emoji : Slot.Text;
            ref var s = ref SlotRef(slot);

            var dstBase = s.vertices.count;
            AppendQuadFromGenerator(ref s, gen, srcBase);
            OnQuadAppended(slot, dstBase, gen);
        }

        private static void AppendQuadFromGenerator(ref SlotBuffers s, UniTextMeshGenerator gen, int srcBase)
        {
            EnsureSlotCapacity(ref s, 4, 6);

            var dstBase = s.vertices.count;

            var srcVerts = gen.Vertices;
            var srcUv0 = gen.Uvs0;
            var srcUv1 = gen.Uvs1;
            var srcCols = gen.Colors;

            Array.Copy(srcVerts, srcBase, s.vertices.data, dstBase, 4);
            Array.Copy(srcUv0, srcBase, s.uvs0.data, dstBase, 4);
            Array.Copy(srcUv1, srcBase, s.uvs1.data, dstBase, 4);
            Array.Copy(srcCols, srcBase, s.colors.data, dstBase, 4);

            if (s.hasUvs2 && s.uvs2.data != null)
            {
                s.uvs2.data[dstBase]     = default;
                s.uvs2.data[dstBase + 1] = default;
                s.uvs2.data[dstBase + 2] = default;
                s.uvs2.data[dstBase + 3] = default;
            }
            if (s.hasUvs3 && s.uvs3.data != null)
            {
                s.uvs3.data[dstBase]     = default;
                s.uvs3.data[dstBase + 1] = default;
                s.uvs3.data[dstBase + 2] = default;
                s.uvs3.data[dstBase + 3] = default;
            }

            var tri = s.triangles.count;
            s.triangles.data[tri]     = dstBase;
            s.triangles.data[tri + 1] = dstBase + 1;
            s.triangles.data[tri + 2] = dstBase + 2;
            s.triangles.data[tri + 3] = dstBase + 2;
            s.triangles.data[tri + 4] = dstBase + 3;
            s.triangles.data[tri + 5] = dstBase;

            s.vertices.count += 4;
            s.uvs0.count     += 4;
            s.uvs1.count     += 4;
            s.colors.count   += 4;
            s.triangles.count = tri + 6;
            if (s.uvs2.data != null) s.uvs2.count = s.vertices.count;
            if (s.uvs3.data != null) s.uvs3.count = s.vertices.count;
        }

        private static void EnsureSlotCapacity(ref SlotBuffers s, int extraVerts, int extraTris)
        {
            EnsureArrayCapacity(ref s.vertices, s.vertices.count + extraVerts);
            EnsureArrayCapacity(ref s.uvs0,     s.vertices.count + extraVerts);
            EnsureArrayCapacity(ref s.uvs1,     s.vertices.count + extraVerts);
            EnsureArrayCapacity(ref s.colors,   s.vertices.count + extraVerts);
            EnsureArrayCapacity(ref s.triangles,s.triangles.count + extraTris);

            if (s.hasUvs2) EnsureArrayCapacity(ref s.uvs2, s.vertices.count + extraVerts);
            if (s.hasUvs3) EnsureArrayCapacity(ref s.uvs3, s.vertices.count + extraVerts);
        }

        private static void EnsureSlotUvBuffer(ref SlotBuffers s, int channel, int requiredCount)
        {
            ref var buf = ref channel == 3 ? ref s.uvs3 : ref s.uvs2;
            if (buf.data == null)
                buf.Rent(Math.Max(requiredCount, 16));
            else if (buf.data.Length < requiredCount)
                EnsureArrayCapacity(ref buf, requiredCount);

            if (buf.count < requiredCount)
                buf.count = requiredCount;
        }

        private static void EnsureArrayCapacity<T>(ref PooledBuffer<T> buf, int required)
        {
            if (buf.data == null)
            {
                buf.Rent(Math.Max(required, 16));
                return;
            }
            if (buf.data.Length >= required) return;

            var newCap = Math.Max(required, buf.data.Length * 2);
            var newData = UniTextArrayPool<T>.Rent(newCap);
            if (buf.count > 0)
                Array.Copy(buf.data, newData, buf.count);
            UniTextArrayPool<T>.Return(buf.data);
            buf.data = newData;
        }

        private void OnCollectSubMeshes(List<UniTextRenderData> results)
        {
            EmitSlot(results, Slot.Text, ref textSlot);
            EmitSlot(results, Slot.Emoji, ref emojiSlot);
        }

        private void EmitSlot(List<UniTextRenderData> results, Slot slot, ref SlotBuffers s)
        {
            if (s.VertexCount == 0) return;

            var material = GetMaterialForSlot(slot);
            if (material == null) return;

            results.Add(new UniTextRenderData
            {
                fontId           = slot == Slot.Emoji ? EmojiFont.FontId : 0,
                materialOverride = material,
                atlasOverride    = GetAtlasForSlot(slot),
                order            = GetRenderOrder(),
                sortIndex        = GetSortIndex(),

                vertices       = s.vertices.data,
                uvs0           = s.uvs0.data,
                uvs1           = s.uvs1.data,
                uvs2           = s.hasUvs2 ? s.uvs2.data : null,
                uvs3           = s.hasUvs3 ? s.uvs3.data : null,
                colors         = s.colors.data,
                triangles      = s.triangles.data,
                vertexOffset   = 0,
                vertexCount    = s.vertices.count,
                triangleOffset = 0,
                triangleCount  = s.triangles.count,
                hasUv1         = true,
                hasUv2         = s.hasUvs2,
                hasUv3         = s.hasUvs3,
            });
        }
    }
}
