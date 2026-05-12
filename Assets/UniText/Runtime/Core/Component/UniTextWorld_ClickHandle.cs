using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide
{
    /// <summary>
    /// UniTextWorld partial class wiring pointer interaction into the world-space event pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The event system delivers <see cref="PointerEventData"/> with the raycaster's camera
    /// populated. That camera is all <see cref="RectTransformUtility.ScreenPointToLocalPointInRectangle"/>
    /// needs to map a screen hit to the component's local rect — which then feeds the shared
    /// hit-test path in <see cref="UniTextBase"/>.
    /// </para>
    /// <para>
    /// For pointer events to reach this component, attach <see cref="UniTextWorldRaycaster"/>
    /// to the rendering camera. It performs ray/plane/rect math directly against
    /// <see cref="UniTextWorld.Active"/> instances — no collider required.
    /// </para>
    /// </remarks>
    public partial class UniTextWorld
    {
        /// <inheritdoc/>
        protected override Camera ResolveEventCamera(PointerEventData eventData) =>
            eventData.enterEventCamera != null ? eventData.enterEventCamera : eventData.pressEventCamera;
    }
}
