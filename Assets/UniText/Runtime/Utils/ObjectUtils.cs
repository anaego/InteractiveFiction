using UnityEngine;

namespace LightSide
{
    internal static class ObjectUtils
    {
        public static void SafeDestroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }
        
        public static void SafeDestroy(Object obj, bool allowDestroyAsset)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj, allowDestroyAsset);
        }

        public static int GetInstanceIdCompat(Object obj)
        {
#if UNITY_6000_4_OR_NEWER
            return (int)(EntityId.ToULong(obj.GetEntityId()) & uint.MaxValue);
#else
            return obj.GetInstanceID();
#endif
        }

        public static T FindFirst<T>() where T : Object
        {
#if UNITY_2022_2_OR_NEWER
            return Object.FindFirstObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }

        public static T FindAny<T>() where T : Object
        {
#if UNITY_2022_2_OR_NEWER
            return Object.FindAnyObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }

        public static T[] FindAll<T>() where T : Object
        {
#if UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<T>();
#endif
        }
    }
}
