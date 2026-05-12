using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace LightSide
{
    /// <summary>
    /// Controls the rendering order of a sub-mesh relative to the base text pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Canvas renders children in sibling order: later siblings appear visually on top.
    /// <see cref="CollectRenderData"/> sorts <see cref="UniTextRenderData"/> segments by
    /// (<see cref="order"/>, <see cref="UniTextRenderData.sortIndex"/>) so that base text always sits
    /// between underlay and overlay passes.
    /// </para>
    /// </remarks>
    public enum RenderOrder : byte
    {
        /// <summary>Rendered before the base text (behind it).</summary>
        Under = 0,
        /// <summary>The base text pass (SDF or emoji).</summary>
        Base = 1,
        /// <summary>Rendered after the base text (in front of it).</summary>
        Over = 2,
    }

    /// <summary>
    /// Layering of an <see cref="EffectModifier"/> duplicate quad relative to the face within the
    /// SDF mesh. Effect modifiers read <see cref="UniTextMeshGenerator.currentEffectPass"/> when a
    /// quad is enqueued and route its triangles into the matching pass buffer.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><see cref="PreFace"/> — triangles are prepended to the index buffer so the face draws
    /// on top of the duplicate (standard outline / shadow / extrude behavior under glyphs).</item>
    /// <item><see cref="PostFace"/> — triangles are appended after the face so the duplicate draws
    /// on top of the face (used by overlay decoration lines whose effects must sit above text).</item>
    /// </list>
    /// </remarks>
    public enum EffectPass : byte
    {
        PreFace = 0,
        PostFace = 1,
    }

    /// <summary>
    /// Raw geometry slice describing a single text/emoji/sub-mesh render segment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Holds <b>references</b> to pooled vertex/UV/color/index arrays plus offset+count into them.
    /// The consumer (canvas: <see cref="UniText.UpdateSubMeshes"/>; world:
    /// <see cref="UniTextWorldBatcher"/>) does whatever it needs with this data — either uploads into
    /// a reusable <see cref="Mesh"/> for <c>CanvasRenderer</c>, or copies into its combined-mesh
    /// buffers for world-space batching.
    /// </para>
    /// <para>
    /// Array references are valid <b>only until the next collect cycle</b> on the same generator —
    /// pooled buffers can be returned or regrown. Consumers must read/copy immediately and not retain
    /// references across frames.
    /// </para>
    /// </remarks>
    public struct UniTextRenderData
    {
        /// <summary>The font identifier this render data belongs to.</summary>
        public int fontId;

        /// <summary>
        /// Optional custom material. When <see langword="null"/>, the renderer uses the default
        /// SDF/MSDF or emoji material for this <see cref="fontId"/>.
        /// </summary>
        public Material materialOverride;

        /// <summary>
        /// Optional atlas override for <see cref="materialOverride"/>. When <see langword="null"/>,
        /// the renderer uses the default atlas for this <see cref="fontId"/>.
        /// </summary>
        public GlyphAtlas atlasOverride;

        /// <summary>Render pass this segment belongs to (sorts relative to base text).</summary>
        public RenderOrder order;

        /// <summary>Stable sort key within the same <see cref="order"/> group (lower renders first).</summary>
        public int sortIndex;

        /// <summary>Vertex positions array (pooled). Read <c>[vertexOffset, vertexOffset+vertexCount)</c>.</summary>
        public Vector3[] vertices;
        /// <summary>UV0 array (pooled). Must always be valid when <see cref="vertexCount"/> &gt; 0.</summary>
        public Vector4[] uvs0;
        /// <summary>UV1 array (pooled). <see langword="null"/> or ignored when <see cref="hasUv1"/> is false.</summary>
        public Vector4[] uvs1;
        /// <summary>UV2 array (pooled). <see langword="null"/> or ignored when <see cref="hasUv2"/> is false.</summary>
        public Vector4[] uvs2;
        /// <summary>UV3 array (pooled). <see langword="null"/> or ignored when <see cref="hasUv3"/> is false.</summary>
        public Vector4[] uvs3;
        /// <summary>Vertex colors array (pooled).</summary>
        public Color32[] colors;
        /// <summary>Triangle indices array (pooled). Indices are relative to the <see cref="vertices"/> array
        /// start (not to <see cref="vertexOffset"/>). Read <c>[triangleOffset, triangleOffset+triangleCount)</c>.</summary>
        public int[] triangles;

        /// <summary>Start index in <see cref="vertices"/>/<see cref="uvs0"/>/<see cref="colors"/> etc.</summary>
        public int vertexOffset;
        /// <summary>Number of vertices in this segment (starting at <see cref="vertexOffset"/>).</summary>
        public int vertexCount;
        /// <summary>Start index in <see cref="triangles"/>.</summary>
        public int triangleOffset;
        /// <summary>Number of triangle indices in this segment.</summary>
        public int triangleCount;

        /// <summary>When <see langword="true"/>, <see cref="uvs1"/> is valid and should be uploaded.</summary>
        public bool hasUv1;
        /// <summary>When <see langword="true"/>, <see cref="uvs2"/> is valid and should be uploaded.</summary>
        public bool hasUv2;
        /// <summary>When <see langword="true"/>, <see cref="uvs3"/> is valid and should be uploaded.</summary>
        public bool hasUv3;
    }


    /// <summary>
    /// Converts positioned glyphs into Unity mesh data for text rendering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the final stage of the text processing pipeline. It takes <see cref="PositionedGlyph"/>
    /// data from <see cref="TextProcessor"/> and generates vertex, UV, color, and triangle data
    /// suitable for Unity's mesh system.
    /// </para>
    /// <para>
    /// Key features:
    /// <list type="bullet">
    /// <item>Groups glyphs by rendering target to minimize draw calls: one segment per font (Texture2DArray atlas)</item>
    /// <item>Uses pooled buffers from <see cref="UniTextArrayPool{T}"/> for zero allocations</item>
    /// <item>Provides callbacks for text modifiers to inject custom processing</item>
    /// </list>
    /// </para>
    /// <para>
    /// Typical usage:
    /// <code>
    /// generator.SetRectOffset(rect);
    /// generator.GenerateMeshDataOnly(positionedGlyphs);
    /// var renderData = generator.CollectRenderData();
    /// // Use renderData to render each segment
    /// generator.ReturnInstanceBuffers();
    /// </code>
    /// </para>
    /// </remarks>
    /// <seealso cref="TextProcessor"/>
    /// <seealso cref="PositionedGlyph"/>
    /// <seealso cref="UniTextRenderData"/>
    public class UniTextMeshGenerator
    {
        /// <summary>
        /// Base UV-space padding (normalized by glyph height) allocated around every face quad.
        /// Face and effect modifiers that expand the quad must subtract this baseline from their
        /// requested extent when computing the expansion delta.
        /// </summary>
        public const float DefaultSdfPadding = 0.02f;

        /// <summary>The cluster index of the glyph currently being processed.</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback. Maps back to codepoint indices.</remarks>
        public int currentCluster;

        /// <summary>Height of the current glyph including padding.</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback.</remarks>
        public float height;

        /// <summary>Y coordinate of the text baseline for the current glyph.</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback.</remarks>
        public float baselineY;

        /// <summary>X coordinate of the cursor position (pen position) for the current glyph.</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback. Use as pivot for per-glyph scaling
        /// so that bearing and width scale proportionally with per-cluster advance changes.</remarks>
        public float cursorX;

        /// <summary>Current font scale factor (FontSize / font.UnitsPerEm).</summary>
        public float scale;

        /// <summary>FontSize * FontScale — converts normalized glyph metrics to UI-space units. Constant per font.</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback. Use for fixed-pixel-size effects in the Base shader
        /// where glyphH cancels out (outline dilate, shadow dilate/softness).</remarks>
        public float fontMetricFactor;

        /// <summary>Default vertex color applied to all glyphs.</summary>
        public Color32 defaultColor;

        /// <summary>Atlas padding in pixels from font settings.</summary>
        public float paddingPixels;

        /// <summary>Padding in font units.</summary>
        public float padding;

        /// <summary>Double padding for width/height calculations.</summary>
        public float padding2;

        /// <summary>Inverse atlas size for UV calculations: 1 / atlasSize.</summary>
        public float invAtlasSize;

        /// <summary>Current font being processed.</summary>
        /// <remarks>Valid during mesh generation for the current font segment.</remarks>
        public UniTextFont font;

        /// <summary>X offset from the rect origin.</summary>
        public float offsetX;

        /// <summary>Y offset from the rect origin.</summary>
        public float offsetY;

        /// <summary>Current number of vertices in the mesh buffers.</summary>
        public int vertexCount;

        /// <summary>Current number of triangle indices in the mesh buffers.</summary>
        public int triangleCount;

        /// <summary>
        /// Index of the first vertex of the face quad of the glyph currently being processed.
        /// Stable across all <see cref="onGlyph"/> invocations for a single glyph, even when
        /// modifiers append additional geometry that grows <see cref="vertexCount"/>.
        /// </summary>
        public int faceBaseIdx;


        private PooledBuffer<Vector3> vertices;
        private PooledBuffer<Vector4> uvs0;
        private PooledBuffer<Vector4> uvs1;
        private PooledBuffer<Vector4> uvs2;
        private PooledBuffer<Vector4> uvs3;
        private PooledBuffer<Color32> colors;
        private PooledBuffer<int> triangles;
        private bool hasGeneratedData;
        private int currentSegmentVertexStart;

        /// <summary>Number of vertices in the SDF segment.</summary>
        public int SdfVertexCount => sdfVertexCount;

        /// <summary>Number of triangle indices in the SDF segment.</summary>
        public int SdfTriangleCount => sdfTriangleCount;

        /// <summary>Number of vertices in the emoji segment.</summary>
        public int EmojiVertexCount => emojiVertexCount;

        /// <summary>Number of triangle indices in the emoji segment.</summary>
        public int EmojiTriangleCount => emojiTriangleCount;

        private int sdfVertexCount;
        private int sdfTriangleCount;
        private int sdfFontId;
        private int emojiVertexCount;
        private int emojiTriangleCount;
        private int emojiFontId;

        /// <summary>Glyph atlas keys used in the last mesh generation (for reference counting).</summary>
        internal PooledBuffer<long> usedGlyphKeys;
        internal PooledBuffer<long> usedEmojiKeys;

        [ThreadStatic] private static FastLongDictionary<GlyphAtlas.GlyphEntry> glyphEntryCache;

        [ThreadStatic] private static PooledBuffer<int> sharedPreFaceTris;
        [ThreadStatic] private static int sharedPreFaceTriCount;
        [ThreadStatic] private static PooledBuffer<int> sharedPostFaceTris;
        [ThreadStatic] private static int sharedPostFaceTriCount;

        /// <summary>
        /// Looks up a glyph entry from the per-frame cache. Returns true if found (repeated glyph).
        /// On miss, the caller should look up the atlas and call <see cref="CacheGlyphEntry"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetCachedGlyphEntry(long glyphKey, out GlyphAtlas.GlyphEntry entry)
        {
            return glyphEntryCache.TryGetValue(glyphKey, out entry);
        }

        /// <summary>
        /// Stores a glyph entry in the cache and tracks the key for atlas ref counting.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CacheGlyphEntry(long glyphKey, in GlyphAtlas.GlyphEntry entry)
        {
            glyphEntryCache.AddOrUpdate(glyphKey, entry);
            usedGlyphKeys.Add(glyphKey);
        }

        /// <summary>
        /// Tracks a glyph key for atlas ref counting. Deduplicates automatically.
        /// Use for modifier glyphs that don't need cached entry data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TrackGlyphKey(long glyphKey)
        {
            if (glyphEntryCache.ContainsKey(glyphKey))
                return false;
            glyphEntryCache.AddOrUpdate(glyphKey, default);
            usedGlyphKeys.Add(glyphKey);
            return true;
        }

        /// <summary>Starting vertex index for the current segment. Used to compute relative triangle indices.</summary>
        public int CurrentSegmentVertexStart => currentSegmentVertexStart;

        private readonly UniTextFontProvider fontProvider;
        private readonly UniTextBuffers buf;

        /// <summary>
        /// Invoked after the main glyph loop completes, before any finalization phase.
        /// Subscribers may emit additional quads into the open vertex stream; if they call
        /// <see cref="onGlyph"/> for each emitted quad with <see cref="isVirtualGlyph"/> set,
        /// per-glyph modifiers (color, gradient, bold) and effect modifiers (outline, shadow,
        /// extrude) pick up the new quads through the standard pipeline.
        /// </summary>
        /// <remarks>
        /// Used by decoration modifiers (underline, strikethrough, kashida) to add their
        /// geometry while staying within the same effect/color/etc pipeline as primary glyphs.
        /// </remarks>
        public Action onMainPassComplete;

        /// <summary>
        /// Finalization phase for the main glyph pass (SDF). Runs after <see cref="onMainPassComplete"/>
        /// and before the emoji segment is processed.
        /// </summary>
        /// <remarks>
        /// Effect modifiers flush queued effect requests here, appending duplicate quads (outline,
        /// shadow, extrude) into the SDF segment. Subscribers that emit additional geometry MUST
        /// emit it before the emoji segment runs — appending after the emoji block would corrupt
        /// the emoji vertex/triangle ranges.
        /// </remarks>
        public Action onMainPassFinalize;

        /// <summary>Invoked for each glyph during mesh generation.</summary>
        /// <remarks>
        /// Primary callback for text modifiers to apply per-glyph effects.
        /// Access current glyph data via the public state fields on this generator instance.
        /// </remarks>
        public Action onGlyph;

        /// <summary>Invoked after all mesh generation is complete.</summary>
        public Action onRebuildEnd;

        /// <summary>Invoked before mesh generation starts.</summary>
        public Action onRebuildStart;

        /// <summary>
        /// Invoked by <see cref="CollectRenderData"/> after base SDF/emoji segments are written to the
        /// result buffer, but before the buffer is sorted and returned. Subscribers append their own
        /// <see cref="UniTextRenderData"/> entries (each with a custom <see cref="UniTextRenderData.materialOverride"/>,
        /// <see cref="UniTextRenderData.atlasOverride"/>, <see cref="UniTextRenderData.order"/> and
        /// <see cref="UniTextRenderData.sortIndex"/>) to the provided list.
        /// </summary>
        /// <remarks>
        /// After all subscribers run, the result buffer is stable-sorted by
        /// (<see cref="UniTextRenderData.order"/>, <see cref="UniTextRenderData.sortIndex"/>), which
        /// determines sibling order of <c>-_UTSM_-</c> renderers in <see cref="UniText.UpdateSubMeshes"/>.
        /// </remarks>
        public Action<List<UniTextRenderData>> onCollectSubMeshes;

        /// <summary>
        /// Maximum UV-space padding requested for the current glyph by any modifier.
        /// Reset to 0 before each <see cref="onGlyph"/> invocation. Subscribers accumulate via max.
        /// Read after <see cref="onGlyph"/> to decide atlas band upgrades.
        /// </summary>
        public float currentMaxGlyphExtent;

        /// <summary>
        /// True when the currently processed glyph is virtual (injected by a modifier — list marker,
        /// ellipsis dot — and has no <see cref="ShapedGlyph"/> behind it).
        /// </summary>
        /// <remarks>
        /// Modifiers that drive their behavior from shaping data (truncation flags, super/sub
        /// position) must early-out on virtual glyphs to avoid acting on cluster indices that
        /// belong to source text rather than the injected decoration.
        /// </remarks>
        public bool isVirtualGlyph;

        /// <summary>
        /// Determines where <see cref="EffectModifier"/> duplicate quads (outline, shadow, extrude)
        /// land in the index buffer for the currently processed glyph.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Default is <see cref="EffectPass.PreFace"/> — duplicates are prepended before the face,
        /// so the face perfectly covers them in its center and only the dilated rim shows through.
        /// </para>
        /// <para>
        /// Decoration modifiers (overlay underline / strikethrough) that need their effects to sit
        /// <em>above</em> text temporarily set this to <see cref="EffectPass.PostFace"/> while
        /// emitting the line through <see cref="LineRenderHelper.DrawLine"/>; effect modifiers
        /// consult this flag at <c>EnqueueEffectQuad</c> time, so it must be set before invoking
        /// the per-glyph callback and restored afterwards.
        /// </para>
        /// </remarks>
        public EffectPass currentEffectPass;

        /// <summary>
        /// Triangle-buffer index where <see cref="EffectPass.PostFace"/> triangles are inserted
        /// during finalization. Negative means "append to the tail".
        /// </summary>
        /// <remarks>
        /// Decoration modifiers that emit overlay lines latch this to <c>triangleCount</c> immediately
        /// before adding their first line, so post-face effect triangles are inserted between the
        /// face block and the line block. This makes the line perfectly cover its own outline in the
        /// center (same painter relationship as <see cref="EffectPass.PreFace"/> has with the face),
        /// while still keeping the line itself above face text.
        /// </remarks>
        public int postFaceInsertPoint;

        internal struct BandUpgradeRequest
        {
            public long glyphKey;
            public uint glyphIndex;
            public int requiredBandPx;
            public UniTextFont font;
            public long varHash48;
            public int[] ftCoords;
            public UniTextRenderMode mode;
        }

        internal readonly List<BandUpgradeRequest> bandUpgradeRequests = new();

        private Rect rectOffset;

        /// <summary>
        /// Initializes a new instance of the <see cref="UniTextMeshGenerator"/> class.
        /// </summary>
        /// <param name="fontProvider">The font provider for accessing font assets and materials.</param>
        /// <param name="uniTextBuffers">The shared buffer container from text processing.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="fontProvider"/> or <paramref name="uniTextBuffers"/> is <see langword="null"/>.
        /// </exception>
        public UniTextMeshGenerator(UniTextFontProvider fontProvider, UniTextBuffers uniTextBuffers)
        {
            this.fontProvider = fontProvider ?? throw new ArgumentNullException(nameof(fontProvider));
            buf = uniTextBuffers ?? throw new ArgumentNullException(nameof(uniTextBuffers));
        }

        /// <summary>Gets or sets the font size in points for mesh generation.</summary>
        public float FontSize { get; set; } = 36f;

        /// <summary>Gets or sets the atlas mode (SDF or MSDF) for glyph lookup and material selection.</summary>
        public UniTextRenderMode RenderMode { get; set; }

        /// <summary>Gets a value indicating whether mesh data has been generated and is available.</summary>
        public bool HasGeneratedData => hasGeneratedData;

        /// <summary>Gets the vertex position buffer (X, Y, Z coordinates).</summary>
        public Vector3[] Vertices => vertices.data;

        /// <summary>
        /// Scales a glyph quad (4 vertices) around the cursor position and baseline.
        /// Used by SizeModifier, SmallCapsModifier, ScriptPositionModifier.
        /// </summary>
        /// <param name="pivotX">Cursor position (pen X) — bearing and width scale proportionally from this pivot.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ScaleGlyphQuad(Vector3[] verts, int baseIdx, float pivotX, float baselineY, float scale, float yOffset = 0f)
        {
            var pivotY = baselineY + yOffset;
            for (var i = 0; i < 4; i++)
            {
                ref var v = ref verts[baseIdx + i];
                v.x = pivotX + (v.x - pivotX) * scale;
                v.y = pivotY + (v.y - baselineY) * scale;
            }
        }

        /// <summary>Gets the primary UV buffer (texture coordinates and scale in W component).</summary>
        public Vector4[] Uvs0 => uvs0.data;

        /// <summary>Gets the vertex color buffer.</summary>
        public Color32[] Colors => colors.data;

        /// <summary>Gets the triangle index buffer.</summary>
        public int[] Triangles => triangles.data;

        /// <summary>Gets the UV1 buffer: x = aspect (glyphW/glyphH), y = face dilate.</summary>
        /// <summary>Gets the UV1 buffer: x = aspect (glyphW/glyphH), y = faceDilate,
        /// z = per-glyph cluster index (monotonic per-line, transform-invariant),
        /// w = intra-glyph X fraction (0 on the left edge, 1 on the right — interpolated by GPU).</summary>
        public Vector4[] Uvs1 => uvs1.data;

        /// <summary>Gets the UV2 buffer containing layer 2 (underlay/shadow) parameters.</summary>
        /// <remarks>
        /// Layout: x = dilate, y = color (packed Color32), z = (free), w = softness.
        /// Shadow offset is applied via mesh vertex displacement, not UV.
        /// Not allocated by default. Call <see cref="EnsureUvBuffer"/> to allocate before writing.
        /// </remarks>
        public Vector4[] Uvs2 => uvs2.data;

        /// <summary>Gets the UV3 buffer containing layer 3 (underlay/shadow) parameters.</summary>
        /// <remarks>
        /// Layout: x = dilate, y = color (packed Color32), z = (free), w = softness.
        /// Shadow offset is applied via mesh vertex displacement, not UV.
        /// Not allocated by default. Call <see cref="EnsureUvBuffer"/> to allocate before writing.
        /// </remarks>
        public Vector4[] Uvs3 => uvs3.data;

        /// <summary>
        /// Allocates and zero-clears a UV effect buffer (channel 2 or 3) if not already allocated.
        /// </summary>
        /// <param name="channel">UV channel: 2 or 3.</param>
        public void EnsureUvBuffer(int channel)
        {
            ref var buf = ref (channel == 3 ? ref uvs3 : ref uvs2);
            if (buf.data != null) return;
            buf.Rent(vertices.Capacity);
            Array.Clear(buf.data, 0, buf.data.Length);
            buf.count = vertexCount;
        }

        #region Instance Buffer Management

        private void RentInstanceBuffers(int estimatedVertices, int estimatedTriangles)
        {
            vertices.Rent(estimatedVertices);
            uvs0.Rent(estimatedVertices);
            uvs1.Rent(estimatedVertices);
            colors.Rent(estimatedVertices);
            triangles.Rent(estimatedTriangles);
        }

        /// <summary>
        /// Returns all instance buffers to the pool and clears the generated data flag.
        /// </summary>
        /// <remarks>
        /// Must be called after mesh generation is complete and data has been applied to Unity meshes.
        /// Failing to call this method will result in buffer leaks.
        /// </remarks>
        public void ReturnInstanceBuffers()
        {
            vertices.Return();
            uvs0.Return();
            uvs1.Return();
            uvs2.Return();
            uvs3.Return();
            colors.Return();
            triangles.Return();
            hasGeneratedData = false;
        }

        /// <summary>
        /// Releases all pooled resources. Call when the generator is no longer needed.
        /// </summary>
        public void Dispose()
        {
            ReturnInstanceBuffers();
            usedGlyphKeys.Return();
            usedEmojiKeys.Return();
        }

        /// <summary>
        /// Ensures the vertex and triangle buffers have capacity for additional data.
        /// </summary>
        /// <param name="additionalVertices">Number of additional vertices needed.</param>
        /// <param name="additionalTriangles">Number of additional triangle indices needed.</param>
        /// <remarks>
        /// Called by text modifiers when they need to add geometry beyond the base glyph quads.
        /// Automatically grows buffers using the pooled array system if needed.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCapacity(int additionalVertices, int additionalTriangles)
        {
            var requiredVertices = vertexCount + additionalVertices;
            var requiredTriangles = triangleCount + additionalTriangles;

            if (requiredVertices > vertices.Capacity)
                GrowVertexBuffers(requiredVertices);

            if (requiredTriangles > triangles.Capacity)
                GrowTriangleBuffer(requiredTriangles);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowVertexBuffers(int required)
        {
            var newCapacity = Math.Max(required, vertices.Capacity * 2);
            var currentCount = vertexCount;

            GrowBuffer(ref vertices, newCapacity, currentCount);
            GrowBuffer(ref uvs0, newCapacity, currentCount);
            GrowBuffer(ref uvs1, newCapacity, currentCount);
            if (uvs2.data != null)
            {
                GrowBuffer(ref uvs2, newCapacity, currentCount);
                Array.Clear(uvs2.data, currentCount, uvs2.data.Length - currentCount);
            }
            if (uvs3.data != null)
            {
                GrowBuffer(ref uvs3, newCapacity, currentCount);
                Array.Clear(uvs3.data, currentCount, uvs3.data.Length - currentCount);
            }
            GrowBuffer(ref colors, newCapacity, currentCount);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowTriangleBuffer(int required)
        {
            var newCapacity = Math.Max(required, triangles.Capacity * 2);
            GrowBuffer(ref triangles, newCapacity, triangleCount);
        }

        private static void GrowBuffer<T>(ref PooledBuffer<T> buffer, int newCapacity, int currentCount)
        {
            var oldData = buffer.data;

            var newData = UniTextArrayPool<T>.Rent(newCapacity);
            if (oldData != null && currentCount > 0)
                oldData.AsSpan(0, currentCount).CopyTo(newData);

            UniTextArrayPool<T>.Return(oldData);
            buffer.data = newData;
        }

        #endregion

        #region Shared Effect Triangles

        /// <summary>
        /// Queues the 6 triangle indices for an effect duplicate quad starting at
        /// <paramref name="destBaseIdx"/> into the shared pre- or post-face buffer.
        /// </summary>
        /// <remarks>
        /// Called by <see cref="EffectModifier.AppendSharedEffectQuad"/> after writing the quad's
        /// vertices. The buffers are flushed once at the end of <see cref="GenerateMeshDataOnly"/>,
        /// producing the final painter order
        /// <c>[pre-face 1..N, face, post-face 1..N, line]</c> regardless of how many
        /// <see cref="EffectModifier"/> instances are active.
        /// </remarks>
        public void QueueEffectTriangle(EffectPass pass, int destBaseIdx)
        {
            if (pass == EffectPass.PostFace)
            {
                EnsurePassTriCapacity(ref sharedPostFaceTris, sharedPostFaceTriCount, sharedPostFaceTriCount + 6);
                var tris = sharedPostFaceTris.data;
                tris[sharedPostFaceTriCount]     = destBaseIdx;
                tris[sharedPostFaceTriCount + 1] = destBaseIdx + 1;
                tris[sharedPostFaceTriCount + 2] = destBaseIdx + 2;
                tris[sharedPostFaceTriCount + 3] = destBaseIdx + 2;
                tris[sharedPostFaceTriCount + 4] = destBaseIdx + 3;
                tris[sharedPostFaceTriCount + 5] = destBaseIdx;
                sharedPostFaceTriCount += 6;
            }
            else
            {
                EnsurePassTriCapacity(ref sharedPreFaceTris, sharedPreFaceTriCount, sharedPreFaceTriCount + 6);
                var tris = sharedPreFaceTris.data;
                tris[sharedPreFaceTriCount]     = destBaseIdx;
                tris[sharedPreFaceTriCount + 1] = destBaseIdx + 1;
                tris[sharedPreFaceTriCount + 2] = destBaseIdx + 2;
                tris[sharedPreFaceTriCount + 3] = destBaseIdx + 2;
                tris[sharedPreFaceTriCount + 4] = destBaseIdx + 3;
                tris[sharedPreFaceTriCount + 5] = destBaseIdx;
                sharedPreFaceTriCount += 6;
            }
        }

        private static void EnsurePassTriCapacity(ref PooledBuffer<int> buf, int filled, int required)
        {
            if (buf.data == null)
            {
                buf.Rent(Math.Max(required, 64));
                return;
            }
            if (required <= buf.Capacity) return;

            var newCap = Math.Max(required, buf.Capacity * 2);
            var oldData = buf.data;
            var newData = UniTextArrayPool<int>.Rent(newCap);
            if (filled > 0)
                Array.Copy(oldData, 0, newData, 0, filled);
            UniTextArrayPool<int>.Return(oldData);
            buf.data = newData;
        }

        /// <summary>
        /// Flushes the shared pre/post effect triangle buffers into the index buffer in one shot.
        /// Post tris are inserted at <see cref="postFaceInsertPoint"/> (or appended when no insert
        /// point was latched); pre tris are then prepended. Resets both counts to 0.
        /// </summary>
        private void FlushSharedEffectTriangles()
        {
            var postCount = sharedPostFaceTriCount;
            if (postCount > 0)
            {
                var existingTris = triangleCount;
                EnsureCapacity(0, postCount);
                var trisArr = triangles.data;
                var insertAt = postFaceInsertPoint;
                if ((uint)insertAt > (uint)existingTris) insertAt = existingTris;
                if (insertAt < existingTris)
                    Array.Copy(trisArr, insertAt, trisArr, insertAt + postCount, existingTris - insertAt);
                Array.Copy(sharedPostFaceTris.data, 0, trisArr, insertAt, postCount);
                triangleCount = existingTris + postCount;
                sharedPostFaceTriCount = 0;
            }

            var preCount = sharedPreFaceTriCount;
            if (preCount > 0)
            {
                var existingTris = triangleCount;
                EnsureCapacity(0, preCount);
                var trisArr = triangles.data;
                Array.Copy(trisArr, 0, trisArr, preCount, existingTris);
                Array.Copy(sharedPreFaceTris.data, 0, trisArr, 0, preCount);
                triangleCount = existingTris + preCount;
                sharedPreFaceTriCount = 0;
            }
        }

        #endregion

        /// <summary>
        /// Requests an atlas band upgrade for the current glyph if any effect modifier asked for
        /// SDF padding wider than the current tile's computed band. Re-rasterizes the glyph at a
        /// larger tile so subsequent frames have room for the requested outline / shadow extent.
        /// </summary>
        /// <remarks>
        /// Must be called immediately after <see cref="onGlyph"/> for any quad whose outline /
        /// shadow / extrude effects should be allowed to grow beyond the default SDF padding.
        /// Reads <see cref="currentMaxGlyphExtent"/> (effect modifiers write into it during onGlyph)
        /// and the face dilate stored in <see cref="Uvs1"/>[<see cref="faceBaseIdx"/>].y.
        /// </remarks>
        public void RequestBandUpgradeIfNeeded(long glyphKey, uint glyphIndex, in GlyphAtlas.GlyphEntry entry,
            UniTextFont font, long varHash48, int[] ftCoords, float glyphH, float aspect)
        {
            if (glyphH < 1e-6f) return;

            var faceDilate = uvs1.data[faceBaseIdx].y;
            var padGlyph = GlyphAtlas.Pad / glyphH;
            var facePad = faceDilate * padGlyph;
            var requiredPad = facePad > currentMaxGlyphExtent ? facePad : currentMaxGlyphExtent;
            if (requiredPad <= DefaultSdfPadding) return;

            var effectExtent = requiredPad < padGlyph ? requiredPad : padGlyph;
            var atlas = GlyphAtlas.GetInstance(RenderMode);
            var tileSize = atlas.TileSizeFromEncoded(entry.encodedTile);
            var totalExt = (aspect > 1f ? aspect : 1f) + 2f * padGlyph;
            var requiredBandPx = (int)Math.Ceiling(effectExtent * tileSize / totalExt);
            if (requiredBandPx <= entry.computedBandPx) return;

            bandUpgradeRequests.Add(new BandUpgradeRequest
            {
                glyphKey = glyphKey,
                glyphIndex = glyphIndex,
                requiredBandPx = requiredBandPx,
                font = font,
                varHash48 = varHash48,
                ftCoords = ftCoords,
                mode = RenderMode
            });
        }

        #region Quad Modification API

        /// <summary>
        /// Expands a 4-vertex glyph quad outward on all sides by <paramref name="delta"/>
        /// (UV-space). Updates both positions and UV0 consistently so the atlas sample stays aligned.
        /// </summary>
        /// <param name="baseIdx">Index of the first vertex of the quad.</param>
        /// <param name="delta">UV-space padding to add on each side (normalized by glyph height).</param>
        /// <remarks>
        /// The position delta is computed as <c>delta * glyphH * fontMetricFactor</c>,
        /// using the glyph height stored in <c>Uvs0[baseIdx].w</c>.
        /// Caller is responsible for deciding whether expansion is needed.
        /// </remarks>
        public void ExpandQuad(int baseIdx, float delta)
        {
            if (delta <= 0f) return;

            var verts = vertices.data;
            var glyphH = uvs0.data[baseIdx].w;
            var deltaPixels = delta * glyphH * fontMetricFactor;

            verts[baseIdx].x -= deltaPixels;
            verts[baseIdx].y -= deltaPixels;
            verts[baseIdx + 1].x -= deltaPixels;
            verts[baseIdx + 1].y += deltaPixels;
            verts[baseIdx + 2].x += deltaPixels;
            verts[baseIdx + 2].y += deltaPixels;
            verts[baseIdx + 3].x += deltaPixels;
            verts[baseIdx + 3].y -= deltaPixels;

            var uvData = uvs0.data;
            uvData[baseIdx].x -= delta;
            uvData[baseIdx].y -= delta;
            uvData[baseIdx + 1].x -= delta;
            uvData[baseIdx + 1].y += delta;
            uvData[baseIdx + 2].x += delta;
            uvData[baseIdx + 2].y += delta;
            uvData[baseIdx + 3].x += delta;
            uvData[baseIdx + 3].y -= delta;
        }

        #endregion

        /// <summary>
        /// Sets the layout rectangle for text positioning.
        /// </summary>
        /// <param name="rect">The rect defining the text layout bounds.</param>
        public void SetRectOffset(Rect rect)
        {
            rectOffset = rect;
        }

        #region Parallel Mesh Generation

        /// <summary>
        /// Generates mesh data (vertices, UVs, colors, triangles) from positioned glyphs.
        /// Groups by rendering target: SDF fonts in one segment (Texture2DArray), emoji separately.
        /// </summary>
        public void GenerateMeshDataOnly(ReadOnlySpan<PositionedGlyph> glyphs, ReadOnlySpan<PositionedGlyph> virtualGlyphs)
        {
            currentEffectPass = EffectPass.PreFace;
            postFaceInsertPoint = -1;
            sharedPreFaceTriCount = 0;
            sharedPostFaceTriCount = 0;
            onRebuildStart?.Invoke();
            var glyphLen = glyphs.Length + virtualGlyphs.Length;
            usedGlyphKeys.FakeClear();
            usedGlyphKeys.EnsureCapacity(glyphLen);
            usedEmojiKeys.FakeClear();
            usedEmojiKeys.EnsureCapacity(glyphLen);
            bandUpgradeRequests.Clear();

            glyphEntryCache ??= new FastLongDictionary<GlyphAtlas.GlyphEntry>(512);
            glyphEntryCache.ClearFast();
            var estimatedVertices = glyphLen * 4;
            var estimatedTriangles = glyphLen * 6;

            RentInstanceBuffers(estimatedVertices, estimatedTriangles);

            var allGlyphs = UniTextArrayPool<PositionedGlyph>.Rent(glyphLen);
            glyphs.CopyTo(allGlyphs);
            if (virtualGlyphs.Length > 0)
                virtualGlyphs.CopyTo(allGlyphs.AsSpan(glyphs.Length));

            var offX = rectOffset.xMin;
            var offY = rectOffset.yMax;
            offsetX = offX;
            offsetY = offY;
            vertexCount = 0;
            triangleCount = 0;
            currentSegmentVertexStart = 0;

            PooledList<int> emojiGlyphList = null;
            var atlas = GlyphAtlas.GetInstance(RenderMode);
            var skippedGlyphs = 0;
            var lastSdfFontId = int.MinValue;

            var lastFontId = int.MinValue;
            UniTextFont lastFont = null;
            long lastVarHash = 0;
            int[] lastFtCoords = null;
            var lastIsEmoji = false;

            float upem = 0, metricsFactor = 0;
            var glyphColor = defaultColor;

            var verts = vertices.data;
            var uvData = uvs0.data;
            var uv1Data = uvs1.data;
            var cols = colors.data;
            var tris = triangles.data;

            for (var i = 0; i < glyphLen; i++)
            {
                ref var glyph = ref allGlyphs[i];
                var glyphFontId = glyph.fontId;

                if (glyphFontId != lastFontId)
                {
                    lastFontId = glyphFontId;
                    lastFont = fontProvider.GetFontAsset(glyphFontId);
                    if (buf.variationMap != null &&
                        buf.variationMap.TryGetValue(glyphFontId, out var varRunInfo))
                    {
                        lastVarHash = varRunInfo.varHash48;
                        lastFtCoords = varRunInfo.ftCoords;
                    }
                    else
                    {
                        lastVarHash = lastFont.DefaultVarHash48;
                        lastFtCoords = null;
                    }
                    lastIsEmoji = lastFont is EmojiFont;

                    if (!lastIsEmoji)
                    {
                        font = lastFont;

                        upem = lastFont.UnitsPerEm;
                        var fontScaleMul = lastFont.FontScale;
                        scale = FontSize * fontScaleMul / upem;
                        metricsFactor = FontSize * fontScaleMul;
                        fontMetricFactor = metricsFactor;

                        glyphColor = lastFont.IsColor
                            ? new Color32(255, 255, 255, defaultColor.a)
                            : defaultColor;
                    }
                }

                if (lastIsEmoji)
                {
                    emojiGlyphList ??= SharedPipelineComponents.AcquireGlyphIndexList(glyphLen);
                    emojiGlyphList.buffer[emojiGlyphList.buffer.count++] = i;
                    continue;
                }

                var glyphId = (uint)glyph.glyphId;
                var glyphKey = GlyphAtlas.MakeKey(lastVarHash, glyphId);

                if (!TryGetCachedGlyphEntry(glyphKey, out var entry))
                {
                    if (!atlas.TryGetEntry(glyphKey, out entry) || entry.encodedTile < 0)
                    {
                        skippedGlyphs++;
                        continue;
                    }
                    CacheGlyphEntry(glyphKey, in entry);
                }

                lastSdfFontId = glyphFontId;

                EnsureCapacity(4, 6);
                verts = vertices.data;
                uvData = uvs0.data;
                uv1Data = uvs1.data;
                cols = colors.data;
                tris = triangles.data;

                var cluster = glyph.cluster;
                var metrics = entry.metrics;

                const float sdfPadding = DefaultSdfPadding;
                var bearingXNorm = metrics.horizontalBearingX / upem;
                var bearingYNorm = metrics.horizontalBearingY / upem;
                var glyphW = metrics.width / upem;
                var glyphH = metrics.height / upem;
                var aspect = glyphH > 1e-6f ? glyphW / glyphH : 1f;

                var maxDim = MathF.Max(aspect, 1f);
                var marginX = (maxDim - aspect) * 0.5f;
                var marginY = (maxDim - 1f) * 0.5f;

                var padEmX = (marginX + sdfPadding) * glyphH;
                var padEmY = (marginY + sdfPadding) * glyphH;
                var sideScaled = (maxDim + sdfPadding * 2) * glyphH * metricsFactor;

                var bearingXScaled = (bearingXNorm - padEmX) * metricsFactor;
                var bearingYScaled = (bearingYNorm + padEmY) * metricsFactor;
                var widthScaled = sideScaled;
                var heightScaled = sideScaled;

                var tlX = offX + glyph.x + bearingXScaled;
                var tlY = offY - glyph.y + bearingYScaled;
                var blY = tlY - heightScaled;
                var trX = tlX + widthScaled;

                var uvMinX = -(marginX + sdfPadding);
                var uvMinY = -(marginY + sdfPadding);
                var uvMaxX = aspect + marginX + sdfPadding;
                var uvMaxY = 1f + marginY + sdfPadding;

                var tileIdx = (float)(entry.encodedTile + entry.pageIndex * GlyphAtlas.PageStride);

                var i0 = vertexCount;
                var i1 = vertexCount + 1;
                var i2 = vertexCount + 2;
                var i3 = vertexCount + 3;

                ref var v0 = ref verts[i0];
                v0.x = tlX; v0.y = blY; v0.z = 0;
                ref var v1 = ref verts[i1];
                v1.x = tlX; v1.y = tlY; v1.z = 0;
                ref var v2 = ref verts[i2];
                v2.x = trX; v2.y = tlY; v2.z = 0;
                ref var v3 = ref verts[i3];
                v3.x = trX; v3.y = blY; v3.z = 0;

                ref var uv0 = ref uvData[i0];
                uv0.x = uvMinX; uv0.y = uvMinY; uv0.z = tileIdx; uv0.w = glyphH;
                ref var uv1 = ref uvData[i1];
                uv1.x = uvMinX; uv1.y = uvMaxY; uv1.z = tileIdx; uv1.w = glyphH;
                ref var uv2 = ref uvData[i2];
                uv2.x = uvMaxX; uv2.y = uvMaxY; uv2.z = tileIdx; uv2.w = glyphH;
                ref var uv3 = ref uvData[i3];
                uv3.x = uvMaxX; uv3.y = uvMinY; uv3.z = tileIdx; uv3.w = glyphH;

                cols[i0] = glyphColor;
                cols[i1] = glyphColor;
                cols[i2] = glyphColor;
                cols[i3] = glyphColor;

                uv1Data[i0] = new Vector4(aspect, 0, cluster, 0);
                uv1Data[i1] = new Vector4(aspect, 0, cluster, 0);
                uv1Data[i2] = new Vector4(aspect, 0, cluster, 1);
                uv1Data[i3] = new Vector4(aspect, 0, cluster, 1);

                var localI0 = i0 - currentSegmentVertexStart;
                tris[triangleCount] = localI0;
                tris[triangleCount + 1] = localI0 + 1;
                tris[triangleCount + 2] = localI0 + 2;
                tris[triangleCount + 3] = localI0 + 2;
                tris[triangleCount + 4] = localI0 + 3;
                tris[triangleCount + 5] = localI0;

                currentCluster = cluster;
                height = heightScaled;
                baselineY = offY - glyph.y;
                cursorX = offX + glyph.x;

                vertexCount += 4;
                triangleCount += 6;

                faceBaseIdx = vertexCount - 4;
                currentMaxGlyphExtent = 0f;
                isVirtualGlyph = glyph.shapedGlyphIndex < 0;
                onGlyph?.Invoke();

                RequestBandUpgradeIfNeeded(glyphKey, glyphId, in entry, lastFont, lastVarHash, lastFtCoords, glyphH, aspect);

                verts = vertices.data;
                uvData = uvs0.data;
                uv1Data = uvs1.data;
                cols = colors.data;
                tris = triangles.data;
            }

            onMainPassComplete?.Invoke();

            onMainPassFinalize?.Invoke();
            FlushSharedEffectTriangles();

            if (skippedGlyphs > 0)
                Cat.MeowFormat("[MeshGenerator] SKIPPED {0} glyphs (not in atlas)", skippedGlyphs);

            if (vertexCount > 0)
                sdfFontId = lastSdfFontId;

            sdfVertexCount = vertexCount;
            sdfTriangleCount = triangleCount;

            if (emojiGlyphList != null)
            {
                ref var firstEmojiGlyph = ref allGlyphs[emojiGlyphList[0]];
                var emojiId = firstEmojiGlyph.fontId;
                var fontAsset = fontProvider.GetFontAsset(emojiId);

                currentSegmentVertexStart = vertexCount;
                GenerateEmojiSegment(emojiGlyphList, allGlyphs, fontAsset);
                emojiFontId = emojiId;

                SharedPipelineComponents.ReleaseGlyphIndexList(emojiGlyphList);
            }

            emojiVertexCount = vertexCount - sdfVertexCount;
            emojiTriangleCount = triangleCount - sdfTriangleCount;

            vertices.count = vertexCount;
            uvs0.count = vertexCount;
            uvs1.count = vertexCount;
            if (uvs2.data != null) uvs2.count = vertexCount;
            if (uvs3.data != null) uvs3.count = vertexCount;
            colors.count = vertexCount;
            triangles.count = triangleCount;

            UniTextArrayPool<PositionedGlyph>.Return(allGlyphs);

            buf.hasValidGlyphCache = true;
            hasGeneratedData = true;

            Cat.MeowFormat("[MeshGenerator] Generated: {0} verts, {1} tris, sdf={2}+emoji={3}",
                vertices.count, triangles.count, sdfVertexCount, emojiVertexCount);

            onRebuildEnd?.Invoke();
        }

        private void GenerateEmojiSegment(PooledList<int> glyphIndices, PositionedGlyph[] positionedGlyphs, UniTextFont font)
        {
            var glyphCount = glyphIndices.Count;
            var emojiFont = (EmojiFont)font;
            var emojiVarHash = GlyphAtlas.DefaultVarHash(font.FontDataHash);

            var upem = font.UnitsPerEm;
            var fontScaleMul = font.FontScale;
            var scaleVal = FontSize * fontScaleMul / upem;
            var atlasSizeVal = emojiFont.AtlasSize;

            var paddingPixelsVal = font.AtlasPadding;
            var paddingVal = (float)paddingPixelsVal;
            var padding2Val = paddingVal * 2;

            var invAtlasSizeVal = 1f / atlasSizeVal;

            var offX = rectOffset.xMin;
            var offY = rectOffset.yMax;

            scale = scaleVal;
            offsetX = offX;
            offsetY = offY;
            this.font = font;
            paddingPixels = paddingPixelsVal;
            padding = paddingVal;
            padding2 = padding2Val;
            invAtlasSize = invAtlasSizeVal;

            EnsureCapacity(glyphCount * 4, glyphCount * 6);

            var isColorFont = font.IsColor;
            var glyphColor = isColorFont
                ? new Color32(255, 255, 255, defaultColor.a)
                : defaultColor;

            buf.glyphDataCache.EnsureCapacity(buf.shapedGlyphs.count);
            var glyphCache = buf.glyphDataCache.data;
            var useCache = buf.hasValidGlyphCache;

            var verts = vertices.data;
            var uvData = uvs0.data;
            var cols = colors.data;
            var tris = triangles.data;

            var skippedGlyphs = 0;
            var zeroRectGlyphs = 0;

            for (var i = 0; i < glyphCount; i++)
            {
                var glyphIndex = glyphIndices[i];
                ref var glyph = ref positionedGlyphs[glyphIndex];
                var cacheIndex = glyph.shapedGlyphIndex;

                ref var cachedData = ref glyphCache[cacheIndex];
                var emojiGlyphId = (uint)glyph.glyphId;
                var emojiKey = GlyphAtlas.MakeKey(emojiVarHash, emojiGlyphId);
                usedEmojiKeys.Add(emojiKey);

                if (!useCache || !cachedData.isValid)
                {
                    var emojiAtlas = GlyphAtlas.Emoji;
                    if (emojiAtlas == null || !emojiAtlas.TryGetEntry(emojiKey, out var entry) || entry.encodedTile < 0)
                    {
                        skippedGlyphs++;
                        cachedData.isValid = false;
                        continue;
                    }

                    int tileSize = emojiAtlas.TileSizeFromEncoded(entry.encodedTile);
                    emojiAtlas.DecodeTileXY(entry.encodedTile, tileSize, out int tileX, out int tileY);
                    int g = emojiAtlas.TileGutter;
                    var metrics = entry.metrics;
                    cachedData.rectX = tileX + g;
                    cachedData.rectY = tileY + g;
                    cachedData.rectWidth = entry.pixelWidth;
                    cachedData.rectHeight = entry.pixelHeight;
                    cachedData.bearingX = metrics.horizontalBearingX;
                    cachedData.bearingY = metrics.horizontalBearingY;
                    cachedData.width = metrics.width;
                    cachedData.height = metrics.height;
                    cachedData.atlasIndex = entry.pageIndex;
                    cachedData.isValid = true;
                }

                if (cachedData.rectWidth == 0 || cachedData.rectHeight == 0)
                {
                    zeroRectGlyphs++;
                    continue;
                }

                var cluster = glyph.cluster;

                var bearingXScaled = (cachedData.bearingX - padding) * scale;
                var bearingYScaled = (cachedData.bearingY + padding) * scale;
                var heightScaled = (cachedData.height + padding2) * scale;
                var widthScaled = (cachedData.width + padding2) * scale;

                var tlX = offX + glyph.x + bearingXScaled;
                var tlY = offY - glyph.y + bearingYScaled;
                var blY = tlY - heightScaled;
                var trX = tlX + widthScaled;

                var uvBLx = (cachedData.rectX - paddingPixels) * invAtlasSize;
                var uvBLy = (cachedData.rectY - paddingPixels) * invAtlasSize;
                var uvTLy = (cachedData.rectY + cachedData.rectHeight + paddingPixels) * invAtlasSize;
                var uvTRx = (cachedData.rectX + cachedData.rectWidth + paddingPixels) * invAtlasSize;

                var i0 = vertexCount;
                var i1 = vertexCount + 1;
                var i2 = vertexCount + 2;
                var i3 = vertexCount + 3;

                ref var v0 = ref verts[i0];
                v0.x = tlX; v0.y = blY; v0.z = 0;
                ref var v1 = ref verts[i1];
                v1.x = tlX; v1.y = tlY; v1.z = 0;
                ref var v2 = ref verts[i2];
                v2.x = trX; v2.y = tlY; v2.z = 0;
                ref var v3 = ref verts[i3];
                v3.x = trX; v3.y = blY; v3.z = 0;

                var layerZ = (float)cachedData.atlasIndex;

                ref var uv0 = ref uvData[i0];
                uv0.x = uvBLx; uv0.y = uvBLy; uv0.z = layerZ; uv0.w = 0;
                ref var uv1 = ref uvData[i1];
                uv1.x = uvBLx; uv1.y = uvTLy; uv1.z = layerZ; uv1.w = 0;
                ref var uv2 = ref uvData[i2];
                uv2.x = uvTRx; uv2.y = uvTLy; uv2.z = layerZ; uv2.w = 0;
                ref var uv3 = ref uvData[i3];
                uv3.x = uvTRx; uv3.y = uvBLy; uv3.z = layerZ; uv3.w = 0;

                cols[i0] = glyphColor;
                cols[i1] = glyphColor;
                cols[i2] = glyphColor;
                cols[i3] = glyphColor;

                var localI0 = i0 - currentSegmentVertexStart;
                tris[triangleCount] = localI0;
                tris[triangleCount + 1] = localI0 + 1;
                tris[triangleCount + 2] = localI0 + 2;
                tris[triangleCount + 3] = localI0 + 2;
                tris[triangleCount + 4] = localI0 + 3;
                tris[triangleCount + 5] = localI0;

                currentCluster = cluster;
                height = heightScaled;
                baselineY = offY - glyph.y;
                cursorX = offX + glyph.x;

                vertexCount += 4;
                triangleCount += 6;

                faceBaseIdx = i0;
                isVirtualGlyph = glyph.shapedGlyphIndex < 0;
                onGlyph?.Invoke();

                verts = vertices.data;
                uvData = uvs0.data;
                cols = colors.data;
                tris = triangles.data;
            }

            if (skippedGlyphs > 0)
                Cat.MeowFormat("[GenerateEmojiSegment] {0}: SKIPPED {1} glyphs", font.CachedName, skippedGlyphs);
            if (zeroRectGlyphs > 0)
                Cat.MeowFormat("[GenerateEmojiSegment] {0}: ZERO RECT {1} glyphs", font.CachedName, zeroRectGlyphs);
        }


        /// <summary>
        /// Collects raw render data (vertex/UV/triangle array slices + material/order metadata) for every
        /// segment produced by the latest mesh generation. Does <b>not</b> build Unity <see cref="Mesh"/>
        /// objects — consumers (canvas <c>UpdateSubMeshes</c>, world batcher) decide what to do with
        /// the raw data.
        /// </summary>
        /// <returns>
        /// Shared list of <see cref="UniTextRenderData"/> entries — base SDF and/or emoji segments plus
        /// any sub-meshes appended by <see cref="onCollectSubMeshes"/> subscribers. The list is reused
        /// on the next call; consumers must use/copy immediately.
        /// </returns>
        /// <remarks>
        /// <para>
        /// SDF and emoji live in the same pooled buffers (<see cref="Vertices"/>, <see cref="Uvs0"/>,
        /// <see cref="Colors"/>, <see cref="Triangles"/>) as contiguous ranges:
        /// SDF in <c>[0, sdfVertexCount)</c>, emoji in <c>[sdfVertexCount, sdfVertexCount+emojiVertexCount)</c>.
        /// Entries carry <c>vertexOffset</c> and <c>vertexCount</c> so consumers can read the right slice.
        /// </para>
        /// <para>
        /// Array references remain valid only until the next <see cref="GenerateMeshDataOnly"/> /
        /// <see cref="CollectRenderData"/> cycle on the same generator (pooled buffers may be regrown
        /// or returned). Consumers must not retain references across frames.
        /// </para>
        /// </remarks>
        public List<UniTextRenderData> CollectRenderData()
        {
            var resultBuffer = SharedPipelineComponents.MeshResultBuffer;
            resultBuffer.Clear();

            if (!hasGeneratedData)
                return resultBuffer;

            if (sdfVertexCount > 0)
            {
                resultBuffer.Add(new UniTextRenderData
                {
                    fontId         = sdfFontId,
                    order          = RenderOrder.Base,
                    vertices       = vertices.data,
                    uvs0           = uvs0.data,
                    uvs1           = uvs1.data,
                    uvs2           = uvs2.data,
                    uvs3           = uvs3.data,
                    colors         = colors.data,
                    triangles      = triangles.data,
                    vertexOffset   = 0,
                    vertexCount    = sdfVertexCount,
                    triangleOffset = 0,
                    triangleCount  = sdfTriangleCount,
                    hasUv1         = true,
                    hasUv2         = uvs2.data != null,
                    hasUv3         = uvs3.data != null,
                });
            }

            if (emojiVertexCount > 0)
            {
                resultBuffer.Add(new UniTextRenderData
                {
                    fontId         = emojiFontId,
                    order          = RenderOrder.Base,
                    vertices       = vertices.data,
                    uvs0           = uvs0.data,
                    uvs1           = null,
                    uvs2           = null,
                    uvs3           = null,
                    colors         = colors.data,
                    triangles      = triangles.data,
                    vertexOffset   = sdfVertexCount,
                    vertexCount    = emojiVertexCount,
                    triangleOffset = sdfTriangleCount,
                    triangleCount  = emojiTriangleCount,
                    hasUv1         = false,
                    hasUv2         = false,
                    hasUv3         = false,
                });
            }

            onCollectSubMeshes?.Invoke(resultBuffer);
            StableSortByOrder(resultBuffer);

            return resultBuffer;
        }

        /// <summary>
        /// In-place stable insertion sort of <paramref name="list"/> by
        /// (<see cref="UniTextRenderData.order"/>, <see cref="UniTextRenderData.sortIndex"/>) ascending.
        /// Typical list size is 1–5 entries (SDF + optional emoji + a few sub-mesh providers), so
        /// insertion sort is the right choice: zero allocations, cache-friendly, and minimal overhead on
        /// already-ordered input (the common case where no sub-mesh providers are registered).
        /// </summary>
        private static void StableSortByOrder(List<UniTextRenderData> list)
        {
            var n = list.Count;
            if (n < 2) return;
            for (var i = 1; i < n; i++)
            {
                var x = list[i];
                var j = i - 1;
                while (j >= 0)
                {
                    var y = list[j];
                    if ((int)y.order < (int)x.order) break;
                    if ((int)y.order == (int)x.order && y.sortIndex <= x.sortIndex) break;
                    list[j + 1] = y;
                    j--;
                }
                list[j + 1] = x;
            }
        }

        /// <summary>
        /// Ensures atlas subscription for auto-material management.
        /// Must be called on the main thread (creates materials lazily).
        /// </summary>
        public void AssignAutoMaterials()
        {
            if (!hasGeneratedData) return;

            bool isMsdf = RenderMode == UniTextRenderMode.MSDF;

            if (isMsdf)
                UniTextMaterialCache.EnsureMsdfAtlasSubscription();
            else
                UniTextMaterialCache.EnsureAtlasSubscription();
        }

        #endregion
    }

}
