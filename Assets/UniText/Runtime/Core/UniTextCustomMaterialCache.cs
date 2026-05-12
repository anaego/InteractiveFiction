using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Process-wide cache of runtime material clones used by <see cref="MaterialModifier"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// UGUI batches CanvasRenderers by reference equality on their material, so cloning a source
    /// material once per modifier instance breaks batching (N MaterialModifiers sharing a source
    /// would produce N draw calls). This cache keys clones by
    /// <c>(source instance id, isMsdf, isEmoji)</c>, so every MaterialModifier that references the
    /// same source material receives the same runtime clone — a single draw call across all of them.
    /// </para>
    /// <para>
    /// The cache is static and lives for the lifetime of the process: unloading a source material
    /// will invalidate the corresponding entry on the next <see cref="Acquire"/> call. Atlas changes
    /// are forwarded into cached clones through a one-time subscription per (clone, atlas) pair.
    /// </para>
    /// <para>
    /// <b>Property freshness:</b> shader properties are copied from the source material only once,
    /// when the runtime clone is first created. Runtime edits to the source material
    /// (<c>material.SetColor(...)</c> etc.) are <b>not</b> reflected in the cached clone; modify the
    /// clone directly, or expose per-text animation through <see cref="MaterialModifier.ConstantUv2"/>/
    /// <see cref="MaterialModifier.ConstantUv3"/> which are written into vertex attributes rather than
    /// material state.
    /// </para>
    /// </remarks>
    internal static class UniTextCustomMaterialCache
    {
        private const string MsdfKeywordName = "UNITEXT_MSDF";
        private const string EmojiKeywordName = "UNITEXT_EMOJI";

        private readonly struct Key : IEquatable<Key>
        {
            public readonly int sourceId;
            public readonly bool isMsdf;
            public readonly bool isEmoji;

            public Key(int sourceId, bool isMsdf, bool isEmoji)
            {
                this.sourceId = sourceId;
                this.isMsdf = isMsdf;
                this.isEmoji = isEmoji;
            }

            public bool Equals(Key other) =>
                sourceId == other.sourceId && isMsdf == other.isMsdf && isEmoji == other.isEmoji;

            public override bool Equals(object obj) => obj is Key k && Equals(k);

            public override int GetHashCode() =>
                (sourceId * 397) ^ ((isMsdf ? 1 : 0) << 1) ^ (isEmoji ? 1 : 0);
        }

        private sealed class Entry
        {
            public Material runtime;
            public GlyphAtlas subscribedAtlas;
            public readonly Action<Texture> atlasCallback;

            public Entry()
            {
                atlasCallback = OnAtlasTextureChanged;
            }

            private void OnAtlasTextureChanged(Texture tex)
            {
                if (runtime != null && runtime.mainTexture != tex)
                    runtime.mainTexture = tex;
            }
        }

        /// <summary>
        /// Per-source bookkeeping for the <see cref="BindSourceDirect"/> path — tracks which atlas
        /// the source material is currently subscribed to and keeps the cached delegate. Does not
        /// own the <see cref="source"/> material (it's a user asset).
        /// </summary>
        private sealed class DirectBinding
        {
            public Material source;
            public GlyphAtlas subscribedAtlas;
            public readonly Action<Texture> atlasCallback;

            public DirectBinding()
            {
                atlasCallback = OnAtlasTextureChanged;
            }

            private void OnAtlasTextureChanged(Texture tex)
            {
                if (source != null && source.mainTexture != tex)
                    source.mainTexture = tex;
            }
        }

        private static readonly Dictionary<Key, Entry> cache = new();
        private static readonly Dictionary<Material, DirectBinding> directBindings = new();

#if UNITY_EDITOR
        static UniTextCustomMaterialCache() => Reseter.UnmanagedCleaning += DestroyAll;

        private static void DestroyAll()
        {
            foreach (var entry in cache.Values)
            {
                UnsubscribeAtlas(entry);
                if (entry.runtime != null)
                    UnityEngine.Object.DestroyImmediate(entry.runtime);
            }
            cache.Clear();

            foreach (var binding in directBindings.Values)
                UnsubscribeDirect(binding);
            directBindings.Clear();
        }
#endif

        /// <summary>
        /// Returns a shared runtime material clone for the given source + atlas-mode combination.
        /// On first call for a key the clone is created, properties are copied from <paramref name="source"/>,
        /// and MSDF/emoji keywords are set. Subsequent calls return the same clone untouched
        /// (properties and keywords are cached — see type remarks).
        /// </summary>
        public static Material Acquire(Material source, bool isMsdf, bool isEmoji, GlyphAtlas atlas)
        {
            if (source == null) return null;

            var key = new Key(ObjectUtils.GetInstanceIdCompat(source), isMsdf, isEmoji);

            if (!cache.TryGetValue(key, out var entry) || entry.runtime == null)
            {
                if (entry != null) UnsubscribeAtlas(entry);

                var runtime = new Material(source)
                {
                    name = $"UniText Custom [{source.name}]",
                    hideFlags = HideFlags.HideAndDontSave,
                };

                if (isMsdf) runtime.EnableKeyword(MsdfKeywordName);
                else runtime.DisableKeyword(MsdfKeywordName);
                if (isEmoji) runtime.EnableKeyword(EmojiKeywordName);
                else runtime.DisableKeyword(EmojiKeywordName);

                entry = new Entry { runtime = runtime };
                cache[key] = entry;
            }

            BindAtlas(entry, atlas);
            return entry.runtime;
        }

        private static void BindAtlas(Entry entry, GlyphAtlas atlas)
        {
            if (entry.subscribedAtlas != atlas)
            {
                UnsubscribeAtlas(entry);
                if (atlas != null)
                {
                    atlas.AtlasTextureChanged += entry.atlasCallback;
                    entry.subscribedAtlas = atlas;
                }
            }

            if (atlas != null)
            {
                var tex = atlas.AtlasTexture;
                if (entry.runtime.mainTexture != tex)
                    entry.runtime.mainTexture = tex;
            }
        }

        private static void UnsubscribeAtlas(Entry entry)
        {
            if (entry.subscribedAtlas != null)
                entry.subscribedAtlas.AtlasTextureChanged -= entry.atlasCallback;
            entry.subscribedAtlas = null;
        }

        /// <summary>
        /// Opt-out of the clone cache: returns <paramref name="source"/> itself and binds
        /// <paramref name="atlas"/>'s current texture onto it (with a persistent subscription for
        /// future atlas texture changes). Use when the caller wants runtime edits on the source
        /// material (<c>source.SetColor(...)</c>) to be visible immediately — at the cost of
        /// shared keyword / texture state across every consumer of the same material instance.
        /// Keywords are NOT set here — configure them on the source material yourself.
        /// </summary>
        public static Material BindSourceDirect(Material source, GlyphAtlas atlas)
        {
            if (source == null) return null;

            if (!directBindings.TryGetValue(source, out var binding))
            {
                binding = new DirectBinding { source = source };
                directBindings[source] = binding;
            }

            if (binding.subscribedAtlas != atlas)
            {
                UnsubscribeDirect(binding);
                if (atlas != null)
                {
                    atlas.AtlasTextureChanged += binding.atlasCallback;
                    binding.subscribedAtlas = atlas;
                }
            }

            if (atlas != null)
            {
                var tex = atlas.AtlasTexture;
                if (source.mainTexture != tex)
                    source.mainTexture = tex;
            }

            return source;
        }

        private static void UnsubscribeDirect(DirectBinding binding)
        {
            if (binding.subscribedAtlas != null)
                binding.subscribedAtlas.AtlasTextureChanged -= binding.atlasCallback;
            binding.subscribedAtlas = null;
        }

        /// <summary>
        /// Drops any cached runtime clone and direct binding associated with <paramref name="source"/>.
        /// The next <see cref="Acquire"/> call for this source will rebuild the clone from scratch,
        /// picking up any runtime edits made to the source material (property values, keyword state).
        /// Call when you've modified the source material and want subsequent text rebuilds to pick
        /// up the new state.
        /// </summary>
        /// <remarks>
        /// Does not trigger text rebuilds on its own — call <c>UniTextBase.SetDirty(UniTextDirtyFlags.Material)</c>
        /// on the affected text components after invalidation so they re-acquire the refreshed material.
        /// </remarks>
        public static void InvalidateSource(Material source)
        {
            if (source == null) return;
            var sourceId = ObjectUtils.GetInstanceIdCompat(source);

            List<Key> toRemove = null;
            foreach (var kvp in cache)
            {
                if (kvp.Key.sourceId != sourceId) continue;
                (toRemove ??= new List<Key>(2)).Add(kvp.Key);
            }

            if (toRemove != null)
            {
                for (var i = 0; i < toRemove.Count; i++)
                {
                    if (!cache.TryGetValue(toRemove[i], out var entry)) continue;
                    UnsubscribeAtlas(entry);
                    if (entry.runtime != null)
                        UnityEngine.Object.Destroy(entry.runtime);
                    cache.Remove(toRemove[i]);
                }
            }

            if (directBindings.TryGetValue(source, out var binding))
            {
                UnsubscribeDirect(binding);
                directBindings.Remove(source);
            }
        }
    }
}
