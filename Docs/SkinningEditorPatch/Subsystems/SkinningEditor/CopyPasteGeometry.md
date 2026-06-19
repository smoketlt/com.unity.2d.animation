# Copy/Paste Geometry

## Purpose

`CopyTool` handles Skinning Editor copy/paste workflows for geometry, bones, weights, sprite data, and fork-specific mirrored vertex paste behavior.

## Source

- `Editor/SkinningModule/CopyTool.cs`
- `Editor/SkinningModule/UI/PastePanel.cs`
- `Editor/Assets/SkinningModule/PastePanel.uxml`

## Entry Points

- keyboard handling in `SkinningModule.DoCopyPasteKeyboardEventHandling()`
- `CopyTool.OnCopyActivated()`
- paste panel actions
- selected vertex paste helpers inside `CopyTool`

## Fork-Specific Behavior

- Copying vertices stores their placement.
- `Ctrl+V` can paste copied vertex placement to another sprite or selected target vertices, depending on the current workflow.
- `Ctrl+Shift+V` performs mirrored paste.
- Selected-vertex mirrored paste works within one UV/sprite by mapping copied vertices onto the currently selected opposite-side vertices.
- When bones are selected, `SkinningModule` intercepts `Ctrl+C`, `Ctrl+V`, and `Ctrl+Shift+V` before `CopyTool`: it copies world bone transforms, pastes them onto the current selected bones, and mirrors horizontally for `Ctrl+Shift+V`.

## Data Notes

Copy/paste uses mesh data from `MeshTool.mesh`:

- `vertices`
- `vertexWeights`
- `indices`
- `edges`
- sprite rect and pixels-per-unit context

Mirroring uses sprite rect dimensions when converting x/y coordinates.

Selected-bone transform copy/paste stores only transform data, not topology:

- world position
- world rotation
- world length
- depth

Bone transform mirroring uses the character bounds in Character mode and the selected sprite rect in Sprite Sheet mode.

## Change Risks

- Selected-target paste assumes selected target indices are valid for the active mesh.
- Mirroring order matters; copied and target vertices must be sorted consistently.
- Weight/bone copy workflows share code with geometry copy workflows, so narrow changes carefully.
