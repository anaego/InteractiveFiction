using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// <see cref="IGradientProvider"/> backed by an explicit <see cref="UniTextGradients"/>
    /// asset referenced on the modifier. Use this when a single component should resolve
    /// gradients from a different catalog than the project-wide one.
    /// </summary>
    [Serializable]
    [TypeDescription("Resolves names through an explicit UniTextGradients asset reference.")]
    public sealed class AssetGradientProvider : IGradientProvider
    {
        [SerializeField]
        [Tooltip("UniTextGradients asset to resolve <gradient=name> tags from.")]
        private UniTextGradients asset;

        /// <summary>The asset used for name resolution. Setting to <see langword="null"/> makes <see cref="TryGetGradient"/> return <see langword="false"/> for every name.</summary>
        public UniTextGradients Asset
        {
            get => asset;
            set => asset = value;
        }

        /// <inheritdoc/>
        public bool TryGetGradient(string name, out Gradient gradient)
        {
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
            return asset == null ? Enumerable.Empty<UniTextGradients.NamedGradient>() : EnumerateAsset(asset);
        }

        private static IEnumerable<UniTextGradients.NamedGradient> EnumerateAsset(UniTextGradients src)
        {
            foreach (var name in src.GradientNames)
            {
                if (src.TryGetGradient(name, out var gradient) && gradient != null)
                    yield return new UniTextGradients.NamedGradient { name = name, gradient = gradient };
            }
        }
    }
}
