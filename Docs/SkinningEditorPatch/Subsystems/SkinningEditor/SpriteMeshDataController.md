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

## Batch Vertex Deletion

`RemoveVertex(IEnumerable<int>)` removes vertices in descending index order. It snapshots triangle-derived outline edges once, then stitches and remaps that working boundary after each removal. The serialized outline remains unchanged until retriangulation, so reading it again during a batch would use stale indices and could create edges into an unrelated mesh island. Single-vertex removal uses the same helper with a fresh boundary snapshot.

`Tests/Editor/GeometryDeletionTests.cs` covers complete island deletion with contiguous and interleaved indices, preserved main-contour triangulation and weights, and adjacent boundary deletion with internal edges.

Verification on 2026-10-02: all three EditMode cases passed on Unity 6000.0.81f1 using a packed checkout snapshot. The Unity 6000.5.7f1 run and a control run against the original controller timed out during initial package resolution/refresh before executing tests. Unity 6000.0.7 and interactive deletion of the reported sprite were not verified.

## Weight Behavior

When triangulation does not add new vertices, it preserves existing weights. When new vertices are added and the mesh had weight data, it can regenerate weights.

## Change Risks

- Calling `spriteMeshData.Clear()` directly without rebuilding vertices and weights can leave tools with no valid mesh.
- `SetIndices(...)` updates outline edges; direct index array mutation should be avoided.
- Weight arrays must match vertex arrays.
- Triangulation fallback can replace geometry with a quad, which may remove custom geometry.
