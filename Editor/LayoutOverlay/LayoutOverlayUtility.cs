using System;
using UnityEditor.U2D.Animation;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Layout
{
    internal static class LayoutOverlayUtility
    {
        public static Button CreateButton(string name, Action clickEvent, string tooltip = null, string text = null, string imageResourcePath = null, string stylesheetPath = null)
        {
            Button button = new Button(clickEvent);
            button.name = name;
            button.tooltip = tooltip;

            if (!String.IsNullOrEmpty(text))
                button.text = text;
            if (!String.IsNullOrEmpty(imageResourcePath))
            {
                Texture texture = ResourceLoader.Load<Texture>(imageResourcePath);
                if (texture != null)
                {
                    Image image = new Image();
                    image.image = texture;
                    button.Add(image);
                }
            }
            if (!String.IsNullOrEmpty(stylesheetPath))
                button.styleSheets.Add(ResourceLoader.Load<StyleSheet>(stylesheetPath));

            return button;
        }

        public static void MakeDraggableOverlayPanel(VisualElement panel)
        {
            if (panel.Q<VisualElement>("OverlayDragHandle") != null)
                return;

            panel.AddToClassList("DraggableOverlayPanel");

            VisualElement handle = new VisualElement
            {
                name = "OverlayDragHandle",
                pickingMode = PickingMode.Position
            };
            handle.AddToClassList("OverlayDragHandle");
            handle.AddManipulator(new OverlayPanelDragger(panel));
            panel.Add(handle);
        }

        public static void ResetDraggableOverlayPanel(VisualElement panel)
        {
            panel.style.position = Position.Relative;
            panel.style.left = StyleKeyword.Auto;
            panel.style.top = StyleKeyword.Auto;
            panel.style.right = StyleKeyword.Auto;
            panel.style.bottom = StyleKeyword.Auto;
            panel.style.width = StyleKeyword.Auto;
            panel.style.height = StyleKeyword.Auto;
        }
    }

}
