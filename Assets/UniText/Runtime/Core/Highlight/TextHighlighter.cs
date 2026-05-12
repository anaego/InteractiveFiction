using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Base class for text highlighting and selection visualization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inherit from this class to create custom highlight effects for interactive ranges,
    /// text selection, or any other visual feedback.
    /// </para>
    /// <para>
    /// Assign to <see cref="UniTextBase.Highlighter"/> to enable highlighting. The two
    /// <c>CreateHighlightRenderer</c> overloads (one per backend) are dispatched
    /// type-safely from the owner's actual type — subclass to plug a custom visual on
    /// either or both backends without runtime cast risk. Set to null to disable.
    /// </para>
    /// </remarks>
    [Serializable]
    public abstract class TextHighlighter
    {
        protected UniTextBase owner;
        protected bool isInitialized;

        /// <summary>
        /// Initializes the handler with its owner.
        /// </summary>
        public virtual void Initialize(UniTextBase owner)
        {
            this.owner = owner;
            isInitialized = true;
        }

        /// <summary>
        /// Creates a Canvas-side <see cref="TextHighlightRenderer"/> for the given
        /// <see cref="UniText"/> owner. Override to plug a custom Canvas visual.
        /// </summary>
        /// <param name="owner">The Canvas text component this surface anchors to.</param>
        /// <param name="name">GameObject name for debugging.</param>
        /// <param name="order">Relative render order vs the owning text.</param>
        protected abstract TextHighlightRenderer CreateHighlightRenderer(UniText owner, string name, HighlightOrder order);

        /// <summary>
        /// Creates a world-space <see cref="TextHighlightRenderer"/> for the given
        /// <see cref="UniTextWorld"/> owner. Override to plug a custom mesh-based visual.
        /// </summary>
        /// <param name="owner">The world-space text component this surface anchors to.</param>
        /// <param name="name">GameObject name for debugging.</param>
        /// <param name="order">Relative render order vs the owning text.</param>
        protected abstract TextHighlightRenderer CreateHighlightRenderer(UniTextWorld owner, string name, HighlightOrder order);

        /// <summary>
        /// Creates a backend-appropriate <see cref="TextHighlightRenderer"/> for the current
        /// <see cref="owner"/>, dispatching to the correct typed overload above. Call this
        /// from event handlers when a renderer is actually needed.
        /// </summary>
        /// <param name="name">GameObject name for debugging.</param>
        /// <param name="order">Relative render order vs the owning text.</param>
        protected TextHighlightRenderer CreateHighlightRenderer(string name, HighlightOrder order) => owner switch
        {
            UniText canvas => CreateHighlightRenderer(canvas, name, order),
            UniTextWorld world => CreateHighlightRenderer(world, name, order),
            _ => throw new InvalidOperationException($"Unsupported owner type: {owner?.GetType().Name ?? "null"}")
        };

        /// <summary>
        /// Called when an interactive range is clicked.
        /// </summary>
        /// <param name="range">The clicked range.</param>
        /// <param name="bounds">Visual bounds of the range (may be multiple for BiDi text).</param>
        public virtual void OnRangeClicked(InteractiveRange range, List<Rect> bounds) { }

        /// <summary>
        /// Called when pointer enters an interactive range (desktop only).
        /// </summary>
        /// <param name="range">The entered range.</param>
        /// <param name="bounds">Visual bounds of the range.</param>
        public virtual void OnRangeEntered(InteractiveRange range, List<Rect> bounds) { }

        /// <summary>
        /// Called when pointer exits an interactive range (desktop only).
        /// </summary>
        /// <param name="range">The exited range.</param>
        public virtual void OnRangeExited(InteractiveRange range) { }

        /// <summary>
        /// Called when text selection changes (for InputField).
        /// </summary>
        /// <param name="startCluster">Start of selection (cluster index).</param>
        /// <param name="endCluster">End of selection (cluster index).</param>
        /// <param name="bounds">Visual bounds of selection.</param>
        public virtual void OnSelectionChanged(int startCluster, int endCluster, List<Rect> bounds) { }

        /// <summary>
        /// Called every frame for animation updates.
        /// </summary>
        public virtual void Update() { }

        /// <summary>
        /// Cleans up resources when the handler is removed or UniText is destroyed.
        /// </summary>
        public virtual void Destroy()
        {
            isInitialized = false;
            owner = null;
        }
    }
}
