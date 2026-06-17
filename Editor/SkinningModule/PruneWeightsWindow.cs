using System;
using UnityEditor;
using UnityEngine;

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

        public static void ShowWindow(Func<int, float, int> getRemovedCount, Action<int, float> apply)
        {
            PruneWeightsWindow window = CreateInstance<PruneWeightsWindow>();
            window.titleContent = new GUIContent(TextContent.pruneWeights);
            window.minSize = new Vector2(280f, 132f);
            window.maxSize = new Vector2(280f, 132f);
            window.m_GetRemovedCount = getRemovedCount;
            window.m_Apply = apply;
            window.ShowUtility();
        }

        private void OnGUI()
        {
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
