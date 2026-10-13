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
- with each Scene editing popup open, trigger script/domain reload: it discards staging and closes without saving, releases selection/Scene callbacks and restores renderer/tools. Restart with a saved popup layout and check that the stale popup closes without repeated Scene GUI exceptions. Closure failures log to the Console and must still destroy the popup; see [popup lifecycle](Subsystems/SkinningEditor/SceneMeshEditing.md#domain-reload-and-restored-windows);
- open each Scene editing popup with Move/Rotate/Scale active: native object handles stay hidden after toolbar/shortcut tool changes, while custom editing controls and navigation work. Check weight Preview/Bind and Escape, close/reopen, blocked editing and domain reload; cleanup restores prior handle visibility and keeps an explicitly chosen tool;
- open **Edit Mesh**, check `CreateMesh.png` in its header, enable **Deformed**, and play/scrub Scene animation in Edit Mode: image, vertices, edges, triangles and box selection follow bones; playback stays clean, edits/Undo remain in source space, and closing/reopening does not bake a sampled pose. Check flips, scaled/downsampled sprites, Create/edge drags, domain reload and the source-plane fallback for New/Trace, cleared weights and missing SpriteSkin;
- open **Edit Bones** from a SpriteRenderer and from a character root/bone: check the title, `EditBone.png` header icon and layout at the active editor DPI;
- inspect a PSB SpriteRenderer and its character root: check the two mode buttons and full-width Bake button with Uniskin/PSB/Bake icons. Only the alternative mode can be clicked; Bake requires unbaked changes. Switching to PSB bakes pending changes, switching back reloads current PSB data into the rig, and Bake alone retains the active mode. Check a root without bones and a hierarchy containing two PSB sources;
- with Edit Bones open, play and scrub Scene animation in Edit Mode: bones, endpoints and hit targets follow the current pose; test mouse edits during playback, retained edits after frame changes, Undo after advancing the frame, animated Split segments and uniquely resolved unbound PSB bones. Playback alone must stay clean; save/reopen must retain source edits without baking the sampled animation pose. See [Scene Bone Editing](Subsystems/SkinningEditor/SceneBoneEditing.md#editing-during-scene-animation) for automated coverage and remaining manual checks;
- select a bone from the Weight Brush rows, rename it with F2, and confirm the renamed row/popup target, viewport bones, and Visibility list remain available without `ArgumentOutOfRangeException`; also check Weight Slider and canceling the rename dialog;
- settings panels move by their title bars and resize by the lower-right grip; positions and sizes survive tool changes, Sprite Editor reopening, and Unity restart, with separate Brush/Slider, Bone/Sprite Influence, and Constraints-mode layouts; lists expand and scroll correctly, and smaller viewports keep panels reachable;
- alpha-channel toggle shows grayscale source alpha in normal and deformed mesh previews, including the default mesh fallback; partial alpha stays gray with weight colors or reduced Visibility opacity, and switching back restores RGB;
- set Adjust Weight Opacity to 0%, 50%, and 100% on a sprite with transparent regions: only RGB weight tint changes; transparent texels stay transparent, partial-alpha edges keep their coverage, and no padding RGB or opaque mesh rectangle appears;
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
