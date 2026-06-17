# WeightPainterTool

## Purpose

`WeightPainterTool` owns the Weight Slider and Weight Brush workflows for editing existing skinning weights.

## Source

- `Editor/SkinningModule/WeightPainterTool.cs`
- `Editor/SkinningModule/WeightPainterToolWrapper.cs`
- `Editor/SkinningModule/UI/WeightPainterPanel.cs`
- `Editor/Assets/SkinningModule/WeightPainterPanel.uxml`
- `Editor/Assets/SkinningModule/WeightPainterPanelStyle.uss`

## Mode Popup

The `Mode` row in `WeightPainterPanel.uxml` is a placeholder container. `WeightPainterPanel.GenerateFromUXML()` creates the actual mode popup in C#.

The mode popup uses an explicit `PopupField<string>` backed by `WeightEditorMode` values instead of `EnumField`. This keeps Weight Slider and Weight Brush mode selection stable when the package is installed from a Git dependency, where UI Toolkit enum menu resolution for internal editor assemblies can differ from a local development checkout.

## Modes

- `AddAndSubtract`: add or remove weight for the selected bone.
- `GrowAndShrink`: grow or shrink existing weight channels without creating missing channels.
- `Smooth`: smooth selected or brush-covered vertex weights.

The core difference between `AddAndSubtract` and `GrowAndShrink` is channel creation. `AddAndSubtract` can add the selected bone to vertices that do not already have that bone weight, then normalizes/compensates other channels. `GrowAndShrink` only changes vertices where the selected bone already has a weight channel; vertices without that channel are skipped instead of gaining a new influence.

## Bone Selection Clearing

All Weight toolbar modes use `SkeletonTool` for bone picking. Weight Slider, Weight Brush, Auto Weights, Bone Influence, and Sprite Influence enable `SkeletonTool` bone unselection so `Esc` and primary empty click clear the selected bones through `UnselectTool<BoneCache>`. Right-click does not clear selected bones.

For tools that also run `MeshTool`, bone unselection treats the mesh default control as an empty-space target so clicks in the sprite mesh area can clear bones when no bone is hit.

## Weight Slider Smooth Drag

Weight Slider has custom vertex-drag behavior in addition to the panel slider:

- `AddAndSubtract` and `GrowAndShrink` drag behavior transfers weight between the selected skeleton bones and the other active channels on the selected mesh vertices.
- `Smooth` drag behavior stores the selected vertex weights when the drag starts. Dragging up blends from those stored weights toward a bone-distance target distribution; dragging down reduces that blend and can return all weights to the stored drag-start values. With no selected bones, the target distribution uses all bones in the current mesh. With more than one selected bone, the target distribution uses only those selected bones that are present in the mesh; any previous weight on unselected bones is faded out as the vertex is redistributed across the selected set.
- A single selected bone does not start Smooth drag redistribution because there is no bone set to smooth between.

## Weight Slider Smooth Button

Weight Slider also exposes a `Smooth` button under the `Amount` slider. Each click applies one conservative neighborhood smoothing pass modeled after Spine's Weights view:

- If vertices are selected, only those vertices are rewritten. If no vertices are selected, all mesh vertices are smoothed.
- If two or more bones are selected, only those selected bones that belong to the current mesh are smoothed. If zero or one bone is selected, all mesh bones are smoothed.
- Neighbor vertices come from both authored mesh edges and triangulated outline edges, matching Spine's hull/internal-edge neighborhood concept as closely as the Unity mesh data allows.
- The operation snapshots all weights before the pass, so every target vertex averages the same pre-click neighbor state rather than being affected by vertices already processed earlier in the pass.
- Neighbor values use inverse-edge-length weighting, so close connected vertices influence the result more than long edges.
- A click blends partway from the current weight to the neighbor average instead of replacing the current value outright. This keeps the result stable and makes repeated clicks the intended way to increase smoothing strength.
- Smoothed channels are clamped back to Unity's four-weight `BoneWeight` limit and normalized after the pass.

## Vertex Weight List

In Weight Slider mode, the Vertex Weight list displays every bone assigned to the current sprite mesh. Rows are not based on the currently enabled channels of the selected vertex, so assigned bones remain visible even when their current weight is `0`.

Weight Slider viewport vertices draw as compact weighted color pies. Unselected weighted vertices are 30% smaller than the previous fork size, while selected weighted vertices draw 20% larger than unselected weighted vertices. The Weight Slider vertex hit radius remains larger than the visible dot so selection and drag targeting stay forgiving.

Weight Brush uses the same weighted color pie vertex display as Weight Slider, so brush-painted vertices show their current weight distribution directly in the viewport. Brush-covered vertices are tracked internally for painting but do not replace the persistent viewport vertex selection.

When Weight Brush has no selected vertices, all weighted vertex pies draw at full opacity for readability. Once one or more vertices are selected, unselected vertex pies return to the lower opacity used by Weight Slider so the persistent selection remains visually distinct.

Weight Brush shows the same assigned-bone weight list used by Weight Slider. The brush controls are labeled `Strength`, `Size`, and `Feather`, in that order. `Strength` is the per-stroke weight amount, `Size` is the outer brush radius, and `Feather` controls the edge falloff band.

The Weight Brush viewport gizmo draws two wire circles plus a strength arc: an outer circle for the full brush area, a grey inner circle for the start of the feather band, and a reddish-orange outer arc showing `Strength`. The outer brush-area circle uses the selected weight bone's weight-map color and is hidden when no valid weight bone is selected. The strength arc starts at the top of the brush and fills clockwise; `100%` draws a full ring and low values draw only a short start segment. Vertices inside the grey circle receive full `Strength`; vertices between the grey and outer circles receive linearly faded strength toward the edge.

When `Strength`, `Size`, or `Feather` is changed from the panel, Weight Brush temporarily previews the same gizmo at the center of the current viewport so the user can judge the edited brush settings without moving the mouse into the canvas.

Weight Brush also supports viewport parameter hotkeys: hold `S` and move the mouse left/right or hold `Ctrl` and scroll to decrease/increase `Strength`, hold `B` and move left/right or hold `Shift` and scroll to decrease/increase `Size`, and hold `F` and move left/right or hold `Shift` + `Ctrl` and scroll to decrease/increase `Feather`. These viewport hotkeys update the panel values without showing the temporary center-viewport preview; the live brush under the cursor remains visible instead.

When Weight Slider or Weight Brush is activated, `SkinningEditorInfoOverlay` shows a short top hint. The Weight Brush hint covers bone target selection, `Alt` vertex selection, brush parameter hotkeys, temporary Smooth, and subtract painting.

In Weight Brush mode, `Alt` + primary click toggles the nearest viewport vertex in the persistent vertex selection, and `Alt` + primary drag rectangle-selects viewport vertices. If one or more vertices are selected, brush strokes only affect selected vertices covered by the brush circle; with no selected vertices, brush strokes affect every vertex covered by the brush circle.

Holding `Shift` while using Weight Brush temporarily edits in `Smooth` mode and the panel's `Mode` popup displays `Smooth` while the key is held. Releasing `Shift` returns the brush to the panel's previously selected mode; if `Shift` changes during an active stroke, the current edit segment is closed and a new segment starts with the effective mode.

In Weight Brush `Smooth`, if more than one mesh bone is selected, smoothing only changes those selected bone channels and preserves the other channels on each affected vertex. If zero or one mesh bone is selected, smoothing uses all mesh bone channels.

Weight Brush keeps the brush gizmo visible and paintable while hovering bones. A primary click with no `Alt`, `Ctrl`/`Command`, or `Shift` modifier on a bone selects that bone, but holding the primary button and dragging from a bone starts a brush stroke instead of selecting the bone.

Each row shows a small color swatch button tinted with the bone's bind-pose color, a standard text bone-name button, followed by a weight slider and numeric field for that bone. The swatch uses an embedded white 16x16 rectangle PNG as the tint mask, and locked rows draw a separate embedded 16x16 lock PNG over it. The bone-name button uses standard label text color; selected bones show a dark-grey row background.

Clicking a bone name selects that mesh bone in the Skeleton selection; clicking with Shift, Ctrl, or Command toggles that bone in the current multi-selection.

Clicking the color swatch toggles a per-Weight-Slider lock for that bone. A locked bone shows a lock icon in the swatch. Locked bone weights are preserved by direct row edits, Weight Slider drag edits, Smooth, and Prune.

When the assigned bone list is longer than the visible row budget, the bone weight rows scroll vertically inside the Vertex Weight list instead of expanding past the Weight Slider panel.

Changing a row's weight rebuilds the selected vertex weights across assigned bones: the edited bone receives the requested value, and the remaining weight is distributed across the other assigned bones. If the other assigned bones currently have zero weight, the remaining weight is distributed evenly among them.

Weight Slider edits always normalize vertex weights back to a total of `1.0`. The old `Normalize` toggle is intentionally hidden from the UI because normalized weights are the normal safe path for deformation.

## Prune

The Weight Slider panel exposes a `Prune` button next to `Smooth`. It opens a small utility window with live preview:

- `Bones` sets the maximum number of bones that may keep weight on each target vertex.
- `Threshold` removes unlocked weights below the threshold.
- The preview text shows how many weight channels will be removed.
- Removed unlocked weight is redistributed to remaining unlocked weights on the same vertex.
- Locked bone weights are never removed or redistributed.
- If vertices are selected, Prune targets those vertices. If no vertices are selected, it targets all vertices.

## Change Risks

- The same `WeightPainterPanel` instance is reused by Weight Slider and Weight Brush.
- The selected mode is read by `WeightPainterTool.SetupWeightEditor()` before edits begin.
- Smooth mode hides the bone popup and changes the Weight Slider amount range.
