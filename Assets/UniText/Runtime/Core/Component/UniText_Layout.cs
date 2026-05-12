using UnityEngine.UI;

namespace LightSide
{
    /// <summary>
    /// UniText partial class integrating with Unity UI layout system.
    /// </summary>
    /// <remarks>
    /// Thin shim over <see cref="UniTextBase.EnsureLayoutComputed"/>. Both
    /// <see cref="UnityEngine.UI.ILayoutElement"/> and <see cref="UnityEngine.UI.ILayoutController"/>
    /// entry points delegate to the same unified layout method; repeated calls in one cycle hit
    /// the internal cache.
    /// </remarks>
    public partial class UniText : ILayoutElement, ILayoutController
    {
        private float cachedPreferredWidth;

        #region ILayoutElement

        void ILayoutElement.CalculateLayoutInputHorizontal()
        {
            EnsureFirstPassComplete();

            UniTextDebug.BeginSample("UniText.CalculateLayoutInputHorizontal");

            cachedPreferredWidth = 0;

            if (!sourceText.IsEmpty && textProcessor != null && textProcessor.HasValidFirstPassData)
            {
                var effectiveFontSize = autoSize ? maxFontSize : fontSize;
                cachedPreferredWidth = textProcessor.GetPreferredWidth(effectiveFontSize);
            }

            UniTextDebug.EndSample();
        }

        void ILayoutElement.CalculateLayoutInputVertical()
        {
            UniTextDebug.BeginSample("UniText.CalculateLayoutInputVertical");
            EnsureLayoutComputed();
            UniTextDebug.EndSample();
        }

        float ILayoutElement.minWidth => 0;
        float ILayoutElement.preferredWidth => cachedPreferredWidth;
        float ILayoutElement.flexibleWidth => -1;

        float ILayoutElement.minHeight => 0;
        float ILayoutElement.preferredHeight => PreferredHeight;
        float ILayoutElement.flexibleHeight => -1;

        int ILayoutElement.layoutPriority => 0;

        #endregion

        #region ILayoutController

        void ILayoutController.SetLayoutHorizontal() { }

        void ILayoutController.SetLayoutVertical() => EnsureLayoutFit();

        #endregion
    }
}
