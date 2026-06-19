# Animation Preview

## Purpose

Animation Preview plays an `AnimationClip` directly in the Skinning Editor so mesh deformation can be inspected while geometry and weights are edited.

## Source

- `Editor/SkinningModule/AnimationPreviewController.cs`
- `Editor/SkinningModule/UI/AnimationPreviewPanel.cs`
- `Editor/Assets/SkinningModule/AnimationPreviewPanel.uss`
- `Editor/SkinningModule/SkinningModule.cs`
- `Editor/SkinningModule/SkinningModuleView.cs`

## Timeline

The timeline is a persistent horizontal strip at the bottom of the Skinning Editor. It contains:

- an `AnimationClip` object field;
- first, previous, next, and last frame buttons;
- `Play` / `Pause`;
- `Stop`, which restores the pose that was active before the clip was selected;
- a frame slider and numeric frame field;
- a `Loop` toggle;
- a matched-bone status such as `12/15 bones`.

Dragging the slider or editing the frame field samples an exact clip frame. Playback uses editor time and samples continuously, while the displayed frame follows the clip frame rate.

## Bone Binding

`AnimationPreviewController` reads Transform curves through `AnimationUtility.GetCurveBindings(...)`. It supports:

- local position;
- quaternion rotation;
- raw, baked, or standard local Euler rotation;
- local scale.

Clip binding paths are matched to Skinning Editor bone hierarchy paths. Exact paths are preferred, followed by root-relative and suffix matches so clips whose Animator root adds an outer hierarchy prefix can still bind. The status label reports how many bones received at least one supported Transform curve.

AnimationClip positions are stored in Unity units, while Skinning Editor bone positions use sprite pixels. Position samples are converted using the Sprite Editor data provider's pixels-per-unit value. Root bones in Sprite Sheet mode also include the selected sprite pivot offset used by the importer conversion.

## Preview Semantics

- Animation sampling changes only temporary `BoneCache` transforms.
- Sampling does not edit bind pose, mesh geometry, or saved Sprite importer data.
- Sampling does not create an Undo operation on every frame.
- Weight Slider and Weight Brush continue editing saved vertex weights while the mesh is deformed at the sampled pose.
- Mesh preview updates through `skeletonPreviewPoseChanged` and the existing `MeshPreviewCache` skinning path.
- `Pause` keeps the sampled pose visible.
- `Stop`, clip replacement, Sprite selection changes, Skinning mode changes, and module deactivation restore the pose captured before preview began.
- Skeleton topology changes rebind the selected clip against the current bones.

## Current Scope

Animation Preview intentionally ignores non-Transform AnimationClip bindings, including Sprite Swap, component properties, animation events, and root-motion processing. Constraints continue to use the existing Skinning Editor constraint preview pass after the sampled bone pose is applied.

## Change Risks

- Bone names and hierarchy paths must remain compatible with the clip. Renamed or structurally different bones will be reported as unmatched.
- Position conversion depends on the source Sprite import pixels-per-unit value.
- Do not route sampled poses through bind-pose mutation APIs or mark Sprite Editor data modified.
- Always restore the captured transform snapshot when disposing or rebinding the preview controller.
