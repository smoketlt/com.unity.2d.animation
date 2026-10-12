using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnityEngine.UIElements;

// Unity treats this fork's Unity.* assembly as a built-in assembly and sorts its
// inspectors by full type name. A UnityEditor.U2D name loses to the native
// UnityEditor.SpriteRendererEditor. Keep the extension in the product namespace.
namespace Fedorov.Uniskin
{
    [CustomEditor(typeof(SpriteRenderer)), CanEditMultipleObjects]
    internal sealed class UniskinSpriteRendererEditor : Editor
    {
        Editor m_NativeEditor;
        MethodInfo m_NativeSceneGUI;
        Sprite m_CachedSprite;
        Hash128 m_CachedHash;
        bool m_HasMesh;
        bool m_Supported;
        int m_CachedRigRevision = -1;

        void OnEnable()
        {
            // Delegate Unity's complete inspector, including tiling, rendering and sorting.
            var nativeType = typeof(Editor).Assembly.GetType("UnityEditor.SpriteRendererEditor");
            if (nativeType == null) return;
            m_NativeEditor = CreateEditor(targets, nativeType);
            for (var type = nativeType; type != null && m_NativeSceneGUI == null; type = type.BaseType)
                m_NativeSceneGUI = type.GetMethod("OnSceneGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        }

        void OnDisable()
        {
            if (m_NativeEditor != null) DestroyImmediate(m_NativeEditor);
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new IMGUIContainer(() =>
            {
                if (m_NativeEditor != null) m_NativeEditor.OnInspectorGUI();
                else DrawDefaultInspector();
            }));
            var button = new Button { name = "uniskinEditMesh" };
            var icon = new Image { pickingMode = PickingMode.Ignore };
            var label = new Label { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("uniskin-inspector-icon");
            button.Insert(0, icon);
            button.Add(label);
            var style = ResourceLoader.Load<StyleSheet>("SkinningModule/SceneMeshInspector.uss");
            if (style != null) root.styleSheets.Add(style);
            button.clicked += () =>
            {
                var renderer = target as SpriteRenderer;
                if (renderer == null || renderer.sprite == null) return;
                try
                {
                    var create = !new SceneMeshSource(renderer.sprite).hasAuthoredMesh;
                    EditorApplication.delayCall += () =>
                    {
                        if (renderer != null) SceneEditMeshWindow.OpenForRenderer(renderer, create);
                    };
                }
                catch (Exception ex) { EditorUtility.DisplayDialog("Edit Mesh", ex.Message, "OK"); }
            };
            root.Add(button);
            var boneButton = new Button(() =>
            {
                var renderer = target as SpriteRenderer;
                EditorApplication.delayCall += () => { if (renderer != null) SceneEditBoneWindow.OpenForRenderer(renderer); };
            }) { name = "uniskinEditBone" };
            var boneIcon = new Image { image = ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/EditBone.png"), pickingMode = PickingMode.Ignore };
            boneIcon.AddToClassList("uniskin-inspector-icon");
            boneButton.Insert(0, boneIcon);
            boneButton.Add(new Label("Edit Bone") { pickingMode = PickingMode.Ignore });
            root.Add(boneButton);
            var weightsButton = new Button(() =>
            {
                var renderer = target as SpriteRenderer;
                EditorApplication.delayCall += () => { if (renderer != null) SceneEditWeightsWindow.OpenForRenderer(renderer); };
            }) { name = "uniskinEditWeights" };
            var weightsIcon = new Image { image = ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/Weights.png"), pickingMode = PickingMode.Ignore };
            weightsIcon.AddToClassList("uniskin-inspector-icon");
            weightsButton.Add(weightsIcon);
            weightsButton.Add(new Label("Edit Weights") { pickingMode = PickingMode.Ignore });
            root.Add(weightsButton);
            var rigButton = new Button(() => RunRigAction(renderer =>
            {
                var rig = UniskinRigUtility.Find(renderer.sprite) ?? UniskinRigUtility.Create(renderer.sprite);
                if (rig != null) { UniskinRigUtility.Enable(rig); EditorGUIUtility.PingObject(rig); }
            })) { name = "uniskinUseRig", text = "Create UniskinRig" };
            var bakeButton = new Button(() => RunRigAction(renderer => UniskinRigUtility.Bake(UniskinRigUtility.Find(renderer.sprite))))
                { name = "uniskinBakeRig", text = "Bake to PSB" };
            var psbButton = new Button(() => RunRigAction(renderer => UniskinRigUtility.UsePsb(UniskinRigUtility.Find(renderer.sprite))))
                { name = "uniskinUsePsb", text = "Bake and Use PSB" };
            var skinningButton = new Button(() => RunRigAction(renderer => UniskinRigUtility.OpenSkinningEditor(UniskinRigUtility.Find(renderer.sprite))))
                { name = "uniskinOpenSkinning", text = "Skinning Editor (PSB)" };
            root.Add(rigButton); root.Add(bakeButton); root.Add(psbButton); root.Add(skinningButton);
            foreach (var action in new[] { rigButton, bakeButton, psbButton, skinningButton }) action.AddToClassList("uniskin-rig-action");
            void Refresh()
            {
                var renderer = target as SpriteRenderer;
                var sprite = renderer != null ? renderer.sprite : null;
                var path = sprite != null ? AssetDatabase.GetAssetPath(sprite) : null;
                var hash = !string.IsNullOrEmpty(path) ? AssetDatabase.GetAssetDependencyHash(path) : default;
                var rig = UniskinRigUtility.Find(sprite);
                var revision = rig != null ? rig.revision : -1;
                if (sprite != m_CachedSprite || hash != m_CachedHash || revision != m_CachedRigRevision)
                {
                    m_CachedRigRevision = revision;
                    m_CachedSprite = sprite;
                    m_CachedHash = hash;
                    m_HasMesh = m_Supported = false;
                    if (sprite != null)
                        try { m_HasMesh = new SceneMeshSource(sprite).hasAuthoredMesh; m_Supported = true; }
                        catch (Exception) { /* Unsupported sprites retain Unity's inspector. */ }
                }
                var edit = m_HasMesh || SceneEditMeshWindow.IsEditingRenderer(renderer);
                label.text = edit ? "Edit Mesh" : "Create Mesh";
                button.SetEnabled(targets.Length == 1 && m_Supported && !EditorApplication.isPlayingOrWillChangePlaymode);
                boneButton.SetEnabled(targets.Length == 1 && m_Supported && !EditorApplication.isPlayingOrWillChangePlaymode);
                weightsButton.SetEnabled(targets.Length == 1 && m_Supported && m_HasMesh && !EditorApplication.isPlayingOrWillChangePlaymode);
                var psb = sprite != null && UniskinRigUtility.SourcePath(sprite).EndsWith(".psb", StringComparison.OrdinalIgnoreCase);
                rigButton.text = rig == null ? "Create UniskinRig" : "Use UniskinRig";
                rigButton.style.display = psb && (rig == null || !rig.editingEnabled) ? DisplayStyle.Flex : DisplayStyle.None;
                rigButton.SetEnabled(targets.Length == 1 && m_Supported && !EditorApplication.isPlayingOrWillChangePlaymode);
                bakeButton.style.display = psb && rig != null ? DisplayStyle.Flex : DisplayStyle.None;
                psbButton.style.display = skinningButton.style.display = psb && rig != null && rig.editingEnabled ? DisplayStyle.Flex : DisplayStyle.None;
                bakeButton.SetEnabled(targets.Length == 1 && rig != null && rig.hasUnbakedChanges && !EditorApplication.isPlayingOrWillChangePlaymode);
                psbButton.SetEnabled(targets.Length == 1 && !EditorApplication.isPlayingOrWillChangePlaymode);
                skinningButton.SetEnabled(targets.Length == 1 && !EditorApplication.isPlayingOrWillChangePlaymode);
                icon.image = ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/" + (edit ? "EditMesh" : "CreateMesh") + ".png");
            }
            root.schedule.Execute(Refresh).Every(200);
            Refresh();
            return root;
        }

        void RunRigAction(Action<SpriteRenderer> action)
        {
            var renderer = target as SpriteRenderer;
            EditorApplication.delayCall += () =>
            {
                if (renderer == null || renderer.sprite == null) return;
                try { action(renderer); }
                catch (Exception exception) { EditorUtility.DisplayDialog("UniskinRig", exception.Message, "OK"); }
            };
        }

        void OnSceneGUI()
        {
            if (m_NativeEditor != null && !SceneEditMeshWindow.IsEditingRenderer(target as SpriteRenderer) && !SceneEditBoneWindow.IsEditingRenderer(target as SpriteRenderer) && !SceneEditWeightsWindow.IsEditingRenderer(target as SpriteRenderer))
                m_NativeSceneGUI?.Invoke(m_NativeEditor, null);
        }

        public override bool HasPreviewGUI() => m_NativeEditor != null && m_NativeEditor.HasPreviewGUI();
        public override void OnPreviewGUI(Rect rect, GUIStyle background) => m_NativeEditor?.OnPreviewGUI(rect, background);
        public override void OnInteractivePreviewGUI(Rect rect, GUIStyle background) => m_NativeEditor?.OnInteractivePreviewGUI(rect, background);
        public override void OnPreviewSettings() => m_NativeEditor?.OnPreviewSettings();
        public override GUIContent GetPreviewTitle() => m_NativeEditor != null ? m_NativeEditor.GetPreviewTitle() : base.GetPreviewTitle();
    }
}
