using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Layout
{
    internal class OverlayPanelDragger : PointerManipulator
    {
        readonly OverlayPanelLayout m_Layout;
        readonly bool m_Resize;
        Vector2 m_StartPointerPosition;
        Rect m_StartRect;
        int m_PointerId = -1;

        public OverlayPanelDragger(OverlayPanelLayout layout, bool resize = false)
        {
            m_Layout = layout;
            m_Resize = resize;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (m_PointerId != -1 || !CanStartManipulation(evt) || m_Layout.panel.parent == null ||
                !OverlayPanelLayout.IsValidRect(m_Layout.panel.layout))
                return;

            m_StartPointerPosition = m_Layout.panel.parent.WorldToLocal(evt.position);
            m_StartRect = m_Layout.BeginInteraction();
            m_PointerId = evt.pointerId;
            target.CapturePointer(m_PointerId);
            evt.StopImmediatePropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (m_PointerId != evt.pointerId || !target.HasPointerCapture(m_PointerId) || m_Layout.panel.parent == null)
                return;

            Vector2 delta = (Vector2)m_Layout.panel.parent.WorldToLocal(evt.position) - m_StartPointerPosition;
            Rect rect = m_StartRect;
            if (m_Resize)
                rect.size += delta;
            else
                rect.position += delta;
            m_Layout.UpdateInteraction(rect, m_Resize);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (m_PointerId != evt.pointerId || !CanStopManipulation(evt))
                return;

            m_Layout.Save();
            int pointerId = m_PointerId;
            m_PointerId = -1;
            target.ReleasePointer(pointerId);
            evt.StopImmediatePropagation();
        }

        void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            if (m_PointerId != evt.pointerId)
                return;
            m_Layout.Save();
            m_PointerId = -1;
        }
    }
}
