using UnityEditor.U2D.Layout;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal class SkeletonTool : BaseTool
    {
        [SerializeField]
        SkeletonController m_SkeletonController;
        SkeletonToolView m_SkeletonToolView;
        RectBoneSelector m_RectBoneSelector = new RectBoneSelector();
        RectSelectionTool<BoneCache> m_RectSelectionTool = new RectSelectionTool<BoneCache>();
        UnselectTool<BoneCache> m_UnselectTool = new UnselectTool<BoneCache>();

        public bool enableBoneInspector { get; set; }

        public SkeletonMode mode
        {
            get => m_SkeletonController.view.mode;
            set => m_SkeletonController.view.mode = value;
        }

        public bool editBindPose
        {
            get => m_SkeletonController.editBindPose;
            set => m_SkeletonController.editBindPose = value;
        }

        public bool suppressBoneSelection
        {
            get => m_SkeletonController.suppressBoneSelection;
            set => m_SkeletonController.suppressBoneSelection = value;
        }

        public ISkeletonStyle skeletonStyle
        {
            get => m_SkeletonController.styleOverride;
            set => m_SkeletonController.styleOverride = value;
        }

        public override int defaultControlID => 0;

        public BoneCache hoveredBone => m_SkeletonController.hoveredBone;

        public bool clearSelectionOnEscape
        {
            get => m_UnselectTool.clearOnEscape;
            set => m_UnselectTool.clearOnEscape = value;
        }

        public bool clearSelectionOnPrimaryEmptyClick
        {
            get => m_UnselectTool.clearOnPrimaryEmptyClick;
            set => m_UnselectTool.clearOnPrimaryEmptyClick = value;
        }

        public int secondaryEmptyControlID
        {
            get => m_UnselectTool.secondaryEmptyControlID;
            set => m_UnselectTool.secondaryEmptyControlID = value;
        }

        public bool allowPrimaryEmptyClickFallback
        {
            get => m_UnselectTool.allowPrimaryEmptyClickFallback;
            set => m_UnselectTool.allowPrimaryEmptyClickFallback = value;
        }

        public SkeletonCache skeleton
        {
            get => m_SkeletonController.skeleton;
            private set => m_SkeletonController.skeleton = value;
        }

        internal override void OnCreate()
        {
            m_SkeletonController = new SkeletonController();
            m_SkeletonController.view = new SkeletonView(new GUIWrapper());
            m_SkeletonController.view.InvalidID = 0;
            m_SkeletonController.selection = skinningCache.skeletonSelection;
            m_SkeletonToolView = new SkeletonToolView();
            m_SkeletonToolView.onBoneNameChanged += BoneNameChanged;
            m_SkeletonToolView.onBoneDepthChanged += BoneDepthChanged;
            m_SkeletonToolView.onBonePositionChanged += BonePositionChanged;
            m_SkeletonToolView.onBoneRotationChanged += BoneRotationChanged;
            m_SkeletonToolView.onBoneLengthChanged += BoneLengthChanged;
            m_SkeletonToolView.onBoneColorChanged += BoneColorChanged;
            m_SkeletonToolView.onBonesNameChanged += BonesNameChanged;
            m_SkeletonToolView.onBonesDepthChanged += BonesDepthChanged;
            m_SkeletonToolView.onBonesColorChanged += BonesColorChanged;
            m_RectBoneSelector.selection = skinningCache.skeletonSelection;
            m_RectSelectionTool.rectSelector = m_RectBoneSelector;
            m_RectSelectionTool.cacheUndo = skinningCache;
            m_RectSelectionTool.onSelectionUpdate += () =>
            {
                skinningCache.events.boneSelectionChanged.Invoke();
            };
            m_UnselectTool.cacheUndo = skinningCache;
            m_UnselectTool.selection = skinningCache.skeletonSelection;
            m_UnselectTool.onUnselect += () =>
            {
                skinningCache.events.boneSelectionChanged.Invoke();
            };
            m_UnselectTool.isPrimaryEmptyClickCandidate = IsPrimaryEmptyClickCandidate;
        }

        public override void Initialize(LayoutOverlay layout)
        {
            m_SkeletonToolView.Initialize(layout);
        }

        protected override void OnActivate()
        {
            SetupSkeleton(skinningCache.GetEffectiveSkeleton(skinningCache.selectedSprite));
            UpdateBoneInspector();
            skinningCache.events.skeletonTopologyChanged.AddListener(SkeletonTopologyChanged);
            skinningCache.events.boneSelectionChanged.AddListener(BoneSelectionChanged);
            skinningCache.events.selectedSpriteChanged.AddListener(SelectedSpriteChanged);
            skinningCache.events.skinningModeChanged.AddListener(SkinningModeChanged);
            skinningCache.events.skeletonPreviewPoseChanged.AddListener(SkeletonPoseChanged);
            skinningCache.events.skeletonBindPoseChanged.AddListener(SkeletonPoseChanged);
            skinningCache.events.boneDepthChanged.AddListener(BoneDataChanged);
            skinningCache.events.boneNameChanged.AddListener(BoneDataChanged);
            skinningCache.events.boneColorChanged.AddListener(BoneDataChanged);
            skeletonStyle = null;
            clearSelectionOnEscape = false;
            clearSelectionOnPrimaryEmptyClick = false;
            secondaryEmptyControlID = 0;
            allowPrimaryEmptyClickFallback = true;
            suppressBoneSelection = false;
        }

        protected override void OnDeactivate()
        {
            m_SkeletonToolView.Hide();
            m_SkeletonController.Reset();
            skinningCache.events.skeletonTopologyChanged.RemoveListener(SkeletonTopologyChanged);
            skinningCache.events.boneSelectionChanged.RemoveListener(BoneSelectionChanged);
            skinningCache.events.selectedSpriteChanged.RemoveListener(SelectedSpriteChanged);
            skinningCache.events.skinningModeChanged.RemoveListener(SkinningModeChanged);
            skinningCache.events.skeletonPreviewPoseChanged.RemoveListener(SkeletonPoseChanged);
            skinningCache.events.skeletonBindPoseChanged.RemoveListener(SkeletonPoseChanged);
            skinningCache.events.boneDepthChanged.RemoveListener(BoneDataChanged);
            skinningCache.events.boneNameChanged.RemoveListener(BoneDataChanged);
            skinningCache.events.boneColorChanged.RemoveListener(BoneDataChanged);
            skeletonStyle = null;
            suppressBoneSelection = false;
        }

        void SkeletonTopologyChanged(SkeletonCache skeletonCache)
        {
            if (skeleton == skeletonCache && skeleton != null)
                m_RectBoneSelector.bones = skeleton.bones;
        }

        void BoneDataChanged(BoneCache bone)
        {
            if (m_SkeletonToolView.target == bone)
                UpdateSingleBoneInspector(bone);
            else if (ContainsTarget(m_SkeletonToolView.targets, bone))
                UpdateMultiBoneInspector(m_SkeletonToolView.targets);
        }

        void SkeletonPoseChanged(SkeletonCache skeletonCache)
        {
            if (skeletonCache != skeleton)
                return;

            BoneCache selectedBone = m_SkeletonToolView.target;
            if (selectedBone != null)
                UpdateSingleBoneInspector(selectedBone);
            else
                UpdateMultiBoneInspector(m_SkeletonToolView.targets);
        }

        void SelectedSpriteChanged(SpriteCache sprite)
        {
            SetupSkeleton(skinningCache.GetEffectiveSkeleton(sprite));
        }

        void BoneSelectionChanged()
        {
            UpdateBoneInspector();
        }

        void UpdateBoneInspector()
        {
            BoneCache selectedBone = skinningCache.skeletonSelection.activeElement;
            int selectionCount = skinningCache.skeletonSelection.Count;

            m_SkeletonToolView.Hide();

            if (enableBoneInspector && selectedBone != null && selectionCount == 1)
            {
                UpdateSingleBoneInspector(selectedBone);
                bool isReadOnly = skinningCache.bonesReadOnly;
                m_SkeletonToolView.Show(selectedBone, isReadOnly);
            }
            else if (enableBoneInspector && selectedBone != null && selectionCount > 1)
            {
                BoneCache[] selectedBones = GetSelectedBonesInSkeletonOrder();
                UpdateMultiBoneInspector(selectedBones);
                bool isReadOnly = skinningCache.bonesReadOnly;
                m_SkeletonToolView.Show(selectedBones, isReadOnly);
            }
        }

        void SkinningModeChanged(SkinningMode skinningMode)
        {
            SetupSkeleton(skinningCache.GetEffectiveSkeleton(skinningCache.selectedSprite));
        }

        void SetupSkeleton(SkeletonCache sk)
        {
            m_RectBoneSelector.bones = null;
            skeleton = sk;

            if (skeleton != null)
                m_RectBoneSelector.bones = skeleton.bones;
        }

        protected override void OnGUI()
        {
            m_SkeletonController.view.defaultControlID = 0;

            if (skeleton != null && mode != SkeletonMode.Disabled)
            {
                m_UnselectTool.emptyControlID = m_RectSelectionTool.controlID;
                m_UnselectTool.OnGUI();
                m_RectSelectionTool.OnGUI();
                m_SkeletonController.view.defaultControlID = m_RectSelectionTool.controlID;
                m_UnselectTool.emptyControlID = m_RectSelectionTool.controlID;
            }
            else
            {
                m_UnselectTool.emptyControlID = 0;
                m_UnselectTool.OnGUI();
            }

            m_SkeletonController.OnGUI();
        }

        bool IsPrimaryEmptyClickCandidate()
        {
            return !skinningCache.IsOnVisualElement() &&
                mode != SkeletonMode.CreateBone &&
                m_SkeletonController.hoveredBone == null &&
                GUIUtility.hotControl == 0;
        }

        void BoneColorChanged(BoneCache selectedBone, Color32 color)
        {
            if (selectedBone != null)
            {
                skinningCache.BeginUndoOperation(TextContent.colorBoneChanged);
                selectedBone.bindPoseColor = color;
                skinningCache.events.boneColorChanged.Invoke(selectedBone);
            }
        }

        void BonesColorChanged(BoneCache[] selectedBones, Color32 color)
        {
            if (selectedBones == null || selectedBones.Length == 0)
                return;

            skinningCache.BeginUndoOperation(TextContent.colorBoneChanged);
            foreach (BoneCache bone in selectedBones)
            {
                if (bone == null || bone.bindPoseColor.Equals(color))
                    continue;

                bone.bindPoseColor = color;
                skinningCache.events.boneColorChanged.Invoke(bone);
            }
        }

        void BonePositionChanged(BoneCache selectedBone, Vector2 position)
        {
            if (selectedBone != null)
            {
                skinningCache.BeginUndoOperation(TextContent.moveBone);
                selectedBone.position = position;
                HandleUtility.Repaint();
                m_SkeletonController.InvokePoseChanged();
            }
        }

        void BoneRotationChanged(BoneCache selectedBone, float rotation)
        {
            if (selectedBone != null)
            {
                Vector3 euler = selectedBone.rotation.eulerAngles;
                euler.z = rotation;
                skinningCache.BeginUndoOperation(TextContent.rotateBone);
                selectedBone.rotation = Quaternion.Euler(euler);
                HandleUtility.Repaint();
                m_SkeletonController.InvokePoseChanged();
            }
        }

        void BoneLengthChanged(BoneCache selectedBone, float length)
        {
            if (selectedBone != null)
            {
                float clampedLength = Mathf.Max(0f, length);
                if (Mathf.Approximately(selectedBone.length, clampedLength))
                {
                    if (!Mathf.Approximately(length, clampedLength))
                        UpdateSingleBoneInspector(selectedBone);
                    return;
                }

                skinningCache.BeginUndoOperation(TextContent.boneLength);
                selectedBone.length = clampedLength;
                HandleUtility.Repaint();
                m_SkeletonController.InvokePoseChanged();
                UpdateSingleBoneInspector(selectedBone);
            }
        }

        void BoneNameChanged(BoneCache selectedBone, string name)
        {
            if (selectedBone != null)
            {
                if (string.Compare(selectedBone.name, name) == 0)
                    return;

                if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(name))
                    UpdateSingleBoneInspector(selectedBone);
                else
                {
                    using (skinningCache.UndoScope(TextContent.boneName))
                    {
                        selectedBone.name = name;
                        skinningCache.events.boneNameChanged.Invoke(selectedBone);
                    }
                }
            }
        }

        void BonesNameChanged(BoneCache[] selectedBones, string name)
        {
            if (selectedBones == null || selectedBones.Length == 0)
                return;

            if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(name))
            {
                UpdateMultiBoneInspector(selectedBones);
                return;
            }

            using (skinningCache.UndoScope(TextContent.boneName))
            {
                for (int i = 0; i < selectedBones.Length; ++i)
                {
                    BoneCache bone = selectedBones[i];
                    if (bone == null)
                        continue;

                    string newName = $"{name}_{i + 1}";
                    if (string.Compare(bone.name, newName) == 0)
                        continue;

                    bone.name = newName;
                    skinningCache.events.boneNameChanged.Invoke(bone);
                }
            }

            UpdateMultiBoneInspector(selectedBones);
        }

        void BoneDepthChanged(BoneCache selectedBone, int depth)
        {
            if (selectedBone != null)
            {
                if (Mathf.RoundToInt(selectedBone.depth) == depth)
                    return;

                using (skinningCache.UndoScope(TextContent.boneDepth))
                {
                    selectedBone.depth = depth;
                    skinningCache.events.boneDepthChanged.Invoke(selectedBone);
                }
            }
        }

        void BonesDepthChanged(BoneCache[] selectedBones, int depth)
        {
            if (selectedBones == null || selectedBones.Length == 0)
                return;

            using (skinningCache.UndoScope(TextContent.boneDepth))
            {
                foreach (BoneCache bone in selectedBones)
                {
                    if (bone == null || Mathf.RoundToInt(bone.depth) == depth)
                        continue;

                    bone.depth = depth;
                    skinningCache.events.boneDepthChanged.Invoke(bone);
                }
            }
        }

        void UpdateMultiBoneInspector(BoneCache[] selectedBones)
        {
            if (selectedBones == null || selectedBones.Length == 0 || selectedBones[0] == null)
                return;

            BoneCache referenceBone = selectedBones[0];
            m_SkeletonToolView.Update(string.Empty, Mathf.RoundToInt(referenceBone.depth), Vector2.zero, 0f, 0f, referenceBone.bindPoseColor);
        }

        void UpdateSingleBoneInspector(BoneCache bone)
        {
            m_SkeletonToolView.Update(bone.name, Mathf.RoundToInt(bone.depth), bone.position, bone.rotation.eulerAngles.z, bone.length, bone.bindPoseColor);
        }

        BoneCache[] GetSelectedBonesInSkeletonOrder()
        {
            BoneCache[] selectedBones = skinningCache.skeletonSelection.elements;
            BoneCache[] skeletonBones = skeleton != null ? skeleton.bones : null;
            if (skeletonBones == null)
                return selectedBones;

            System.Array.Sort(selectedBones, (a, b) => System.Array.IndexOf(skeletonBones, a).CompareTo(System.Array.IndexOf(skeletonBones, b)));
            return selectedBones;
        }

        static bool ContainsTarget(BoneCache[] targets, BoneCache bone)
        {
            if (targets == null || bone == null)
                return false;

            foreach (BoneCache target in targets)
            {
                if (target == bone)
                    return true;
            }

            return false;
        }
    }
}
