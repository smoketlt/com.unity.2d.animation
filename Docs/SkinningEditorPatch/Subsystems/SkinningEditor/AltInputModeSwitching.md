# Alt Input Mode Switching

## Purpose

This page documents the fork-specific Alt behavior in Geometry mode.

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
