using UnityEngine;

namespace UnityEngine.U2D.Animation
{
    /// <summary>Persistent contour geometry and weights, borrowing an Uniskin character skeleton.</summary>
    public sealed class UniskinClippingMaskData : ScriptableObject
    {
        public UniskinRig rig;
        public Sprite source;
        [HideInInspector] public Sprite sprite;
        [HideInInspector] public string authoringData;
        [HideInInspector] public string sourceDependencyHash;
        [HideInInspector] public Matrix4x4 rigToContour = Matrix4x4.identity;
        [HideInInspector] public int revision;
        [HideInInspector] public bool hasGeometry;
    }
}
