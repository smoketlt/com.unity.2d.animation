# Reset Geometry

## Purpose

`Reset` is a Geometry toolbar command that replaces the current sprite mesh with a four-vertex rectangle.

## Source

- command handler: `Editor/SkinningModule/SkinningModuleView.cs`
- toolbar event: `Editor/SkinningModule/UI/MeshToolbar.cs`
- mesh helper: `Editor/SkinningModule/SpriteMeshData/SpriteMeshDataController.cs`
- text constants: `Editor/SkinningModule/TextContent.cs`
- UXML: `Editor/Assets/SkinningModule/MeshToolbar.uxml`

## User-Facing Behavior

- Pressing `Reset` resets only the selected sprite attachment.
- If the mesh has weights, show a confirmation dialog:
  - title: `Attachment weights`
  - message: `This attachment has weights.\n\nPerforming this operation will remove all weights for this attachment.`
  - confirm: `Remove weights`
  - cancel: `Cancel`
- Confirming removes old geometry and weights.
- Cancelling does nothing.

## Implementation Flow

`SkinningModuleView.ResetGeometry()`:

1. reads `skinningCache.selectedSprite`;
2. gets `sprite.GetMesh()`;
3. checks `HasWeights(mesh)`;
4. shows `ConfirmResetWeightedGeometry()` when needed;
5. opens `skinningCache.UndoScope(TextContent.resetGeometry)`;
6. creates a local `SpriteMeshDataController`;
7. assigns `spriteMeshDataController.spriteMeshData = mesh`;
8. calls `mesh.Clear()`;
9. calls `CreateQuad()`;
10. calls `Triangulate(new Triangulator())`;
11. calls `SortTrianglesByDepth()`;
12. clears `skinningCache.vertexSelection`;
13. restores bind pose and clears bone selection;
14. invokes `skinningCache.events.meshChanged.Invoke(mesh)`;
15. requests repaint.

## Why Reset Is Not A Tool

The toolbar label `Reset` is a command, not a mode. It must not leave a selected active tool or checked button. `MeshToolbar.UpdateToggleState()` always sets the Reset button unchecked.

## Change Risks

- If `meshChanged` is not invoked, Unity may not mark the Sprite Editor data dirty.
- If undo scope is missing, reset cannot be undone correctly.
- If weight detection is too broad, users get unnecessary confirmation dialogs.
- If weight detection is too narrow, weighted attachments can lose weights without warning.
