# Package Fork Workflow

## Purpose

This project is a fork of Unity's `com.unity.2d.animation` package. The fork keeps the package name and assembly names so Unity can load it as a replacement package, while project-specific Skinning Editor behavior is implemented directly inside the package source.

## Package Identity

- package name: `com.unity.2d.animation`
- package version in this fork: `10.2.2`
- fork author shown in Package Manager: `Fedorov Mishaniya` (the `author.name` field in `package.json`)
- Unity version targeted by the current user project: `6000.0.7`
- expected branch: `codex/skinning-editor-fork`
- editor assembly: `Unity.2D.Animation.Editor`
- runtime assembly: `Unity.2D.Animation.Runtime`

The fork retains its 10.2.2 baseline and minimum Unity version. Compatibility with later Unity 6 releases is implemented through targeted adapters, not by claiming to be the corresponding upstream Animation package. See [Unity Version Compatibility](UnityVersionCompatibility.md) for the API boundaries and verification matrix.

The private upstream `com.unity.2d.animation.tests` and `com.unity.2d.common.tests` related-package entries have been removed: these versions are not available in the public registry and prevent resolving a testable local fork. Regression tests are included directly in this repository.

## Consumption Modes

### Git dependency

The Unity project can consume the fork through a Git URL:

```json
"com.unity.2d.animation": "https://github.com/smoketlt/com.unity.2d.animation.git#codex/skinning-editor-fork"
```

Use this when the project should pull pushed commits from GitHub.

### Local file dependency

The Unity project can consume the local checkout directly:

```json
"com.unity.2d.animation": "file:C:/Users/Mike/Documents/Unity Skinning Editor"
```

Use this while iterating quickly. Unity should recompile from the local package source.

## Repository Boundaries

- `Editor/SkinningModule/**` is the primary area for Skinning Editor changes.
- `Editor/Assets/SkinningModule/**` owns UXML, USS, and editor resources.
- `Runtime/**` is runtime animation behavior and should not be edited for editor-only tooling changes.
- `IK/**` is separate 2D IK package code and should not be touched unless the task explicitly targets IK.
- `Documentation~/**` is Unity package documentation, separate from this developer knowledge base under `Docs/**`.

## Fork Rules

- Keep Unity package identity stable unless the user explicitly asks for a renamed package.
- Prefer small fork patches over broad upstream rewrites.
- Do not override `com.unity.2d.sprite` locally for Skinning Editor behavior; previous work avoided that by suppressing base Alt panning inside this package.
- Keep custom behavior documented under `Docs/SkinningEditorPatch/**`.
- Preserve Unity serialization-sensitive names in UXML and assets unless changing them is necessary.

## Update Flow

1. Read `Docs/SkinningEditorPatch/Index.md`.
2. Read subsystem/entity docs for the task.
3. Make the code change.
4. Update docs in the same change.
5. Run at least `git diff --check`.
6. Commit and push to `codex/skinning-editor-fork` when the user expects the Unity project to update from Git.

## Compatibility Rollback

The Unity 6000.0–6000.6 compatibility update and fork author metadata are delivered as one non-merge commit. The preceding revision is `2dbc7545d384c21ac4895f922bdd2d35434c1e19` (`Improve Weight Brush multi-bone workflow`).

If this update causes a regression, revert the compatibility commit with `git revert <compatibility-commit-sha>` and push `codex/skinning-editor-fork`. Do not reset or force-push the shared branch. This rolls back the author metadata and compatibility documentation/tests along with the implementation.

To test the previous package in only one Unity project without changing the shared branch, pin its Git dependency to `https://github.com/smoketlt/com.unity.2d.animation.git#2dbc7545d384c21ac4895f922bdd2d35434c1e19`. A project using a local `file:` dependency instead follows the checkout and must be switched explicitly if a pinned Git revision is desired.

## Change Risks

- Package identity changes can make Unity resolve the wrong package.
- UXML name changes can break `Q<Button>(...)` lookups.
- Fork changes in editor internals can be overwritten by upstream package updates.
- Local file dependency changes compile immediately; Git dependency changes require push and Unity package refresh.
