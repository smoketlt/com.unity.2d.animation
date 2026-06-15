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
            bool currentAltKeyDown = evt.alt;

            if ((evt.type == EventType.KeyDown || evt.rawType == EventType.KeyDown) &&
                (evt.keyCode == KeyCode.LeftAlt || evt.keyCode == KeyCode.RightAlt))
                currentAltKeyDown = true;

            if ((evt.type == EventType.KeyUp || evt.rawType == EventType.KeyUp) &&
                (evt.keyCode == KeyCode.LeftAlt || evt.keyCode == KeyCode.RightAlt))
                currentAltKeyDown = false;

            altKeyDown = currentAltKeyDown;

            return previousAltKeyDown != altKeyDown;
        }

        public static void Reset()
        {
            altKeyDown = false;
        }
    }
}
