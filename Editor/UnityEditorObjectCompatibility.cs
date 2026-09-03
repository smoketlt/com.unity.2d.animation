#if UNITY_6000_4_OR_NEWER
using ObjectId = UnityEngine.EntityId;
#else
using ObjectId = System.Int32;
#endif

namespace UnityEditor.U2D.Animation
{
    internal static class UnityEditorObjectCompatibility
    {
        internal static UnityEngine.Object FindObject(ObjectId id)
        {
#if UNITY_6000_4_OR_NEWER
            return EditorUtility.EntityIdToObject(id);
#else
            return EditorUtility.InstanceIDToObject(id);
#endif
        }
    }
}
