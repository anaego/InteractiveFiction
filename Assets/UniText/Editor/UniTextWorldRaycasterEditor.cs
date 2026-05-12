using UnityEditor;
using UnityEngine;

namespace LightSide
{
    [CustomEditor(typeof(UniTextWorldRaycaster))]
    [CanEditMultipleObjects]
    internal class UniTextWorldRaycasterEditor : Editor
    {
        private SerializedProperty blockingObjectsProp;
        private SerializedProperty blockingMaskProp;
        private SerializedProperty sortPriorityProp;
        private SerializedProperty renderPriorityProp;

        private void OnEnable()
        {
            blockingObjectsProp = serializedObject.FindProperty("blockingObjects");
            blockingMaskProp = serializedObject.FindProperty("blockingMask");
            sortPriorityProp = serializedObject.FindProperty("sortPriority");
            renderPriorityProp = serializedObject.FindProperty("renderPriority");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawBlockingSection();
            DrawSortingSection();

            serializedObject.ApplyModifiedProperties();

            UniTextBaseEditor.DrawLoveLabel();
        }

        private void DrawBlockingSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Blocking", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(blockingObjectsProp, new GUIContent(
                "Blocking Objects",
                "Physical systems consulted for occlusion. Leave as None if a PhysicsRaycaster also runs on this camera."));

            var mode = (UniTextWorldRaycaster.BlockingObjects)blockingObjectsProp.enumValueIndex;

#if !UNITEXT_HAS_PHYSICS
            if (mode == UniTextWorldRaycaster.BlockingObjects.ThreeD ||
                mode == UniTextWorldRaycaster.BlockingObjects.All)
            {
                EditorGUILayout.HelpBox(
                    "3D Physics module is disabled for this project. The 3D occlusion check has no effect.",
                    MessageType.Warning);
            }
#endif

#if !UNITEXT_HAS_PHYSICS2D
            if (mode == UniTextWorldRaycaster.BlockingObjects.TwoD ||
                mode == UniTextWorldRaycaster.BlockingObjects.All)
            {
                EditorGUILayout.HelpBox(
                    "2D Physics module is disabled for this project. The 2D occlusion check has no effect.",
                    MessageType.Warning);
            }
#endif

            if (mode != UniTextWorldRaycaster.BlockingObjects.None)
            {
                EditorGUILayout.PropertyField(blockingMaskProp, new GUIContent(
                    "Blocking Mask",
                    "Layer mask used for blocking collider queries."));
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSortingSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Sorting", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(sortPriorityProp, new GUIContent(
                "Sort Priority",
                "Raycaster sort priority compared against other raycasters on the same camera."));

            EditorGUILayout.PropertyField(renderPriorityProp, new GUIContent(
                "Render Priority",
                "Secondary tie-breaker when sort priorities are equal."));

            EditorGUILayout.EndVertical();
        }
    }
}
