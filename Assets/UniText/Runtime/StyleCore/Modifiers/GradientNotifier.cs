using System;

namespace LightSide
{
    /// <summary>
    /// Broadcast channel for changes in any gradient source visible to <see cref="GradientModifier"/>.
    /// Fires when a <see cref="UniTextGradients"/> asset is edited, when an
    /// <see cref="InlineGradientProvider"/>'s entries are changed in the inspector, or when a custom
    /// <see cref="IGradientProvider"/> implementation invokes <see cref="NotifyChanged"/> directly.
    /// </summary>
    /// <remarks>
    /// <see cref="GradientModifier"/> subscribes during its active lifecycle and marks the owning
    /// UniText component dirty so colors are recomputed on the next rebuild. Custom providers that
    /// expose their own mutable state should call <see cref="NotifyChanged"/> when their resolution
    /// result changes.
    /// </remarks>
    public static class GradientNotifier
    {
        /// <summary>
        /// Raised when any subscribed gradient source has changed. Subscribers should treat this as
        /// "invalidate cached gradient resolution and rebuild" — there is no per-source filtering.
        /// </summary>
        public static event Action AnyChanged;

        /// <summary>Raises <see cref="AnyChanged"/>. Safe to call when there are no subscribers.</summary>
        public static void NotifyChanged() => AnyChanged?.Invoke();
    }
}
