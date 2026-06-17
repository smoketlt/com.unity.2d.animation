using System;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal class Brush
    {
        private static readonly float kWheelSizeSpeed = 1f;
        private const float kDeferredStrokeDragThreshold = 3f;
        private static readonly int kBrushHashCode = "Brush".GetHashCode();
        private IGUIWrapper m_GUIWrapper;
        private float m_DeltaAcc = 0f;
        private int m_ControlID = -1;
        private SliderData m_SliderData = SliderData.zero;
        private bool m_StrokeStarted;
        private Vector2 m_StrokeStartMousePosition;

        public event Action<Brush> onMove = (b) => { };
        public event Action<Brush> onSize = (b) => { };
        public event Action<Brush> onRepaint = (b) => { };
        public event Action<Brush> onStrokeBegin = (b) => { };
        public event Action<Brush> onStrokeDelta = (b) => { };
        public event Action<Brush> onStrokeStep = (b) => { };
        public event Action<Brush> onStrokeEnd = (b) => { };

        public bool isHot
        {
            get { return m_GUIWrapper.IsControlHot(m_ControlID); }
        }
        public bool isActivable
        {
            get { return !IsAltDown() && m_GUIWrapper.IsControlHot(0) && (m_GUIWrapper.IsControlNearest(m_ControlID) || captureMouseWhenNotNearest); }
        }

        public int controlID
        {
            get { return m_ControlID; }
        }

        public float hardness { get; set; }
        public float feather { get; set; }
        public float step { get; set; }
        public float size { get; set; }
        public bool captureMouseWhenNotNearest { get; set; }
        public bool deferStrokeStartUntilDrag { get; set; }
        public Vector3 position
        {
            get { return m_SliderData.position; }
        }

        public Brush(IGUIWrapper guiWrapper)
        {
            m_GUIWrapper = guiWrapper;
            size = 25f;
            step = 20f;
        }

        public void OnGUI()
        {
            m_ControlID = m_GUIWrapper.GetControlID(kBrushHashCode, FocusType.Passive);

            EventType eventType = m_GUIWrapper.eventType;

            if (!IsAltDown() && !captureMouseWhenNotNearest)
                m_GUIWrapper.LayoutControl(controlID, 0f);

            if (isActivable)
            {
                m_SliderData.position = m_GUIWrapper.GUIToWorld(m_GUIWrapper.mousePosition);

                if (m_GUIWrapper.IsMouseDown(0))
                {
                    m_DeltaAcc = 0f;
                    m_StrokeStarted = false;
                    m_StrokeStartMousePosition = m_GUIWrapper.mousePosition;
                    if (captureMouseWhenNotNearest)
                        m_GUIWrapper.SetControlHot(controlID);
                    if (!deferStrokeStartUntilDrag)
                        StartStroke();
                    m_GUIWrapper.SetGuiChanged(true);
                    if (captureMouseWhenNotNearest)
                        m_GUIWrapper.UseCurrentEvent();
                }

                if (eventType == EventType.MouseMove)
                {
                    onMove(this);
                    m_GUIWrapper.UseCurrentEvent();
                }

                if (m_GUIWrapper.isShiftDown && eventType == EventType.ScrollWheel)
                {
                    float sizeDelta = HandleUtility.niceMouseDeltaZoom * kWheelSizeSpeed;
                    size = Mathf.Max(1f, size + sizeDelta);
                    onSize(this);
                    m_GUIWrapper.UseCurrentEvent();
                }
            }

            if (isHot && m_GUIWrapper.IsMouseUp(0))
            {
                bool strokeStarted = m_StrokeStarted;
                if (m_StrokeStarted)
                    onStrokeEnd(this);
                m_StrokeStarted = false;
                if (captureMouseWhenNotNearest)
                {
                    m_GUIWrapper.SetControlHot(0);
                    if (!deferStrokeStartUntilDrag || strokeStarted)
                        m_GUIWrapper.UseCurrentEvent();
                }
            }

            if (m_GUIWrapper.IsRepainting() && (isHot || isActivable))
                onRepaint(this);

            if (captureMouseWhenNotNearest)
            {
                if (isHot && eventType == EventType.MouseDrag && m_GUIWrapper.mouseButton == 0)
                {
                    if (deferStrokeStartUntilDrag &&
                        !m_StrokeStarted &&
                        (m_GUIWrapper.mousePosition - m_StrokeStartMousePosition).magnitude < kDeferredStrokeDragThreshold)
                    {
                        m_GUIWrapper.UseCurrentEvent();
                        return;
                    }

                    Vector3 mouseWorldPosition = m_GUIWrapper.GUIToWorld(m_GUIWrapper.mousePosition);
                    MoveStroke(mouseWorldPosition);
                    m_GUIWrapper.UseCurrentEvent();
                }

                return;
            }

            Vector3 position;
            if (m_GUIWrapper.DoSlider(m_ControlID, m_SliderData, out position))
            {
                MoveStroke(position);
            }
        }

        private void StartStroke()
        {
            if (m_StrokeStarted)
                return;

            m_StrokeStarted = true;
            onStrokeBegin(this);
            onStrokeStep(this);
        }

        private void MoveStroke(Vector3 position)
        {
            if (!m_StrokeStarted)
                StartStroke();

            step = Mathf.Max(step, 1f);

            Vector3 delta = position - m_SliderData.position;
            Vector3 direction = delta.normalized;
            float magnitude = delta.magnitude;

            m_SliderData.position -= direction * m_DeltaAcc;

            m_DeltaAcc += magnitude;

            if (m_DeltaAcc >= step)
            {
                Vector3 stepVector = direction * step;

                while (m_DeltaAcc >= step)
                {
                    m_SliderData.position += stepVector;

                    onMove(this);
                    onStrokeStep(this);

                    m_DeltaAcc -= step;
                }
            }

            m_SliderData.position = position;
            onStrokeDelta(this);
        }

        private bool IsAltDown()
        {
            return m_GUIWrapper.isAltDown || SkinningEditorInput.altKeyDown;
        }
    }
}
