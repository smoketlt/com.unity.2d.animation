# MeshToolWrapper

## Purpose

`MeshToolWrapper` adapts the shared `MeshTool` instance to a specific `SpriteMeshViewMode` and optional skeleton mode.

## Source

- `Editor/SkinningModule/MeshTool/MeshToolWrapper.cs`

## Entry Points

- constructor
- `OnActivate()`
- `OnDeactivate()`
- `DoMeshGUI()`
- `GetEffectiveMeshMode()`

## Inputs

- configured `meshMode`;
- shared `MeshTool`;
- shared `SkeletonTool`;
- `SkinningEditorInput.altKeyDown`.

## Outputs

- sets `skeletonTool.mode`;
- sets `meshTool.disable`;
- sets `meshTool.mode` to the effective mesh mode;
- calls `meshTool.DoGUI()`.

## Effective Mode Rule

`GetEffectiveMeshMode()` is the only place that swaps `Modify` and `Create` based on the shared Alt state:

- base `EditGeometry` + Alt -> effective `CreateVertex`
- base `CreateVertex` + Alt -> effective `EditGeometry`
- any other mode + Alt -> unchanged

`NewGeometry` is not affected by Alt.

## Change Risks

- Do not duplicate Alt mode checks in `SpriteMeshView` or `SpriteMeshController`.
- Do not mutate `meshMode` for temporary behavior; only return an effective mode.
- If another temporary modifier is added, keep the effective-mode calculation centralized here.
