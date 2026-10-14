# Bone icons

## Choosing an icon

Scene **Edit Bones** has a 24x24-point icon button beside the Color field, matching the command buttons in height, showing the selected bone's current icon. Clicking it opens a separate, auto-closing popup with the same dark background and teal selection color as Edit Bones, seven columns and compact icon-only tiles. Selecting a tile applies it to all selected editable bones through the existing cache Undo scope. Mixed selection shows the first bone's icon in the button, explains the mixed state in its tooltip and has no current tile in the picker. Constraint parent controls do not accept icons.

The palette contains 35 choices from 29 supplied PNGs. `0000_Default` is the button/palette representation of ordinary bone drawing; choosing it restores the complete original bone drawing. `0008_Direction` and `0010_Arrow` each expose four cardinal directions in 90-degree steps. These are distinct saved choices sharing their original texture, rotated during UI and viewport drawing, rather than resampled PNG files.

Custom icons completely replace the bone drawing at its start position: no bone body, joint circle, endpoint or body outline is rendered. Only the icon exposes a joint picking/movement target; hidden body/tail controls are omitted. Length and rotation remain stored and available through the existing property/pose workflows. Parent links target the icon position when their parent uses an icon. They are fixed to the screen orientation represented in the picker and use the per-bone Size value times the Bone Size preference. They use the existing bone color, hover feedback and selection outline. The same drawing path is used in Skinning Editor, Scene Edit Bones/Weights and native bound/unbound Scene bone gizmos.

The properties area places **Size** beside **Length**. Size is a delayed float field enabled for selected custom icons, defaults to 24 screen points and is clamped to 4–128. It supports multiple selected icon bones and mixed values; Default bones are ignored. Size affects viewport icons and their selection outlines, not palette/button previews or skeleton lengths. Switching icon shapes preserves Size.

## Persistence

`BoneCache.iconId` and `BoneCache.iconSize` are serialized for staged editing and Undo. `BoneIconUtility` stores icon choices and sizes by stable bone GUID in a namespaced `Uniskin.BoneIcons.v1:` JSON line in importer userData or the detached UniskinRig document's userData. Existing unrelated userData lines are preserved. Previously saved diagonal Arrow/Direction choices resolve to the nearest cardinal direction (ties clockwise). Unknown/missing icon IDs fall back to Default. Older metadata without a Size uses 24. A non-default Size is retained even when switching back to Default, so selecting another custom icon restores that size. Scene dirty detection and source fingerprints include icon metadata. Source saves clear the native Scene gizmo cache so icon-only rig saves appear immediately.

Cache construction loads icons into character and sprite-sheet bones; character-to-layer synchronization copies the choice. Normal Skinning Editor Apply preserves metadata with the resulting bone set. UniskinRig Save/Bake retains the document metadata through its existing userData transport. Renaming does not change the key; deleted bones are removed from the metadata on save. This is fork-specific editor metadata, not a new field in Unity's native SpriteBone or a runtime rendering feature.

Normal Scene gizmos resolve a clipping contour back to its owning rig before reading bone display metadata. The contour borrows that skeleton but retains a private authoring document, whose icon metadata may predate later Edit Bones saves. Both bound contour bones and unbound character bones use the owner's current icon/Size. Unbound discovery deduplicates by host and owning source path, so renderer traversal order cannot overwrite an icon with a stale contour default. Closing Edit Bones therefore continues displaying the saved icon without copying metadata into the contour or changing its weights.

## Sources

- `Editor/SkinningModule/BoneIconUtility.cs`: choices, original textures, rotations, metadata and drawing.
- `Editor/SkinningModule/SceneMesh/BoneIconPicker.cs`: UI Toolkit popup grid.
- `SceneEditBoneWindow.cs` and `SceneEditBoneWindow.uxml` / `.uss`: current-icon button, selection and picker styling.
- `BoneCache`, `SkinningCache`, `CharacterPartCacheExtensions`, `SceneBoneSession`, `SkinningModule`: staging, load/save and synchronization.
- `SkeletonController`, `ISkeletonView`, `SkeletonView`, `BoneDrawingUtility`, `Editor/SpriteSkin/BoneGizmo.cs`: shared editor display.
- `Editor/Assets/SkinningModule/BoneIcons/`: supplied 64x64 transparent PNGs and texture import settings, preserving original image bytes.
- `Tests/Editor/SceneBoneEditingTests.cs` and `UniskinRigTests.cs`: metadata/resource and native/rig round-trips.

## Verification

Unity 6000.0.81f1 compiles the implementation. Three regression methods were directly invoked on disposable native Sprite/PSB/rig fixtures, with Setup/Cleanup, because the consuming project's Test Runner did not discover the package tests. All three passed: icon Undo/Redo, GUID persistence across save/reopen/rename and reset to Default; original resource availability/eight directions/unrelated metadata preservation; rig save/reload and native Bake metadata. This direct invocation is not a full Test Runner suite. The temporary rig fixture copy omitted its global Undo.ClearAll cleanup to preserve the user's Undo history.

The final picker content was hosted in a disposable UI Toolkit window for layout verification: all 43 tiles originally fit a 408x408-point panel. The four-direction compact revision uses a 264x192-point panel, seven columns, 32x32-point tiles with 2-point margins and 20x20-point images. The shared bone-properties sizing helper now sizes only actual base fields, preserving the shared Color/icon row's natural height. Mouse selection, screen placement and rendered icons during animated editing remain interactive checks. No version-sensitive API was introduced; live execution on exact Unity 6000.0.7 and Unity 6000.5+ is not claimed. With the consuming project's local file dependency, Unity recompiles directly from this checkout.

On 2026-10-14, the compact/full-replacement revision compiled in the connected Unity 6000.0.81f1 Editor. Three directly invoked Scene Bone fixture methods passed, including a new regression proving icon picking omits the hidden body/tail while Default restores both. The compact picker layout was checked in a temporary host. Actual viewport rendering and popup mouse interaction remain manual checks.

The Size revision compiled in Unity 6000.0.81f1 and passed four directly invoked fixture methods covering picking, icon/size Undo/Redo and native save/reopen/rename, legacy metadata without Size, UI/resource checks and rig save/Bake size persistence. The Length/Size row was checked in a temporary UI host. Interactive Size editing and exact Unity 6000.0.7/6000.5+ remain unverified.

On 2026-10-15, the close-window regression reproduced with a clipping contour: saved rig metadata retained Heart, but normal unbound Scene gizmos displayed Default. Two disposable tests failed on the previous lookup (Heart on an unbound bone or Arrow on a contour-bound bone), then passed after resolving display metadata through the owner, with both renderer orders. They close a real popup, verify icon/direction/Size, rebuild the gizmo cache and verify unchanged rig/contour metadata and PSB importer data. Three existing icon persistence/picking/resource checks also passed. These five cases were directly invoked with fixture Setup/Cleanup, rather than a full Test Runner run. Unity 6000.0.81f1 compiled without errors; the live closed-window cache now resolves `Ear_Pin_R` to Heart and `Ear_Pin_L` to Aim. Whitespace checks passed. Exact 6000.0.7 and 6000.5+ execution remain unverified; no version-sensitive API was added. The local file dependency recompiles directly from this checkout.
