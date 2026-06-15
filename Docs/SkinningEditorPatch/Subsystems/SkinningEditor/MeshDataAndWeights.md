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

## Weight Loss On Reset/New

Resetting geometry clears all vertices and creates new vertices with empty weights. New mesh hull mode clears all vertices and then lets the user define new vertices with empty weights. Both operations intentionally remove existing skinning weights for the attachment.

When weights exist, the user must confirm the operation.

## Change Risks

- Directly replacing vertices without matching `vertexWeights` breaks data invariants.
- Changing indices must update outline edges through `SetIndices(...)`.
- Triangulation can add or reorder geometry; preserve weight behavior when using it.
- `MeshCache.SetBones(...)` fixes weights when the compatible bone set changes.
