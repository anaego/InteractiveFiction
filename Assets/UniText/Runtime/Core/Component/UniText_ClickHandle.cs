using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide
{
    /// <summary>
    /// UniText partial class wiring pointer interaction into Unity's Canvas event system.
    /// </summary>
    /// <remarks>
    /// Shared hit testing, pointer handlers, interactive-range logic and the component-level
    /// <see cref="TextHighlighter"/> all live in <see cref="UniTextBase"/>. This partial only
    /// supplies the Canvas-specific event camera (null for overlay canvases,
    /// <c>canvas.worldCamera</c> otherwise).
    /// </remarks>
    public partial class UniText
    {
        /// <inheritdoc/>
        protected override Camera ResolveEventCamera(PointerEventData eventData) =>
            canvas != null && canvas.renderMode != UnityEngine.RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
    }
}
