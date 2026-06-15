# SkinningModule

## Purpose

`SkinningModule` is the Sprite Editor module host. It owns lifecycle, tool activation, toolbar setup, copy/paste keyboard handling, mesh preview rendering, and module data dirty state.

## Source

- `Editor/SkinningModule/SkinningModule.cs`
- `Editor/SkinningModule/SkinningModuleView.cs`

## Entry Points

- `OnModuleActivate()`
- `OnModuleDeactivate()`
- `DoMainGUI()`
- `DoToolbarGUI(...)`
- shortcut methods in `SkinningModuleView.cs`
- toolbar handlers: `SetMeshTool(...)`, `SetSkeletonTool(...)`, `SetWeightTool(...)`
- command handler: `ResetGeometry()`

## Inputs

- `SpriteEditorModuleBase.spriteEditor`
- `SkinningCache`
- `Event.current`
- toolbar events from `MeshToolbar`, `BoneToolbar`, `WeightToolbar`, `PoseToolbar`, `RigToolbar`
- selected sprite and mesh from `SkinningCache`

## Outputs

- activates/deactivates cached tools;
- updates toolbar checked state;
- handles copy/paste keyboard commands;
- suppresses base Sprite Editor Alt panning after Skinning Editor input;
- resets mesh geometry through `ResetGeometry()`;
- invokes `skinningCache.events.meshChanged`;
- marks Sprite Editor data modified through `DataModified()`.

## State / Storage

- `m_SkinningCache`
- `m_MeshPreviewTool`
- `m_LayoutOverlay`
- toolbar instances
- `m_HasUnsavedChanges`
- current and previous tool through `skinningCache.selectedTool`

## Reset Geometry Command

`ResetGeometry()`:

1. reads `skinningCache.selectedSprite`;
2. gets its `MeshCache`;
3. checks current weights with `HasWeights(...)`;
4. shows `EditorUtility.DisplayDialog(...)` if weights exist;
5. opens `skinningCache.UndoScope(TextContent.resetGeometry)`;
6. clears the mesh;
7. calls `SpriteMeshDataController.CreateQuad()`;
8. triangulates and sorts triangles by depth;
9. clears vertex selection and bone selection;
10. invokes `meshChanged`;
11. requests repaint.

## Dependencies

- `SkinningCache`
- `MeshToolbar`
- `SpriteMeshDataController`
- `Triangulator`
- `MeshCache`
- `EditableBoneWeightUtility.Sum(...)`
- `EditorUtility.DisplayDialog(...)`

## Change Risks

- Toolbar command handlers must not forget undo and `meshChanged`.
- `DoMainGUI()` order matters: Skinning Editor input must be processed before tool GUI, while base Alt navigation suppression happens after Skinning Editor UI has had a chance to consume Alt.
- `ResetGeometry()` removes weights by replacing all vertices; callers must confirm when weights exist.
