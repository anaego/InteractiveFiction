using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// <see cref="IGradientProvider"/> backed by the project-wide
    /// <see cref="UniTextSettings.Gradients"/> asset. Use this when the same gradient
    /// catalog is shared across all UniText components in the project.
    /// </summary>
    [Serializable]
    [TypeDescription("Resolves names through the project-wide UniTextSettings.Gradients asset.")]
    public sealed class GlobalSettingsGradientProvider : IGradientProvider
    {
        /// <inheritdoc/>
        public bool TryGetGradient(string name, out Gradient gradient)
        {
            var asset = UniTextSettings.Gradients;
            if (asset == null)
            {
                gradient = null;
                return false;
            }
            return asset.TryGetGradient(name, out gradient);
        }

        /// <inheritdoc/>
        public IEnumerable<UniTextGradients.NamedGradient> Enumerate()
        {
            var asset = UniTextSettings.Gradients;
            return asset == null ? Enumerable.Empty<UniTextGradients.NamedGradient>() : EnumerateAsset(asset);
        }

        private static IEnumerable<UniTextGradients.NamedGradient> EnumerateAsset(UniTextGradients asset)
        {
            foreach (var name in asset.GradientNames)
            {
                if (asset.TryGetGradient(name, out var gradient) && gradient != null)
                    yield return new UniTextGradients.NamedGradient { name = name, gradient = gradient };
            }
        }
    }
}
