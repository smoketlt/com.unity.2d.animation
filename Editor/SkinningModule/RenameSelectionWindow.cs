using System;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal class RenameSelectionWindow : EditorWindow
    {
        private const float k_WindowWidth = 300f;
        private const float k_WindowHeight = 92f;

        private string m_Name;
        private Action<string> m_OnAccepted;
        private bool m_ShouldFocusNameField = true;

        public static void Show(string currentName, Action<string> onAccepted)
        {
            RenameSelectionWindow window = CreateInstance<RenameSelectionWindow>();
            window.titleContent = new GUIContent(TextContent.renameTitle);
            window.m_Name = currentName;
            window.m_OnAccepted = onAccepted;
            window.position = GetCenteredPosition(k_WindowWidth, k_WindowHeight);
            window.minSize = new Vector2(k_WindowWidth, k_WindowHeight);
            window.maxSize = new Vector2(k_WindowWidth, k_WindowHeight);
            window.ShowModalUtility();
        }

        private static Rect GetCenteredPosition(float width, float height)
        {
            Rect mainWindowPosition = EditorGUIUtility.GetMainWindowPosition();
            return new Rect(
                mainWindowPosition.x + (mainWindowPosition.width - width) * 0.5f,
                mainWindowPosition.y + (mainWindowPosition.height - height) * 0.5f,
                width,
                height);
        }

        private void OnGUI()
        {
            Event evt = Event.current;
            if (evt.type == EventType.KeyDown)
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    Close();
                    evt.Use();
                    return;
                }

                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    Accept();
                    evt.Use();
                    return;
                }
            }

            EditorGUILayout.Space(8f);
            GUI.SetNextControlName("RenameNameField");
            m_Name = EditorGUILayout.TextField(TextContent.renameNameLabel, m_Name);

            if (m_ShouldFocusNameField)
            {
                m_ShouldFocusNameField = false;
                EditorGUI.FocusTextInControl("RenameNameField");
            }

            GUILayout.FlexibleSpace();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button(TextContent.cancel, GUILayout.Width(80f)))
                    Close();

                if (GUILayout.Button(TextContent.ok, GUILayout.Width(80f)))
                    Accept();
            }

            EditorGUILayout.Space(8f);
        }

        private void Accept()
        {
            m_OnAccepted?.Invoke(m_Name);
            Close();
        }
    }
}
