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
