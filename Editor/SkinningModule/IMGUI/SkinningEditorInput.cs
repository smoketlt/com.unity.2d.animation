using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal static class SkinningEditorInput
    {
        public static bool altKeyDown { get; private set; }

        public static bool Update(Event evt)
        {
            if (evt == null)
                return false;

            bool previousAltKeyDown = altKeyDown;

            if (IsAltKeyEvent(evt, EventType.KeyDown) || (evt.alt && !IsKeyEvent(evt, EventType.KeyUp)))
                altKeyDown = true;
            else if (IsAltKeyEvent(evt, EventType.KeyUp) || IsAnyKeyUpWithoutAlt(evt))
                altKeyDown = false;

            return previousAltKeyDown != altKeyDown;
        }

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
