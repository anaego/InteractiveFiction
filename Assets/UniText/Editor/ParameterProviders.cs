using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Single option entry exposed by a <see cref="ParameterProviders"/> rich provider.
    /// Carries the value written into the parameter, an optional display label and an
    /// optional renderer drawn in a fixed-width column on the right side of the dropdown row
    /// (used by <see cref="GradientModifier"/> to draw a live gradient preview, for example).
    /// </summary>
    public readonly struct ParameterOption
    {
        /// <summary>Value emitted into the tag parameter when this option is selected.</summary>
        public readonly string value;

        /// <summary>Label shown in the dropdown. Falls back to <see cref="value"/> when null.</summary>
        public readonly string displayName;

        /// <summary>Optional decorator drawn in a fixed-width column at the right edge of the row.</summary>
        public readonly Action<Rect> drawPreview;

        /// <summary>Optional description shown in the selector's preview pane.</summary>
        public readonly string description;

        public ParameterOption(string value, string displayName = null,
            Action<Rect> drawPreview = null, string description = null)
        {
            this.value = value;
            this.displayName = displayName ?? value;
            this.drawPreview = drawPreview;
            this.description = description;
        }
    }

    /// <summary>
    /// Editor-only registry for dynamic enum providers used by <see cref="ParameterFieldAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Register a provider with a key, then reference it in the attribute type string
    /// as <c>"enum:@key"</c>. The editor drawer will call the provider to populate the dropdown.
    /// Use the <see cref="ParameterOption"/> overload when entries should carry per-option metadata
    /// (display name, preview, description); the string overload remains for simple cases.
    /// <example>
    /// <code>
    /// // Simple (legacy):
    /// ParameterProviders.Register("gradients", () => myGradients.Select(g =&gt; g.name));
    ///
    /// // Rich (with preview):
    /// ParameterProviders.Register("gradients", () =&gt; myGradients.Select(g =&gt;
    ///     new ParameterOption(g.name, drawPreview: r =&gt; DrawPreview(r, g.gradient))));
    ///
    /// [ParameterField(0, "Name", "enum:@gradients")]
    /// </code>
    /// </example>
    /// </remarks>
    /// <summary>
    /// Resolves <see cref="ParameterOption"/> entries for an <c>"enum:@key"</c> field, given the
    /// owning modifier instance. Use this overload when the dropdown content depends on per-modifier
    /// state — e.g. <see cref="GradientModifier"/> reading from its assigned <see cref="IGradientProvider"/>.
    /// The <paramref name="modifier"/> may be <see langword="null"/> when no owning modifier could be
    /// resolved (e.g. preview contexts); implementations should fall back to a sensible default.
    /// </summary>
    public delegate IEnumerable<ParameterOption> ContextualParameterOptionsProvider(BaseModifier modifier);

    public static class ParameterProviders
    {
        private static readonly Dictionary<string, Func<IEnumerable<string>>> stringProviders = new();
        private static readonly Dictionary<string, Func<IEnumerable<ParameterOption>>> richProviders = new();
        private static readonly Dictionary<string, ContextualParameterOptionsProvider> contextualProviders = new();

        /// <summary>Registers a simple options provider (string values only).</summary>
        public static void Register(string key, Func<IEnumerable<string>> provider)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            stringProviders[key] = provider;
            richProviders.Remove(key);
            contextualProviders.Remove(key);
        }

        /// <summary>
        /// Registers a rich options provider whose entries can carry per-option display name,
        /// preview decorator and description.
        /// </summary>
        public static void Register(string key, Func<IEnumerable<ParameterOption>> richProvider)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (richProvider == null) throw new ArgumentNullException(nameof(richProvider));
            richProviders[key] = richProvider;
            stringProviders.Remove(key);
            contextualProviders.Remove(key);
        }

        /// <summary>
        /// Registers a contextual options provider that receives the owning modifier instance and can
        /// produce dropdown content depending on per-modifier state (e.g. an injected provider).
        /// </summary>
        public static void Register(string key, ContextualParameterOptionsProvider contextualProvider)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (contextualProvider == null) throw new ArgumentNullException(nameof(contextualProvider));
            contextualProviders[key] = contextualProvider;
            stringProviders.Remove(key);
            richProviders.Remove(key);
        }

        /// <summary>Removes a previously registered provider.</summary>
        public static void Unregister(string key)
        {
            if (key == null) return;
            stringProviders.Remove(key);
            richProviders.Remove(key);
            contextualProviders.Remove(key);
        }

        /// <summary>
        /// Tries to get the current options as raw strings. Adapts rich/contextual providers automatically
        /// by projecting <see cref="ParameterOption.value"/>. Contextual providers are invoked with a
        /// <see langword="null"/> modifier — pass a modifier explicitly via <see cref="TryGetRichOptions(string, BaseModifier, out IEnumerable{ParameterOption})"/>.
        /// </summary>
        public static bool TryGetOptions(string key, out IEnumerable<string> options)
        {
            options = null;
            if (key == null) return false;

            if (contextualProviders.TryGetValue(key, out var ctx))
            {
                var result = ctx(null);
                if (result == null) return false;
                options = ProjectValues(result);
                return true;
            }

            if (richProviders.TryGetValue(key, out var rich))
            {
                var richResult = rich();
                if (richResult == null) return false;
                options = ProjectValues(richResult);
                return true;
            }

            if (stringProviders.TryGetValue(key, out var simple))
            {
                options = simple();
                return options != null;
            }

            return false;
        }

        /// <summary>
        /// Tries to get the current options as <see cref="ParameterOption"/> entries, optionally bound
        /// to an owning <paramref name="modifier"/>. Contextual providers receive the modifier; rich
        /// and simple providers ignore it. Adapts simple string providers automatically.
        /// </summary>
        public static bool TryGetRichOptions(string key, BaseModifier modifier, out IEnumerable<ParameterOption> options)
        {
            options = null;
            if (key == null) return false;

            if (contextualProviders.TryGetValue(key, out var ctx))
            {
                options = ctx(modifier);
                return options != null;
            }

            if (richProviders.TryGetValue(key, out var rich))
            {
                options = rich();
                return options != null;
            }

            if (stringProviders.TryGetValue(key, out var simple))
            {
                var result = simple();
                if (result == null) return false;
                options = WrapValues(result);
                return true;
            }

            return false;
        }

        private static IEnumerable<string> ProjectValues(IEnumerable<ParameterOption> source)
        {
            foreach (var opt in source)
                yield return opt.value;
        }

        private static IEnumerable<ParameterOption> WrapValues(IEnumerable<string> source)
        {
            foreach (var v in source)
                yield return new ParameterOption(v);
        }
    }

    [InitializeOnLoad]
    internal static class BuiltInParameterProviders
    {
        private static readonly GlobalSettingsGradientProvider globalFallback = new();

        static BuiltInParameterProviders()
        {
            ParameterProviders.Register("gradients", EnumerateGradients);
        }

        private static IEnumerable<ParameterOption> EnumerateGradients(BaseModifier modifier)
        {
            var source = modifier is GradientModifier gm && gm.Provider != null
                ? gm.Provider
                : globalFallback;

            foreach (var entry in source.Enumerate())
            {
                if (string.IsNullOrEmpty(entry.name) || entry.gradient == null)
                    continue;

                var captured = entry.gradient;
                yield return new ParameterOption(
                    value: entry.name,
                    displayName: entry.name,
                    drawPreview: rect => DrawGradientPreview(rect, captured));
            }
        }

        private static void DrawGradientPreview(Rect rect, Gradient gradient)
        {
            EditorGUI.GradientField(rect, gradient);
        }
    }
}
