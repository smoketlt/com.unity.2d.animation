# Unity Version Compatibility

## Scope

The fork keeps package identity `com.unity.2d.animation`, version `10.2.2`, and minimum Unity `6000.0`. The compatibility target is the Unity 6000.0–6000.6 release families. This does not imply feature parity with upstream Animation 13–16 or verification of every editor patch release.

Because the fork replaces the entire package, compatibility also requires changes to the original runtime deformation, Sprite Library, and IK object-ID consumers. These changes do not replace or fork `com.unity.2d.sprite`.

## API Boundaries

| Boundary | Old path | New path |
| --- | --- | --- |
| Unity 6000.5+ active-object discovery | `FindObjectsByType<T>(FindObjectsSortMode.None)` | `FindObjectsByType<T>()` through `UnityObjectCompatibility.FindObjectsByType<T>` |
| Unity 6000.4+ object identity | `int` / `GetInstanceID` | full `EntityId` / `GetEntityId` |
| Unity 6000.4+ editor object lookup | `InstanceIDToObject` | `EntityIdToObject` |
| Visibility TreeView | non-generic before 6000.2; `TreeView<int>` in 6000.2–6000.3 | `TreeView<EntityId>` in 6000.4+ |
| Unity 6000.4+ asset creation / drag and drop | `EndNameEditAction`, `AddDropHandler` | `AssetCreationEndAction`, `AddDropHandlerV2` |
| Unity 6000.3+ checked UI state | 2D Common `SetChecked` / `IsChecked` extensions | UI Toolkit `SetCheckedPseudoState` / `hasCheckedPseudoState` |
| 2D Common 13+ GPU skinning bridge | global query and bone pointer/size arrays | renderer-aware query and packed bone-index array |

GPU selection is controlled by `ANIMATION_COMMON_13_OR_NEWER`, an assembly version define based on the resolved `com.unity.2d.common`, not the manifest's minimum version. The no-renderer query passes `null`, as in upstream Animation 16. The per-renderer query passes the actual SpriteRenderer. CPU skinning remains available; GPU skinning is not disabled as a compilation workaround.

The modern GPU upload uses each valid skin's `PerSkinJobData.bindPosesIndex.x`. Invalid skins receive `-1`. Existing packed bone-matrix uploads and bounds computation remain in place.

## Batched Deformation State Recovery

`GpuDeformationSystem.Update` can report an assertion such as `Expected: 2 == 4` when the per-skin active array has two entries but the registered skin set has four. This checks batch registration state, not the sprite's vertex or bone count. The report was observed with the `AlphaVersion` Git dependency at `a04a09cc` on Unity 6000.5.7; the original triggering action has not yet been reproduced.

CPU and GPU updates now use the shared `BaseDeformationSystem.SynchronizeSpriteSkinData` before and after processing pending registration changes, before validation and job scheduling. When shared per-skin arrays or the renderer array have inconsistent lengths, it completes the deformation job, resizes through the system's virtual resize method (including GPU arrays), reassigns every skin's data index and recopies its sprite/renderer data. Existing assertions remain enabled. Normal frames with matching lengths do not rebuild the batch.

Removal sizes come from the actual remaining skin set after removing queued entries, rather than subtracting the queue size. Cleanup also clears pending additions, removals and transform-removal IDs so those queues cannot survive system disposal.

`DeformationBatchRecoversTruncatedArraysAndStaleIndices` exercises both CPU and GPU systems with four registered skins, arrays truncated to two entries and deliberately stale indices, with no pending change, a pending removal or a pending addition. It checks that Update succeeds, indices are contiguous and renderer bindings match each skin. This is an injected-state regression test, not reproduction of the original user's scene or proof of rendered GPU output.

Verified on 2026-10-13 using an immutable snapshot in disposable projects: Unity 6000.0.81f1 with Common 9.1.2 and Unity 6000.5.7f1 with Common 14.0.1 each passed all 14 compatibility test cases with no skipped cases or C# compilation errors. Both runs used `-batchmode -nographics`; visual GPU deformation and the original scene remain unverified. The connected Unity 6000.0.81f1 development project also compiled successfully, but its manifest does not enable package tests, so its zero-test Pipeline run is not counted as regression verification.

## Destroyed SpriteRenderer and SpriteSkin Cleanup

Deleting a sprite GameObject can leave a destroyed renderer in a deformation batch until its deferred removal is processed. The reported follow-up on `420261d9` accessed that renderer in `DeactivateDeformableBuffers`, producing `MissingReferenceException` from `IsUsingDeformableBuffer`.

Batch synchronization now filters destroyed skins/renderers from pending additions and active membership before copying sprite data. If active entries are removed this way, it rebuilds transform references from the surviving skins (excluding queued removals), avoiding the destroyed skins' disposed bone-ID arrays. Buffer deactivation also checks Unity object null semantics before calling the native bridge.

`DeformationBatchDropsDestroyedSkins` destroys actual GameObjects in CPU/GPU batches, including pending additions, queued removal of the last skin, and deletion combined with truncated arrays. It checks the native deactivation boundary with a stale renderer, surviving membership and indices, repeated Update, and the final empty batch.

Verified on 2026-10-14: all 20 compatibility cases passed with no skipped cases on Unity 6000.0.81f1/Common 9.1.2 and Unity 6000.5.7f1/Common 14.0.1. The disposable projects used a snapshot of published `420261d9` plus this runtime/test change, excluding unrelated working-tree edits. Both runs used `-batchmode -nographics`; the original scene and visual GPU output remain unverified.

## Stale Registration Indices and Invalid World Bounds

Re-enabling the body GameObject and zooming the Scene view exposed repeated `Invalid worldAABB` errors in Unity 6000.6.0f1. Live inspection found `a_Hand_R` with a 39-vertex cache but a batch entry containing 24 vertices and a different source pointer; the batch bounds were approximately 1e29. The body's own data remained finite. Renderer bounds alone did not reveal the corrupt batch entry.

`CopyToSpriteSkinData` now ignores skins outside the registered set and skins queued for removal. Removed skins can retain an old data index that another skin owns after batch compaction; newly enabled skins can publish changes before their deferred addition assigns a new index. Those callbacks must not write through the stale index. Normal registered copies and the registration-time copy remain enabled. This fixes the stale-write path rather than clamping the resulting bounds or suppressing the native error.

`DeformationBatchIgnoresCopiesFromUnregisteredSkins` covers CPU and GPU batches, removal/compaction, a stale copy, a pending re-addition and final registration. It verifies both renderer ownership and the sprite data's transform identity.

Verified on 2026-10-16: the connected Unity 6000.6.0f1 Editor completed recompilation without errors. A disposable reflection-based harness exercised the same CPU/GPU removal and re-addition sequence and passed six ownership checks. A subsequent live deformation update showed matching batch/skin vertex counts and source pointers for all 27 scene skins, with finite normal bounds. The NUnit fixture was added but not run through Test Runner; Unity 6000.0 and 6000.5 were not recompiled during this task. The guard uses existing collection APIs with no new version-dependent Unity API. Repeating the user's interactive disable/re-enable/zoom gesture remains manual verification.

## Warning Maintenance

Runtime IK culling, Sprite post-processing, Scene GPU picking and Scene mesh renderer discovery share the runtime object-discovery wrapper. Its explicit `UNITY_6000_5_OR_NEWER` branch avoids the deprecated sort-mode overload on newer editors while retaining the active-only, unordered query on 6000.0–6000.4. The runtime assembly already exposes internals to both the editor and IK assemblies.

The Constraints ListView subscribes to `selectionChanged`, available on the minimum supported Unity version. `SkinningCache.m_Events` and `BaseTool.m_LayoutOverlay` are session-only fields without `[SerializeField]`: Unity cannot serialize their types. Events retain their field initializer and module listener wiring; the VisualElement reference is assigned by `BaseTool.Initialize`. Removing the ignored attributes does not add these fields to Undo or persistence.

Verified on 2026-10-15 in the connected Unity 6000.6.0f1 Editor consuming this checkout: requested script compilation, confirmed compilation/import had finished, and read zero warning/error Console entries. The 6000.0–6000.4 branch retains the previous query; it was not recompiled in an older Editor during this warning-maintenance task.

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

Do not add `-quit` to this test invocation; let the Test Framework finish and exit. Confirm the XML reports twenty executed test cases (including CPU/GPU batch recovery and destruction), not merely a successful process or a zero-test result. The historical matrix below predates these twelve additional cases.

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
