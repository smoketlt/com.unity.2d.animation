# Mesh Data And Weights

## Purpose

This page documents the mesh data model used by Skinning Editor geometry, including vertices, edges, indices, outline edges, and bone weights.

## Source

- `Editor/SkinningModule/SpriteMeshData/SpriteMeshData.cs`
- `Editor/SkinningModule/SpriteMeshData/SpriteMeshDataController.cs`
- `Editor/SkinningModule/SkinningCache/MeshCache.cs`
- `Editor/SkinningModule/SpriteMeshData/EditableBoneWeight.cs`
- `Editor/SkinningModule/SpriteMeshData/EditableBoneWeightUtility.cs`

## Data Model

`BaseSpriteMeshData` stores:

- `Vector2[] vertices`
- `EditableBoneWeight[] vertexWeights`
- `int[] indices`
- `int2[] edges`
- `int2[] outlineEdges`
- temporary vertex-position override for pose preview

`MeshCache` extends `BaseSpriteMeshData` for a concrete `SpriteCache`.

## Geometry Mutation

Use `SpriteMeshDataController` for geometry operations:

- `CreateVertex(...)`
- `CreateEdge(...)`
- `CreateQuad()`
- `RemoveVertex(...)`
- `Triangulate(...)`
- `SortTrianglesByDepth()`
- `ClearWeights(...)`
- `NormalizeWeights(...)`
- `CalculateWeights(...)`

## Quad Geometry

`CreateQuad()` creates four vertices from `spriteMeshData.frame.size`:

- bottom-left
- bottom-right
- top-left
- top-right

Then it creates the perimeter edges and triangulation can produce the final indices.

## Weight Detection

Use `EditableBoneWeight.Sum()` to detect whether a mesh has meaningful weights. A vertex weight array can exist even when all weights are empty, so array length is not enough.

The Reset and New commands treat a mesh as weighted when any vertex weight has `Sum() > 0f`.

## Weight Slider Vertex Display

In Weight Slider mode, mesh vertices replace the regular cyan/yellow handles with large circular weight pies. The pie diameter is twice the previous regular vertex size. Each enabled weight channel contributes a colored slice using the bound bone's `bindPoseColor`; duplicate channels for the same bone are merged before drawing. Slices are ordered by bone name and then bone index so colors do not flip while weights change. Slice angles use absolute weight share up to `1.0`; any unassigned remainder is black. Vertices with no enabled weight draw as a black circle.

Selected Weight Slider vertices draw at full opacity; unselected vertices draw at 50% opacity. Clicking and dragging from a vertex edits the currently selected bone influences on the selected vertices: dragging up transfers weight from other active channels into the selected bone channels, and dragging down transfers weight from selected bone channels into other active channels. If no selected bone maps to the current mesh influences, or if there is no opposite active channel to receive/give weight, dragging leaves weights unchanged.

## Weight Loss On Reset/New

Resetting geometry clears all vertices and creates new vertices with empty weights. New mesh hull mode clears all vertices and then lets the user define new vertices with empty weights. Both operations intentionally remove existing skinning weights for the attachment.

When weights exist, the user must confirm the operation.

## Change Risks

- Directly replacing vertices without matching `vertexWeights` breaks data invariants.
- Changing indices must update outline edges through `SetIndices(...)`.
- Triangulation can add or reorder geometry; preserve weight behavior when using it.
- `MeshCache.SetBones(...)` fixes weights when the compatible bone set changes.
