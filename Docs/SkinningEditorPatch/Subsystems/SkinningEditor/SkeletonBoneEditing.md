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

The interaction is click-drag-release:

1. clicking empty space creates a short minimum-length bone;
2. clicking an existing bone selects it;
3. clicking and dragging from an existing bone creates a new bone from the clicked point;
4. dragging previews the direction and length;
5. mouse up creates the bone and closes the current create gesture.

If mouse up happens at the same position as mouse down, the controller creates a short minimum-length bone pointing right.

Chained creation still works when starting from a hovered bone tail. The new bone is parented to that tail bone and the parent is marked as chained. Creating a bone does not automatically continue into another chained bone.

`SkeletonMode.CreateBone` does not include joint movement. This keeps drag gestures on existing bones reserved for creating new bones, while simple clicks still select existing bones.

## Dynamic Drawing

Skinning Editor bones are drawn dynamically in IMGUI through `BoneDrawingUtility`; the in-scene bone shape is not a PNG asset.

The toolbar icon is a separate UI resource:

- light theme: `Editor/Assets/EditorIcons/Light/Create Bones.png`
- dark theme: `Editor/Assets/EditorIcons/Dark/d_Create Bones.png`
- selected: `Editor/Assets/EditorIcons/Selected/Create Bones.png`

`BoneDrawingUtility.DrawBone(...)` draws the tapered bone body and `DrawBoneNode(...)` draws joint/tail rings. Preview bones use the same drawing methods as saved bones.

## Change Risks

- `SkeletonView.IsActionFinishing(SkeletonAction.CreateBone)` controls when a create gesture commits; changing it affects the required mouse gesture.
- `SkeletonController.HandleCreateBone()` owns parent/chained behavior and must keep topology and pose events intact.
- `BoneDrawingUtility` is also used by Sprite Skin bone gizmos, so drawing changes can affect both Skinning Editor previews and scene gizmos.
