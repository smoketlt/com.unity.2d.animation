# Skinning Module Assets

## Purpose

This page maps UXML/USS/resource files used by the Skinning Editor module.

## Primary Location

- `Editor/Assets/SkinningModule/**`

## Toolbar Assets

| Asset | Purpose |
| --- | --- |
| `MeshToolbar.uxml` | Geometry toolbar buttons |
| `MeshToolbarStyle.uss` | Geometry toolbar styling |
| `BoneToolbar.uxml` | Bone toolbar |
| `WeightToolbar.uxml` | Weight toolbar |
| `ConstraintsToolbar.uxml` | Runtime constraint toolbar |
| `PoseToolbar.uxml` | Pose toolbar |
| `RigToolbar.uxml` | Rig copy/paste toolbar |

## Geometry Toolbar Notes

`MeshToolbar.uxml` still uses the old UXML name `SplitEdge` for the Reset button. C# treats it as `k_ResetGeometryId`.

`MeshToolbar.uxml` also still uses the old UXML name `CreateEdge` for the `New` button. C# still routes it through `Tools.CreateEdge`, but the behavior is new mesh hull creation.

Do not rename the UXML element unless all C# lookups, USS selectors, and metadata references are checked.

## Panels

- `GenerateGeometryPanel.uxml` / `.uss`
- `GenerateWeightsPanel.uxml` / `.uss`
- `PastePanel.uxml` / `.uss`
- `WeightPainterPanel.uxml` / `.uss`
- `AnimationPreviewPanel.uss` styles the persistent bottom timeline created by `AnimationPreviewPanel.cs`.
- `VisibilityToolWindow.uxml`
- `InfluenceWindow.uxml`
- Constraint settings are built in `Editor/SkinningModule/ConstraintsTool.cs` and hosted in the bottom overlay.

## Shaders

- `SkinningModule-GUITextureClip.shader` draws clipped textured editor meshes such as sprite mesh previews.
- `SkinningModule-BoneTexture.shader` draws bitmap bone body slices in both Skinning Editor and Scene view. It does not sample GUI clip masks, because Scene view bone gizmos should not be clipped by editor GUI textures.

## Overlay Placement

Skinning tool panels that are short inspector/control windows are hosted in `LayoutOverlay.bottomOverlay`, appear centered at the bottom by default, and can be dragged by their title-bar area. This includes Weight Painter, Bone Inspector, Generate Geometry, Generate Weights, Paste, Pivot, and Influence panels. Dragged absolute positioning is reset when the owning tool hides the panel, so switching tools shows the panel centered again instead of preserving a stale hidden position.

The Visibility window stays in `LayoutOverlay.rightOverlay` because it is a tall resizable list window. Shared popup sizing and form styles in `Editor/Assets/LayoutOverlay/LayoutOverlayStyle.uss` must cover both right and bottom overlay selectors when a panel can live in either area.

## Weight Painter Panel Notes

`WeightPainterPanel.uxml` contains placeholder containers for the Mode and Bone popups. `WeightPainterPanel.GenerateFromUXML()` creates both popup controls in C#.

The Mode popup is intentionally a `PopupField<string>` backed by `WeightEditorMode` values. Avoid replacing it with `EnumField` unless Git-installed package behavior is re-tested in Unity 6000, because the enum dropdown can fail to open or select values when resolved from an internal editor package assembly.

## Change Risks

- UI Toolkit queries rely on stable names.
- Tooltips in UXML should match command behavior.
- Localized text generated through code should live in `TextContent.cs`.
