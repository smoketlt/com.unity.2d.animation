# MeshToolbar

## Purpose

`MeshToolbar` binds the Geometry toolbar UXML buttons to Skinning Editor tool activation and command events.

## Source

- `Editor/SkinningModule/UI/MeshToolbar.cs`
- `Editor/Assets/SkinningModule/MeshToolbar.uxml`
- `Editor/Assets/SkinningModule/MeshToolbarStyle.uss`

## User-Facing Buttons

| Label | UXML name | Behavior |
| --- | --- | --- |
| `Modify` | `SelectGeometry` | activates `Tools.EditGeometry` |
| `Create` | `CreateVertex` | activates `Tools.CreateVertex` |
| `New` | `CreateEdge` | toggles `Tools.CreateEdge`, which now enters/completes `NewGeometry` hull mode |
| `Reset` | `SplitEdge` | invokes `ResetGeometry` command |
| `Generate` | `GenerateGeometry` | activates `Tools.GenerateGeometry` |

The `Reset` button still uses UXML name `SplitEdge` for asset compatibility, but the C# constant is named `k_ResetGeometryId`.

## Entry Points

- `GenerateFromUXML()`
- `BindElements()`
- `UpdateToggleState()`
- `AddShortcutsToToolTips()`

## Events

- `SetMeshTool(Tools mode)` for mode buttons.
- `ResetGeometry()` for the Reset command.

## Checked State

`UpdateToggleState()` reads active tool state from `SkinningCache`.

For `Modify` and `Create`, checked state is swapped when `SkinningEditorInput.altKeyDown` is true:

- active `Modify` + Alt -> `Create` appears checked;
- active `Create` + Alt -> `Modify` appears checked.

`Reset` is a command button and is never checked.

## Shortcut Notes

`Reset` does not append the old `Split Edge` shortcut to its tooltip. The old `SplitEdge` tool/shortcut still exists in the codebase, but the Geometry toolbar button no longer activates it.

`New` still uses UXML name `CreateEdge` and `Tools.CreateEdge` for compatibility, but its user-facing behavior is new mesh hull creation.

When `New` is clicked, the button blurs itself after dispatching `SetMeshTool(Tools.CreateEdge)`. This prevents the focused toolbar button from swallowing `Esc`; keyboard input should return to the editor view so `Esc` can cancel New mode.

## Change Risks

- Renaming UXML element names breaks `Q<Button>(...)` lookups.
- Adding a new button requires UXML, C# binding, checked-state logic, and docs updates.
- Do not route command buttons through `SetMeshTool(...)`; commands should expose their own event.
