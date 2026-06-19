# SkinningModule

## Purpose

`SkinningModule` is the Sprite Editor module host. It owns lifecycle, tool activation, toolbar setup, copy/paste and rename keyboard handling, mesh preview rendering, and module data dirty state.

## Source

- `Editor/SkinningModule/SkinningModule.cs`
- `Editor/SkinningModule/SkinningModuleView.cs`
- `Editor/SkinningModule/RenameSelectionWindow.cs`

## Entry Points

- `OnModuleActivate()`
- `OnModuleDeactivate()`
- `DoMainGUI()`
- `DoToolbarGUI(...)`
- shortcut methods in `SkinningModuleView.cs`
- toolbar handlers: `SetMeshTool(...)`, `SetSkeletonTool(...)`, `SetWeightTool(...)`
- new hull handlers: `ToggleNewGeometryTool(...)`, `BeginNewGeometryTool(...)`, `TryCompleteNewGeometry(...)`, `ExitNewGeometryMode()`
- command handler: `ResetGeometry()`
- visibility shortcut command: `ToggleSelectedVisibility()`

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
- handles `F2` rename for the active selected bone, or the selected sprite when no bone is selected;
- handles `H` hide/show selected visibility toggling;
- suppresses base Sprite Editor Alt panning after Skinning Editor input;
- starts and completes `New` hull-authoring mode;
- resets mesh geometry through `ResetGeometry()`;
- invokes `skinningCache.events.meshChanged`;
- marks Sprite Editor data modified through `DataModified()`.

## F2 Rename

`F2` is registered as `ShortcutIds.renameSelection` in the Skinning Editor shortcut context.

When invoked, `SkinningModuleView.ShowRenameSelectionWindow()` chooses the target in this order:

1. `skinningCache.skeletonSelection.activeElement`;
2. the first selected bone in `skinningCache.skeletonSelection.elements`;
3. `skinningCache.selectedSprite`.

`RenameSelectionWindow` opens a modal `Rename` window with a `Name:` text field prefilled with the current target name and `Cancel` / `OK` buttons. `Enter` accepts and `Esc` cancels.

Accepted bone names use `TextContent.boneName` undo and invoke `skinningCache.events.boneNameChanged`. Accepted sprite names use `TextContent.spriteName` undo and mark Sprite Editor data modified. `SkinningModule.ApplyChanges(...)` writes renamed sprites back to `ISpriteEditorDataProvider.SetSpriteRects(...)` before applying bones, mesh, and character data.

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

## New Geometry Mode

`SetMeshTool(Tools.CreateEdge)` is intercepted and routed through `ToggleNewGeometryTool(...)`.

First press:

1. confirms weight removal when needed;
2. activates the `Tools.CreateEdge` wrapper;
3. clears the mesh and vertex selection;
4. invokes `meshChanged`;
5. stays in `NewGeometry` mode.

Second press:

1. requires at least three vertices;
2. adds the closing edge from last vertex to vertex `0`;
3. triangulates and sorts triangles;
4. clears selection;
5. exits to `Modify`.

Selecting another mesh tool while `NewGeometry` is active also completes the hull first when at least three vertices exist. Clicking the first vertex in the view uses the same completion flow through a deferred `MeshTool.newGeometryCompleted` event.

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
- `NewGeometry` also removes weights on entry; keep the confirmation behavior aligned with Reset.
