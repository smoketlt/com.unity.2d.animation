# Alt Input Mode Switching

## Purpose

This page documents the fork-specific Alt behavior in Geometry mode and Scene Edit Bones.

## User-Facing Behavior

- Holding `Alt` in `Modify` temporarily switches to `Create`.
- Holding `Alt` in `Create` temporarily switches to `Modify`.
- The toolbar checked button reflects the temporary mode.
- Releasing `Alt` returns to the original active mode.
- In temporary `Create`, dragging from a vertex creates an edge and returns to `Create` after mouse up.

## Source

- `Editor/SkinningModule/IMGUI/SkinningEditorInput.cs`
- `Editor/SkinningModule/MeshTool/MeshToolWrapper.cs`
- `Editor/SkinningModule/UI/MeshToolbar.cs`
- `Editor/SkinningModule/SkinningModule.cs`
- `Editor/SkinningModule/IMGUI/SpriteMeshView.cs`
- `Editor/SkinningModule/SceneMesh/SceneEditMeshWindow.cs`

## Architecture

`SkinningEditorInput` owns shared Alt state.

On Windows editor builds, it polls physical Alt key state with `GetAsyncKeyState` under `UNITY_EDITOR_WIN`. This avoids unreliable `Event.alt` transitions in Unity IMGUI, where synthetic or repaint/layout events can omit modifier flags.

On non-Windows editor builds, it falls back to IMGUI event tracking.

`MeshToolWrapper.GetEffectiveMeshMode()` reads `SkinningEditorInput.altKeyDown` and swaps only `EditGeometry` and `CreateVertex`.

`MeshToolbar.UpdateToggleState()` reads the same shared status to show the effective checked button.

`SpriteMeshView` does not know about Alt mode switching. It only sees the effective `SpriteMeshViewMode`.

## Base Sprite Editor Alt Navigation

`SkinningModule` updates `SkinningEditorInput` before editing the current event. When the active geometry tool is `Modify` or `Create` and `Alt` is down, it clears the Alt modifier before geometry IMGUI runs without relying on `spriteEditor.windowDimension`. The effective mode still comes from `SkinningEditorInput.altKeyDown`, but low-level handles and sliders no longer treat the same mouse event as Sprite Editor view navigation.

`SkinningModule.ConsumeUnhandledAltMouseNavigation()` consumes unhandled `Alt` mouse down/drag/up events in `Modify` and `Create` after Skinning Editor tools have had first chance to process them. This prevents leftover `Alt + mouse` input from reaching the base Sprite Editor pan handler when a mesh action did not use the event.

`SkinningModule.DisableBaseSpriteEditorAltNavigation()` also clears the Alt modifier from the current event after Skinning Editor GUI has run. This keeps non-mouse and already-handled events from carrying Alt into base Sprite Editor navigation without forking `com.unity.2d.sprite`.

## Scene mesh editor

Scene Edit Mesh shares the same Alt state and swaps only its selected Modify/Create modes. Session mode stays unchanged; Scene handles, direct double-click deletion and toolbar highlighting use the effective mode. Alt is latched before event modifiers are cleared for primary-button mesh input; unhandled primary mouse down/drag/up is consumed after the mesh controller. Right/middle mouse navigation remains available. New, Delete and active Trace do not swap.

On Windows, Scene's editor-update callback calls `SkinningEditorInput.Update(null)` for physical-key polling and repaints when the state changes, including when focus moves to the popup. Non-Windows null updates do nothing and original Scene IMGUI events drive the existing fallback. The callback is unsubscribed when the window disables. Scene uses the existing physical-key implementation rather than a second Alt tracker.

## Scene bone editor

With exactly one editable bone selected, Scene **Edit Bones** temporarily uses Alt + primary click/drag/release to redraw that bone while preserving its identity, hierarchy and exact weights. Its Create tile highlights during the temporary gesture; releasing Alt restores the prior command. The preview commits only on mouse-up with Alt still held, automatically saving the bone session and refreshing scene bindings/pivots without closing the popup. Alt release, Esc or window/gesture cancellation discards it. Right/middle mouse navigation remains available; Alt with no single editable selection retains Scene navigation. See [Scene Bone Editing](SceneBoneEditing.md#redrawing-a-selected-bone-with-alt).

The host latches shared `SkinningEditorInput` before suppressing Alt for the replacement gesture. Its editor-update callback polls the physical key on Windows and repaints mode feedback, following Scene Edit Mesh's existing tracking convention. Bone selection/transform handles are disabled during replacement so an Alt drag over another bone cannot move or select it. No shared `SkeletonController` creation behavior is changed.

## Known Failure Modes

- If Alt is tracked directly from `Event.alt` on every event, mode can bounce `Modify <-> Create` during `Layout/Repaint`.
- If Alt is only tracked from `KeyUp`, mode can remain stuck in temporary mode when Unity sends KeyUp to another window.
- If temporary mode is implemented inside `SpriteMeshView`, toolbar visual state and action routing drift apart.
- If the event Alt modifier is only cleared after geometry GUI, temporary `Modify` can fail to drag vertices because legacy sliders still see `Event.current.alt`.
- If early Alt suppression depends on `spriteEditor.windowDimension`, it can work in one part of the editor viewport and still allow base panning in another.
- If unhandled `Alt + mouse` events are only stripped of the Alt modifier but not consumed, the base Sprite Editor can still intermittently start panning from the same event stream.

## Change Risks

- Do not reintroduce per-action Alt checks in `SpriteMeshView`.
- Do not reset `altKeyDown` on `Layout` or `Repaint`.
- Do not clear the event Alt modifier before calling `SkinningEditorInput.Update(...)`; the shared state must be latched from the original event first.
- If adding macOS/Linux physical polling, keep the public contract as `SkinningEditorInput.altKeyDown`.
