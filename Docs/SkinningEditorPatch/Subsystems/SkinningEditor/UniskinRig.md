# UniskinRig authoring and PSB baking

## Storage and activation

UniskinRig separates geometry, skeleton, per-layer influences and weights from PSB image import. A single `UniskinRig` ScriptableObject is authoritative for one source PSB while enabled. Its source GUID survives asset moves. Its detached authoring document retains Sprite IDs, SpriteRects, complete SpriteBone index order, vertices, weights, indices, edges, CharacterData and namespaced weight/draw-order metadata. Texture, outlines, source layering and texture import settings remain owned by the PSB.

The SpriteRenderer Inspector exposes **Create UniskinRig** for PSB sources. This creates `<source>_UniskinRig.asset` alongside the source and updates loaded character instances. Selection and Inspector polling never create a rig. Ordinary imported Sprite workflows continue to use their native importer. External shared skeletons must be detached/baked at their own source before creating a rig.

The asset contains persistent generated Sprite subassets cloned from the imported Sprites. They share the imported texture/atlas and retain native rendering information; geometry and bones use the same authoring-to-runtime conversion as PSB postprocessing. Scene Mesh/Bone/Weights resolve both source and generated Sprites back to the original PSB identity and use a detached provider adapter for staged changes. Save updates the rig and its generated Sprites without calling PSB Apply or SaveAndReimport. The original image bytes and importer metadata remain unchanged until baking.

## Scene and runtime bindings

`UniskinRigBinding` is serialized on each loaded character root. It maps bone GUIDs to existing Transforms, preserves existing scene poses, creates missing bones with Undo, and assigns the generated Sprites and SpriteSkin Transform arrays. Rig saves affect loaded instances of the same source. Newly instantiated source characters are attached through the editor hierarchy update. Each character must have its own root; a single binding root cannot represent two PSB rigs.

The binding runs after SpriteResolver and before SpriteSkin, so source Sprite Library swaps are mapped to the corresponding generated Sprite. Scenes containing the component and generated Sprite references use the rig in players without Editor APIs. **Create Rig Prefab** creates a regular prefab snapshot of a loaded rig character for runtime instantiation; instantiating an unchanged PSB prefab directly in a player uses its baked PSB data. When a saved rig prefab predates later changes, the runtime binding creates new bones and missing SpriteSkin components, updates authored pose deltas and hierarchy, and removes obsolete bones while preserving their remaining children. An older rig prefab also resolves the baked skeleton on load after the asset switches to PSB mode.

Existing Transforms retain their identity. Removing an earlier bone may change numeric parent indices without changing the parent Transform; that case preserves the surviving bone's scene pose. Sprite-layer GameObjects are excluded when locating an unbound skeleton Transform. Bone rename animation-path validation remains shared with Scene Edit Bone. Closing a Scene Mesh/Bone/Weights popup or switching tools automatically saves its valid staged changes without a confirmation dialog. Delayed fields commit before saving; a failed controlled close keeps the tool open and reports the error. An external window teardown also attempts to save pending data and logs any failure. Explicit programmatic discard suppresses that fallback. Saving to the enabled rig does not import the PSB, and newly saved bones are available to Edit Weights immediately.

## Bake and return to the native workflow

**Bake to PSB** is available in the renderer and rig Inspectors. It automatically saves and closes active Scene tools, validates the complete rig document, writes mesh/bone/character data through the native Sprite Editor providers, preserves other import settings, and reimports once. The Photoshop image bytes are not rewritten: standard Skinning Editor data is stored in the PSB importer metadata, as with native Apply.

Bake verifies the reloaded authoring payload before marking the rig synchronized. If verification/import fails, it attempts to restore the previous importer serialization and keeps the rig unbaked. Generated Sprite IDs remain stable during normal rig saves. During PSB prefab regeneration, newly generated duplicate bones are reconciled with the authored scene Transforms to retain scene/animation references.

Entering the Skinning Editor module on an enabled source PSB automatically runs the same bake before creating its cache. **Skinning Editor (PSB)** / **Open Skinning Editor (Auto Bake)** also provide direct access from a generated Sprite context. Synchronized rigs skip reimport. A synchronization failure leaves the Skinning module blocked and explains the source conflict instead of allowing edits to stale PSB data.

**Bake and Use PSB** bakes, returns loaded renderers to the source Sprites, retains ordinary SpriteSkin bindings and removes their rig binding components. Renderer and SpriteSkin changes are recorded as prefab overrides. The rig asset remains as a backup. **Use UniskinRig** re-enables it. **Reload from PSB** explicitly replaces rig authoring data with current native data and refreshes the generated Sprites; unbaked changes require a discard confirmation. Successful native PSB imports refresh a synchronized rig through a deferred postprocessor; unbaked rigs are never silently overwritten. Imported bone poses already applied by prefab regeneration advance the binding baseline, preventing the same movement from being applied twice.

## Conflicts and Undo

Source dependency hashes and the captured native authoring baseline protect against external PSB changes. A changed source blocks rig save/bake until the source is restored or the rig is explicitly reloaded. Source layer creation/removal is outside this initial adapter and requires a new rig after backing up the old one. Duplicate rigs referencing one PSB are rejected rather than selected arbitrarily.

Staged editing continues to use the existing mesh/skeleton Undo scopes and events. Rig saves record the asset, native Sprite subassets, bindings, bone creation and affected scene objects; failed saves revert their scene update group. The original PSB is not reimported by Undo of a rig mesh save, including after the rig lookup cache has been invalidated. Bake records the source importer through its normal Unity Undo lifecycle.

## Source routes

- `Runtime/UniskinRig.cs`: persistent authoring document, source identity and generated Sprite references.
- `Runtime/UniskinRigBinding.cs`: player-compatible Sprite/GUID binding.
- `Editor/SkinningModule/SceneMesh/UniskinRigDataProvider.cs`: detached authoring document and provider adapter.
- `Editor/SkinningModule/SceneMesh/UniskinRigUtility.cs`: create/save/bake, conflicts, scene reconciliation, source refresh and rig prefab creation.
- `Editor/SkinningModule/SceneMesh/UniskinRigEditor.cs`: rig Inspector controls.
- `SceneMeshSource`, Scene Bone/Weight sessions and Scene windows: source routing and rig save integration.
- `Editor/SpritePostProcess.cs`: shared native conversion.
- `SkinningModule.OnModuleActivate`: automatic bake before cache creation.
- `Tests/Editor/UniskinRigTests.cs`: persistent native Sprite round-trip, source preservation, new bones/weights, explicit/automatic baking, conflicts and Undo.

## Verification

Validation uses disposable projects with packed snapshots, native PSD Importer and `testables: ["com.unity.2d.animation"]`. The optional character fixture is a copy of a 34-layer, 34,435,252-byte PSB at `Assets/UniskinRigCharacterFixture.psb`; tests create and delete further copies. No original user PSB, scene or project settings are modified by these tests.

`UniskinRigTests` covers persistent native Sprite subassets and stable IDs, untouched PSB image/metadata during rig Save, added bones and weights, complete native bake, source conflicts, Undo after index invalidation, native pose reload, pose preservation after bone removal, SpriteSkin validity for an older prefab in both rig and baked-PSB Play Mode, and Skinning module activation before mesh-cache creation. The activation test uses the public `ISpriteEditor` host contract with a real native PSB provider; interactive window/input behavior remains a manual check. A native window activation also passed in Unity 6000.0; newer Sprite Frame GUI modes cannot reliably initialize from a synchronous batch test outside OnGUI.

Verified on 2026-10-12 using the final auto-close implementation:

| Editor | PSD Importer / Common | UniskinRig tests | Compatibility tests |
| --- | --- | ---: | ---: |
| Unity 6000.0.81f1 | 9.1.2 / 9.1.2 | 12/12 passed | 8/8 passed |
| Unity 6000.5.7f1 | 14.0.3 / 14.0.1 | 12/12 passed | 8/8 passed |

Both runs have zero skipped/failed tests. The window-lifecycle test closes actual Mesh, Bone and Weights window instances, checks the saved mesh/new bones/native weights, and confirms unchanged PSB metadata. Full runtime/editor assemblies and all Editor test sources also compile against Unity 6000.0.81f1 references. Unity 6000.0.7 is not installed and was not tested directly.

One synchronous timing sample per editor on the copied 34-layer character measured bone Save at 70/106 ms, weight Save at 85/126 ms, and full PSB Bake at 2942/2697 ms (6000.0/6000.5). These are observations on this fixture and machine, including fresh Source preparation and save processing, excluding user interaction and opening the tools. They are not latency guarantees; Bake remains a native import while ordinary rig saves avoid it.

An expanded 53-test Scene Mesh/Bone/Weights sweep passed 52 tests with graphics enabled in Unity 6000.0 and 6000.5. The existing `RenameUpdatesDescendantFloatAndObjectCurvesAndSupportsUndo` assertion failed because the old animation curve remained; the same failure reproduces on unmodified Git HEAD `14f89ca7`. An additional headless baseline comparison reproduced the same 10 failures in baseline and modified Scene suites; most require a readable graphics texture. These baseline issues are outside this rig change. The connected Editor Console reported zero errors after recompilation.

Visual input, animation scrubbing, rendered CPU/GPU deformation and a standalone player build remain separate interactive/build checks; a passing data/Play Mode suite does not certify those paths.
