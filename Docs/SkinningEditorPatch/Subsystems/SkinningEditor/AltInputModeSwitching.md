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

`SkinningModule.DisableBaseSpriteEditorAltNavigation()` clears the Alt modifier from the current event after Skinning Editor GUI has run. This prevents the base Sprite Editor window from panning on Alt without forking `com.unity.2d.sprite`.

## Known Failure Modes

- If Alt is tracked directly from `Event.alt` on every event, mode can bounce `Modify <-> Create` during `Layout/Repaint`.
- If Alt is only tracked from `KeyUp`, mode can remain stuck in temporary mode when Unity sends KeyUp to another window.
- If temporary mode is implemented inside `SpriteMeshView`, toolbar visual state and action routing drift apart.

## Change Risks

- Do not reintroduce per-action Alt checks in `SpriteMeshView`.
- Do not reset `altKeyDown` on `Layout` or `Repaint`.
- If adding macOS/Linux physical polling, keep the public contract as `SkinningEditorInput.altKeyDown`.
