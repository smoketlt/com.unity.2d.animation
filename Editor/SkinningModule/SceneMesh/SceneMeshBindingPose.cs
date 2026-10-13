using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace UnityEditor.U2D.Animation
{
    [Serializable]
    internal sealed class SceneMeshBindingPose
    {
        const string kPrefix = "Uniskin.MeshBindingPose.v1:";
        [Serializable] sealed class Document { public List<SceneMeshBindingPose> sprites = new List<SceneMeshBindingPose>(); }
        [Serializable] internal sealed class Bone { public string guid; public Matrix4x4 pixelsToBone; }
        public string id;
        public Bone[] bones;
        public Vector2[] positions;
        public Vector2[] texturePositions;
        public int[] indices;

        internal static SceneMeshBindingPose Read(string userData, string id) => ReadDocument(userData).sprites.FirstOrDefault(item => item.id == id);

        static Document ReadDocument(string userData)
        {
            var line = (userData ?? string.Empty).Split('\n').FirstOrDefault(item => item.StartsWith(kPrefix, StringComparison.Ordinal));
            if (line == null) return new Document();
            try
            {
                var document = JsonUtility.FromJson<Document>(Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(kPrefix.Length))));
                return document?.sprites != null ? document : new Document();
            }
            catch (ArgumentException) { return new Document(); }
            catch (FormatException) { return new Document(); }
        }

        internal static string Write(string userData, SceneMeshBindingPose pose)
        {
            var document = ReadDocument(userData);
            document.sprites.RemoveAll(item => item.id == pose.id);
            document.sprites.Add(pose);
            var lines = (userData ?? string.Empty).Split('\n').Where(line => !line.StartsWith(kPrefix, StringComparison.Ordinal)).ToList();
            lines.Add(kPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonUtility.ToJson(document))));
            return string.Join("\n", lines);
        }

        internal Matrix4x4? FindBone(string guid) => bones?.FirstOrDefault(bone => bone.guid == guid)?.pixelsToBone;

        internal static Matrix4x4 PixelsToLocal(Sprite sprite, Rect frame)
        {
            Vector2 scale = sprite.rect.size / frame.size / sprite.pixelsPerUnit;
            Vector2 pivot = sprite.pivot / sprite.rect.size * frame.size;
            return Matrix4x4.Scale(new Vector3(scale.x, scale.y, 1)) * Matrix4x4.Translate(-pivot);
        }

        internal static bool IsFinite(Matrix4x4 matrix)
        {
            for (var i = 0; i < 16; ++i) if (!float.IsFinite(matrix[i])) return false;
            return true;
        }

        internal void Validate()
        {
            if (bones == null || bones.Length == 0 || bones.Any(bone => bone == null || string.IsNullOrEmpty(bone.guid) ||
                !IsFinite(bone.pixelsToBone) || Mathf.Abs(bone.pixelsToBone.determinant) < 1e-12f) ||
                bones.Select(bone => bone.guid).Distinct().Count() != bones.Length)
                throw new InvalidOperationException("Update Bindings requires valid bone transforms with non-zero scale.");
            if (positions == null || texturePositions == null || positions.Length != texturePositions.Length || positions.Length < 3 ||
                positions.Concat(texturePositions).Any(point => !float.IsFinite(point.x) || !float.IsFinite(point.y)) ||
                indices == null || indices.Length < 3 || indices.Length % 3 != 0 || indices.Any(index => index < 0 || index >= positions.Length))
                throw new InvalidOperationException("The mesh binding texture mapping is incomplete.");
        }

        internal Vector2 TexturePosition(Vector2 position, int vertexIndex = -1)
        {
            if (positions == null || texturePositions == null || positions.Length != texturePositions.Length) return position;
            if (vertexIndex >= 0 && vertexIndex < positions.Length && (positions[vertexIndex] - position).sqrMagnitude < 1e-6f)
                return texturePositions[vertexIndex];
            for (var i = 0; i < positions.Length; ++i)
                if ((positions[i] - position).sqrMagnitude < 1e-8f) return texturePositions[i];
            var best = float.PositiveInfinity;
            var result = position;
            // Retain the texture mapping when Edit Mesh subsequently adds or moves vertices.
            for (var i = 0; indices != null && i + 2 < indices.Length; i += 3)
            {
                var a = indices[i]; var b = indices[i + 1]; var c = indices[i + 2];
                var ab = positions[b] - positions[a]; var ac = positions[c] - positions[a]; var offset = position - positions[a];
                var determinant = ab.x * ac.y - ab.y * ac.x;
                if (Mathf.Abs(determinant) < 1e-8f) continue;
                var y = (offset.x * ac.y - offset.y * ac.x) / determinant;
                var z = (ab.x * offset.y - ab.y * offset.x) / determinant;
                var x = 1 - y - z;
                var distance = Mathf.Max(0, -x) + Mathf.Max(0, -y) + Mathf.Max(0, -z);
                if (distance >= best) continue;
                best = distance;
                result = texturePositions[a] * x + texturePositions[b] * y + texturePositions[c] * z;
                if (distance == 0) break;
            }
            return result;
        }

        internal static string UserData(ISpriteEditorDataProvider provider, AssetImporter importer) =>
            provider is UniskinRigDataProvider rig ? rig.data.userData : importer != null ? importer.userData : string.Empty;

        internal static void Apply(ISpriteEditorDataProvider provider, Sprite[] sprites, string userData)
        {
            var document = ReadDocument(userData);
            if (document.sprites.Count == 0) return;
            foreach (var sprite in sprites)
            {
                var pose = document.sprites.FirstOrDefault(item => item.id == sprite.GetSpriteID().ToString());
                if (pose == null) continue;
                pose.Validate();
                var frame = provider.GetSpriteRects().First(rect => rect.spriteID.ToString() == pose.id).rect;
                var toLocal = PixelsToLocal(sprite, frame);
                var toPixels = toLocal.inverse;
                var bones = sprite.GetBones();
                var bindPoses = new NativeArray<Matrix4x4>(sprite.GetBindPoses().ToArray(), Allocator.Temp);
                try
                {
                    for (var i = 0; i < Mathf.Min(bindPoses.Length, bones.Length); ++i)
                    {
                        var binding = pose.FindBone(bones[i].guid);
                        if (binding.HasValue)
                            bindPoses[i] = Matrix4x4.Scale(Vector3.one / provider.pixelsPerUnit) * binding.Value * toPixels;
                    }
                    sprite.SetBindPoses(bindPoses);
                }
                finally { bindPoses.Dispose(); }
                ApplyTextureCoordinates(sprite, frame, pose);
            }
        }

        internal static void ApplyTextureCoordinates(Sprite sprite, Rect frame, SceneMeshBindingPose pose)
        {
            var vertices = sprite.GetVertexAttribute<Vector3>(VertexAttribute.Position);
            var toPixels = PixelsToLocal(sprite, frame).inverse;
            var textureRect = sprite.textureRect;
            var offset = sprite.textureRectOffset;
            var textureSize = new Vector2(sprite.texture.width, sprite.texture.height);
            var uv = new NativeArray<Vector2>(vertices.Length, Allocator.Temp);
            try
            {
                for (var i = 0; i < uv.Length; ++i)
                {
                    var pixel = pose.TexturePosition(toPixels.MultiplyPoint3x4(vertices[i]), i);
                    uv[i] = (textureRect.position - offset + Vector2.Scale(pixel / frame.size, sprite.rect.size)) / textureSize;
                }
                sprite.SetVertexAttribute(VertexAttribute.TexCoord0, uv);
            }
            finally { uv.Dispose(); }
        }
    }
}
