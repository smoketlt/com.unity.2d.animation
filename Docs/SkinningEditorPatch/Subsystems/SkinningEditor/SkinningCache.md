# SkinningCache

## Purpose

`SkinningCache` is the canonical editor cache for the Skinning Editor module. It owns selected sprite/tool state, selections, tool instances, undo integration, and Skinning Editor events.

## Source

- `Editor/SkinningModule/SkinningCache/SkinningCache.cs`
- related enums: `Editor/SkinningModule/SkinningCache/SkinningEnums.cs`
- events: `Editor/SkinningModule/SkinningCache/SkinningEvents.cs`

## Entry Points

- `Create(...)`
- `CreateToolCache(...)`
- `GetTool(Tools tool)`
- `UndoScope(...)`
- `BeginUndoOperation(...)`
- `RestoreFromPersistentState()`
- `RestoreToolStateFromPersistentState()`
- selected sprite/tool properties

## Inputs

- Unity Sprite Editor data providers;
- persistent Skinning Editor state;
- Sprite cache, skeleton cache, mesh cache, and tool cache objects;
- Unity undo service.

## Outputs

- creates and stores tool instances;
- stores `selectedTool`;
- stores `selectedSprite`;
- exposes `vertexSelection`;
- emits Skinning events such as `meshChanged`;
- registers undo for editor state changes.

## Tool Registry

Geometry tools are registered in `CreateToolCache(...)`:

- `EditGeometry`
- `CreateVertex`
- `CreateEdge`
- `SplitEdge`
- `GenerateGeometry`

The fork keeps `SplitEdge` registered even though the visible `Reset` toolbar button no longer activates it.

## State / Storage

- cached sprite, mesh, skeleton, and character data;
- current selected tool;
- vertex selection;
- persistent state object;
- tool map keyed by `Tools`.

## Dependencies

- `MeshTool`
- `SkeletonTool`
- `MeshToolWrapper`
- `GenerateGeometryTool`
- `CopyTool`
- `GenerateWeightsTool`
- `SkinningEvents`

## Change Risks

- Tool map changes affect shortcuts and toolbar activation.
- Undo registration must cover state mutations that should be reversible.
- Selected-sprite changes clear or remap selection state.
- Removing `SplitEdge` from the registry can break existing shortcut paths even if the toolbar does not expose it.
