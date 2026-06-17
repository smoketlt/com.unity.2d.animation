using System;
using System.Collections.Generic;
using UnityEditor.U2D.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
#if ENABLE_UXML_SERIALIZED_DATA
    [UxmlElement]
#endif
    internal partial class WeightPainterPanel : VisualElement
    {
#if ENABLE_UXML_TRAITS
        public class WeightPainterPanelFactory : UxmlFactory<WeightPainterPanel, WeightPainterPanelUxmlTraits> {}
        public class WeightPainterPanelUxmlTraits : UxmlTraits {}
#endif
        public static readonly string kNone = "None";
        static readonly WeightEditorMode[] k_ModeValues =
        {
            WeightEditorMode.AddAndSubtract,
            WeightEditorMode.GrowAndShrink,
            WeightEditorMode.Smooth
        };
        static readonly List<string> k_ModeLabels = new List<string>
        {
            ObjectNames.NicifyVariableName(WeightEditorMode.AddAndSubtract.ToString()),
            ObjectNames.NicifyVariableName(WeightEditorMode.GrowAndShrink.ToString()),
            ObjectNames.NicifyVariableName(WeightEditorMode.Smooth.ToString())
        };

        private WeightPainterMode m_PaintMode;
        private WeightEditorMode m_Mode = WeightEditorMode.AddAndSubtract;
        private PopupField<string> m_ModeField;
        private IntegerField m_StrengthField;
        private IntegerField m_FeatherField;
        private IntegerField m_SizeField;
        private Slider m_StrengthSlider;
        private Slider m_FeatherSlider;
        private FloatField m_AmountField;
        private Slider m_AmountSlider;
        private Button m_SmoothButton;
        private Button m_PruneButton;
        private VisualElement m_BonePopupContainer;
        private PopupField<string> m_BonePopup;
        private bool m_SliderActive = false;
        private WeightInspectorIMGUIPanel m_WeightInspectorPanel;
        private UnityEngine.UIElements.PopupWindow m_PopupWindow;

        public event Action<int> bonePopupChanged = (s) => { };
        public event Action sliderStarted = () => { };
        public event Action<float> sliderChanged = (s) => { };
        public event Action sliderEnded = () => { };
        public event Action smoothClicked = () => { };
        public event Action pruneClicked = () => { };
        public event Action<int, bool> boneButtonClicked = (boneIndex, additive) => {};
        public event Action<int> lockButtonClicked = (boneIndex) => {};
        public event Action weightsChanged = () => { };
        public event Action brushPreviewChanged = () => { };

        public WeightPainterMode paintMode
        {
            get { return m_PaintMode; }
            set
            {
                if (value == m_PaintMode)
                    return;

                m_PaintMode = value;
                if (m_PaintMode == WeightPainterMode.Brush)
                {
                    RemoveFromClassList("SliderMode");
                    AddToClassList("BrushMode");
                }
                else
                {
                    RemoveFromClassList("BrushMode");
                    AddToClassList("SliderMode");
                }
            }
        }

        public string title
        {
            set { m_PopupWindow.text = value; }
        }

        public WeightEditorMode mode
        {
            get { return m_Mode; }
            set
            {
                m_Mode = value;
                SetModeFieldValue(value);
            }
        }

        public void SetModeDisplayOverride(bool active, WeightEditorMode overrideMode)
        {
            SetModeFieldValue(active ? overrideMode : m_Mode);
        }

        public int boneIndex
        {
            get { return m_BonePopup.index - 1; }
        }

        public int size
        {
            get { return m_SizeField.value; }
            set { m_SizeField.value = value; }
        }

        public int strength
        {
            get { return m_StrengthField.value; }
            set { m_StrengthField.value = value; }
        }

        public int feather
        {
            get { return m_FeatherField.value; }
            set { m_FeatherField.value = value; }
        }

        public void SetBrushParametersWithoutPreview(int strengthValue, int sizeValue, int featherValue)
        {
            m_StrengthField.SetValueWithoutNotify(strengthValue);
            m_StrengthSlider.SetValueWithoutNotify(strengthValue);
            m_SizeField.SetValueWithoutNotify(sizeValue);
            m_FeatherField.SetValueWithoutNotify(featherValue);
            m_FeatherSlider.SetValueWithoutNotify(featherValue);
        }

        public bool normalize
        {
            get { return true; }
            set {}
        }

        public float amount
        {
            get { return m_AmountField.value; }
            set { m_AmountField.value = value; }
        }

        public WeightPainterPanel()
        {
            styleSheets.Add(ResourceLoader.Load<StyleSheet>("SkinningModule/WeightPainterPanelStyle.uss"));
            if (EditorGUIUtility.isProSkin)
                AddToClassList("Dark");

            paintMode = WeightPainterMode.Brush;
            AddToClassList("BrushMode");

            RegisterCallback<MouseDownEvent>((e) => { e.StopPropagation(); });
            RegisterCallback<MouseUpEvent>((e) => { e.StopPropagation(); });
        }

        public void BindElements()
        {
            m_ModeField = this.Q<PopupField<string>>("ModeField");
            m_BonePopupContainer = this.Q<VisualElement>("BoneEnumPopup");
            m_SizeField = this.Q<IntegerField>("SizeField");
            m_StrengthField = this.Q<IntegerField>("StrengthField");
            m_FeatherField = this.Q<IntegerField>("FeatherField");
            m_StrengthSlider = this.Q<Slider>("StrengthSlider");
            m_FeatherSlider = this.Q<Slider>("FeatherSlider");
            m_AmountSlider = this.Q<Slider>("AmountSlider");
            m_AmountField = this.Q<FloatField>("AmountField");
            m_SmoothButton = this.Q<Button>("SmoothButton");
            m_PruneButton = this.Q<Button>("PruneButton");
            m_AmountField.isDelayed = true;
            m_WeightInspectorPanel = this.Q<WeightInspectorIMGUIPanel>("WeightsInspector");
            m_PopupWindow = this.Q<UnityEngine.UIElements.PopupWindow>();

            LinkSliderToIntegerField(m_StrengthSlider, m_StrengthField);
            LinkSliderToIntegerField(m_FeatherSlider, m_FeatherField);
            m_StrengthField.RegisterValueChangedCallback((evt) => brushPreviewChanged());
            m_SizeField.RegisterValueChangedCallback((evt) => brushPreviewChanged());
            m_FeatherField.RegisterValueChangedCallback((evt) => brushPreviewChanged());

            m_ModeField.RegisterValueChangedCallback((evt) =>
            {
                m_Mode = GetModeValue(evt.newValue);
                SetupMode();
            });

            m_AmountSlider.RegisterValueChangedCallback((evt) =>
            {
                if (!evt.Equals(m_AmountField.value))
                    m_AmountField.value = (float)Math.Round((double)evt.newValue, 2);
                if (m_SliderActive)
                    sliderChanged?.Invoke(m_AmountField.value);
            });
            m_AmountSlider.RegisterCallback<MouseCaptureEvent>(evt =>
            {
                m_SliderActive = true;
                sliderStarted?.Invoke();
            }, TrickleDown.TrickleDown);

            m_AmountSlider.RegisterCallback<MouseCaptureOutEvent>(evt =>
            {
                m_SliderActive = false;
                sliderEnded?.Invoke();
                m_AmountSlider.value = 0;
            }, TrickleDown.TrickleDown);

            m_AmountField.RegisterValueChangedCallback((evt) =>
            {
                float newValue = Mathf.Clamp(evt.newValue, m_AmountSlider.lowValue, m_AmountSlider.highValue);

                if (focusController.focusedElement == m_AmountField && !newValue.Equals(m_AmountSlider.value))
                {
                    sliderStarted();
                    sliderChanged(newValue);
                    sliderEnded();
                    Focus();
                    m_AmountField.value = 0f;
                    m_AmountSlider.value = 0f;
                }
            });

            m_WeightInspectorPanel.weightsChanged += () => weightsChanged();
            m_WeightInspectorPanel.boneButtonClicked += (boneIndex, additive) => boneButtonClicked(boneIndex, additive);
            m_WeightInspectorPanel.lockButtonClicked += (boneIndex) => lockButtonClicked(boneIndex);
            m_SmoothButton.text = TextContent.smoothWeights;
            m_SmoothButton.tooltip = TextContent.smoothWeightsTooltip;
            m_SmoothButton.clicked += () => smoothClicked();
            m_PruneButton.text = TextContent.pruneWeights;
            m_PruneButton.tooltip = TextContent.pruneWeightsTooltip;
            m_PruneButton.clicked += () => pruneClicked();
        }

        public void SetActive(bool active)
        {
            this.Q("Amount").SetEnabled(active);
            this.Q("SmoothButtonRow").SetEnabled(active);
        }

        private void SetupMode()
        {
            VisualElement boneElement = this.Q<VisualElement>("Bone");
            boneElement.SetHiddenFromLayout(mode == WeightEditorMode.Smooth);
            SetupAmountSlider();
        }

        private void SetupAmountSlider()
        {
            if (paintMode == WeightPainterMode.Slider)
            {
                if (mode == WeightEditorMode.Smooth)
                {
                    m_AmountSlider.lowValue = 0.0f;
                    m_AmountSlider.highValue = 8.0f;
                }
                else
                {
                    m_AmountSlider.lowValue = -1.0f;
                    m_AmountSlider.highValue = 1.0f;
                }
            }
        }

        private void LinkSliderToIntegerField(Slider slider, IntegerField field)
        {
            slider.RegisterValueChangedCallback((evt) =>
            {
                if (!evt.newValue.Equals(field.value))
                    field.value = Mathf.RoundToInt(evt.newValue);
            });
            field.RegisterValueChangedCallback((evt) =>
            {
                if (!evt.newValue.Equals((int)slider.value))
                    slider.value = evt.newValue;
            });
        }

        public void UpdateWeightInspector(BaseSpriteMeshData spriteMeshData, string[] boneNames, Color[] boneColors, int[] selectedBoneIndices, int[] lockedBoneIndices, ISelection<int> selection, ICacheUndo cacheUndo)
        {
            m_WeightInspectorPanel.weightInspector.spriteMeshData = spriteMeshData;
            m_WeightInspectorPanel.weightInspector.boneNames = ModuleUtility.ToGUIContentArray(boneNames);
            m_WeightInspectorPanel.weightInspector.boneColors = boneColors;
            m_WeightInspectorPanel.weightInspector.selectedBoneIndices = selectedBoneIndices;
            m_WeightInspectorPanel.weightInspector.lockedBoneIndices = lockedBoneIndices;
            m_WeightInspectorPanel.weightInspector.selection = selection;
            m_WeightInspectorPanel.weightInspector.cacheUndo = cacheUndo;
        }

        public void UpdatePanel(string[] boneNames)
        {
            SetupMode();
            UpdateBonePopup(boneNames);
        }

        private void UpdateBonePopup(string[] names)
        {
            VisualElement boneElement = null;
            if (m_ModeField != null && m_Mode == WeightEditorMode.Smooth)
            {
                boneElement = this.Q<VisualElement>("Bone");
                boneElement.SetHiddenFromLayout(false);
            }

            if (m_BonePopup != null)
            {
                m_BonePopupContainer.Remove(m_BonePopup);
            }

            m_BonePopup = new PopupField<string>(new List<string>(names), 0);
            m_BonePopup.name = "BonePopupField";
            m_BonePopup.label = TextContent.bone;
            m_BonePopup.tooltip = TextContent.boneToolTip;
            m_BonePopup.RegisterValueChangedCallback((evt) =>
            {
                bonePopupChanged(boneIndex);
            });
            m_BonePopupContainer.Add(m_BonePopup);

            if (boneElement != null)
            {
                boneElement.SetHiddenFromLayout(true);
            }
        }

        internal void SetBoneSelectionByName(string boneName)
        {
            if (m_BonePopup != null)
                m_BonePopup.value = boneName;
        }

        private static string GetModeLabel(WeightEditorMode mode)
        {
            int index = Array.IndexOf(k_ModeValues, mode);
            return index == -1 ? k_ModeLabels[0] : k_ModeLabels[index];
        }

        private void SetModeFieldValue(WeightEditorMode displayMode)
        {
            if (m_ModeField != null)
                m_ModeField.SetValueWithoutNotify(GetModeLabel(displayMode));
        }

        private static WeightEditorMode GetModeValue(string label)
        {
            int index = k_ModeLabels.IndexOf(label);
            return index == -1 ? k_ModeValues[0] : k_ModeValues[index];
        }

        public static WeightPainterPanel GenerateFromUXML()
        {
            VisualTreeAsset visualTree = ResourceLoader.Load<VisualTreeAsset>("SkinningModule/WeightPainterPanel.uxml");
            WeightPainterPanel clone = visualTree.CloneTree().Q<WeightPainterPanel>("WeightPainterPanel");
            clone.LocalizeTextInChildren();

            // Use an explicit popup instead of EnumField so Git package installs do not depend on
            // UI Toolkit's runtime enum menu resolution for internal editor assemblies.
            VisualElement mode = clone.Q<VisualElement>("Mode");
            PopupField<string> modeField = new PopupField<string>(k_ModeLabels, 0);
            modeField.name = "ModeField";
            modeField.label = TextContent.mode;
            modeField.tooltip = TextContent.modeTooltip;
            mode.Add(modeField);

            clone.BindElements();
            return clone;
        }
    }
}
