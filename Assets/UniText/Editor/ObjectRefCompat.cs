using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal static class ObjectRefCompat
    {
        public static string Serialize(Object obj)
        {
            if (obj == null) return "0";
#if UNITY_6000_4_OR_NEWER
            return EntityId.ToULong(obj.GetEntityId()).ToString();
#else
            return obj.GetInstanceID().ToString();
#endif
        }

        public static Object Deserialize(string data)
        {
#if UNITY_6000_4_OR_NEWER
            var raw = ulong.Parse(data);
            return raw != 0UL ? EditorUtility.EntityIdToObject(EntityId.FromULong(raw)) : null;
#else
#pragma warning disable CS0618
            var id = int.Parse(data);
            return id != 0 ? EditorUtility.InstanceIDToObject(id) : null;
#pragma warning restore CS0618  
#endif
        }
    }
}
