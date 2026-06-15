# Skinning Editor Architecture

## Purpose

This page maps the editor-side architecture used by the forked Skinning Editor package.

## Primary Flow

1. Unity opens the Sprite Editor with the Skinning Editor module.
2. `SkinningModule` creates `SkinningCache`, tool caches, preview tools, and toolbar UI.
3. `SkinningModuleView` wires toolbar button events to tool activation or command handlers.
4. `SkinningCache` owns selected sprite, selected tool, mesh/bone selections, undo, and events.
5. Geometry tools route through one shared `MeshTool` instance wrapped by `MeshToolWrapper` variants.
6. `MeshTool` sets up `SpriteMeshController` and `SpriteMeshView`.
7. `SpriteMeshView` identifies active low-level actions from IMGUI events and handle controls.
8. `SpriteMeshController` mutates mesh data through `SpriteMeshDataController`.
9. Mesh mutations invoke `skinningCache.events.meshChanged`, causing `SkinningModule` to mark Sprite Editor data dirty.

## Major Areas

- lifecycle and module host: `SkinningModule.cs`
- UI and shortcuts: `SkinningModuleView.cs`
- tool creation and state: `SkinningCache.cs`
- geometry toolbar: `MeshToolbar.cs`, `MeshToolbar.uxml`
- mesh tool wrapper: `MeshToolWrapper.cs`
- geometry view/controller: `SpriteMeshView.cs`, `SpriteMeshController.cs`
- mesh data: `SpriteMeshData.cs`, `SpriteMeshDataController.cs`, `MeshCache.cs`
- input patch: `SkinningEditorInput.cs`

## Tool Model

`SkinningCache.CreateToolCache(...)` registers tools by `Tools` enum values. Geometry tool entries are wrappers around the same `MeshTool` instance:

- `Tools.EditGeometry` -> `SpriteMeshViewMode.EditGeometry`
- `Tools.CreateVertex` -> `SpriteMeshViewMode.CreateVertex`
- `Tools.CreateEdge` -> `SpriteMeshViewMode.CreateEdge`
- `Tools.SplitEdge` -> `SpriteMeshViewMode.SplitEdge`
- `Tools.GenerateGeometry` -> `GenerateGeometryTool`

The forked toolbar uses user-facing labels:

- `Modify` = `Tools.EditGeometry`
- `Create` = `Tools.CreateVertex`
- `New` = `Tools.CreateEdge`
- `Reset` = command event, not `Tools.SplitEdge`
- `Generate` = `Tools.GenerateGeometry`

## Fork-Specific Architecture

### Shared Alt state

`SkinningEditorInput` owns the shared `Alt` key state. `MeshToolWrapper` reads this state and computes the effective mode. `SpriteMeshView` does not query Alt directly for mode switching.

### Reset command

The `Reset` toolbar button is now a command handled by `SkinningModuleView.ResetGeometry()`. It does not activate a mesh mode.

### Base Sprite Editor Alt panning

`SkinningModule.DisableBaseSpriteEditorAltNavigation()` removes the Alt modifier from the current event after Skinning Editor GUI has used it. This prevents the base Sprite Editor window from treating Alt as pan navigation without forking `com.unity.2d.sprite`.

## Change Risks

- Moving mode-switching logic into `SpriteMeshView` would reintroduce duplicated mode state.
- Making `Reset` a tool again would conflict with the user-facing command semantics.
- Calling mesh mutation APIs without `UndoScope` or `meshChanged` can leave Unity data dirty state incorrect.
- Adding new toolbar buttons requires updating UXML, `MeshToolbar.cs`, `SkinningModuleView.cs`, and docs.
