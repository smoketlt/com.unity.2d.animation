using System;
using System.Linq;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    // Extend the native GameObject header without replacing Transform's inspector.
    [InitializeOnLoad]
    internal static class SceneBoneInspector
    {
        static GUIContent s_EditBone;

        static SceneBoneInspector()
        {
            Editor.finishedDefaultHeaderGUI += DrawHeader;
        }

        static void DrawHeader(Editor editor)
        {
            if (!(editor.target is GameObject gameObject) ||
                gameObject.GetComponent<SpriteRenderer>() != null) return;

            var objects = editor.targets.OfType<GameObject>().ToArray();
            if (objects.Length != editor.targets.Length) return;
            if (TryGetSources(objects, out _, out _)) DrawBoneButton(objects);
            if (objects.Length != 1) return;
            if (UniskinRigInspectorControls.TryGetRootSource(gameObject, out var rigRenderer))
                UniskinRigInspectorControls.DrawRoot(gameObject, rigRenderer.sprite);
        }

        internal static bool TryGetSources(GameObject[] objects, out SpriteRenderer renderer, out string[] boneGuids)
        {
            renderer = null;
            boneGuids = Array.Empty<string>();
            if (objects == null || objects.Length == 0) return false;
            var guids = new System.Collections.Generic.List<string>();
            Transform host = null;
            foreach (var item in objects)
            {
                if (item == null || item.GetComponent<SpriteRenderer>() != null ||
                    !TryGetSource(item.transform, out var candidate, out var guid)) return false;
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(candidate.gameObject);
                var binding = candidate.GetComponentInParent<UniskinRigBinding>();
                var candidateHost = binding != null ? binding.transform : root != null ? root.transform : candidate.transform.root;
                if (renderer != null && (host != candidateHost ||
                    UniskinRigUtility.SourcePath(renderer.sprite) != UniskinRigUtility.SourcePath(candidate.sprite))) return false;
                // Ordinary sprites have independent skeletons even in the same image.
                if (renderer != null && renderer.sprite != candidate.sprite &&
                    new SceneMeshSource(candidate.sprite).provider.GetDataProvider<ICharacterDataProvider>() == null) return false;
                renderer = renderer != null ? renderer : candidate;
                host = candidateHost;
                if (!string.IsNullOrEmpty(guid)) guids.Add(guid);
            }
            boneGuids = guids.Distinct().ToArray();
            return true;
        }

        static void DrawBoneButton(GameObject[] objects)
        {

            if (s_EditBone == null)
                s_EditBone = new GUIContent("Edit Bones",
                    ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/EditBone.png"),
                    "Edit this bone in the source Sprite/PSB skeleton.");
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (!GUILayout.Button(s_EditBone, GUILayout.Height(28))) return;
                EditorApplication.delayCall += () =>
                {
                    // Re-resolve after the GUI event in case bindings or selection changed.
                    if (TryGetSources(objects, out var renderer, out var boneGuids))
                        SceneEditBoneWindow.OpenForBones(renderer, boneGuids);
                };
            }
        }

        internal static bool TryGetSource(Transform bone, out SpriteRenderer renderer, out string boneGuid)
        {
            renderer = null;
            boneGuid = null;
            if (bone == null || EditorUtility.IsPersistent(bone) || !bone.gameObject.scene.IsValid()) return false;
            var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(bone.gameObject);
            var host = prefabRoot != null ? prefabRoot.transform : bone.root;
            foreach (var skin in host.GetComponentsInChildren<SpriteSkin>(true))
            {
                var transforms = skin.boneTransforms;
                if (transforms == null) continue;
                var index = Array.IndexOf(transforms, bone);
                if (index < 0) continue;
                var candidate = skin.GetComponent<SpriteRenderer>();
                if (candidate == null || candidate.sprite == null || string.IsNullOrEmpty(UniskinRigUtility.SourcePath(candidate.sprite))) continue;
                var bones = candidate.sprite.GetBones();
                if (index >= bones.Length || string.IsNullOrEmpty(bones[index].guid)) continue;
                renderer = candidate;
                boneGuid = bones[index].guid;
                return true;
            }
            // A character root or an unbound character bone need not occur in boneTransforms.
            // Resolve one shared character source in this hierarchy; never guess between assets.
            var sources = host.GetComponentsInChildren<SpriteRenderer>(true)
                .Where(candidate => candidate.sprite != null)
                .GroupBy(candidate => UniskinRigUtility.SourcePath(candidate.sprite));
            SpriteRenderer found = null;
            string foundGuid = null;
            foreach (var group in sources)
            {
                if (string.IsNullOrEmpty(group.Key)) continue;
                var candidate = group.First();
                SceneMeshSource source;
                try { source = new SceneMeshSource(candidate.sprite); }
                catch (Exception) { continue; }
                var characterProvider = source.provider.GetDataProvider<ICharacterDataProvider>();
                if (characterProvider == null) continue;
                var character = characterProvider.GetCharacterData();
                if (character.bones == null) continue;
                var matches = Enumerable.Range(0, character.bones.Length)
                    .Where(index => FindCharacterTransform(host, character.bones, index) == bone).ToArray();
                var isContainer = group.Any(item => item.transform.IsChildOf(bone));
                if (matches.Length != 1 && !isContainer) continue;
                if (matches.Length > 1 || found != null) return false;
                found = candidate;
                foundGuid = matches.Length == 1 ? character.bones[matches[0]].guid : null;
            }
            renderer = found; boneGuid = foundGuid;
            return renderer != null;
        }

        static bool MatchesCharacterPath(Transform transform, Transform host, SpriteBone[] bones, int index)
        {
            var visited = 0;
            while (index >= 0)
            {
                if (index >= bones.Length || transform == null || !transform.IsChildOf(host) || ++visited > bones.Length)
                    return false;
                var name = bones[index].name;
                if (string.IsNullOrEmpty(name)) return false;
                // PSD Importer may disambiguate a bone name against a sprite-layer name.
                var suffix = transform.name.StartsWith(name + "_", StringComparison.Ordinal) ? transform.name.Substring(name.Length + 1) : null;
                if (transform.name != name && (string.IsNullOrEmpty(suffix) || !suffix.All(char.IsDigit))) return false;
                transform = transform.parent;
                index = bones[index].parentId;
            }
            return true;
        }

        internal static Transform FindCharacterTransform(Transform host, SpriteBone[] bones, int index, Func<Transform, bool> filter = null)
        {
            // A rig's GUID map survives source/scene parent differences. Unweighted
            // bones have no SpriteSkin entry, so both gizmos and Inspector resolution
            // must consult this map before trying the imported name hierarchy.
            var owner = host.GetComponentInParent<UniskinRigBinding>();
            var rigs = host.GetComponentsInChildren<UniskinRigBinding>(true).AsEnumerable();
            if (owner != null) rigs = rigs.Append(owner).Distinct();
            var guid = bones[index].guid;
            var rigMatches = rigs.Where(binding => binding.rig != null && binding.rig.editingEnabled &&
                    binding.rig.characterBones.Any(bone => bone.guid == guid))
                .SelectMany(binding => binding.bones).Where(binding => binding.guid == guid && binding.transform != null &&
                    binding.transform.IsChildOf(host) && (filter == null || filter(binding.transform)))
                .Select(binding => binding.transform).Distinct().Take(2).ToArray();
            if (rigMatches.Length > 0) return rigMatches.Length == 1 ? rigMatches[0] : null;
            var matches = host.GetComponentsInChildren<Transform>(true)
                .Where(transform => (filter == null || filter(transform)) && MatchesCharacterPath(transform, host, bones, index)).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        // PSD Importer creates character Transforms using the author's PPU. A resized
        // texture also scales Sprite.pixelsPerUnit, which must not stretch these bones.
        // Runtime Sprite.GetBones() already includes the texture definition scale.
        internal static float CharacterBoneLocalLength(ISpriteEditorDataProvider provider, float sourceLength) =>
            sourceLength / provider.pixelsPerUnit;
    }
}
