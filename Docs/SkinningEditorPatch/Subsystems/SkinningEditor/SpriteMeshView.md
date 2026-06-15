# SpriteMeshView

## Purpose

`SpriteMeshView` is the low-level IMGUI view for sprite mesh geometry. It computes hover state, action availability, action triggers, and drawing.

## Source

- `Editor/SkinningModule/IMGUI/SpriteMeshView.cs`
- interface: `Editor/SkinningModule/IMGUI/ISpriteMeshView.cs`

## Entry Points

- `BeginLayout()`
- `LayoutVertex(...)`
- `LayoutEdge(...)`
- `EndLayout()`
- `DoCreateVertex()`
- `DoCreateEdge()`
- `DoSplitEdge()`
- `DoSelectVertex(...)`
- `DoSelectEdge(...)`
- `DoMoveVertex(...)`
- `DoMoveEdge(...)`
- `IsActionActive(...)`
- `IsActionTriggered(...)`
- drawing methods

## Inputs

- `mode`
- `selection`
- `frame`
- `defaultControlID`
- `IGUIWrapper`
- Unity handle control state

## Outputs

- hovered vertex/edge values;
- active action decisions;
- event consumption;
- GUI changed/repaint requests;
- preview drawing for vertices and edges.

## Fork-Specific State

- `kVertexHitRadius = 16f`
- `kNewGeometryFrameHitRadius = 16f`
- visible vertex dot styles are larger than upstream
- `m_CreateEdgeDragActive` gates create-edge behavior while dragging from a vertex in `Create`
- `NewGeometry` action methods gate open-hull click, completion, Esc cancel, and double-click delete behavior

## Action Notes

- `MoveVertex` is inactive in `CreateVertex` mode so vertex drag can create edges.
- `CreateEdge` in `CreateVertex` mode triggers on mouse up, not mouse down.
- `CreateVertex` in `EditGeometry` still requires double-click.
- `NewGeometry` creates vertices on empty clicks, completes on first-vertex click, and deletes vertices on double-click.
- `NewGeometry` accepts empty clicks slightly outside the sprite frame and clamps created vertices to the frame.
- `NewGeometry` exits to `Modify` through the controller/tool cancel event when `Esc` is pressed.
- Normal remove and edge movement are disabled in `NewGeometry`.

## Change Risks

- Handle control IDs are shared with Unity IMGUI; incorrect nearest/hot checks can break all mouse interaction.
- `ConsumeMouseMoveEvents()` affects preview responsiveness.
- Avoid direct Alt checks here. Mode switching is handled before this class receives `mode`.
- Keep `NewGeometry` action methods separate from normal `CreateVertex`/`CreateEdge` actions so open hull editing does not triangulate prematurely.
