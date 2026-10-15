using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    // Editor-only pose sampling: never asks the GPU for a readback or enables CPU deformation.
    internal sealed class SceneSpriteDeformedMesh
    {
        internal Vector3[] vertices;
        internal NativeArray<ushort> indices;
        Matrix4x4[] m_Bones;

        internal bool Sample(SpriteRenderer renderer)
        {
            if (renderer == null || renderer.sprite == null) return false;
            var sprite = renderer.sprite;
            var source = sprite.GetVertexAttribute<Vector3>(VertexAttribute.Position);
            indices = sprite.GetIndices();
            if (source.Length == 0 || indices.Length < 3) return false;
            if (vertices == null || vertices.Length != source.Length) vertices = new Vector3[source.Length];
            var skin = renderer.GetComponent<SpriteSkin>();
            var poses = sprite.GetBindPoses();
            var bones = skin != null ? skin.boneTransforms : null;
            var weights = sprite.HasVertexAttribute(VertexAttribute.BlendWeight)
                ? sprite.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight) : default;
            var deform = skin != null && skin.isActiveAndEnabled && skin.isValid && bones != null &&
                bones.Length == poses.Length && poses.Length > 0 && weights.Length == source.Length;
            if (deform)
            {
                if (m_Bones == null || m_Bones.Length != bones.Length) m_Bones = new Matrix4x4[bones.Length];
                for (var i = 0; i < bones.Length; ++i)
                {
                    if (bones[i] == null) { deform = false; break; }
                    m_Bones[i] = renderer.transform.worldToLocalMatrix * bones[i].localToWorldMatrix * poses[i];
                }
            }
            for (var i = 0; i < source.Length; ++i)
            {
                var position = source[i];
                if (deform)
                {
                    var weight = weights[i];
                    position = Weighted(position, weight.boneIndex0, weight.weight0) +
                        Weighted(position, weight.boneIndex1, weight.weight1) +
                        Weighted(position, weight.boneIndex2, weight.weight2) +
                        Weighted(position, weight.boneIndex3, weight.weight3);
                }
                if (renderer.flipX) position.x = -position.x;
                if (renderer.flipY) position.y = -position.y;
                vertices[i] = renderer.transform.TransformPoint(position);
            }
            return true;
        }

        Vector3 Weighted(Vector3 position, int index, float weight) =>
            weight > 0 && index >= 0 && index < m_Bones.Length ? m_Bones[index].MultiplyPoint3x4(position) * weight : Vector3.zero;

        internal bool Hit(Ray ray, out float distance)
        {
            distance = float.PositiveInfinity;
            for (var i = 0; i + 2 < indices.Length; i += 3)
                if (Intersect(ray, vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]], out var hit))
                    distance = Mathf.Min(distance, hit);
            return !float.IsPositiveInfinity(distance);
        }

        // Layout may test geometry, but must never request Unity's native picking render.
        internal static bool HitSource(SpriteRenderer renderer, Ray ray)
        {
            if (renderer == null || renderer.sprite == null) return false;
            var origin = renderer.transform.InverseTransformPoint(ray.origin);
            var direction = renderer.transform.InverseTransformVector(ray.direction);
            if (renderer.flipX) { origin.x = -origin.x; direction.x = -direction.x; }
            if (renderer.flipY) { origin.y = -origin.y; direction.y = -direction.y; }
            var localRay = new Ray(origin, direction);
            var source = renderer.sprite.GetVertexAttribute<Vector3>(VertexAttribute.Position);
            var triangles = renderer.sprite.GetIndices();
            for (var i = 0; i + 2 < triangles.Length; i += 3)
                if (Intersect(localRay, source[triangles[i]], source[triangles[i + 1]], source[triangles[i + 2]], out _)) return true;
            return false;
        }

        // Double-sided Moller-Trumbore: mirrored transforms and folded triangles are selectable.
        internal static bool Intersect(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0;
            var edge1 = b - a;
            var edge2 = c - a;
            var p = Vector3.Cross(ray.direction, edge2);
            var determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < 1e-10f) return false;
            var inverse = 1f / determinant;
            var t = ray.origin - a;
            var u = Vector3.Dot(t, p) * inverse;
            if (u < 0 || u > 1) return false;
            var q = Vector3.Cross(t, edge1);
            var v = Vector3.Dot(ray.direction, q) * inverse;
            if (v < 0 || u + v > 1) return false;
            distance = Vector3.Dot(edge2, q) * inverse;
            return distance >= 0;
        }
    }
}
