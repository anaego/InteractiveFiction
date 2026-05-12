using System;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal static class UniTextVeryImportantClass
    {
        private static readonly bool forceAlwaysShow = false;

        private const string KeyEarliest = "UniText.EasterEgg.EarliestShow";

        private const double MinCooldownDays = 5;
        private const double MaxCooldownDays = 7;

        private const float SlideInDuration = 0.4f;
        private const float HoldDuration = 1.7f;
        private const float SlideOutDuration = 0.4f;
        private const float TotalDuration = SlideInDuration + HoldDuration + SlideOutDuration;

        private const float KittenSize = 128f;
        private const float Margin = 8f;
        private const float ClipAreaHeight = KittenSize + Margin * 2f;

        private const string KittenResourcePath = "UniText/Icons/kitten";

        private static double animStartTime = -1.0;
        private static Editor activeEditor;
        private static Texture2D kittenTexture;

        public static float ClipHeight => ClipAreaHeight;

        public static void OnEditorEnable(Editor editor)
        {
            if (animStartTime > 0.0) return;

            if (!forceAlwaysShow && !TryAdvanceWindow()) return;

            activeEditor = editor;
            animStartTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += DriveRepaint;
        }

        public static void Draw(Rect clipRect)
        {
            if (animStartTime < 0.0) return;
            if (Event.current.type != EventType.Repaint) return;
            if (clipRect.width <= 0f || clipRect.height <= 0f) return;

            var elapsed = (float)(EditorApplication.timeSinceStartup - animStartTime);
            if (elapsed < 0f || elapsed >= TotalDuration) return;

            var tex = LoadKitten();
            if (tex == null) return;

            float xMin;
            if (elapsed < SlideInDuration)
            {
                var t = EaseOutCubic(elapsed / SlideInDuration);
                xMin = Mathf.Lerp(-KittenSize, Margin, t);
            }
            else if (elapsed < SlideInDuration + HoldDuration)
            {
                xMin = Margin;
            }
            else
            {
                var t = EaseInCubic((elapsed - SlideInDuration - HoldDuration) / SlideOutDuration);
                xMin = Mathf.Lerp(Margin, -KittenSize, t);
            }

            GUI.BeginGroup(clipRect);
            GUI.DrawTexture(new Rect(xMin, Margin, KittenSize, KittenSize), tex, ScaleMode.ScaleToFit, true);
            GUI.EndGroup();
        }

        private static void DriveRepaint()
        {
            if (animStartTime < 0.0)
            {
                EditorApplication.update -= DriveRepaint;
                return;
            }

            var elapsed = EditorApplication.timeSinceStartup - animStartTime;
            if (elapsed >= TotalDuration)
            {
                animStartTime = -1.0;
                activeEditor = null;
                EditorApplication.update -= DriveRepaint;
                return;
            }

            if (activeEditor != null) activeEditor.Repaint();
        }

        private static bool TryAdvanceWindow()
        {
            var earliestStr = EditorPrefs.GetString(KeyEarliest, "");
            if (string.IsNullOrEmpty(earliestStr))
            {
                ScheduleNextEarliest();
                return false;
            }

            if (!long.TryParse(earliestStr, out var binary)) return false;

            DateTime earliest;
            try { earliest = DateTime.FromBinary(binary); }
            catch { return false; }

            if (DateTime.UtcNow < earliest) return false;

            ScheduleNextEarliest();
            return true;
        }

        private static void ScheduleNextEarliest()
        {
            var days = UnityEngine.Random.Range((float)MinCooldownDays, (float)MaxCooldownDays);
            var earliest = DateTime.UtcNow.AddDays(days);
            EditorPrefs.SetString(KeyEarliest, earliest.ToBinary().ToString());
        }

        private static Texture2D LoadKitten()
        {
            if (kittenTexture != null) return kittenTexture;
            kittenTexture = Resources.Load<Texture2D>(KittenResourcePath);
            return kittenTexture;
        }

        private static float EaseOutCubic(float t)
        {
            var u = 1f - t;
            return 1f - u * u * u;
        }

        private static float EaseInCubic(float t) => t * t * t;
    }
}
