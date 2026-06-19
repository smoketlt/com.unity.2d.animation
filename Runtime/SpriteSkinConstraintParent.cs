using UnityEngine;

namespace UnityEngine.U2D.Animation
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class SpriteSkinConstraintParent : MonoBehaviour
    {
        public const string GuidPrefix = "constraint-parent:";

        [SerializeField]
        string m_DrivenBoneGuid;

        public string drivenBoneGuid
        {
            get => m_DrivenBoneGuid;
            set => m_DrivenBoneGuid = value;
        }

        public static string GetGuid(string drivenBoneGuid)
        {
            return string.IsNullOrEmpty(drivenBoneGuid) ? string.Empty : $"{GuidPrefix}{drivenBoneGuid}";
        }

        public static bool IsConstraintParentGuid(string guid)
        {
            return !string.IsNullOrEmpty(guid) && guid.StartsWith(GuidPrefix);
        }

        public static bool IsConstraintParentFor(string parentGuid, string drivenBoneGuid)
        {
            return parentGuid == GetGuid(drivenBoneGuid);
        }

        void OnDrawGizmos()
        {
            const float size = 0.08f;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            Vector3 position = transform.position;
            Gizmos.DrawLine(position - transform.right * size, position + transform.right * size);
            Gizmos.DrawLine(position - transform.up * size, position + transform.up * size);
        }
    }
}
