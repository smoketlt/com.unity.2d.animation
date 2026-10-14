using System;
using System.IO;
using System.Linq;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Animation
{
    [InitializeOnLoad]
    internal static class UniskinClippingMaskUtility
    {
        static UniskinClippingMaskUtility()
        {
            Undo.undoRedoPerformed += RestoreLoaded;
            EditorApplication.delayCall += RestoreLoaded;
        }

        internal static UniskinClippingMaskData Find(Sprite sprite) => sprite != null ?
            AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(sprite)) as UniskinClippingMaskData : null;

        internal static void Create(UniskinClippingMask mask)
        {
            if (mask.contour != null) throw new InvalidOperationException("A contour is already assigned.");
            var target = mask.GetComponent<SpriteRenderer>();
            var character = mask.character != null ? mask.character : target.GetComponentInParent<UniskinRigBinding>();
            if (character == null || character.rig == null || !character.rig.editingEnabled)
                throw new InvalidOperationException("Choose a character using Uniskin rig.");
            if (!target.transform.IsChildOf(character.transform)) throw new InvalidOperationException("The target must belong to the selected character.");
            var children = target.GetComponentsInChildren<SpriteRenderer>(true).Where(renderer => renderer != target).ToArray();
            var staleContours = children.Where(renderer => renderer.transform.parent == target.transform &&
                renderer.name == "Uniskin Clipping Contour" &&
                (renderer.gameObject.hideFlags & HideFlags.HideInHierarchy) != 0 &&
                Find(renderer.sprite)?.rig == character.rig).ToArray();
            if (target.GetComponent<SortingGroup>() != null || children.Except(staleContours).Any())
                throw new InvalidOperationException("Use a Sprite layer without a SortingGroup or child Sprite layers.");
            var path = AssetDatabase.GUIDToAssetPath(character.rig.sourceGuid);
            if (!UniskinRigUtility.CloseSceneTools(path)) return;
            UniskinRigUtility.CheckSource(character.rig);
            var source = character.rig.Find(target.sprite)?.source ?? target.sprite;
            if (source == null) throw new InvalidOperationException("Choose a source Sprite for the initial contour.");
            var native = UniskinRigUtility.NativeProvider(AssetDatabase.GetAssetPath(source));
            var rect = native.GetSpriteRects().Single(item => item.spriteID == source.GetSpriteID());
            Undo.IncrementCurrentGroup(); var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Uniskin Clipping Contour");
            var asset = ScriptableObject.CreateInstance<UniskinClippingMaskData>();
            var assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.GetDirectoryName(AssetDatabase.GetAssetPath(character.rig)).Replace('\\', '/') + "/" + target.name + "_ClippingMask.asset");
            try
            {
                foreach (var stale in staleContours) Undo.DestroyObjectImmediate(stale.gameObject);
                var go = new GameObject("Uniskin Clipping Contour") { hideFlags = HideFlags.HideInHierarchy }; Undo.RegisterCreatedObjectUndo(go, "Create Clipping Contour");
                go.transform.SetParent(target.transform, false);
                var renderer = Undo.AddComponent<SpriteRenderer>(go);
                renderer.sprite = source; renderer.enabled = false;
                asset.name = target.name + " Clipping Mask"; asset.rig = character.rig; asset.source = source;
                asset.rigToContour = go.transform.worldToLocalMatrix * character.transform.localToWorldMatrix;
                asset.sourceDependencyHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source)).ToString();
                var data = new UniskinRigData
                {
                    pixelsPerUnit = native.pixelsPerUnit, hasCharacter = true, userData = string.Empty,
                    sprites = new[] { new UniskinRigSpriteData { rect = UniskinRigData.Clone(rect), bones = Array.Empty<SpriteBone>(),
                        vertices = Array.Empty<Vertex2DMetaData>(),
                        indices = Array.Empty<int>(), edges = Array.Empty<Vector2Int>() } },
                    character = new CharacterData { dimension = Vector2Int.RoundToInt(rect.rect.size), pivot = rect.pivot,
                        bones = BorrowedBones(asset, native.pixelsPerUnit, rect),
                        parts = new[] { new CharacterPart { spriteId = rect.spriteID.ToString(), parentGroup = -1,
                            spritePosition = new RectInt(0, 0, Mathf.RoundToInt(rect.rect.width), Mathf.RoundToInt(rect.rect.height)), bones = Array.Empty<int>() } } }
                };
                UniskinRigUtility.Validate(data); asset.authoringData = data.Serialize();
                AssetDatabase.CreateAsset(asset, assetPath);
                asset.sprite = Object.Instantiate(source); asset.sprite.name = asset.name; asset.sprite.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(asset.sprite, asset);
                Rebuild(asset, new UniskinRigDataProvider(asset, native));
                Undo.RecordObject(mask, "Assign Clipping Contour");
                Undo.RecordObject(target, "Assign Clipping Contour");
                mask.contour = asset; mask.character = character; mask.contourRenderer = renderer;
                renderer.sprite = asset.sprite;
                mask.Refresh();
                Record(mask); Record(target); Record(renderer);
                AssetDatabase.SaveAssetIfDirty(asset);
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) == asset) AssetDatabase.DeleteAsset(assetPath);
                else Object.DestroyImmediate(asset);
                throw;
            }
        }

        internal static void CheckSource(UniskinClippingMaskData asset)
        {
            if (asset.rig == null || asset.source == null || asset.sprite == null) throw new InvalidOperationException("The contour image or character rig is missing.");
            UniskinRigUtility.CheckSource(asset.rig);
            var path = AssetDatabase.GetAssetPath(asset.source);
            if (path != AssetDatabase.GUIDToAssetPath(asset.rig.sourceGuid) && AssetDatabase.GetAssetDependencyHash(path).ToString() != asset.sourceDependencyHash)
                throw new InvalidOperationException("The source image/import settings changed. Back up this contour before creating a replacement.");
        }

        internal static void Save(UniskinRigDataProvider provider)
        {
            var asset = provider.clipping; CheckSource(asset); UniskinRigUtility.Validate(provider.data);
            var expected = BorrowedBones(asset, provider.data.pixelsPerUnit, provider.data.sprites[0].rect);
            if (!SameBones(expected, provider.data.character.bones))
                throw new InvalidOperationException("Edit the skeleton on the character; reopen the contour after a skeleton change.");
            if (asset.authoringData == provider.data.Serialize()) return;
            Undo.IncrementCurrentGroup(); var group = Undo.GetCurrentGroup();
            try
            {
                Undo.RegisterCompleteObjectUndo(asset, "Save Clipping Contour"); Undo.RegisterCompleteObjectUndo(asset.sprite, "Save Clipping Contour");
                asset.authoringData = provider.data.Serialize(); Rebuild(asset, provider); RefreshInstances(asset, true);
                AssetDatabase.SaveAssetIfDirty(asset); Undo.CollapseUndoOperations(group);
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }

        [Serializable] sealed class BoneBox { public SpriteBone[] bones; }
        static bool SameBones(SpriteBone[] a, SpriteBone[] b) => JsonUtility.ToJson(new BoneBox { bones = a }) == JsonUtility.ToJson(new BoneBox { bones = b });

        internal static SpriteBone[] BorrowedBones(UniskinClippingMaskData asset, float ppu, SpriteRect rect)
        {
            var rig = asset.rig; var source = rig.characterBones;
            var worlds = new Matrix4x4[source.Length]; var ready = new bool[source.Length];
            Matrix4x4 World(int index)
            {
                if (ready[index]) return worlds[index];
                var bone = source[index]; var parent = bone.parentId >= 0 ? World(bone.parentId) : Matrix4x4.Translate(rig.rootOffset);
                ready[index] = true;
                return worlds[index] = parent * Matrix4x4.TRS(bone.position / rig.pixelsPerUnit, bone.rotation, Vector3.one);
            }
            var toPixels = Matrix4x4.Translate(Vector2.Scale(rect.rect.size, rect.pivot)) * Matrix4x4.Scale(Vector3.one * ppu) * asset.rigToContour;
            if (!SceneMeshBindingPose.IsFinite(toPixels) || Mathf.Abs(toPixels.determinant) < 1e-12f) throw new InvalidOperationException("Contour transforms must have non-zero, finite scale.");
            var result = new SpriteBone[source.Length];
            for (var i = 0; i < result.Length; ++i)
            {
                var bone = source[i]; var world = toPixels * World(i);
                // SpriteBone translations are pixels, including child-local offsets.
                // Inverting the scaled parent matrix would cancel the pixel conversion
                // and leave children in world units while roots/lengths remain pixels.
                var position = world.MultiplyPoint3x4(Vector3.zero);
                var rotation = world.rotation;
                if (bone.parentId >= 0)
                {
                    var parentWorld = toPixels * World(bone.parentId);
                    var inverseRotation = Quaternion.Inverse(parentWorld.rotation);
                    position = inverseRotation * (position - parentWorld.MultiplyPoint3x4(Vector3.zero));
                    rotation = inverseRotation * rotation;
                }
                bone.position = position; bone.rotation = rotation;
                bone.length = world.MultiplyVector(Vector3.right * source[i].length / rig.pixelsPerUnit).magnitude;
                result[i] = bone;
            }
            return result;
        }

        internal static void Synchronize(UniskinRig rig, bool undo)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniskinClippingMaskData"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniskinClippingMaskData>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null || asset.rig != rig || asset.source == null || asset.sprite == null) continue;
                var provider = new UniskinRigDataProvider(asset, UniskinRigUtility.NativeProvider(AssetDatabase.GetAssetPath(asset.source)));
                var data = provider.data; var item = data.sprites[0]; var bones = BorrowedBones(asset, data.pixelsPerUnit, item.rect);
                if (SameBones(bones, JsonUtility.FromJson<UniskinRigData>(asset.authoringData).character.bones)) continue;
                if (undo) { Undo.RegisterCompleteObjectUndo(asset, "Update Clipping Skeleton"); Undo.RegisterCompleteObjectUndo(asset.sprite, "Update Clipping Skeleton"); }
                var retained = item.bones.Where(bone => bones.Any(candidate => candidate.guid == bone.guid)).ToArray();
                for (var vertex = 0; vertex < item.vertices.Length; ++vertex)
                {
                    var weight = item.vertices[vertex].boneWeight; var total = 0f;
                    for (var channel = 0; channel < 4; ++channel)
                    {
                        var old = weight.GetBoneIndex(channel);
                        var index = old >= 0 && old < item.bones.Length ? Array.FindIndex(retained, bone => bone.guid == item.bones[old].guid) : -1;
                        if (index < 0) BoneWeightExtensions.SetWeight(ref weight, channel, 0);
                        BoneWeightExtensions.SetBoneIndex(ref weight, channel, Mathf.Max(index, 0)); total += weight.GetWeight(channel);
                    }
                    if (total > 0) for (var channel = 0; channel < 4; ++channel) BoneWeightExtensions.SetWeight(ref weight, channel, weight.GetWeight(channel) / total);
                    else if (retained.Length > 0) weight = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
                    item.vertices[vertex].boneWeight = weight;
                }
                item.bones = retained; data.character.bones = bones;
                var part = data.character.parts[0]; part.bones = retained.Select(bone => Array.FindIndex(bones, candidate => candidate.guid == bone.guid)).ToArray(); data.character.parts[0] = part;
                var cache = Cache.Create<SkinningCache>();
                try
                {
                    cache.Create(provider, new SpritePostProcess.SkinningCachePersistentStateTemp());
                    cache.selectedSprite = cache.GetSprite(item.rect.spriteID.ToString());
                    // SkinningCache supplies an image outline for absent geometry.
                    // An empty contour is intentional, including after Undo: keep
                    // its empty vertex stream while updating borrowed influences.
                    if (item.vertices.Length == 0)
                        cache.selectedSprite.GetMesh().SetVertices(Array.Empty<Vector2>(), Array.Empty<EditableBoneWeight>());
                    SkinningModule.ApplySceneWeightChanges(cache, provider);
                }
                finally { Object.DestroyImmediate(cache); }
                asset.authoringData = data.Serialize(); Rebuild(asset, provider); RefreshInstances(asset, undo); AssetDatabase.SaveAssetIfDirty(asset);
            }
        }

        static void Rebuild(UniskinClippingMaskData asset, UniskinRigDataProvider provider)
        {
            SpritePostProcess.ApplyUniskinRigData(provider, new[] { asset.sprite });
            UniskinRigUtility.RestoreTextureCoordinates(asset.source, asset.sprite);
            SceneMeshBindingPose.Apply(provider, new[] { asset.sprite }, provider.data.userData);
            asset.hasGeometry = provider.data.sprites[0].vertices.Length >= 3 && provider.data.sprites[0].indices.Length >= 3;
            ++asset.revision; EditorUtility.SetDirty(asset); EditorUtility.SetDirty(asset.sprite);
        }

        static void RefreshInstances(UniskinClippingMaskData asset, bool undo)
        {
            foreach (var mask in Resources.FindObjectsOfTypeAll<UniskinClippingMask>().Where(mask => mask.contour == asset && !EditorUtility.IsPersistent(mask)))
            {
                var renderer = mask.contourRenderer; if (renderer == null) continue;
                if (asset.sprite.GetBones().Length > 0 && renderer.GetComponent<SpriteSkin>() == null)
                {
                    if (undo) Undo.AddComponent<SpriteSkin>(renderer.gameObject); else renderer.gameObject.AddComponent<SpriteSkin>();
                }
                if (undo) { Undo.RecordObject(renderer, "Update Clipping Contour"); var skin = renderer.GetComponent<SpriteSkin>(); if (skin != null) Undo.RecordObject(skin, "Update Clipping Bindings"); }
                mask.Refresh(); if (undo) Record(renderer);
            }
        }

        static void RestoreLoaded()
        {
            var assets = Resources.FindObjectsOfTypeAll<UniskinClippingMask>().Where(mask => mask.contour != null && mask.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(mask))
                .Select(mask => mask.contour).Distinct().ToArray();
            // Upgrade borrowed skeleton coordinates on existing contours too. The
            // GUID remap preserves authored weights and mesh-specific binding poses;
            // recalculating weights remains the user's explicit Auto command.
            foreach (var rig in assets.Select(asset => asset.rig).Where(rig => rig != null).Distinct())
                try { Synchronize(rig, false); }
                catch (Exception ex) { Debug.LogWarning("Uniskin clipping skeleton: " + ex.Message, rig); }
            foreach (var asset in assets)
                try
                {
                    Rebuild(asset, new UniskinRigDataProvider(asset, UniskinRigUtility.NativeProvider(AssetDatabase.GetAssetPath(asset.source))));
                    RefreshInstances(asset, false);
                }
                catch (Exception ex) { Debug.LogWarning("Uniskin clipping contour: " + ex.Message, asset); }
        }

        static void Record(Object obj)
        {
            EditorUtility.SetDirty(obj);
            if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            if (obj is Component component && component.gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }
    }
}
