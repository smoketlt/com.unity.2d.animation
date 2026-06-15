# SpriteMeshController

## Purpose

`SpriteMeshController` coordinates mesh editing actions and applies data changes through `SpriteMeshDataController`.

## Source

- `Editor/SkinningModule/IMGUI/SpriteMeshController.cs`

## Entry Points

- `OnGUI()`
- `HandleSelectVertex()`
- `HandleMoveVertexAndEdge()`
- `HandleCreateVertex()`
- `HandleCreateEdge()`
- `HandleSplitEdge()`
- `HandleRemoveVertices()`
- `Triangulate()`

## Inputs

- `spriteMeshView`
- `spriteMeshData`
- `selection`
- `cacheUndo`
- `triangulator`
- `frame`

## Outputs

- mutates vertices, edges, weights, and indices;
- opens undo operations for mesh edits;
- clears or updates selection;
- triangulates and sorts triangles.

## Operation Order

`OnGUI()` runs layout, draw previews, then handles mesh operations in a stable order:

1. split edge;
2. create edge;
3. create vertex;
4. select vertex/edge;
5. move vertex/edge;
6. remove.

This order is part of the interaction contract. Changing it can alter whether a drag creates an edge, moves a vertex, or selects something.

## Edge Creation

`CreateEdge(fromVertexIndex, toVertexIndex)`:

- opens undo with `TextContent.createEdge`;
- calls `SpriteMeshDataController.CreateEdge(...)`;
- triangulates;
- clears selection;
- selects the destination vertex;
- increments undo group.

## Change Risks

- Create-edge behavior depends on `selection.activeElement`.
- Remove behavior falls back to a quad if fewer than three vertices would remain.
- Any mesh mutation must preserve triangulation and depth ordering.
