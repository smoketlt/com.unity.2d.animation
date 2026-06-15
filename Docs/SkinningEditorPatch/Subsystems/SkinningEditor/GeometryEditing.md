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
| `New` | `Tools.CreateEdge` | `CreateEdge` |
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

## Create Edge From Create Mode

The fork adds `m_CreateEdgeDragActive` in `SpriteMeshView`.

The flag:

1. becomes true when mouse down starts on a vertex in `CreateVertex` mode;
2. allows `CreateEdge` action while dragging;
3. triggers edge creation on mouse up;
4. resets after mouse up, even if no edge was created.

## Selection Clearing

`MeshTool.SetupGUI()` configures `UnselectTool<int>`:

- `clearOnEscape = true`
- `clearOnPrimaryEmptyClick = true`

This is why empty left-click and Esc clear selection.

## Change Risks

- Action priority in `SpriteMeshController.OnGUI()` matters.
- Moving vertex behavior can conflict with edge drag from vertex.
- `CanCreateEdge()` must avoid leaving persistent edge-preview state after mouse up.
- Mesh mutations must call triangulation and mesh-changed events through the owning flow.
