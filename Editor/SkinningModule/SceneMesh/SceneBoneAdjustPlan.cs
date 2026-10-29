using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    internal sealed class SceneBoneAdjustPlan
    {
        readonly Dictionary<Transform, Matrix4x4> m_World = new Dictionary<Transform, Matrix4x4>();
        readonly Dictionary<Transform, (Vector3 position, Quaternion rotation, Vector3 scale)> m_Local =
            new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();
        readonly List<(UniskinClippingMaskData asset, string data)> m_Contours = new List<(UniskinClippingMaskData, string)>();
        string m_UserData;
        readonly List<(Sprite sprite, Matrix4x4[] poses)> m_Bindings = new List<(Sprite, Matrix4x4[])>();

        internal static SceneBoneAdjustPlan Create(SceneMeshSource source, Dictionary<string, Quaternion> rotations)
            => Create(source, rotations.ToDictionary(pair => pair.Key, pair => Matrix4x4.Rotate(pair.Value)));

        internal static SceneBoneAdjustPlan Create(SceneMeshSource source, Dictionary<string, Matrix4x4> rotations)
        {
            var plan = new SceneBoneAdjustPlan();
            plan.m_UserData = plan.CompensateBindings(source, rotations);
            if (source.rig != null)
                foreach (var guid in AssetDatabase.FindAssets("t:UniskinClippingMaskData"))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UniskinClippingMaskData>(AssetDatabase.GUIDToAssetPath(guid));
                    if (asset == null || asset.rig != source.rig || asset.sprite == null) continue;
                    var contour = new SceneMeshSource(asset.sprite, validateClippingImage: false);
                    var provider = (UniskinRigDataProvider)contour.provider;
                    provider.data.userData = plan.CompensateBindings(contour, rotations);
                    plan.m_Contours.Add((asset, provider.data.Serialize()));
                }

            var targets = new Dictionary<Transform, Matrix4x4>();
            var hosts = new HashSet<Transform>();
            foreach (var renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
            {
                if (!renderer.gameObject.scene.IsValid() || EditorUtility.IsPersistent(renderer) || renderer.sprite == null ||
                    UniskinRigUtility.SourcePath(renderer.sprite) != source.assetPath) continue;
                var instance = PrefabUtility.GetNearestPrefabInstanceRoot(renderer.gameObject);
                hosts.Add(instance != null ? instance.transform : renderer.transform.root);
                var skin = renderer.GetComponent<SpriteSkin>();
                var bones = renderer.sprite.GetBones();
                if (skin == null || skin.boneTransforms == null) continue;
                for (var i = 0; i < Mathf.Min(bones.Length, skin.boneTransforms.Length); ++i)
                    if (rotations.TryGetValue(bones[i].guid, out var delta) && skin.boneTransforms[i] != null)
                        targets[skin.boneTransforms[i]] = delta;
            }
            var character = source.provider.GetDataProvider<ICharacterDataProvider>()?.GetCharacterData().bones;
            if (character != null)
                foreach (var host in hosts)
                    for (var i = 0; i < character.Length; ++i)
                        if (rotations.TryGetValue(character[i].guid, out var delta))
                        {
                            var transform = SceneBoneInspector.FindCharacterTransform(host, character, i);
                            if (transform != null) targets[transform] = delta;
                        }
            foreach (var target in targets)
                foreach (var transform in target.Key.GetComponentsInChildren<Transform>(true))
                    plan.m_World[transform] = transform.localToWorldMatrix;
            // Tnew * Bnew = (Told * delta) * (inverse(delta) * Bold).
            foreach (var target in targets)
                plan.m_World[target.Key] = target.Key.localToWorldMatrix * target.Value;
            foreach (var pair in plan.m_World)
            {
                var parent = pair.Key.parent;
                var parentWorld = parent == null ? Matrix4x4.identity :
                    plan.m_World.TryGetValue(parent, out var desired) ? desired : parent.localToWorldMatrix;
                plan.m_Local[pair.Key] = Decompose(parentWorld.inverse * pair.Value);
            }
            return plan;
        }

        internal static (Vector3 position, Quaternion rotation, Vector3 scale) Decompose(Matrix4x4 matrix)
        {
            if (!SceneMeshBindingPose.IsFinite(matrix) || Mathf.Abs(matrix.determinant) < 1e-10f)
                throw new InvalidOperationException("Adjust requires finite transforms with non-zero scale.");
            Vector3 position = matrix.GetColumn(3);
            Vector3 scale = new Vector3(((Vector3)matrix.GetColumn(0)).magnitude,
                ((Vector3)matrix.GetColumn(1)).magnitude, ((Vector3)matrix.GetColumn(2)).magnitude);
            if (matrix.determinant < 0) scale.x = -scale.x;
            var rotation = Quaternion.LookRotation((Vector3)matrix.GetColumn(2) / scale.z, (Vector3)matrix.GetColumn(1) / scale.y);
            var check = Matrix4x4.TRS(position, rotation, scale);
            for (var i = 0; i < 16; ++i)
                if (Mathf.Abs(matrix[i] - check[i]) > .00002f * Mathf.Max(1f, Mathf.Abs(matrix[i])))
                    throw new InvalidOperationException("Adjust cannot preserve this hierarchy exactly because its non-uniform scale introduces shear. Undo the rotation or cancel Adjust.");
            return (position, rotation, scale);
        }

        string CompensateBindings(SceneMeshSource source, Dictionary<string, Matrix4x4> rotations)
        {
            var data = source.userData;
            var boneProvider = source.provider.GetDataProvider<ISpriteBoneDataProvider>();
            foreach (var rect in source.provider.GetSpriteRects())
            {
                var bones = boneProvider.GetBones(rect.spriteID).ToArray();
                if (!bones.Any(bone => rotations.ContainsKey(bone.guid))) continue;
                var vertices = source.meshProvider.GetVertices(rect.spriteID);
                if (vertices == null || vertices.Length < 3) continue;
                var sprite = source.ResolveSprite(rect.spriteID.ToString());
                var runtimeBones = sprite.GetBones();
                var bind = sprite.GetBindPoses();
                var toLocal = SceneMeshBindingPose.PixelsToLocal(sprite, rect.rect);
                var previous = SceneMeshBindingPose.Read(data, rect.spriteID.ToString());
                var pose = new SceneMeshBindingPose
                {
                    id = rect.spriteID.ToString(),
                    positions = previous?.positions ?? vertices.Select(vertex => vertex.position).ToArray(),
                    texturePositions = previous?.texturePositions ?? vertices.Select(vertex => vertex.position).ToArray(),
                    indices = previous?.indices ?? source.meshProvider.GetIndices(rect.spriteID),
                    bones = bones.Select(bone =>
                    {
                        var index = Array.FindIndex(runtimeBones, item => item.guid == bone.guid);
                        var matrix = previous?.FindBone(bone.guid);
                        if (!matrix.HasValue)
                        {
                            if (index < 0 || index >= bind.Length) throw new InvalidOperationException("Reopen Edit Bones: a mesh binding is missing for " + bone.name);
                            matrix = Matrix4x4.Scale(Vector3.one * source.provider.pixelsPerUnit) * bind[index] * toLocal;
                        }
                        if (rotations.TryGetValue(bone.guid, out var delta)) matrix = Matrix4x4.Scale(Vector3.one * source.provider.pixelsPerUnit) * delta.inverse *
                            Matrix4x4.Scale(Vector3.one / source.provider.pixelsPerUnit) * matrix.Value;
                        return new SceneMeshBindingPose.Bone { guid = bone.guid, pixelsToBone = matrix.Value };
                    }).ToArray()
                };
                pose.Validate();
                var toPixels = toLocal.inverse;
                m_Bindings.Add((sprite, runtimeBones.Select((bone, index) =>
                {
                    var adjusted = pose.FindBone(bone.guid);
                    return adjusted.HasValue ? Matrix4x4.Scale(Vector3.one / source.provider.pixelsPerUnit) * adjusted.Value * toPixels : bind[index];
                }).ToArray()));
                data = SceneMeshBindingPose.Write(data, pose);
            }
            return data;
        }

        internal void WriteBindings(SceneMeshSource source)
        {
            source.userData = m_UserData;
            foreach (var contour in m_Contours)
            {
                Undo.RegisterCompleteObjectUndo(contour.asset, "Adjust Clipping Bindings");
                contour.asset.authoringData = contour.data;
                EditorUtility.SetDirty(contour.asset);
            }
        }

        internal void ApplyScenePoses(bool undo = true)
        {
            foreach (var pair in m_Local)
            {
                if (pair.Key == null) throw new InvalidOperationException("A bone object was replaced while saving Adjust.");
                if (undo) Undo.RegisterCompleteObjectUndo(pair.Key, "Adjust Bone Axes");
                pair.Key.localPosition = pair.Value.position;
                pair.Key.localRotation = pair.Value.rotation;
                pair.Key.localScale = pair.Value.scale;
                if (undo)
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(pair.Key);
                    EditorUtility.SetDirty(pair.Key);
                }
            }
            if (undo) foreach (var contour in m_Contours) AssetDatabase.SaveAssetIfDirty(contour.asset);
        }

        internal void ApplyPreview()
        {
            ApplyScenePoses(false);
            foreach (var binding in m_Bindings)
            {
                var poses = new NativeArray<Matrix4x4>(binding.poses, Allocator.Temp);
                try { binding.sprite.SetBindPoses(poses); }
                finally { poses.Dispose(); }
            }
        }
    }
}
