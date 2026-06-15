using System;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal interface IMeshPreviewBehaviour
    {
        float GetMeshOpacity(SpriteCache sprite);
        float GetWeightMapOpacity(SpriteCache sprite);
        bool DrawWireframe(SpriteCache sprite);
        bool Overlay(SpriteCache sprite);
        bool OverlayWireframe(SpriteCache sprite);
    }
}
