using System;
using UnityEditor.U2D.Common;
using UnityEditor.U2D.Layout;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal class SkeletonToolView
    {
        private BoneInspectorPanel m_BoneInspectorPanel;

        public event Action<BoneCache, string> onBoneNameChanged = (b, s) => { };
        public event Action<BoneCache, int> onBoneDepthChanged = (b, i) => { };
        public event Action<BoneCache, float> onBoneRotationChanged = (b, i) => { };
        public event Action<BoneCache, float> onBoneLengthChanged = (b, i) => { };
        public event Action<BoneCache, Vector2> onBonePositionChanged = (b, i) => { };
        public event Action<BoneCache, Color32> onBoneColorChanged = (b, i) => { };
        public event Action<BoneCache[], string> onBonesNameChanged = (b, s) => { };
        public event Action<BoneCache[], int> onBonesDepthChanged = (b, i) => { };
        public event Action<BoneCache[], Color32> onBonesColorChanged = (b, i) => { };

        public SkeletonToolView()
        {
            m_BoneInspectorPanel = BoneInspectorPanel.GenerateFromUXML();
            m_BoneInspectorPanel.onBoneNameChanged += (b, n) => onBoneNameChanged(b, n);
            m_BoneInspectorPanel.onBoneDepthChanged += (b, d) => onBoneDepthChanged(b, d);
            m_BoneInspectorPanel.onBoneRotationChanged += (b, n) => onBoneRotationChanged(b, n);
            m_BoneInspectorPanel.onBoneLengthChanged += (b, n) => onBoneLengthChanged(b, n);
            m_BoneInspectorPanel.onBonePositionChanged += (b, d) => onBonePositionChanged(b, d);
            m_BoneInspectorPanel.onBoneColorChanged += (b, d) => onBoneColorChanged(b, d);
            m_BoneInspectorPanel.onBonesNameChanged += (b, n) => onBonesNameChanged(b, n);
            m_BoneInspectorPanel.onBonesDepthChanged += (b, d) => onBonesDepthChanged(b, d);
            m_BoneInspectorPanel.onBonesColorChanged += (b, d) => onBonesColorChanged(b, d);
            Hide();
        }

        public void Initialize(LayoutOverlay layout)
        {
            layout.AddBottomOverlayPanel(m_BoneInspectorPanel);
        }

        public void Show(BoneCache target, bool isReadOnly)
        {
            m_BoneInspectorPanel.target = target;
            m_BoneInspectorPanel.targets = Array.Empty<BoneCache>();
            m_BoneInspectorPanel.SetMultiSelectionMode(false);
            m_BoneInspectorPanel.SetHiddenFromLayout(false);
            BoneInspectorPanel.PropertyReadOnly readOnlyProperty = BoneInspectorPanel.PropertyReadOnly.None;
            if (isReadOnly)
                readOnlyProperty = BoneInspectorPanel.PropertyReadOnly.Name |
                    BoneInspectorPanel.PropertyReadOnly.Depth |
                    BoneInspectorPanel.PropertyReadOnly.Length |
                    BoneInspectorPanel.PropertyReadOnly.Color;
            m_BoneInspectorPanel.SetReadOnly(readOnlyProperty);
        }

        public void Show(BoneCache[] targets, bool isReadOnly)
        {
            m_BoneInspectorPanel.target = null;
            m_BoneInspectorPanel.targets = targets ?? Array.Empty<BoneCache>();
            m_BoneInspectorPanel.SetMultiSelectionMode(true);
            m_BoneInspectorPanel.SetHiddenFromLayout(false);
            BoneInspectorPanel.PropertyReadOnly readOnlyProperty = BoneInspectorPanel.PropertyReadOnly.Position |
                BoneInspectorPanel.PropertyReadOnly.Rotation |
                BoneInspectorPanel.PropertyReadOnly.Length;
            if (isReadOnly)
                readOnlyProperty |= BoneInspectorPanel.PropertyReadOnly.Name |
                    BoneInspectorPanel.PropertyReadOnly.Depth |
                    BoneInspectorPanel.PropertyReadOnly.Color;
            m_BoneInspectorPanel.SetReadOnly(readOnlyProperty);
        }

        public BoneCache target => m_BoneInspectorPanel.target;
        public BoneCache[] targets => m_BoneInspectorPanel.targets;

        public void Hide()
        {
            LayoutOverlayUtility.ResetDraggableOverlayPanel(m_BoneInspectorPanel);
            m_BoneInspectorPanel.HidePanel();
            m_BoneInspectorPanel.target = null;
            m_BoneInspectorPanel.targets = Array.Empty<BoneCache>();
        }

        public void Update(string name, int depth, Vector2 position, float rotation, float length, Color32 color)
        {
            m_BoneInspectorPanel.boneName = name;
            m_BoneInspectorPanel.boneDepth = depth;
            m_BoneInspectorPanel.bonePosition = position;
            m_BoneInspectorPanel.boneRotation = rotation;
            m_BoneInspectorPanel.boneLength = length;
            m_BoneInspectorPanel.boneColor = color;
        }
    }
}
