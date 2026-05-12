using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// UniTextBase partial class implementing auto-size and layout computation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shared between Canvas (<see cref="UniText"/>) and world-space (<see cref="UniTextWorld"/>)
    /// variants. Canvas integrates through <see cref="UnityEngine.UI.ILayoutElement"/> and
    /// <see cref="UnityEngine.UI.ILayoutController"/> so Unity's <c>LayoutRebuilder</c> drives
    /// the two phases; world-space has the render pipeline invoke <see cref="EnsureLayoutFit"/>
    /// directly.
    /// </para>
    /// <para>
    /// <b>Two-phase design.</b> The phases have independent caches because they key on different
    /// inputs and are driven by different UI rebuild points:
    /// <list type="bullet">
    /// <item><see cref="EnsureLayoutComputed"/> — the main pass. Computes the "preferred"
    ///   state (<see cref="cachedPreferredHeight"/> and initial <see cref="cachedEffectiveFontSize"/>)
    ///   at the unconstrained ideal font size. Cached by <c>rect.width</c>; independent of
    ///   <c>rect.height</c>. Invoked from <c>CalcLayoutInputVertical</c> and returns what the
    ///   parent layout group reads as "what this text wants".</item>
    /// <item><see cref="EnsureLayoutFit"/> — the second pass. When auto-size is on and the actual
    ///   <c>rect.height</c> is smaller than the preferred, shrinks
    ///   <see cref="cachedEffectiveFontSize"/> to fit. Cached by <c>rect.height</c>. Deliberately
    ///   does not update <see cref="cachedPreferredHeight"/> — "preferred" stays what the text
    ///   wants, not what the rect gives it. Without this separation, shrinking preferred on one
    ///   frame would make parents (e.g. <c>ContentSizeFitter</c> + <c>VerticalLayoutGroup</c>)
    ///   assign an even smaller rect next frame, causing a feedback shrink loop.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public abstract partial class UniTextBase
    {
        #region Cached Layout State

        /// <summary>
        /// Cached effective font size after auto-sizing. Zero means layout has not been computed;
        /// consumers fall back to <c>maxFontSize</c>/<c>fontSize</c> as appropriate.
        /// </summary>
        protected float cachedEffectiveFontSize;

        /// <summary>
        /// Cached preferred height — what the text wants at its unconstrained (main-pass) size.
        /// Not updated by the second pass: parent layout groups read this as "desired height",
        /// which must stay stable even when the rect is smaller than desired.
        /// </summary>
        protected float cachedPreferredHeight;

        private float cachedMainPassWidth;
        private float cachedMainPassFontSize;
        private bool hasValidMainPass;

        private float cachedSecondPassHeight;
        private bool hasValidSecondPass;

        #endregion

        #region Public API

        /// <summary>Gets the computed preferred height for the current rect (accounts for auto-sizing).</summary>
        public float PreferredHeight => cachedPreferredHeight;

        #endregion

        #region Layout Computation

        /// <summary>
        /// Main layout pass. Computes the "preferred" state — <see cref="cachedPreferredHeight"/>
        /// and an initial <see cref="cachedEffectiveFontSize"/> — for the current rect width.
        /// Cached by width only: result is independent of <c>rect.height</c>.
        /// </summary>
        /// <remarks>
        /// Main-thread only. Canvas variant invokes this from
        /// <see cref="UnityEngine.UI.ILayoutElement.CalculateLayoutInputVertical"/>; world-space
        /// variant has it called implicitly by <see cref="EnsureLayoutFit"/>.
        /// </remarks>
        protected void EnsureLayoutComputed()
        {
            if (sourceText.IsEmpty || textProcessor == null || !textProcessor.HasValidFirstPassData)
            {
                hasValidMainPass = false;
                hasValidSecondPass = false;
                cachedPreferredHeight = 0f;
                return;
            }

            var rect = rectTransform.rect;
            if (rect.width <= 0f)
            {
                hasValidMainPass = false;
                hasValidSecondPass = false;
                cachedPreferredHeight = 0f;
                return;
            }

            if (hasValidMainPass && Mathf.Approximately(cachedMainPassWidth, rect.width))
                return;

            cachedEffectiveFontSize = GetEffectiveFontSize(rect.width, TextProcessSettings.FloatMax);
            cachedMainPassFontSize = cachedEffectiveFontSize;
            textProcessor.EnsureLines(rect.width, cachedEffectiveFontSize, wordWrap);

            var probeSize = (autoSize && wordWrap) ? maxFontSize : cachedEffectiveFontSize;
            cachedPreferredHeight = textProcessor.GetPreferredHeight(
                probeSize, 0f, overEdge, underEdge, leadingDistribution);

            cachedMainPassWidth = rect.width;
            hasValidMainPass = true;
            hasValidSecondPass = false;
        }

        /// <summary>
        /// Full layout: main pass then, if needed, shrinks <see cref="cachedEffectiveFontSize"/>
        /// under the current <c>rect.height</c>. Main-pass state comes from the width cache;
        /// second pass is cached by <c>rect.height</c> so repeat calls within a frame are free.
        /// </summary>
        /// <remarks>
        /// Main-thread only. Canvas variant invokes this from
        /// <see cref="UnityEngine.UI.ILayoutController.SetLayoutVertical"/>; world-space variant
        /// from the render pipeline before mesh generation.
        /// </remarks>
        protected void EnsureLayoutFit()
        {
            EnsureLayoutComputed();
            if (!hasValidMainPass) return;
            if (!autoSize) return;

            var rect = rectTransform.rect;
            if (rect.height <= 0f) return;
            if (rect.height >= cachedPreferredHeight - 0.01f)
            {
                cachedEffectiveFontSize = cachedMainPassFontSize;
                hasValidSecondPass = false;
                return;
            }

            if (hasValidSecondPass && Mathf.Approximately(cachedSecondPassHeight, rect.height))
                return;

            var settings = new TextProcessSettings
            {
                MaxWidth = rect.width,
                MaxHeight = rect.height,
                OverEdge = overEdge,
                UnderEdge = underEdge,
                LeadingDistribution = leadingDistribution,
                fontSize = maxFontSize,
                baseDirection = baseDirection,
                enableWordWrap = wordWrap
            };

            cachedEffectiveFontSize = textProcessor.FindOptimalFontSize(
                minFontSize, maxFontSize, rect.width, rect.height, settings);
            textProcessor.EnsureLines(rect.width, cachedEffectiveFontSize, wordWrap);

            cachedSecondPassHeight = rect.height;
            hasValidSecondPass = true;
        }

        private float GetEffectiveFontSize(float width, float height)
        {
            if (!autoSize) return fontSize;
            if (wordWrap) return maxFontSize;

            var settings = new TextProcessSettings
            {
                MaxWidth = width,
                MaxHeight = height,
                OverEdge = overEdge,
                UnderEdge = underEdge,
                fontSize = maxFontSize,
                baseDirection = baseDirection,
                enableWordWrap = false
            };

            return textProcessor.FindOptimalFontSize(
                minFontSize, maxFontSize, width, height, settings);
        }

        private void InvalidateLayoutCache()
        {
            hasValidMainPass = false;
            hasValidSecondPass = false;
        }

        #endregion
    }
}
