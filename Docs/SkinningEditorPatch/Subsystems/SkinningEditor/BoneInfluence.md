# Bone Influence

## Purpose

Bone Influence and Sprite Influence edit the assigned bone set for character-mode sprite parts.

## Source

- `Editor/SkinningModule/SpriteBoneInfluence/SpriteBoneInfluenceTool.cs`
- `Editor/SkinningModule/SpriteBoneInfluence/BoneSpriteInfluenceTool.cs`
- `Editor/SkinningModule/SpriteBoneInfluence/InfluenceWindow.cs`
- `Editor/SkinningModule/SkinningCache/SpriteCacheExtensions.cs`

## Assigned Bones And Weights

Bone Influence and Sprite Influence use the shared `SkeletonTool` bone picker. `Esc` and primary empty click clear selected bones; right-click does not clear bones.

In Character mode, a sprite part's assigned bones are stored on `CharacterPartCache.bones`. `characterPartChanged` routes through `SkinningCache.CreateSpriteSheetSkeleton(...)`, which updates the sprite mesh compatible bone set.

Adding a bone influence now also calls `SpriteCache.CalculateMissingWeights()` after the character part update. This runs bounded biharmonic weight generation only for vertices whose weights sum to zero, then sorts triangles by depth and emits `meshChanged`. The goal is that assigning bones to an otherwise empty sprite immediately gives Weight Slider visible, editable weights.

The same missing-weight fill is used when Weight Slider auto-associates the selected bone with the current character part before editing.

Removing a bone influence keeps the existing `SmoothFill()` behavior, which fills weight gaps after the compatible bone set removes channels.

## Weight Slider Display

Weight Slider's Vertex Weight list is based on assigned mesh bones, not active weight channels. Assigned bones remain visible even when their current weight on the selected vertex is zero; zero-weight channels can still be filtered out internally.
