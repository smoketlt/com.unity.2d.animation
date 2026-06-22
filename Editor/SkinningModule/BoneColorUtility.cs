using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal static class BoneColorUtility
    {
        public static Color DefaultCreatedBoneColor => SkinningModuleSettings.defaultBoneColor;

        static readonly Color32[] kWeightMapColors =
        {
            new Color32(0x36, 0xcd, 0xff, 0xff),
            new Color32(0xff, 0x36, 0xcd, 0xff),
            new Color32(0x68, 0x36, 0xff, 0xff),
            new Color32(0xff, 0x89, 0x15, 0xff),
            new Color32(0x9a, 0xff, 0x36, 0xff),
            new Color32(0x36, 0x9a, 0xff, 0xff),
            new Color32(0x36, 0xff, 0x9a, 0xff),
            new Color32(0x9a, 0x36, 0xff, 0xff),
            new Color32(0xff, 0x36, 0x9a, 0xff),
            new Color32(0x68, 0xff, 0x36, 0xff),
            new Color32(0xff, 0xcd, 0x36, 0xff),
            new Color32(0x36, 0xff, 0xcd, 0xff),
            new Color32(0x36, 0x68, 0xff, 0xff),
            new Color32(0xff, 0x36, 0x68, 0xff),
            new Color32(0xcd, 0x36, 0xff, 0xff),
            new Color32(0xff, 0xff, 0x36, 0xff),
            new Color32(0xff, 0x36, 0x36, 0xff),
            new Color32(0x36, 0xff, 0x36, 0xff),
            new Color32(0x36, 0xff, 0xff, 0xff),
            new Color32(0xff, 0x36, 0xff, 0xff),
            new Color32(0xcd, 0xff, 0x36, 0xff),
            new Color32(0x36, 0x36, 0xff, 0xff),
            new Color32(0x36, 0xff, 0x68, 0xff),
            new Color32(0xff, 0x4e, 0x13, 0xff),
        };

        public static Color GetWeightMapColor(int index)
        {
            if (index < 0)
                return Color.gray;

            return kWeightMapColors[index % kWeightMapColors.Length];
        }

        public static Color GetWeightMapColor(BoneCache bone)
        {
            if (bone == null)
                return Color.gray;

            SkeletonCache skeleton = bone.skeleton;
            if (skeleton == null)
                return Color.gray;

            return GetWeightMapColor(skeleton.IndexOf(bone));
        }
    }
}
