using System;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    // Scene editing popups own transient sessions and must never resume from a window layout.
    internal abstract class SceneEditWindow : EditorWindow
    {
        [SerializeField] bool m_WasEnabled;
        SceneEditToolScope m_ToolScope;
        protected SceneEditToolScope toolScope => m_ToolScope ?? (m_ToolScope = new SceneEditToolScope());
        internal bool isRestoredWindow { get; private set; }
        internal bool isClosingForReload { get; private set; }

        protected bool EnableWindowLifetime(bool hasSerializedSession)
        {
            // The second condition also catches layouts saved before this marker existed.
            isRestoredWindow = m_WasEnabled || hasSerializedSession;
            m_WasEnabled = true;
            if (isRestoredWindow)
            {
                // Closing directly inside OnEnable can interrupt Unity's layout restoration.
                EditorApplication.delayCall += CloseBeforeReload;
                return false;
            }
            AssemblyReloadEvents.beforeAssemblyReload += CloseBeforeReload;
            return true;
        }

        protected void DisableWindowLifetime()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= CloseBeforeReload;
            EditorApplication.delayCall -= CloseBeforeReload;
        }

        internal void CloseBeforeReload()
        {
            if (this == null) return;
            // Reload discards the session. Never save or display a modal dialog here.
            isClosingForReload = true;
            hasUnsavedChanges = false;
            try
            {
                // EditorWindow.Close dereferences a missing parent on an orphan window.
                if (rootVisualElement.panel == null) DestroyImmediate(this);
                else Close();
            }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { if (this != null) DestroyImmediate(this); }
        }
    }
}
