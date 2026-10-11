using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    internal class PruneWeightsWindow : EditorWindow
    {
        private const int kMinBones = 1;
        private const int kMaxBones = 4;

        private int m_Bones = 4;
        private float m_ThresholdPercent = 5f;
        private Func<int, float, int> m_GetRemovedCount;
        private Action<int, float> m_Apply;
        [SerializeField] private bool m_SceneStyle;

        public static void ShowWindow(Func<int, float, int> getRemovedCount, Action<int, float> apply, bool sceneStyle = false)
        {
            PruneWeightsWindow window = CreateInstance<PruneWeightsWindow>();
            window.titleContent = new GUIContent(TextContent.pruneWeights);
            window.m_SceneStyle = sceneStyle;
            var size = sceneStyle ? new Vector2(320f, 160f) / Mathf.Max(1, EditorGUIUtility.pixelsPerPoint) : new Vector2(280f, 132f);
            window.minSize = size;
            if (!sceneStyle) window.maxSize = size;
            var origin = focusedWindow != null ? focusedWindow.position.position + new Vector2(24, 32) : new Vector2(100, 100);
            window.position = new Rect(origin, size);
            window.m_GetRemovedCount = getRemovedCount;
            window.m_Apply = apply;
            if (sceneStyle) window.ShowPopup();
            else window.ShowUtility();
        }

        public void CreateGUI()
        {
            if (!m_SceneStyle) return;
            rootVisualElement.styleSheets.Add(ResourceLoader.Load<StyleSheet>("SkinningModule/SceneEditMeshWindow.uss"));
            var panel = new VisualElement(); panel.AddToClassList("uniskin-mesh-window"); rootVisualElement.Add(panel);
            var header = new VisualElement { name = "windowHeader" }; header.AddToClassList("mesh-header"); panel.Add(header);
            var title = new Label("Prune") { name = "windowTitle" }; title.AddToClassList("mesh-window-title"); header.Add(title);
            header.Add(new Button { name = "closeWindow", text = "\u00d7" });
            var body = new VisualElement(); body.AddToClassList("mesh-body"); panel.Add(body);
            var block = new VisualElement(); block.AddToClassList("mesh-block"); body.Add(block);
            var bones = new SliderInt("Bones", kMinBones, kMaxBones) { value = m_Bones, showInputField = true };
            var threshold = new Slider("Threshold", 0, 100) { value = m_ThresholdPercent, showInputField = true };
            block.Add(bones); block.Add(threshold);
            var count = new Label(); count.style.unityTextAlign = TextAnchor.MiddleCenter; count.style.marginBottom = 6; block.Add(count);
            void RefreshCount() => count.text = $"{m_GetRemovedCount?.Invoke(m_Bones, m_ThresholdPercent * .01f) ?? 0} weights removed.";
            var boneFill = SliderFill(bones); var thresholdFill = SliderFill(threshold);
            void Refresh()
            {
                boneFill.style.width = Length.Percent((m_Bones - kMinBones) * 100f / (kMaxBones - kMinBones));
                thresholdFill.style.width = Length.Percent(m_ThresholdPercent); RefreshCount();
            }
            bones.RegisterValueChangedCallback(evt => { m_Bones = evt.newValue; Refresh(); });
            threshold.RegisterValueChangedCallback(evt => { m_ThresholdPercent = evt.newValue; Refresh(); });
            var actions = new VisualElement(); actions.AddToClassList("mesh-row"); block.Add(actions);
            actions.Add(new Button(() => { m_Apply?.Invoke(m_Bones, m_ThresholdPercent * .01f); Close(); }) { text = "OK" });
            var cancel = new Button(Close) { text = "Cancel" }; cancel.AddToClassList("mesh-last-action"); actions.Add(cancel);
            SceneMeshWindowStyle.Configure(rootVisualElement, this, Close);
            Refresh(); rootVisualElement.schedule.Execute(RefreshCount).Every(200);
        }

        static VisualElement SliderFill(VisualElement slider)
        {
            var fill = new VisualElement { pickingMode = PickingMode.Ignore }; fill.AddToClassList("mesh-slider-fill");
            slider.Q(className: "unity-base-slider__tracker").Add(fill); return fill;
        }

        private void OnGUI()
        {
            if (m_SceneStyle) return;
            EditorGUILayout.Space(8f);

            m_Bones = EditorGUILayout.IntSlider("Bones:", m_Bones, kMinBones, kMaxBones);
            m_ThresholdPercent = EditorGUILayout.Slider("Threshold:", m_ThresholdPercent, 0f, 100f);

            int removedCount = m_GetRemovedCount != null ? m_GetRemovedCount(m_Bones, m_ThresholdPercent * 0.01f) : 0;
            GUILayout.Label(string.Format("{0} weights removed.", removedCount), EditorStyles.centeredGreyMiniLabel);

            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("OK", GUILayout.Width(88f)))
            {
                m_Apply?.Invoke(m_Bones, m_ThresholdPercent * 0.01f);
                Close();
            }

            if (GUILayout.Button("Cancel", GUILayout.Width(88f)))
                Close();

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6f);
        }
    }
}
