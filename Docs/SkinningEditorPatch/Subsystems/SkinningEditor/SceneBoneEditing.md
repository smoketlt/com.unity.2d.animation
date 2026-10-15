# Scene View Bone Editing

## Entry and scope

An enabled [UniskinRig](UniskinRig.md) stores Scene bone saves independently of the PSB importer. New saved bones are created/bound in loaded rig instances immediately and can be weighted without PSB reimport. The source-provider semantics below are retained by the adapter; explicit Bake or Skinning Editor entry synchronizes the native PSB.

The SpriteRenderer Inspector has an explicit **Edit Bones** button beneath Create/Edit Mesh. A character root or bone uses its GameObject Inspector header entry described below. There is no Window menu entry. Selection alone never activates editing. The popup shares Edit Mesh's dark Unimator styling, draggable header, red close tile, supplied icons and automatic content height.

The popup title and Inspector entries read **Edit Bones** (or **Edit Bones — Skeleton** for the skeleton-only popup). Its native title icon and the visible header both use `SceneMeshIcons/EditBone.png`, with a 16-pixel header icon scaled for editor DPI.

The Inspector entry uses separate Image and Label children in a centered horizontal row, matching Edit Mesh. Button.text remains empty so the button's own text does not overlap the icon.

A single scene bone bound through `SpriteSkin.boneTransforms` also has an **Edit Bones** button in the GameObject Inspector header, above Transform. It uses Unity's `Editor.finishedDefaultHeaderGUI` extension so the native GameObject and Transform inspectors remain intact. The button resolves the bound SpriteRenderer in the same prefab instance/hierarchy and maps bound bone indices to imported GUIDs; unbound character bones use the unique hierarchy fallback described below. It opens the existing popup with that source bone selected and its Color and Length ready to edit. Selecting the bone alone does not dirty the session or reimport the source. The header entry is also available for a character container/root and uniquely identifiable unbound character bones. It remains hidden for unrelated objects, SpriteRenderer objects (which have their own entry), persistent prefab assets and multi-object inspection, and disabled during Play Mode. Both the bone header and SpriteRenderer Inspector use `SceneMeshIcons/EditBone.png`.

For PSB prefab instances, Scene bone coordinates use the instance root and the imported layer placement rather than the layer's scene Transform override. Moving one SpriteRenderer layer must not translate or rotate the shared character skeleton. Ordinary sprites and character renderers outside their source prefab retain the renderer-based mapping.

The same native GameObject header extension also adds the [UniskinRig mode/bake block](UniskinRig.md#storage-and-activation) to a scene character root with one unambiguous PSB source. It remains available when the character has no bones yet. Individual bones retain only their Edit Bones entry; a root containing several PSB sources has no shared rig controls.

On entry, the session matches source bone GUIDs to SpriteSkin bindings in this character instance and captures their current scene pose in source-pixel coordinates. Existing posed bones stay aligned with the scene. The private cache contains this displayed pose; no scene Transform is changed. A serialized per-bone offset converts edited positions, rotations and lengths back to source space at save time. Existing scene rotations are not baked into the shared asset. Unchanged bones retain their original source poses, while new children and split segments inherit the parent's conversion. New root bones use the character plane. The initial pose conversion remains available until the popup closes; subsequent scene pose changes are handled by [live animation editing](#editing-during-scene-animation).

This host edits the **source Sprite/PSB skeleton**, not scene Transform objects. Ordinary Sprites use their own sprite skeleton. PSB character assets use their complete shared character skeleton, with matching per-layer skeletons and influence mappings. A shared external skeleton provider is read-only and must be edited at its source. Package identity and runtime component types remain unchanged.

## Controls and gestures

### Bone chain clipboard

The second command row provides icon buttons **Copy**, **Paste** and **Paste Mirror**.
Copy snapshots selected bones and all their descendants, including branches; overlapping
selections include each bone once. Paste creates new GUIDs and preserves the copied
hierarchy, chained endpoints, color, depth and icon settings. Original bones and their
weights are unchanged. New bones follow the mesh-entry/skeleton-only influence policy.
Pasted bones become selected and the entire insertion supports staged Undo/Redo and
the normal automatic save on close.

Each copied chain retains its original external parent by GUID. Positions are captured
relative to that parent's joint and rotation; paste uses the parent's current frame.
Paste Mirror reflects all chain joints and endpoints along the parent's longitudinal
local X axis (negates local Y). A parentless chain, or a chain whose external parent is
unavailable in the destination skeleton, uses the skeleton origin and X axis instead.
It does not reparent to the currently selected bone.

Mirroring swaps `Left`/`Right` (including lowercase and uppercase variants) and isolated
`L`/`R` markers, such as `Leg_R` → `Leg_L`, in both directions. Existing destination
names receive a numeric suffix rather than replacing an existing bone. The clipboard
is an in-memory snapshot shared by Edit Bones popups until assembly reload; subsequent
source edits/deletions do not mutate it. It does not copy mesh weights or animation clips.

Verification on 2026-10-16: the connected Editor compiled without errors. A disposable
live probe passed descendant copying, rotated-parent reflection, mirrored names,
parent/chained-child links, Undo, ordinary paste, unique names and Sprite skeleton save;
the updated UXML cloned with all three clipboard buttons. Temporary assets and probe
source were removed. NUnit regression cases were added, but the consuming project's
Test Runner discovered zero package tests, so those cases were not executed as a suite.
Interactive layout/DPI and animated-pose pasting remain manual checks; exact Unity
6000.0.7 and 6000.5+ execution was not performed. No version-sensitive API was introduced.

The properties area includes a current-icon button opening the separate Spine-style [Bone Icons](BoneIcons.md) picker. Icon selection supports multiple bones and Undo, and saves by GUID to native importer or rig metadata.

The selected GameObject's native transform handles are automatically hidden while Edit Bones is available, including after choosing Move/Rotate/Scale again. Custom bone handles remain available. The shared [Scene object tool scope](SceneMeshEditing.md#scene-object-tool-handles) restores the previous visibility when editing closes, reloads or becomes blocked; manually switching to View is unnecessary.

### Editing during Scene animation

Open **Edit Bones** from the SpriteRenderer Inspector for mesh entry, or from the character root/bone Inspector for skeleton-only entry. Play or scrub the animation driving that character's scene Transforms in Edit Mode. The displayed bones and their hit targets follow the current scene pose while the popup remains open. Keep the entry selection: changing Unity's object selection retains the existing automatic-save-and-close behavior.

Bone gestures and property controls remain available at the displayed pose. Edits stay staged in source-skeleton coordinates and are projected into subsequent frames; a completed Alt redraw immediately saves and refreshes the session as described below. New children and split segments inherit their parent's current conversion. Saving converts the edits back to the source Sprite/PSB skeleton; animation poses are not baked into that skeleton, and scene Transforms and clip curves are unchanged by pose refresh.

`SceneEditBoneWindow.UpdateScenePose` refreshes at 30 Hz through `EditorApplication.update`, and `OnSceneGUI` refreshes before handle layout. `SceneBoneSession.RefreshScenePose` reads bound SpriteSkin Transforms and uniquely resolved unbound character Transforms. It captures authored positions, rotations and lengths through the previous conversion before projecting them through the new conversion. Exact source/displayed pose pairs prevent round-trip drift, and source-space snapshots ensure that playback alone never creates pending changes. Refresh does not create Undo records; an authored edit records the matching pose conversion with the session, so Undo can restore it after the animation advances.

Bindings are rebuilt when cached scene Transforms are destroyed/replaced. Domain reload closes the popup; reopening captures fresh bindings. Captured local bone lengths retain the original conversion basis across source saves. Play Mode retains automatic save and close on entry; the editing workflow above uses animation preview in Edit Mode. The Skinning Editor's own clip timeline is documented separately in [Animation Preview](AnimationPreview.md), and opening Skinning Editor for the same source asset still blocks Scene editing.

### Mesh entry versus skeleton-only entry

Opening from a SpriteRenderer keeps the existing mesh workflow: new bones join the selected mesh and any character parts influenced by their parent. Positive existing weights are retained; first-bone saves can still initialize missing weights. Opening from the shared character root or a bone's GameObject header uses **Edit Bones — Skeleton**: new bones are added only to the character skeleton, without extending any part's binding list or initializing zero weights on save. Binding and weight authoring are subsequently explicit in Edit Weights. The entry policy is serialized with the session; switching policies closes the existing session through automatic save on close rather than silently changing its behavior. Editing/deleting existing weighted bones still updates affected data normally.

Root/unbound resolution searches the enclosing prefab/root for one character source. Bound bones retain direct SpriteSkin GUID mapping. For an enabled rig, shared `SceneBoneInspector.FindCharacterTransform` first uses the stored GUID-to-Transform bindings within the requested host, including a binding on an ancestor. This keeps unweighted bones visible and their Edit Bones button available when source and scene parent chains differ. Multiple matching rig Transforms are rejected. If no eligible rig binding matches, the complete source parent-name chain must match uniquely; importer numeric name suffixes are accepted, while ambiguous paths or multiple source assets are rejected. Lookup filters remain effective, including Bake reconciliation's exclusion of the authored Transform when locating a regenerated native duplicate. A character container can open without selecting a specific bone. Unbound character transforms are also included in initial captured scene-pose mapping when uniquely resolved, so drawing does not fall back to an unrelated layer position. No new scene skeleton storage or importer override is introduced: PSD Importer's character-mode `CreateBonesGO` already generates all saved character bones, including unbound ones. Ordinary Sprite bone headers retain their existing per-sprite workflow; skeleton-only entry requires a shared character source.

| Control | Behavior |
| --- | --- |
| No active command | Existing Edit Joints interaction: select bones, move their bodies/joints, and adjust endpoints. Ctrl/Cmd adds/removes bones from selection. |
| Create | Reuse Skinning Editor click/drag/release creation, including chained creation from an existing tail and normal bone selection on a simple bone click. |
| Alt + primary drag with one bone selected | Temporarily redraw the selected bone: press at its new start, drag to its new endpoint, then release the mouse while holding Alt. Preserve its GUID, name, color, depth, parent, children, influence slots and all vertex weights. |
| Split | Click a bone body to divide it at the cursor; repeat to create more segments. The original bone retains its GUID and existing weights. New segments initially have no weights. |
| Set Parent | Select the child, then click a parent bone. If no child is selected on entry, the first bone click selects it. Clicking empty space detaches it to the skeleton root. Preserve world position and rotation; reject self-parenting, descendant-parenting and service constraint parents. A successful operation returns to normal editing. |
| Color | Set one or several selected bones' serialized color; mixed colors are indicated. |
| Name | Rename one selected bone after Enter/focus loss. Trim surrounding whitespace; reject empty names, slashes, line breaks and duplicate source bone names. Changes remain staged until Save. |
| Length | Set a single selected bone's length, in original source pixels; clamp to zero and reject non-finite input. |
| Active command again | Return to normal editing. |
| Esc | Cancel the current gesture, return to normal editing and clear bone selection. |
| Empty primary click | Clear selection outside Create; in Set Parent with a selected child, detach instead. |
| Delete/Backspace | Delete selected bones, preserve surviving child positions and update affected influences/weights. Staged Undo/Redo remains available. |
| Right/middle mouse, or Alt without exactly one editable selected bone | Retain Scene navigation. |

Property fields refresh after viewport changes and Undo without replacing unfinished text/numeric input. Name and Length are disabled for multiple selections; Color supports bulk editing. There is no extra Modify or Delete button.

For enabled Uniskin rigs, saving or automatically closing after Set Parent preserves the final displayed pose, including movement performed after reparenting. The binding snapshots the old scene pose and old/new source world matrices before updating the hierarchy, then applies the authored delta through each bone's scene-pose conversion. It does not replace scene locals with bind-pose locals or discard explicit movement. This matters when the old/new parents have scene pose overrides. Unweighted character bones first resolve through the rig's stable GUID-to-Transform map, before source-name-chain fallback, so a stale scene hierarchy does not make their initial display lose its scene-pose conversion. See [UniskinRig](UniskinRig.md#scene-and-runtime-bindings) for runtime prefab synchronization and verification limits.

When Set Parent changes an inherited prefab bone, saving unpacks the affected scene prefab instance with Undo before applying the new parent. Unity otherwise refuses the hierarchy change. The same bone objects and references survive; Undo restores the prefab connection and previous hierarchy. Matching Sprite revisions alone no longer suppress repair of stale scene parents. See [scene hierarchy synchronization](UniskinRig.md#scene-hierarchy-synchronization-and-prefab-instances) for the exact unpacking policy and prefab-link implications.

### Redrawing a selected bone with Alt

Select exactly one bone in either mesh-entry or skeleton-only **Edit Bones**, then hold Alt and draw its new start/end with the primary mouse button. This temporarily overrides the active command; the Create tile highlights while Alt is held and the previous command resumes on release. Starting over another bone still redraws the selected bone. The preview leaves the skeleton untouched until mouse-up; a click without a drag produces the same short right-facing minimum bone as Create. Esc, releasing Alt before mouse-up, leaving the Scene window, Undo/Redo, changing commands or closing the popup cancels the preview. Right/middle mouse navigation remains available.

Mouse-up automatically saves the current bone session and runs the same source/scene synchronization as closing the window, while retaining the popup, selection and active command. Other pending bone edits in that session are saved together. Enabled rigs refresh their generated Sprite bind poses and loaded scene bone Transforms; native sources use their normal importer save/reimport. The animation pivot therefore updates immediately, without closing Edit Bones. Existing position tracks are retargeted as described below so subsequent animation cannot write the old joint coordinates back.

`SceneEditBoneWindow` captures the gesture with its Scene control and uses shared `SkinningEditorInput` Alt tracking. `SceneBoneSession.RecreateBone` rewrites the existing bone pose, emits bind-pose and mesh events and preserves child world poses. Parenting stays intact; a chained endpoint marker is cleared when the redrawn endpoints no longer meet. It does not remove/create a bone or change the influence arrays. `RecreateBoneAndSave` commits that edit, recaptures source/displayed pose offsets and scene bone lengths from the refreshed source, and updates the source fingerprint. The existing cache objects are retained. Redraw, source/scene save and recapture collapse into one Undo step; recapture has its own cache Undo scope because the save path advances Unity's Undo group. This prevents Redo from restoring the old pivot conversion. Subsequent animation and repeated redraws use the new baseline, avoiding double application of the movement.

Saving edits to existing bones preserves zero-weight vertices as well as positive weights. Automatic initialization of missing weights in mesh-entry sessions runs only when new bone identities join that mesh; skeleton-only sessions continue to skip initialization entirely.

### Animation position tracks after redraw

Animation clips store absolute local Transform positions. Source save and SpriteSkin refresh alone cannot move a joint with keyed position channels: starting animation restores the old key values. `SceneBoneAnimationPositionPlan` captures matching scene Transforms by source asset/GUID before the redraw save, and shifts their existing `m_LocalPosition.x/y/z` curves afterwards. It includes the redrawn bone and its direct bone children, whose local coordinates can change when their world poses are preserved.

The plan discovers clips from ancestor Animator controllers (including effective override clips) and legacy Animation components in loaded instances. Paths must resolve to the captured bone Transform. Constant or nearly constant tracks, including recording noise within `0.00001` Unity units, are anchored to the new local joint position. Moving tracks receive the local position delta, preserving their motion. Key times, tangents, weights and curve wrapping are retained; rotation, scale, other bone tracks, object curves and events are unaffected. Repeated redraws shift from the latest state. A pending rename uses the resulting Transform path after save.

Clip edits participate in the same single Undo/Redo group as the source, scene Transforms and pose recapture. A position clip must be an editable `.anim` asset; incompatible offsets for the same binding in a shared clip reject the operation rather than applying two different changes. The feature updates existing used clips in place, without scanning unused animation assets or adding position channels that were absent.

Name refresh tracks the displayed bone GUID and model name. The 200 ms UI poll does not write the same model value back into the delayed TextField, so pending input such as `bone_32` cannot be reset to `bone_3` even when UI Toolkit reports focus on the field itself. Selection changes and model changes (including Undo/Redo) refresh the displayed name. Length's focus guard accepts both the field and its focused descendants.

The follow-up input fix and `NamePollingPreservesPendingInputAndRefreshesChangedModelOrSelection` regression compiled with the full editor assembly and Scene Bone fixture against Unity 6000.0.81f1 references. The test simulates repeated polls while `bone_32` is pending, then checks model/selection refresh. It was not executed in Unity; typing and Enter/focus-loss confirmation still require an interactive check.

### Renaming scene bones and animation bindings

Save compares source bone names with staged names by stable GUID. `SceneBoneRenamePlan` captures matching bone Transforms in loaded scene instances before source reimport, using SpriteSkin GUID bindings and the existing unique character-path fallback for unbound bones. It renames their GameObjects and records prefab instance name overrides before prefab regeneration. Revert and staged Undo do not modify GameObjects or clips.

For every affected bone, animation hosts are all ancestor GameObjects with an Animator controller or legacy Animation component, including the character root. The plan reads all clips used by those hosts, including effective AnimatorOverrideController clips and inactive scene objects. It rewrites exact full binding paths for the renamed bone and its descendants, including several renamed ancestors in one save. Numeric curves and object-reference curves retain their values; unrelated bindings remain unchanged. Clips are marked dirty and saved, and scene object/clip changes record Undo. Source importer undo/reimport remains subject to Unity's existing importer lifecycle.

Validation runs before importer writes. Sibling name collisions, ambiguous hierarchy paths, destination curve collisions, incompatible paths in a clip shared by affected roots, and non-editable/imported embedded clips block Save with an explanation. Embedded clips must first become editable `.anim` assets. Shared clip assets are edited in place, so all controller users of that asset see the new paths; the feature does not duplicate clips or scan arbitrary unused animation assets. Scene transforms in unloaded scenes are not inspected. Imported PSB prefab bones still use the saved source names, while loaded scene instances receive explicit matching name overrides. No scene bone GameObjects are created by the rename plan.

## Staging, saving and safety

The private `SceneBoneSession` owns a hidden `SkinningCache` and implements its persistent state independently of Sprite Editor preferences. Undo scopes cover skeleton changes and emit the existing topology/bind-pose/mesh events. In mesh-entry sessions, character topology changes assign new bones to the selected part and parts that influence their parent; skeleton-only sessions preserve those binding lists. Both rebuild sprite-sheet skeletons from the resulting influence lists. Name/color/pose edits synchronize matching layer bones. The popup closes before domain reload without saving, with callback/tool cleanup; restored startup/reload windows close without reconstructing the session or registering Scene handlers. See [Domain reload and restored windows](SceneMeshEditing.md#domain-reload-and-restored-windows).

The popup is pinned to its source renderer context and opening selection; in skeleton-only mode the renderer supplies source coordinates and importer access without becoming a mesh binding target. Closing, changing selection or entering Play Mode automatically saves valid pending changes without confirmation. A failed controlled save keeps the session open and restores the entry selection. Undo can discard pending edits before closing. Saving writes through Sprite Editor bone, mesh and character providers; native sources reimport, while enabled UniskinRig sources update the rig without PSB import. All instances of that source use the saved skeleton.

`SkinningModule.ApplySceneBoneChanges` shares the module's parent-first ordering and bone identity conversion. Existing authored vertex positions, indices and edges are preserved. Ordinary Sprites without authored meshes remain without authored meshes. For surviving bones with stable GUIDs, positive weights retain their exact saved floating-point values and only bone indices change. Removing a weighted bone uses the existing cache's influence removal and weight normalization. Before saving a mesh with bones, vertices whose weights sum to zero receive initial weights through the existing safe bounded biharmonic generator. This prevents a previously unrigged PSB layer from collapsing when the importer activates SpriteSkin. Adding or splitting bones does not redistribute existing positive weights; further weight authoring remains available in Skinning Editor.

PSB saves update the character bone array and each layer's parent-first mapping while retaining dimensions, pivot, groups, layer positions/order and other existing character metadata. Save rejects a source that changed externally while the session was open. Skinning Editor blocks Scene editing/saving for the same asset. Edit Bones and Edit Mesh close the other popup through its normal save/discard prompt before opening. Existing SpriteSkin scene gizmos and the native SpriteRenderer handles are suspended while this Scene bone host is active, avoiding competing input. The previous Unity tool is restored on exit or blocking.

Bone coordinates use the selected Sprite's original source-pixel plane, including pivot, importer/runtime rect scaling, renderer flips and renderer transform. Character coordinates are mapped through the selected part's inverse matrix. This authors the source skeleton while displaying the instance's current scene pose, and does not create scene bone GameObjects directly. Saving a PSB skeleton does cause PSD Importer to regenerate its prefab with bone Transforms and SpriteSkin bindings. Existing prefab instances inherit those generated bones in the scene hierarchy. Explicit scene Transform overrides remain owned by the instance.

Shared `CharacterData.bones` lengths are source pixels, while runtime `Sprite.GetBones()` lengths have already been converted to Unity units. Unbound character transform lookup must use `SceneBoneInspector.CharacterBoneLocalLength` (source length divided by `ISpriteEditorDataProvider.pixelsPerUnit`) before TransformPoint/TransformVector calculations, matching PSD Importer's character hierarchy conversion. The runtime Sprite PPU may be reduced when the texture is resized, so using it here stretches bones without moving their joints. Runtime SpriteSkin lengths already include the texture definition scale; do not convert them again. Capture uses the converted local length before projecting back into source coordinates. Neither fix changes the saved source skeleton.

## Source routes

- `Editor/SkinningModule/SceneMesh/SceneEditBoneWindow.cs`: popup title/header icon, Scene host, live pose refresh scheduling, selection and close lifecycle.
- `Editor/SkinningModule/SceneMesh/SceneBoneSession.cs`: private state/cache, live pose projection, source-space dirty detection, topology synchronization, coordinates, source conflict checks and saving.
- `Editor/SkinningModule/SceneMesh/SceneBoneRenamePlan.cs`: pre-save scene bone matching, animation path validation and curve/GameObject rename application.
- `Editor/SkinningModule/SceneMesh/SceneBoneAnimationPositionPlan.cs`: GUID/Transform matching and position-key retargeting after Alt redraw.
- `Editor/SkinningModule/SceneMesh/UniskinSpriteRendererEditor.cs`: explicit Inspector entry.
- `Editor/SkinningModule/SceneMesh/SceneBoneInspector.cs`: native GameObject header entry and GUID resolution for bound scene bones.
- `Editor/SkinningModule/SkinningModule.cs`: shared save ordering, GUID weight mapping and Scene bone provider payload.
- `Editor/SkinningModule/SkinningCache/SkinningCache.cs`: persistent state reattachment after domain reload.
- `Editor/SkinningModule/IMGUI/SkeletonController.cs` and `SkeletonView.cs`: reused bone interaction/drawing.
- `Editor/Assets/SkinningModule/SceneEditBoneWindow.uxml` / `.uss`, `SceneMeshIcons/EditBone.png` and `SceneMeshIcons/Bone_*.png`: popup header and command resources.
- `Tests/Editor/SceneBoneEditingTests.cs`: importer round-trips, hierarchy, weights, animated pose projection, staged edits/Undo across frames, coordinates and resources.

## Verification

On 2026-10-13, Alt redraw and its immediate source/scene/animation-position synchronization compiled in the connected **Unity 6000.0.81f1** editor. Temporary checks covered exact weights, source saves, animation startup, constant/moving/stale/noisy position tracks, actual Animator playback, repeated redraw and Undo/Redo. An exact disposable copy of the then-current user clip retained the new joint across 31 sampled frames without changing the original clip. The user subsequently confirmed normal behavior and requested removal of the added tests. All task-added tests and temporary verification scripts were removed; existing repository tests retain their original contents. No one-time manual repair of the user's changed clip was applied. Unity 6000.0.7 and 6000.5+ execution was not performed; no new version-sensitive API was introduced.

On 2026-10-12, live animated Scene bone editing and the Edit Bones title/header icon compiled in the connected **Unity 6000.0.81f1** editor consuming this checkout through a local `file:` dependency. Six regression cases were directly invoked with Setup/Cleanup in a temporary editor runner: **6 passing / 0 failing**. This was not a full Test Runner suite. The runner and its temporary assets were removed.

The new animated-pose regression samples Transform curves over successive frames and checks displayed positions/directions, clean playback, retained source-space edits, Undo after advancing the animation, animated Split segments, source-pose save and clean playback after save. The PSB skeleton-only entry case additionally animates a generated unbound bone and checks its joint, endpoint and clean state. Existing posed editing, mesh-entry influence policy, weighted reparent/Split geometry preservation and UI resources also pass. Binding references are rebuilt after domain reload or scene Transform replacement; captured local lengths preserve the original conversion basis across source saves.

Manual checks remain for mouse dragging during continuous playback, popup appearance at different editor DPI settings, animation-window focus changes and domain reload with pending edits. Exact Unity 6000.0.7 and Unity 6000.5+ execution was not performed for this change. No new version-sensitive APIs were introduced. With a local file dependency, Unity recompiles directly from this checkout; reopen an existing popup after compilation to load the new title/header.

On 2026-10-12, the Name field, staged rename and scene/animation rename plan passed full editor-assembly compilation against Unity 6000.0.81f1 references. The Scene Bone fixture compiled with four new regression methods covering source save/staging/Undo, descendant numeric/object curves and unrelated path preservation, sibling collisions, effective AnimatorOverrideController clips and destination binding collisions. The existing UI-resource check now expects Name. These tests were not executed: MCP stopped returning after a refresh/domain reload, and Unity CLI found no Pipeline connection. Manual verification remains required for Save/Revert, renamed PSB prefab instances, animation playback and UI input. Exact Unity 6000.0.7 and 6000.5+ execution was not performed; this feature introduces no APIs requiring a new version branch.

The authoring-PPU correction passed the full editor-assembly and Scene Bone test-fixture compilation against Unity 6000.0.81 references. `CharacterLengthUsesAuthoringPpuWhenTextureIsDownscaled` adds a resized texture fixture with different source/runtime PPU; the PSB endpoint assertion independently uses provider PPU. These tests were compiled, not executed. Manually compare unbound head/leg/foot endpoints with Skinning Editor after texture resizing and reopen active Scene editors to discard old captured lengths.

On 2026-10-11 the package compiled in the connected **Unity 6000.0.81f1** editor consuming this checkout through a local `file:` dependency. Seven NUnit regression methods ran through a disposable compiled test assembly loaded in that editor, with **7 passing / 0 failing**. This was a direct invocation of the fixture methods with Setup/Cleanup, not a full Test Runner suite. Temporary assets and compiled verification files were removed.

Checks cover ordinary Sprite bones without mesh creation, staged isolation, Split and reparent serialization, parent-first ordering with GUID-preserved weights, unchanged geometry, staged reparent Undo, external-edit rejection, rotated/scaled/flipped coordinates and imported UI/icon resources. The PSB check saves only a disposable copy, validates every layer's geometry and exact GUID-associated weights, preserves non-bone character metadata and verifies that the original PSB bytes/meta remain unchanged. A UI clone also confirmed the Edit Bone title and three commands plus its close button without opening a scene editing session.

A follow-up on 2026-10-11 reproduced a disappearing FightLute layer in the user's scene: its 29 imported vertices had zero weights, its SpriteSkin referenced the two generated prefab bones, and its deformed bounds had zero extents. After filling missing source weights and reimporting, all 29 vertices had valid weights and the scene renderer had nonzero extents. Nine directly invoked regression tests passed, including first-bone assignment on ordinary authored geometry and a disposable PSB layer, runtime-imported normalized weights and prefab bone bindings. The preservation check retains exact existing positive weights while allowing formerly zero-weight vertices to be initialized. Warnings while creating intentionally zero-weight test fixtures are expected.

The implementation adds no new version-sensitive object identity calls and reuses the existing compatibility wrappers/controller aliases. Exact Unity 6000.0.7 and Unity 6000.5+ execution have not been verified for this feature.

The layer-placement follow-up passed two coordinate regression checks in Unity 6000.0.81f1: a PSB prefab instance with a translated, rotated and scaled root matches imported bone Transforms, retains identical skeleton coordinates after its renderer layer is moved/rotated/scaled, and remains clean; the ordinary Sprite rotation/scale/flip mapping also passes. No source or scene poses are rewritten by the mapping fix.

Posed editing adds a regression covering clean capture of two rotated scene bones, unchanged source pose conversion, lossless restoration of the displayed cache, movement along a posed bone, staged Undo, Set Parent/detach, Split and source save without modifying scene rotations. Ten existing checks passed in the same run; after fixing floating-point restoration, the expanded posed check passed separately. The scope restores cached local poses exactly, avoiding conversion drift in staging.

Manual checks still needed: click/drag creation and chained tails, repeated Split, joint/body/end dragging, Set Parent and detach, popup header movement, automatic save on close on selection change, domain reload with pending edits and an animated SpriteSkin source-pose display. Also check retained Create/Edit Mesh behavior after switching between the two popups.

The bone Inspector entry and supplied EditBone icon compiled in the same Unity 6000.0.81f1 editor on 2026-10-11. A temporary live probe resolved 18 bound scene bones, mapped the selected `bone_6` through its SpriteSkin binding to the shared source GUID, verified that GUID selection remains clean and that an unknown GUID preserves selection, and opened the popup on `bone_6` with Name, Color and Length populated and no pending changes. The imported icon is 16x16 and byte-identical to the supplied file. Verification helper files were removed. The button's visual layout and mouse click still require a manual Inspector check; Unity 6000.5+ execution was not performed.

Removing the popup Name field and its callbacks passed the full editor-assembly compile against Unity 6000.0.81 references. The existing resource assertion now expects that field to be absent; the fixture was not executed. Shared checkbox centering and the shorter Edit Bone layout remain manual visual checks.

The two Edit Bone entry policies and root/unbound resolver passed a full editor-assembly compile against Unity 6000.0.81 references. The updated test fixture compiled, including both creation policies, skeleton-only save/reload, unchanged per-part binding GUIDs/weights, and generated unbound-bone/root resolution. These added test cases were not executed. Native PSD Importer 9.1.2 source confirms that character prefab generation iterates the full shared bone array. Manual checks remain: open from a root and a mesh, create/save/reopen, verify no unintended mesh influences or weights, then bind the new bone through Edit Weights. No Unity 6000.5+ interactive verification was performed.

The unbound length-unit fix and native unbound Scene gizmos passed the full editor-assembly compile against Unity 6000.0.81 references. The PSB entry-policy fixture compiled with added endpoint assertions after capturing a translated, rotated and uniformly scaled generated instance; these tests were not executed. Manual checks remain: reopen Edit Bone/Weights after compilation, verify existing saved unbound bones have correct positions/lengths, verify normal Gizmos visibility with no bindings, and confirm binding/weights and the source skeleton remain unchanged.
