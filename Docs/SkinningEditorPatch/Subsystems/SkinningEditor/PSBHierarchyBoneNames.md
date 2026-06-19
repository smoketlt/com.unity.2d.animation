# PSB Hierarchy Bone Names

## Purpose

PSB prefab generation in `com.unity.2d.psdimporter` uses one global name generator for both sprite layer GameObjects and bone GameObjects. A sprite layer named `Nose` can therefore cause an unrelated nested bone whose canonical `SpriteBone.name` is also `Nose` to be generated as `Nose_1`, even though the objects do not share a parent.

## Source

- `Editor/SpriteSkin/SpriteSkinBoneNameNormalizer.cs`

## Behavior

`SpriteSkinBoneNameNormalizer` listens for editor hierarchy changes and inspects loaded scene `SpriteSkin` components on a delayed callback. Each `SpriteSkin.boneTransforms` entry is paired with the corresponding `SpriteBone` from its current Sprite.

A transform is renamed to the canonical `SpriteBone.name` only when:

- its current name is exactly the canonical name followed by `_` and one or more digits;
- no sibling already uses the canonical name;
- the SpriteSkin belongs to a valid loaded scene.

This changes importer-generated names such as `Nose_1` back to `Nose` without touching arbitrary manual names. The rename records Undo, records a prefab-instance property modification, marks the Transform dirty, and marks its scene dirty.

## Scope

This is a scene-instance normalization layer in `com.unity.2d.animation`; it does not fork or modify `com.unity.2d.psdimporter`. Imported PSB prefab source data remains owned by the PSD Importer package.

## Change Risks

- Do not remove the sibling-name check; duplicate sibling names create ambiguous animation paths.
- Do not normalize arbitrary mismatches, because users may intentionally rename scene bones.
- Keep the callback delayed and coalesced because renaming a Transform raises another hierarchy change.
