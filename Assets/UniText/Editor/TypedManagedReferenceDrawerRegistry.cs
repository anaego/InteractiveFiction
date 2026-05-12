using System;
using System.Collections.Generic;
using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Lightweight drawer contract used by <see cref="TypeSelectorDrawer"/> to render the body
    /// (everything below the type-picker header) of a specific concrete type inside a
    /// <c>[SerializeReference, TypeSelector]</c> field.
    /// </summary>
    /// <remarks>
    /// Unity's attribute drawer for <c>[TypeSelector]</c> has priority over any
    /// <c>[CustomPropertyDrawer(typeof(ConcreteType))]</c> on the referenced type, so we can't rely on
    /// the standard PropertyDrawer route to customize how a concrete subclass is rendered inside a
    /// TypeSelector field. This registry is the escape hatch: a subclass registers itself once, and
    /// <see cref="TypeSelectorDrawer"/> dispatches to it instead of iterating children manually.
    /// </remarks>
    internal interface IManagedReferenceDrawer
    {
        void OnGUI(UnityEngine.Rect position, SerializedProperty property);
        float GetPropertyHeight(SerializedProperty property);
    }

    internal static class TypedManagedReferenceDrawerRegistry
    {
        private static readonly Dictionary<Type, IManagedReferenceDrawer> drawers = new();

        public static void Register(Type type, IManagedReferenceDrawer drawer)
        {
            drawers[type] = drawer;
        }

        public static bool TryGet(Type type, out IManagedReferenceDrawer drawer)
        {
            return drawers.TryGetValue(type, out drawer);
        }
    }
}
