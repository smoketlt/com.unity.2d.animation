using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor.ShortcutManagement;
using UnityEditor.U2D.Common;
using UnityEditor.U2D.Layout;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    internal partial class SkinningModule
    {
        private LayoutOverlay m_LayoutOverlay;
        private PoseToolbar m_PoseToolbar;
        private BoneToolbar m_BoneToolbar;
        private MeshToolbar m_MeshToolbar;
        private WeightToolbar m_WeightToolbar;
        private ConstraintsToolbar m_ConstraintsToolbar;
        private RigToolbar m_RigToolbar;

        private InternalEditorBridge.ShortcutContext m_ShortcutContext;
        private NewGeometrySnapshot m_NewGeometrySnapshot;
        private const string k_BoneTransformCopyPrefix = "Unity2DAnimationSkinningBoneTransformCopy:";

        private class NewGeometrySnapshot
        {
            public Vector2[] vertices;
            public EditableBoneWeight[] vertexWeights;
            public int[] indices;
            public int2[] edges;

            public static NewGeometrySnapshot Capture(MeshCache mesh)
            {
                return new NewGeometrySnapshot
                {
                    vertices = (Vector2[])mesh.vertices.Clone(),
                    vertexWeights = CloneWeights(mesh.vertexWeights),
                    indices = (int[])mesh.indices.Clone(),
                    edges = (int2[])mesh.edges.Clone()
                };
            }

            public void Restore(MeshCache mesh)
            {
                mesh.SetVertices((Vector2[])vertices.Clone(), CloneWeights(vertexWeights));
                mesh.SetEdges((int2[])edges.Clone());
                mesh.SetIndices((int[])indices.Clone());
            }

            private static EditableBoneWeight[] CloneWeights(EditableBoneWeight[] source)
            {
                EditableBoneWeight[] clone = new EditableBoneWeight[source.Length];
                for (int i = 0; i < source.Length; ++i)
                    clone[i] = CloneWeight(source[i]);

                return clone;
            }

            private static EditableBoneWeight CloneWeight(EditableBoneWeight source)
            {
                EditableBoneWeight clone = new EditableBoneWeight();
                if (source == null)
                    return clone;

                foreach (BoneWeightChannel channel in source)
                    clone.AddChannel(channel.boneIndex, channel.weight, channel.enabled);

                return clone;
            }
        }

        [Serializable]
        private class BoneTransformCopyData
        {
            public BoneTransformData[] transforms = Array.Empty<BoneTransformData>();
        }

        [Serializable]
        private struct BoneTransformData
        {
            public Vector3 position;
            public Quaternion rotation;
            public float length;
            public float depth;
        }

        private static SkinningModule GetModuleFromContext(ShortcutArguments args)
        {
            InternalEditorBridge.ShortcutContext sc = args.context as InternalEditorBridge.ShortcutContext;
            if (sc == null)
                return null;

            return sc.context as SkinningModule;
        }

        [Shortcut(ShortcutIds.toggleToolText, typeof(InternalEditorBridge.ShortcutContext), KeyCode.BackQuote, ShortcutModifiers.Shift)]
        private static void CollapseToolbar(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null)
            {
                SkinningModuleSettings.compactToolBar = !SkinningModuleSettings.compactToolBar;
            }
        }

        [Shortcut(ShortcutIds.restoreBindPose, typeof(InternalEditorBridge.ShortcutContext), KeyCode.R)]
        private static void DisablePoseModeKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled)
            {
                SkeletonCache effectiveSkeleton = sm.skinningCache.GetEffectiveSkeleton(sm.skinningCache.selectedSprite);
                if (effectiveSkeleton != null && effectiveSkeleton.isPosePreview)
                {
                    using (sm.skinningCache.UndoScope(TextContent.restorePose))
                    {
                        sm.skinningCache.RestoreBindPose();
                        sm.skinningCache.events.shortcut.Invoke("r");
                    }
                }
            }
        }

        [Shortcut(ShortcutIds.toggleCharacterMode, typeof(InternalEditorBridge.ShortcutContext), KeyCode.Alpha2, ShortcutModifiers.Shift)]
        private static void ToggleCharacterModeKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled && sm.skinningCache.hasCharacter)
            {
                BaseTool tool = sm.skinningCache.GetTool(Tools.SwitchMode);

                using (sm.skinningCache.UndoScope(TextContent.setMode))
                {
                    if (tool.isActive)
                        tool.Deactivate();
                    else
                        tool.Activate();
                }

                sm.skinningCache.events.shortcut.Invoke("#2");
            }
        }

        [Shortcut(ShortcutIds.previewPose, typeof(InternalEditorBridge.ShortcutContext), KeyCode.Q, ShortcutModifiers.Shift)]
        private static void EditPoseKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled)
            {
                sm.SetSkeletonTool(Tools.EditPose);
                sm.skinningCache.events.shortcut.Invoke("#q");
            }
        }

        [Shortcut(ShortcutIds.characterPivot, typeof(InternalEditorBridge.ShortcutContext), KeyCode.T, ShortcutModifiers.Shift)]
        private static void EditCharacterPivotKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled && sm.skinningCache.mode == SkinningMode.Character)
            {
                sm.SetSkeletonTool(Tools.CharacterPivotTool);
                sm.skinningCache.events.shortcut.Invoke("#t");
            }
        }

        [Shortcut(ShortcutIds.editBone, typeof(InternalEditorBridge.ShortcutContext), KeyCode.W, ShortcutModifiers.Shift)]
        private static void EditJointsKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled)
            {
                sm.SetSkeletonTool(Tools.EditJoints);
                sm.skinningCache.events.shortcut.Invoke("#w");
            }
        }

        [Shortcut(ShortcutIds.createBone, typeof(InternalEditorBridge.ShortcutContext), KeyCode.E, ShortcutModifiers.Shift)]
        private static void CreateBoneKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled)
            {
                sm.SetSkeletonTool(Tools.CreateBone);
                sm.skinningCache.events.shortcut.Invoke("#e");
            }
        }

        [Shortcut(ShortcutIds.splitBone, typeof(InternalEditorBridge.ShortcutContext), KeyCode.R, ShortcutModifiers.Shift)]
        private static void SplitBoneKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled)
            {
                sm.SetSkeletonTool(Tools.SplitBone);
                sm.skinningCache.events.shortcut.Invoke("#r");
            }
        }

        [Shortcut(ShortcutIds.autoGeometry, typeof(InternalEditorBridge.ShortcutContext), KeyCode.A, ShortcutModifiers.Shift)]
        private static void GenerateGeometryKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled)
            {
                sm.SetMeshTool(Tools.GenerateGeometry);
                sm.skinningCache.events.shortcut.Invoke("#a");
            }
        }

        [Shortcut(ShortcutIds.editGeometry, typeof(InternalEditorBridge.ShortcutContext), KeyCode.S, ShortcutModifiers.Shift)]
        private static void MeshSelectionKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled)
            {
                sm.SetMeshTool(Tools.EditGeometry);
                sm.skinningCache.events.shortcut.Invoke("#s");
            }
        }

        [Shortcut(ShortcutIds.createVertex, typeof(InternalEditorBridge.ShortcutContext), KeyCode.J, ShortcutModifiers.Shift)]
        private static void CreateVertex(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled)
            {
                sm.SetMeshTool(Tools.CreateVertex);
                sm.skinningCache.events.shortcut.Invoke("#d");
            }
        }

        [Shortcut(ShortcutIds.createEdge, typeof(InternalEditorBridge.ShortcutContext), KeyCode.G, ShortcutModifiers.Shift)]
        private static void CreateEdgeKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled)
            {
                sm.SetMeshTool(Tools.CreateEdge);
                sm.skinningCache.events.shortcut.Invoke("#g");
            }
        }

        [Shortcut(ShortcutIds.splitEdge, typeof(InternalEditorBridge.ShortcutContext), KeyCode.H, ShortcutModifiers.Shift)]
        private static void SplitEdge(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled)
            {
                sm.SetMeshTool(Tools.SplitEdge);
                sm.skinningCache.events.shortcut.Invoke("#h");
            }
        }

        [Shortcut(ShortcutIds.autoWeights, typeof(InternalEditorBridge.ShortcutContext), KeyCode.Z, ShortcutModifiers.Shift)]
        private static void GenerateWeightsKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled)
            {
                sm.SetWeightTool(Tools.GenerateWeights);
                sm.skinningCache.events.shortcut.Invoke("#z");
            }
        }

        [Shortcut(ShortcutIds.weightSlider, typeof(InternalEditorBridge.ShortcutContext), KeyCode.X, ShortcutModifiers.Shift)]
        private static void WeightSliderKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled)
            {
                sm.SetWeightTool(Tools.WeightSlider);
                sm.skinningCache.events.shortcut.Invoke("#x");
            }
        }

        [Shortcut(ShortcutIds.weightBrush, typeof(InternalEditorBridge.ShortcutContext), KeyCode.N, ShortcutModifiers.Shift)]
        private static void WeightBrushKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled)
            {
                sm.SetWeightTool(Tools.WeightBrush);
                sm.skinningCache.events.shortcut.Invoke("#c");
            }
        }

        [Shortcut(ShortcutIds.boneInfluence, typeof(InternalEditorBridge.ShortcutContext), KeyCode.V, ShortcutModifiers.Shift)]
        private static void BoneInfluenceKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled && sm.skinningCache.mode == SkinningMode.Character)
            {
                sm.SetWeightTool(Tools.BoneInfluence);
                sm.skinningCache.events.shortcut.Invoke("#v");
            }
        }

        [Shortcut(ShortcutIds.spriteInfluence, typeof(InternalEditorBridge.ShortcutContext), KeyCode.M, ShortcutModifiers.Shift)]
        private static void SpriteInfluenceKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled && sm.skinningCache.mode == SkinningMode.Character)
            {
                sm.SetWeightTool(Tools.SpriteInfluence);
                sm.skinningCache.events.shortcut.Invoke("#m");
            }
        }

        [Shortcut(ShortcutIds.pastePanelWeights, typeof(InternalEditorBridge.ShortcutContext), KeyCode.B, ShortcutModifiers.Shift)]
        private static void PastePanelKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled)
            {
                sm.TogglePasteTool();
                sm.skinningCache.events.shortcut.Invoke("#b");
            }
        }

        [Shortcut(ShortcutIds.visibilityPanel, typeof(InternalEditorBridge.ShortcutContext), KeyCode.P, ShortcutModifiers.Shift)]
        private static void VisibilityPanelKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled)
            {
                sm.m_HorizontalToggleTools.ToggleVisibilityTool(sm.currentTool);
                sm.skinningCache.events.shortcut.Invoke("#p");
            }
        }

        [Shortcut(ShortcutIds.hideShowSelected, typeof(InternalEditorBridge.ShortcutContext), KeyCode.H)]
        private static void HideShowSelectedKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled && sm.ToggleSelectedVisibility())
                sm.skinningCache.events.shortcut.Invoke("h");
        }

        [Shortcut(ShortcutIds.renameSelection, typeof(InternalEditorBridge.ShortcutContext), KeyCode.F2)]
        private static void RenameSelectionKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled && sm.ShowRenameSelectionWindow())
                sm.skinningCache.events.shortcut.Invoke("f2");
        }

        [Shortcut(ShortcutIds.duplicateBone, typeof(InternalEditorBridge.ShortcutContext), KeyCode.D, ShortcutModifiers.Action)]
        private static void DuplicateBoneKey(ShortcutArguments args)
        {
            SkinningModule sm = GetModuleFromContext(args);
            if (sm != null && !sm.spriteEditor.editingDisabled && sm.DuplicateSelectedBone())
                sm.skinningCache.events.shortcut.Invoke("%d");
        }

        private bool ShowRenameSelectionWindow()
        {
            BoneCache selectedBone = GetSelectedBoneForRename();
            if (selectedBone != null)
            {
                RenameSelectionWindow.Show(selectedBone.name, newName => RenameBone(selectedBone, newName));
                return true;
            }

            SpriteCache selectedSprite = skinningCache.selectedSprite;
            if (selectedSprite != null)
            {
                RenameSelectionWindow.Show(selectedSprite.name, newName => RenameSprite(selectedSprite, newName));
                return true;
            }

            return false;
        }

        private bool DuplicateSelectedBone()
        {
            if (skinningCache.bonesReadOnly || (skinningCache.hasCharacter && skinningCache.mode == SkinningMode.SpriteSheet))
                return false;

            SkeletonCache skeleton = skinningCache.GetEffectiveSkeleton(skinningCache.selectedSprite);
            BoneCache sourceBone = GetSelectedBoneForSkeleton(skeleton);
            if (skeleton == null || sourceBone == null || sourceBone.IsConstraintParent())
                return false;

            using (skinningCache.UndoScope(TextContent.duplicateBone))
            {
                List<BoneCache> duplicatedBones = DuplicateBoneTree(skeleton, sourceBone);
                if (duplicatedBones.Count == 0)
                    return false;

                skinningCache.skeletonSelection.activeElement = duplicatedBones[0].ToCharacterIfNeeded();
                skinningCache.events.boneSelectionChanged.Invoke();
                skinningCache.events.skeletonTopologyChanged.Invoke(skeleton);
                skinningCache.events.skeletonBindPoseChanged.Invoke(skeleton);
            }

            spriteEditor.RequestRepaint();
            return true;
        }

        private BoneCache GetSelectedBoneForSkeleton(SkeletonCache skeleton)
        {
            if (skeleton == null)
                return null;

            BoneCache selectedBone = GetSelectedBoneForRename();
            if (selectedBone == null)
                return null;

            selectedBone = selectedBone.ToSpriteSheetIfNeeded();
            return selectedBone != null && skeleton.Contains(selectedBone) ? selectedBone : null;
        }

        private List<BoneCache> DuplicateBoneTree(SkeletonCache skeleton, BoneCache sourceRoot)
        {
            Dictionary<BoneCache, BoneCache> duplicatesBySource = new Dictionary<BoneCache, BoneCache>();
            List<BoneCache> duplicatedBones = new List<BoneCache>();

            DuplicateBoneTreeRecursive(skeleton, sourceRoot, sourceRoot.parentBone, duplicatesBySource, duplicatedBones);
            RestoreDuplicatedChainedChildren(duplicatesBySource);

            return duplicatedBones;
        }

        private void DuplicateBoneTreeRecursive(
            SkeletonCache skeleton,
            BoneCache sourceBone,
            BoneCache duplicateParent,
            Dictionary<BoneCache, BoneCache> duplicatesBySource,
            List<BoneCache> duplicatedBones)
        {
            if (sourceBone == null)
                return;

            BoneCache nextParent = duplicateParent;
            if (!sourceBone.IsConstraintParent())
            {
                BoneCache duplicate = CreateBoneDuplicate(skeleton, sourceBone, duplicateParent);
                duplicatesBySource[sourceBone] = duplicate;
                duplicatedBones.Add(duplicate);
                nextParent = duplicate;
            }

            TransformCache[] children = sourceBone.children;
            for (int i = 0; i < children.Length; ++i)
                DuplicateBoneTreeRecursive(skeleton, children[i] as BoneCache, nextParent, duplicatesBySource, duplicatedBones);
        }

        private void RestoreDuplicatedChainedChildren(Dictionary<BoneCache, BoneCache> duplicatesBySource)
        {
            foreach (KeyValuePair<BoneCache, BoneCache> pair in duplicatesBySource)
            {
                BoneCache sourceChainedChild = pair.Key.chainedChild;
                if (sourceChainedChild == null)
                    continue;

                if (duplicatesBySource.TryGetValue(sourceChainedChild, out BoneCache duplicateChainedChild))
                    pair.Value.chainedChild = duplicateChainedChild;
            }
        }

        private BoneCache CreateBoneDuplicate(SkeletonCache skeleton, BoneCache sourceBone, BoneCache parentBone)
        {
            string duplicateName = SkeletonController.AutoNameBoneCopy(sourceBone.name, skeleton.bones);
            BoneCache duplicate = skinningCache.CreateCache<BoneCache>();
            duplicate.SetParent(parentBone);
            skeleton.AddBone(duplicate);

            duplicate.name = duplicateName;
            duplicate.guid = GUID.Generate().ToString();
            duplicate.bindPoseColor = sourceBone.bindPoseColor;
            duplicate.depth = sourceBone.depth;
            duplicate.isVisible = sourceBone.isVisible;
            duplicate.worldPose = sourceBone.worldPose;
            duplicate.SetDefaultPose();
            return duplicate;
        }

        private bool CopySelectedBoneTransforms()
        {
            SkeletonCache skeleton = skinningCache.GetEffectiveSkeleton(skinningCache.selectedSprite);
            BoneCache[] selectedBones = GetSelectedBonesForSkeleton(skeleton);
            if (selectedBones.Length == 0)
                return false;

            BoneTransformCopyData copyData = new BoneTransformCopyData
            {
                transforms = new BoneTransformData[selectedBones.Length]
            };

            for (int i = 0; i < selectedBones.Length; ++i)
                copyData.transforms[i] = CreateBoneTransformData(selectedBones[i]);

            EditorGUIUtility.systemCopyBuffer = k_BoneTransformCopyPrefix + JsonUtility.ToJson(copyData);
            skinningCache.events.copy.Invoke();
            return true;
        }

        private bool PasteSelectedBoneTransforms(bool mirrored)
        {
            if (skinningCache.bonesReadOnly || (skinningCache.hasCharacter && skinningCache.mode == SkinningMode.SpriteSheet))
                return false;

            if (!TryGetCopiedBoneTransforms(out BoneTransformCopyData copyData) || copyData.transforms.Length == 0)
                return false;

            SkeletonCache skeleton = skinningCache.GetEffectiveSkeleton(skinningCache.selectedSprite);
            BoneCache[] selectedBones = GetSelectedBonesForSkeleton(skeleton);
            if (selectedBones.Length == 0 || selectedBones.Length != copyData.transforms.Length)
                return false;

            Rect mirrorRect = GetBoneTransformMirrorRect();
            using (skinningCache.UndoScope(TextContent.pasteBoneTransform))
            {
                for (int i = 0; i < selectedBones.Length; ++i)
                {
                    BoneTransformData transformData = copyData.transforms[i];
                    if (mirrored)
                        transformData = MirrorBoneTransformData(transformData, mirrorRect);

                    ApplyBoneTransformData(selectedBones[i], transformData);
                }

                if (PasteTransformsEditsBindPose())
                {
                    skeleton.SetDefaultPose();
                    skinningCache.events.skeletonBindPoseChanged.Invoke(skeleton);
                }
                else
                {
                    skeleton.SetPosePreview();
                    skinningCache.events.skeletonPreviewPoseChanged.Invoke(skeleton);
                }
            }

            skinningCache.events.paste.Invoke(true, false, mirrored, false);
            spriteEditor.RequestRepaint();
            return true;
        }

        private BoneCache[] GetSelectedBonesForSkeleton(SkeletonCache skeleton)
        {
            if (skeleton == null)
                return Array.Empty<BoneCache>();

            HashSet<BoneCache> selectedBones = new HashSet<BoneCache>();
            BoneCache[] selection = skinningCache.skeletonSelection.elements;
            for (int i = 0; i < selection.Length; ++i)
            {
                BoneCache bone = selection[i].ToSpriteSheetIfNeeded();
                if (bone != null && skeleton.Contains(bone) && !bone.IsConstraintParent())
                    selectedBones.Add(bone);
            }

            List<BoneCache> orderedBones = new List<BoneCache>();
            BoneCache[] skeletonBones = skeleton.bones;
            for (int i = 0; i < skeletonBones.Length; ++i)
            {
                if (selectedBones.Contains(skeletonBones[i]))
                    orderedBones.Add(skeletonBones[i]);
            }

            return orderedBones.ToArray();
        }

        private static BoneTransformData CreateBoneTransformData(BoneCache bone)
        {
            return new BoneTransformData
            {
                position = bone.position,
                rotation = bone.rotation,
                length = bone.length,
                depth = bone.depth
            };
        }

        private static void ApplyBoneTransformData(BoneCache bone, BoneTransformData transformData)
        {
            bone.position = transformData.position;
            bone.rotation = transformData.rotation;
            bone.length = transformData.length;
            bone.depth = transformData.depth;
        }

        private static bool TryGetCopiedBoneTransforms(out BoneTransformCopyData copyData)
        {
            copyData = null;
            string copyBuffer = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(copyBuffer) || !copyBuffer.StartsWith(k_BoneTransformCopyPrefix, StringComparison.Ordinal))
                return false;

            copyData = JsonUtility.FromJson<BoneTransformCopyData>(copyBuffer.Substring(k_BoneTransformCopyPrefix.Length));
            return copyData != null && copyData.transforms != null;
        }

        private Rect GetBoneTransformMirrorRect()
        {
            if (skinningCache.mode == SkinningMode.Character && skinningCache.character != null)
                return new Rect(Vector2.zero, skinningCache.character.dimension);

            SpriteCache selectedSprite = skinningCache.selectedSprite;
            return selectedSprite != null ? selectedSprite.textureRect : new Rect(Vector2.zero, Vector2.zero);
        }

        private static BoneTransformData MirrorBoneTransformData(BoneTransformData transformData, Rect mirrorRect)
        {
            transformData.position.x = mirrorRect.x + mirrorRect.width - transformData.position.x;
            transformData.rotation = GetMirroredBoneRotation(transformData.rotation);
            return transformData;
        }

        private static Quaternion GetMirroredBoneRotation(Quaternion rotation)
        {
            Vector3 euler = rotation.eulerAngles;
            euler.z = euler.z <= 180f ? 180f - euler.z : 540f - euler.z;
            return Quaternion.Euler(euler);
        }

        private bool PasteTransformsEditsBindPose()
        {
            SkeletonToolWrapper skeletonTool = currentTool as SkeletonToolWrapper;
            return skeletonTool == null || skeletonTool.editBindPose;
        }

        private BoneCache GetSelectedBoneForRename()
        {
            BoneCache activeBone = skinningCache.skeletonSelection.activeElement;
            if (activeBone != null)
                return activeBone;

            BoneCache[] selectedBones = skinningCache.skeletonSelection.elements;
            return selectedBones.Length > 0 ? selectedBones[0] : null;
        }

        private void RenameBone(BoneCache bone, string newName)
        {
            if (bone == null || string.Compare(bone.name, newName) == 0)
                return;

            if (string.IsNullOrEmpty(newName) || string.IsNullOrWhiteSpace(newName))
                return;

            using (skinningCache.UndoScope(TextContent.boneName))
            {
                bone.name = newName;
                skinningCache.events.boneNameChanged.Invoke(bone);
            }

            spriteEditor.RequestRepaint();
        }

        private void RenameSprite(SpriteCache sprite, string newName)
        {
            if (sprite == null || string.Compare(sprite.name, newName) == 0)
                return;

            if (string.IsNullOrEmpty(newName) || string.IsNullOrWhiteSpace(newName))
                return;

            using (skinningCache.UndoScope(TextContent.spriteName))
                sprite.name = newName;

            DataModified();
            spriteEditor.RequestRepaint();
        }

        private bool ToggleSelectedVisibility()
        {
            BoneCache[] selectedBones = skinningCache.skeletonSelection.elements;
            if (selectedBones.Length > 0)
            {
                bool visible = false;
                for (int i = 0; i < selectedBones.Length; ++i)
                    visible |= selectedBones[i].isVisible;

                using (skinningCache.UndoScope(TextContent.visibilityChange))
                {
                    bool newVisibility = !visible;
                    for (int i = 0; i < selectedBones.Length; ++i)
                        selectedBones[i].isVisible = newVisibility;

                    skinningCache.BoneVisibilityChanged();
                }

                spriteEditor.RequestRepaint();
                return true;
            }

            SpriteCache selectedSprite = skinningCache.selectedSprite;
            CharacterPartCache characterPart = selectedSprite != null ? selectedSprite.GetCharacterPart() : null;
            if (skinningCache.mode == SkinningMode.Character && characterPart != null)
            {
                using (skinningCache.UndoScope(TextContent.spriteVisibility))
                    characterPart.isVisible = !characterPart.isVisible;

                spriteEditor.RequestRepaint();
                return true;
            }

            return false;
        }

        private void AddMainUI(VisualElement mainView)
        {
            VisualTreeAsset visualTree = ResourceLoader.Load<VisualTreeAsset>("LayoutOverlay/LayoutOverlay.uxml");
            VisualElement clone = visualTree.CloneTree();
            m_LayoutOverlay = clone.Q<LayoutOverlay>("LayoutOverlay");

            mainView.Add(m_LayoutOverlay);
            m_LayoutOverlay.hasScrollbar = true;
            m_LayoutOverlay.verticalToolbar.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            m_LayoutOverlay.StretchToParentSize();

            CreateAnimationPreviewPanel();

            CreatePoseToolbar();
            CreateBoneToolbar();
            CreateMeshToolbar();
            CreateWeightToolbar();
            CreateConstraintsToolbar();
            CreateRigToolbar();

            m_ShortcutContext = new InternalEditorBridge.ShortcutContext()
            {
                isActive = isFocused,
                context = this
            };
            InternalEditorBridge.RegisterShortcutContext(m_ShortcutContext);
            InternalEditorBridge.AddEditorApplicationProjectLoadedCallback(OnProjectLoaded);
        }

        private void OnProjectLoaded()
        {
            if (m_ShortcutContext != null)
                InternalEditorBridge.RegisterShortcutContext(m_ShortcutContext);
        }

        private void DoViewGUI()
        {
            m_AnimationPreviewPanel.SetEnabled(!spriteEditor.editingDisabled);

            if (spriteEditor.editingDisabled == m_BoneToolbar.enabledSelf)
            {
                m_BoneToolbar.SetEnabled(!spriteEditor.editingDisabled);
                m_MeshToolbar.SetEnabled(!spriteEditor.editingDisabled);
                m_WeightToolbar.SetEnabled(!spriteEditor.editingDisabled);
                m_ConstraintsToolbar.SetEnabled(!spriteEditor.editingDisabled);
            }

            if (spriteEditor.editingDisabled == m_LayoutOverlay.rightOverlay.enabledSelf)
            {
                m_LayoutOverlay.rightOverlay.SetEnabled(!spriteEditor.editingDisabled);
                m_LayoutOverlay.rightOverlay.visible = !spriteEditor.editingDisabled;
                m_LayoutOverlay.bottomOverlay.SetEnabled(!spriteEditor.editingDisabled);
                m_LayoutOverlay.bottomOverlay.visible = !spriteEditor.editingDisabled;
            }

            m_PoseToolbar.UpdateResetButtonState();
            m_RigToolbar.UpdatePasteButtonEnabledState();
        }

        private bool isFocused()
        {
            return spriteEditor != null && (EditorWindow.focusedWindow == spriteEditor as EditorWindow);
        }

        private void CreateAnimationPreviewPanel()
        {
            m_AnimationPreviewPanel = new AnimationPreviewPanel();
            m_LayoutOverlay.timelineOverlay.Add(m_AnimationPreviewPanel);
        }

        private void CreatePoseToolbar()
        {
            m_PoseToolbar = PoseToolbar.GenerateFromUXML();
            m_PoseToolbar.Setup(skinningCache);
            m_LayoutOverlay.verticalToolbar.AddToContainer(m_PoseToolbar);

            m_PoseToolbar.SetMeshTool += SetMeshTool;
            m_PoseToolbar.SetSkeletonTool += SetSkeletonTool;
            m_PoseToolbar.ActivateEditPoseTool += ActivateEditPoseTool;
            m_PoseToolbar.SetEnabled(!spriteEditor.editingDisabled);
        }

        private void CreateBoneToolbar()
        {
            m_BoneToolbar = BoneToolbar.GenerateFromUXML();
            m_BoneToolbar.Setup(skinningCache);
            m_LayoutOverlay.verticalToolbar.AddToContainer(m_BoneToolbar);

            m_BoneToolbar.SetSkeletonTool += SetSkeletonTool;
            m_BoneToolbar.SetEnabled(!spriteEditor.editingDisabled);
        }

        private void CreateMeshToolbar()
        {
            m_MeshToolbar = MeshToolbar.GenerateFromUXML();
            m_MeshToolbar.skinningCache = skinningCache;
            m_LayoutOverlay.verticalToolbar.AddToContainer(m_MeshToolbar);

            m_MeshToolbar.SetMeshTool += SetMeshTool;
            m_MeshToolbar.ResetGeometry += ResetGeometry;
            MeshToolWrapper newGeometryTool = skinningCache.GetTool(Tools.CreateEdge) as MeshToolWrapper;
            if (newGeometryTool != null && newGeometryTool.meshTool != null)
            {
                newGeometryTool.meshTool.newGeometryCompleted += CompleteNewGeometryMode;
                newGeometryTool.meshTool.newGeometryCanceled += CancelNewGeometryMode;
            }
            m_MeshToolbar.SetEnabled(!spriteEditor.editingDisabled);
        }

        private void CreateWeightToolbar()
        {
            m_WeightToolbar = WeightToolbar.GenerateFromUXML();
            m_WeightToolbar.skinningCache = skinningCache;
            m_LayoutOverlay.verticalToolbar.AddToContainer(m_WeightToolbar);
            m_WeightToolbar.SetWeightTool += SetWeightTool;
            m_WeightToolbar.SetEnabled(!spriteEditor.editingDisabled);
        }

        private void CreateConstraintsToolbar()
        {
            m_ConstraintsToolbar = ConstraintsToolbar.GenerateFromUXML();
            m_ConstraintsToolbar.skinningCache = skinningCache;
            m_LayoutOverlay.verticalToolbar.AddToContainer(m_ConstraintsToolbar);
            m_ConstraintsToolbar.SetConstraintTool += SetConstraintTool;
            m_ConstraintsToolbar.SetEnabled(!spriteEditor.editingDisabled);
        }

        private void CreateRigToolbar()
        {
            m_RigToolbar = RigToolbar.GenerateFromUXML();
            m_RigToolbar.skinningCache = skinningCache;
            m_LayoutOverlay.verticalToolbar.AddToContainer(m_RigToolbar);

            m_RigToolbar.ActivateCopyTool += ActivateCopyTool;
            m_RigToolbar.TogglePasteTool += TogglePasteTool;
            m_RigToolbar.SetEnabled(!spriteEditor.editingDisabled);
        }

        private void ActivateEditPoseTool()
        {
            BaseTool tool = skinningCache.GetTool(Tools.EditPose);
            if (currentTool == tool)
                return;

            using (skinningCache.UndoScope(TextContent.setTool))
            {
                ActivateTool(tool);
            }
        }

        private void SetSkeletonTool(Tools toolType)
        {
            SkeletonToolWrapper tool = skinningCache.GetTool(toolType) as SkeletonToolWrapper;

            if (tool == null)
                return;

            if (currentTool == tool)
                return;

            using (skinningCache.UndoScope(TextContent.setTool))
            {
                ActivateTool(tool);

                if (tool.editBindPose)
                    skinningCache.RestoreBindPose();
            }
        }

        private void SetConstraintTool(Tools toolType)
        {
            ConstraintsTool tool = skinningCache.GetTool(toolType) as ConstraintsTool;

            if (tool == null || currentTool == tool)
                return;

            using (skinningCache.UndoScope(TextContent.setTool))
            {
                ActivateTool(tool);
            }

            spriteEditor.RequestRepaint();
        }

        private void SetMeshTool(Tools toolType)
        {
            BaseTool tool = skinningCache.GetTool(toolType);

            if (toolType == Tools.CreateEdge)
            {
                ToggleNewGeometryTool(tool);
                return;
            }

            if (IsNewGeometryToolActive())
            {
                if (TryCompleteNewGeometry())
                    ExitNewGeometryMode();

                return;
            }

            if (currentTool == tool)
                return;

            using (skinningCache.UndoScope(TextContent.setTool))
            {
                ActivateTool(tool);
                skinningCache.RestoreBindPose();
                UnselectBones();
            }
        }

        private void ToggleNewGeometryTool(BaseTool tool)
        {
            if (currentTool == tool)
            {
                if (TryCompleteNewGeometry())
                    ExitNewGeometryMode();
                return;
            }

            BeginNewGeometryTool(tool);
        }

        private void BeginNewGeometryTool(BaseTool tool)
        {
            SpriteCache sprite = skinningCache.selectedSprite;
            if (sprite == null)
                return;

            MeshCache mesh = sprite.GetMesh();
            if (mesh == null)
                return;

            if (HasWeights(mesh) && !ConfirmResetWeightedGeometry())
                return;

            using (skinningCache.UndoScope(TextContent.newGeometry))
            {
                m_NewGeometrySnapshot = NewGeometrySnapshot.Capture(mesh);
                ActivateTool(tool);
                skinningCache.RestoreBindPose();
                UnselectBones();
                skinningCache.vertexSelection.Clear();
                mesh.Clear();
                skinningCache.events.meshChanged.Invoke(mesh);
            }

            spriteEditor.RequestRepaint();
        }

        private bool IsNewGeometryToolActive()
        {
            return currentTool == skinningCache.GetTool(Tools.CreateEdge);
        }

        private bool TryCompleteNewGeometry()
        {
            SpriteCache sprite = skinningCache.selectedSprite;
            if (sprite == null)
                return false;

            MeshCache mesh = sprite.GetMesh();
            if (mesh == null || mesh.vertexCount < 3)
                return false;

            using (skinningCache.UndoScope(TextContent.newGeometry))
            {
                SpriteMeshDataController spriteMeshDataController = new SpriteMeshDataController();
                spriteMeshDataController.spriteMeshData = mesh;
                spriteMeshDataController.CreateEdge(mesh.vertexCount - 1, 0);
                spriteMeshDataController.Triangulate(new Triangulator());
                spriteMeshDataController.SortTrianglesByDepth();
                skinningCache.vertexSelection.Clear();
                skinningCache.events.meshChanged.Invoke(mesh);
            }

            m_NewGeometrySnapshot = null;
            return true;
        }

        private void CompleteNewGeometryMode()
        {
            m_NewGeometrySnapshot = null;
            ExitNewGeometryMode();
        }

        private void CancelNewGeometryMode()
        {
            SpriteCache sprite = skinningCache.selectedSprite;
            MeshCache mesh = sprite != null ? sprite.GetMesh() : null;

            if (mesh != null && m_NewGeometrySnapshot != null)
            {
                using (skinningCache.UndoScope(TextContent.newGeometry))
                {
                    m_NewGeometrySnapshot.Restore(mesh);
                    skinningCache.vertexSelection.Clear();
                    skinningCache.events.meshChanged.Invoke(mesh);
                }
            }

            m_NewGeometrySnapshot = null;
            ExitNewGeometryMode();
        }

        private void HandleNewGeometryExitRequest()
        {
            MeshToolWrapper newGeometryTool = skinningCache.GetTool(Tools.CreateEdge) as MeshToolWrapper;
            if (newGeometryTool == null || newGeometryTool.meshTool == null)
                return;

            MeshTool.NewGeometryExitRequest request = newGeometryTool.meshTool.ConsumeNewGeometryExitRequest();
            if (request == MeshTool.NewGeometryExitRequest.Completed)
                CompleteNewGeometryMode();
            else if (request == MeshTool.NewGeometryExitRequest.Canceled)
                CancelNewGeometryMode();
        }

        private void NormalizeRestoredNewGeometryTool()
        {
            if (skinningCache.selectedTool == skinningCache.GetTool(Tools.CreateEdge))
                skinningCache.selectedTool = skinningCache.GetTool(Tools.EditGeometry);
        }

        private void CancelNewGeometryModeOnDeactivate()
        {
            if (!IsNewGeometryToolActive())
                return;

            if (m_NewGeometrySnapshot != null)
                CancelNewGeometryMode();
            else
                NormalizeRestoredNewGeometryTool();
        }

        private void ExitNewGeometryMode()
        {
            BaseTool editGeometryTool = skinningCache.GetTool(Tools.EditGeometry);
            if (editGeometryTool != null && currentTool != editGeometryTool)
                ActivateTool(editGeometryTool);

            UpdateToggleState();
            spriteEditor.RequestRepaint();
        }

        private void ResetGeometry()
        {
            SpriteCache sprite = skinningCache.selectedSprite;
            if (sprite == null)
                return;

            MeshCache mesh = sprite.GetMesh();
            if (mesh == null)
                return;

            if (HasWeights(mesh) && !ConfirmResetWeightedGeometry())
                return;

            using (skinningCache.UndoScope(TextContent.resetGeometry))
            {
                SpriteMeshDataController spriteMeshDataController = new SpriteMeshDataController();
                spriteMeshDataController.spriteMeshData = mesh;
                mesh.Clear();
                spriteMeshDataController.CreateQuad();
                spriteMeshDataController.Triangulate(new Triangulator());
                spriteMeshDataController.SortTrianglesByDepth();

                skinningCache.vertexSelection.Clear();
                skinningCache.RestoreBindPose();
                UnselectBones();
                skinningCache.events.meshChanged.Invoke(mesh);
            }

            spriteEditor.RequestRepaint();
        }

        private bool ConfirmResetWeightedGeometry()
        {
            return EditorUtility.DisplayDialog(
                TextContent.resetGeometryWeightsTitle,
                TextContent.resetGeometryWeightsMessage,
                TextContent.resetGeometryWeightsConfirm,
                TextContent.resetGeometryWeightsCancel);
        }

        private bool HasWeights(MeshCache mesh)
        {
            foreach (EditableBoneWeight vertexWeight in mesh.vertexWeights)
            {
                if (vertexWeight != null && vertexWeight.Sum() > 0f)
                    return true;
            }

            return false;
        }

        private void SetWeightTool(Tools toolType)
        {
            BaseTool tool = skinningCache.GetTool(toolType);

            if (currentTool == tool)
                return;

            using (skinningCache.UndoScope(TextContent.setTool))
            {
                ActivateTool(tool);
            }
        }

        private void ActivateCopyTool()
        {
            CopyTool tool = skinningCache.GetTool(Tools.CopyPaste) as CopyTool;
            tool.OnCopyActivated();
        }

        private void TogglePasteTool()
        {
            CopyTool tool = skinningCache.GetTool(Tools.CopyPaste) as CopyTool;
            if (!tool.isActive)
                ActivateTool(tool);
            else if (previousTool != null)
                ActivateTool(previousTool);
        }

        private void StorePreviousTool()
        {
            if (currentTool is CopyTool || currentTool is VisibilityTool || currentTool is ConstraintsTool)
                return;

            previousTool = currentTool;
        }

        private void ActivateTool(BaseTool tool)
        {
            StorePreviousTool();

            m_ModuleToolGroup.ActivateTool(tool);
            UpdateToggleState();
            skinningCache.events.toolChanged.Invoke(tool);
        }

        private void UnselectBones()
        {
            skinningCache.skeletonSelection.Clear();
            skinningCache.events.boneSelectionChanged.Invoke();
        }

        private void UpdateToggleState()
        {
            Debug.Assert(m_PoseToolbar != null);
            Debug.Assert(m_BoneToolbar != null);
            Debug.Assert(m_MeshToolbar != null);
            Debug.Assert(m_WeightToolbar != null);

            m_PoseToolbar.UpdateToggleState();
            m_BoneToolbar.UpdateToggleState();
            m_MeshToolbar.UpdateToggleState();
            m_WeightToolbar.UpdateToggleState();
            m_ConstraintsToolbar.UpdateToggleState();
            m_RigToolbar.UpdatePasteButtonCheckedState();
        }

        private void RemoveMainUI(VisualElement mainView)
        {
            InternalEditorBridge.RemoveEditorApplicationProjectLoadedCallback(OnProjectLoaded);
            InternalEditorBridge.UnregisterShortcutContext(m_ShortcutContext);
        }
    }
}
