using System;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Fedorov.Uniskin
{
    public static class UniskinPsbImporterUtility
    {
        const string Menu = "Assets/Fedorov/Uniskin/PSB Importer/";

        [MenuItem(Menu + "Use Uniskin PSB Importer")]
        static void UseUniskinSelection() => SwitchFromUI(SelectedPaths(), true);
        [MenuItem(Menu + "Use Unity PSD Importer")]
        static void UseUnitySelection() => SwitchFromUI(SelectedPaths(), false);
        [MenuItem(Menu + "Use Uniskin PSB Importer", true)]
        static bool ValidateUniskin() => CanSwitchSelection(true);
        [MenuItem(Menu + "Use Unity PSD Importer", true)]
        static bool ValidateUnity() => CanSwitchSelection(false);

        static string[] SelectedPaths() => Selection.objects.Select(AssetDatabase.GetAssetPath).Distinct().ToArray();
        static bool CanSwitchSelection(bool uniskin)
        {
            var paths = SelectedPaths();
            return !EditorApplication.isPlayingOrWillChangePlaymode && paths.Length > 0 &&
                paths.All(path => IsSupported(path) &&
                    AssetImporter.GetAtPath(path) is UniskinPsbImporter != uniskin);
        }

        public static bool IsSupported(string path) => !string.IsNullOrEmpty(path) &&
            path.StartsWith("Assets/", StringComparison.Ordinal) &&
            path.EndsWith(".psb", StringComparison.OrdinalIgnoreCase) &&
            AssetImporter.GetAtPath(path) is PSDImporter;

        public static void UseUniskin(string path) => Switch(path, true);
        public static void UseUnity(string path) => Switch(path, false);

        internal static void SwitchFromUI(string[] paths, bool uniskin)
        {
            try
            {
                foreach (var path in paths) Switch(path, uniskin);
            }
            catch (Exception ex) { EditorUtility.DisplayDialog("PSB Importer", ex.Message, "OK"); }
        }

        static void Switch(string path, bool uniskin)
        {
            if (!IsSupported(path)) throw new InvalidOperationException("Select a PSB asset imported by Unity or Uniskin.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Switch PSB importers in Edit Mode.");
            if (AssetImporter.GetAtPath(path) is UniskinPsbImporter == uniskin) return;
            if (SkinningModule.IsEditingAsset(path) || SceneEditMeshWindow.IsEditingAsset(path))
                throw new InvalidOperationException("Close Edit Mesh and Skinning Editor for this PSB before switching importers.");
            foreach (var editor in ActiveEditorTracker.sharedTracker.activeEditors.OfType<AssetImporterEditor>())
                if (editor.targets.OfType<AssetImporter>().Any(importer => importer.assetPath == path) && editor.HasModified())
                    throw new InvalidOperationException("Apply or Revert the PSB import settings before switching importers.");

            var source = AssetImporter.GetAtPath(path);
            var originalWasUniskin = source is UniskinPsbImporter;
            var before = CaptureState(path);
            try
            {
                Activate(path, uniskin, source);
                if (CaptureState(path) != before)
                    throw new InvalidOperationException("Switching changed Sprite data or asset identifiers.");
            }
            catch (Exception failure)
            {
                try { Activate(path, originalWasUniskin, source); }
                catch (Exception rollback)
                {
                    throw new AggregateException("PSB importer switching and restoration failed.", failure, rollback);
                }
                throw new InvalidOperationException("Could not switch the PSB importer. The original importer and settings were restored.", failure);
            }
        }

        static void Activate(string path, bool uniskin, AssetImporter source)
        {
            if (uniskin) AssetDatabase.SetImporterOverride<UniskinPsbImporter>(path);
            else AssetDatabase.ClearImporterOverride(path);
            var destination = AssetImporter.GetAtPath(path);
            if (destination is UniskinPsbImporter != uniskin)
                throw new InvalidOperationException("Unity did not activate the requested PSB importer.");
            // Both types share PSDImporter's managed serialization contract. Native
            // importer identity (including m_Script) must stay with the destination.
            EditorUtility.CopySerializedManagedFieldsOnly(source, destination);
            EditorUtility.SetDirty(destination);
            destination.SaveAndReimport();
        }

        internal static string CaptureState(string path)
        {
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(AssetImporter.GetAtPath(path));
            if (provider == null || !provider.HasDataProvider(typeof(ISpriteMeshDataProvider)) ||
                !provider.HasDataProvider(typeof(ISpriteBoneDataProvider)))
                throw new InvalidOperationException("The PSB does not provide Sprite mesh and bone data.");
            provider.InitSpriteEditorDataProvider();
            var mesh = provider.GetDataProvider<ISpriteMeshDataProvider>();
            var bones = provider.GetDataProvider<ISpriteBoneDataProvider>();
            if (mesh == null || bones == null) throw new InvalidOperationException("The Sprite data providers are unavailable.");
            var state = new System.Text.StringBuilder();
            foreach (var rect in provider.GetSpriteRects().OrderBy(rect => rect.spriteID.ToString()))
                state.Append(JsonUtility.ToJson(new SpriteState
                {
                    rect = rect, vertices = mesh.GetVertices(rect.spriteID), indices = mesh.GetIndices(rect.spriteID),
                    edges = mesh.GetEdges(rect.spriteID), bones = bones.GetBones(rect.spriteID)?.ToArray()
                }));
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path).OrderBy(asset => asset.GetType().FullName + asset.name))
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id);
                state.Append(asset.GetType().FullName).Append(':').Append(asset.name).Append(':').Append(id).Append(';');
            }
            return state.ToString();
        }

        [Serializable]
        sealed class SpriteState
        {
            public SpriteRect rect;
            public Vertex2DMetaData[] vertices;
            public int[] indices;
            public Vector2Int[] edges;
            public UnityEngine.U2D.SpriteBone[] bones;
        }
    }
}
