# Unity 2D Animation Skinning Editor Patch Instructions

## Project Scope

- This repository is a fork of Unity `com.unity.2d.animation`.
- In this repository, `Skinning Editor patch` means the customized editor-side Skinning module under `Editor/SkinningModule/**` and its UI assets under `Editor/Assets/SkinningModule/**`.
- The main development branch is `codex/skinning-editor-fork`.
- The current user project targets Unity `6000.0.7`.

## Documentation-First Workflow

Before starting every new task in this repository, first read:

1. `Docs/SkinningEditorPatch/Index.md`

Use that index as the primary entry point. Prefer routing through the documented subsystem pages before opening source files.

This rule applies before reading or editing code. If the new request is a direct continuation of the same active task and the relevant documentation was already read during that task, re-reading is optional. If there is any uncertainty, stale context, or missing memory about the relevant code, read the documentation first instead of searching the whole project.

Before reading or editing code in `Editor/SkinningModule/**`, read:

1. `Docs/SkinningEditorPatch/Index.md`
2. `Docs/SkinningEditorPatch/Subsystems/SkinningEditor/README.md`

Then choose the most relevant page before opening source files.

## Routing

- Package dependency, fork, or Unity project setup: `Docs/SkinningEditorPatch/Architecture/PackageForkWorkflow.md`
- Overall module architecture: `Docs/SkinningEditorPatch/Architecture/SkinningEditorArchitecture.md`
- Geometry toolbar labels, checked state, and button events: `Docs/SkinningEditorPatch/Subsystems/SkinningEditor/MeshToolbar.md`
- Geometry mouse behavior, vertex/edge creation, hit radius, selection clear: `Docs/SkinningEditorPatch/Subsystems/SkinningEditor/GeometryEditing.md`
- Alt temporary Modify/Create behavior: `Docs/SkinningEditorPatch/Subsystems/SkinningEditor/AltInputModeSwitching.md`
- Reset button and weight confirmation: `Docs/SkinningEditorPatch/Subsystems/SkinningEditor/ResetGeometry.md`
- Mesh vertices, edges, indices, triangulation, weights: `Docs/SkinningEditorPatch/Subsystems/SkinningEditor/MeshDataAndWeights.md`
- Copy/paste and mirrored paste: `Docs/SkinningEditorPatch/Subsystems/SkinningEditor/CopyPasteGeometry.md`
- UXML/USS/resources: `Docs/SkinningEditorPatch/Resources/SkinningModuleAssets.md`
- Final checks before commit/push: `Docs/SkinningEditorPatch/QualityAuditPlaybook.md`

Only after reading the relevant docs should source inspection become the primary discovery path.

## Documentation Update Rule

- Update documentation before finishing every task that changes behavior, architecture, workflows, toolbar commands, shortcuts, dialogs, input handling, mesh data semantics, or package workflow.
- If implementation reveals that the current documentation is wrong or incomplete, correct the documentation in the same task.
- If a new source area becomes important for future work, add a route for it in `Docs/SkinningEditorPatch/Index.md`.
- Keep docs in English.

## Fork Safety Rules

- Keep package identity `com.unity.2d.animation` unless the user explicitly asks to rename/package-split.
- Do not locally override or fork `com.unity.2d.sprite` for Skinning Editor behavior unless the user explicitly reverses the current rule.
- Prefer narrow editor-side changes under `Editor/SkinningModule/**`.
- Preserve UXML element names unless all code and style references are updated.
- Use undo scopes and `skinningCache.events.meshChanged` for mesh mutations that should affect saved Sprite Editor data.

## Verification

- Run `git diff --check` before committing.
- For significant editor behavior changes, inspect `git diff --stat` and the relevant docs diff.
- When pushing changes for Unity to consume from Git, push `codex/skinning-editor-fork`.
- If Unity local file dependency is being used, remind that Unity should recompile directly from this checkout.

## Language Preference

- Write documentation in English.
- Write user-facing summaries in English unless the user explicitly asks for another language.
