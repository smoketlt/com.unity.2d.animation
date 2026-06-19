using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.U2D.Animation
{
    /// <summary>
    /// Asset containing primitive source-to-driven bone constraints for SpriteSkinConstraintController.
    /// </summary>
    [CreateAssetMenu(fileName = "SpriteSkinConstraintSet", menuName = "2D Animation/Sprite Skin Constraint Set")]
    public class SpriteSkinConstraintSet : ScriptableObject
    {
        [SerializeField]
        List<SpriteBoneConstraint> m_Constraints = new List<SpriteBoneConstraint>();

        public List<SpriteBoneConstraint> constraints => m_Constraints;
    }
}
