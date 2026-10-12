# Scene mesh save performance

The independent [UniskinRig](UniskinRig.md) workflow saves Scene edits to a separate asset and generated Sprite subassets. It avoids the PSB import path measured below during ordinary rig Save; explicit or automatic Bake still runs native PSB import. The historical numbers below are not timing measurements of the new rig workflow.

## Environment and method

Measured on 2026-10-11 in the connected Unity **6000.0.81f1** editor, using this package through its existing local `file:` dependency. Rollback checkpoint: `ffc07190`.

The selected scene source was `Assets/BardCharacter 1.psb`: 34,435,252 bytes, 34 sprites, a 2048-by-2048 imported atlas, and `PSDImporter`. All mutations used an AssetDatabase copy in the unique temporary folder `Assets/__UniskinSaveBenchmark_20261011_b723d`. The original asset's dependency hash was checked before and after testing. Its mesh and import settings were not changed.

Timings use `EditorApplication.timeSinceStartup` around fresh provider preparation, mesh setters, Apply and SaveAndReimport. Three rounds per configuration changed a DressFront vertex by 0.125 source pixels. Verification after each save compared selected mesh positions, weights, indices and edges, saved bones and every neighboring Sprite's provider data. The production-code benchmark additionally checked the imported Sprite vertex count and unchanged metadata bytes for skipped saves.

Numbers below measure synchronous save calls, excluding the user dialog, opening/closing the popup and post-save verification. They are observations from this asset and machine, not universal latency guarantees.

## Results

The subsequent opt-in [Alternative PSB Importer](AlternativePsbImporter.md) reuses unchanged imported outputs for geometry saves. On the same PSB its final cache-hit imports measured 365.26, 356.74 and 358.94 ms, with exact native output comparisons against full Unity import. The original results below describe the default Unity importer and unchanged-save optimization.

| Configuration | Round 1 (ms) | Round 2 (ms) | Round 3 (ms) |
| --- | ---: | ---: | ---: |
| Original save, changed mesh, preparation + Apply + import | 2205.8 | 2511.3 | 1932.5 |
| Original Apply only | 5.1 | 5.7 | 5.1 |
| Original import only | 2196.7 | 2503.6 | 1925.5 |
| StartAssetEditing/StopAssetEditing around save, including preparation | 1922.1 | 2328.8 | 1919.5 |
| Original save, identical mesh, including preparation | 2058.7 | 1978.4 | 2418.5 |
| Optimized production WriteMesh, changed mesh | 2164.6 | 2371.4 | 1970.0 |
| Optimized production WriteMesh, identical mesh | 1.758 | 0.015 | 0.014 |
| Optimized changed mesh, compression disabled on copy | 2210.1 | 2284.9 | 1921.6 |
| Optimized identical mesh, compression disabled on copy | 1.874 | 0.022 | 0.017 |

The existing window already avoids saving a clean session. The new payload check also protects SaveChanges and dirty sessions whose final converted importer data is unchanged; it should not be presented as a speedup for a genuinely changed mesh. First authoring cannot be skipped just because the new quad equals the runtime sprite geometry.

Native Editor.log refresh statistics showed one actual PSB import, no domain reload, no cache-server download, and roughly 1.9–2.5 seconds inside ImportInProcess. Scanning, callbacks and hot reload accounted for milliseconds. PSDImporter's OnImportAsset reads the PSB, decodes its layers, repacks its image and generates texture/sprites on reimport. Fine-grained ProfilerRecorder/frame probes did not yield usable dynamic samples, so no timing is attributed separately to those internal stages.

## Rejected approaches and practical limit

- Batching moves the expensive work to StopAssetEditing. Its variation overlaps the baseline; it does not establish a meaningful speedup for one asset and was not added.
- ProduceArtifactAsync after provider Apply returned the previous artifact immediately. Final ImportAsset still blocked for 2046 ms. Reusing that artifact would leave stale native geometry; the experiment was not shipped.
- Disabling compression on the copy did not significantly improve real-edit import. Original compression, mipmaps, platform settings and project refresh mode were preserved.
- Metadata-only saving or runtime OverrideGeometry cannot replace importing: native Sprite data, build artifacts and weights must stay consistent with the source provider.
- Moving the same blocking ImportAsset to delayCall would relocate the pause without reducing it. No delayed save queue was added.

The implemented optimization removes unnecessary imports and duplicate post-save fingerprint serialization. A save guard disables popup commands and ignores reentrant Scene/selection/Undo/save events until Apply completes, then restores controls in finally. There is no supported background-import path demonstrated by these experiments. Real PSB edits still block for approximately two seconds. A larger reduction requires a separate PSDImporter optimization, such as reuse of unchanged decoded/packed image data with correct import dependencies and invalidation; this patch does not change the PSDImporter or Sprite packages.

## Regression verification and cleanup

**28/28 EditMode tests passed, 0 failed, 0 skipped**, including SceneMeshEditingTests, GeometryDeletionTests, UnityCompatibilityTests and one disposable PSB benchmark. Final regression checks also passed with compression disabled only on the disposable copy. SceneMeshEditingTests verifies unchanged saves preserve metadata bytes, first authoring persists, very small positions are not lost, and weight/triangle/edge-only changes still import.

The benchmark assembly was created temporarily in Assets with separate temporary friend-access files. The project manifest was not changed. The benchmark folder and friend files were removed at completion. The original PSB dependency hash remained unchanged. Source compilation used Unity's 6000.0.81f1 Roslyn compiler; no new version-specific Unity API was introduced. Unity 6000.5+ and manual window interaction were not executed for this optimization.
