# Mesh Data And Weights

## Purpose

This page documents the mesh data model used by Skinning Editor geometry, including vertices, edges, indices, outline edges, and bone weights.

## Source

- `Editor/SkinningModule/SpriteMeshData/SpriteMeshData.cs`
- `Editor/SkinningModule/SpriteMeshData/SpriteMeshDataController.cs`
- `Editor/SkinningModule/SkinningCache/MeshCache.cs`
- `Editor/SkinningModule/SpriteMeshData/EditableBoneWeight.cs`
- `Editor/SkinningModule/SpriteMeshData/EditableBoneWeightUtility.cs`

## Data Model

`BaseSpriteMeshData` stores:

- `Vector2[] vertices`
- `EditableBoneWeight[] vertexWeights`
- `int[] indices`
- `int2[] edges`
- `int2[] outlineEdges`
- temporary vertex-position override for pose preview

`MeshCache` extends `BaseSpriteMeshData` for a concrete `SpriteCache`.

Serialized mesh weights use indices into the complete saved `SpriteBone` array. That array can include generated constraint-parent bones, while `MeshCache.bones` intentionally excludes those service bones. During cache loading, `MeshCache` therefore temporarily keeps the complete unfiltered bone list while reading vertex weights, then remaps every weight channel into the filtered mesh-compatible list. The inverse remap is performed when Apply serializes mesh weights. Both directions are required to keep weights stable across Apply/reload.

In Character mode, `MeshCache.bones` references character-skeleton bones and the sprite skeleton contains separate cloned `BoneCache` objects. During initial cache creation, saved weights are first loaded against sprite-skeleton order and then remapped by GUID into `CharacterPartCache.bones`. Apply performs the inverse GUID mapping back into serialized sprite-skeleton order. It must not treat an index in `CharacterPartCache.bones` as a sprite-skeleton index: that array is an influence list whose order can differ arbitrarily from both the character skeleton and sprite skeleton, including reversed chain order.

The serialized `CharacterPart.bones` array has stricter semantics than the editor-side influence list: each entry maps the SpriteBone at the same array position to a bone in the character skeleton. Apply rebuilds this array by iterating the sprite's exact saved parent-first bone order and resolving each corresponding character bone by GUID. Saving the editor influence-list order directly can look correct inside Skinning Editor but binds runtime `SpriteSkin.boneTransforms` to the wrong SpriteBone indices after PSD Importer regenerates the prefab.

## Geometry Mutation

Use `SpriteMeshDataController` for geometry operations:

- `CreateVertex(...)`
- `CreateEdge(...)`
- `CreateQuad()`
- `RemoveVertex(...)`
- `Triangulate(...)`
- `SortTrianglesByDepth()`
- `ClearWeights(...)`
- `NormalizeWeights(...)`
- `CalculateWeights(...)`

## Quad Geometry

`CreateQuad()` creates four vertices from `spriteMeshData.frame.size`:

- bottom-left
- bottom-right
- top-left
- top-right

Then it creates the perimeter edges and triangulation can produce the final indices.

## Weight Detection

Use `EditableBoneWeight.Sum()` to detect whether a mesh has meaningful weights. A vertex weight array can exist even when all weights are empty, so array length is not enough.

The Reset and New commands treat a mesh as weighted when any vertex weight has `Sum() > 0f`.

## Selection Safety

Weight multi-edit reads and writes ignore selected vertex indices that are no longer valid for the current `vertexWeights` array. This prevents the Weight Inspector from throwing when geometry deletion or retriangulation changes the mesh before another UI panel has repainted with the cleaned selection.

## Weight Slider Vertex Display

In Weight Slider mode, mesh vertices replace the regular cyan/yellow handles with large circular weight pies. The pie diameter is twice the previous regular vertex size. Each enabled weight channel contributes a colored slice using the bound bone's `bindPoseColor`; duplicate channels for the same bone are merged before drawing. Slices are ordered by bone name and then bone index so colors do not flip while weights change. Slice angles use absolute weight share up to `1.0`; any unassigned remainder is black. Vertices with no enabled weight draw as a black circle.

The Weight Slider Vertex Weight list is separate from the internal active channel list. It displays every bone assigned to the current mesh, including bones whose selected-vertex weight is currently zero, while zero-weight channels can still be filtered out internally.

Selected Weight Slider vertices draw at full opacity with a white outline; unselected vertices draw at 50% opacity. Clicking and dragging from a vertex edits the currently selected bone influences on the selected vertices: dragging up transfers weight from other active channels into the selected bone channels, and dragging down transfers weight from selected bone channels into other active channels. If no selected bone maps to the current mesh influences, or if there is no opposite active channel to receive/give weight, dragging leaves weights unchanged.

Weight Slider row swatches can lock a bone's weights. Locked weights are preserved by row edits, drag edits, Smooth, and Prune.

The Weight Slider panel `Smooth` button performs one conservative Laplacian-style pass over vertex weights. It averages each targeted bone's value from immediate edge neighbors, using selected vertices as the target set when present and all vertices otherwise. Two or more selected bones limit which weight channels are averaged; zero or one selected bone means every unlocked mesh bone is included. The pass uses a pre-click weight snapshot for all neighbor reads, weights neighbors by inverse edge length, blends partway from the current value to that neighbor average, then clamps to four channels and normalizes.

The `Prune` button removes small or excess unlocked weights. It previews how many weight channels will be removed, applies a maximum-bones-per-vertex limit, applies a threshold, and redistributes removed unlocked weight to remaining unlocked weights.

## Weight Loss On Reset/New

Resetting geometry clears all vertices and creates new vertices with empty weights. New mesh hull mode clears all vertices and then lets the user define new vertices with empty weights. Both operations intentionally remove existing skinning weights for the attachment.

When weights exist, the user must confirm the operation.

## Change Risks

- Directly replacing vertices without matching `vertexWeights` breaks data invariants.
- Changing indices must update outline edges through `SetIndices(...)`.
- Triangulation can add or reorder geometry; preserve weight behavior when using it.
- `MeshCache.SetBones(...)` fixes weights when the compatible bone set changes.
- Loading saved weights directly against the filtered mesh bone list shifts every index after a generated constraint parent and corrupts weights on Apply/reload.
- Treating a Character Part influence-list index as a sprite-skeleton index can reverse or otherwise permute weights when Apply serializes them; use bone GUID identity across the two skeleton caches.
- Replacing the sprite-skeleton bone list with Character Part influence bones without remapping loaded channels causes the same permutation immediately after Apply reloads the cache.
- Serializing `CharacterPart.bones` in influence-list order instead of saved SpriteBone order corrupts the generated runtime `SpriteSkin` binding even when Skinning Editor weights remain correct.
