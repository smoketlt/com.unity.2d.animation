using System.Runtime.InteropServices;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal static class SkinningEditorInput
    {
#if UNITY_EDITOR_WIN
        private const int k_VKMenu = 0x12;
        private const int k_VKLeftMenu = 0xA4;
        private const int k_VKRightMenu = 0xA5;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
#endif

        public static bool altKeyDown { get; private set; }

        public static bool Update(Event evt)
        {
            if (evt == null)
                return false;

            bool previousAltKeyDown = altKeyDown;

#if UNITY_EDITOR_WIN
            altKeyDown = IsPhysicalAltKeyDown();
#else
            if (IsAltKeyEvent(evt, EventType.KeyDown) || (evt.alt && !IsKeyEvent(evt, EventType.KeyUp)))
                altKeyDown = true;
            else if (IsAltKeyEvent(evt, EventType.KeyUp) || IsAnyKeyUpWithoutAlt(evt))
                altKeyDown = false;
#endif

            return previousAltKeyDown != altKeyDown;
        }

#if UNITY_EDITOR_WIN
        private static bool IsPhysicalAltKeyDown()
        {
            return IsVirtualKeyDown(k_VKMenu) ||
                IsVirtualKeyDown(k_VKLeftMenu) ||
                IsVirtualKeyDown(k_VKRightMenu);
        }

        private static bool IsVirtualKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }
#endif

        private static bool IsAltKeyEvent(Event evt, EventType eventType)
        {
            return (evt.type == eventType || evt.rawType == eventType) &&
                (evt.keyCode == KeyCode.LeftAlt || evt.keyCode == KeyCode.RightAlt);
        }

        private static bool IsKeyEvent(Event evt, EventType eventType)
        {
            return evt.type == eventType || evt.rawType == eventType;
        }

        private static bool IsAnyKeyUpWithoutAlt(Event evt)
        {
            return IsKeyEvent(evt, EventType.KeyUp) && !evt.alt;
        }

        public static void Reset()
        {
            altKeyDown = false;
        }
    }
}
