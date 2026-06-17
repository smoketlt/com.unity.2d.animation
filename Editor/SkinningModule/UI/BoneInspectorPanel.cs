using System;
using UnityEditor.U2D.Common;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
#if ENABLE_UXML_SERIALIZED_DATA
    [UxmlElement]
#endif
    internal partial class BoneInspectorPanel : VisualElement
    {
        [Flags]
        internal enum PropertyReadOnly
        {
            None,
            Name = 1,
            Depth = 1 << 2,
            Position = 1 << 3,
            Rotation = 1 << 4,
            Color = 1 << 5,
            Length = 1 << 6
        }

#if ENABLE_UXML_TRAITS
        public class BoneInspectorPanelFactory : UxmlFactory<BoneInspectorPanel, BoneInspectorPanelUxmlTraits> { }
        public class BoneInspectorPanelUxmlTraits : UxmlTraits { }
#endif

        public event Action<BoneCache, int> onBoneDepthChanged = (bone, depth) => { };
        public event Action<BoneCache, Vector2> onBonePositionChanged = (bone, position) => { };
        public event Action<BoneCache, float> onBoneRotationChanged = (bone, rotation) => { };
        public event Action<BoneCache, float> onBoneLengthChanged = (bone, length) => { };
        public event Action<BoneCache, string> onBoneNameChanged = (bone, name) => { };
        public event Action<BoneCache, Color32> onBoneColorChanged = (bone, color) => { };
        public event Action<BoneCache[], string> onBonesNameChanged = (bones, name) => { };
        public event Action<BoneCache[], int> onBonesDepthChanged = (bones, depth) => { };
        public event Action<BoneCache[], Color32> onBonesColorChanged = (bones, color) => { };

        private TextField m_BoneNameField;
        private IntegerField m_BoneDepthField;
        private FloatField m_BoneRotationField;
        private FloatField m_BoneLengthField;
        private Vector2Field m_BonePositionField;
        private ColorField m_BoneColorField;
        private UnityEngine.UIElements.PopupWindow m_PopupWindow;
        private VisualElement m_BonePositionRow;
        private Label m_BoneNameLabel;

        public string boneName
        {
            get { return m_BoneNameField.value; }
            set { m_BoneNameField.value = value; }
        }

        public BoneCache target { get; set; }
        public BoneCache[] targets { get; set; } = Array.Empty<BoneCache>();

        public int boneDepth
        {
            get { return m_BoneDepthField.value; }
            set { m_BoneDepthField.value = value; }
        }

        public Vector2 bonePosition
        {
            get { return m_BonePositionField.value; }
            set { m_BonePositionField.SetValueWithoutNotify(value); }
        }

        public float boneRotation
        {
            get { return m_BoneRotationField.value; }
            set { m_BoneRotationField.SetValueWithoutNotify(value); }
        }

        public float boneLength
        {
            get { return m_BoneLengthField.value; }
            set { m_BoneLengthField.SetValueWithoutNotify(value); }
        }

        public Color32 boneColor
        {
            get => m_BoneColorField.value;
            set { m_BoneColorField.SetValueWithoutNotify(value); }
        }

        public BoneInspectorPanel()
        {
            styleSheets.Add(ResourceLoader.Load<StyleSheet>("SkinningModule/BoneInspectorPanelStyle.uss"));

            RegisterCallback<MouseDownEvent>((e) => { e.StopPropagation(); });
            RegisterCallback<MouseUpEvent>((e) => { e.StopPropagation(); });
        }

        public void BindElements()
        {
            m_BoneNameField = this.Q<TextField>("BoneNameField");
            m_BoneDepthField = this.Q<IntegerField>("BoneDepthField");
            m_BoneRotationField = this.Q<FloatField>("BoneRotationField");
            m_BoneLengthField = this.Q<FloatField>("BoneLengthField");
            m_BonePositionField = this.Q<Vector2Field>("BonePositionField");
            m_BoneColorField = this.Q<ColorField>("BoneColorField");
            m_PopupWindow = this.Q<UnityEngine.UIElements.PopupWindow>("BoneInspectorPopupWindow");
            m_BonePositionRow = this.Q<VisualElement>("BonePosition");
            m_BoneNameLabel = this.Q<Label>("BoneNameLabel");
            m_BoneNameField.RegisterCallback<FocusOutEvent>(BoneNameFocusChanged);
            m_BoneDepthField.RegisterCallback<FocusOutEvent>(BoneDepthFocusChanged);
            m_BoneRotationField.RegisterValueChangedCallback(evt => onBoneRotationChanged(target, evt.newValue));
            m_BoneLengthField.RegisterValueChangedCallback(evt => onBoneLengthChanged(target, evt.newValue));
            m_BonePositionField.RegisterValueChangedCallback(evt => onBonePositionChanged(target, evt.newValue));
            m_BoneColorField.RegisterValueChangedCallback(evt =>
            {
                if (isMultiSelection)
                    onBonesColorChanged(targets, evt.newValue);
                else
                    onBoneColorChanged(target, evt.newValue);
            });
        }

        private void BoneNameFocusChanged(FocusOutEvent evt)
        {
            if (isMultiSelection)
                onBonesNameChanged(targets, boneName);
            else
                onBoneNameChanged(target, boneName);
        }

        private void BoneDepthFocusChanged(FocusOutEvent evt)
        {
            if (isMultiSelection)
                onBonesDepthChanged(targets, boneDepth);
            else
                onBoneDepthChanged(target, boneDepth);
        }

        bool isMultiSelection => targets != null && targets.Length > 1;

        public void SetMultiSelectionMode(bool multiSelection)
        {
            m_PopupWindow.text = multiSelection ? "Bones" : "Bone";
            m_BoneNameLabel.text = multiSelection ? "Rename bones" : "Name";
            m_BoneColorField.label = multiSelection ? "Bones Color" : "Bone Color";
            m_BonePositionRow.SetHiddenFromLayout(multiSelection);
            m_BoneRotationField.SetHiddenFromLayout(multiSelection);
            m_BoneLengthField.SetHiddenFromLayout(multiSelection);
            style.height = multiSelection ? 98 : 162;
        }

        public void HidePanel()
        {
            // We are hidding the panel, sent any unchanged value
            this.SetHiddenFromLayout(true);
            if (!isMultiSelection)
            {
                onBoneNameChanged(target, boneName);
                onBoneDepthChanged(target, boneDepth);
            }
        }
        public static BoneInspectorPanel GenerateFromUXML()
        {
            VisualTreeAsset visualTree = ResourceLoader.Load<VisualTreeAsset>("SkinningModule/BoneInspectorPanel.uxml");
            BoneInspectorPanel clone = visualTree.CloneTree().Q<BoneInspectorPanel>("BoneInspectorPanel");
            clone.LocalizeTextInChildren();
            clone.BindElements();
            return clone;
        }

        public void SetReadOnly(PropertyReadOnly property)
        {
            m_BoneDepthField.SetEnabled(!property.HasFlag(PropertyReadOnly.Depth));
            m_BoneNameField.SetEnabled(!property.HasFlag(PropertyReadOnly.Name));
            m_BonePositionField.SetEnabled(!property.HasFlag(PropertyReadOnly.Position));
            m_BoneRotationField.SetEnabled(!property.HasFlag(PropertyReadOnly.Rotation));
            m_BoneLengthField.SetEnabled(!property.HasFlag(PropertyReadOnly.Length));
            m_BoneColorField.SetEnabled(!property.HasFlag(PropertyReadOnly.Color));
        }
    }
}
