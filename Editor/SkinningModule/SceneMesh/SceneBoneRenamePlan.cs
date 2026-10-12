using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    // Capture old paths before importing the renamed skeleton. Never search/replace
    // individual name fragments: a descendant path can contain several renamed bones.
    internal sealed class SceneBoneRenamePlan
    {
        readonly Dictionary<Transform, string> m_Names;
        readonly Dictionary<AnimationClip, Dictionary<string, string>> m_Paths = new Dictionary<AnimationClip, Dictionary<string, string>>();

        internal static SceneBoneRenamePlan Create(SceneMeshSource source, SkeletonCache skeleton)
        {
            var provider = source.provider.GetDataProvider<ICharacterDataProvider>();
            var original = provider?.GetCharacterData().bones ?? source.provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(source.rect.spriteID).ToArray();
            var desired = skeleton.bones.ToDictionary(bone => bone.guid, bone => bone.name);
            var changed = original.Where(bone => desired.TryGetValue(bone.guid, out var name) && bone.name != name)
                .ToDictionary(bone => bone.guid, bone => desired[bone.guid]);
            var names = new Dictionary<Transform, string>();
            var hosts = new HashSet<Transform>();
            foreach (var renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
            {
                if (!renderer.gameObject.scene.IsValid() || EditorUtility.IsPersistent(renderer) || renderer.sprite == null ||
                    UniskinRigUtility.SourcePath(renderer.sprite) != source.assetPath) continue;
                var instance = PrefabUtility.GetNearestPrefabInstanceRoot(renderer.gameObject);
                var host = instance != null ? instance.transform : renderer.transform.root;
                hosts.Add(host);
                var skin = renderer.GetComponent<SpriteSkin>();
                var bones = renderer.sprite.GetBones();
                var transforms = skin != null ? skin.boneTransforms : null;
                if (transforms == null) continue;
                for (var i = 0; i < Math.Min(bones.Length, transforms.Length); ++i)
                    if (transforms[i] != null && changed.TryGetValue(bones[i].guid, out var name)) names[transforms[i]] = name;
            }
            if (provider != null)
                foreach (var host in hosts)
                    for (var i = 0; i < original.Length; ++i)
                    {
                        if (!changed.TryGetValue(original[i].guid, out var name)) continue;
                        var transform = SceneBoneInspector.FindCharacterTransform(host, original, i);
                        if (transform != null) names[transform] = name;
                    }
            return new SceneBoneRenamePlan(names);
        }

        internal SceneBoneRenamePlan(Dictionary<Transform, string> names)
        {
            m_Names = names;
            foreach (var pair in names)
            {
                var siblings = pair.Key.parent != null ? pair.Key.parent.Cast<Transform>() :
                    pair.Key.gameObject.scene.GetRootGameObjects().Select(obj => obj.transform);
                if (siblings.Any(other => other != pair.Key && FinalName(other) == pair.Value))
                    throw new InvalidOperationException("A sibling GameObject already uses the bone name '" + pair.Value + "'.");
            }
            var roots = new Dictionary<Transform, HashSet<AnimationClip>>();
            foreach (var bone in names.Keys)
                for (var ancestor = bone; ancestor != null; ancestor = ancestor.parent)
                {
                    var animator = ancestor.GetComponent<Animator>();
                    var animation = ancestor.GetComponent<UnityEngine.Animation>();
                    var clips = new List<AnimationClip>();
                    if (animator != null && animator.runtimeAnimatorController != null)
                        clips.AddRange(animator.runtimeAnimatorController.animationClips);
                    if (animation != null) clips.AddRange(AnimationUtility.GetAnimationClips(ancestor.gameObject));
                    if (clips.Count == 0) continue;
                    if (!roots.TryGetValue(ancestor, out var set)) roots[ancestor] = set = new HashSet<AnimationClip>();
                    foreach (var clip in clips) if (clip != null) set.Add(clip);
                }
            foreach (var root in roots)
            {
                var paths = new Dictionary<string, string>();
                foreach (var transform in root.Key.GetComponentsInChildren<Transform>(true))
                {
                    var oldPath = AnimationUtility.CalculateTransformPath(transform, root.Key);
                    var segments = new List<string>();
                    for (var current = transform; current != root.Key; current = current.parent) segments.Add(FinalName(current));
                    segments.Reverse();
                    var newPath = string.Join("/", segments);
                    if (oldPath == newPath) continue;
                    if (paths.TryGetValue(oldPath, out var previous) && previous != newPath)
                        throw new InvalidOperationException("Ambiguous animation path: " + oldPath);
                    paths[oldPath] = newPath;
                }
                if (root.Key.GetComponentsInChildren<Transform>(true)
                    .GroupBy(transform => AnimationUtility.CalculateTransformPath(transform, root.Key))
                    .Any(group => group.Count() > 1 && paths.ContainsKey(group.Key)))
                    throw new InvalidOperationException("Duplicate GameObject paths prevent safe animation binding updates.");
                foreach (var clip in root.Value)
                {
                    if (!m_Paths.TryGetValue(clip, out var combined)) m_Paths[clip] = combined = new Dictionary<string, string>();
                    // Include unchanged bindings too: a shared clip must have the same
                    // resulting path in every connected animation root.
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                    {
                        var target = paths.TryGetValue(binding.path, out var path) ? path : binding.path;
                        if (combined.TryGetValue(binding.path, out var prior) && prior != target)
                            throw new InvalidOperationException("Shared animation clip has incompatible bone paths: " + clip.name);
                        combined[binding.path] = target;
                    }
                }
            }
            foreach (var pair in m_Paths)
            {
                var bindings = AnimationUtility.GetCurveBindings(pair.Key).Concat(AnimationUtility.GetObjectReferenceCurveBindings(pair.Key)).ToArray();
                var targets = new HashSet<EditorCurveBinding>();
                foreach (var binding in bindings)
                {
                    var target = binding;
                    target.path = pair.Value[binding.path];
                    if (!targets.Add(target)) throw new InvalidOperationException("Renaming would overwrite an animation binding in " + pair.Key.name);
                }
                if (!pair.Value.Any(path => path.Key != path.Value)) continue;
                var assetPath = AssetDatabase.GetAssetPath(pair.Key);
                if (!string.IsNullOrEmpty(assetPath) && (!assetPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) ||
                    !AssetDatabase.IsOpenForEdit(pair.Key)))
                    throw new InvalidOperationException("Animation clip '" + pair.Key.name + "' must be an editable .anim asset before renaming this bone.");
            }
        }

        string FinalName(Transform transform) => m_Names.TryGetValue(transform, out var name) ? name : transform.name;

        internal void Apply()
        {
            foreach (var pair in m_Paths)
            {
                if (!pair.Value.Any(path => path.Key != path.Value)) continue;
                var clip = pair.Key;
                var curves = AnimationUtility.GetCurveBindings(clip).Where(binding => pair.Value[binding.path] != binding.path)
                    .Select(binding => (binding, curve: AnimationUtility.GetEditorCurve(clip, binding))).ToArray();
                var objects = AnimationUtility.GetObjectReferenceCurveBindings(clip).Where(binding => pair.Value[binding.path] != binding.path)
                    .Select(binding => (binding, curve: AnimationUtility.GetObjectReferenceCurve(clip, binding))).ToArray();
                Undo.RegisterCompleteObjectUndo(clip, "Rename Bone Animation Bindings");
                foreach (var entry in curves) AnimationUtility.SetEditorCurve(clip, entry.binding, null);
                foreach (var entry in objects) AnimationUtility.SetObjectReferenceCurve(clip, entry.binding, null);
                foreach (var entry in curves)
                {
                    var binding = entry.binding; binding.path = pair.Value[binding.path];
                    AnimationUtility.SetEditorCurve(clip, binding, entry.curve);
                }
                foreach (var entry in objects)
                {
                    var binding = entry.binding; binding.path = pair.Value[binding.path];
                    AnimationUtility.SetObjectReferenceCurve(clip, binding, entry.curve);
                }
                EditorUtility.SetDirty(clip);
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(clip))) AssetDatabase.SaveAssetIfDirty(clip);
            }
            foreach (var pair in m_Names)
            {
                Undo.RegisterCompleteObjectUndo(pair.Key.gameObject, "Rename Bone GameObject");
                pair.Key.gameObject.name = pair.Value;
                if (PrefabUtility.IsPartOfPrefabInstance(pair.Key.gameObject))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(pair.Key.gameObject);
                EditorSceneManager.MarkSceneDirty(pair.Key.gameObject.scene);
            }
        }
    }
}
