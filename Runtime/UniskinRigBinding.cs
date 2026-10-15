using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.U2D;

namespace UnityEngine.U2D.Animation
{
    /// <summary>Keeps a scene or prefab instance bound to the persistent Uniskin rig.</summary>
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("2D Animation/Uniskin Rig Binding")]
    [DefaultExecutionOrder(UpdateOrder.spriteResolverUpdateOrder + 1)]
    public sealed class UniskinRigBinding : MonoBehaviour
    {
        [Serializable]
        public sealed class BoneBinding
        {
            public string guid;
            public Transform transform;
            public bool created;
            public SpriteBone sourcePose;
        }

        public UniskinRig rig;
        [Tooltip("Show this rig's bones in the Scene view even when it is not selected. Requires Scene view Gizmos.")]
        public bool alwaysShowBones;
        [HideInInspector] public SpriteRenderer[] renderers = Array.Empty<SpriteRenderer>();
        [HideInInspector] public BoneBinding[] bones = Array.Empty<BoneBinding>();
        [NonSerialized] int m_AppliedRevision = -1;
        public int appliedRevision => m_AppliedRevision;

        void OnEnable() => Apply();

        void LateUpdate()
        {
            if (rig == null || !rig.editingEnabled) return;
            if (m_AppliedRevision != rig.revision || renderers.Any(renderer =>
                renderer != null && rig.Find(renderer.sprite) is UniskinRig.SpriteEntry entry && renderer.sprite != entry.sprite))
                Apply();
        }

        // Bone GameObjects are authored by the Editor with Undo. Runtime needs only the
        // serialized GUID -> Transform map and generated Sprite subassets, never an importer.
        public void Apply()
        {
            if (rig == null) return;
            if (Application.isPlaying && m_AppliedRevision != rig.revision) UpdateRuntimeBones();
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var entry = rig.Find(renderer.sprite);
                if (entry == null) continue;
                var sprite = rig.editingEnabled ? entry.sprite : entry.source;
                if (sprite == null) continue;
                renderer.sprite = sprite;
                var spriteBones = sprite.GetBones();
                var skin = renderer.GetComponent<SpriteSkin>();
                if (skin == null && Application.isPlaying && spriteBones.Length > 0)
                    skin = renderer.gameObject.AddComponent<SpriteSkin>();
                if (skin == null) continue;
                var transforms = spriteBones.Select(bone => Array.Find(bones, binding => binding.guid == bone.guid)?.transform).ToArray();
                // Do not publish incomplete arrays during source/prefab regeneration.
                if (transforms.Any(transform => transform == null)) continue;
                skin.SetRootBone(transform);
                skin.SetBoneTransforms(transforms);
                skin.ResetSprite();
            }
            m_AppliedRevision = rig.revision;
        }

        void UpdateRuntimeBones()
        {
            var hierarchyPoses = CaptureHierarchyPoses(rig, bones, transform);
            var links = bones.Where(binding => binding.transform != null).ToDictionary(binding => binding.guid);
            var created = new Transform[rig.characterBones.Length];
            for (var i = 0; i < created.Length; ++i) Ensure(i);
            foreach (var obsolete in links.Values.Where(link => !rig.characterBones.Any(bone => bone.guid == link.guid)))
            {
                foreach (var child in obsolete.transform.Cast<Transform>().ToArray()) child.SetParent(obsolete.transform.parent, true);
                Destroy(obsolete.transform.gameObject);
            }
            bones = rig.characterBones.Select(bone => links[bone.guid]).ToArray();

            Transform Ensure(int index)
            {
                if (created[index] != null) return created[index];
                var bone = rig.characterBones[index];
                var parent = bone.parentId >= 0 ? Ensure(bone.parentId) : transform;
                if (!links.TryGetValue(bone.guid, out var link))
                {
                    var child = new GameObject(bone.name).transform;
                    child.SetParent(parent, false);
                    child.localPosition = bone.position / rig.pixelsPerUnit + (bone.parentId < 0 ? (Vector3)rig.rootOffset : Vector3.zero);
                    child.localRotation = bone.rotation;
                    links[bone.guid] = link = new BoneBinding { guid = bone.guid, transform = child, created = true, sourcePose = bone };
                }
                else
                {
                    var parentChanged = link.transform.parent != parent;
                    if (parentChanged)
                    {
                        // Keep the instance's pose when adopting a changed hierarchy.
                        // Source local coordinates belong to the bind pose, which can
                        // differ from the current animated/prefab pose.
                        link.transform.SetParent(parent, true);
                    }
                    if (hierarchyPoses.TryGetValue(bone.guid, out var pose))
                        link.transform.SetPositionAndRotation(pose.position, pose.rotation);
                    else if (!parentChanged)
                    {
                        link.transform.localPosition += (bone.position - link.sourcePose.position) / rig.pixelsPerUnit;
                        link.transform.localRotation = bone.rotation * Quaternion.Inverse(link.sourcePose.rotation) * link.transform.localRotation;
                    }
                    if (link.sourcePose.name != bone.name) link.transform.name = bone.name;
                    link.sourcePose = bone;
                }
                return created[index] = link.transform;
            }
        }

        // Capture before changing any parent or baseline. Local source coordinates
        // from different hierarchies cannot be subtracted. Transfer the authored
        // world-space delta through each bone's current scene-pose conversion.
        internal static Dictionary<string, Pose> CaptureHierarchyPoses(UniskinRig rig, BoneBinding[] bindings, Transform root)
        {
            var result = new Dictionary<string, Pose>();
            var previous = bindings.Select(binding => binding.sourcePose).ToArray();
            var next = rig.characterBones;
            string ParentGuid(SpriteBone[] values, int index) => index >= 0 && index < values.Length ? values[index].guid : null;
            var sourceHierarchyChanged = previous.Any(bone => Array.Exists(next, candidate => candidate.guid == bone.guid &&
                ParentGuid(previous, bone.parentId) != ParentGuid(next, candidate.parentId)));
            var sceneHierarchyChanged = bindings.Any(binding => binding.transform != null && Array.Exists(next, bone =>
                bone.guid == binding.guid && binding.transform.parent != (bone.parentId < 0 ? root :
                    Array.Find(bindings, parent => parent.guid == next[bone.parentId].guid)?.transform)));
            if (!sourceHierarchyChanged && !sceneHierarchyChanged) return result;
            Matrix4x4[] Matrices(SpriteBone[] values)
            {
                var matrices = new Matrix4x4[values.Length];
                var ready = new bool[values.Length];
                Matrix4x4 Build(int i)
                {
                    if (ready[i]) return matrices[i];
                    var bone = values[i];
                    var local = Matrix4x4.TRS(bone.position / rig.pixelsPerUnit +
                        (bone.parentId < 0 ? (Vector3)rig.rootOffset : Vector3.zero), bone.rotation, Vector3.one);
                    matrices[i] = bone.parentId >= 0 ? Build(bone.parentId) * local : local;
                    ready[i] = true;
                    return matrices[i];
                }
                for (var i = 0; i < values.Length; ++i) Build(i);
                return matrices;
            }
            var before = Matrices(previous);
            var after = Matrices(next);
            for (var i = 0; i < bindings.Length; ++i)
            {
                var binding = bindings[i];
                var index = Array.FindIndex(next, bone => bone.guid == binding.guid);
                if (binding.transform == null || index < 0) continue;
                var delta = before[i].inverse * after[index];
                result[binding.guid] = new Pose(binding.transform.TransformPoint(delta.MultiplyPoint3x4(Vector3.zero)),
                    binding.transform.rotation * delta.rotation);
            }
            return result;
        }
    }
}
