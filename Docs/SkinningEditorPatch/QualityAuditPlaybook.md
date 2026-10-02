# Quality Audit Playbook

## Purpose

Use this checklist before committing or pushing changes to the Skinning Editor fork.

## Required Documentation Checks

- Read `Docs/SkinningEditorPatch/Index.md` before starting a new task.
- Read relevant subsystem/entity docs before opening source files.
- Update docs when behavior changes.
- Update `Index.md` when adding a new workflow, command, or important class.

## Code Checks

- Run `git diff --check`.
- Inspect `git diff --stat` for unexpected file churn.
- Check `git status --short --branch`.
- For toolbar changes, inspect both UXML and C# binding.
- For mesh changes, verify undo, `meshChanged`, selection clearing, and triangulation.
- For input changes, verify `SkinningEditorInput`, `MeshToolWrapper`, and `MeshToolbar` together.

## Manual Unity Checks

For cross-version changes, follow [Unity Version Compatibility](Architecture/UnityVersionCompatibility.md), run the included EditMode compatibility suite in separate projects, and record exact versions and limitations. Passing a headless suite is not a substitute for visual GPU and input testing.

When a Unity editor is available, verify in the local test project:

- package recompiles from the expected dependency source;
- alpha-channel toggle shows grayscale source alpha in normal and deformed mesh previews, including the default mesh fallback; partial alpha stays gray with weight colors or reduced Visibility opacity, and switching back restores RGB;
- with mipmaps enabled on a packed PSB atlas, zoom and deform the mesh in RGB and alpha modes: neighboring-layer colors and inconsistent mip blur must not appear across triangles; distinguish remaining base-level compression/downscaling artifacts from mip sampling;
- Geometry toolbar labels show `Modify`, `Create`, `New`, `Reset`, `Generate`;
- `Alt` swaps `Modify` and `Create` while held and returns on release;
- drag from vertex in `Create` creates an edge and exits edge-drag state on mouse up;
- `Reset` resets to a four-corner rectangle;
- weighted reset shows confirmation and cancel leaves geometry unchanged;
- unweighted reset runs without confirmation;
- copy/paste and mirrored paste still work after geometry changes.

## Push Checklist

1. Confirm docs and code are both staged.
2. Commit with a focused message.
3. Push `codex/skinning-editor-fork`.
4. Tell the user what changed and whether Unity should refresh/recompile.
