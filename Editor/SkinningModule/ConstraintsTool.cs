using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.U2D.Common;
using UnityEditor.U2D.Layout;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.U2D.Common;
using UnityEngine.UIElements;
using RuntimeConstraint = UnityEngine.U2D.Animation.SpriteBoneConstraint;
using RuntimeConstraintSet = UnityEngine.U2D.Animation.SpriteSkinConstraintSet;
using RuntimeConstraintType = UnityEngine.U2D.Animation.SpriteBoneConstraintType;

namespace UnityEditor.U2D.Animation
{
    internal class ConstraintsTool : SkeletonToolWrapper
    {
        const float k_ConstraintParentLength = 0.05f;

        static RuntimeConstraintSet s_ActiveConstraintSet;
        static ConstraintsTool s_PreviewOwner;

        ConstraintsPanel m_Panel;
        readonly List<PreviewConstraint> m_PreviewConstraints = new List<PreviewConstraint>();
        RuntimeConstraintSet m_BoundConstraintSet;
        SpriteCache m_BoundSprite;
        SkinningMode m_BoundMode;

        public RuntimeConstraintType constraintType { get; set; }

        public override void Initialize(LayoutOverlay layout)
        {
            base.Initialize(layout);
            m_Panel = new ConstraintsPanel();
            m_Panel.onCreateSet += CreateConstraintSet;
            m_Panel.onAdd += AddConstraint;
            m_Panel.onUpdate += UpdateConstraint;
            m_Panel.onRemove += RemoveConstraint;
            m_Panel.onConstraintSetChanged += OnConstraintSetChanged;
            m_Panel.onPickSource += PickSourceBone;
            m_Panel.onPickDriven += PickDrivenBone;
            m_Panel.onSelectedConstraintChanged += SelectConstraintBones;
            layout.AddBottomOverlayPanel(m_Panel);
            HidePanel();
        }

        protected override void OnActivate()
        {
            Debug.Assert(skeletonTool != null);
            skeletonTool.enableBoneInspector = false;
            skeletonTool.Activate();
            ShowInfoOverlay(SkinningEditorInfoText.Constraints);
            ShowPanel();
            m_Panel.BringToFront();
            RefreshPanel();
            BecomePreviewOwner();
            skinningCache.events.boneSelectionChanged.AddListener(RefreshPanel);
            skinningCache.events.skinningModeChanged.AddListener(OnSkinningModeChanged);
            skinningCache.events.selectedSpriteChanged.AddListener(OnSelectedSpriteChanged);
        }

        protected override void OnDeactivate()
        {
            skinningCache.events.boneSelectionChanged.RemoveListener(RefreshPanel);
            skinningCache.events.skinningModeChanged.RemoveListener(OnSkinningModeChanged);
            skinningCache.events.selectedSpriteChanged.RemoveListener(OnSelectedSpriteChanged);
            if (m_Panel != null)
            {
                LayoutOverlayUtility.ResetDraggableOverlayPanel(m_Panel);
                HidePanel();
            }
            base.OnDeactivate();
        }

        protected override void OnGUI()
        {
            base.OnGUI();
            ApplySharedPreview();
        }

        internal void ApplySharedPreview()
        {
            if (s_PreviewOwner != this)
                return;

            EnsurePreviewBindingsCurrent();
            ApplyPreviewConstraints();
        }

        void ShowPanel()
        {
            m_Panel.style.display = DisplayStyle.Flex;
            m_Panel.visible = true;
        }

        void HidePanel()
        {
            m_Panel.style.display = DisplayStyle.None;
            m_Panel.visible = false;
        }

        void OnSkinningModeChanged(SkinningMode mode)
        {
            RefreshPanel();
            RefreshPreviewBindings();
        }

        void OnSelectedSpriteChanged(SpriteCache sprite)
        {
            RefreshPanel();
            RefreshPreviewBindings();
        }

        void RefreshPanel()
        {
            SkeletonCache skeleton = skinningCache.GetEffectiveSkeleton(skinningCache.selectedSprite);
            BoneCache[] bones = skeleton != null ? GetUserBones(skeleton.bones) : Array.Empty<BoneCache>();
            m_Panel.constraintSet = s_ActiveConstraintSet;
            m_Panel.SetType(constraintType);
            m_Panel.SetBones(bones, skinningCache.skeletonSelection.elements);
        }

        void OnConstraintSetChanged()
        {
            s_ActiveConstraintSet = m_Panel.constraintSet;
            EnsureConstraintParentBones();
            BecomePreviewOwner();
        }

        void PickSourceBone()
        {
            BoneCache bone = GetFirstSelectedBone();
            if (bone != null)
                m_Panel.SetSourceBone(bone);
        }

        void PickDrivenBone()
        {
            BoneCache bone = GetFirstSelectedBone();
            if (bone != null)
                m_Panel.SetDrivenBone(bone);
        }

        BoneCache GetFirstSelectedBone()
        {
            BoneCache[] selection = skinningCache.skeletonSelection.elements;
            if (selection == null)
                return null;

            for (int i = 0; i < selection.Length; ++i)
            {
                if (selection[i] != null && !selection[i].IsConstraintParent())
                    return selection[i];
            }

            return null;
        }

        void SelectConstraintBones(RuntimeConstraint constraint)
        {
            if (constraint == null)
                return;

            SkeletonCache skeleton = skinningCache.GetEffectiveSkeleton(skinningCache.selectedSprite);
            if (skeleton == null)
                return;

            BoneCache source = FindBone(skeleton.bones, constraint.sourceBoneGuid);
            BoneCache driven = FindBone(skeleton.bones, constraint.drivenBoneGuid);
            if (source == null && driven == null)
                return;

            skinningCache.skeletonSelection.Clear();
            if (source != null)
                skinningCache.skeletonSelection.Select(source, true);
            if (driven != null && driven != source)
                skinningCache.skeletonSelection.Select(driven, true);
            skinningCache.events.boneSelectionChanged.Invoke();
        }

        void CreateConstraintSet()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Constraint Set",
                "SpriteSkinConstraintSet",
                "asset",
                "Choose where to save the Sprite Skin constraint set.");

            if (string.IsNullOrEmpty(path))
                return;

            RuntimeConstraintSet set = ScriptableObject.CreateInstance<RuntimeConstraintSet>();
            AssetDatabase.CreateAsset(set, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            RuntimeConstraintSet createdSet = AssetDatabase.LoadAssetAtPath<RuntimeConstraintSet>(path);
            if (createdSet == null)
            {
                AssetDatabase.DeleteAsset(path);
                EditorUtility.DisplayDialog(
                    "Create Constraint Set",
                    "Unity created an invalid Sprite Skin Constraint Set asset. Make sure the package has recompiled, then create the asset again.",
                    "OK");
                return;
            }

            m_Panel.constraintSet = createdSet;
            EditorGUIUtility.PingObject(createdSet);
            OnConstraintSetChanged();
        }

        void AddConstraint(BoneCache source, BoneCache driven, RuntimeConstraintType type, float influence, Vector3 multiplier)
        {
            RuntimeConstraintSet set = m_Panel.constraintSet;
            if (set == null || source == null || driven == null || source == driven)
                return;

            Undo.RecordObject(set, TextContent.editConstraints);
            RuntimeConstraint constraint = new RuntimeConstraint
            {
                type = type,
                sourceBoneGuid = source.guid,
                drivenBoneGuid = driven.guid,
                influence = influence,
                multiplier = multiplier
            };
            set.constraints.Add(constraint);
            EditorUtility.SetDirty(set);
            m_Panel.SelectConstraint(constraint);
            s_ActiveConstraintSet = set;
            EnsureConstraintParentBones();
            BecomePreviewOwner();
            SelectOnlySourceBone(source);
        }

        void UpdateConstraint(RuntimeConstraint constraint, BoneCache source, BoneCache driven, RuntimeConstraintType type, float influence, Vector3 multiplier)
        {
            RuntimeConstraintSet set = m_Panel.constraintSet;
            if (set == null || constraint == null || source == null || driven == null || source == driven)
                return;

            Undo.RecordObject(set, TextContent.editConstraints);
            constraint.type = type;
            constraint.sourceBoneGuid = source.guid;
            constraint.drivenBoneGuid = driven.guid;
            constraint.influence = influence;
            constraint.multiplier = multiplier;
            EditorUtility.SetDirty(set);
            m_Panel.RefreshConstraintList();
            s_ActiveConstraintSet = set;
            EnsureConstraintParentBones();
            BecomePreviewOwner();
            SelectOnlySourceBone(source);
        }

        void SelectOnlySourceBone(BoneCache source)
        {
            if (source == null)
                return;

            skinningCache.skeletonSelection.Clear();
            skinningCache.skeletonSelection.Select(source, true);
            skinningCache.events.boneSelectionChanged.Invoke();
        }

        void RemoveConstraint(RuntimeConstraint constraint)
        {
            RuntimeConstraintSet set = m_Panel.constraintSet;
            if (set == null || constraint == null)
                return;

            Undo.RecordObject(set, TextContent.editConstraints);
            set.constraints.Remove(constraint);
            EditorUtility.SetDirty(set);
            m_Panel.SelectConstraint(null);
            BecomePreviewOwner();
        }

        void BecomePreviewOwner()
        {
            s_ActiveConstraintSet = m_Panel.constraintSet;
            s_PreviewOwner = s_ActiveConstraintSet != null ? this : null;
            RefreshPreviewBindings();
        }

        void EnsureConstraintParentBones()
        {
            RuntimeConstraintSet set = s_ActiveConstraintSet;
            SkeletonCache skeleton = skinningCache.GetEffectiveSkeleton(skinningCache.selectedSprite);
            if (set == null || skeleton == null)
                return;

            bool changed = false;
            using (skinningCache.UndoScope(TextContent.editConstraints))
            {
                foreach (RuntimeConstraint constraint in set.constraints)
                {
                    if (string.IsNullOrEmpty(constraint.drivenBoneGuid))
                        continue;

                    BoneCache driven = FindBone(skeleton.bones, constraint.drivenBoneGuid);
                    if (driven == null || driven.IsConstraintParent())
                        continue;

                    changed |= EnsureConstraintParentBone(skeleton, driven);
                }
            }

            if (changed)
            {
                skinningCache.events.skeletonTopologyChanged.Invoke(skeleton);
                skinningCache.events.skeletonBindPoseChanged.Invoke(skeleton);
                skinningCache.events.skeletonPreviewPoseChanged.Invoke(skeleton);
            }
        }

        bool EnsureConstraintParentBone(SkeletonCache skeleton, BoneCache driven)
        {
            string parentGuid = UnityEngine.U2D.Animation.SpriteSkinConstraintParent.GetGuid(driven.guid);
            BoneCache parent = FindBone(skeleton.bones, parentGuid);

            if (parent == null)
            {
                parent = skinningCache.CreateCache<BoneCache>();
                parent.name = $"{driven.name} Constraint";
                parent.guid = parentGuid;
                parent.bindPoseColor = new Color32(64, 210, 255, 255);
                parent.localLength = k_ConstraintParentLength;
                parent.depth = driven.depth;
                parent.SetParent(driven.parent);
                parent.position = driven.position;
                parent.rotation = driven.rotation;
                skeleton.AddBone(parent);
            }

            if (driven.parentBone == parent && driven.localPosition == Vector3.zero && driven.localRotation == Quaternion.identity)
                return false;

            parent.position = driven.position;
            parent.rotation = driven.rotation;
            parent.localLength = k_ConstraintParentLength;
            driven.SetParent(parent);
            driven.localPosition = Vector3.zero;
            driven.localRotation = Quaternion.identity;
            parent.SetDefaultPose();
            driven.SetDefaultPose();
            return true;
        }

        void RefreshPreviewBindings()
        {
            List<PreviewConstraint> previousConstraints = new List<PreviewConstraint>(m_PreviewConstraints);
            m_PreviewConstraints.Clear();
            m_BoundConstraintSet = s_ActiveConstraintSet;
            m_BoundSprite = skinningCache.selectedSprite;
            m_BoundMode = skinningCache.mode;

            RuntimeConstraintSet set = s_ActiveConstraintSet;
            SkeletonCache skeleton = skinningCache.GetEffectiveSkeleton(skinningCache.selectedSprite);
            if (set == null || skeleton == null)
                return;

            BoneCache[] bones = skeleton.bones;
            foreach (RuntimeConstraint constraint in set.constraints)
            {
                BoneCache source = FindBone(bones, constraint.sourceBoneGuid);
                BoneCache driven = FindDrivenPreviewTarget(bones, constraint.drivenBoneGuid);
                if (source == null || driven == null || source == driven)
                    continue;

                m_PreviewConstraints.Add(new PreviewConstraint(constraint, source, driven, FindPreviousPreviewConstraint(previousConstraints, constraint, source, driven)));
            }
        }

        static BoneCache FindDrivenPreviewTarget(BoneCache[] bones, string drivenGuid)
        {
            BoneCache parent = FindBone(bones, UnityEngine.U2D.Animation.SpriteSkinConstraintParent.GetGuid(drivenGuid));
            return parent != null ? parent : FindBone(bones, drivenGuid);
        }

        static PreviewConstraint FindPreviousPreviewConstraint(List<PreviewConstraint> previousConstraints, RuntimeConstraint constraint, BoneCache source, BoneCache driven)
        {
            for (int i = 0; i < previousConstraints.Count; ++i)
            {
                if (previousConstraints[i].Matches(constraint, source, driven))
                    return previousConstraints[i];
            }

            return null;
        }

        void EnsurePreviewBindingsCurrent()
        {
            if (m_BoundConstraintSet != s_ActiveConstraintSet ||
                m_BoundSprite != skinningCache.selectedSprite ||
                m_BoundMode != skinningCache.mode)
            {
                RefreshPreviewBindings();
            }
        }

        void ApplyPreviewConstraints()
        {
            if (m_PreviewConstraints.Count == 0)
                return;

            bool changed = false;
            for (int i = 0; i < m_PreviewConstraints.Count; ++i)
                changed |= m_PreviewConstraints[i].Apply();

            if (changed)
            {
                SkeletonCache skeleton = skinningCache.GetEffectiveSkeleton(skinningCache.selectedSprite);
                if (skeleton != null)
                {
                    skeleton.SetPosePreview();
                    skinningCache.events.skeletonPreviewPoseChanged.Invoke(skeleton);
                }
            }
        }

        static BoneCache FindBone(BoneCache[] bones, string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return null;

            for (int i = 0; i < bones.Length; ++i)
            {
                if (bones[i].guid == guid)
                    return bones[i];
            }

            return null;
        }

        static BoneCache[] GetUserBones(BoneCache[] bones)
        {
            return bones.Where(bone => bone != null && !bone.IsConstraintParent()).ToArray();
        }

        class PreviewConstraint
        {
            readonly RuntimeConstraint m_Data;
            readonly BoneCache m_Source;
            readonly BoneCache m_Driven;
            readonly Vector3 m_SourcePosition;
            readonly Vector3 m_DrivenPosition;
            readonly Vector3 m_SourceScale;
            readonly Vector3 m_DrivenScale;
            readonly Quaternion m_SourceRotation;
            readonly Quaternion m_DrivenRotation;

            public PreviewConstraint(RuntimeConstraint data, BoneCache source, BoneCache driven, PreviewConstraint previous)
            {
                m_Data = data;
                m_Source = source;
                m_Driven = driven;
                m_SourcePosition = previous != null ? previous.m_SourcePosition : source.localPosition;
                m_DrivenPosition = previous != null ? previous.m_DrivenPosition : driven.localPosition;
                m_SourceScale = previous != null ? previous.m_SourceScale : source.localScale;
                m_DrivenScale = previous != null ? previous.m_DrivenScale : driven.localScale;
                m_SourceRotation = previous != null ? previous.m_SourceRotation : source.localRotation;
                m_DrivenRotation = previous != null ? previous.m_DrivenRotation : driven.localRotation;
            }

            public bool Matches(RuntimeConstraint data, BoneCache source, BoneCache driven)
            {
                return ReferenceEquals(m_Data, data) && m_Source == source && m_Driven == driven;
            }

            public bool Apply()
            {
                float influence = Mathf.Clamp01(m_Data.influence);
                Vector3 multiplier = m_Data.multiplier;

                switch (m_Data.type)
                {
                    case RuntimeConstraintType.Position:
                    {
                        Vector3 positionDelta = Vector3.Scale(m_Source.localPosition - m_SourcePosition, multiplier) * influence;
                        Vector3 position = m_DrivenPosition + positionDelta;
                        if ((m_Driven.localPosition - position).sqrMagnitude <= 0.000001f)
                            return false;

                        m_Driven.localPosition = position;
                        return true;
                    }

                    case RuntimeConstraintType.Rotation:
                    {
                        Vector3 sourceEuler = m_SourceRotation.eulerAngles;
                        Vector3 currentEuler = m_Source.localRotation.eulerAngles;
                        Vector3 rotationDelta = new Vector3(
                            Mathf.DeltaAngle(sourceEuler.x, currentEuler.x) * multiplier.x,
                            Mathf.DeltaAngle(sourceEuler.y, currentEuler.y) * multiplier.y,
                            Mathf.DeltaAngle(sourceEuler.z, currentEuler.z) * multiplier.z) * influence;
                        Quaternion rotation = m_DrivenRotation * Quaternion.Euler(rotationDelta);
                        if (Quaternion.Angle(m_Driven.localRotation, rotation) <= 0.001f)
                            return false;

                        m_Driven.localRotation = rotation;
                        return true;
                    }

                    case RuntimeConstraintType.Scale:
                    {
                        Vector3 scaleDelta = Vector3.Scale(m_Source.localScale - m_SourceScale, multiplier) * influence;
                        Vector3 scale = m_DrivenScale + scaleDelta;
                        if ((m_Driven.localScale - scale).sqrMagnitude <= 0.000001f)
                            return false;

                        m_Driven.localScale = scale;
                        return true;
                    }
                }

                return false;
            }
        }
    }

    internal class ConstraintsPanel : VisualElement
    {
        const int k_PanelWidth = 500;
        const int k_ContentWidth = 460;
        const int k_ButtonRowWidth = 463;
        const int k_LabelWidth = 80;
        const int k_FieldWidth = 380;
        const int k_PickPopupWidth = 312;
        const int k_PickButtonWidth = 64;
        const int k_InfluenceSliderWidth = 320;
        const int k_InfluenceValueWidth = 56;
        const int k_MultiplierFieldWidth = 40;
        const int k_MultiplierAxisWidth = 14;
        const int k_MultiplierGap = 10;
        const int k_SetFieldWidth = 298;
        const int k_CreateButtonWidth = 74;
        const int k_ListItemHeight = 22;
        const int k_RowHeight = 24;

        ObjectField m_SetField;
        Button m_CreateSetButton;
        PopupField<string> m_SourcePopup;
        PopupField<string> m_DrivenPopup;
        Button m_PickSourceButton;
        Button m_PickDrivenButton;
        Slider m_Influence;
        FloatField m_InfluenceField;
        FloatField m_MultiplierX;
        FloatField m_MultiplierY;
        FloatField m_MultiplierZ;
        ListView m_ListView;
        Button m_AddButton;
        Button m_UpdateButton;
        Button m_RemoveButton;
        Label m_Status;

        readonly List<BoneCache> m_Bones = new List<BoneCache>();
        readonly List<string> m_BoneNames = new List<string>();
        readonly List<RuntimeConstraint> m_FilteredConstraints = new List<RuntimeConstraint>();
        RuntimeConstraintType m_Type;
        RuntimeConstraint m_SelectedConstraint;

        public event Action onCreateSet = () => { };
        public event Action<BoneCache, BoneCache, RuntimeConstraintType, float, Vector3> onAdd = (source, driven, type, influence, multiplier) => { };
        public event Action<RuntimeConstraint, BoneCache, BoneCache, RuntimeConstraintType, float, Vector3> onUpdate = (constraint, source, driven, type, influence, multiplier) => { };
        public event Action<RuntimeConstraint> onRemove = constraint => { };
        public event Action onConstraintSetChanged = () => { };
        public event Action onPickSource = () => { };
        public event Action onPickDriven = () => { };
        public event Action<RuntimeConstraint> onSelectedConstraintChanged = constraint => { };

        public RuntimeConstraintSet constraintSet
        {
            get => m_SetField.value as RuntimeConstraintSet;
            set
            {
                m_SetField.SetValueWithoutNotify(value);
                RefreshConstraintList();
                RefreshButtons();
            }
        }

        public ConstraintsPanel()
        {
            if (EditorGUIUtility.isProSkin)
                AddToClassList("Dark");

            RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
            RegisterCallback<MouseUpEvent>(e => e.StopPropagation());

            style.width = k_PanelWidth;
            style.height = 270;

            var popup = new UnityEngine.UIElements.PopupWindow
            {
                name = "ConstraintSettingsWindow",
                text = "Constraint Settings"
            };
            popup.style.width = k_PanelWidth;
            popup.style.paddingLeft = 14;
            popup.style.paddingRight = 14;
            Add(popup);

            m_BoneNames.Add(TextContent.none);

            m_SetField = new ObjectField
            {
                name = "ConstraintSetField",
                objectType = typeof(RuntimeConstraintSet),
                allowSceneObjects = false
            };
            m_SetField.style.width = k_SetFieldWidth;

            m_CreateSetButton = new Button(() => onCreateSet())
            {
                name = "CreateConstraintSetButton",
                text = "Create"
            };
            m_CreateSetButton.style.width = k_CreateButtonWidth;
            m_CreateSetButton.style.marginLeft = 4;
            m_CreateSetButton.style.marginRight = 0;
            popup.Add(CreateRow("Set", m_SetField, m_CreateSetButton));

            m_SourcePopup = new PopupField<string>(m_BoneNames, 0) { name = "SourcePopupField" };
            m_DrivenPopup = new PopupField<string>(m_BoneNames, 0) { name = "DrivenPopupField" };
            m_PickSourceButton = CreatePickButton("PickSourceButton", () => onPickSource());
            m_PickDrivenButton = CreatePickButton("PickDrivenButton", () => onPickDriven());
            popup.Add(CreateRow("Source", CreatePickControl(m_SourcePopup, m_PickSourceButton)));
            popup.Add(CreateRow("Driven", CreatePickControl(m_DrivenPopup, m_PickDrivenButton)));

            m_Influence = new Slider(0f, 1f)
            {
                name = "InfluenceSlider",
                value = 1f
            };
            SetSliderWidth(m_Influence, k_InfluenceSliderWidth);

            m_InfluenceField = new FloatField { name = "InfluenceValueField", value = 1f };
            m_InfluenceField.style.width = k_InfluenceValueWidth;
            m_InfluenceField.style.marginLeft = 4;
            m_InfluenceField.style.marginRight = 0;

            VisualElement influenceContainer = new VisualElement { name = "InfluenceControl" };
            SetFieldWidth(influenceContainer);
            influenceContainer.style.flexDirection = FlexDirection.Row;
            influenceContainer.Add(m_Influence);
            influenceContainer.Add(m_InfluenceField);
            popup.Add(CreateRow("Influence", influenceContainer));

            popup.Add(CreateRow("Multiplier", CreateMultiplierControl()));

            VisualElement buttonRow = new VisualElement { name = "ConstraintButtonRow" };
            buttonRow.style.width = k_ButtonRowWidth;
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 4;

            m_AddButton = CreateActionButton("AddConstraintButton", "Add");
            m_UpdateButton = CreateActionButton("UpdateConstraintButton", "Update");
            m_RemoveButton = CreateActionButton("RemoveConstraintButton", "Remove");
            buttonRow.Add(m_AddButton);
            buttonRow.Add(m_UpdateButton);
            buttonRow.Add(m_RemoveButton);
            popup.Add(buttonRow);

            m_AddButton.clickable.clicked += AddClicked;
            m_UpdateButton.clickable.clicked += UpdateClicked;
            m_RemoveButton.clickable.clicked += RemoveClicked;

            m_ListView = new ListView(m_FilteredConstraints, k_ListItemHeight, MakeConstraintItem, BindConstraintItem)
            {
                name = "ConstraintListView",
                selectionType = SelectionType.Single
            };
            m_ListView.style.width = k_ContentWidth;
            m_ListView.style.height = 70;
            m_ListView.style.marginTop = 4;
            m_ListView.onSelectionChange += OnSelectionChange;
            popup.Add(m_ListView);

            m_Status = new Label { name = "ConstraintStatus" };
            m_Status.style.width = k_ContentWidth;
            m_Status.style.whiteSpace = WhiteSpace.Normal;
            m_Status.style.marginTop = 4;
            popup.Add(m_Status);

            m_SetField.RegisterValueChangedCallback(_ =>
            {
                SelectConstraint(null);
                RefreshConstraintList();
                onConstraintSetChanged();
            });

            m_SourcePopup.RegisterValueChangedCallback(_ => RefreshButtons());
            m_DrivenPopup.RegisterValueChangedCallback(_ => RefreshButtons());
            m_Influence.RegisterValueChangedCallback(evt => m_InfluenceField.SetValueWithoutNotify(evt.newValue));
            m_InfluenceField.RegisterValueChangedCallback(evt =>
            {
                float value = Mathf.Clamp01(evt.newValue);
                m_InfluenceField.SetValueWithoutNotify(value);
                m_Influence.SetValueWithoutNotify(value);
            });
        }

        VisualElement CreateRow(string labelText, VisualElement field, VisualElement extraField = null)
        {
            VisualElement row = new VisualElement();
            row.style.width = k_ContentWidth;
            row.style.height = k_RowHeight;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 2;
            row.style.marginBottom = 2;

            Label label = new Label(labelText);
            label.style.width = k_LabelWidth;
            label.style.minWidth = k_LabelWidth;
            label.style.flexGrow = 0;
            label.style.flexShrink = 0;
            row.Add(label);
            row.Add(field);
            if (extraField != null)
            {
                row.Add(extraField);
            }

            return row;
        }

        static void SetFieldWidth(VisualElement field)
        {
            field.style.width = k_FieldWidth;
            field.style.minWidth = k_FieldWidth;
            field.style.maxWidth = k_FieldWidth;
            field.style.flexGrow = 0;
            field.style.flexShrink = 0;
            field.style.marginRight = 0;
        }

        static void SetSliderWidth(Slider slider, int width)
        {
            slider.style.width = width;
            slider.style.minWidth = width;
            slider.style.maxWidth = width;
            slider.style.flexGrow = 0;
            slider.style.flexShrink = 0;
            slider.style.marginRight = 0;
            slider.RegisterCallback<AttachToPanelEvent>(_ => StretchSliderInput(slider, width));
            StretchSliderInput(slider, width);
        }

        static void StretchSliderInput(Slider slider, int width)
        {
            VisualElement input = slider.Q<VisualElement>(className: "unity-base-slider__input");
            if (input == null)
                return;

            input.style.width = width;
            input.style.minWidth = width;
            input.style.maxWidth = width;
            input.style.flexGrow = 0;
            input.style.flexShrink = 0;
            input.style.marginRight = 0;
        }

        static Button CreateActionButton(string name, string text)
        {
            Button button = new Button { name = name, text = text };
            button.style.flexGrow = 1;
            button.style.flexShrink = 0;
            return button;
        }

        static Button CreatePickButton(string name, Action clicked)
        {
            Button button = new Button(clicked)
            {
                name = name,
                text = "Pick"
            };
            button.style.width = k_PickButtonWidth;
            button.style.minWidth = k_PickButtonWidth;
            button.style.maxWidth = k_PickButtonWidth;
            button.style.flexGrow = 0;
            button.style.flexShrink = 0;
            button.style.marginLeft = 4;
            button.style.marginRight = 0;
            return button;
        }

        static VisualElement CreatePickControl(PopupField<string> popup, Button pickButton)
        {
            VisualElement container = new VisualElement();
            SetFieldWidth(container);
            container.style.flexDirection = FlexDirection.Row;

            popup.style.width = k_PickPopupWidth;
            popup.style.minWidth = k_PickPopupWidth;
            popup.style.maxWidth = k_PickPopupWidth;
            popup.style.flexGrow = 0;
            popup.style.flexShrink = 0;
            popup.style.marginRight = 0;

            container.Add(popup);
            container.Add(pickButton);
            return container;
        }

        VisualElement CreateMultiplierControl()
        {
            VisualElement container = new VisualElement { name = "MultiplierField" };
            SetFieldWidth(container);
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;

            m_MultiplierX = CreateMultiplierField("MultiplierXField");
            m_MultiplierY = CreateMultiplierField("MultiplierYField");
            m_MultiplierZ = CreateMultiplierField("MultiplierZField");

            AddMultiplierAxis(container, "X", m_MultiplierX, 0);
            AddMultiplierAxis(container, "Y", m_MultiplierY, k_MultiplierGap);
            AddMultiplierAxis(container, "Z", m_MultiplierZ, k_MultiplierGap);
            return container;
        }

        static FloatField CreateMultiplierField(string name)
        {
            FloatField field = new FloatField { name = name, value = 1f };
            field.style.width = k_MultiplierFieldWidth;
            field.style.flexGrow = 0;
            field.style.flexShrink = 0;
            field.style.marginLeft = 0;
            field.style.marginRight = 0;
            return field;
        }

        static void AddMultiplierAxis(VisualElement container, string labelText, FloatField field, int marginLeft)
        {
            Label label = new Label(labelText);
            label.style.width = k_MultiplierAxisWidth;
            label.style.minWidth = k_MultiplierAxisWidth;
            label.style.flexGrow = 0;
            label.style.flexShrink = 0;
            label.style.marginLeft = marginLeft;
            label.style.marginRight = 4;
            container.Add(label);
            container.Add(field);
        }

        Vector3 multiplierValue => new Vector3(m_MultiplierX.value, m_MultiplierY.value, m_MultiplierZ.value);

        void SetMultiplierValueWithoutNotify(Vector3 value)
        {
            m_MultiplierX.SetValueWithoutNotify(value.x);
            m_MultiplierY.SetValueWithoutNotify(value.y);
            m_MultiplierZ.SetValueWithoutNotify(value.z);
        }

        public void SetType(RuntimeConstraintType type)
        {
            m_Type = type;
            RefreshConstraintList();
        }

        public void SetBones(BoneCache[] bones, BoneCache[] selection)
        {
            m_Bones.Clear();
            m_Bones.AddRange(bones);
            m_BoneNames.Clear();
            for (int i = 0; i < m_Bones.Count; ++i)
                m_BoneNames.Add($"{i}: {m_Bones[i].name}");
            if (m_BoneNames.Count == 0)
                m_BoneNames.Add(TextContent.none);

            m_SourcePopup.choices = m_BoneNames;
            m_DrivenPopup.choices = m_BoneNames;

            if (m_Bones.Count > 1 && GetPopupBone(m_DrivenPopup) == null)
                m_DrivenPopup.SetValueWithoutNotify(m_BoneNames[1]);

            RefreshButtons();
        }

        public void RefreshConstraintList()
        {
            m_FilteredConstraints.Clear();
            RuntimeConstraintSet set = constraintSet;
            if (set != null)
            {
                foreach (RuntimeConstraint constraint in set.constraints)
                {
                    if (constraint.type == m_Type)
                        m_FilteredConstraints.Add(constraint);
                }
            }

            m_ListView.Rebuild();
            RefreshButtons();
        }

        public void SelectConstraint(RuntimeConstraint constraint)
        {
            m_SelectedConstraint = constraint;
            if (constraint == null)
            {
                m_ListView.ClearSelection();
                RefreshButtons();
                onSelectedConstraintChanged(null);
                return;
            }

            SetPopupToGuid(m_SourcePopup, constraint.sourceBoneGuid);
            SetPopupToGuid(m_DrivenPopup, constraint.drivenBoneGuid);
            m_Influence.SetValueWithoutNotify(constraint.influence);
            m_InfluenceField.SetValueWithoutNotify(constraint.influence);
            SetMultiplierValueWithoutNotify(constraint.multiplier);
            RefreshConstraintList();
            onSelectedConstraintChanged(constraint);
        }

        public void SetSourceBone(BoneCache bone)
        {
            SetPopupToBone(m_SourcePopup, bone);
            RefreshButtons();
        }

        public void SetDrivenBone(BoneCache bone)
        {
            SetPopupToBone(m_DrivenPopup, bone);
            RefreshButtons();
        }

        VisualElement MakeConstraintItem()
        {
            Label label = new Label();
            label.style.height = k_ListItemHeight;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.marginTop = 0;
            label.style.marginBottom = 0;
            return label;
        }

        void BindConstraintItem(VisualElement element, int index)
        {
            Label label = (Label)element;
            RuntimeConstraint constraint = m_FilteredConstraints[index];
            label.text = $"{GetBoneName(constraint.sourceBoneGuid)} -> {GetBoneName(constraint.drivenBoneGuid)}  {constraint.influence:0.##} x {constraint.multiplier}";
        }

        void OnSelectionChange(IEnumerable<object> items)
        {
            SelectConstraint(items.OfType<RuntimeConstraint>().FirstOrDefault());
        }

        void AddClicked()
        {
            onAdd(GetPopupBone(m_SourcePopup), GetPopupBone(m_DrivenPopup), m_Type, m_Influence.value, multiplierValue);
            RefreshConstraintList();
        }

        void UpdateClicked()
        {
            onUpdate(m_SelectedConstraint, GetPopupBone(m_SourcePopup), GetPopupBone(m_DrivenPopup), m_Type, m_Influence.value, multiplierValue);
        }

        void RemoveClicked()
        {
            onRemove(m_SelectedConstraint);
            RefreshConstraintList();
        }

        void RefreshButtons()
        {
            bool hasSet = constraintSet != null;
            bool hasBones = m_Bones.Count >= 2;
            bool hasValidPair = GetPopupBone(m_SourcePopup) != null && GetPopupBone(m_DrivenPopup) != null && GetPopupBone(m_SourcePopup) != GetPopupBone(m_DrivenPopup);
            m_AddButton.SetEnabled(hasSet && hasBones && hasValidPair);
            m_UpdateButton.SetEnabled(hasSet && m_SelectedConstraint != null && hasValidPair);
            m_RemoveButton.SetEnabled(hasSet && m_SelectedConstraint != null);
            m_Status.text = hasSet ? "Select source and driven bones, then add or update a constraint." : "Create or assign a constraint set asset.";
        }

        BoneCache GetPopupBone(PopupField<string> popup)
        {
            int index = m_BoneNames.IndexOf(popup.value);
            if (index < 0 || index >= m_Bones.Count)
                return null;
            return m_Bones[index];
        }

        void SetPopupToBone(PopupField<string> popup, BoneCache bone)
        {
            if (bone == null)
                return;

            int index = m_Bones.IndexOf(bone);
            if (index >= 0 && index < m_BoneNames.Count)
                popup.SetValueWithoutNotify(m_BoneNames[index]);
        }

        void SetPopupToGuid(PopupField<string> popup, string guid)
        {
            for (int i = 0; i < m_Bones.Count; ++i)
            {
                if (m_Bones[i].guid == guid)
                {
                    popup.SetValueWithoutNotify(m_BoneNames[i]);
                    return;
                }
            }
        }

        string GetBoneName(string guid)
        {
            foreach (BoneCache bone in m_Bones)
            {
                if (bone.guid == guid)
                    return bone.name;
            }

            return TextContent.none;
        }
    }
}
