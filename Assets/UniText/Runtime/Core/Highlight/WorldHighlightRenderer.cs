using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightSide
{
    /// <summary>
    /// World-space <see cref="TextHighlightRenderer"/> — wraps a child GameObject with
    /// <see cref="MeshFilter"/> + <see cref="MeshRenderer"/> and builds quads from rects
    /// directly into a dynamic <see cref="Mesh"/>. One shared material
    /// (<see cref="UniTextMaterialCache.Highlight"/>) serves every instance; color travels
    /// through vertex colors.
    /// </summary>
    /// <remarks>
    /// Z-ordering against the owning text and other world renderers uses the sorting system
    /// (<c>sortingLayerID</c> + <c>sortingOrder</c>) so highlights interleave correctly with
    /// <see cref="SpriteRenderer"/>, other <see cref="UniTextWorld"/> instances and the
    /// <see cref="UniTextWorldBatcher"/> meshes on the same layer.
    /// </remarks>
    public class WorldHighlightRenderer : TextHighlightRenderer
    {
        private readonly UniTextWorld owner;
        private readonly HighlightOrder order;

        private GameObject go;
        private MeshRenderer renderer;
        private MeshFilter filter;
        private Mesh mesh;

        private readonly List<Vector3> verts = new(16);
        private readonly List<int> tris = new(24);
        private readonly List<Color32> colors = new(16);

        private Color color = Color.clear;
        private int rectsCount;

        public WorldHighlightRenderer(UniTextWorld owner, string name, HighlightOrder order)
        {
            this.owner = owner;
            this.order = order;

            go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(owner.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            filter = go.AddComponent<MeshFilter>();
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = UniTextMaterialCache.Highlight;
            renderer.receiveShadows = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            ApplySorting();

            mesh = new Mesh
            {
                name = $"UniTextHighlight_{name}",
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;

            owner.SortingChanged += OnOwnerSortingChanged;
        }

        private void ApplySorting()
        {
            if (renderer == null || owner == null) return;
            renderer.sortingLayerID = owner.SortingLayerID;
            renderer.sortingOrder = owner.SortingOrder + (order == HighlightOrder.Above ? 1 : -1);
        }

        private void OnOwnerSortingChanged(UniTextWorld _) => ApplySorting();

        public override Color Color
        {
            get => color;
            set
            {
                if (color == value) return;
                color = value;
                RefillColors();
            }
        }

        public override void SetRects(IReadOnlyList<Rect> rects)
        {
            verts.Clear();
            tris.Clear();
            colors.Clear();
            rectsCount = rects?.Count ?? 0;

            if (mesh == null) return;

            if (rectsCount == 0)
            {
                mesh.Clear();
                return;
            }

            var c32 = (Color32)color;
            for (var i = 0; i < rectsCount; i++)
            {
                var r = rects[i];
                var baseIdx = verts.Count;

                verts.Add(new Vector3(r.xMin, r.yMin, 0f));
                verts.Add(new Vector3(r.xMin, r.yMax, 0f));
                verts.Add(new Vector3(r.xMax, r.yMax, 0f));
                verts.Add(new Vector3(r.xMax, r.yMin, 0f));

                colors.Add(c32);
                colors.Add(c32);
                colors.Add(c32);
                colors.Add(c32);

                tris.Add(baseIdx);
                tris.Add(baseIdx + 1);
                tris.Add(baseIdx + 2);
                tris.Add(baseIdx + 2);
                tris.Add(baseIdx + 3);
                tris.Add(baseIdx);
            }

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        public override void Clear()
        {
            verts.Clear();
            tris.Clear();
            colors.Clear();
            rectsCount = 0;
            if (mesh != null) mesh.Clear();
        }

        public override void Destroy()
        {
            if (owner != null) owner.SortingChanged -= OnOwnerSortingChanged;
            if (mesh != null) ObjectUtils.SafeDestroy(mesh);
            if (go != null) ObjectUtils.SafeDestroy(go);
            mesh = null;
            go = null;
            renderer = null;
            filter = null;
        }

        private void RefillColors()
        {
            if (mesh == null || rectsCount == 0) return;
            var c32 = (Color32)color;
            colors.Clear();
            for (var i = 0; i < rectsCount * 4; i++)
                colors.Add(c32);
            mesh.SetColors(colors);
        }
    }
}
