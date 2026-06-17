using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Layout
{
    internal class OverlayPanelDragger : MouseManipulator
    {
        private readonly VisualElement m_Panel;
        private Vector2 m_StartMousePosition;
        private Vector2 m_StartPanelPosition;
        private bool m_Active;

        public OverlayPanelDragger(VisualElement panel)
        {
            m_Panel = panel;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<MouseDownEvent>(OnMouseDown);
            target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            target.RegisterCallback<MouseUpEvent>(OnMouseUp);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
            target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (!CanStartManipulation(evt) || m_Panel.parent == null)
                return;

            Rect layout = m_Panel.layout;
            m_StartMousePosition = evt.mousePosition;
            m_StartPanelPosition = layout.position;

            m_Panel.style.position = Position.Absolute;
            m_Panel.style.left = layout.x;
            m_Panel.style.top = layout.y;
            m_Panel.style.right = StyleKeyword.Auto;
            m_Panel.style.bottom = StyleKeyword.Auto;
            m_Panel.style.width = layout.width;
            m_Panel.style.height = layout.height;

            m_Active = true;
            target.CaptureMouse();
            evt.StopImmediatePropagation();
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (!m_Active || m_Panel.parent == null)
                return;

            Vector2 position = m_StartPanelPosition + evt.mousePosition - m_StartMousePosition;
            Rect parentRect = m_Panel.parent.layout;
            Rect panelRect = m_Panel.layout;

            position.x = Mathf.Clamp(position.x, 0f, Mathf.Max(0f, parentRect.width - panelRect.width));
            position.y = Mathf.Clamp(position.y, 0f, Mathf.Max(0f, parentRect.height - panelRect.height));

            m_Panel.style.left = position.x;
            m_Panel.style.top = position.y;
            evt.StopPropagation();
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            if (!m_Active || !CanStopManipulation(evt))
                return;

            m_Active = false;
            target.ReleaseMouse();
            evt.StopImmediatePropagation();
        }
    }
}
