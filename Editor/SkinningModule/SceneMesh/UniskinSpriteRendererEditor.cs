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
        string m_SupportError;
        int m_CachedRigRevision = -1;
        readonly SceneSpriteWireframe m_Wireframe = new SceneSpriteWireframe();

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
            boneButton.Add(new Label("Edit Bones") { pickingMode = PickingMode.Ignore });
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
            var rigControls = new UniskinRigInspectorControls(() => (target as SpriteRenderer)?.sprite);
            root.Add(rigControls);
            var supportMessage = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            root.Add(supportMessage);
            root.Add(new SceneSpriteMeshInfo(() => targets));
            void Refresh()
            {
                var renderer = target as SpriteRenderer;
                var sprite = renderer != null ? renderer.sprite : null;
                RefreshMeshCache(sprite);
                var clipping = UniskinClippingMaskUtility.Find(sprite);
                var edit = m_HasMesh || SceneEditMeshWindow.IsEditingRenderer(renderer);
                label.text = edit ? "Edit Mesh" : "Create Mesh";
                button.SetEnabled(targets.Length == 1 && m_Supported && !EditorApplication.isPlayingOrWillChangePlaymode);
                boneButton.SetEnabled(targets.Length == 1 && m_Supported && clipping == null && !EditorApplication.isPlayingOrWillChangePlaymode);
                weightsButton.SetEnabled(targets.Length == 1 && m_Supported && m_HasMesh && !EditorApplication.isPlayingOrWillChangePlaymode);
                if (SceneSkinningSuspension.active)
                {
                    button.SetEnabled(false); boneButton.SetEnabled(false); weightsButton.SetEnabled(false);
                }
                rigControls.Refresh(sprite, targets.Length == 1 && m_Supported);
                rigControls.SetEnabled(!SceneSkinningSuspension.active);
                supportMessage.text = m_SupportError ?? string.Empty;
                supportMessage.style.display = string.IsNullOrEmpty(m_SupportError) ? DisplayStyle.None : DisplayStyle.Flex;
                icon.image = ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/" + (edit ? "EditMesh" : "CreateMesh") + ".png");
            }
            root.schedule.Execute(Refresh).Every(200);
            Refresh();
            return root;
        }

        void RefreshMeshCache(Sprite sprite)
        {
            var path = sprite != null ? AssetDatabase.GetAssetPath(sprite) : null;
            var hash = !string.IsNullOrEmpty(path) ? AssetDatabase.GetAssetDependencyHash(path) : default;
            var rig = UniskinRigUtility.Find(sprite);
            var clipping = UniskinClippingMaskUtility.Find(sprite);
            var revision = clipping != null ? clipping.revision : rig != null ? rig.revision : -1;
            if (sprite == m_CachedSprite && hash == m_CachedHash && revision == m_CachedRigRevision) return;
            m_CachedRigRevision = revision;
            m_CachedSprite = sprite;
            m_CachedHash = hash;
            m_HasMesh = m_Supported = false;
            m_SupportError = null;
            if (sprite != null)
                try { m_HasMesh = new SceneMeshSource(sprite).hasAuthoredMesh; m_Supported = true; }
                catch (Exception ex) { m_SupportError = ex.Message; }
        }

        void OnSceneGUI()
        {
            var renderer = target as SpriteRenderer;
            if (SceneEditMeshWindow.IsEditingRenderer(renderer) ||
                SceneEditWeightsWindow.IsEditingRenderer(renderer)) return;
            // Bone editing keeps the live renderer and its selected-mesh wireframe.
            // Only native sprite handles compete with the popup's bone controls.
            if (!SceneEditBoneWindow.IsEditingRenderer(renderer) && m_NativeEditor != null)
                m_NativeSceneGUI?.Invoke(m_NativeEditor, null);
            if (Event.current.type == EventType.Repaint && renderer != null)
            {
                RefreshMeshCache(renderer.sprite);
                m_Wireframe.Draw(renderer, !m_Supported || m_HasMesh);
            }
        }

        public override bool HasPreviewGUI() => m_NativeEditor != null && m_NativeEditor.HasPreviewGUI();
        public override void OnPreviewGUI(Rect rect, GUIStyle background) => m_NativeEditor?.OnPreviewGUI(rect, background);
        public override void OnInteractivePreviewGUI(Rect rect, GUIStyle background) => m_NativeEditor?.OnInteractivePreviewGUI(rect, background);
        public override void OnPreviewSettings() => m_NativeEditor?.OnPreviewSettings();
        public override GUIContent GetPreviewTitle() => m_NativeEditor != null ? m_NativeEditor.GetPreviewTitle() : base.GetPreviewTitle();
    }
}
