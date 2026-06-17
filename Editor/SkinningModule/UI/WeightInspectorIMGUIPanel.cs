using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
#if ENABLE_UXML_SERIALIZED_DATA
    [UxmlElement]
#endif
    internal partial class WeightInspectorIMGUIPanel : VisualElement
    {
#if ENABLE_UXML_TRAITS
        public class CustomUXMLFactor : UxmlFactory<WeightInspectorIMGUIPanel, UxmlTraits> {}
#endif

        private WeightInspector m_WeightInspector = new WeightInspector();

        public WeightInspector weightInspector
        {
            get { return m_WeightInspector; }
        }

        public event Action weightsChanged = () => { };
        public event Action<int, bool> boneButtonClicked = (boneIndex, additive) => {};
        public event Action<int> lockButtonClicked = (boneIndex) => {};

        public WeightInspectorIMGUIPanel()
        {
            name = "WeightInspectorIMGUIPanel";
            styleSheets.Add(ResourceLoader.Load<StyleSheet>("SkinningModule/WeightInspectorIMGUIPanelStyle.uss"));
            m_WeightInspector.boneButtonClicked = (boneIndex, additive) => boneButtonClicked(boneIndex, additive);
            m_WeightInspector.lockButtonClicked = (boneIndex) => lockButtonClicked(boneIndex);

            this.Add(new IMGUIContainer(OnGUI));
            this.pickingMode = PickingMode.Ignore;
            this.RegisterCallback<MouseDownEvent>((e) => { e.StopPropagation(); });
            this.RegisterCallback<MouseUpEvent>((e) => { e.StopPropagation(); });
        }

        protected void OnGUI()
        {
            int selectionCount = 0;

            if (weightInspector.selection != null)
                selectionCount = weightInspector.selection.Count;

            m_WeightInspector.weightsEditable = m_WeightInspector.spriteMeshData != null && selectionCount > 0;

            using (new EditorGUI.DisabledGroupScope(m_WeightInspector.spriteMeshData == null))
                GUILayout.Label(new GUIContent(TextContent.vertexWeight, TextContent.vertexWeightToolTip));

            EditorGUI.BeginChangeCheck();
            m_WeightInspector.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
                weightsChanged();
        }
    }
}
