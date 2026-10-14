using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    internal sealed class BoneIconPicker : PopupWindowContent
    {
        const int Columns = 7;
        const int CellSize = 36;
        readonly string m_Selected;
        readonly Action<string> m_Choose;

        internal BoneIconPicker(string selected, Action<string> choose)
        {
            m_Selected = selected;
            m_Choose = choose;
        }

        public override Vector2 GetWindowSize() => new Vector2(Columns * CellSize + 12,
            Mathf.CeilToInt(BoneIconUtility.Choices.Length / (float)Columns) * CellSize + 12);

        public override void OnOpen()
        {
            var root = editorWindow.rootVisualElement;
            root.styleSheets.Add(ResourceLoader.Load<StyleSheet>("SkinningModule/SceneEditBoneWindow.uss"));
            var grid = new VisualElement { name = "boneIconPicker" };
            grid.AddToClassList("bone-icon-picker");
            root.Add(grid);
            foreach (var id in BoneIconUtility.Choices)
            {
                var button = new Button(() =>
                {
                    m_Choose(id);
                    editorWindow.Close();
                }) { name = "boneIcon_" + id, tooltip = id.Substring(5).Replace("@", " · ") };
                button.AddToClassList("bone-icon-picker-button");
                button.EnableInClassList("bone-icon-current", id == m_Selected);
                var image = new Image { pickingMode = PickingMode.Ignore };
                image.AddToClassList("bone-icon-image");
                BoneIconUtility.SetImage(image, id);
                button.Add(image);
                grid.Add(button);
            }
        }

        public override void OnGUI(Rect rect) { }
    }
}
