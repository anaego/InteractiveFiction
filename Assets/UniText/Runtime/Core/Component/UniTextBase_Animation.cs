using System;
using UnityEngine;

namespace LightSide
{
    public abstract partial class UniTextBase
    {
        /// <summary>
        /// Raised after Unity Animator applies animated property values to this component, once
        /// the base field diff has issued the corresponding <see cref="SetDirty"/>.
        /// Modifiers with their own animatable fields should subscribe and run an equivalent
        /// diff over their state, calling <see cref="SetDirty"/> with the flags that match
        /// their effect.
        /// </summary>
        /// <remarks>
        /// Animator writes directly to serialized fields and bypasses property setters, so
        /// dirty flags would otherwise never be raised. The event fires regardless of whether
        /// any base field actually changed — modifiers must perform their own diff and emit
        /// <see cref="UniTextDirtyFlags"/> only when their animated input actually moved.
        /// </remarks>
        public event Action Animated;

        /// <summary>
        /// Unity callback invoked after Animator writes animated properties into this component's
        /// serialized fields. Subclasses delegate to their composed
        /// <see cref="AnimationHandlerBase{T}"/> to diff the animated state and aggregate the
        /// resulting <see cref="UniTextDirtyFlags"/> in a single <see cref="SetDirty"/> call.
        /// </summary>
        protected override void OnDidApplyAnimationProperties()
        {
            base.OnDidApplyAnimationProperties();
            HandleAnimation();
            Animated?.Invoke();
        }

        /// <summary>
        /// Diffs animated fields against the cached baseline through the subclass's animation
        /// handler. Implementations should forward to their composed
        /// <see cref="AnimationHandlerBase{T}"/> instance.
        /// </summary>
        protected abstract void HandleAnimation();

        /// <summary>
        /// Bridges Unity's <c>OnDidApplyAnimationProperties</c> callback to <see cref="SetDirty"/>.
        /// Animator writes directly into serialized fields, bypassing property setters that
        /// normally raise dirty flags — this handler diffs cached values against the
        /// post-animation field state and aggregates the appropriate <see cref="UniTextDirtyFlags"/>
        /// in a single <see cref="SetDirty"/> call.
        /// </summary>
        /// <typeparam name="T">Concrete <see cref="UniTextBase"/> subclass driving the animation.</typeparam>
        /// <remarks>
        /// Composed (not inherited) into a <see cref="UniTextBase"/> subclass. Nested inside
        /// <see cref="UniTextBase"/> so it can read the private/protected serialized fields
        /// directly. Subclass handlers extend the diff for their own fields by overriding
        /// <see cref="DiffSubclassFields"/> and <see cref="CaptureSubclassBaseline"/>; nest them
        /// inside their owning component to retain direct field access.
        /// </remarks>
        public abstract class AnimationHandlerBase<T> where T : UniTextBase
        {
            protected readonly T target;

            private float fontSizeCache;
            private Color colorCache;
            private bool wordWrapCache;
            private bool autoSizeCache;
            private float minFontSizeCache;
            private float maxFontSizeCache;
            private TextDirection baseDirectionCache;
            private HorizontalAlignment horizontalAlignmentCache;
            private VerticalAlignment verticalAlignmentCache;
            private TextOverEdge overEdgeCache;
            private TextUnderEdge underEdgeCache;
            private LeadingDistribution leadingDistributionCache;

            protected AnimationHandlerBase(T target)
            {
                this.target = target;
                CaptureBaseline();
            }

            /// <summary>
            /// Snapshots current property values as the comparison baseline. Called from the
            /// constructor and may be invoked again after non-animator state changes (e.g.
            /// property setters) to keep the baseline aligned with the live fields.
            /// </summary>
            public void CaptureBaseline()
            {
                fontSizeCache = target.fontSize;
                colorCache = target.color;
                wordWrapCache = target.wordWrap;
                autoSizeCache = target.autoSize;
                minFontSizeCache = target.minFontSize;
                maxFontSizeCache = target.maxFontSize;
                baseDirectionCache = target.baseDirection;
                horizontalAlignmentCache = target.horizontalAlignment;
                verticalAlignmentCache = target.verticalAlignment;
                overEdgeCache = target.overEdge;
                underEdgeCache = target.underEdge;
                leadingDistributionCache = target.leadingDistribution;

                CaptureSubclassBaseline();
            }

            /// <summary>
            /// Diffs current field values against the baseline, raises a single
            /// <see cref="SetDirty"/> with the aggregated flags, and refreshes the baseline.
            /// Returns <see langword="true"/> if any field changed.
            /// </summary>
            public bool Handle()
            {
                var flags = UniTextDirtyFlags.None;

                if (!Mathf.Approximately(fontSizeCache, target.fontSize))
                {
                    fontSizeCache = target.fontSize;
                    flags |= UniTextDirtyFlags.FontSize;
                }

                if (colorCache != target.color)
                {
                    colorCache = target.color;
                    flags |= UniTextDirtyFlags.Color;
                }

                if (wordWrapCache != target.wordWrap)
                {
                    wordWrapCache = target.wordWrap;
                    flags |= UniTextDirtyFlags.Layout;
                }

                if (autoSizeCache != target.autoSize)
                {
                    autoSizeCache = target.autoSize;
                    flags |= UniTextDirtyFlags.Layout;
                }

                if (!Mathf.Approximately(minFontSizeCache, target.minFontSize))
                {
                    minFontSizeCache = target.minFontSize;
                    if (target.autoSize) flags |= UniTextDirtyFlags.Layout;
                }

                if (!Mathf.Approximately(maxFontSizeCache, target.maxFontSize))
                {
                    maxFontSizeCache = target.maxFontSize;
                    if (target.autoSize) flags |= UniTextDirtyFlags.Layout;
                }

                if (baseDirectionCache != target.baseDirection)
                {
                    baseDirectionCache = target.baseDirection;
                    flags |= UniTextDirtyFlags.Direction;
                }

                if (horizontalAlignmentCache != target.horizontalAlignment)
                {
                    horizontalAlignmentCache = target.horizontalAlignment;
                    flags |= UniTextDirtyFlags.Alignment;
                }

                if (verticalAlignmentCache != target.verticalAlignment)
                {
                    verticalAlignmentCache = target.verticalAlignment;
                    flags |= UniTextDirtyFlags.Alignment;
                }

                if (overEdgeCache != target.overEdge)
                {
                    overEdgeCache = target.overEdge;
                    flags |= UniTextDirtyFlags.Layout;
                }

                if (underEdgeCache != target.underEdge)
                {
                    underEdgeCache = target.underEdge;
                    flags |= UniTextDirtyFlags.Layout;
                }

                if (leadingDistributionCache != target.leadingDistribution)
                {
                    leadingDistributionCache = target.leadingDistribution;
                    flags |= UniTextDirtyFlags.Layout;
                }

                flags |= DiffSubclassFields();

                if (flags == UniTextDirtyFlags.None) return false;
                target.SetDirty(flags);
                return true;
            }

            /// <summary>
            /// Captures subclass-specific animatable fields into the baseline. Mirror of
            /// <see cref="DiffSubclassFields"/>; both must read the same set of fields.
            /// </summary>
            protected virtual void CaptureSubclassBaseline() { }

            /// <summary>
            /// Diffs subclass-specific animatable fields against the cached baseline, refreshes
            /// the baseline for changed fields, and returns the aggregated flags. Implementations
            /// must not call <see cref="SetDirty"/> directly — the base method aggregates flags
            /// across the whole component before issuing one call.
            /// </summary>
            protected virtual UniTextDirtyFlags DiffSubclassFields() => UniTextDirtyFlags.None;
        }
    }
}
