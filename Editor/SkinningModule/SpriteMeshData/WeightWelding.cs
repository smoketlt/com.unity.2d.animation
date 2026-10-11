using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    // Samples bind-space triangles; outside samples project to the closest edge.
    internal static class WeightWelding
    {
        internal static bool Sample(Vector2 point, Vector2[] positions, int[] indices,
            EditableBoneWeight[] weights, int[] boneMap, bool overlapping, float[] values)
        {
            var bestDistance = float.PositiveInfinity;
            var bestTriangle = -1;
            var bestBarycentric = Vector3.zero;
            for (var triangle = 0; triangle + 2 < indices.Length; triangle += 3)
            {
                var ia = indices[triangle]; var ib = indices[triangle + 1]; var ic = indices[triangle + 2];
                if (ia < 0 || ib < 0 || ic < 0 || ia >= positions.Length || ib >= positions.Length || ic >= positions.Length ||
                    ia >= weights.Length || ib >= weights.Length || ic >= weights.Length) continue;
                var a = positions[ia]; var b = positions[ib]; var c = positions[ic];
                var denominator = Cross(b - a, c - a);
                if (Mathf.Abs(denominator) <= 1e-8f) continue;
                var v = Cross(point - a, c - a) / denominator;
                var w = Cross(b - a, point - a) / denominator;
                var barycentric = new Vector3(1 - v - w, v, w);
                var distance = 0f;
                if (barycentric.x < -1e-5f || barycentric.y < -1e-5f || barycentric.z < -1e-5f)
                {
                    if (overlapping) continue;
                    distance = float.PositiveInfinity;
                    Edge(point, a, b, new Vector3(1, 0, 0), new Vector3(0, 1, 0), ref distance, ref barycentric);
                    Edge(point, b, c, new Vector3(0, 1, 0), new Vector3(0, 0, 1), ref distance, ref barycentric);
                    Edge(point, c, a, new Vector3(0, 0, 1), new Vector3(1, 0, 0), ref distance, ref barycentric);
                }
                if (distance >= bestDistance) continue;
                bestDistance = distance; bestTriangle = triangle; bestBarycentric = barycentric;
                if (distance == 0) break;
            }
            if (bestTriangle < 0) return false;
            for (var corner = 0; corner < 3; ++corner)
                foreach (var channel in weights[indices[bestTriangle + corner]])
                    if (channel.enabled && channel.weight > 0 && channel.boneIndex >= 0 && channel.boneIndex < boneMap.Length)
                    {
                        var bone = boneMap[channel.boneIndex];
                        if (bone >= 0 && bone < values.Length)
                            values[bone] += channel.weight * Mathf.Max(0, bestBarycentric[corner]);
                    }
            // No transferable influence: leave the target untouched.
            var sum = 0f;
            foreach (var value in values) sum += value;
            return float.IsFinite(sum) && sum > 0;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static void Edge(Vector2 point, Vector2 a, Vector2 b, Vector3 start, Vector3 end,
            ref float distance, ref Vector3 barycentric)
        {
            var edge = b - a;
            var t = edge.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude) : 0;
            var candidate = (point - (a + edge * t)).sqrMagnitude;
            if (candidate >= distance) return;
            distance = candidate; barycentric = Vector3.Lerp(start, end, t);
        }
    }
}
