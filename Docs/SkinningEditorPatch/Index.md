# Index

## Starting Points

| If you need... | Read first | Then |
| --- | --- | --- |
| Overall package/fork workflow | [Architecture/PackageForkWorkflow](Architecture/PackageForkWorkflow.md) | `package.json`, Unity project `Packages/manifest.json` |
| Skinning Editor architecture | [Architecture/SkinningEditorArchitecture](Architecture/SkinningEditorArchitecture.md) | `Editor/SkinningModule/SkinningModule*.cs` |
| Geometry toolbar behavior | [Subsystems/SkinningEditor/MeshToolbar](Subsystems/SkinningEditor/MeshToolbar.md) | `Editor/SkinningModule/UI/MeshToolbar.cs`, `Editor/Assets/SkinningModule/MeshToolbar.uxml` |
| Modify/Create/New/Reset/Generate workflow | [Subsystems/SkinningEditor/GeometryEditing](Subsystems/SkinningEditor/GeometryEditing.md) | `Editor/SkinningModule/MeshTool/**`, `Editor/SkinningModule/IMGUI/SpriteMesh*.cs` |
| Alt temporary mode switching | [Subsystems/SkinningEditor/AltInputModeSwitching](Subsystems/SkinningEditor/AltInputModeSwitching.md) | `SkinningEditorInput.cs`, `MeshToolWrapper.cs`, `MeshToolbar.cs` |
| Reset geometry command | [Subsystems/SkinningEditor/ResetGeometry](Subsystems/SkinningEditor/ResetGeometry.md) | `SkinningModuleView.cs`, `SpriteMeshDataController.cs`, `MeshCache.cs` |
| Vertex dots, hit radius, mesh interaction | [Subsystems/SkinningEditor/GeometryEditing](Subsystems/SkinningEditor/GeometryEditing.md) | `SpriteMeshView.cs`, `SpriteMeshController.cs` |
| Mesh vertices, edges, indices, weights | [Subsystems/SkinningEditor/MeshDataAndWeights](Subsystems/SkinningEditor/MeshDataAndWeights.md) | `SpriteMeshDataController.cs`, `MeshCache.cs`, `SpriteMeshData.cs` |
| Copy/paste, mirrored paste, selected-vertex paste | [Subsystems/SkinningEditor/CopyPasteGeometry](Subsystems/SkinningEditor/CopyPasteGeometry.md) | `Editor/SkinningModule/CopyTool.cs` |
| Auto geometry generation | [Subsystems/SkinningEditor/GenerateGeometryTool](Subsystems/SkinningEditor/GenerateGeometryTool.md) | `Editor/SkinningModule/GenerateGeometryTool.cs` |
| Weight generation/normalization/clearing | [Subsystems/SkinningEditor/GenerateWeightsTool](Subsystems/SkinningEditor/GenerateWeightsTool.md) | `Editor/SkinningModule/GenerateWeightsTool.cs` |
| UXML/USS resources | [Resources/SkinningModuleAssets](Resources/SkinningModuleAssets.md) | `Editor/Assets/SkinningModule/**` |
| Regression sweep before push | [QualityAuditPlaybook](QualityAuditPlaybook.md) | diff, Unity compile, local Unity test project |

## Core Classes

| Symbol | Role | Source |
| --- | --- | --- |
| `SkinningModule` | Sprite Editor module host, lifecycle, toolbar wiring, keyboard copy/paste, reset command | `Editor/SkinningModule/SkinningModule.cs`, `SkinningModuleView.cs` |
| `SkinningCache` | Canonical editor cache, selected sprite/tool, selections, undo and events | `Editor/SkinningModule/SkinningCache/SkinningCache.cs` |
| `MeshToolbar` | Geometry toolbar UI binding and visual checked state | `Editor/SkinningModule/UI/MeshToolbar.cs` |
| `MeshToolWrapper` | Active mesh/skeleton mode wrapper and effective Alt-swapped mesh mode | `Editor/SkinningModule/MeshTool/MeshToolWrapper.cs` |
| `MeshTool` | Shared geometry editor tool instance and mesh GUI bridge | `Editor/SkinningModule/MeshTool/MeshTool.cs` |
| `SpriteMeshView` | Low-level IMGUI hit testing, hover state, action activation and drawing | `Editor/SkinningModule/IMGUI/SpriteMeshView.cs` |
| `SpriteMeshController` | Mesh operation controller: select, move, create vertex, create edge, split, remove, triangulate | `Editor/SkinningModule/IMGUI/SpriteMeshController.cs` |
| `SpriteMeshDataController` | Geometry data mutation and triangulation helper | `Editor/SkinningModule/SpriteMeshData/SpriteMeshDataController.cs` |
| `MeshCache` | Per-sprite mesh data and bone compatibility adapter | `Editor/SkinningModule/SkinningCache/MeshCache.cs` |
| `BaseSpriteMeshData` | Serialized vertices, weights, edges, indices, outline edges | `Editor/SkinningModule/SpriteMeshData/SpriteMeshData.cs` |
| `CopyTool` | Copy/paste data workflows including mirrored geometry paste | `Editor/SkinningModule/CopyTool.cs` |
| `GenerateGeometryTool` | Auto outline/triangulation/weight generation flow | `Editor/SkinningModule/GenerateGeometryTool.cs` |
| `GenerateWeightsTool` | Weight generation, normalization, and clearing panel | `Editor/SkinningModule/GenerateWeightsTool.cs` |
| `SkinningEditorInput` | Fork-specific shared modifier-key status for Skinning Editor tools | `Editor/SkinningModule/IMGUI/SkinningEditorInput.cs` |

## Current Fork Behavior

- The package is used as a fork of Unity `com.unity.2d.animation` 10.2.2 for Unity 6000.0.
- The expected development branch is `codex/skinning-editor-fork`.
- The local Unity test project can consume the package through a `file:` dependency pointed at this repository.
- The Geometry toolbar labels are customized as `Modify`, `Create`, `New`, `Reset`, `Generate`.
- `New` deletes the current mesh and enters an open hull-authoring mode modeled after Spine 2D.
- `Reset` is a command that resets the current sprite mesh to a four-vertex rectangle; it is no longer the `SplitEdge` mode button.
- `Alt` temporarily swaps `Modify` and `Create` using shared `SkinningEditorInput.altKeyDown`.
- In `Create`, dragging from a vertex creates an edge and returns to `Create` after mouse up.
- Vertex handles are larger than upstream and have a larger hit radius.
- `Esc`, right-click, and primary empty click clear geometry selection where configured.
- Copy/paste supports mirrored paste and selected-vertex mirrored placement workflows.

## How To Search

- Start with this index and the subsystem README before using broad `rg`.
- If a behavior touches toolbar state, read `MeshToolbar.md` and `SkinningModule.md`.
- If a behavior touches mouse hover, drag, new hull mode, vertex size, or action priority, read `GeometryEditing.md`, `SpriteMeshView.md`, and `SpriteMeshController.md`.
- If a behavior touches `Alt`, read `AltInputModeSwitching.md` before editing source.
- If a behavior touches weights or confirmation dialogs, read `MeshDataAndWeights.md` and `ResetGeometry.md`.
- If a task is a continuation of the same active change and all relevant docs were already read in that task, re-reading is optional; if context is uncertain, read the docs again.
