using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// <see cref="IGradientProvider"/> with an inline list of named gradients edited directly
    /// on the modifier. Use this for one-off catalogs that don't deserve a dedicated asset
    /// and aren't shared across components.
    /// </summary>
    [Serializable]
    [TypeDescription("Inline list of named gradients edited directly on the modifier.")]
    public sealed class InlineGradientProvider : IGradientProvider, ISerializationCallbackReceiver
    {
        [SerializeField]
        [Tooltip("Inline named gradients available to this modifier. Names are case-insensitive.")]
        private List<UniTextGradients.NamedGradient> entries = new();

        private Dictionary<string, Gradient> lookup;

        /// <inheritdoc/>
        public bool TryGetGradient(string name, out Gradient gradient)
        {
            EnsureLookup();
            return lookup.TryGetValue(name, out gradient);
        }

        /// <inheritdoc/>
        public IEnumerable<UniTextGradients.NamedGradient> Enumerate()
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (!string.IsNullOrEmpty(e.name) && e.gradient != null)
                    yield return e;
            }
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            lookup = null;
            GradientNotifier.NotifyChanged();
        }

        private void EnsureLookup()
        {
            if (lookup != null) return;

            lookup = new Dictionary<string, Gradient>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (!string.IsNullOrEmpty(e.name) && e.gradient != null)
                    lookup[e.name] = e.gradient;
            }
        }
    }
}
