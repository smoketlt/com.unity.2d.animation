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
| `PoseToolbar.uxml` | Pose toolbar |
| `RigToolbar.uxml` | Copy/paste/visibility toolbar cluster |

## Geometry Toolbar Notes

`MeshToolbar.uxml` still uses the old UXML name `SplitEdge` for the Reset button. C# treats it as `k_ResetGeometryId`.

Do not rename the UXML element unless all C# lookups, USS selectors, and metadata references are checked.

## Panels

- `GenerateGeometryPanel.uxml` / `.uss`
- `GenerateWeightsPanel.uxml` / `.uss`
- `PastePanel.uxml` / `.uss`
- `WeightPainterPanel.uxml` / `.uss`
- `VisibilityToolWindow.uxml`
- `InfluenceWindow.uxml`

## Change Risks

- UI Toolkit queries rely on stable names.
- Tooltips in UXML should match command behavior.
- Localized text generated through code should live in `TextContent.cs`.
