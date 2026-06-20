# Index

## Starting Points

| If you need... | Read first | Then |
| --- | --- | --- |
| Overall package/fork workflow | [Architecture/PackageForkWorkflow](Architecture/PackageForkWorkflow.md) | `package.json`, Unity project `Packages/manifest.json` |
| Skinning Editor architecture | [Architecture/SkinningEditorArchitecture](Architecture/SkinningEditorArchitecture.md) | `Editor/SkinningModule/SkinningModule*.cs` |
| Skinning Editor shortcuts, copy/paste, bone transform copy/paste, F2 rename, Ctrl+D bone duplicate | [Subsystems/SkinningEditor/SkinningModule](Subsystems/SkinningEditor/SkinningModule.md) | `Editor/SkinningModule/SkinningModuleView.cs`, `Editor/SkinningModule/RenameSelectionWindow.cs` |
| Geometry toolbar behavior | [Subsystems/SkinningEditor/MeshToolbar](Subsystems/SkinningEditor/MeshToolbar.md) | `Editor/SkinningModule/UI/MeshToolbar.cs`, `Editor/Assets/SkinningModule/MeshToolbar.uxml` |
| Modify/Create/New/Reset/Generate workflow | [Subsystems/SkinningEditor/GeometryEditing](Subsystems/SkinningEditor/GeometryEditing.md) | `Editor/SkinningModule/MeshTool/**`, `Editor/SkinningModule/IMGUI/SpriteMesh*.cs` |
| Skeleton bone drawing and Create Bone behavior | [Subsystems/SkinningEditor/SkeletonBoneEditing](Subsystems/SkinningEditor/SkeletonBoneEditing.md) | `Editor/SkinningModule/IMGUI/SkeletonView.cs`, `Editor/SkinningModule/IMGUI/SkeletonController.cs`, `Editor/SkinningModule/BoneDrawingUtility.cs` |
| Bone display size preference | [Subsystems/SkinningEditor/SkeletonBoneEditing](Subsystems/SkinningEditor/SkeletonBoneEditing.md) | `Editor/SkinningModule/UserSettings.cs`, `Editor/SkinningModule/BoneDrawingUtility.cs` |
| Alt temporary mode switching | [Subsystems/SkinningEditor/AltInputModeSwitching](Subsystems/SkinningEditor/AltInputModeSwitching.md) | `SkinningEditorInput.cs`, `MeshToolWrapper.cs`, `MeshToolbar.cs` |
| Reset geometry command | [Subsystems/SkinningEditor/ResetGeometry](Subsystems/SkinningEditor/ResetGeometry.md) | `SkinningModuleView.cs`, `SpriteMeshDataController.cs`, `MeshCache.cs` |
| Vertex dots, hit radius, mesh interaction | [Subsystems/SkinningEditor/GeometryEditing](Subsystems/SkinningEditor/GeometryEditing.md) | `SpriteMeshView.cs`, `SpriteMeshController.cs` |
| Mesh vertices, edges, indices, weights | [Subsystems/SkinningEditor/MeshDataAndWeights](Subsystems/SkinningEditor/MeshDataAndWeights.md) | `SpriteMeshDataController.cs`, `MeshCache.cs`, `SpriteMeshData.cs` |
| Copy/paste, mirrored paste, selected-vertex paste | [Subsystems/SkinningEditor/CopyPasteGeometry](Subsystems/SkinningEditor/CopyPasteGeometry.md) | `Editor/SkinningModule/CopyTool.cs` |
| Auto geometry generation | [Subsystems/SkinningEditor/GenerateGeometryTool](Subsystems/SkinningEditor/GenerateGeometryTool.md) | `Editor/SkinningModule/GenerateGeometryTool.cs` |
| Weight generation/normalization/clearing | [Subsystems/SkinningEditor/GenerateWeightsTool](Subsystems/SkinningEditor/GenerateWeightsTool.md) | `Editor/SkinningModule/GenerateWeightsTool.cs` |
| Bone Influence/Sprite Influence assignment and auto weights | [Subsystems/SkinningEditor/BoneInfluence](Subsystems/SkinningEditor/BoneInfluence.md) | `Editor/SkinningModule/SpriteBoneInfluence/**`, `Editor/SkinningModule/SkinningCache/SpriteCacheExtensions.cs` |
| Weight Slider/Brush modes and painter panel | [Subsystems/SkinningEditor/WeightPainterTool](Subsystems/SkinningEditor/WeightPainterTool.md) | `Editor/SkinningModule/WeightPainterTool.cs`, `Editor/SkinningModule/UI/WeightPainterPanel.cs` |
| Animation clip playback, frame scrubbing, Play/Pause, Stop, and Loop | [Subsystems/SkinningEditor/AnimationPreview](Subsystems/SkinningEditor/AnimationPreview.md) | `Editor/SkinningModule/AnimationPreviewController.cs`, `Editor/SkinningModule/UI/AnimationPreviewPanel.cs` |
| Visibility window, opacity sliders, bone/sprite visibility lists | [Subsystems/SkinningEditor/VisibilityTool](Subsystems/SkinningEditor/VisibilityTool.md) | `Editor/SkinningModule/VisibilityTool/**`, `Editor/Assets/SkinningModule/VisibilityToolWindow.uxml` |
| Runtime bone constraints | [Subsystems/SkinningEditor/Constraints](Subsystems/SkinningEditor/Constraints.md) | `Editor/SkinningModule/ConstraintsTool.cs`, `Editor/SkinningModule/UI/ConstraintsToolbar.cs`, `Runtime/SpriteSkinConstraintSet.cs`, `Runtime/SpriteSkinConstraintController.cs` |
| PSB scene hierarchy bone names such as `Nose_1` | [Subsystems/SkinningEditor/PSBHierarchyBoneNames](Subsystems/SkinningEditor/PSBHierarchyBoneNames.md) | `Editor/SpriteSkin/SpriteSkinBoneNameNormalizer.cs` |
| Top informational overlay text | [Architecture/SkinningEditorArchitecture](Architecture/SkinningEditorArchitecture.md) | `Editor/SkinningModule/UI/SkinningEditorInfoOverlay.cs` |
| Skinning panel placement and draggable bottom overlays | [Architecture/SkinningEditorArchitecture](Architecture/SkinningEditorArchitecture.md) | `Editor/LayoutOverlay/**`, `Editor/Assets/LayoutOverlay/**` |
| UXML/USS resources | [Resources/SkinningModuleAssets](Resources/SkinningModuleAssets.md) | `Editor/Assets/SkinningModule/**` |
| Regression sweep before push | [QualityAuditPlaybook](QualityAuditPlaybook.md) | diff, Unity compile, local Unity test project |

## Core Classes

| Symbol | Role | Source |
| --- | --- | --- |
| `SkinningModule` | Sprite Editor module host, lifecycle, toolbar wiring, keyboard copy/paste, bone transform copy/paste, F2 rename, Ctrl+D bone duplicate, reset command | `Editor/SkinningModule/SkinningModule.cs`, `SkinningModuleView.cs` |
| `SkinningCache` | Canonical editor cache, selected sprite/tool, selections, undo and events | `Editor/SkinningModule/SkinningCache/SkinningCache.cs` |
| `MeshToolbar` | Geometry toolbar UI binding and visual checked state | `Editor/SkinningModule/UI/MeshToolbar.cs` |
| `MeshToolWrapper` | Active mesh/skeleton mode wrapper and effective Alt-swapped mesh mode | `Editor/SkinningModule/MeshTool/MeshToolWrapper.cs` |
| `MeshTool` | Shared geometry editor tool instance and mesh GUI bridge | `Editor/SkinningModule/MeshTool/MeshTool.cs` |
| `SpriteMeshView` | Low-level IMGUI hit testing, hover state, action activation and drawing | `Editor/SkinningModule/IMGUI/SpriteMeshView.cs` |
| `SpriteMeshController` | Mesh operation controller: select, move, create vertex, create edge, split, remove, triangulate | `Editor/SkinningModule/IMGUI/SpriteMeshController.cs` |
| `SpriteMeshDataController` | Geometry data mutation and triangulation helper | `Editor/SkinningModule/SpriteMeshData/SpriteMeshDataController.cs` |
| `SkeletonView` | Low-level IMGUI hit testing, action activation, and bone drawing bridge | `Editor/SkinningModule/IMGUI/SkeletonView.cs` |
| `SkeletonController` | Bone selection, transform, creation, split, remove, and skeleton event handling | `Editor/SkinningModule/IMGUI/SkeletonController.cs` |
| `MeshCache` | Per-sprite mesh data and bone compatibility adapter | `Editor/SkinningModule/SkinningCache/MeshCache.cs` |
| `BaseSpriteMeshData` | Serialized vertices, weights, edges, indices, outline edges | `Editor/SkinningModule/SpriteMeshData/SpriteMeshData.cs` |
| `CopyTool` | Copy/paste data workflows including mirrored geometry paste | `Editor/SkinningModule/CopyTool.cs` |
| `GenerateGeometryTool` | Auto outline/triangulation/weight generation flow | `Editor/SkinningModule/GenerateGeometryTool.cs` |
| `GenerateWeightsTool` | Weight generation, normalization, and clearing panel | `Editor/SkinningModule/GenerateWeightsTool.cs` |
| `SpriteBoneInfluenceTool` / `BoneSpriteInfluenceTool` | Assigned bone/sprite influence list tools and auto missing-weight fill after assignment | `Editor/SkinningModule/SpriteBoneInfluence/**` |
| `WeightPainterTool` | Weight Slider/Brush editing, mode popup, brush settings, weight inspector panel | `Editor/SkinningModule/WeightPainterTool.cs`, `Editor/SkinningModule/UI/WeightPainterPanel.cs` |
| `AnimationPreviewController` / `AnimationPreviewPanel` | AnimationClip binding, temporary bone-pose sampling, frame scrubbing, playback, loop state, and bottom timeline UI | `Editor/SkinningModule/AnimationPreviewController.cs`, `Editor/SkinningModule/UI/AnimationPreviewPanel.cs` |
| `SkinningEditorInput` | Fork-specific shared modifier-key status for Skinning Editor tools | `Editor/SkinningModule/IMGUI/SkinningEditorInput.cs` |
| `VisibilityTool` | Visibility popup window, tab state, and opacity slider preview behavior | `Editor/SkinningModule/VisibilityTool/VisibilityTool.cs` |
| `ConstraintsTool` | Constraint set selection and Position/Rotation/Scale constraint editing panel | `Editor/SkinningModule/ConstraintsTool.cs` |
| `SpriteSkinConstraintController` | Runtime component that applies SpriteSkin bone constraints from a constraint set asset | `Runtime/SpriteSkinConstraintController.cs` |
| `LayoutOverlay` | Shared toolbar and overlay host, including the Skinning bottom draggable panel area | `Editor/LayoutOverlay/LayoutOverlay.cs`, `Editor/Assets/LayoutOverlay/LayoutOverlay.uxml` |
| `SkinningEditorInfoOverlay` | Static helper for showing dark-backed text at the top of the Skinning Editor overlay | `Editor/SkinningModule/UI/SkinningEditorInfoOverlay.cs` |

## Current Fork Behavior

- The package is used as a fork of Unity `com.unity.2d.animation` 10.2.2 for Unity 6000.0.
- The expected development branch is `codex/skinning-editor-fork`.
- The local Unity test project can consume the package through a `file:` dependency pointed at this repository.
- The Geometry toolbar labels are customized as `Modify`, `Create`, `New`, `Reset`, `Generate`.
- `New` deletes the current mesh and enters an open hull-authoring mode modeled after Spine 2D.
- In `New`, the selected sprite remains fully visible, unselected sprites are dimmed, and border clicks are allowed slightly outside the sprite frame and clamped to the frame.
- Exiting `New` always switches to `Modify`; Esc restores the mesh that existed before entering `New`.
- `Reset` is a command that resets the current sprite mesh to a four-vertex rectangle; it is no longer the `SplitEdge` mode button.
- `Alt` temporarily swaps `Modify` and `Create` using shared `SkinningEditorInput.altKeyDown`.
- In `Create`, dragging from a vertex creates an edge and returns to `Create` after mouse up.
- Vertex handles are larger than upstream and have a larger hit radius.
- `Esc` and primary empty click clear geometry selection where configured; right-click does not clear vertices.
- In Preview Pose and Bone toolbar modes other than `Create Bone`, `Esc` and primary empty click clear selected bones.
- In all Weight toolbar modes, `Esc` and primary empty click clear selected bones; right-click does not clear bones.
- Copy/paste supports mirrored paste and selected-vertex mirrored placement workflows.
- The Skinning Editor has a separate `Constraints -> Position/Rotation/Scale` toolbar section; constraint settings are stored in `SpriteSkinConstraintSet` assets and evaluated at runtime by `SpriteSkinConstraintController`.

## How To Search

- Start with this index and the subsystem README before using broad `rg`.
- If a behavior touches toolbar state, read `MeshToolbar.md` and `SkinningModule.md`.
- If a behavior touches mouse hover, drag, new hull mode, vertex size, or action priority, read `GeometryEditing.md`, `SpriteMeshView.md`, and `SpriteMeshController.md`.
- If a behavior touches `Alt`, read `AltInputModeSwitching.md` before editing source.
- If a behavior touches weights or confirmation dialogs, read `MeshDataAndWeights.md` and `ResetGeometry.md`.
- If a task is a continuation of the same active change and all relevant docs were already read in that task, re-reading is optional; if context is uncertain, read the docs again.
