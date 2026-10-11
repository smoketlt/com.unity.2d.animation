using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    // Unity's Sprite ID extension is restricted to its editor friend assemblies.
    internal static class SceneSpriteIdentity
    {
        internal static GUID GetId(Sprite sprite) => sprite.GetSpriteID();
    }
}
