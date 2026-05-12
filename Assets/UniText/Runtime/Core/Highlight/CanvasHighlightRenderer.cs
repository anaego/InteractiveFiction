using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Canvas-backed <see cref="TextHighlightRenderer"/> — wraps a child GameObject with
    /// <see cref="RangeHighlightGraphic"/> anchored to the owning <see cref="UniText"/>.
    /// Z-order is managed via sibling index (above = last, behind = first).
    /// </summary>
    public class CanvasHighlightRenderer : TextHighlightRenderer
    {
        private readonly HighlightOrder order;
        private GameObject go;
        private RangeHighlightGraphic graphic;

        public CanvasHighlightRenderer(UniText owner, string name, HighlightOrder order)
        {
            this.order = order;

            go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(owner.transform, false);
            if (order == HighlightOrder.Behind) go.transform.SetAsFirstSibling();
            else go.transform.SetAsLastSibling();

            var ownerRT = owner.rectTransform;
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = ownerRT.pivot;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            graphic = go.AddComponent<RangeHighlightGraphic>();
            graphic.color = Color.clear;
        }

        public override Color Color
        {
            get => graphic != null ? graphic.color : Color.clear;
            set { if (graphic != null) graphic.color = value; }
        }

        public override void SetRects(IReadOnlyList<Rect> rects)
        {
            if (graphic == null) return;
            graphic.SetRects(rects);
            if (order == HighlightOrder.Above && go != null)
                go.transform.SetAsLastSibling();
        }

        public override void Clear()
        {
            if (graphic != null) graphic.Clear();
        }

        public override void Destroy()
        {
            if (go != null) ObjectUtils.SafeDestroy(go);
            go = null;
            graphic = null;
        }
    }
}
