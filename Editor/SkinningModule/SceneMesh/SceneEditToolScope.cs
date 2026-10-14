namespace UnityEditor.U2D.Animation
{
    // Scene popups are mutually exclusive. Keep tool cleanup shared across all three hosts.
    internal sealed class SceneEditToolScope
    {
        Tool m_PreviousTool;
        bool m_PreviousHidden;
        bool m_Active;

        public void Hide()
        {
            if (!m_Active)
            {
                m_PreviousTool = UnityEditor.Tools.current;
                m_PreviousHidden = UnityEditor.Tools.hidden;
                m_Active = true;
                UnityEditor.Tools.current = Tool.None;
            }
            // Choosing Move/Rotate/Scale again must not bring back object handles.
            UnityEditor.Tools.hidden = true;
        }

        public bool Restore()
        {
            if (!m_Active) return false;
            UnityEditor.Tools.hidden = m_PreviousHidden;
            if (UnityEditor.Tools.current == Tool.None)
            {
                // Tool.Custom is a category, not the identity of the entry tool.
                // Assigning it can revive a stale component tool (e.g. Light Probes)
                // whose target was destroyed while the popup was open.
                if (m_PreviousTool == Tool.Custom)
                    EditorTools.ToolManager.RestorePreviousPersistentTool();
                else UnityEditor.Tools.current = m_PreviousTool;
            }
            m_Active = false;
            return true;
        }
    }
}
