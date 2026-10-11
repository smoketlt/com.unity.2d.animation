using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditorInternal;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Fedorov.Uniskin
{
    internal static class UniskinPsbImportCache
    {
        const string DirectoryName = "Library/Uniskin/PsbImportCache";
        const int Schema = 2;
        sealed class UnsupportedCacheException : Exception { }
        const string UrpLightPostprocessor = "UnityEditor.Rendering.Universal.Light2DPostProcess";
        static readonly Type LightPostprocessor = TypeCache.GetTypesDerivedFrom<AssetPostprocessor>()
            .FirstOrDefault(type => type.FullName == UrpLightPostprocessor);
        static readonly bool HasCustomTextureCallbacks = TypeCache.GetTypesDerivedFrom<AssetPostprocessor>()
            .Any(type => type != typeof(SpritePostProcess) && type != LightPostprocessor && type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(method => method.Name == "OnPostprocessSprites" || method.Name == "OnPostprocessTexture" || method.Name == "OnPreprocessTexture"));

        internal static bool TryImport(UniskinPsbImporter importer, AssetImportContext context)
        {
            Object[] loaded = null;
            bool registered = false;
            try
            {
                var key = Key(importer, context);
                var path = CachePath(importer);
                if (!File.Exists(path))
                    return false;
                loaded = InternalEditorUtility.LoadSerializedFileAndForget(path);
                var archive = loaded.OfType<UniskinPsbCachedAssets>().Single();
                if (archive.key != key)
                {
                    foreach (var value in loaded)
                        if (value != null && !EditorUtility.IsPersistent(value)) Object.DestroyImmediate(value);
                    return false;
                }
                if (archive.main == null || archive.assets == null || archive.identifiers == null ||
                    archive.assets.Length != archive.identifiers.Length || archive.assets.Any(asset => asset == null) ||
                    archive.identifiers.Distinct().Count() != archive.identifiers.Length)
                    throw new InvalidDataException("Incomplete PSB cache archive.");
                UpdateSprites(importer, archive.assets.OfType<Sprite>().ToArray());
                // URP's optional callback refreshes Sprite lights. Keep that notification
                // without introducing a hard URP dependency into the Animation package.
                if (LightPostprocessor != null)
                    LightPostprocessor.GetMethod("OnPostprocessSprites", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                        null, new[] { typeof(Texture2D), typeof(Sprite[]) }, null)
                        .Invoke(Activator.CreateInstance(LightPostprocessor, true), new object[]
                        { archive.assets.OfType<Texture2D>().Single(), archive.assets.OfType<Sprite>().ToArray() });
                // Complete all validation before registering anything with this import.
                registered = true;
                for (var i = 0; i < archive.assets.Length; ++i)
                    context.AddObjectToAsset(archive.identifiers[i], archive.assets[i]);
                context.SetMainObject(archive.main);
                Object.DestroyImmediate(archive);
                return true;
            }
            catch (Exception ex)
            {
                if (registered) throw;
                if (!(ex is UnsupportedCacheException))
                    Debug.LogWarning("Uniskin PSB cache was unavailable; using full import. " + ex.Message, importer);
                if (loaded != null)
                    foreach (var value in loaded)
                        if (value != null && !EditorUtility.IsPersistent(value)) Object.DestroyImmediate(value);
                return false;
            }
        }

        internal static void Store(UniskinPsbImporter importer, AssetImportContext context)
        {
            UniskinPsbCachedAssets archive = null;
            try
            {
                var assets = new List<Object>();
                context.GetObjects(assets);
                if (context.mainObject == null || !assets.OfType<Sprite>().Any()) return;
                using (var settings = new SerializedObject(importer))
                {
                    archive = ScriptableObject.CreateInstance<UniskinPsbCachedAssets>();
                    archive.assets = assets.ToArray();
                    archive.identifiers = assets.Select(asset => Identifier(asset, settings)).ToArray();
                    archive.main = context.mainObject;
                    archive.key = Key(importer, context);
                }
                if (archive.identifiers.Any(string.IsNullOrEmpty) || archive.identifiers.Distinct().Count() != assets.Count)
                    return; // A future upstream output type requires an explicit cache adapter.
                var objects = new HashSet<Object>(assets) { archive };
                foreach (var root in assets.OfType<GameObject>())
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        objects.Add(transform.gameObject);
                        foreach (var component in transform.GetComponents<Component>())
                            if (component != null) objects.Add(component);
                    }
                Directory.CreateDirectory(DirectoryName);
                var path = CachePath(importer);
                InternalEditorUtility.SaveToSerializedFileAndForget(objects.ToArray(), path + ".tmp", false);
                if (File.Exists(path)) File.Delete(path);
                File.Move(path + ".tmp", path);
            }
            catch (Exception ex)
            {
                // Cache failures never invalidate a successful native import.
                if (!(ex is UnsupportedCacheException))
                    Debug.LogWarning("Uniskin could not cache this PSB import. " + ex.Message, importer);
            }
            finally { if (archive != null) Object.DestroyImmediate(archive); }
        }

        static string CachePath(AssetImporter importer) => DirectoryName + "/" + AssetDatabase.AssetPathToGUID(importer.assetPath) + ".bin";

        static string Identifier(Object asset, SerializedObject settings)
        {
            if (asset is Sprite sprite) return SceneSpriteIdentity.GetId(sprite).ToString();
            if (asset is Texture2D) return Name(settings, "m_TextureAssetName", "Texture");
            if (asset is GameObject) return Name(settings, "m_PrefabAssetName", "Prefab");
            switch (asset.GetType().FullName)
            {
                case "UnityEditor.U2D.PSD.PSDImportData": return "PSDImportData";
                case "UnityEngine.U2D.Animation.SkeletonAsset": return Name(settings, "m_SkeletonAssetName", "SkeletonAsset");
                case "UnityEngine.U2D.Animation.SpriteLibraryAsset": return Name(settings, "m_SpriteLibAssetName", "SpriteLibAsset");
                default: return null;
            }
        }

        static string Name(SerializedObject settings, string propertyName, string fallback)
        {
            var value = settings.FindProperty(propertyName)?.stringValue;
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        static string Key(UniskinPsbImporter importer, AssetImportContext context)
        {
            if (HasCustomTextureCallbacks) throw new UnsupportedCacheException();
            var text = new StringBuilder().Append(Schema).Append(Application.unityVersion)
                .Append(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PSDImporter).Assembly)?.version)
                .Append(typeof(PSDImporter).Module.ModuleVersionId)
                .Append(typeof(UniskinPsbImportCache).Module.ModuleVersionId)
                .Append(typeof(SpritePostProcess).Module.ModuleVersionId)
                .Append(LightPostprocessor?.Module.ModuleVersionId)
                .Append(context.selectedBuildTarget);
            using (var stream = File.OpenRead(context.assetPath))
            using (var hash = SHA256.Create()) text.Append(Convert.ToBase64String(hash.ComputeHash(stream)));
            using (var settings = new SerializedObject(importer))
            {
                var property = settings.GetIterator();
                bool enter = true;
                while (property.Next(enter))
                {
                    enter = true;
                    // The script already participates in Unity's importer dependency tracking.
                    // Adding an artifact dependency on its own script can restart the import.
                    if (property.name == "m_Script") { enter = false; continue; }
                    // Only geometry payloads may differ on the cached path. Frame, bones,
                    // hierarchy, outlines, packing, texture/platform settings stay in the key.
                    if (property.name == "vertices" || property.name == "indices" || property.name == "edges")
                    {
                        if (property.name != "edges")
                            text.Append(property.propertyPath).Append(property.arraySize >= 3);
                        enter = false;
                        continue;
                    }
                    if (property.propertyType == SerializedPropertyType.Generic) continue;
                    text.Append(property.propertyPath).Append(':');
                    if (property.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        var value = property.objectReferenceValue;
                        var path = AssetDatabase.GetAssetPath(value);
                        if (value != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id))
                            text.Append(guid).Append(':').Append(id);
                        text.Append(path);
                        if (!string.IsNullOrEmpty(path) && path != context.assetPath)
                        {
                            // External outputs can change through their own importers without
                            // changes to their source bytes. Keep those PSBs on the native path.
                            throw new UnsupportedCacheException();
                        }
                    }
                    else
                    {
                        text.Append(property.contentHash);
                        if (property.propertyType == SerializedPropertyType.String && property.stringValue.Length == 32)
                        {
                            var path = AssetDatabase.GUIDToAssetPath(property.stringValue);
                            if (!string.IsNullOrEmpty(path) && path != context.assetPath)
                            {
                                throw new UnsupportedCacheException();
                            }
                        }
                    }
                }
            }
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }

        static void UpdateSprites(UniskinPsbImporter importer, Sprite[] sprites)
        {
            var provider = (ISpriteEditorDataProvider)importer;
            provider.InitSpriteEditorDataProvider();
            if (!provider.HasDataProvider(typeof(ISpriteMeshDataProvider))) throw new InvalidOperationException("No mesh provider.");
            var mesh = provider.GetDataProvider<ISpriteMeshDataProvider>();
            if (mesh == null) throw new InvalidOperationException("No mesh provider.");
            var rects = provider.GetSpriteRects();
            if (rects.Length != sprites.Length) throw new InvalidDataException("Cached Sprite count changed.");
            foreach (var sprite in sprites)
            {
                var rect = rects.Single(rect => rect.spriteID == SceneSpriteIdentity.GetId(sprite));
                var metadata = mesh.GetVertices(rect.spriteID);
                if (metadata == null || metadata.Length == 0) continue;
                var indices = mesh.GetIndices(rect.spriteID);
                if (indices == null || indices.Length == 0 || indices.Length % 3 != 0 ||
                    metadata.Length > ushort.MaxValue || indices.Any(index => index < 0 || index >= metadata.Length))
                    throw new InvalidDataException("Invalid cached Sprite geometry.");
            }
            SpritePostProcess.ApplyCachedMeshData(provider, sprites, importer);
        }
    }
}
