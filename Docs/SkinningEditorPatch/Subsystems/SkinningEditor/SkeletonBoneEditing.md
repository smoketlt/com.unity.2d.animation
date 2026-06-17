# Skeleton Bone Editing

## Purpose

This page documents Skinning Editor bone creation and dynamic bone drawing.

## Source

- `Editor/SkinningModule/IMGUI/SkeletonView.cs`
- `Editor/SkinningModule/IMGUI/SkeletonController.cs`
- `Editor/SkinningModule/BoneDrawingUtility.cs`
- `Editor/Assets/SkinningModule/BoneToolbar.uxml`
- `Editor/Assets/SkinningModule/BoneToolbarStyle.uss`

## Create Bone Interaction

`Create Bone` uses `Tools.CreateBone` and `SkeletonMode.CreateBone`.

Preview Pose and Bone toolbar modes other than `Create Bone` clear the current bone selection with `Esc` or a primary click in empty space. `Create Bone` keeps its existing empty-space click behavior for creating bones.

The interaction is click-drag-release:

1. clicking empty space creates a short minimum-length bone;
2. clicking an existing bone selects it;
3. clicking and dragging from an existing bone creates a new bone from the clicked point;
4. dragging previews the direction and length;
5. mouse up creates the bone and closes the current create gesture.

If mouse up happens at the same position as mouse down, the controller creates a short minimum-length bone pointing right.

Chained creation still works when starting from a hovered bone tail. The new bone is parented to that tail bone and the parent is marked as chained. Creating a bone does not automatically continue into another chained bone.

`SkeletonMode.CreateBone` does not include joint movement. This keeps drag gestures on existing bones reserved for creating new bones, while simple clicks still select existing bones.

## Cursor Feedback

Bone body dragging uses `MouseCursor.MoveArrow`. Bone ring and tail/end-position handles use `MouseCursor.ScaleArrow`. Hover feedback also distinguishes the target: hovering the joint tints `B_Circle`, and hovering the tail/end-position tints `B_EndCap` with `Handles.preselectionColor`. The tail hit area is larger than the visual end cap so it is easier to hover and drag.

## Bitmap Bone Drawing

Skinning Editor bones are drawn in IMGUI through `BoneDrawingUtility` using embedded bitmap slices.

The current bone shape uses four normal slices and four selected-outline slices:

- `B_Circle` / `B_Circle_Selected` (`24x24`)
- `B_Head` / `B_Head_Selected` (`26x20`)
- `B_Stretch` / `B_Stretch_Selected` (`26x53`)
- `B_EndCap` / `B_EndCap_Selected` (`10x10`)

`B_Head`, `B_Stretch`, and `B_EndCap` are drawn as world-space textured quads with `Hidden/SkinningModule-BoneTexture` instead of long rotated IMGUI texture rects, because Unity can clip very large GUI texture rects while zoomed in. The bone texture shader is separate from `Hidden/SkinningModule-GUITextureClip` so Scene view bone drawing is not clipped by GUI clip masks. Body world positions and widths are derived from the projected screen length, avoiding GUI raycast plane failures in the Sprite Editor view. Body UVs map the bone start to the bottom of each PNG and the bone end to the top. The body slices do not overlap, so semi-transparent bones do not double-blend at the `B_Head` / `B_Stretch` seam. Selected and hovered bones draw selected slices in the outline pass as a thicker underlay, then normal slices are drawn above them; normal unselected bones do not draw an outline pass. When the projected bone length is shorter than the fixed head and end-cap slices, the body is skipped so the joint circle remains as the fallback shape.

The toolbar icon is a separate UI resource:

- light theme: `Editor/Assets/EditorIcons/Light/Create Bones.png`
- dark theme: `Editor/Assets/EditorIcons/Dark/d_Create Bones.png`
- selected: `Editor/Assets/EditorIcons/Selected/Create Bones.png`

`BoneDrawingUtility.DrawBone(...)` draws the sliced bitmap body and `DrawBoneNode(...)` draws joint circles. Tail circles are not drawn over `B_EndCap`, and legacy square IMGUI handle caps are not drawn over bitmap joints. Preview bones use the same drawing methods as saved bones.

Selected bones use the selected bitmap slices in the outline pass so selection remains readable against bone colors and mesh overlays.

Scene view Sprite Skin bone gizmos use `SpriteBone.color` for the normal bone body instead of forcing white. If a serialized sprite bone color has zero alpha, the gizmo falls back to white so older data does not become invisible.

## Change Risks

- `SkeletonView.IsActionFinishing(SkeletonAction.CreateBone)` controls when a create gesture commits; changing it affects the required mouse gesture.
- `SkeletonController.HandleCreateBone()` owns parent/chained behavior and must keep topology and pose events intact.
- `BoneDrawingUtility` is also used by Sprite Skin bone gizmos, so drawing changes can affect both Skinning Editor previews and scene gizmos.
