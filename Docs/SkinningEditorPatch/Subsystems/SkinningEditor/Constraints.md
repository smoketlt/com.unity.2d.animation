# Constraints

## Purpose

The Constraints workflow adds primitive runtime bone constraints for SpriteSkin rigs.

## Source

- `Editor/SkinningModule/ConstraintsTool.cs`
- `Editor/SkinningModule/UI/ConstraintsToolbar.cs`
- `Editor/Assets/SkinningModule/ConstraintsToolbar.uxml`
- `Editor/SpriteSkin/SpriteSkinConstraintControllerEditor.cs`
- `Runtime/SpriteSkinConstraints.cs`
- `Runtime/SpriteSkinConstraintParent.cs`
- `Runtime/SpriteSkinConstraintSet.cs`
- `Runtime/SpriteSkinConstraintController.cs`

## Editor Workflow

The Skinning Editor has a separate `Constraints` toolbar popup with three entries:

- `Position`
- `Rotation`
- `Scale`

Each entry activates the shared Constraints panel with the selected constraint type. The panel is hosted in the bottom overlay, is built by `ConstraintsTool.cs`, and contains:

- a `SpriteSkinConstraintSet` asset field;
- a `Create` button for making a new constraint set asset;
- `Source` and `Driven` bone dropdowns populated from the effective skeleton, each with a `Pick` button that assigns the first currently selected viewport bone to that field;
- `Influence`, clamped from `0` to `1`, with a slider and numeric value field;
- `Multiplier`, a per-axis multiplier. Dragging the `X`, `Y`, or `Z` axis label horizontally adjusts that axis value; hold `Shift` while dragging for finer increments. A quick click toggles that axis for the `Tune` controls; selected axis labels turn green. The `Tune` `+` and `-` buttons adjust selected axes by `0.1` and immediately update the selected constraint. Holding `Shift` changes the step to `0.01`; holding `Ctrl`/`Cmd` changes it to `0.5`.
- a full-width `Find constraint for selected` button below the list that takes the first currently selected viewport bone and selects the first listed constraint of the active type where that bone is either Source or Driven. Pressing it again while the current constraint still matches that bone searches downward and selects the next matching constraint if one exists.

The working hint for choosing Source/Driven bones and adding/updating constraints is shown in the top `SkinningEditorInfoOverlay` when a Constraints tool is active, not inside the bottom Constraint Settings panel. The panel does not show informational status text at the bottom.

Viewport bone selection does not automatically overwrite the Source or Driven fields; use the field's `Pick` button to assign the selected bone intentionally. Selecting a row in the constraint list selects that constraint's Source and Driven bones in the Skinning Editor viewport, briefly dims the other bones, and flashes the involved bones a couple of times so they are easy to spot in dense rigs. Pressing `Add` or `Update` leaves only the Source bone selected in the viewport after the constraint is written. Constraints are saved into the selected `SpriteSkinConstraintSet` asset, not directly into Sprite Editor mesh or bone importer data.

When a Skinning Editor bone is deleted, constraints in the active constraint set that reference that bone as Source or Driven are deleted in the same undo operation. This prevents constraint assets from keeping bindings to removed bone GUIDs.

After creating a constraint set asset, `ConstraintsTool` synchronously reimports and reloads it as `SpriteSkinConstraintSet`. If Unity fails to bind the asset to the runtime script type, the tool deletes the invalid asset and reports the failure instead of leaving a hidden `m_Script: 0` asset that cannot appear in the object picker.

The three constraint tools must be registered in `SkinningModule.SetupModuleToolGroup()` group `1`; otherwise `ActivateTool(...)` will not deactivate the previous active mode and the toolbar will appear stuck on the previous button.

Constraint preview remains active after leaving the Constraints toolbar mode. `ConstraintsTool` keeps the last assigned `SpriteSkinConstraintSet` as the shared preview set. `SkinningModule.DoMainGUI()` applies shared constraints before drawing `MeshPreviewTool`, so a pose sampled by continuous Animation Preview is constrained before its mesh is rendered. It applies them again after the active tool GUI so bone edits made in the current input event update immediately. Preview bindings are rebuilt when the constraint set/sprite/mode changes, but every Source and Driven baseline is read from `BoneCache.defaultPose`, never from the current preview transforms. Switching between the Position, Rotation, and Scale constraint tools therefore cannot capture an already constrained pose and accumulate offsets. Parameter changes immediately recompute the Driven `BoneCache` from the deterministic default baseline. The preview invokes `skeletonPreviewPoseChanged` so mesh preview updates immediately.

Adding or updating a constraint creates a generated parent bone above the Driven bone inside the Skinning Editor skeleton data. Generated parent bones use GUIDs with the `constraint-parent:` prefix, use a short non-zero length to avoid Skinning Editor chained-child auto-orientation, are excluded from Skinning Editor bone hit-testing and mesh-compatible bone lists, and are drawn as small cross markers instead of normal bones. The generated parent receives the original pose of the Driven bone and becomes the constraint-driven transform. The original Driven bone is reparented under it with a neutral local pose, so local animation is authored on the original bone while the constraint writes to the generated parent. The original Driven bone remains selectable and animatable under the generated parent.

Constraint-parent topology is always created from the skeleton default pose. If a constraint set is assigned, or a constraint is added or updated, while Preview Pose or Animation Preview is active, `EnsureConstraintParentBones()` restores the effective skeleton before creating or reparenting bones. The resulting topology event lets Animation Preview rebind and sample the current frame again. Never call `SetDefaultPose()` for newly generated constraint parents while the skeleton still contains an animated preview pose, because that would permanently promote the animated transforms into the restorable default pose.

Changing the panel's constraint set is wrapped by `constraintSetChangeStarted` and `constraintSetChangeFinished` events. Animation Preview responds by performing its full Stop-and-restore operation before the set is processed, suppressing intermediate topology rebinds, binding the clip again after constraint topology and preview bindings are ready, sampling frame zero, and resuming Play only when it had been playing before the set change.

When applying Sprite Editor changes, bones are serialized in parent-before-child order. This prevents generated constraint parents from being saved after their Driven children and avoids Unity rebuilding saved bone positions or rotations from forward parent references. Mesh vertex weights are remapped from the editor mesh bone list, which excludes generated constraint parents, into the serialized SpriteBone list that includes them.

## Runtime Workflow

Add `SpriteSkinConstraintController` to the animation root and assign the `SpriteSkinConstraintSet` asset. Press the inspector `Update` button to rebuild bindings and apply constraints once immediately. `Update` finds all child `SpriteSkin` components, including inactive children, builds one shared bone GUID-to-transform map from them, and binds every constraint in the assigned asset through that map. The `Restore Pose` button below `Update` restores the captured local position, rotation, and scale of every bound generated constraint parent. It remains available while the component is disabled and records the affected child transforms for editor Undo. The legacy serialized `Sprite Skin` override remains available through the runtime API for backward compatibility, but is hidden from the Inspector because the normal root-controller workflow does not require it. Existing components with that override assigned remain limited to the assigned `SpriteSkin`.

The controller does not create scene hierarchy parent objects. It expects the generated `constraint-parent:` bones saved by the Skinning Editor to exist in the Sprite/SpriteSkin bone hierarchy. For each constraint, Source binds to the original source bone transform and Driven binds to the generated parent bone transform for that Driven GUID. If the generated parent is present in the transform hierarchy but is missing from `SpriteSkin.boneTransforms`, the controller falls back to the original Driven transform's direct parent when that parent name ends with ` Constraint`. It never falls back to writing the original Driven child bone directly. This makes the final pose `constraint parent * driven local animation`, so a Driven bone can be animated locally while still following its Source.

The custom inspector also listens for editor Undo/Redo and reapplies the already-bound constraints without rebuilding bindings.

Disabling the component immediately restores the captured channels. Re-enabling and explicit `Update` rebinding carry forward baselines for unchanged constraint/source/driven triples instead of replacing them with an animated or previously constrained pose. After an assembly reload, `OnDisable` has already restored the hierarchy before new bindings are captured.

At runtime and in edit-mode animation preview, the controller:

1. finds either the assigned `SpriteSkin` or all child `SpriteSkin` components;
2. reads each active Sprite's `SpriteBone` GUIDs;
3. builds one shared GUID-to-transform map from each `SpriteSkin.boneTransforms`;
4. finds the generated `constraint-parent:` transform for each Driven GUID;
5. binds every constraint Source against the original bone map and every Driven target against its generated constraint parent;
6. captures the source and constraint parent local transforms when binding;
7. applies source deltas to constraint parents in `LateUpdate` before SpriteSkin deformation update order;
8. also applies after Unity samples animation properties in the Animation Window through `OnDidApplyAnimationProperties()`.

Position constraints apply:

`constraintParent.localPosition = parentBindPosition + ConvertVector(sourceParent, constraintParentParent, (source.localPosition - sourceBindPosition) * influence * multiplier)`

The Driven baseline is stored in local space. The Source delta is converted through world-vector space into the Driven parent's local space before it is added. This keeps constrained bones attached to the Character hierarchy when its root is translated, rotated, or scaled instead of treating the bind position as a fixed world-space anchor.

Rotation constraints apply per-axis local Euler deltas from the source bind rotation to the current source rotation with `Mathf.DeltaAngle`, then multiply that delta onto the constraint parent's bind local rotation.

Scale constraints apply:

`constraintParent.localScale = parentBindScale + (source.localScale - sourceBindScale) * influence * multiplier`

## Limitations

- Constraints are asset-driven runtime data; Unity's Sprite Editor data providers do not store these custom constraints in SpriteBone data.
- The runtime controller can be placed on an animation root for all child `SpriteSkin` components, or on a single `SpriteSkin` GameObject when the `Sprite Skin` field is assigned.
- Generated constraint parent bones change prefab transform paths after SpriteSkin regenerates its bone hierarchy and should be created before authoring animation clips.
- Multiple constraints can share one generated Driven parent. Multiple constraints writing the same channel on the same parent are applied in list order.

## Change Risks

- The runtime controller binds by SpriteBone GUID, so edits that regenerate bone GUIDs can break existing constraint sets.
- Changing evaluation order relative to `SpriteSkin` can make constraints visible one frame late.
- Adding importer-level constraint persistence would require a separate custom data provider or asset workflow and should not be mixed into mesh or bone data silently.
