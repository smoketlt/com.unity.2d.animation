using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fedorov.Uniskin
{
    [CustomEditor(typeof(UniskinPsbImporter)), CanEditMultipleObjects]
    public sealed class UniskinPsbImporterEditor : UniskinCompatiblePsdImporterEditor
    {
    }

    [CustomEditor(typeof(PSDImporter)), CanEditMultipleObjects]
    public class UniskinCompatiblePsdImporterEditor : PSDImporterEditor
    {
        bool IsPsbSelection() => System.Array.TrueForAll(targets, value =>
            value is AssetImporter importer && UniskinPsbImporterUtility.IsSupported(importer.assetPath));

        protected override void OnHeaderGUI()
        {
            if (!IsPsbSelection()) { base.OnHeaderGUI(); return; }
            // Unity's built-in importer dropdown discards settings when changing types.
            // Offer the preserving switch below instead of exposing that destructive path.
            using (new EditorGUILayout.HorizontalScope())
            {
                var importer = (AssetImporter)target;
                var asset = AssetDatabase.LoadMainAssetAtPath(importer.assetPath);
                GUILayout.Label(AssetPreview.GetMiniThumbnail(asset), GUILayout.Width(32), GUILayout.Height(32));
                GUILayout.Label(targets.Length == 1 ? (asset != null ? asset.name : System.IO.Path.GetFileNameWithoutExtension(importer.assetPath)) : "PSB Assets", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (targets.Length == 1 && asset != null && GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(50)))
                    AssetDatabase.OpenAsset(asset);
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            if (!IsPsbSelection()) return base.CreateInspectorGUI();
            var uniskin = target is UniskinPsbImporter;
            var root = new VisualElement();
            root.Add(new Label(uniskin ? "PSB Importer: Uniskin" : "PSB Importer: Unity"));
            var restore = new Button(() =>
            {
                var paths = System.Array.ConvertAll(targets, value => ((AssetImporter)value).assetPath);
                // Defer until the Inspector event completes: switching destroys its target.
                EditorApplication.delayCall += () => UniskinPsbImporterUtility.SwitchFromUI(paths, !uniskin);
            }) { text = uniskin ? "Use Unity PSD Importer" : "Use Uniskin PSB Importer", name = "uniskinSwitchPsbImporter" };
            root.Add(restore);
            root.Add(base.CreateInspectorGUI());
            return root;
        }
    }
}
