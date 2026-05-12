using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Resolves named <see cref="Gradient"/> entries for <see cref="GradientModifier"/>.
    /// </summary>
    /// <remarks>
    /// Implementations decide where the gradient catalog lives — project-wide settings,
    /// an explicit asset reference, or an inline list edited per-modifier. Names are
    /// matched case-insensitively, matching <c>&lt;gradient=name&gt;</c> tag behavior.
    /// </remarks>
    /// <seealso cref="GradientModifier"/>
    /// <seealso cref="GlobalSettingsGradientProvider"/>
    /// <seealso cref="AssetGradientProvider"/>
    /// <seealso cref="InlineGradientProvider"/>
    public interface IGradientProvider
    {
        /// <summary>
        /// Tries to resolve a name (as used in <c>&lt;gradient=name&gt;</c>) to a Gradient.
        /// </summary>
        /// <param name="name">Gradient name. Case-insensitive.</param>
        /// <param name="gradient">The resolved gradient when this call returns <see langword="true"/>; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when a gradient with the given name was found.</returns>
        bool TryGetGradient(string name, out Gradient gradient);

        /// <summary>
        /// Enumerates all gradients exposed by this provider. Used by editor tooling to populate the
        /// <c>&lt;gradient=…&gt;</c> dropdown for the modifier instance that owns this provider.
        /// Lazy or runtime-resolved providers may return an empty sequence.
        /// </summary>
        IEnumerable<UniTextGradients.NamedGradient> Enumerate();
    }
}
