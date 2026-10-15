# Skeleton Bone Editing

## Purpose

This page documents Skinning Editor bone creation and dynamic bone drawing.

For the floating **Edit Bone** popup launched from a SpriteRenderer Inspector, see [Scene Bone Editing](SceneBoneEditing.md). It reuses these bone gestures and drawing while staging source Sprite/PSB skeleton changes in a private cache.

## Source

- `Editor/SkinningModule/IMGUI/SkeletonView.cs`
- `Editor/SkinningModule/IMGUI/SkeletonController.cs`
- `Editor/SkinningModule/BoneDrawingUtility.cs`
- `Editor/SkinningModule/UserSettings.cs`
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
5. mouse up creates and selects the new bone, then closes the current create gesture.

If mouse up happens at the same position as mouse down, the controller creates a short minimum-length bone pointing right.

Chained creation still works when starting from a hovered bone tail. The new bone is parented to that tail bone and the parent is marked as chained. Creating a bone does not automatically continue into another chained bone.

`SkeletonMode.CreateBone` does not include joint movement. This keeps drag gestures on existing bones reserved for creating new bones, while simple clicks still select existing bones.

## Bone Inspector

Scene **Edit Bones** also exposes an Icon button and separate seven-column picker for per-bone symbols replacing the entire bone drawing, including directional Arrow/Direction variants. Default retains the original bone drawing. See [Bone Icons](BoneIcons.md) for display and persistence semantics.

Edit Bones shows the bottom bone inspector when one or more bones are selected.

For a single selected bone, the inspector title is `Bone` and exposes name, position, rotation, length, bone color, and depth. `Length` reads and writes the selected bone's world length. Position, rotation, and length update when the selected bone is edited directly in the viewport.

For multiple selected bones, the inspector title is `Bones` and exposes only bulk-safe fields:

- `Rename bones`: renames the selected bones to the entered base name plus a 1-based suffix, e.g. `Arm_1`, `Arm_2`.
- `Bones Color`: applies the selected color to every selected bone.
- `Depth`: applies the selected depth to every selected bone.

Multi-bone rename order follows the selected bones' order in the active skeleton, so repeated renames are deterministic.

## Cursor Feedback

Default bone bodies expose a screen-space selection strip along the bone segment, with a generous 13-point half-width multiplied by the Bone Size preference. Hits inside this strip register zero distance, allowing hover and selection over a SpriteRenderer's native picking area. Endpoint clamping is retained; joint/tail controls keep priority on equal distances. Custom icon bones still omit the invisible body and tail targets.

Bone body dragging uses `MouseCursor.MoveArrow`. Bone ring and tail/end-position handles use `MouseCursor.ScaleArrow`. Hovering any bone part recolors the whole bone fill to amber `#FFB900`, preserving its opacity and dark contour, instead of tinting only the joint/end cap. `BoneDrawingUtility.GetHoveredColor` supplies the shared color for Skinning Editor and native Scene gizmos. The tail hit area is larger than the visual end cap so it is easier to hover and drag.

Zero-length bones only expose the start joint circle in the viewport. Their body and tail hit areas are not laid out, so Edit Bone interactions can move the joint but cannot rotate the bone or change its length from the viewport. Length can still be changed from the Bone inspector field.

## Bone Compositing Order

Skinning Editor and native Scene gizmos draw each bone's outline and fill consecutively in existing skeleton order. Parent links remain behind bones. There is no global all-outlines-then-all-fills pass: a later bone covers both the fill and contour of earlier bones. Scene gizmo batches are flushed between each outline/fill and the next bone to preserve this order. Custom icons draw their own black contour immediately beneath their fill; selected outlines remain behind that contour.

## Bone Drawing and Zoom

`BoneDrawingUtility` draws the body as one solid geometric diamond, from the outer edge of the start joint ring to the exact bone endpoint. The visible ring radius is 9 GUI points times Bone Size (inside the padded 24-point bitmap); both body fill and contour start there, leaving the transparent joint center clear. Bones shorter than this radius show only the joint. This inset changes drawing only, not the bone transform or picking. The shoulder is `visibleBodyLength * 0.1` GUI points from the joint rim; half-width is `min(projectedLength * 0.08, clamp(projectedLength * 0.025, 6 * scale, 10 * scale))` GUI points. Short bones shrink continuously rather than disappearing at a fixed slice-length threshold. The shoulder stays at 10% of the bone length without an early screen-space cap. Width retains its previous appearance at ordinary zoom, then grows gradually from a 6-point to a 10-point half-width (times Bone Size) at high zoom, following the supplied Spine comparisons.

The fill uses a four-vertex world-space mesh and `Hidden/SkinningModule-BoneTexture` with a white texture. Lateral offsets use the projected perpendicular's size, keeping width independent of zoom and Handles transforms. The body tip is the exact bone endpoint and the visible root meets the joint rim, with no transparent bitmap padding or independent end cap to drift from the endpoint. A black antialiased closed line outlines the body; the selection pass draws a wider colored contour. Hover keeps the shared `#FFB900` fill. Joint circles retain `B_Circle` / `B_Circle_Selected`; custom bone icons are unchanged. Legacy embedded body slice textures remain available but are no longer used to render bodies.

The toolbar icon is a separate UI resource:

- light theme: `Editor/Assets/EditorIcons/Light/Create Bones.png`
- dark theme: `Editor/Assets/EditorIcons/Dark/d_Create Bones.png`
- selected: `Editor/Assets/EditorIcons/Selected/Create Bones.png`

`BoneDrawingUtility.DrawBone(...)` draws the geometric body and `DrawBoneNode(...)` draws joint circles. There is no separate end-cap marker or legacy square handle over the body. Preview bones use the same drawing methods as saved bones.

## Bone Display Size

The **Uniskin Rig Binding** component Inspector exposes **Always Show Bones**. This per-instance serialized option defaults to off and persists in scenes/prefabs with Undo and multi-object editing. Enabled active bindings in the current stage contribute their own hierarchy to Scene bone display even without selection, including bound and source-resolved unbound bones. Selection, hierarchy changes, Undo/Redo and Inspector property changes refresh the display. Scene Gizmos and existing Edit Bones/Edit Weights drawing suppression still apply. Preferences has no global Always Show Bones option. Runtime deformation is unaffected.

On 2026-10-15, the connected Unity 6000.6.0f1 Editor recompiled the component option with no Console errors. A read-only diagnostic created the actual custom Inspector for the loaded Hippo binding and verified both its serialized boolean and the PropertyField binding path. Interactive deselection/display, Undo/prefab persistence and Unity 6000.0/6000.5 execution remain unverified for this option. The consuming project uses this checkout as a local file dependency and recompiles directly.

`Preferences > Uniskin > Bone Size` controls the screen-space size of Skinning Editor bones and Sprite Skin Scene view gizmos. The user preference defaults to `1.0` and is clamped to `0.25-2.0`. Existing values from the former `2D > Animation` page are retained.

The scale applies immediately to joint circles, capped body width, and the body picking strip. It does not modify skeleton geometry, serialized bone data, or joint/tail picking areas.

Selected bones use a wider geometric body contour and the selected joint bitmap in the outline pass so selection remains readable against bone colors and mesh overlays.

Unchained parent links are not drawn as ghost bones. `SkeletonController` draws them from the child joint to the parent bone's visual tail marker, and `BoneDrawingUtility.DrawBoneParentLink(...)` renders a colored thick dotted line with a filled triangle arrowhead at the parent tail marker. Links are semi-transparent by default and become fully opaque when their child bone is selected. Create Bone root-parent preview uses the same dotted arrow styling.

Scene view Sprite Skin bone gizmos use `SpriteBone.color` for the normal bone body. Selected scene bones use the configurable `Selected Bone Color` while preserving their gizmo alpha, and their selection outline remains visible. If a serialized sprite bone color has zero alpha, the gizmo falls back to the configured default bone color so older data does not become invisible.

## Bone Colors

Scene bone gizmos additionally collect uniquely resolved unbound bones from the selected character's full source skeleton, including characters whose meshes do not yet have SpriteSkin bindings. Their local lengths are source pixels divided by the source data provider's PPU, matching generated bone Transforms. Do not use runtime Sprite PPU: texture downscaling can reduce it without changing the character hierarchy. Existing bound SpriteSkin gizmos keep their runtime lengths and take precedence, so bones are not drawn twice. The unbound cache refreshes on selection/source reimport, reads live Transforms and respects the Scene View Gizmos toggle. Scene Edit Bone/Weights hosts continue to suppress these native gizmos while their own drawing is active. This display support does not bind bones or modify saved weights.

`Preferences > Uniskin` exposes two user-specific color preferences stored in `EditorPrefs`:

- `Default Bone Color` controls the color assigned to newly created bones and defaults to `#5B729A`. Changing it does not recolor existing bones.
- `Selected Bone Color` controls the fill color of selected Sprite Skin bones in the Scene view and defaults to white.

## Change Risks

- `SkeletonView.IsActionFinishing(SkeletonAction.CreateBone)` controls when a create gesture commits; changing it affects the required mouse gesture.
- `SkeletonController.HandleCreateBone()` owns parent/chained behavior and must keep topology and pose events intact.
- `BoneDrawingUtility` is also used by Sprite Skin bone gizmos, so drawing changes can affect both Skinning Editor previews and scene gizmos.
