using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    // Animation positions are absolute local coordinates. Rebuilding SpriteSkin
    // alone cannot move keyed pivots: the next sample writes the old position back.
    internal sealed class SceneBoneAnimationPositionPlan
    {
        sealed class Track
        {
            public AnimationClip clip;
            public Transform root, bone;
            public EditorCurveBinding binding;
            public float before;
            public int axis;
        }

        readonly List<Track> m_Tracks = new List<Track>();

        internal static SceneBoneAnimationPositionPlan Create(SceneMeshSource source, IEnumerable<BoneCache> affected)
        {
            var plan = new SceneBoneAnimationPositionPlan();
            var guids = new HashSet<string>(affected.Select(bone => bone.guid));
            var character = source.provider.GetDataProvider<ICharacterDataProvider>();
            var original = character?.GetCharacterData().bones;
            var transforms = new HashSet<Transform>();
            var hosts = new HashSet<Transform>();
            foreach (var renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
            {
                if (!renderer.gameObject.scene.IsValid() || EditorUtility.IsPersistent(renderer) || renderer.sprite == null ||
                    UniskinRigUtility.SourcePath(renderer.sprite) != source.assetPath) continue;
                var instance = PrefabUtility.GetNearestPrefabInstanceRoot(renderer.gameObject);
                hosts.Add(instance != null ? instance.transform : renderer.transform.root);
                var skin = renderer.GetComponent<SpriteSkin>();
                var bones = renderer.sprite.GetBones();
                var bindings = skin != null ? skin.boneTransforms : null;
                if (bindings == null) continue;
                for (var i = 0; i < Mathf.Min(bones.Length, bindings.Length); ++i)
                    if (bindings[i] != null && guids.Contains(bones[i].guid)) transforms.Add(bindings[i]);
            }
            if (original != null)
                foreach (var host in hosts)
                    for (var i = 0; i < original.Length; ++i)
                    {
                        if (!guids.Contains(original[i].guid)) continue;
                        var transform = SceneBoneInspector.FindCharacterTransform(host, original, i);
                        if (transform != null) transforms.Add(transform);
                    }
            var roots = new HashSet<Transform>();
            foreach (var transform in transforms)
                for (var ancestor = transform; ancestor != null; ancestor = ancestor.parent) roots.Add(ancestor);
            foreach (var root in roots)
            {
                var animator = root.GetComponent<Animator>();
                var animation = root.GetComponent<UnityEngine.Animation>();
                var clips = new HashSet<AnimationClip>();
                if (animator != null && animator.runtimeAnimatorController != null)
                    foreach (var clip in animator.runtimeAnimatorController.animationClips) if (clip != null) clips.Add(clip);
                if (animation != null)
                    foreach (var clip in AnimationUtility.GetAnimationClips(root.gameObject)) if (clip != null) clips.Add(clip);
                foreach (var clip in clips)
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        var axis = PositionAxis(binding);
                        if (axis < 0) continue;
                        var transform = string.IsNullOrEmpty(binding.path) ? root : root.Find(binding.path);
                        if (transform == null || !transforms.Contains(transform)) continue;
                        var path = AssetDatabase.GetAssetPath(clip);
                        if (!string.IsNullOrEmpty(path) && (!path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) || !AssetDatabase.IsOpenForEdit(clip)))
                            throw new InvalidOperationException("Animation clip '" + clip.name + "' must be an editable .anim asset before redrawing this bone.");
                        plan.m_Tracks.Add(new Track { clip = clip, root = root, bone = transform, binding = binding,
                            axis = axis, before = transform.localPosition[axis] });
                    }
            }
            return plan;
        }

        static int PositionAxis(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Transform)) return -1;
            if (binding.propertyName == "m_LocalPosition.x") return 0;
            if (binding.propertyName == "m_LocalPosition.y") return 1;
            if (binding.propertyName == "m_LocalPosition.z") return 2;
            return -1;
        }

        internal void Apply()
        {
            var changes = new Dictionary<AnimationClip, Dictionary<EditorCurveBinding, float>>();
            foreach (var track in m_Tracks)
            {
                if (track.bone == null || track.root == null) continue;
                // Other staged edits can rename a bone during the same source save.
                var binding = track.binding;
                binding.path = AnimationUtility.CalculateTransformPath(track.bone, track.root);
                var curve = AnimationUtility.GetEditorCurve(track.clip, binding);
                if (curve == null || curve.length == 0) continue;
                var keys = curve.keys;
                // Constant position tracks commonly accompany rotation-only clips.
                // Anchor them to the new joint, also repairing stale constant keys
                // from earlier redraws. Retain small recording noise and tangents.
                var samples = keys.SelectMany((key, i) => i + 1 < keys.Length ?
                    Enumerable.Range(0, 4).Select(step => curve.Evaluate(Mathf.Lerp(key.time, keys[i + 1].time, step / 4f))) :
                    new[] { key.value }).ToArray();
                var constant = samples.Max() - samples.Min() <= .00001f;
                var delta = track.bone.localPosition[track.axis] - (constant ? curve.Evaluate(0) : track.before);
                if (Mathf.Abs(delta) < .0000001f) continue;
                if (!changes.TryGetValue(track.clip, out var edits)) changes[track.clip] = edits = new Dictionary<EditorCurveBinding, float>();
                if (edits.TryGetValue(binding, out var previous) && Mathf.Abs(previous - delta) > .00001f)
                    throw new InvalidOperationException("Shared animation clip has incompatible bone position offsets: " + track.clip.name);
                edits[binding] = delta;
            }
            foreach (var clip in changes)
            {
                Undo.RegisterCompleteObjectUndo(clip.Key, "Recreate Bone Animation Positions");
                foreach (var edit in clip.Value)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip.Key, edit.Key);
                    var keys = curve.keys;
                    for (var i = 0; i < keys.Length; ++i) keys[i].value += edit.Value;
                    curve.keys = keys;
                    AnimationUtility.SetEditorCurve(clip.Key, edit.Key, curve);
                }
                EditorUtility.SetDirty(clip.Key);
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(clip.Key))) AssetDatabase.SaveAssetIfDirty(clip.Key);
            }
        }
    }
}
