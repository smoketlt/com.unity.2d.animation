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
            if (editor.targets.Length != 1 || !(editor.target is GameObject gameObject) ||
                gameObject.GetComponent<SpriteRenderer>() != null ||
                !TryGetSource(gameObject.transform, out var renderer, out var boneGuid)) return;

            if (s_EditBone == null)
                s_EditBone = new GUIContent("Edit Bone",
                    ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/EditBone.png"),
                    "Edit this bone in the source Sprite/PSB skeleton.");
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (!GUILayout.Button(s_EditBone, GUILayout.Height(28))) return;
                EditorApplication.delayCall += () =>
                {
                    // Re-resolve after the GUI event in case bindings or selection changed.
                    if (gameObject != null && TryGetSource(gameObject.transform, out renderer, out boneGuid))
                        SceneEditBoneWindow.OpenForBone(renderer, boneGuid);
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
                if (candidate == null || candidate.sprite == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(candidate.sprite))) continue;
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
                .GroupBy(candidate => AssetDatabase.GetAssetPath(candidate.sprite));
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
                    .Where(index => MatchesCharacterPath(bone, host, character.bones, index)).ToArray();
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

        internal static Transform FindCharacterTransform(Transform host, SpriteBone[] bones, int index)
        {
            var matches = host.GetComponentsInChildren<Transform>(true)
                .Where(transform => MatchesCharacterPath(transform, host, bones, index)).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        // PSD Importer creates character Transforms using the author's PPU. A resized
        // texture also scales Sprite.pixelsPerUnit, which must not stretch these bones.
        // Runtime Sprite.GetBones() already includes the texture definition scale.
        internal static float CharacterBoneLocalLength(ISpriteEditorDataProvider provider, float sourceLength) =>
            sourceLength / provider.pixelsPerUnit;
    }
}
