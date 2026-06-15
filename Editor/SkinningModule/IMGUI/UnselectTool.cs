using System;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal class UnselectTool<T>
    {
        const float k_ClickMovementThreshold = 3f;

        private Unselector<T> m_Unselector = new Unselector<T>();
        private bool m_ClearOnPrimaryMouseUp;
        private Vector2 m_PrimaryMouseDownPosition;

        public ICacheUndo cacheUndo { get; set; }
        public int emptyControlID { get; set; }
        public bool clearOnEscape { get; set; }
        public bool clearOnPrimaryEmptyClick { get; set; }
        public ISelection<T> selection
        {
            get { return m_Unselector.selection; }
            set { m_Unselector.selection = value; }
        }
        public Action onUnselect = () => { };

        public void OnGUI()
        {
            Debug.Assert(cacheUndo != null);
            Debug.Assert(selection != null);

            Event e = Event.current;

            if (selection.Count == 0)
            {
                m_ClearOnPrimaryMouseUp = false;
                return;
            }

            if (clearOnEscape && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                ClearSelection(e);
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 1 && !e.alt)
            {
                ClearSelection(e);
                return;
            }

            HandlePrimaryEmptyClick(e);
        }

        void HandlePrimaryEmptyClick(Event e)
        {
            if (!clearOnPrimaryEmptyClick || emptyControlID <= 0)
                return;

            EventType eventType = e.GetTypeForControl(emptyControlID);

            if (eventType == EventType.MouseDown && e.button == 0 && !e.alt && HandleUtility.nearestControl == emptyControlID)
            {
                m_ClearOnPrimaryMouseUp = true;
                m_PrimaryMouseDownPosition = e.mousePosition;
                return;
            }

            if (!m_ClearOnPrimaryMouseUp)
                return;

            if (eventType == EventType.MouseDrag && (e.mousePosition - m_PrimaryMouseDownPosition).sqrMagnitude > k_ClickMovementThreshold * k_ClickMovementThreshold)
            {
                m_ClearOnPrimaryMouseUp = false;
                return;
            }

            if (eventType == EventType.MouseUp && e.button == 0)
            {
                m_ClearOnPrimaryMouseUp = false;

                if ((e.mousePosition - m_PrimaryMouseDownPosition).sqrMagnitude <= k_ClickMovementThreshold * k_ClickMovementThreshold)
                    ClearSelection(e);
            }
        }

        void ClearSelection(Event e)
        {
            cacheUndo.BeginUndoOperation(TextContent.clearSelection);
            m_Unselector.Select();
            e.Use();
            onUnselect.Invoke();
        }
    }
}
