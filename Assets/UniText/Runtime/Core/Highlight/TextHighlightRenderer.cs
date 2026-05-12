using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>Relative render order of a highlight surface versus its owning text.</summary>
    public enum HighlightOrder
    {
        /// <summary>Render behind the text (selections, hover glow).</summary>
        Behind,
        /// <summary>Render in front of the text (click flashes, cursor).</summary>
        Above
    }

    /// <summary>
    /// Backend-agnostic view surface for <see cref="TextHighlighter"/> visuals.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built-in implementations: <see cref="CanvasHighlightRenderer"/> wraps
    /// <see cref="RangeHighlightGraphic"/> for <see cref="UniText"/>, while
    /// <see cref="WorldHighlightRenderer"/> wraps a <see cref="MeshRenderer"/>-based
    /// quad mesh for <see cref="UniTextWorld"/>. A <see cref="TextHighlighter"/>
    /// subclass picks one of these (or a custom subclass) per backend via its
    /// <c>CreateHighlightRenderer(UniText, ...)</c> / <c>CreateHighlightRenderer(UniTextWorld, ...)</c>
    /// overloads and drives the result with the same lifecycle regardless of backend.
    /// </para>
    /// </remarks>
    public abstract class TextHighlightRenderer
    {
        /// <summary>Solid color applied to every rect. Setting this updates visuals immediately.</summary>
        public abstract Color Color { get; set; }

        /// <summary>Replaces the rectangles rendered by this surface. Rects are in text-local space.</summary>
        public abstract void SetRects(IReadOnlyList<Rect> rects);

        /// <summary>Clears all rectangles. The surface stays alive but draws nothing.</summary>
        public abstract void Clear();

        /// <summary>Permanently destroys the backing GameObject / resources.</summary>
        public abstract void Destroy();
    }
}
