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

            if (IsAltKeyEvent(evt, EventType.KeyDown))
                altKeyDown = true;
            else if (IsAltKeyEvent(evt, EventType.KeyUp))
                altKeyDown = false;
            else if (evt.alt)
                altKeyDown = true;
            else if (IsReliableModifierEvent(evt))
                altKeyDown = false;

            return previousAltKeyDown != altKeyDown;
        }

        private static bool IsAltKeyEvent(Event evt, EventType eventType)
        {
            return (evt.type == eventType || evt.rawType == eventType) &&
                (evt.keyCode == KeyCode.LeftAlt || evt.keyCode == KeyCode.RightAlt);
        }

        private static bool IsReliableModifierEvent(Event evt)
        {
            return evt.type == EventType.KeyDown ||
                evt.type == EventType.KeyUp ||
                evt.type == EventType.MouseDown ||
                evt.type == EventType.MouseUp ||
                evt.type == EventType.MouseMove ||
                evt.type == EventType.MouseDrag ||
                evt.type == EventType.ScrollWheel ||
                evt.rawType == EventType.KeyDown ||
                evt.rawType == EventType.KeyUp ||
                evt.rawType == EventType.MouseDown ||
                evt.rawType == EventType.MouseUp ||
                evt.rawType == EventType.MouseMove ||
                evt.rawType == EventType.MouseDrag ||
                evt.rawType == EventType.ScrollWheel;
        }

        public static void Reset()
        {
            altKeyDown = false;
        }
    }
}
