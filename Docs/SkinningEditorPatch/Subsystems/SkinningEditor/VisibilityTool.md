# Visibility Tool

## Purpose

The Visibility tool owns the popup opened from the horizontal Visibility toolbar button. It exposes Bone and Sprite visibility lists, plus preview opacity controls.

## Source

- `Editor/SkinningModule/VisibilityTool/VisibilityTool.cs`
- `Editor/SkinningModule/MeshPreviewTool/MeshPreviewBehaviour.cs`
- `Editor/SkinningModule/UserSettings.cs`
- `Editor/Assets/SkinningModule/VisibilityToolWindow.uxml`
- `Editor/Assets/SkinningModule/VisibilityTool.uss`

## Opacity Sliders

The popup has three persisted opacity sliders:

- Bone opacity: stored in `VisibilityToolSettings.boneOpacity`; applied by skeleton drawing styles.
- Weight opacity: stored in `VisibilityToolSettings.meshOpacity`; applied to the selected sprite weight map when the Visibility tool shows weight preview.
- Sprite opacity: stored in `VisibilityToolSettings.spriteOpacity`; when a sprite is selected, the selected sprite stays fully opaque and all other sprites render at this opacity while the Visibility tool preview is active.

`VisibilityToolController` binds slider values and drag begin/end events. Visibility tool activation sets `skinningCache.events.meshPreviewBehaviourChange` to the Visibility preview behavior, and deactivation clears it. Mesh and Sprite opacity drag events refresh the same preview behavior instead of clearing it, so opacity remains applied after releasing the mouse while the Visibility popup stays active.

The weight opacity value becomes `_VertexColorBlend` in `SkinningModule-GUITextureClip.shader`. Blend only texture RGB with the interpolated vertex-weight RGB; preserve the texture/tint alpha, then apply GUI clipping and sprite opacity. Weight vertex colors have alpha 1, so interpolating the complete RGBA color incorrectly raises transparent texel coverage to the slider value. At 50%, hidden atlas padding RGB becomes visible as colored blocks; at 100%, the entire mesh (including transparent sprite regions) becomes opaque weight color. RGB-only blending keeps the sprite silhouette and partial-alpha edges unchanged at 0%, 50%, and 100%. Alpha-channel preview continues to ignore weight opacity.

GPU verification on 2026-10-02 in Unity 6000.0.81f1 compared the old and corrected shader using texels with alpha 0, 0.25, and 1. The old RGB path returned approximately 0.5, 0.625, and 1 at 50% weight opacity, and 1 for every texel at 100%. The corrected shader preserved approximately 0, 0.25, and 1 at all three slider values in both RGB and alpha-channel mode. Shader compilation and all six corrected render checks passed. The same shader source is used in Unity 6000.5 or newer; the corrected weight-blend path has not yet been rendered there.

## Hide/Show Selected Shortcut

`H` is registered as `2D/Animation/Hide Show Selected` in `SkinningModuleView.cs`.

When invoked, it first toggles the selected bones. If any selected bone is visible, all selected bones are hidden; otherwise all selected bones are shown. Bone visibility changes run inside `TextContent.visibilityChange` undo and call `skinningCache.BoneVisibilityChanged()` so persistent editor state is updated.

If no bones are selected, `H` toggles the currently selected sprite's `CharacterPartCache.isVisible` in Character mode. Sprite visibility uses the existing `TextContent.spriteVisibility` undo path and the `CharacterPartCache.isVisible` setter updates persistent sprite visibility state.

## Preview Behavior

`MeshPreviewBehaviour.GetMeshOpacity` dims unselected sprites only when `dimUnselectedSprites` is enabled and `skinningCache.selectedSprite` is not null. The actual opacity comes from `unselectedSpriteOpacity`.

`MeshToolWrapper` still sets `unselectedSpriteOpacity` to `0.1f` for `NewGeometry` so the New tool keeps its fixed dimming behavior. Visibility tool activation sets it from `VisibilityToolSettings.spriteOpacity`.

Visibility is a parallel horizontal tool, so its top informational hint is registered below the active primary mode's hint. Opening Visibility while Weight Brush or another primary mode is active does not replace that mode's shortcut instructions. Closing Visibility removes only the Visibility-owned hint instead of hiding the primary hint.

## Change Risks

- Do not change UXML names without updating `VisibilityToolWindow.BindElements` and USS selectors.
- Keep `NewGeometry` dimming fixed unless that workflow is intentionally changed.
- Sprite opacity should not dim anything when no selected sprite exists.
