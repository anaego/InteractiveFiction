using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    [CustomEditor(typeof(UniTextFontStack))]
    internal class UniTextFontStackEditor : Editor
    {
        private SerializedProperty familiesProp;
        private SerializedProperty fallbackStackProp;
        private List<bool> familyFoldouts = new();

        private void OnEnable()
        {
            familiesProp = serializedObject.FindProperty("families");
            fallbackStackProp = serializedObject.FindProperty("fallbackStack");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawFamilies();

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(fallbackStackProp);

            serializedObject.ApplyModifiedProperties();
        }

        private string GetFamilyTitle(SerializedProperty primaryProp, SerializedProperty facesProp)
        {
            var names = new HashSet<string>();
            var primaryFont = primaryProp.objectReferenceValue as UniTextFont;
            if (primaryFont != null)
            {
                var name = primaryFont.FaceInfo.familyName;
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }

            if (facesProp != null)
            {
                for (int j = 0; j < facesProp.arraySize; j++)
                {
                    var faceFont = facesProp.GetArrayElementAtIndex(j).objectReferenceValue as UniTextFont;
                    if (faceFont != null)
                    {
                        var name = faceFont.FaceInfo.familyName;
                        if (!string.IsNullOrEmpty(name)) names.Add(name);
                    }
                }
            }

            if (names.Count == 0) return "Empty Family";
            return string.Join(", ", names);
        }

        private void DrawFamilies()
        {
            while (familyFoldouts.Count < familiesProp.arraySize)
                familyFoldouts.Add(false);
            if (familyFoldouts.Count > familiesProp.arraySize)
                familyFoldouts.RemoveRange(familiesProp.arraySize, familyFoldouts.Count - familiesProp.arraySize);

            for (int fi = 0; fi < familiesProp.arraySize; fi++)
            {
                var familyProp = familiesProp.GetArrayElementAtIndex(fi);
                var nameProp = familyProp.FindPropertyRelative("name");
                var primaryProp = familyProp.FindPropertyRelative("primary");
                var facesProp = familyProp.FindPropertyRelative("faces");
                var preferredLanguageProp = familyProp.FindPropertyRelative("preferredLanguage");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                var headerRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
                var title = GetFamilyTitle(primaryProp, facesProp);
                var foldoutStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };

                const float DeleteBtnW = 20f;
                const float InlineNameW = 80f;
                const float Pad = 4f;
                const float FoldoutIconW = 18f;
                const float TitleDesiredW = 130f;
                const float TitleMinW = 60f;
                const float PrimaryMinW = 100f;

                var collapsed = !familyFoldouts[fi];
                float titleW, primaryW;

                if (collapsed)
                {
                    var freeW = headerRect.width - FoldoutIconW - Pad - InlineNameW - Pad - DeleteBtnW;
                    titleW = TitleDesiredW;
                    primaryW = freeW - titleW;
                    if (primaryW < PrimaryMinW)
                    {
                        primaryW = Mathf.Min(PrimaryMinW, freeW - TitleMinW);
                        titleW = freeW - primaryW;
                    }
                }
                else
                {
                    titleW = headerRect.width - DeleteBtnW - FoldoutIconW;
                    primaryW = 0f;
                }

                var foldoutRect = new Rect(headerRect.x + 14, headerRect.y, titleW, headerRect.height);
                familyFoldouts[fi] = EditorGUI.Foldout(foldoutRect, familyFoldouts[fi], title, true, foldoutStyle);

                if (collapsed)
                {
                    var nameRect = new Rect(
                        headerRect.xMax - DeleteBtnW - Pad - primaryW - Pad - InlineNameW,
                        headerRect.y, InlineNameW, headerRect.height);
                    if (nameProp != null)
                        EditorGUI.PropertyField(nameRect, nameProp, GUIContent.none);

                    var primaryRect = new Rect(
                        headerRect.xMax - DeleteBtnW - Pad - primaryW,
                        headerRect.y, primaryW, headerRect.height);
                    EditorGUI.PropertyField(primaryRect, primaryProp, GUIContent.none);
                }

                var btnRect = new Rect(headerRect.xMax - DeleteBtnW, headerRect.y, DeleteBtnW, headerRect.height);
                GUI.backgroundColor = new Color(1f, 0.47f, 0.47f);
                if (GUI.Button(btnRect, "✕"))
                {
                    GUI.backgroundColor = Color.white;
                    familiesProp.DeleteArrayElementAtIndex(fi);
                    familyFoldouts.RemoveAt(fi);
                    EditorGUILayout.EndVertical();
                    break;
                }
                GUI.backgroundColor = Color.white;

                if (collapsed)
                {
                    GUILayout.Space(2);
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(2);
                    continue;
                }

                var prevColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
                var innerStyle = new GUIStyle(EditorStyles.helpBox);
                innerStyle.margin = new RectOffset(15, 0, 2, 0);
                EditorGUILayout.BeginVertical(innerStyle);
                GUI.backgroundColor = prevColor;

                if (nameProp != null)
                {
                    EditorGUILayout.PropertyField(nameProp, new GUIContent(
                        "Name",
                        "User-facing identifier used by <font=...> rich-text tags (case-sensitive, " +
                        "unique per stack). Leave empty to make this family unaddressable by name."));
                }

                EditorGUILayout.PropertyField(primaryProp, new GUIContent("Primary"));

                if (preferredLanguageProp != null)
                {
                    EditorGUILayout.PropertyField(preferredLanguageProp, new GUIContent(
                        "Preferred Language",
                        "Optional BCP 47 tag (e.g. zh-Hans, zh-Hant, ja, ko, en-US). When text " +
                        "carries a matching language tag, this family wins during codepoint-to-font " +
                        "resolution. Leave empty for locale-agnostic families."));
                }

                if (primaryProp.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("Primary font is required. This family will be ignored without it.", MessageType.Warning);
                }
                else if (facesProp != null && facesProp.arraySize > 0)
                {
                    var primaryFont = primaryProp.objectReferenceValue as UniTextFont;
                    var primaryFamily = primaryFont != null ? primaryFont.FaceInfo.familyName : null;
                    if (!string.IsNullOrEmpty(primaryFamily))
                    {
                        for (int j = 0; j < facesProp.arraySize; j++)
                        {
                            var faceFont = facesProp.GetArrayElementAtIndex(j).objectReferenceValue as UniTextFont;
                            if (faceFont != null && faceFont.FaceInfo.familyName != primaryFamily)
                            {
                                EditorGUILayout.HelpBox(
                                    $"Face \"{faceFont.FaceInfo.familyName}\" differs from primary \"{primaryFamily}\". This is allowed but may be unintentional.",
                                    MessageType.Info);
                                break;
                            }
                        }
                    }
                }

                if (facesProp != null)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Faces", EditorStyles.boldLabel);
                    for (int j = 0; j < facesProp.arraySize; j++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        var faceProp = facesProp.GetArrayElementAtIndex(j);
                        EditorGUILayout.PropertyField(faceProp, GUIContent.none);

                        var faceFont = faceProp.objectReferenceValue as UniTextFont;
                        if (faceFont != null)
                        {
                            var w = faceFont.FaceInfo.weightClass;
                            var info = w > 0 ? $"W:{w}" : "W:?";
                            if (faceFont.FaceInfo.isItalic) info += " Italic";
                            if (faceFont.IsVariable) info += " Variable";
                            EditorGUILayout.LabelField(info, EditorStyles.miniLabel, GUILayout.Width(100));
                        }

                        GUI.backgroundColor = new Color(1f, 0.47f, 0.47f);
                        if (GUILayout.Button("✕", GUILayout.Width(20)))
                        {
                            facesProp.DeleteArrayElementAtIndex(j);
                            GUI.backgroundColor = Color.white;
                            break;
                        }
                        GUI.backgroundColor = Color.white;
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Add Face", GUILayout.Width(80)))
                    {
                        facesProp.InsertArrayElementAtIndex(facesProp.arraySize);
                        facesProp.GetArrayElementAtIndex(facesProp.arraySize - 1).objectReferenceValue = null;
                    }
                    if (facesProp.arraySize > 0)
                    {
                        GUI.backgroundColor = new Color(1f, 0.47f, 0.47f);
                        if (GUILayout.Button("Clear", GUILayout.Width(60)))
                            facesProp.ClearArray();
                        GUI.backgroundColor = Color.white;
                    }
                    EditorGUILayout.EndHorizontal();

                    DrawFaceDropZone(facesProp, primaryProp);
                }

                GUILayout.Space(2);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            if (GUILayout.Button("Add Family", GUILayout.Width(100)))
            {
                familiesProp.InsertArrayElementAtIndex(familiesProp.arraySize);
                var newFamily = familiesProp.GetArrayElementAtIndex(familiesProp.arraySize - 1);
                var name = newFamily.FindPropertyRelative("name");
                if (name != null) name.stringValue = string.Empty;
                newFamily.FindPropertyRelative("primary").objectReferenceValue = null;
                var faces = newFamily.FindPropertyRelative("faces");
                if (faces != null) faces.ClearArray();
                var lang = newFamily.FindPropertyRelative("preferredLanguage");
                if (lang != null) lang.stringValue = string.Empty;
                familyFoldouts.Add(true);
            }
        }

        private void DrawFaceDropZone(SerializedProperty facesProp, SerializedProperty primaryProp)
        {
            var dropRect = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));

            var dropStyle = new GUIStyle(EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            GUI.Box(dropRect, "Drop UniText Font Assets here", dropStyle);

            var evt = Event.current;
            if (!dropRect.Contains(evt.mousePosition)) return;

            if (evt.type == EventType.DragUpdated)
            {
                bool hasFont = false;
                foreach (var obj in DragAndDrop.objectReferences)
                    if (obj is UniTextFont) { hasFont = true; break; }
                DragAndDrop.visualMode = hasFont ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                evt.Use();
            }
            else if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is not UniTextFont font) continue;

                    if (primaryProp.objectReferenceValue == font) continue;

                    bool exists = false;
                    for (int j = 0; j < facesProp.arraySize; j++)
                    {
                        if (facesProp.GetArrayElementAtIndex(j).objectReferenceValue == font)
                        { exists = true; break; }
                    }
                    if (exists) continue;

                    if (!font.IsVariable)
                    {
                        bool duplicate = false;
                        for (int j = 0; j < facesProp.arraySize; j++)
                        {
                            var existing = facesProp.GetArrayElementAtIndex(j).objectReferenceValue as UniTextFont;
                            if (existing != null && !existing.IsVariable
                                && existing.FaceInfo.weightClass == font.FaceInfo.weightClass
                                && existing.FaceInfo.isItalic == font.FaceInfo.isItalic)
                            {
                                duplicate = true;
                                break;
                            }
                        }
                        if (duplicate)
                        {
                            Debug.LogWarning(
                                $"[UniText] Duplicate face: weight {font.FaceInfo.weightClass}, " +
                                $"italic={font.FaceInfo.isItalic} already exists in this family. Skipped '{font.name}'.");
                            continue;
                        }
                    }

                    int idx = facesProp.arraySize;
                    facesProp.InsertArrayElementAtIndex(idx);
                    facesProp.GetArrayElementAtIndex(idx).objectReferenceValue = font;
                }
                evt.Use();
            }
        }
    }
}
