# CopyTool

## Purpose

`CopyTool` owns copy/paste workflows for Skinning Editor data, including the fork's geometry mirroring behavior.

## Source

- `Editor/SkinningModule/CopyTool.cs`
- `Editor/Assets/SkinningModule/PastePanel.uxml`
- `Editor/SkinningModule/UI/PastePanel.cs`

## Entry Points

- `OnCopyActivated()`
- paste handlers inside `CopyTool`
- `CopyToolView`
- keyboard routing from `SkinningModule.DoCopyPasteKeyboardEventHandling()`

## Inputs

- selected sprite;
- current mesh from `MeshTool`;
- selected vertices;
- copied sprite data;
- paste panel toggles.

## Outputs

- writes vertices, weights, indices, edges, bones, and sprite data depending on paste mode;
- supports mirrored geometry placement;
- updates mesh and selection state;
- triggers undo and mesh changes through Skinning Editor workflows.

## Mirrored Geometry Notes

The fork includes mirrored paste behaviors:

- paste mirrored geometry to another sprite;
- mirror copied vertex positions into selected target vertices within one UV/sprite;
- preserve ordering by sorting source and target vertex sets before assignment.

## Dependencies

- `MeshTool`
- `MeshCache`
- `SpriteCache`
- `EditableBoneWeight`
- `PastePanel`

## Change Risks

- Copy/paste code spans geometry, weights, bones, and sprite data; make narrow edits.
- Mirrored paste depends on sprite rect dimensions and vertex ordering.
- Invalid selected target indices must be guarded before writing positions.
