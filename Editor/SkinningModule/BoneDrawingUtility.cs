using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal static class BoneDrawingUtility
    {
        public static float GetBoneRadius(Vector3 position, float scale = 1.0f)
        {
            if (Camera.current != null)
            {
                return 0.15f * scale * HandleUtility.GetHandleSize(position);
            }

            return 10f * scale / Handles.matrix.GetColumn(0).magnitude;
        }

        public static void DrawBoneNode(Vector3 position, Vector3 forward, Color color, float scale = 1.0f)
        {
            Vector3 normal = -forward;
            Vector3 from = Vector3.Cross(normal, Vector3.up);
            if (from.sqrMagnitude < 0.001f)
                from = Vector3.Cross(normal, Vector3.right);

            const int numSamples = 24;
            BatchedDrawing.RegisterSolidArcWithOutline(position, normal, from.normalized, 360f, GetBoneRadius(position, scale) * 0.22f, 1.75f, color, numSamples);
        }

        public static void DrawBone(Vector3 position, Vector3 endPosition, Vector3 forward, Color color, float scale = 1.0f)
        {
            Vector3 right = Vector3.right;
            Vector3 v = endPosition - position;

            if (v.sqrMagnitude != 0)
                right = v.normalized;

            float radius = GetBoneRadius(position, scale);
            int numSamples = 12;

            if (v.sqrMagnitude <= radius * radius * 0.25f)
            {
                Vector3 up = Vector3.Cross(right, forward).normalized;
                BatchedDrawing.RegisterSolidArc(position, -forward, up, 360f, radius, color, numSamples * 2);
            }
            else
                DrawTaperedBone(position, endPosition, right, forward, radius, color);
        }

        public static void DrawBoneOutline(Vector3 position, Vector3 endPosition, Vector3 forward, Color color, float outlineScale = 1.35f, float scale = 1.0f)
        {
            outlineScale = Mathf.Max(1f, outlineScale);

            Vector3 right = Vector3.right;
            Vector3 v = endPosition - position;

            if (v.sqrMagnitude != 0)
                right = v.normalized;

            Vector3 up = Vector3.Cross(right, forward).normalized;
            float radius = GetBoneRadius(position, scale);
            const int numSamples = 12;

            if (v.sqrMagnitude <= radius * radius)
                BatchedDrawing.RegisterSolidArcWithOutline(position, -forward, up, 360f, radius, outlineScale, color, numSamples * 2);
            else
                DrawTaperedBone(position, endPosition, right, forward, radius * outlineScale, color);
        }

        static void DrawTaperedBone(Vector3 position, Vector3 endPosition, Vector3 right, Vector3 forward, float radius, Color color)
        {
            Vector3 v = endPosition - position;
            float length = v.magnitude;
            float startDistance = Mathf.Min(length * 0.08f, radius * 0.55f);
            float bellyDistance = Mathf.Min(length * 0.18f, radius * 2.6f);
            Vector3 startPosition = position + right * startDistance;
            Vector3 bellyPosition = position + right * bellyDistance;
            float bellyWidth = radius * 1.75f;

            BatchedDrawing.RegisterLine(startPosition, bellyPosition, forward, 0f, bellyWidth, color);
            BatchedDrawing.RegisterLine(bellyPosition, endPosition, forward, bellyWidth, 0f, color);
        }
    }
}
