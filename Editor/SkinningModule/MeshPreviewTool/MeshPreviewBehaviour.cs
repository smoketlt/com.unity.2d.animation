using System;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal class DefaultPreviewBehaviour : IMeshPreviewBehaviour
    {
        public float GetMeshOpacity(SpriteCache sprite)
        {
            return 1f;
        }

        public float GetWeightMapOpacity(SpriteCache sprite)
        {
            return 0f;
        }

        public bool DrawWireframe(SpriteCache sprite)
        {
            return false;
        }

        public bool Overlay(SpriteCache sprite)
        {
            return false;
        }

        public bool OverlayWireframe(SpriteCache sprite)
        {
            return sprite.IsVisible() && sprite.skinningCache.selectedSprite == sprite;
        }
    }

    internal class MeshPreviewBehaviour : IMeshPreviewBehaviour
    {
        public bool showWeightMap { get; set; }
        public bool drawWireframe { get; set; }
        public bool overlaySelected { get; set; }
        public bool dimUnselectedSprites { get; set; }
        public float unselectedSpriteOpacity { get; set; } = 0.1f;

        public float GetMeshOpacity(SpriteCache sprite)
        {
            SkinningCache skinningCache = sprite.skinningCache;
            if (dimUnselectedSprites && skinningCache.selectedSprite != null && skinningCache.selectedSprite != sprite)
                return unselectedSpriteOpacity;

            return 1f;
        }

        public float GetWeightMapOpacity(SpriteCache sprite)
        {
            SkinningCache skinningCache = sprite.skinningCache;

            if (showWeightMap)
            {
                if (skinningCache.selectedSprite == sprite || skinningCache.selectedSprite == null)
                    return VisibilityToolSettings.meshOpacity;
            }

            return 0f;
        }

        public bool DrawWireframe(SpriteCache sprite)
        {
            SkinningCache skinningCache = sprite.skinningCache;

            if (drawWireframe)
                return skinningCache.selectedSprite == null;

            return false;
        }

        public bool Overlay(SpriteCache sprite)
        {
            SkinningCache skinningCache = sprite.skinningCache;

            if (overlaySelected && skinningCache.selectedSprite == sprite)
                return true;

            return false;
        }

        public bool OverlayWireframe(SpriteCache sprite)
        {
            return sprite.IsVisible() && sprite.skinningCache.selectedSprite == sprite;
        }
    }
}
