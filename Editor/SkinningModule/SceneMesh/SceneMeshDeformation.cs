using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    // Only the transient working mesh contains the sampled pose. Undo and saving
    // always see SceneMeshSession's source-space mesh.
    internal sealed class SceneMeshDeformation
    {
        readonly Matrix4x4[] m_BoneMatrices;
        readonly Dictionary<EditableBoneWeight, (Vector2 source, Vector2 displayed)> m_OriginalVertices = new();
        public readonly SpriteMeshData mesh;
        public Rect editingFrame { get; private set; }

        internal static SceneMeshDeformation Create(SpriteRenderer renderer, SpriteMeshData source)
        {
            var skin = renderer.GetComponent<SpriteSkin>();
            if (skin == null || !skin.enabled || source.vertexCount != source.vertexWeights.Length) return null;
            var bones = skin.boneTransforms;
            var bindPoses = renderer.sprite.GetBindPoses();
            if (bones == null || bindPoses.Length == 0 || bones.Length != bindPoses.Length ||
                source.boneCount != bindPoses.Length) return null;
            // SpriteRenderer flips are applied after skinning. Build deformation in
            // the unflipped renderer plane; PixelToWorld applies the flips at draw time.
            var sprite = renderer.sprite;
            Vector2 scale = sprite.rect.size / source.frame.size / sprite.pixelsPerUnit;
            Vector2 pivot = sprite.pivot / sprite.rect.size * source.frame.size;
            var pixelsToLocal = Matrix4x4.Scale(new Vector3(scale.x, scale.y, 1f)) * Matrix4x4.Translate(-pivot);
            var worldToPixels = pixelsToLocal.inverse * renderer.transform.worldToLocalMatrix;
            var matrices = new Matrix4x4[bones.Length];
            for (var i = 0; i < bones.Length; ++i)
            {
                if (bones[i] == null) return null;
                matrices[i] = worldToPixels * bones[i].localToWorldMatrix * bindPoses[i] * pixelsToLocal;
            }
            return new SceneMeshDeformation(source, matrices);
        }

        internal SceneMeshDeformation(SpriteMeshData source, Matrix4x4[] boneMatrices)
        {
            m_BoneMatrices = boneMatrices;
            mesh = JsonUtility.FromJson<SpriteMeshData>(JsonUtility.ToJson(source));
            editingFrame = new Rect(Vector2.zero, source.frame.size);
            for (var i = 0; i < mesh.vertexCount; ++i)
            {
                var displayed = (Vector2)VertexMatrix(mesh.vertexWeights[i]).MultiplyPoint3x4(source.vertices[i]);
                mesh.vertices[i] = displayed;
                m_OriginalVertices.Add(mesh.vertexWeights[i], (source.vertices[i], displayed));
                editingFrame = Rect.MinMaxRect(Mathf.Min(editingFrame.xMin, displayed.x), Mathf.Min(editingFrame.yMin, displayed.y),
                    Mathf.Max(editingFrame.xMax, displayed.x), Mathf.Max(editingFrame.yMax, displayed.y));
            }
        }

        Matrix4x4 VertexMatrix(EditableBoneWeight weights)
        {
            var result = Matrix4x4.zero;
            var total = 0f;
            foreach (var channel in weights)
            {
                if (!channel.enabled || channel.weight <= 0f || channel.boneIndex < 0 || channel.boneIndex >= m_BoneMatrices.Length) continue;
                var bone = m_BoneMatrices[channel.boneIndex];
                for (var row = 0; row < 3; ++row)
                    for (var column = 0; column < 4; ++column)
                        result[row, column] += bone[row, column] * channel.weight;
                total += channel.weight;
            }
            // Cleared/new unbound geometry remains editable in its source plane.
            if (total <= 0f) return Matrix4x4.identity;
            result.m33 = 1f;
            return result;
        }

        internal Vector2 DisplayPosition(Vector2 position, EditableBoneWeight weight) =>
            VertexMatrix(weight).MultiplyPoint3x4(position);

        internal Vector2 SourcePosition(Vector2 position, EditableBoneWeight weight)
        {
            if (m_OriginalVertices.TryGetValue(weight, out var original) && position.Equals(original.displayed))
                return original.source;
            var matrix = VertexMatrix(weight);
            var determinant = matrix.m00 * matrix.m11 - matrix.m01 * matrix.m10;
            if (!float.IsFinite(determinant) || Mathf.Abs(determinant) < 0.000001f)
                throw new InvalidOperationException("This animation pose collapses the vertex plane. Change the frame or disable Deformed to edit it.");
            var x = position.x - matrix.m03;
            var y = position.y - matrix.m13;
            return new Vector2((x * matrix.m11 - y * matrix.m01) / determinant,
                (y * matrix.m00 - x * matrix.m10) / determinant);
        }

        internal SpriteMeshData ToSourceMesh()
        {
            var result = JsonUtility.FromJson<SpriteMeshData>(JsonUtility.ToJson(mesh));
            for (var i = 0; i < mesh.vertexCount; ++i)
            {
                // Solve the displayed XY plane explicitly; a 3D inverse is incorrect
                // after projecting out-of-plane bone rotations into a 2D editor.
                result.vertices[i] = SourcePosition(mesh.vertices[i], mesh.vertexWeights[i]);
            }
            return result;
        }

        internal bool Commit(SceneMeshSession session)
        {
            var source = ToSourceMesh();
            if (JsonUtility.ToJson(source) == JsonUtility.ToJson(session.mesh)) return false;
            // Triangulate in the authoring plane, never bake frame-specific topology.
            var controller = new SpriteMeshDataController { spriteMeshData = source };
            controller.Triangulate(new Triangulator());
            controller.SortTrianglesByDepth();
            SceneMeshSource.Validate(source);
            session.mesh = source;
            return true;
        }
    }
}
