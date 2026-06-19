using System;
using UnityEditor.U2D.Common;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
#if ENABLE_UXML_SERIALIZED_DATA
    [UxmlElement]
#endif
    internal partial class ConstraintsToolbar : Toolbar
    {
        const string k_UxmlPath = "SkinningModule/ConstraintsToolbar.uxml";
        const string k_ToolbarId = "ConstraintsToolbar";
        const string k_PositionId = "PositionConstraint";
        const string k_RotationId = "RotationConstraint";
        const string k_ScaleId = "ScaleConstraint";

#if ENABLE_UXML_TRAITS
        public class CustomUXMLFactor : UxmlFactory<ConstraintsToolbar, UxmlTraits> { }
#endif

        Button m_PositionButton;
        Button m_RotationButton;
        Button m_ScaleButton;

        public event Action<Tools> SetConstraintTool = tool => { };
        public SkinningCache skinningCache { get; set; }

        public static ConstraintsToolbar GenerateFromUXML()
        {
            ConstraintsToolbar toolbar = GetClone(k_UxmlPath, k_ToolbarId) as ConstraintsToolbar;
            toolbar.BindElements();
            toolbar.LocalizeTextInChildren();
            return toolbar;
        }

        void BindElements()
        {
            m_PositionButton = this.Q<Button>(k_PositionId);
            m_RotationButton = this.Q<Button>(k_RotationId);
            m_ScaleButton = this.Q<Button>(k_ScaleId);
            m_PositionButton.clickable.clicked += () => SetConstraintTool(Tools.ConstraintsPosition);
            m_RotationButton.clickable.clicked += () => SetConstraintTool(Tools.ConstraintsRotation);
            m_ScaleButton.clickable.clicked += () => SetConstraintTool(Tools.ConstraintsScale);
        }

        public void UpdateToggleState()
        {
            SetButtonChecked(m_PositionButton, skinningCache.GetTool(Tools.ConstraintsPosition).isActive);
            SetButtonChecked(m_RotationButton, skinningCache.GetTool(Tools.ConstraintsRotation).isActive);
            SetButtonChecked(m_ScaleButton, skinningCache.GetTool(Tools.ConstraintsScale).isActive);
        }
    }
}
