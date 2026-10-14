using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Animation
{
    [InitializeOnLoad]
    internal static class UniskinRigUtility
    {
        static Dictionary<string, UniskinRig> s_Rigs;
        static bool s_Baking, s_Refreshing;

        static UniskinRigUtility()
        {
            Undo.undoRedoPerformed += RefreshAfterUndo;
            EditorApplication.hierarchyChanged += QueueInstanceRefresh;
            EditorApplication.delayCall += RepairMissingTextureCoordinates;
        }

        internal static string SourcePath(Sprite sprite)
        {
            var path = AssetDatabase.GetAssetPath(sprite);
            var clipping = AssetDatabase.LoadMainAssetAtPath(path) as UniskinClippingMaskData;
            if (clipping != null) return clipping.rig != null ? AssetDatabase.GUIDToAssetPath(clipping.rig.sourceGuid) : string.Empty;
            var owner = AssetDatabase.LoadMainAssetAtPath(path) as UniskinRig;
            return owner != null ? AssetDatabase.GUIDToAssetPath(owner.sourceGuid) : path;
        }

        internal static UniskinRig Find(Sprite sprite)
        {
            if (sprite == null) return null;
            return FindAtSource(SourcePath(sprite));
        }

        internal static UniskinRig FindAtSource(string path)
        {
            if (s_Rigs == null)
            {
                s_Rigs = new Dictionary<string, UniskinRig>();
                foreach (var guid in AssetDatabase.FindAssets("t:UniskinRig"))
                {
                    var rig = AssetDatabase.LoadAssetAtPath<UniskinRig>(AssetDatabase.GUIDToAssetPath(guid));
                    if (rig == null || string.IsNullOrEmpty(rig.sourceGuid)) continue;
                    if (s_Rigs.ContainsKey(rig.sourceGuid))
                        throw new InvalidOperationException("Multiple UniskinRig assets reference one PSB. Keep one authoritative rig per source.");
                    s_Rigs.Add(rig.sourceGuid, rig);
                }
            }
            s_Rigs.TryGetValue(AssetDatabase.AssetPathToGUID(path), out var result);
            return result;
        }

        internal static ISpriteEditorDataProvider NativeProvider(string path)
        {
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(AssetImporter.GetAtPath(path));
            if (provider == null) throw new InvalidOperationException("The source has no Sprite Editor provider.");
            provider.InitSpriteEditorDataProvider();
            return provider;
        }

        internal static UniskinRig Create(Sprite sprite)
        {
            var path = SourcePath(sprite);
            if (!path.EndsWith(".psb", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("UniskinRig currently requires a PSB source.");
            var existing = FindAtSource(path);
            if (existing != null) return existing;
            if (!CloseSceneTools(path)) return null;
            if (SkinningModule.IsEditingAsset(path)) throw new InvalidOperationException("Close Skinning Editor before creating a rig.");
            var provider = NativeProvider(path);
            if (provider.GetDataProvider<IMainSkeletonDataProvider>() != null)
                throw new InvalidOperationException("Bake or detach the external shared skeleton before creating UniskinRig.");
            var rig = ScriptableObject.CreateInstance<UniskinRig>();
            var assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(path, null) + "_UniskinRig.asset");
            try
            {
                var data = UniskinRigData.Read(provider, AssetImporter.GetAtPath(path).userData);
                if (!data.hasCharacter && data.sprites.Length != 1)
                    throw new InvalidOperationException("A multilayer rig requires PSB Character mode to share its skeleton.");
                Validate(data);
                rig.name = Path.GetFileNameWithoutExtension(path) + " UniskinRig";
                rig.sourceGuid = AssetDatabase.AssetPathToGUID(path);
                rig.authoringData = rig.bakedData = rig.sourceBaseline = data.Serialize();
                CaptureSourceHashes(rig, path);
                var sourceSprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
                rig.sprites = data.sprites.Select(item =>
                {
                    var source = sourceSprites.Single(candidate => candidate.GetSpriteID() == item.rect.spriteID);
                    return new UniskinRig.SpriteEntry { id = item.rect.spriteID.ToString(), source = source };
                }).ToArray();
                AssetDatabase.CreateAsset(rig, assetPath);
                foreach (var entry in rig.sprites)
                {
                    entry.sprite = Object.Instantiate(entry.source);
                    entry.sprite.name = entry.source.name;
                    entry.sprite.hideFlags = HideFlags.None;
                    AssetDatabase.AddObjectToAsset(entry.sprite, rig);
                }
                RefreshSprites(rig, new UniskinRigDataProvider(rig, provider));
                AssetDatabase.SaveAssetIfDirty(rig);
                InvalidateIndex();
                RefreshInstances(rig);
                return rig;
            }
            catch
            {
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) == rig) AssetDatabase.DeleteAsset(assetPath);
                else Object.DestroyImmediate(rig);
                InvalidateIndex();
                throw;
            }
        }

        internal static void CheckSource(UniskinRig rig)
        {
            var path = AssetDatabase.GUIDToAssetPath(rig.sourceGuid);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("The UniskinRig source PSB is missing.");
            if (AssetDatabase.GetAssetDependencyHash(path).ToString() != rig.sourceDependencyHash)
            {
                // Dependency hashes include the importer and its dependencies and are not
                // portable between projects. Compare the actual source contract instead.
                var native = UniskinRigData.Read(NativeProvider(path), AssetImporter.GetAtPath(path).userData);
                var fileHash = SourceFileHash(path);
                if (native.Serialize() != rig.sourceBaseline ||
                    (!string.IsNullOrEmpty(rig.sourceFileHash) && fileHash != rig.sourceFileHash))
                    throw new InvalidOperationException("The PSB changed outside UniskinRig. Editing and bake are blocked to protect both versions. Reload from PSB after backing up the rig, or restore the source.");
                CaptureSourceHashes(rig, path);
                EditorUtility.SetDirty(rig);
                AssetDatabase.SaveAssetIfDirty(rig);
            }
            else if (string.IsNullOrEmpty(rig.sourceFileHash))
            {
                // Upgrade legacy rigs while their original dependency hash still matches.
                rig.sourceFileHash = SourceFileHash(path);
                EditorUtility.SetDirty(rig);
                AssetDatabase.SaveAssetIfDirty(rig);
            }
        }

        static string SourceFileHash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream));
        }

        static void CaptureSourceHashes(UniskinRig rig, string path)
        {
            rig.sourceDependencyHash = AssetDatabase.GetAssetDependencyHash(path).ToString();
            rig.sourceFileHash = SourceFileHash(path);
        }

        internal static void Save(UniskinRigDataProvider provider)
        {
            CheckSource(provider.rig);
            Validate(provider.data);
            var serialized = provider.data.Serialize();
            if (serialized == provider.rig.authoringData) return;
            var rig = provider.rig;
            var previous = rig.authoringData;
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Save UniskinRig");
            Undo.RegisterCompleteObjectUndo(rig, "Save UniskinRig");
            Undo.RegisterCompleteObjectUndo(rig.sprites.Select(entry => (Object)entry.sprite).ToArray(), "Save UniskinRig Sprites");
            try
            {
                rig.authoringData = serialized;
                RefreshSprites(rig, provider);
                UniskinClippingMaskUtility.Synchronize(rig, true);
                RefreshInstances(rig);
                EditorUtility.SetDirty(rig);
                AssetDatabase.SaveAssetIfDirty(rig);
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                rig.authoringData = previous;
                RefreshSprites(rig, new UniskinRigDataProvider(rig, NativeProvider(AssetDatabase.GUIDToAssetPath(rig.sourceGuid))));
                EditorUtility.SetDirty(rig);
                AssetDatabase.SaveAssetIfDirty(rig);
                throw;
            }
        }

        internal static void Validate(UniskinRigData data)
        {
            if (data.pixelsPerUnit <= 0 || !float.IsFinite(data.pixelsPerUnit)) throw new InvalidOperationException("Invalid rig pixels per unit.");
            if (data.sprites.Select(item => item.rect.spriteID).Distinct().Count() != data.sprites.Length)
                throw new InvalidOperationException("Duplicate Sprite IDs in the rig.");
            ValidateBones(data.hasCharacter ? data.character.bones : Array.Empty<SpriteBone>());
            foreach (var item in data.sprites)
            {
                ValidateBones(item.bones);
                if (item.vertices.Length == 0 && item.indices.Length == 0) continue;
                var mesh = new SpriteMeshData();
                mesh.SetFrame(item.rect.rect);
                foreach (var vertex in item.vertices)
                {
                    var weight = vertex.boneWeight;
                    for (var channel = 0; channel < 4; ++channel)
                        if (!float.IsFinite(weight.GetWeight(channel)) || weight.GetWeight(channel) < 0 ||
                            (weight.GetWeight(channel) > 0 && item.bones.Length > 0 &&
                            (weight.GetBoneIndex(channel) < 0 || weight.GetBoneIndex(channel) >= item.bones.Length)))
                            throw new InvalidOperationException("A rig weight references an invalid bone or value.");
                    mesh.AddVertex(vertex.position, weight);
                }
                mesh.SetIndices(item.indices);
                mesh.SetEdges(EditorUtilities.ToInt2(item.edges));
                SceneMeshSource.Validate(mesh);
            }
            if (!data.hasCharacter) return;
            foreach (var part in data.character.parts)
            {
                var item = data.sprites.Single(sprite => sprite.rect.spriteID.ToString() == part.spriteId);
                var map = part.bones ?? Array.Empty<int>();
                if (map.Length != item.bones.Length || map.Where((index, i) => index < 0 || index >= data.character.bones.Length ||
                    data.character.bones[index].guid != item.bones[i].guid).Any())
                    throw new InvalidOperationException("Character bindings do not match Sprite bone GUID order.");
            }
        }

        static void ValidateBones(SpriteBone[] bones)
        {
            var guids = new HashSet<string>();
            for (var i = 0; i < bones.Length; ++i)
            {
                var bone = bones[i];
                if (string.IsNullOrEmpty(bone.guid) || !guids.Add(bone.guid) || bone.parentId < -1 || bone.parentId >= bones.Length ||
                    !float.IsFinite(bone.position.x) || !float.IsFinite(bone.position.y) || !float.IsFinite(bone.position.z) ||
                    !float.IsFinite(bone.rotation.x) || !float.IsFinite(bone.rotation.y) || !float.IsFinite(bone.rotation.z) ||
                    !float.IsFinite(bone.rotation.w) || !float.IsFinite(bone.length) || bone.length < 0)
                    throw new InvalidOperationException("Invalid rig bone identity, hierarchy or pose.");
                var visited = new HashSet<int>();
                for (var index = i; index >= 0; index = bones[index].parentId)
                    if (index >= bones.Length || !visited.Add(index)) throw new InvalidOperationException("Cyclic rig bone hierarchy.");
            }
        }

        static void RefreshSprites(UniskinRig rig, UniskinRigDataProvider provider)
        {
            var data = provider.data;
            rig.pixelsPerUnit = data.pixelsPerUnit;
            rig.characterBones = data.hasCharacter ? data.character.bones : data.sprites.First().bones;
            rig.rootOffset = data.hasCharacter ? -Vector2.Scale(data.character.dimension, data.character.pivot) / data.pixelsPerUnit :
                -Vector2.Scale(data.sprites.First().rect.rect.size, data.sprites.First().rect.pivot) / data.pixelsPerUnit;
            SpritePostProcess.ApplyUniskinRigData(provider, rig.sprites.Select(entry => entry.sprite).ToArray());
            foreach (var entry in rig.sprites) RestoreTextureCoordinates(entry.source, entry.sprite);
            SceneMeshBindingPose.Apply(provider, rig.sprites.Select(entry => entry.sprite).ToArray(), data.userData);
            ++rig.revision;
            foreach (var entry in rig.sprites) EditorUtility.SetDirty(entry.sprite);
            EditorUtility.SetDirty(rig);
        }

        // Native mesh replacement/Undo can clear UV streams. Imports regenerate
        // them afterwards; editing persistent Sprite subassets does not.
        // Derive the atlas mapping from the imported Sprite, never from a generated
        // Sprite whose streams may already have been cleared by Save or Undo.
        internal static void RestoreTextureCoordinates(Sprite source, Sprite destination)
        {
            var positions = source.vertices;
            var uv = source.uv;
            if (positions.Length < 3 || uv.Length != positions.Length)
                throw new InvalidOperationException("The source Sprite has no usable texture coordinates.");
            var origin = positions[0];
            var axisIndex = 1;
            for (var i = 2; i < positions.Length; ++i)
                if ((positions[i] - origin).sqrMagnitude > (positions[axisIndex] - origin).sqrMagnitude) axisIndex = i;
            var axis = positions[axisIndex] - origin;
            var otherIndex = -1;
            var determinant = 0f;
            for (var i = 1; i < positions.Length; ++i)
            {
                var offset = positions[i] - origin;
                var cross = axis.x * offset.y - axis.y * offset.x;
                if (Mathf.Abs(cross) <= Mathf.Abs(determinant)) continue;
                otherIndex = i;
                determinant = cross;
            }
            if (otherIndex < 0)
                throw new InvalidOperationException("The source Sprite texture mapping is degenerate.");
            var other = positions[otherIndex] - origin;
            var uvAxis = uv[axisIndex] - uv[0];
            var uvOther = uv[otherIndex] - uv[0];
            var vertices = destination.GetVertexAttribute<Vector3>(VertexAttribute.Position);
            var coordinates = new NativeArray<Vector2>(vertices.Length, Allocator.Temp);
            try
            {
                for (var i = 0; i < vertices.Length; ++i)
                {
                    var offset = (Vector2)vertices[i] - origin;
                    var x = (offset.x * other.y - offset.y * other.x) / determinant;
                    var y = (axis.x * offset.y - axis.y * offset.x) / determinant;
                    coordinates[i] = uv[0] + x * uvAxis + y * uvOther;
                }
                destination.SetVertexAttribute<Vector2>(VertexAttribute.TexCoord0, coordinates);
            }
            finally { coordinates.Dispose(); }
        }

        internal static void RepairMissingTextureCoordinates()
        {
            if (s_Rigs == null) FindAtSource(string.Empty);
            foreach (var rig in s_Rigs.Values.Where(rig => rig != null && rig.editingEnabled))
            {
                try
                {
                    var repaired = false;
                    foreach (var entry in rig.sprites)
                    {
                        if (entry.source == null || entry.sprite == null) continue;
                        var uv = entry.sprite.uv;
                        if (entry.sprite.GetVertexCount() == 0 || uv.Any(point => point != Vector2.zero)) continue;
                        RestoreTextureCoordinates(entry.source, entry.sprite);
                        var provider = new UniskinRigDataProvider(rig, NativeProvider(AssetDatabase.GUIDToAssetPath(rig.sourceGuid)));
                        SceneMeshBindingPose.Apply(provider, new[] { entry.sprite }, provider.data.userData);
                        EditorUtility.SetDirty(entry.sprite);
                        repaired = true;
                    }
                    if (!repaired) continue;
                    foreach (var binding in Resources.FindObjectsOfTypeAll<UniskinRigBinding>().Where(binding => binding.rig == rig)) binding.Apply();
                    EditorUtility.SetDirty(rig);
                    AssetDatabase.SaveAssetIfDirty(rig);
                    SceneView.RepaintAll();
                }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        internal static bool CloseSceneTools(string path)
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<SceneEditMeshWindow>())
                if (window.editingAssetPath == path && !window.RequestClose()) return false;
            foreach (var window in Resources.FindObjectsOfTypeAll<SceneEditBoneWindow>())
                if (window.editingAssetPath == path && !window.RequestClose()) return false;
            foreach (var window in Resources.FindObjectsOfTypeAll<SceneEditWeightsWindow>())
                if (window.editingAssetPath == path && !window.RequestClose()) return false;
            return true;
        }

        internal static bool Bake(UniskinRig rig, ISpriteEditorDataProvider activeProvider = null)
        {
            if (rig == null || s_Baking) return false;
            var path = AssetDatabase.GUIDToAssetPath(rig.sourceGuid);
            if (!CloseSceneTools(path)) return false;
            if (activeProvider == null && SkinningModule.IsEditingAsset(path))
                throw new InvalidOperationException("Close Skinning Editor before baking the rig.");
            CheckSource(rig);
            if (!rig.hasUnbakedChanges) return true;
            var importer = AssetImporter.GetAtPath(path);
            var provider = activeProvider ?? NativeProvider(path);
            var desired = JsonUtility.FromJson<UniskinRigData>(rig.authoringData);
            Validate(desired);
            var before = UniskinRigData.Read(provider, importer.userData);
            if (before.Serialize() != rig.sourceBaseline)
                throw new InvalidOperationException("The source provider changed outside UniskinRig. Bake would overwrite those edits.");
            var backup = EditorJsonUtility.ToJson(importer);
            var previousBaked = rig.bakedData;
            var previousBaseline = rig.sourceBaseline;
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            s_Baking = true;
            try
            {
                Undo.RegisterCompleteObjectUndo(importer, "Bake UniskinRig to PSB");
                WriteToProvider(desired, provider);
                importer.userData = desired.userData;
                provider.Apply();
                importer.SaveAndReimport();
                var native = NativeProvider(path);
                var imported = UniskinRigData.Read(native, importer.userData);
                if (imported.Serialize() != desired.Serialize())
                    throw new InvalidOperationException("PSB bake verification failed: imported authoring data does not match the rig.");
                rig.bakedData = rig.authoringData;
                rig.sourceBaseline = imported.Serialize();
                CaptureSourceHashes(rig, path);
                foreach (var entry in rig.sprites)
                    entry.source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single(sprite => sprite.GetSpriteID().ToString() == entry.id);
                ReconcileBakedBones(rig);
                EditorUtility.SetDirty(rig);
                AssetDatabase.SaveAssetIfDirty(rig);
                RefreshInstances(rig);
                Undo.CollapseUndoOperations(undoGroup);
                return true;
            }
            catch
            {
                // Restore the importer contract; the rig remains authoritative and unbaked.
                EditorJsonUtility.FromJsonOverwrite(backup, importer);
                importer.SaveAndReimport();
                Undo.RevertAllDownToGroup(undoGroup);
                rig.bakedData = previousBaked;
                rig.sourceBaseline = previousBaseline;
                CaptureSourceHashes(rig, path);
                EditorUtility.SetDirty(rig);
                AssetDatabase.SaveAssetIfDirty(rig);
                throw;
            }
            finally { s_Baking = false; }
        }

        internal static void WriteToProvider(UniskinRigData data, ISpriteEditorDataProvider provider)
        {
            var mesh = provider.GetDataProvider<ISpriteMeshDataProvider>();
            var bones = provider.GetDataProvider<ISpriteBoneDataProvider>();
            foreach (var item in data.sprites)
            {
                var id = item.rect.spriteID;
                bones.SetBones(id, item.bones.ToList());
                mesh.SetVertices(id, item.vertices);
                mesh.SetIndices(id, item.indices);
                mesh.SetEdges(id, item.edges);
            }
            if (data.hasCharacter) provider.GetDataProvider<ICharacterDataProvider>().SetCharacterData(data.character);
        }

        internal static void PrepareSkinningEditor(ISpriteEditorDataProvider provider)
        {
            var rig = FindAtSource(AssetDatabase.GetAssetPath(provider.targetObject));
            if (rig == null || !rig.editingEnabled) return;
            if (!Bake(rig, provider)) throw new InvalidOperationException("Finish or cancel the active Scene tool before opening Skinning Editor.");
            provider.InitSpriteEditorDataProvider();
        }

        internal static void UsePsb(UniskinRig rig)
        {
            if (!Bake(rig)) return;
            Undo.RecordObject(rig, "Use PSB Skinning Data");
            rig.editingEnabled = false;
            foreach (var binding in Resources.FindObjectsOfTypeAll<UniskinRigBinding>().Where(binding => binding.rig == rig && !EditorUtility.IsPersistent(binding)))
            {
                foreach (var renderer in binding.renderers.Where(renderer => renderer != null))
                {
                    Undo.RecordObject(renderer, "Use PSB Sprite");
                    var skin = renderer.GetComponent<SpriteSkin>();
                    if (skin != null) Undo.RecordObject(skin, "Use PSB Skin");
                }
                binding.Apply();
                foreach (var renderer in binding.renderers)
                    if (renderer != null)
                    {
                        RecordSceneObject(renderer);
                        var skin = renderer.GetComponent<SpriteSkin>();
                        if (skin != null) RecordSceneObject(skin);
                    }
                if (!binding.GetComponentsInChildren<UniskinClippingMask>(true).Any(mask => mask.character == binding))
                    Undo.DestroyObjectImmediate(binding);
            }
            EditorUtility.SetDirty(rig);
            AssetDatabase.SaveAssetIfDirty(rig);
        }

        internal static void OpenSkinningEditor(UniskinRig rig)
        {
            if (!Bake(rig)) return;
            Selection.activeObject = rig.sprites.First().source;
            EditorApplication.ExecuteMenuItem("Window/2D/Sprite Editor");
        }

        internal static void CreateRigPrefab(UniskinRig rig)
        {
            if (!CloseSceneTools(AssetDatabase.GUIDToAssetPath(rig.sourceGuid))) return;
            RefreshInstances(rig);
            var binding = Resources.FindObjectsOfTypeAll<UniskinRigBinding>().FirstOrDefault(item =>
                item.rig == rig && !EditorUtility.IsPersistent(item) && item.gameObject.scene.IsValid());
            if (binding == null) throw new InvalidOperationException("Instantiate the PSB character in a scene and enable its rig first.");
            var path = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(AssetDatabase.GetAssetPath(rig), ".prefab"));
            var clone = Object.Instantiate(binding.gameObject);
            try
            {
                clone.name = binding.gameObject.name;
                PrefabUtility.SaveAsPrefabAsset(clone, path);
                EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(path));
            }
            finally { Object.DestroyImmediate(clone); }
        }

        internal static void Enable(UniskinRig rig)
        {
            if (!CloseSceneTools(AssetDatabase.GUIDToAssetPath(rig.sourceGuid))) return;
            if (!rig.editingEnabled) ReloadFromPsb(rig);
            else CheckSource(rig);
            Undo.RecordObject(rig, "Use UniskinRig");
            rig.editingEnabled = true;
            EditorUtility.SetDirty(rig);
            AssetDatabase.SaveAssetIfDirty(rig);
            RefreshInstances(rig);
        }

        internal static void ReloadFromPsb(UniskinRig rig)
        {
            var path = AssetDatabase.GUIDToAssetPath(rig.sourceGuid);
            if (!CloseSceneTools(path)) return;
            var provider = NativeProvider(path);
            var data = UniskinRigData.Read(provider, AssetImporter.GetAtPath(path).userData);
            if (!data.sprites.Select(item => item.rect.spriteID.ToString()).OrderBy(id => id)
                .SequenceEqual(rig.sprites.Select(entry => entry.id).OrderBy(id => id)))
                throw new InvalidOperationException("PSB layers changed. Create a new rig for this source after backing up the existing rig.");
            Undo.RegisterCompleteObjectUndo(rig, "Reload UniskinRig from PSB");
            foreach (var entry in rig.sprites)
            {
                entry.source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single(sprite => sprite.GetSpriteID().ToString() == entry.id);
                Undo.RegisterCompleteObjectUndo(entry.sprite, "Reload Rig Sprite");
                EditorUtility.CopySerialized(entry.source, entry.sprite);
            }
            rig.authoringData = rig.bakedData = rig.sourceBaseline = data.Serialize();
            CaptureSourceHashes(rig, path);
            RefreshSprites(rig, new UniskinRigDataProvider(rig, provider));
            UniskinClippingMaskUtility.Synchronize(rig, true);
            // Native prefab regeneration already applies pose changes to unmodified
            // imported Transforms. Advance their baseline so refresh does not add the
            // same delta twice; scene overrides still receive the authored delta.
            foreach (var binding in Resources.FindObjectsOfTypeAll<UniskinRigBinding>().Where(binding => binding.rig == rig && !EditorUtility.IsPersistent(binding)))
            {
                Undo.RecordObject(binding, "Reload Rig Bone Baselines");
                foreach (var link in binding.bones.Where(link => link.transform != null && PrefabUtility.IsPartOfPrefabInstance(link.transform) &&
                    !PrefabUtility.IsAddedGameObjectOverride(link.transform.gameObject)))
                {
                    var index = Array.FindIndex(rig.characterBones, bone => bone.guid == link.guid);
                    if (index < 0) continue;
                    var serializedTransform = new SerializedObject(link.transform);
                    var pose = rig.characterBones[index];
                    if (!serializedTransform.FindProperty("m_LocalPosition").prefabOverride) link.sourcePose.position = pose.position;
                    if (!serializedTransform.FindProperty("m_LocalRotation").prefabOverride) link.sourcePose.rotation = pose.rotation;
                    if (!serializedTransform.FindProperty("m_Father").prefabOverride) link.sourcePose.parentId = pose.parentId;
                }
            }
            AssetDatabase.SaveAssetIfDirty(rig);
            RefreshInstances(rig);
        }

        internal static void RefreshInstances(UniskinRig rig)
        {
            if (s_Refreshing || !rig.editingEnabled) return;
            s_Refreshing = true;
            try
            {
                var renderers = Resources.FindObjectsOfTypeAll<SpriteRenderer>().Where(renderer =>
                    renderer.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(renderer) && rig.Find(renderer.sprite) != null).ToArray();
                foreach (var group in renderers.GroupBy(renderer =>
                    PrefabUtility.GetNearestPrefabInstanceRoot(renderer.gameObject)?.transform ?? renderer.transform.root))
                {
                    var root = group.Key;
                    var binding = root.GetComponent<UniskinRigBinding>();
                    if (binding != null && binding.rig != rig)
                        throw new InvalidOperationException("Two PSB rigs share one scene root. Give each character its own root.");
                    if (binding != null && binding.appliedRevision == rig.revision && binding.renderers.SequenceEqual(group) &&
                        binding.bones.Length == rig.characterBones.Length && binding.bones.All(link => link.transform != null) &&
                        group.All(renderer => renderer.sprite == rig.Find(renderer.sprite).sprite)) continue;
                    if (binding == null) binding = Undo.AddComponent<UniskinRigBinding>(root.gameObject);
                    Undo.RecordObject(binding, "Update Rig Bindings");
                    binding.rig = rig;
                    binding.renderers = group.ToArray();
                    var map = binding.bones.Where(link => link.transform != null).ToDictionary(link => link.guid);
                    foreach (var renderer in group)
                    {
                        var skin = renderer.GetComponent<SpriteSkin>();
                        var oldBones = renderer.sprite.GetBones();
                        if (skin == null || skin.boneTransforms == null) continue;
                        for (var i = 0; i < Math.Min(oldBones.Length, skin.boneTransforms.Length); ++i)
                            if (skin.boneTransforms[i] != null && !map.ContainsKey(oldBones[i].guid))
                            {
                                var index = Array.FindIndex(rig.characterBones, bone => bone.guid == oldBones[i].guid);
                                if (index >= 0) map[oldBones[i].guid] = new UniskinRigBinding.BoneBinding
                                { guid = oldBones[i].guid, transform = skin.boneTransforms[i], sourcePose = rig.characterBones[index] };
                            }
                    }
                    var transforms = new Transform[rig.characterBones.Length];
                    for (var i = 0; i < transforms.Length; ++i) EnsureBone(i, rig, root, map, transforms, new HashSet<int>());
                    foreach (var obsolete in map.Values.Where(link => link.transform != null &&
                        !rig.characterBones.Any(bone => bone.guid == link.guid)))
                    {
                        foreach (var child in obsolete.transform.Cast<Transform>().ToArray())
                            Undo.SetTransformParent(child, obsolete.transform.parent, "Preserve Removed Bone Children");
                        Undo.DestroyObjectImmediate(obsolete.transform.gameObject);
                    }
                    binding.bones = rig.characterBones.Select(bone => map[bone.guid]).ToArray();
                    foreach (var renderer in group)
                    {
                        Undo.RecordObject(renderer, "Update Rig Sprite");
                        var skin = renderer.GetComponent<SpriteSkin>();
                        var entry = rig.Find(renderer.sprite);
                        if (skin == null && entry.sprite.GetBones().Length > 0) skin = Undo.AddComponent<SpriteSkin>(renderer.gameObject);
                        if (skin != null) Undo.RecordObject(skin, "Update Rig Skin");
                    }
                    binding.Apply();
                    RecordSceneObject(binding);
                    foreach (var renderer in group)
                    {
                        RecordSceneObject(renderer);
                        var skin = renderer.GetComponent<SpriteSkin>();
                        if (skin != null) RecordSceneObject(skin);
                    }
                }
            }
            finally { s_Refreshing = false; }
        }

        static Transform EnsureBone(int index, UniskinRig rig, Transform root,
            Dictionary<string, UniskinRigBinding.BoneBinding> map, Transform[] transforms, HashSet<int> stack)
        {
            if (transforms[index] != null) return transforms[index];
            if (!stack.Add(index)) throw new InvalidOperationException("Cyclic rig hierarchy.");
            var bone = rig.characterBones[index];
            var parent = bone.parentId >= 0 ? EnsureBone(bone.parentId, rig, root, map, transforms, stack) : root;
            if (!map.TryGetValue(bone.guid, out var link))
            {
                var existing = SceneBoneInspector.FindCharacterTransform(root, rig.characterBones, index,
                    candidate => candidate != root && candidate.GetComponent<SpriteRenderer>() == null);
                link = new UniskinRigBinding.BoneBinding { guid = bone.guid, transform = existing, sourcePose = bone };
                map.Add(bone.guid, link);
            }
            var created = link.transform == null;
            if (created)
            {
                var gameObject = new GameObject(bone.name);
                Undo.RegisterCreatedObjectUndo(gameObject, "Create Rig Bone");
                link.transform = gameObject.transform;
                link.created = true;
            }
            var transform = link.transform;
            var parentChanged = !created && transform.parent != parent;
            var changed = created || !link.sourcePose.position.Equals(bone.position) || !link.sourcePose.rotation.Equals(bone.rotation) ||
                parentChanged;
            if (changed)
            {
                Undo.RecordObject(transform, "Update Rig Bone");
                if (created) transform.SetParent(parent, false);
                else if (transform.parent != parent) Undo.SetTransformParent(transform, parent, "Reparent Rig Bone");
                var desired = bone.position / rig.pixelsPerUnit + (bone.parentId < 0 ? (Vector3)rig.rootOffset : Vector3.zero);
                if (created)
                {
                    transform.localPosition = desired;
                    transform.localRotation = bone.rotation;
                }
                else if (!parentChanged)
                {
                    transform.localPosition += (bone.position - link.sourcePose.position) / rig.pixelsPerUnit;
                    transform.localRotation = bone.rotation * Quaternion.Inverse(link.sourcePose.rotation) * transform.localRotation;
                }
                // Set Parent preserves the displayed world pose. Undo.SetTransformParent
                // has already converted it to the new parent's scene coordinates; the
                // source bind pose is in a different frame on a posed instance.
                RecordSceneObject(transform);
            }
            link.sourcePose = bone;
            transforms[index] = transform;
            stack.Remove(index);
            return transform;
        }

        static void ReconcileBakedBones(UniskinRig rig)
        {
            var path = AssetDatabase.GUIDToAssetPath(rig.sourceGuid);
            foreach (var binding in Resources.FindObjectsOfTypeAll<UniskinRigBinding>().Where(binding => binding.rig == rig && !EditorUtility.IsPersistent(binding)))
                foreach (var link in binding.bones.Where(link => link.created && link.transform != null))
                {
                    var index = Array.FindIndex(rig.characterBones, bone => bone.guid == link.guid);
                    if (index < 0) continue;
                    var duplicate = SceneBoneInspector.FindCharacterTransform(binding.transform, rig.characterBones, index, candidate =>
                        candidate != link.transform && AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(candidate)) == path);
                    if (duplicate == null) continue;
                    // Keep the authored Transform and all existing scene/animation references.
                    // Native regeneration introduces a duplicate only for newly added bones.
                    foreach (var child in duplicate.Cast<Transform>().ToArray())
                        Undo.SetTransformParent(child, link.transform, "Reconcile Baked Bone Children");
                    Undo.DestroyObjectImmediate(duplicate.gameObject);
                }
        }

        static void RecordSceneObject(Object obj)
        {
            EditorUtility.SetDirty(obj);
            if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            if (obj is Component component && component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }

        static void RefreshAfterUndo()
        {
            if (s_Baking) return;
            if (s_Rigs == null) FindAtSource(string.Empty);
            foreach (var rig in s_Rigs.Values.Where(rig => rig != null && rig.editingEnabled))
            {
                RefreshSprites(rig, new UniskinRigDataProvider(rig, NativeProvider(AssetDatabase.GUIDToAssetPath(rig.sourceGuid))));
                UniskinClippingMaskUtility.Synchronize(rig, false);
                foreach (var binding in Resources.FindObjectsOfTypeAll<UniskinRigBinding>().Where(binding => binding.rig == rig)) binding.Apply();
            }
        }

        static bool s_RefreshQueued;
        static void QueueInstanceRefresh()
        {
            if (s_RefreshQueued || s_Refreshing || s_Baking) return;
            s_RefreshQueued = true;
            EditorApplication.delayCall += () =>
            {
                s_RefreshQueued = false;
                if (s_Rigs == null) FindAtSource(string.Empty);
                foreach (var rig in s_Rigs.Values.Where(rig => rig != null && rig.editingEnabled))
                    try { RefreshInstances(rig); } catch (Exception exception) { Debug.LogException(exception); }
            };
        }

        internal static void InvalidateIndex() => s_Rigs = null;

        internal static void SourcesImported(string[] paths)
        {
            InvalidateIndex();
            if (s_Baking || !paths.Any(path => path.EndsWith(".psb", StringComparison.OrdinalIgnoreCase))) return;
            EditorApplication.delayCall += () =>
            {
                foreach (var path in paths.Where(path => path.EndsWith(".psb", StringComparison.OrdinalIgnoreCase)))
                {
                    var rig = FindAtSource(path);
                    if (rig == null || rig.hasUnbakedChanges || SceneEditMeshWindow.IsEditingAsset(path) ||
                        SceneEditBoneWindow.IsEditingAsset(path) || SceneEditWeightsWindow.IsEditingAsset(path)) continue;
                    try { ReloadFromPsb(rig); } catch (Exception exception) { Debug.LogException(exception); }
                }
            };
        }
    }

    internal sealed class UniskinRigSourcePostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) =>
            UniskinRigUtility.SourcesImported(imported);
    }
}
