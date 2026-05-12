#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Editor-only Scene view overlay and global toggle that controls whether <see cref="UniTextBase"/>
    /// components react to Unity's Scene Visibility (the eye icon in Hierarchy). When the toggle is off,
    /// hiding a UniText GameObject in the Hierarchy no longer clears its rendered text — components
    /// behave as if the visibility flag were not set. State is per-developer (stored in
    /// <see cref="EditorPrefs"/>) and survives domain reload.
    /// </summary>
    /// <remarks>
    /// Why this exists: UniText renders through a shared batched mesh owned by
    /// <c>UniTextWorldBatcher</c>'s hidden GameObjects, not through the component's own renderer.
    /// Unity's Scene Visibility system only suppresses rendering of the GameObject it targets, so the
    /// batcher cannot honor the eye-icon state without explicit handling. The explicit handling clears
    /// the entry from the batch in <em>both</em> Scene and Game views, which is broader than Unity's
    /// usual "Scene-view-only" semantics. This toggle lets developers opt out of that behavior.
    /// </remarks>
    [Overlay(typeof(SceneView), OverlayId, "❤️", true)]
    [Icon("Assets/UniText/Editor/Resources/UniText/Icons/unitext-visibility-icon.png")]
    internal sealed class SceneVisibilityOverlay : IMGUIOverlay
    {
        private const string OverlayId = "lightside-unitext-scene-visibility";
        private const string PrefKey = "LightSide.UniText.RespectSceneVisibility";
        internal const string MenuPath = "Tools/UniText/Respect Scene Visibility";
        private const string IconResourcePath = "UniText/Icons/unitext-visibility-icon";
        private const float IconSize = 16f;

        private static readonly Color darkThemeTint = new(0.85f, 0.85f, 0.85f, 1f);
        private static readonly Color lightThemeTint = new(0.2f, 0.2f, 0.2f, 1f);

        private static bool cached;
        private static bool cacheLoaded;

        public static event Action Changed;

        public static bool Respect
        {
            get
            {
                if (!cacheLoaded)
                {
                    cached = EditorPrefs.GetBool(PrefKey, true);
                    cacheLoaded = true;
                }
                return cached;
            }
            set
            {
                if (cacheLoaded && cached == value) return;
                cached = value;
                cacheLoaded = true;
                EditorPrefs.SetBool(PrefKey, value);
                Menu.SetChecked(MenuPath, value);
                Changed?.Invoke();
            }
        }

        [InitializeOnLoadMethod]
        private static void SyncMenuOnLoad()
        {
            EditorApplication.delayCall += () => Menu.SetChecked(MenuPath, Respect);
        }

        [MenuItem(MenuPath, false, 200)]
        private static void ToggleMenu() => Respect = !Respect;

        [MenuItem(MenuPath, true)]
        private static bool ToggleMenuValidate()
        {
            Menu.SetChecked(MenuPath, Respect);
            return true;
        }

        public override void OnCreated() => Changed += OnRespectChanged;
        public override void OnWillBeDestroyed() => Changed -= OnRespectChanged;
        private void OnRespectChanged() => SceneView.RepaintAll();

        public override void OnGUI()
        {
            var icon = Resources.Load<Texture2D>(IconResourcePath);

            using (new GUILayout.HorizontalScope())
            {
                if (icon != null)
                {
                    var prev = GUI.color;
                    GUI.color = EditorGUIUtility.isProSkin ? darkThemeTint : lightThemeTint;
                    GUILayout.Label(
                        new GUIContent(icon, "UniText: respect scene visibility (eye icon in Hierarchy)"),
                        GUIStyle.none,
                        GUILayout.Width(IconSize),
                        GUILayout.Height(IconSize));
                    GUI.color = prev;
                }

                EditorGUI.BeginChangeCheck();
                var value = EditorGUILayout.Toggle(GUIContent.none, Respect, GUILayout.Width(IconSize));
                if (EditorGUI.EndChangeCheck())
                    Respect = value;
            }
        }
    }
}
#endif
