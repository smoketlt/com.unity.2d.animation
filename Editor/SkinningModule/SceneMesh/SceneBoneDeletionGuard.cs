using System;
using System.Linq;

namespace UnityEditor.U2D.Animation
{
    internal static class SceneBoneDeletionGuard
    {
        internal static bool Confirm(SkinningCache cache, BoneCache[] bones, Func<string, bool> confirm)
        {
            var guids = bones.Where(bone => bone != null).Select(bone => bone.guid).ToArray();
            var affected = cache.GetSprites().Where(sprite =>
            {
                var mesh = sprite.GetMesh();
                return mesh != null && mesh.vertexWeights.Any(weight => weight.Any(channel =>
                    channel.enabled && channel.weight > 0 && channel.boneIndex >= 0 && channel.boneIndex < mesh.bones.Length &&
                    mesh.bones[channel.boneIndex] != null && guids.Contains(mesh.bones[channel.boneIndex].guid)));
            }).Select(sprite => sprite.name).Distinct().OrderBy(name => name).ToArray();
            if (affected.Length == 0) return true;
            return confirm("The selected bones still have vertex weights. Deleting them removes those influences and redistributes the remaining weights. " +
                "The affected meshes may change shape when Edit Bones is saved or closed. Changing a bone's parent does not transfer its weights.\n\n" +
                "Affected meshes:\n" + string.Join("\n", affected) + "\n\nCancel to transfer the weights in Edit Weights before deleting these bones.");
        }
    }
}
