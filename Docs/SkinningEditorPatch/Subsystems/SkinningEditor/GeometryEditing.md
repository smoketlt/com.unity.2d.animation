# Geometry Editing

## Purpose

This page documents the Geometry editing workflow: selection, vertex creation, edge creation, drag behavior, and reset/generate routing.

## Source

- `Editor/SkinningModule/MeshTool/MeshTool.cs`
- `Editor/SkinningModule/MeshTool/MeshToolWrapper.cs`
- `Editor/SkinningModule/IMGUI/SpriteMeshView.cs`
- `Editor/SkinningModule/IMGUI/SpriteMeshController.cs`
- `Editor/SkinningModule/IMGUI/UnselectTool.cs`
- `Editor/SkinningModule/Selectors/**`

## Current Modes

| User label | Tool | Effective `SpriteMeshViewMode` |
| --- | --- | --- |
| `Modify` | `Tools.EditGeometry` | `EditGeometry` |
| `Create` | `Tools.CreateVertex` | `CreateVertex` |
| `New` | `Tools.CreateEdge` | `NewGeometry` |
| `Reset` | command | not a mode |
| `Generate` | `Tools.GenerateGeometry` | auto geometry panel/tool |

## Interaction Model

`SpriteMeshView` owns hit testing and action activation:

- hovered vertex;
- hovered edge;
- nearest edge;
- vertex hit radius;
- create-vertex preview;
- create-edge preview;
- split-edge preview;
- move hot controls.

`SpriteMeshController` owns actual mesh mutations:

- select vertex/edge;
- move vertex/edge;
- create vertex;
- create edge;
- split edge;
- remove vertices/edges;
- triangulate and sort triangles by depth.

## Forked Interaction Behavior

- Vertex dot rendering is larger than upstream.
- Vertex hit radius is larger than the visible dot.
- `Esc` clears geometry selection.
- Right click clears geometry selection through `UnselectTool`.
- Primary empty click clears geometry selection.
- In `Create`, dragging from a vertex starts edge creation.
- In `Create`, vertex moving is disabled so drag from a vertex is not stolen by move behavior.
- Edge-drag state is reset on mouse up so the tool returns to plain `Create`.
- In `New`, the current mesh is cleared and clicks define a new open hull.
- In `New`, vertices can be dragged without triangulating the open hull.
- In `New`, double-clicking a vertex deletes it.
- In `New`, clicking the first vertex with three or more vertices closes and triangulates the hull.
- Pressing `New` again, or selecting another mesh tool, also closes and triangulates the hull when at least three vertices exist.

## Create Edge From Create Mode

The fork adds `m_CreateEdgeDragActive` in `SpriteMeshView`.

The flag:

1. becomes true when mouse down starts on a vertex in `CreateVertex` mode;
2. allows `CreateEdge` action while dragging;
3. triggers edge creation on mouse up;
4. resets after mouse up, even if no edge was created.

## New Mesh Hull Mode

`New` uses `SpriteMeshViewMode.NewGeometry`.

Entering the mode clears the current mesh, clears vertex selection, and leaves an empty open hull. Each empty click adds a vertex. Each vertex after the first also adds an edge from the previous vertex to the new vertex. The mesh is not triangulated while the hull is open.

The mode is completed by clicking the first vertex or pressing `New` again. Selecting another mesh tool also completes a valid hull before switching tools. Completion adds the closing edge from the last vertex to vertex `0`, triangulates the mesh, sorts triangles by depth, clears selection, and exits to `Modify` when completion was requested through the first vertex or `New`.

If the previous mesh had weights, entry uses the same attachment-weights confirmation dialog as Reset because clearing the mesh removes those weights.

## Selection Clearing

`MeshTool.SetupGUI()` configures `UnselectTool<int>`:

- `clearOnEscape = true`
- `clearOnPrimaryEmptyClick = true`

This is why empty left-click and Esc clear selection.

## Change Risks

- Action priority in `SpriteMeshController.OnGUI()` matters.
- Moving vertex behavior can conflict with edge drag from vertex.
- `CanCreateEdge()` must avoid leaving persistent edge-preview state after mouse up.
- Open hull edits must not call normal triangulation until completion; triangulation fallback creates a quad when fewer than three valid vertices exist.
- Mesh mutations must call triangulation and mesh-changed events through the owning flow.
