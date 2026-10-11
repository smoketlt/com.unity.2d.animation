# Alternative PSB Importer

## Scope and activation

`Editor/PsbImporter/**` contains an optional Editor assembly enabled when `com.unity.2d.psdimporter` 9.1 or newer is installed. It subclasses Unity's PSDImporter; the native package still supplies Photoshop decoding, import settings and Sprite Editor data providers. This is not a separate Photoshop decoder or a replacement package identity.

The ScriptedImporter attribute registers **only an override candidate** for `.psb`. Unity remains the default for existing and newly imported files. `.psd` is unaffected. Select individual PSB files in Project, then use the Inspector's **Use Uniskin PSB Importer** button or **Assets > Fedorov > Uniskin > PSB Importer > Use Uniskin PSB Importer**. Use **Use Unity PSD Importer** to reverse the switch. Overrides are stored in each asset's `.meta` and should travel with that asset in version control.

Both inspectors retain Unity's PSD import settings and Sprite Editor access. The PSB header intentionally replaces Unity's generic importer dropdown: that dropdown changes importer type without preserving its managed settings. Use the Uniskin button/menu instead.

## Preserving migration

`UniskinPsbImporterUtility` copies the shared PSDImporter managed serialization contract with `EditorUtility.CopySerializedManagedFieldsOnly`, keeping the destination's native script identity. It then reimports and compares Sprite rectangles, geometry, weights, bones and native subasset local IDs. A validation failure attempts restoration of the original importer and settings. Inspector settings must be applied/reverted first; switching is blocked while the same PSB is open in Skinning Editor or Scene Edit Mesh, and during Play Mode.

The public `UseUniskin(path)` and `UseUnity(path)` APIs perform the same preserving migration. Direct `AssetDatabase.SetImporterOverride` does not migrate settings and must not be used by callers. A switch requires a full import and can take seconds. No project-wide automatic switching is performed.

## Geometry cache

Full imports use Unity's implementation, then archive registered outputs and their object references under `Library/Uniskin/PsbImportCache/<asset-guid>.bin`. The archive includes texture, native Sprites, import data and generated hierarchy/skeleton/library outputs where present. Its key is embedded in the same binary file; a partial write cannot publish a new payload with an old key.

The key includes source PSB SHA-256, Unity/package versions, implementation assembly IDs, selected build target, and serialized importer settings. Only vertex payloads (including weights), triangle indices and edges are excluded. Transitions between authored and automatic geometry invalidate the cache. Frames, pivots, bones, hierarchy, outlines, texture/platform settings and source image changes require a full import. Asset references to external assets and unknown texture/Sprite postprocessors conservatively disable this path. Unity Animation's mesh conversion and URP's Sprite-light notification are explicitly supported; the optional URP adapter has no assembly dependency and falls back to full import if its callback cannot run.

On a hit, fresh archived Unity objects are loaded, geometry is validated, and `SpritePostProcess.ApplyCachedMeshData` uses the **same** position, weight and tangent conversion as a full native Sprite import. Outputs are registered with their original identifiers in the new AssetImportContext. Persistent Sprite data and build artifacts are updated, not only the live Scene renderer. Source/provider metadata remains the shared asset data used by Skinning Editor.

Meshes with preserved bones and fully cleared authoring weights use the shared runtime-only rigid first-bone fallback. The provider remains unweighted, while SpriteSkin can render the saved Trace/Reset/New/Generate geometry. Explicit rebinding replaces this fallback. The cleared-weight regression compares cached and full imports, including unchanged provider weights and preserved bones; see [Mesh data and weights](MeshDataAndWeights.md#rendering-fully-cleared-weights).

Missing, incompatible or corrupt cache data falls back to a full import before anything is registered. Unknown native output types skip cache creation. Library caches are disposable; the `.meta` and PSB remain authoritative. Do not add an artifact dependency on the importer's own script: Unity already tracks it, and doing so caused a repeated import during the initial experiment.

## Verification

On Unity **6000.0.81f1**, PSD Importer **9.1.2**, five EditMode tests passed with no failures/skips. `Tests/PsbImporter/AlternativePsbImporterTests.cs` generates a disposable flattened PSB and verifies preserving switches, Inspector controls, changing topology and two-bone weights, native UVs/bind poses, source/texture-setting invalidation, missing caches and cleared geometry. Enable package tests with the normal `testables` entry for `com.unity.2d.animation`; this optional test assembly also requires PSD Importer.

An additional disposable copy of the user's 34-sprite, 34,435,252-byte Bard PSB verified Unity → Uniskin → Unity, identical provider data and all subasset local IDs, and exact native vertices, triangles, UVs, PPU and generated hierarchy after cached saves versus a full Unity import. Final synchronous mesh-only saves measured **365.26, 356.74 and 358.94 ms**. The earlier native baseline was approximately **1.9–2.5 seconds**. These are machine/asset observations, not a universal guarantee; cache misses and switching still use full import.

The original PSB dependency hash remained unchanged. Temporary probe scripts, test assemblies, asset copies and their caches are removed after verification. No manifest or native package was modified. Live tests were run only on Unity 6000.0; newer Unity/PSD package combinations still require an integration pass. A mixed compilation using 6000.5 engine assemblies and 6000.0 package binaries cannot validate them because Sprite GUID types moved between assemblies. New source uses provider-inferred GUIDs and the main editor assembly's narrow `SceneSpriteIdentity` bridge; do not mix package binaries across editor releases.
