using System;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal class UnselectTool<T>
    {
        const float k_ClickMovementThreshold = 3f;

        private Unselector<T> m_Unselector = new Unselector<T>();
        private bool m_ClearOnPrimaryMouseUp;
        private int m_PrimaryEmptyControlID;
        private Vector2 m_PrimaryMouseDownPosition;

        public ICacheUndo cacheUndo { get; set; }
        public int emptyControlID { get; set; }
        public int secondaryEmptyControlID { get; set; }
        public bool allowPrimaryEmptyClickFallback { get; set; } = true;
        public Func<bool> isPrimaryEmptyClickCandidate { get; set; } = () => false;
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
                m_PrimaryEmptyControlID = 0;
                return;
            }

            if (clearOnEscape && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                ClearSelection(e);
                return;
            }

            HandlePrimaryEmptyClick(e);
        }

        void HandlePrimaryEmptyClick(Event e)
        {
            if (!clearOnPrimaryEmptyClick)
                return;

            int nearestEmptyControlID = m_ClearOnPrimaryMouseUp ? m_PrimaryEmptyControlID : GetNearestEmptyControlID();
            bool useFallbackCandidate = nearestEmptyControlID <= 0 &&
                (m_ClearOnPrimaryMouseUp || (allowPrimaryEmptyClickFallback && isPrimaryEmptyClickCandidate()));

            if (!useFallbackCandidate && nearestEmptyControlID <= 0)
                return;

            EventType eventType = useFallbackCandidate ? e.type : e.GetTypeForControl(nearestEmptyControlID);

            if (eventType == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                m_ClearOnPrimaryMouseUp = true;
                m_PrimaryEmptyControlID = nearestEmptyControlID;
                m_PrimaryMouseDownPosition = e.mousePosition;
                return;
            }

            if (!m_ClearOnPrimaryMouseUp)
                return;

            if (eventType == EventType.MouseDrag && (e.mousePosition - m_PrimaryMouseDownPosition).sqrMagnitude > k_ClickMovementThreshold * k_ClickMovementThreshold)
            {
                m_ClearOnPrimaryMouseUp = false;
                m_PrimaryEmptyControlID = 0;
                return;
            }

            if (eventType == EventType.MouseUp && e.button == 0)
            {
                m_ClearOnPrimaryMouseUp = false;
                m_PrimaryEmptyControlID = 0;

                if ((e.mousePosition - m_PrimaryMouseDownPosition).sqrMagnitude <= k_ClickMovementThreshold * k_ClickMovementThreshold)
                {
                    GUIUtility.hotControl = 0;
                    ClearSelection(e);
                }
            }
        }

        int GetNearestEmptyControlID()
        {
            if (emptyControlID > 0 && HandleUtility.nearestControl == emptyControlID)
                return emptyControlID;

            if (secondaryEmptyControlID > 0 && HandleUtility.nearestControl == secondaryEmptyControlID)
                return secondaryEmptyControlID;

            return 0;
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
