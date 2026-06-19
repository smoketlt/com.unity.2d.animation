using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    [InitializeOnLoad]
    internal static class SpriteSkinBoneNameNormalizer
    {
        static bool s_NormalizeScheduled;

        static SpriteSkinBoneNameNormalizer()
        {
            EditorApplication.hierarchyChanged += ScheduleNormalize;
            ScheduleNormalize();
        }

        static void ScheduleNormalize()
        {
            if (s_NormalizeScheduled || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            s_NormalizeScheduled = true;
            EditorApplication.delayCall += NormalizeLoadedSpriteSkins;
        }

        static void NormalizeLoadedSpriteSkins()
        {
            s_NormalizeScheduled = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            SpriteSkin[] spriteSkins = Resources.FindObjectsOfTypeAll<SpriteSkin>();
            for (int i = 0; i < spriteSkins.Length; ++i)
                NormalizeSpriteSkin(spriteSkins[i]);
        }

        static void NormalizeSpriteSkin(SpriteSkin spriteSkin)
        {
            if (spriteSkin == null || !spriteSkin.gameObject.scene.IsValid())
                return;

            SpriteRenderer spriteRenderer = spriteSkin.GetComponent<SpriteRenderer>();
            Sprite sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
            Transform[] boneTransforms = spriteSkin.boneTransforms;
            if (sprite == null || boneTransforms == null)
                return;

            SpriteBone[] spriteBones = sprite.GetBones();
            int count = Mathf.Min(spriteBones.Length, boneTransforms.Length);
            for (int i = 0; i < count; ++i)
            {
                Transform boneTransform = boneTransforms[i];
                string desiredName = spriteBones[i].name;
                if (boneTransform == null || string.IsNullOrEmpty(desiredName) || boneTransform.name == desiredName)
                    continue;

                if (!HasGeneratedNumericSuffix(boneTransform.name, desiredName) || HasSiblingNamed(boneTransform, desiredName))
                    continue;

                Undo.RecordObject(boneTransform, "Normalize SpriteSkin Bone Name");
                boneTransform.name = desiredName;
                PrefabUtility.RecordPrefabInstancePropertyModifications(boneTransform);
                EditorUtility.SetDirty(boneTransform);
                EditorSceneManager.MarkSceneDirty(boneTransform.gameObject.scene);
            }
        }

        static bool HasGeneratedNumericSuffix(string currentName, string desiredName)
        {
            string prefix = desiredName + "_";
            if (!currentName.StartsWith(prefix, StringComparison.Ordinal) || currentName.Length == prefix.Length)
                return false;

            for (int i = prefix.Length; i < currentName.Length; ++i)
            {
                if (!char.IsDigit(currentName[i]))
                    return false;
            }

            return true;
        }

        static bool HasSiblingNamed(Transform transform, string name)
        {
            Transform parent = transform.parent;
            if (parent == null)
                return false;

            for (int i = 0; i < parent.childCount; ++i)
            {
                Transform sibling = parent.GetChild(i);
                if (sibling != transform && sibling.name == name)
                    return true;
            }

            return false;
        }
    }
}
