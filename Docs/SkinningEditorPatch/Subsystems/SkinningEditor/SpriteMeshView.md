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
- Geometry selection is white and hover is yellow by default; both colors and mesh line color are configurable in [Preferences > Uniskin](GeometryEditing.md#uniskin-preferences). Selected/hovered dots share a cached neutral procedural texture tinted while drawing; GUI color is restored afterward. Weight-colored dots retain their existing rendering.
- Weight Slider weighted vertex dots use a smaller viewport radius, with selected dots 20% larger than unselected dots
- `m_CreateEdgeDragActive` gates create-edge behavior while dragging from a vertex in `Create`
- `NewGeometry` action methods gate open-hull click, completion, Esc cancel, and double-click delete behavior

## Action Notes

- `MoveVertex` is inactive in `CreateVertex` mode so vertex drag can create edges.
- `CreateEdge` in `CreateVertex` mode triggers on mouse up, not mouse down.
- With `useSceneHandles`, edge dragging captures a dedicated control rather than a move-slider control. Action checks admit that owned capture; cursor/hover refresh on captured drag/repaint/release even without Layout, keeping the edge preview live. Release/cancel clears only this control. Scene's initial press is consumed by its host after selection runs. Without `useSceneHandles`, the legacy uncaptured workflow remains unchanged.
- `CreateVertex` in `EditGeometry` still requires double-click.
- Scene Edit Mesh accepts vertex-creation clicks and Create edge-drag releases outside the source-image frame, including its maximum edges. The controller clamps preview and committed positions to the frame. New hull creation accepts outside clicks at any distance in Scene; Sprite Editor retains its existing frame/radius checks. Mouse-button, hover and hot-control checks still apply.
- `NewGeometry` creates vertices on empty clicks, completes on first-vertex click, and deletes vertices on double-click.
- Scene Edit Mesh supplies `newGeometryClosureVertex` to also accept the mirrored starting handle while Symmetry is active. Completion uses the fresh `EndLayout()` hover result; other callers keep the first-vertex rule. The Scene session additionally completes a symmetric hull automatically when its authored half returns to the center axis after starting there.
- `NewGeometry` forwards `Delete`/`SoftDelete` commands so the controller can remove selected open-hull vertices.
- `NewGeometry` accepts empty clicks slightly outside the sprite frame and clamps created vertices to the frame.
- `NewGeometry` exits to `Modify` through the controller/tool cancel event when `Esc` is pressed.
- Normal remove and edge movement are disabled in `NewGeometry`.

## Change Risks

- Handle control IDs are shared with Unity IMGUI; incorrect nearest/hot checks can break all mouse interaction.
- `ConsumeMouseMoveEvents()` affects preview responsiveness.
- Avoid direct Alt checks here. Mode switching is handled before this class receives `mode`.
- Keep `NewGeometry` action methods separate from normal `CreateVertex`/`CreateEdge` actions so open hull editing does not triangulate prematurely.
