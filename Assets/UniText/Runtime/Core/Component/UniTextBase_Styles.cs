using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Public helpers for querying and mutating styles/modifiers on a UniText component.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Query methods</b> (<see cref="HasModifier{T}"/>, <see cref="TryGetStyle{T}"/>,
    /// <see cref="GetStylesOfType{T}"/>) search both local <c>Styles</c> and shared
    /// <c>StylePresets</c> runtime copies. Local styles are visited first, so they take
    /// priority when duplicates exist.
    /// </para>
    /// <para>
    /// <b>Mutation methods</b> (<see cref="SetWholeText{T}"/>, <see cref="ClearWholeText{T}"/>,
    /// <see cref="ToggleWholeText{T}"/>) only operate on local <c>Styles</c>. Shared presets
    /// are never modified — editing a shared asset through a component API would surprise
    /// any other components using the same asset.
    /// </para>
    /// </remarks>
    public abstract partial class UniTextBase
    {
        #region Query

        /// <summary>Returns true if any style on this component has a modifier of type <typeparamref name="T"/>.</summary>
        public bool HasModifier<T>() where T : BaseModifier => HasModifier(typeof(T));

        /// <summary>Returns true if any style on this component has a modifier assignable to <paramref name="modifierType"/>.</summary>
        public bool HasModifier(Type modifierType)
        {
            if (modifierType == null) return false;
            return TryGetStyle(modifierType, out _);
        }

        /// <summary>Finds the first style whose modifier is of type <typeparamref name="T"/>.</summary>
        public bool TryGetStyle<T>(out Style style) where T : BaseModifier
            => TryGetStyle(typeof(T), out style);

        /// <summary>Finds the first style whose modifier is assignable to <paramref name="modifierType"/>.</summary>
        public bool TryGetStyle(Type modifierType, out Style style)
        {
            if (modifierType != null)
            {
                if (TryFindStyleIn(styles, modifierType, out style)) return true;

                for (var i = 0; i < runtimeStylePresetCopies.Count; i++)
                {
                    var preset = runtimeStylePresetCopies[i];
                    if (preset == null) continue;
                    if (TryFindStyleIn(preset.styles, modifierType, out style)) return true;
                }
            }
            style = null;
            return false;
        }

        /// <summary>
        /// Finds the first whole-text style (range <c>..</c>) whose modifier is of type <typeparamref name="T"/>.
        /// </summary>
        public bool TryGetWholeTextStyle<T>(out Style style) where T : BaseModifier
            => TryGetWholeTextStyle(typeof(T), out style);

        /// <summary>Finds the first whole-text style whose modifier is assignable to <paramref name="modifierType"/>.</summary>
        public bool TryGetWholeTextStyle(Type modifierType, out Style style)
        {
            if (modifierType != null)
            {
                if (TryFindWholeTextStyleIn(styles, modifierType, out style)) return true;

                for (var i = 0; i < runtimeStylePresetCopies.Count; i++)
                {
                    var preset = runtimeStylePresetCopies[i];
                    if (preset == null) continue;
                    if (TryFindWholeTextStyleIn(preset.styles, modifierType, out style)) return true;
                }
            }
            style = null;
            return false;
        }

        /// <summary>Enumerates every style whose modifier is of type <typeparamref name="T"/>, local first.</summary>
        public IEnumerable<Style> GetStylesOfType<T>() where T : BaseModifier
            => GetStylesOfType(typeof(T));

        /// <summary>Enumerates every style whose modifier is assignable to <paramref name="modifierType"/>, local first.</summary>
        public IEnumerable<Style> GetStylesOfType(Type modifierType)
        {
            if (modifierType == null) yield break;

            for (var i = 0; i < styles.Count; i++)
            {
                var s = styles[i];
                if (s?.Modifier != null && modifierType.IsInstanceOfType(s.Modifier))
                    yield return s;
            }

            for (var p = 0; p < runtimeStylePresetCopies.Count; p++)
            {
                var preset = runtimeStylePresetCopies[p];
                if (preset == null) continue;
                for (var i = 0; i < preset.styles.Count; i++)
                {
                    var s = preset.styles[i];
                    if (s?.Modifier != null && modifierType.IsInstanceOfType(s.Modifier))
                        yield return s;
                }
            }
        }

        private static bool TryFindStyleIn(IReadOnlyList<Style> list, Type modifierType, out Style style)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s?.Modifier != null && modifierType.IsInstanceOfType(s.Modifier))
                {
                    style = s;
                    return true;
                }
            }
            style = null;
            return false;
        }

        private static bool TryFindWholeTextStyleIn(IReadOnlyList<Style> list, Type modifierType, out Style style)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s?.Modifier == null) continue;
                if (!modifierType.IsInstanceOfType(s.Modifier)) continue;
                if (!IsWholeTextRule(s.Rule)) continue;
                style = s;
                return true;
            }
            style = null;
            return false;
        }

        internal static bool IsWholeTextRule(IParseRule rule)
        {
            if (rule is not RangeRule rr) return false;
            if (rr.data == null || rr.data.Count == 0) return false;
            return RangeEx.IsWholeText(rr.data[0].range);
        }

        #endregion

        #region Whole-text mutations

        /// <summary>
        /// Adds or updates a whole-text style of modifier type <typeparamref name="T"/>.
        /// If an existing local whole-text style exists, its parameter is updated in place;
        /// otherwise a new style is added via <see cref="AddStyle"/>.
        /// </summary>
        public void SetWholeText<T>(string parameter = null) where T : BaseModifier, new()
            => SetWholeText(typeof(T), parameter, static () => new T());

        /// <summary>
        /// Adds or updates a whole-text style for <paramref name="modifierType"/>, creating
        /// a new modifier via <paramref name="factory"/> when none is found locally.
        /// </summary>
        public void SetWholeText(Type modifierType, string parameter, Func<BaseModifier> factory)
        {
            if (modifierType == null || factory == null) return;

            if (TryFindWholeTextStyleIn(styles, modifierType, out var existing))
            {
                var rr = (RangeRule)existing.Rule;
                var entry = rr.data[0];
                if (entry.parameter == parameter) return;
                entry.parameter = parameter;
                rr.data[0] = entry;
                SetDirty(UniTextDirtyFlags.Text);
                return;
            }

            var modifier = factory();
            if (modifier == null || !modifierType.IsInstanceOfType(modifier)) return;

            AddStyle(Style.WholeText(modifier, parameter));
        }

        /// <summary>
        /// Removes the first local whole-text style whose modifier is of type <typeparamref name="T"/>.
        /// Returns true if a style was removed.
        /// </summary>
        public bool ClearWholeText<T>() where T : BaseModifier => ClearWholeText(typeof(T));

        /// <summary>Removes the first local whole-text style whose modifier is assignable to <paramref name="modifierType"/>.</summary>
        public bool ClearWholeText(Type modifierType)
        {
            if (modifierType == null) return false;
            if (!TryFindWholeTextStyleIn(styles, modifierType, out var style)) return false;
            return RemoveStyle(style);
        }

        /// <summary>
        /// Inverts the presence of a whole-text style of type <typeparamref name="T"/>.
        /// Adds the style with <paramref name="parameter"/> when absent, removes it when present.
        /// Returns true if the style is present after the call.
        /// </summary>
        public bool ToggleWholeText<T>(string parameter = null) where T : BaseModifier, new()
            => ToggleWholeText(typeof(T), parameter, static () => new T());

        /// <summary>
        /// Inverts the presence of a whole-text style for <paramref name="modifierType"/>,
        /// creating a new modifier via <paramref name="factory"/> when adding.
        /// </summary>
        public bool ToggleWholeText(Type modifierType, string parameter, Func<BaseModifier> factory)
        {
            if (modifierType == null) return false;

            if (TryFindWholeTextStyleIn(styles, modifierType, out _))
            {
                ClearWholeText(modifierType);
                return false;
            }

            SetWholeText(modifierType, parameter, factory);
            return true;
        }

        /// <summary>Returns the parameter of the first whole-text style of type <typeparamref name="T"/>, or null.</summary>
        public string GetWholeTextParameter<T>() where T : BaseModifier
            => GetWholeTextParameter(typeof(T));

        /// <summary>Returns the parameter of the first whole-text style of <paramref name="modifierType"/>, or null.</summary>
        public string GetWholeTextParameter(Type modifierType)
        {
            if (!TryGetWholeTextStyle(modifierType, out var style)) return null;
            return ((RangeRule)style.Rule).data[0].parameter;
        }

        #endregion
    }
}
