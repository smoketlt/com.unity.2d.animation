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

            if (evt.alt)
                altKeyDown = true;

            if ((evt.type == EventType.KeyUp || evt.rawType == EventType.KeyUp) &&
                (evt.keyCode == KeyCode.LeftAlt || evt.keyCode == KeyCode.RightAlt))
                altKeyDown = false;

            return previousAltKeyDown != altKeyDown;
        }

        public static void Reset()
        {
            altKeyDown = false;
        }
    }
}
