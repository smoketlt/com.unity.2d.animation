using System;
using System.Runtime.CompilerServices;
using UnityEditor.U2D.Animation;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Layout
{
    internal static class LayoutOverlayUtility
    {
        static readonly ConditionalWeakTable<VisualElement, OverlayPanelLayout> s_PanelLayouts = new ConditionalWeakTable<VisualElement, OverlayPanelLayout>();

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

        public static void MakeDraggableOverlayPanel(VisualElement panel, string layoutKey = null, Vector2? minimumSize = null)
        {
            if (panel.Q<VisualElement>("OverlayDragHandle") != null)
                return;

            panel.AddToClassList("DraggableOverlayPanel");
            var panelLayout = new OverlayPanelLayout(panel, layoutKey ?? panel.GetType().FullName + "." + panel.name, minimumSize);
            s_PanelLayouts.Add(panel, panelLayout);

            VisualElement handle = new VisualElement
            {
                name = "OverlayDragHandle",
                pickingMode = PickingMode.Position
            };
            handle.AddToClassList("OverlayDragHandle");
            handle.AddManipulator(new OverlayPanelDragger(panelLayout));
            panel.Add(handle);

            var resizeHandle = new Label("◢")
            {
                name = "OverlayResizeHandle",
                tooltip = TextContent.resizePanelTooltip,
                pickingMode = PickingMode.Position
            };
            resizeHandle.AddToClassList("OverlayResizeHandle");
            resizeHandle.AddManipulator(new OverlayPanelDragger(panelLayout, true));
            panel.Add(resizeHandle);
        }

        public static void SaveDraggableOverlayPanel(VisualElement panel)
        {
            if (s_PanelLayouts.TryGetValue(panel, out var panelLayout))
                panelLayout.Save();
        }

        public static void SetOverlayPanelLayoutKey(VisualElement panel, string layoutKey)
        {
            if (s_PanelLayouts.TryGetValue(panel, out var panelLayout))
                panelLayout.SetLayoutKey(layoutKey);
        }
    }

}
