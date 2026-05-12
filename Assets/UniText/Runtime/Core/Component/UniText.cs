using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace LightSide
{
    /// <summary>
    /// Canvas text rendering component with full Unicode support.
    /// Uses CanvasRenderer for rendering within Unity UI Canvas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// UniText is a drop-in replacement for Unity's Text/TextMeshPro with proper support for:
    /// <list type="bullet">
    /// <item>Bidirectional text (Arabic, Hebrew) via UAX #9</item>
    /// <item>Complex script shaping (Devanagari, Thai, etc.) via HarfBuzz</item>
    /// <item>Proper line breaking via UAX #14</item>
    /// <item>Color emoji rendering</item>
    /// <item>Extensible markup system via <see cref="IParseRule"/> (HTML tags, Markdown, custom markers)</item>
    /// </list>
    /// </para>
    /// <para>
    /// For world-space text without Canvas, use <see cref="UniTextWorld"/> instead.
    /// </para>
    /// </remarks>
    [AddComponentMenu("UI (Canvas)/UniText")]
    [RequireComponent(typeof(CanvasRenderer))]
    public partial class UniText : UniTextBase
    {
        #region Canvas State

        /// <summary>Cached sub-mesh renderer data to avoid GetComponent calls.</summary>
        private struct SubMeshRenderer
        {
            public CanvasRenderer renderer;
            public RectTransform rectTransform;
        }

        private readonly List<SubMeshRenderer> subMeshRenderers = new();

        private struct StencilPair
        {
            public Material stencil;
            public GlyphAtlas atlas;
        }

        private readonly List<StencilPair> stencilPairs = new();

        private Rect cachedClipRect;
        private bool cachedValidClip;
        private Vector4 cachedClipSoftness;
        private int cachedStencilDepth;
        private bool stencilDepthDirty = true;
        private Vector2 lastSyncedPivot;

        private RenderMode cachedCanvasRenderMode;

        #endregion

        #region Public API

        /// <summary>Gets all canvas renderers used for sub-meshes.</summary>
        public IEnumerable<CanvasRenderer> CanvasRenderers
        {
            get
            {
                for (var i = 0; i < subMeshRenderers.Count; i++)
                    yield return subMeshRenderers[i].renderer;
            }
        }

        #endregion

        #region Abstract Implementations

        protected override void UpdateRendering()
        {
#if UNITY_EDITOR
            if (sceneVisibilityHidden) return;
#endif
            UniTextDebug.BeginSample("UniText.UpdateRendering");

            if (renderData == null || renderData.Count == 0)
            {
                ClearAllRenderers();
                UniTextDebug.EndSample();
                return;
            }

            UpdateSubMeshes();

            UniTextDebug.EndSample();
        }

        protected override void ClearAllRenderers()
        {
            for (var i = 0; i < subMeshRenderers.Count; i++)
            {
                var r = subMeshRenderers[i].renderer;
                if (r != null) r.Clear();
            }
        }

        protected override void OnSetDirty(UniTextDirtyFlags flags)
        {
            if ((flags & (UniTextDirtyFlags.FullRebuild | UniTextDirtyFlags.LayoutRebuild)) != 0)
            {
                LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
            }
        }

        protected override void OnDeInit()
        {
            ReleaseSubMeshStencilMaterials();
        }

        #endregion

        #region Lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();
            CollectExistingSubMeshRenderers();
            cachedCanvasRenderMode = canvas != null ? canvas.renderMode : UnityEngine.RenderMode.ScreenSpaceOverlay;
            EnsureAnimationHandler();
        }

        protected override void Sub()
        {
            base.Sub();
            GlyphAtlas.AnyAtlasTextureChanged += OnAtlasTextureChanged;
        }

        protected override void UnSub()
        {
            base.UnSub();
            GlyphAtlas.AnyAtlasTextureChanged -= OnAtlasTextureChanged;
        }

        private void OnAtlasTextureChanged(Texture _)
        {
            for (var i = 0; i < stencilPairs.Count; i++)
            {
                var pair = stencilPairs[i];
                if (pair.stencil == null) continue;

                var atlasTexture = pair.atlas != null ? pair.atlas.AtlasTexture : null;
                if (pair.stencil.mainTexture != atlasTexture)
                    pair.stencil.mainTexture = atlasTexture;
            }
        }

        private static void EnsureCanvasShaderChannels(Canvas c)
        {
            const AdditionalCanvasShaderChannels required =
                AdditionalCanvasShaderChannels.TexCoord1 |
                AdditionalCanvasShaderChannels.TexCoord2 |
                AdditionalCanvasShaderChannels.TexCoord3 |
                AdditionalCanvasShaderChannels.Normal;

            var current = c.additionalShaderChannels;
            var missing = required & ~current;
            if (missing != 0)
                c.additionalShaderChannels = current | missing;
        }

        private bool syncingCanvasColor;
        private int crossFadeStartFrame;
        private Color lastCanvasRendererColor = Color.white;

        public override void CrossFadeColor(Color targetColor, float duration, bool ignoreTimeScale, bool useAlpha)
        {
            base.CrossFadeColor(targetColor, duration, ignoreTimeScale, useAlpha);
            syncingCanvasColor = true;
            crossFadeStartFrame = Time.frameCount;
        }

        public override void CrossFadeAlpha(float alpha, float duration, bool ignoreTimeScale)
        {
            base.CrossFadeAlpha(alpha, duration, ignoreTimeScale);
            syncingCanvasColor = true;
            crossFadeStartFrame = Time.frameCount;
        }

        protected override void Update()
        {
            base.Update();

            if (syncingCanvasColor)
            {
                var crColor = canvasRenderer.GetColor();
                if (crColor != lastCanvasRendererColor)
                {
                    lastCanvasRendererColor = crColor;
                    for (var i = 0; i < subMeshRenderers.Count; i++)
                    {
                        var r = subMeshRenderers[i].renderer;
                        if (r != null) r.SetColor(crColor);
                    }
                }
                else if (Time.frameCount > crossFadeStartFrame + 1)
                {
                    syncingCanvasColor = false;
                }
            }

            var c = canvas;

            if (c != null)
            {
                EnsureCanvasShaderChannels(c);

                var mode = c.renderMode;
                if (mode != cachedCanvasRenderMode)
                {
                    cachedCanvasRenderMode = mode;
                    SetDirty(UniTextDirtyFlags.Alignment);
                }
            }
        }

        #endregion

        #region Canvas Masking

        /// <summary>Sets the clipping rectangle for masking, applying to all sub-mesh renderers.</summary>
        public override void SetClipRect(Rect clipRect, bool validRect)
        {
            base.SetClipRect(clipRect, validRect);
            cachedClipRect = clipRect;
            cachedValidClip = validRect;

            for (var i = 0; i < subMeshRenderers.Count; i++)
            {
                var r = subMeshRenderers[i].renderer;
                if (r == null) continue;
                if (validRect) r.EnableRectClipping(clipRect);
                else
                {
                    r.DisableRectClipping();
                    r.cull = false;
                }
            }
        }

        /// <summary>Sets soft clipping edges for smooth mask transitions on all sub-mesh renderers.</summary>
        public override void SetClipSoftness(Vector2 clipSoftness)
        {
            base.SetClipSoftness(clipSoftness);
            cachedClipSoftness = new Vector4(clipSoftness.x, clipSoftness.y, 0, 0);

            for (var i = 0; i < subMeshRenderers.Count; i++)
            {
                var r = subMeshRenderers[i].renderer;
                if (r != null) r.clippingSoftness = cachedClipSoftness;
            }
        }

        /// <summary>Applies visibility culling to all sub-mesh renderers based on clip rect.</summary>
        public override void Cull(Rect clipRect, bool validRect)
        {
            base.Cull(clipRect, validRect);
            var cull = canvasRenderer != null && canvasRenderer.cull;

            for (var i = 0; i < subMeshRenderers.Count; i++)
            {
                var r = subMeshRenderers[i].renderer;
                if (r != null) r.cull = cull;
            }
        }

        /// <summary>Recalculates stencil masking, releasing cached stencil materials.</summary>
        public override void RecalculateMasking()
        {
            base.RecalculateMasking();
            stencilDepthDirty = true;
            ReleaseSubMeshStencilMaterials();
            SetDirty(UniTextDirtyFlags.Material);
        }

        #endregion

        #region Sub-mesh Management

        private void CollectExistingSubMeshRenderers()
        {
            subMeshRenderers.Clear();
            for (var i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith("-_UTSM_-"))
                {
                    var r = child.GetComponent<CanvasRenderer>();
                    var rt = child.GetComponent<RectTransform>();
                    if (r != null) subMeshRenderers.Add(new SubMeshRenderer { renderer = r, rectTransform = rt });
                }
            }
        }

        private void UpdateSubMeshes()
        {
            UniTextDebug.BeginSample("UniText.UpdateSubMeshes");

            var existingCount = subMeshRenderers.Count;

            var currentPivot = rectTransform.pivot;
            if (currentPivot != lastSyncedPivot)
            {
                lastSyncedPivot = currentPivot;
                for (var i = 0; i < existingCount; i++)
                    subMeshRenderers[i].rectTransform.pivot = currentPivot;
            }

            if (stencilDepthDirty)
            {
                cachedStencilDepth = 0;
                if (maskable)
                {
                    var rootCanvas = MaskUtilities.FindRootSortOverrideCanvas(transform);
                    cachedStencilDepth = MaskUtilities.GetStencilDepth(transform, rootCanvas);
                }
                stencilDepthDirty = false;
            }
            var stencilDepth = cachedStencilDepth;

            var gen = meshGenerator;
            var segmentCount = renderData.Count;

            for (var i = segmentCount; i < existingCount; i++)
            {
                var r = subMeshRenderers[i].renderer;
                if (r != null) { r.Clear(); r.gameObject.SetActive(false); }
            }

            var isMsdf = gen.RenderMode == UniTextRenderMode.MSDF;
            var sdfMat = isMsdf ? UniTextMaterialCache.Msdf : UniTextMaterialCache.Sdf;
            var textAtlas = GlyphAtlas.GetInstance(gen.RenderMode);
            var emojiAtlas = GlyphAtlas.Emoji;

            for (var i = 0; i < segmentCount; i++)
            {
                var data = renderData[i];
                Material mat;
                GlyphAtlas atlas;
                if (data.materialOverride != null)
                {
                    mat = data.materialOverride;
                    atlas = data.atlasOverride;
                }
                else
                {
                    mat = data.fontId == EmojiFont.FontId ? EmojiFont.Material : sdfMat;
                    atlas = data.fontId == EmojiFont.FontId ? emojiAtlas : textAtlas;
                }

                var mesh = BuildMeshForSegment(i, in data);
                AssignCanvasRenderer(i, mesh, mat, atlas, stencilDepth);
            }

            UniTextDebug.EndSample();
        }

        private static Mesh BuildMeshForSegment(int segmentIndex, in UniTextRenderData data)
        {
            var mesh = SharedMeshes.Get(segmentIndex);
            mesh.Clear(false);
            if (data.vertexCount == 0 || data.triangleCount == 0) return mesh;

            mesh.SetVertices(data.vertices, data.vertexOffset, data.vertexCount);
            mesh.SetUVs(0, data.uvs0, data.vertexOffset, data.vertexCount);
            if (data.hasUv1 && data.uvs1 != null)
                mesh.SetUVs(1, data.uvs1, data.vertexOffset, data.vertexCount);
            if (data.hasUv2 && data.uvs2 != null)
                mesh.SetUVs(2, data.uvs2, data.vertexOffset, data.vertexCount);
            if (data.hasUv3 && data.uvs3 != null)
                mesh.SetUVs(3, data.uvs3, data.vertexOffset, data.vertexCount);
            mesh.SetColors(data.colors, data.vertexOffset, data.vertexCount);
            mesh.SetTriangles(data.triangles, data.triangleOffset, data.triangleCount, 0);
            return mesh;
        }

        private void AssignCanvasRenderer(int crIndex, Mesh mesh, Material mat, GlyphAtlas atlas, int stencilDepth)
        {
            var existingCount = subMeshRenderers.Count;
            if (crIndex < existingCount)
            {
                var r = subMeshRenderers[crIndex].renderer;
                if (r != null)
                {
                    if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                    SetSubMeshRendererData(r, mesh, mat, atlas, crIndex, stencilDepth);
                    return;
                }
            }

            var newR = CreateSubMeshRenderer(crIndex, mesh, mat, atlas, stencilDepth);
            if (crIndex < existingCount) subMeshRenderers[crIndex] = newR;
            else subMeshRenderers.Add(newR);
        }

        private void SetSubMeshRendererData(CanvasRenderer r, Mesh mesh, Material mat, GlyphAtlas atlas, int crIndex, int stencilDepth)
        {
            if (mesh == null || mesh.vertexCount == 0) { r.Clear(); return; }

            r.SetMesh(mesh);

            if (mat == null)
            {
                r.materialCount = 0;
                return;
            }

            r.materialCount = 1;

            var matToUse = mat;
            if (stencilDepth > 0)
            {
                var stencilId = (1 << stencilDepth) - 1;
                var stencilMat = StencilMaterial.Add(mat, stencilId, StencilOp.Keep, CompareFunction.Equal, ColorWriteMask.All, stencilId, 0);

                while (stencilPairs.Count <= crIndex) stencilPairs.Add(default);
                if (stencilPairs[crIndex].stencil != null) StencilMaterial.Remove(stencilPairs[crIndex].stencil);

                stencilPairs[crIndex] = new StencilPair { stencil = stencilMat, atlas = atlas };
                matToUse = stencilMat;
            }

            r.SetMaterial(matToUse, 0);
        }

        private SubMeshRenderer CreateSubMeshRenderer(int index, Mesh mesh, Material mat, GlyphAtlas atlas, int stencilDepth)
        {
            var go = new GameObject("-_UTSM_-") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.pivot = rectTransform.pivot;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var r = go.AddComponent<CanvasRenderer>();
            SetSubMeshRendererData(r, mesh, mat, atlas, index, stencilDepth);

            if (cachedValidClip) r.EnableRectClipping(cachedClipRect);
            r.clippingSoftness = cachedClipSoftness;
            r.cull = subMeshRenderers.Count > 0 && subMeshRenderers[0].renderer != null && subMeshRenderers[0].renderer.cull;

            return new SubMeshRenderer { renderer = r, rectTransform = rt };
        }

        #endregion

        #region Cleanup

        private void ReleaseSubMeshStencilMaterials()
        {
            for (var i = 0; i < stencilPairs.Count; i++)
            {
                if (stencilPairs[i].stencil != null)
                    StencilMaterial.Remove(stencilPairs[i].stencil);
            }
            stencilPairs.Clear();
        }

        #endregion
    }
}
