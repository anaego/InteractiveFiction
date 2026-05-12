using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Default highlighting logic — click flash with fade-out, hover tint, programmatic
    /// selection — wired to the built-in renderers (<see cref="CanvasHighlightRenderer"/>
    /// for <see cref="UniText"/>, <see cref="WorldHighlightRenderer"/> for
    /// <see cref="UniTextWorld"/>). Subclass and override either
    /// <see cref="CreateHighlightRenderer(UniText, string, HighlightOrder)"/> or
    /// <see cref="CreateHighlightRenderer(UniTextWorld, string, HighlightOrder)"/>
    /// to plug a custom visual on the chosen backend while keeping this logic.
    /// </summary>
    [Serializable]
    public class DefaultTextHighlighter : TextHighlighter
    {
        protected override TextHighlightRenderer CreateHighlightRenderer(UniText owner, string name, HighlightOrder order)
            => new CanvasHighlightRenderer(owner, name, order);

        protected override TextHighlightRenderer CreateHighlightRenderer(UniTextWorld owner, string name, HighlightOrder order)
            => new WorldHighlightRenderer(owner, name, order);

        [SerializeField]
        [Tooltip("Color of the click highlight.")]
        private Color clickColor = new(0.2f, 0.5f, 1f, 0.6f);

        [SerializeField]
        [Tooltip("Duration of the fade-out animation in seconds.")]
        private float fadeDuration = 0.25f;

        [SerializeField]
        [Tooltip("Color of the hover highlight.")]
        private Color hoverColor = new(0.2f, 0.5f, 1f, 0.1f);

        private Color selectionColor = Color.clear;

        private TextHighlightRenderer clickRenderer;
        private TextHighlightRenderer hoverRenderer;
        private TextHighlightRenderer selectionRenderer;

        private float clickAlpha;
        private Color currentClickColor;
        private readonly List<Rect> boundsCache = new(4);

        /// <summary>Gets or sets the click highlight color.</summary>
        public Color ClickColor
        {
            get => clickColor;
            set => clickColor = value;
        }

        /// <summary>Gets or sets the fade duration in seconds.</summary>
        public float FadeDuration
        {
            get => fadeDuration;
            set => fadeDuration = Mathf.Max(0.01f, value);
        }

        /// <summary>Gets or sets the hover highlight color.</summary>
        public Color HoverColor
        {
            get => hoverColor;
            set => hoverColor = value;
        }

        /// <summary>Gets or sets the selection highlight color.</summary>
        public Color SelectionColor
        {
            get => selectionColor;
            set
            {
                selectionColor = value;
                if (selectionRenderer != null) selectionRenderer.Color = value;
            }
        }

        public override void OnRangeClicked(InteractiveRange range, List<Rect> bounds)
        {
            if (bounds == null || bounds.Count == 0 || owner == null) return;

            clickRenderer ??= CreateHighlightRenderer("ClickHighlight", HighlightOrder.Above);
            clickRenderer.SetRects(bounds);
            clickAlpha = 1f;
            currentClickColor = clickColor;
            clickRenderer.Color = currentClickColor;
        }

        public override void OnRangeEntered(InteractiveRange range, List<Rect> bounds)
        {
            if (bounds == null || bounds.Count == 0 || owner == null) return;

            hoverRenderer ??= CreateHighlightRenderer("HoverHighlight", HighlightOrder.Behind);
            hoverRenderer.SetRects(bounds);
            hoverRenderer.Color = hoverColor;
        }

        public override void OnRangeExited(InteractiveRange range)
        {
            if (hoverRenderer == null) return;
            hoverRenderer.Clear();
            hoverRenderer.Color = Color.clear;
        }

        /// <summary>
        /// Sets the selection highlight to cover the specified text range.
        /// Use <see cref="SelectionColor"/> to control the color (and animate it externally).
        /// </summary>
        /// <param name="startCluster">Start of the range (cluster index, inclusive).</param>
        /// <param name="endCluster">End of the range (cluster index, exclusive).</param>
        public void SetSelection(int startCluster, int endCluster)
        {
            if (owner == null) return;

            owner.GetRangeBounds(startCluster, endCluster, boundsCache);
            if (boundsCache.Count == 0)
            {
                ClearSelection();
                return;
            }

            if (selectionRenderer == null)
            {
                selectionRenderer = CreateHighlightRenderer("SelectionHighlight", HighlightOrder.Above);
                selectionRenderer.Color = selectionColor;
            }
            selectionRenderer.SetRects(boundsCache);
        }

        /// <summary>Clears the selection highlight.</summary>
        public void ClearSelection()
        {
            if (selectionRenderer == null) return;
            selectionRenderer.Clear();
        }

        public override void Update()
        {
            if (clickAlpha <= 0f) return;

            clickAlpha -= Time.deltaTime / fadeDuration;

            if (clickAlpha <= 0f)
            {
                clickAlpha = 0f;
                if (clickRenderer != null)
                {
                    clickRenderer.Clear();
                    clickRenderer.Color = Color.clear;
                }
            }
            else if (clickRenderer != null)
            {
                currentClickColor.a = clickColor.a * clickAlpha;
                clickRenderer.Color = currentClickColor;
            }
        }

        public override void Destroy()
        {
            clickRenderer?.Destroy();
            hoverRenderer?.Destroy();
            selectionRenderer?.Destroy();
            clickRenderer = null;
            hoverRenderer = null;
            selectionRenderer = null;
            base.Destroy();
        }
    }
}
