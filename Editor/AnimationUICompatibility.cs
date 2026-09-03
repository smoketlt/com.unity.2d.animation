using UnityEditor.U2D.Common;
using UnityEngine.U2D.Common;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    internal static class AnimationUICompatibility
    {
        internal static void SetAnimationChecked(this Button button, bool value)
        {
#if UNITY_6000_3_OR_NEWER
            button.SetCheckedPseudoState(value);
#else
            button.SetChecked(value);
#endif
        }

        internal static bool IsAnimationChecked(this Button button)
        {
#if UNITY_6000_3_OR_NEWER
            return button.hasCheckedPseudoState;
#else
            return button.IsChecked();
#endif
        }
    }
}
