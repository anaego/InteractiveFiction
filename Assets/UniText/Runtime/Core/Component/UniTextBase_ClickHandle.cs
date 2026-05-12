using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide
{
    /// <summary>
    /// UniTextBase partial class implementing pointer interaction and hit testing
    /// shared between Canvas and world-space variants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements Unity <see cref="EventSystem"/> interfaces on the base class so both
    /// <see cref="UniText"/> (driven by <c>GraphicRaycaster</c>) and <see cref="UniTextWorld"/>
    /// (driven by <c>PhysicsRaycaster</c>/<c>Physics2DRaycaster</c> with a collider) receive the
    /// same events through the same code path.
    /// </para>
    /// <para>
    /// Conversion from screen coordinates to local <c>RectTransform</c> coordinates uses
    /// <see cref="RectTransformUtility.ScreenPointToLocalPointInRectangle"/>, which handles both
    /// Canvas and world-space rect transforms — the only variant-specific concern is picking
    /// the right event camera, exposed via <see cref="ResolveEventCamera"/>.
    /// </para>
    /// <para>
    /// Interactive ranges (links, hashtags, mentions, custom) are handled through
    /// <see cref="InteractiveRangeRegistry"/> and <see cref="IInteractiveRangeHandler"/>.
    /// </para>
    /// </remarks>
    /// <seealso cref="InteractiveRangeRegistry"/>
    /// <seealso cref="IInteractiveRangeHandler"/>
    public abstract partial class UniTextBase :
        IPointerClickHandler, IPointerDownHandler, IPointerUpHandler,
        IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        private const float DefaultMaxClickDistance = 20f;

        private TextHitResult lastHoverResult;
        private InteractiveRange lastHoverRange;
        private IInteractiveRangeProvider lastHoverProvider;
        private readonly List<Rect> highlightBoundsCache = new(4);

        #region Events

        /// <summary>Raised when any text is clicked, providing hit test details.</summary>
        public event Action<TextHitResult> TextClicked;

        /// <summary>Raised when an interactive range is clicked.</summary>
        public event Action<InteractiveRangeHit> RangeClicked;

        /// <summary>Raised when the pointer enters an interactive range (desktop only).</summary>
        public event Action<InteractiveRangeHit> RangeEntered;

        /// <summary>Raised when the pointer exits an interactive range (desktop only).</summary>
        public event Action<InteractiveRangeHit> RangeExited;

        /// <summary>Raised when hover position changes, providing hit test details (for InputField).</summary>
        public event Action<TextHitResult> HoverChanged;

        #endregion

        #region Public API

        /// <summary>Gets the last hover hit test result.</summary>
        public TextHitResult LastHoverResult => lastHoverResult;

        /// <summary>Returns true if currently hovering over an interactive range.</summary>
        public bool IsHoveringRange => lastHoverRange.IsValid;

        /// <summary>Gets the interactive range currently being hovered, if any.</summary>
        public InteractiveRange CurrentHoverRange => lastHoverRange;

        #endregion

        #region Pointer Handlers

        /// <inheritdoc/>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsOverInteractiveRange(eventData))
                PropagateToParent(eventData, ExecuteEvents.pointerDownHandler);
        }

        /// <inheritdoc/>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (!IsOverInteractiveRange(eventData))
                PropagateToParent(eventData, ExecuteEvents.pointerUpHandler);
        }

        /// <inheritdoc/>
        public void OnPointerClick(PointerEventData eventData)
        {
            var result = HitTestScreen(eventData.position, ResolveEventCamera(eventData));

            if (result.hit)
            {
                Cat.MeowFormat("[UniText] Click: cluster={0}, distance={1:F1}", result.cluster, result.distance);

                TextClicked?.Invoke(result);

                var registry = InteractiveRangeRegistry.Get(buffers);
                if (registry != null && registry.TryGetRangeAt(result.cluster, out var range, out var provider))
                {
                    Cat.MeowFormat("[UniText] Range clicked: type={0}, data={1}", range.type, range.data);

                    var rangeHit = new InteractiveRangeHit(range, result);
                    RangeClicked?.Invoke(rangeHit);

                    if (provider is IInteractiveRangeHandler handler)
                        handler.OnRangeClicked(range, result);

                    var hl = ResolveHighlighter(provider);
                    if (hl != null)
                    {
                        GetRangeBounds(range.start, range.end, highlightBoundsCache);
                        hl.OnRangeClicked(range, highlightBoundsCache);
                    }

                    return;
                }
            }

            PropagateToParent(eventData, ExecuteEvents.pointerClickHandler);
        }

        /// <inheritdoc/>
        public void OnPointerEnter(PointerEventData eventData) => UpdateHover(eventData);

        /// <inheritdoc/>
        public void OnPointerExit(PointerEventData eventData)
        {
            if (lastHoverRange.IsValid)
            {
                var rangeHit = new InteractiveRangeHit(lastHoverRange, lastHoverResult);
                RangeExited?.Invoke(rangeHit);

                if (lastHoverProvider is IInteractiveRangeHandler handler)
                    handler.OnRangeExited(lastHoverRange);

                ResolveHighlighter(lastHoverProvider)?.OnRangeExited(lastHoverRange);
            }

            lastHoverResult = TextHitResult.None;
            lastHoverRange = default;
            lastHoverProvider = null;
        }

        /// <inheritdoc/>
        public void OnPointerMove(PointerEventData eventData) => UpdateHover(eventData);

        #endregion

        #region Hover

        private void UpdateHover(PointerEventData eventData)
        {
            var result = HitTestScreen(eventData.position, ResolveEventCamera(eventData));

            var registry = InteractiveRangeRegistry.Get(buffers);
            InteractiveRange newRange = default;
            IInteractiveRangeProvider newProvider = null;

            if (result.hit && registry != null)
                registry.TryGetRangeAt(result.cluster, out newRange, out newProvider);

            var wasInRange = lastHoverRange.IsValid;
            var isInRange = newRange.IsValid;

            var rangeChanged = wasInRange != isInRange ||
                               (wasInRange && isInRange &&
                                (lastHoverRange.start != newRange.start ||
                                 lastHoverRange.end != newRange.end ||
                                 lastHoverRange.type != newRange.type));

            if (rangeChanged)
            {
                if (wasInRange)
                {
                    var exitHit = new InteractiveRangeHit(lastHoverRange, lastHoverResult);
                    RangeExited?.Invoke(exitHit);

                    if (lastHoverProvider is IInteractiveRangeHandler exitHandler)
                        exitHandler.OnRangeExited(lastHoverRange);

                    ResolveHighlighter(lastHoverProvider)?.OnRangeExited(lastHoverRange);
                }

                if (isInRange)
                {
                    var enterHit = new InteractiveRangeHit(newRange, result);
                    RangeEntered?.Invoke(enterHit);

                    if (newProvider is IInteractiveRangeHandler enterHandler)
                        enterHandler.OnRangeEntered(newRange, result);

                    var enterHl = ResolveHighlighter(newProvider);
                    if (enterHl != null)
                    {
                        GetRangeBounds(newRange.start, newRange.end, highlightBoundsCache);
                        enterHl.OnRangeEntered(newRange, highlightBoundsCache);
                    }
                }
            }

            if (result.cluster != lastHoverResult.cluster || result.hit != lastHoverResult.hit)
                HoverChanged?.Invoke(result);

            lastHoverResult = result;
            lastHoverRange = newRange;
            lastHoverProvider = newProvider;
        }

        private bool IsOverInteractiveRange(PointerEventData eventData)
        {
            var registry = InteractiveRangeRegistry.Get(buffers);
            if (registry == null || registry.ProviderCount == 0)
                return false;

            var result = HitTestScreen(eventData.position, ResolveEventCamera(eventData));
            return result.hit && registry.TryGetRangeAt(result.cluster, out _, out _);
        }

        private void PropagateToParent<T>(PointerEventData eventData, ExecuteEvents.EventFunction<T> functor)
            where T : IEventSystemHandler
        {
            var parent = transform.parent;
            if (parent != null)
                ExecuteEvents.ExecuteHierarchy(parent.gameObject, eventData, functor);
        }

        #endregion

        #region Hit Testing

        /// <summary>Performs hit testing in local coordinates.</summary>
        /// <param name="localPosition">Position in local RectTransform space.</param>
        /// <param name="maxDistance">Maximum distance from glyph center to count as a hit.</param>
        /// <returns>Hit test result with glyph/cluster information.</returns>
        public TextHitResult HitTest(Vector2 localPosition, float maxDistance = DefaultMaxClickDistance)
        {
            if (textProcessor == null)
                return TextHitResult.None;

            var glyphs = textProcessor.PositionedGlyphs;
            var glyphCount = glyphs.Length;
            if (glyphCount == 0)
                return TextHitResult.None;

            var rect = rectTransform.rect;
            var textX = localPosition.x - rect.xMin;
            var textY = rect.yMax - localPosition.y;

            for (var i = 0; i < glyphCount; i++)
            {
                ref readonly var glyph = ref glyphs[i];

                if (textX >= glyph.left && textX <= glyph.right &&
                    textY >= glyph.top && textY <= glyph.bottom)
                    return new TextHitResult(i, glyph.cluster, new Vector2(glyph.x, glyph.y), 0f);
            }

            if (maxDistance <= 0)
                return TextHitResult.None;

            var closestDistSq = float.MaxValue;
            var closestIndex = -1;

            for (var i = 0; i < glyphCount; i++)
            {
                ref readonly var glyph = ref glyphs[i];

                var centerX = (glyph.left + glyph.right) * 0.5f;
                var centerY = (glyph.top + glyph.bottom) * 0.5f;
                var dx = textX - centerX;
                var dy = textY - centerY;
                var distSq = dx * dx + dy * dy;

                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    closestIndex = i;
                }
            }

            if (closestIndex < 0)
                return TextHitResult.None;

            var distance = Mathf.Sqrt(closestDistSq);
            if (distance > maxDistance)
                return TextHitResult.None;

            ref readonly var closestGlyph = ref glyphs[closestIndex];
            return new TextHitResult(closestIndex, closestGlyph.cluster, new Vector2(closestGlyph.x, closestGlyph.y),
                distance);
        }

        /// <summary>Performs hit testing from screen coordinates.</summary>
        /// <param name="screenPosition">Position in screen space.</param>
        /// <param name="eventCamera">Camera for coordinate conversion (null for overlay canvases).</param>
        /// <param name="maxDistance">Maximum distance from glyph center to count as a hit.</param>
        /// <returns>Hit test result with glyph/cluster information.</returns>
        public TextHitResult HitTestScreen(Vector2 screenPosition, Camera eventCamera, float maxDistance = DefaultMaxClickDistance)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, screenPosition, eventCamera, out var localPos))
                return TextHitResult.None;

            return HitTest(localPos, maxDistance);
        }

        #endregion

        #region Variant Hooks

        /// <summary>
        /// Resolves the camera to use for screen-to-local conversion based on the event source.
        /// </summary>
        /// <remarks>
        /// Canvas variant returns <c>null</c> for <c>ScreenSpaceOverlay</c> and <c>canvas.worldCamera</c>
        /// otherwise; world-space variant returns the raycaster camera from the event data.
        /// </remarks>
        protected abstract Camera ResolveEventCamera(PointerEventData eventData);

        private TextHighlighter ResolveHighlighter(IInteractiveRangeProvider provider)
        {
            if (provider is InteractiveModifier mod && mod.Highlighter != null)
                return mod.Highlighter;
            return highlighter;
        }

        #endregion
    }
}
