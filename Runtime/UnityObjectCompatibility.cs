#if UNITY_6000_4_OR_NEWER
using ObjectId = UnityEngine.EntityId;
#else
using ObjectId = System.Int32;
#endif

namespace UnityEngine.U2D.Animation
{
    // Keep the full object identity in caches and jobs. Never truncate EntityId to int.
    internal static class UnityObjectCompatibility
    {
        // All callers need active objects without a guaranteed sort order.
        internal static T[] FindObjectsByType<T>() where T : Object
        {
#if UNITY_6000_5_OR_NEWER
            return Object.FindObjectsByType<T>();
#else
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#endif
        }

        internal static ObjectId GetObjectId(this Object obj)
        {
#if UNITY_6000_4_OR_NEWER
            return obj.GetEntityId();
#else
            return obj.GetInstanceID();
#endif
        }

        internal static ulong GetBufferId(this Object obj)
        {
#if UNITY_6000_4_OR_NEWER
            return EntityId.ToULong(obj.GetEntityId());
#else
            return unchecked((ulong)obj.GetInstanceID());
#endif
        }
    }
}
