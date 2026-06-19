using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal static class ConstraintBoneFlash
    {
        const float k_Duration = 1.35f;
        const float k_BlinkCount = 2f;
        const float k_DimAlphaMultiplier = 0.18f;
        const float k_MinHighlightedAlphaMultiplier = 0.25f;

        static readonly HashSet<string> s_BoneGuids = new HashSet<string>();
        static double s_StartTime;

        public static bool isActive
        {
            get
            {
                if (s_BoneGuids.Count == 0)
                    return false;

                if (Elapsed >= k_Duration)
                {
                    Clear();
                    return false;
                }

                return true;
            }
        }

        static float Elapsed => (float)(EditorApplication.timeSinceStartup - s_StartTime);

        public static void Start(BoneCache first, BoneCache second)
        {
            s_BoneGuids.Clear();
            Add(first);
            Add(second);

            if (s_BoneGuids.Count == 0)
                return;

            s_StartTime = EditorApplication.timeSinceStartup;
        }

        public static float GetAlphaMultiplier(BoneCache bone)
        {
            if (!isActive || bone == null)
                return 1f;

            if (!IsHighlighted(bone))
                return k_DimAlphaMultiplier;

            float phase = Mathf.PingPong(Elapsed * k_BlinkCount * 2f / k_Duration + 1f, 1f);
            return Mathf.Lerp(k_MinHighlightedAlphaMultiplier, 1f, phase);
        }

        public static bool IsHighlighted(BoneCache bone)
        {
            return bone != null && s_BoneGuids.Contains(bone.guid);
        }

        static void Add(BoneCache bone)
        {
            if (bone != null && !string.IsNullOrEmpty(bone.guid))
                s_BoneGuids.Add(bone.guid);
        }

        static void Clear()
        {
            s_BoneGuids.Clear();
        }
    }
}
