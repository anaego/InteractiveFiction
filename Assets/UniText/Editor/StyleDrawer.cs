using UnityEditor;
using UnityEngine;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(Style))]
    internal class StyleDrawer : PropertyDrawer
    {

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (IsArrayElement(property))
            {
                label = new GUIContent(GetCustomLabel(property), GetModifierIcon(property));
            }

            var ruleProp = property.FindPropertyRelative("rule");
            var modifierProp = property.FindPropertyRelative("modifier");

            var foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

            if (!property.isExpanded) return;

            EditorGUI.indentLevel++;
            var y = position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            if (!IsRuleStandalone(ruleProp))
            {
                var modifierHeight = EditorGUI.GetPropertyHeight(modifierProp, true);
                EditorGUI.PropertyField(new Rect(position.x, y, position.width, modifierHeight), modifierProp, true);
                y += modifierHeight + EditorGUIUtility.standardVerticalSpacing;
            }

            var ruleHeight = EditorGUI.GetPropertyHeight(ruleProp, true);
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, ruleHeight), ruleProp, true);

            EditorGUI.indentLevel--;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded) return EditorGUIUtility.singleLineHeight;

            var ruleProp = property.FindPropertyRelative("rule");
            var modifierProp = property.FindPropertyRelative("modifier");

            var height = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            height += EditorGUI.GetPropertyHeight(ruleProp, true);

            if (!IsRuleStandalone(ruleProp))
            {
                height += EditorGUIUtility.standardVerticalSpacing;
                height += EditorGUI.GetPropertyHeight(modifierProp, true);
            }

            return height;
        }

        private static bool IsRuleStandalone(SerializedProperty ruleProp)
        {
            return ruleProp?.managedReferenceValue is IParseRule rule && rule.IsStandalone;
        }

        private static bool IsArrayElement(SerializedProperty property)
        {
            return property.propertyPath.EndsWith("]");
        }

        private static string GetCustomLabel(SerializedProperty property)
        {
            var modifierProp = property.FindPropertyRelative("modifier");
            var ruleProp = property.FindPropertyRelative("rule");

            var modifierName = GetTypeName(modifierProp);
            var ruleName = GetTypeName(ruleProp);

            if (string.IsNullOrEmpty(modifierName) && string.IsNullOrEmpty(ruleName))
                return "(Empty)";

            if (IsRuleStandalone(ruleProp))
                return ruleName;

            if (string.IsNullOrEmpty(modifierName))
                return $"?({ruleName})";

            if (string.IsNullOrEmpty(ruleName))
                return $"{modifierName}(?)";

            return $"{modifierName} (Rule: {ruleName})";
        }

        private static string GetTypeName(SerializedProperty prop)
        {
            if (prop == null || prop.managedReferenceValue == null)
                return null;

            var type = prop.managedReferenceValue.GetType();
            var name = type.Name;

            if (name.EndsWith("Modifier"))
                return name.Substring(0, name.Length - 8);
            if (name.EndsWith("ParseRule"))
                return name.Substring(0, name.Length - 9);
            if (name.EndsWith("Rule"))
                return name.Substring(0, name.Length - 4);

            return name;
        }

        private static Texture2D GetModifierIcon(SerializedProperty property)
        {
            var modifierProp = property.FindPropertyRelative("modifier");
            if (modifierProp?.managedReferenceValue == null) return null;
            return UniTextEditorResources.GetTypeIcon(modifierProp.managedReferenceValue.GetType());
        }
    }
}
