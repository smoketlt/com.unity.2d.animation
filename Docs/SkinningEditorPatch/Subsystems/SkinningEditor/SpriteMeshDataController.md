# SpriteMeshDataController

## Purpose

`SpriteMeshDataController` is the mutation service for sprite mesh data. It creates and removes vertices/edges, triangulates geometry, generates outlines, and manages weights.

## Source

- `Editor/SkinningModule/SpriteMeshData/SpriteMeshDataController.cs`

## Entry Points

- `CreateVertex(...)`
- `CreateEdge(...)`
- `CreateQuad()`
- `RemoveVertex(...)`
- `Triangulate(...)`
- `Subdivide(...)`
- `OutlineFromAlpha(...)`
- `ClearWeights(...)`
- `NormalizeWeights(...)`
- `CalculateWeights(...)`
- `CalculateWeightsSafe(...)`
- `SmoothWeights(...)`
- `SortTrianglesByDepth()`

## Inputs

- `spriteMeshData`;
- `ITriangulator`;
- `IOutlineGenerator`;
- `ITextureDataProvider`;
- `IWeightsGenerator`;
- optional vertex selection.

## Outputs

- mutates `BaseSpriteMeshData` vertices, weights, edges, and indices;
- recalculates triangulation;
- recalculates weights when generated geometry adds new vertices;
- sorts triangle indices by bone depth.

## CreateQuad

`CreateQuad()` creates the default rectangular mesh from `spriteMeshData.frame.size`. This is the canonical helper used by reset and fallback paths.

## Triangulation

`Triangulate(...)` copies current geometry into temporary arrays, runs the triangulator, then rebuilds the mesh. If triangulation fails or returns empty geometry, it clears the mesh, creates a quad, and triangulates again.

## Weight Behavior

When triangulation does not add new vertices, it preserves existing weights. When new vertices are added and the mesh had weight data, it can regenerate weights.

## Change Risks

- Calling `spriteMeshData.Clear()` directly without rebuilding vertices and weights can leave tools with no valid mesh.
- `SetIndices(...)` updates outline edges; direct index array mutation should be avoided.
- Weight arrays must match vertex arrays.
- Triangulation fallback can replace geometry with a quad, which may remove custom geometry.
