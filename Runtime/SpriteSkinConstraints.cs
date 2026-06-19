using System;
using UnityEngine;

namespace UnityEngine.U2D.Animation
{
    /// <summary>
    /// Type of transform channel driven by a sprite bone constraint.
    /// </summary>
    public enum SpriteBoneConstraintType
    {
        Position,
        Rotation,
        Scale
    }

    /// <summary>
    /// Serializable sprite bone constraint data that can be shared by runtime SpriteSkin instances.
    /// </summary>
    [Serializable]
    public class SpriteBoneConstraint
    {
        public SpriteBoneConstraintType type;
        public string sourceBoneGuid;
        public string drivenBoneGuid;
        [Range(0f, 1f)]
        public float influence = 1f;
        public Vector3 multiplier = Vector3.one;
    }

}
