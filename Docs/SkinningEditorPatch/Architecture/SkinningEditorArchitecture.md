# Skinning Editor Architecture

## Purpose

This page maps the editor-side architecture used by the forked Skinning Editor package.

## Primary Flow

1. Unity opens the Sprite Editor with the Skinning Editor module.
2. `SkinningModule` creates `SkinningCache`, tool caches, preview tools, and toolbar UI.
3. `SkinningModuleView` wires toolbar button events to tool activation or command handlers.
4. `SkinningCache` owns selected sprite, selected tool, mesh/bone selections, undo, and events.
5. Geometry tools route through one shared `MeshTool` instance wrapped by `MeshToolWrapper` variants.
6. `MeshTool` sets up `SpriteMeshController` and `SpriteMeshView`.
7. `SpriteMeshView` identifies active low-level actions from IMGUI events and handle controls.
8. `SpriteMeshController` mutates mesh data through `SpriteMeshDataController`.
9. Mesh mutations invoke `skinningCache.events.meshChanged`, causing `SkinningModule` to mark Sprite Editor data dirty.

## Major Areas

- lifecycle and module host: `SkinningModule.cs`
- UI and shortcuts: `SkinningModuleView.cs`
- tool creation and state: `SkinningCache.cs`
- geometry toolbar: `MeshToolbar.cs`, `MeshToolbar.uxml`
- mesh tool wrapper: `MeshToolWrapper.cs`
- geometry view/controller: `SpriteMeshView.cs`, `SpriteMeshController.cs`
- mesh data: `SpriteMeshData.cs`, `SpriteMeshDataController.cs`, `MeshCache.cs`
- input patch: `SkinningEditorInput.cs`
- top informational overlay: `SkinningEditorInfoOverlay.cs`
- animation preview and bottom timeline: `AnimationPreviewController.cs`, `AnimationPreviewPanel.cs`

## Tool Model

`SkinningCache.CreateToolCache(...)` registers tools by `Tools` enum values. Geometry tool entries are wrappers around the same `MeshTool` instance:

- `Tools.EditGeometry` -> `SpriteMeshViewMode.EditGeometry`
- `Tools.CreateVertex` -> `SpriteMeshViewMode.CreateVertex`
- `Tools.CreateEdge` -> `SpriteMeshViewMode.CreateEdge`
- `Tools.SplitEdge` -> `SpriteMeshViewMode.SplitEdge`
- `Tools.GenerateGeometry` -> `GenerateGeometryTool`

The forked toolbar uses user-facing labels:

- `Modify` = `Tools.EditGeometry`
- `Create` = `Tools.CreateVertex`
- `New` = `Tools.CreateEdge`
- `Reset` = command event, not `Tools.SplitEdge`
- `Generate` = `Tools.GenerateGeometry`

## Fork-Specific Architecture

### Viewport draw order

`SkinningModule.DoMainGUI()` draws sprite rect gizmos after mesh preview overlays and before the active tool GUI. This keeps sprite bounds visible above the preview/wireframe while letting tool handles, including Weight Slider vertex pies, draw above the blue sprite bounds rectangle.

### Shared Alt state

`SkinningEditorInput` owns the shared `Alt` key state. `MeshToolWrapper` reads this state and computes the effective mode. `SpriteMeshView` does not query Alt directly for mode switching.

### Reset command

The `Reset` toolbar button is now a command handled by `SkinningModuleView.ResetGeometry()`. It does not activate a mesh mode.

### Base Sprite Editor Alt panning

`SkinningModule.DisableBaseSpriteEditorAltNavigation()` removes the Alt modifier from the current event after Skinning Editor GUI has used it. This prevents the base Sprite Editor window from treating Alt as pan navigation without forking `com.unity.2d.sprite`.

### Bottom tool panels

`LayoutOverlay` exposes a `bottomOverlay` area for Skinning tool panels that should not stack under the right-side Visibility window. Weight Painter, Bone Inspector, Generate Geometry, Generate Weights, Paste, Pivot, and Influence panels are added through `LayoutOverlay.AddBottomOverlayPanel(...)`.

These panels initially appear centered along the bottom edge. `LayoutOverlayUtility.MakeDraggableOverlayPanel(...)` adds a title-bar drag handle to each panel and `OverlayPanelDragger` switches the panel to absolute positioning after the first drag, clamped inside the bottom overlay. Tools call `LayoutOverlayUtility.ResetDraggableOverlayPanel(...)` before hiding their panel so a dragged panel returns to the normal bottom-center layout the next time that tool is shown.

The Visibility popup remains in `rightOverlay` because it is a tall list window with its own resizer and right-side workflow.

### Animation preview timeline

`AnimationPreviewPanel` occupies the dedicated absolute `TimelineOverlay` strip at the bottom of the Skinning Editor. It remains separate from `bottomOverlay`, whose bottom padding reserves the same space, so persistent clip playback controls do not overlap the active Weight Painter, Bone Inspector, Generate, Paste, Pivot, Influence, or Constraints panel. Do not place the timeline in the legacy `HorizontalToolbar`: `LayoutOverlay` uses reverse column flow and that toolbar appears at the top of the viewport when enabled.

`AnimationPreviewController` reads supported Transform curves from the selected `AnimationClip`, matches binding paths to `BoneCache` hierarchy paths, and applies the sampled local position, rotation, and scale as a temporary preview pose. It drives the existing `skeletonPreviewPoseChanged` -> `MeshPreviewCache.SetSkinningDirty()` flow and never marks Sprite importer data modified.

### Top informational overlay

`SkinningEditorInfoOverlay` is a static UI Toolkit helper for temporary text hints at the top of the Skinning Editor. `BaseTool` registers each hint with the tool instance as its owner and removes only that owner's request on deactivation. The overlay displays the highest-priority active request, using the most recently shown request to break equal-priority ties. This prevents a parallel tool from hiding or permanently replacing the primary mode's instructions. The horizontal Visibility tool registers its hint at lower priority, so Weight Brush and other primary modes keep their own text while Visibility is also active; the Visibility hint remains available as a fallback when no primary hint exists. The overlay uses `PickingMode.Ignore` so it does not block editor input.

All primary Skinning Editor modes show a short hint through this overlay when activated. Shared mesh modes are handled in `MeshToolWrapper`, shared skeleton modes are handled in `SkeletonToolWrapper`, and specialized tools such as Weight Slider, Weight Brush, Auto Weights, Generate Geometry, Influence, Visibility, Pivot, Reparent, and Copy/Paste replace the shared text with tool-specific text from `SkinningEditorInfoText`.

## Change Risks

- Moving mode-switching logic into `SpriteMeshView` would reintroduce duplicated mode state.
- Making `Reset` a tool again would conflict with the user-facing command semantics.
- Calling mesh mutation APIs without `UndoScope` or `meshChanged` can leave Unity data dirty state incorrect.
- Adding new toolbar buttons requires updating UXML, `MeshToolbar.cs`, `SkinningModuleView.cs`, and docs.
- Moving a tool panel between overlay regions requires checking both `LayoutOverlayStyle.uss` and any panel-specific USS selectors scoped to `#RightOverlay` or `#BottomOverlay`.
