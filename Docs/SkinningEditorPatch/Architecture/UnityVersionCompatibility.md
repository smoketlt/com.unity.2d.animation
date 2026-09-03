# Unity Version Compatibility

## Scope

The fork keeps package identity `com.unity.2d.animation`, version `10.2.2`, and minimum Unity `6000.0`. The compatibility target is the Unity 6000.0–6000.6 release families. This does not imply feature parity with upstream Animation 13–16 or verification of every editor patch release.

Because the fork replaces the entire package, compatibility also requires changes to the original runtime deformation, Sprite Library, and IK object-ID consumers. These changes do not replace or fork `com.unity.2d.sprite`.

## API Boundaries

| Boundary | Old path | New path |
| --- | --- | --- |
| Unity 6000.4+ object identity | `int` / `GetInstanceID` | full `EntityId` / `GetEntityId` |
| Unity 6000.4+ editor object lookup | `InstanceIDToObject` | `EntityIdToObject` |
| Visibility TreeView | non-generic before 6000.2; `TreeView<int>` in 6000.2–6000.3 | `TreeView<EntityId>` in 6000.4+ |
| Unity 6000.4+ asset creation / drag and drop | `EndNameEditAction`, `AddDropHandler` | `AssetCreationEndAction`, `AddDropHandlerV2` |
| Unity 6000.3+ checked UI state | 2D Common `SetChecked` / `IsChecked` extensions | UI Toolkit `SetCheckedPseudoState` / `hasCheckedPseudoState` |
| 2D Common 13+ GPU skinning bridge | global query and bone pointer/size arrays | renderer-aware query and packed bone-index array |

GPU selection is controlled by `ANIMATION_COMMON_13_OR_NEWER`, an assembly version define based on the resolved `com.unity.2d.common`, not the manifest's minimum version. The no-renderer query passes `null`, as in upstream Animation 16. The per-renderer query passes the actual SpriteRenderer. CPU skinning remains available; GPU skinning is not disabled as a compilation workaround.

The modern GPU upload uses each valid skin's `PerSkinJobData.bindPosesIndex.x`. Invalid skins receive `-1`. Existing packed bone-matrix uploads and bounds computation remain in place.

## Identity Rules

- Use the per-file `ObjectId` alias for object identities, including native job arrays, dictionaries, visibility rows, skeleton picking, and IK culling.
- Use `UnityObjectCompatibility.GetObjectId` and `UnityEditorObjectCompatibility.FindObject` at engine boundaries.
- Do not cast EntityId to int or use a hash as a bone/cache identity. The new engine prohibits those casts, and truncation could alias unrelated objects.
- Vertex buffers use full `ulong` keys; `GetBufferId` preserves the engine ID. Existing managed deformation-system buffer keys retain their previous hash-based origin.
- IMGUI control IDs, mesh indices, bone-name hashes, and array indices remain integers. The existing analytics schema also remains integer-based and uses the importer's object hash; it is not used to resolve or select objects.
- Object IDs are transient caches, not serialized sprite bone indices. Sprite bone order, weights, undo, and importer Apply semantics are unchanged.

## GUID Collision

New 2D Common packages include a triangulation utility with the GUID originally shipped by Animation 10.2.2. The fork's editor-only `TriangulationUtility.cs.meta` has a unique GUID. This is a static helper, not a serialized component or asset type; its C# type and behavior are unchanged.

## Regression Verification

`Tests/Editor/UnityCompatibilityTests.cs` covers object lookup, transform cache reference counts and jobs, 64-bit buffer-key separation, typed visibility rows, checked toolbar state, custom Geometry toolbar UXML, triangulation against the resolved Common package, and packed GPU offsets.

To enable tests in a disposable Unity project:

1. Reference this checkout using a local `file:` dependency.
2. Add `"testables": ["com.unity.2d.animation"]` at the top level of the project manifest.
3. Install the editor-compatible 2D Common, URP, and Test Framework packages.
4. Run EditMode tests filtered to `UnityEditor.U2D.Animation.Tests.UnityCompatibilityTests`.

The test assembly is named `Unity.2D.Animation.Tests.EditorTests`, matching the package's existing friend-assembly declarations.

Do not upgrade the user's main project to run a compatibility check. Use separate projects per editor release. Batch tests with `-nographics` exercise CPU jobs, data and editor resources, but do not prove rendered GPU output or interactive mouse/keyboard behavior. Full validation still needs a weighted sprite scene, CPU/GPU rendering, Sprite Editor Apply/reopen, and the manual checks in the Quality Audit Playbook.

For a multi-editor sweep, prefer a packed `.tgz` snapshot referenced via `file:`. Direct local folder dependencies are writable: importing UI icon textures in newer editors can automatically upgrade hundreds of `.meta` files in the checkout. The final verification pass uses an immutable package snapshot, and generated icon-importer changes are not part of the fork patch.

The interactive editor's pending API Updater can also write stale C# output over newer local edits. During this compatibility pass it replaced the conditional drag-and-drop registration with a partial upgrade (V2 inspector/scene handlers, but a legacy hierarchy handler), removing the old/new branches after the packed snapshot had passed testing. This caused CS0123 and CS0619 in the open project. Restore the conditional registration from the verified source, then recompile and check the live Console. When a packed test passes but a local checkout fails, compare the actual C# sources before assuming a compiler or test discrepancy; do not run a pending API update over in-progress compatibility edits.

Example batch invocation (use an installed editor executable and a disposable project):

```text
Unity.exe -batchmode -nographics -projectPath <test-project> -runTests -testPlatform EditMode -testFilter UnityEditor.U2D.Animation.Tests.UnityCompatibilityTests -testResults <results.xml> -logFile <editor.log>
```

Do not add `-quit` to this test invocation; let the Test Framework finish and exit. Confirm the XML reports eight executed tests, not merely a successful process or a zero-test result.

## Verification Matrix

Verified on 2026-09-03 using the same packed fork snapshot in five disposable projects:

| Unity editor | 2D Common | Collections | URP | EditMode result |
| --- | --- | --- | --- | --- |
| 6000.0.81f1 | 9.1.2 | 2.6.8 | 17.0.4 | 8/8 passed |
| 6000.3.21f1 | 12.0.3 | 2.6.8 | 17.3.0 | 8/8 passed |
| 6000.4.4f1 | 13.0.1 | 6.4.0 | 17.4.0 | 8/8 passed |
| 6000.5.7f1 | 14.0.1 | 6.5.0 | 17.5.0 | 8/8 passed |
| 6000.6.0f1 | 15.0.0 | 6.6.0 | 17.6.0 | 8/8 passed |

All package runtime, IK, and editor assemblies compiled in these projects. The tests executed 40 times in total with no failures. No package compilation or GUID errors were present in the final logs. Unrelated upstream deprecation/serialization warnings may remain.

Unity 6000.1, 6000.2, and the original 6000.0.7 were not installed and were not tested. The older API branch is retained for them, but do not label these exact versions verified. Rendered GPU output, player builds, full PSD Importer workflows, and interactive Skinning Editor regression checks remain manual follow-up work.

The open `unity 66 test` project was switched from the Git URL to this local checkout for development. Unity should recompile directly from the checkout. MCP initially stopped responding after the editor assembly reload, so the matrix was verified using separate batch processes. During a follow-up check, the stale API Updater overwrite described above was repaired and MCP reported zero errors in the open Unity 6000.6 Console. No Git commit or push is implied by these local test results.
