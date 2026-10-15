using System.Linq;
using UnityEngine.U2D.Animation;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace UnityEditor.U2D.Animation
{
    [CustomEditor(typeof(UniskinRigBinding)), CanEditMultipleObjects]
    internal sealed class UniskinRigBindingEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var script = new ObjectField("Script") { value = MonoScript.FromMonoBehaviour((UniskinRigBinding)target) };
            script.SetEnabled(false);
            root.Add(script);
            root.Add(new PropertyField(serializedObject.FindProperty("rig")));
            var showBones = serializedObject.FindProperty("alwaysShowBones");
            root.Add(new PropertyField(showBones, "Always Show Bones"));
            root.TrackPropertyValue(showBones, _ => BoneGizmo.instance.boneGizmoController.OnSelectionChanged());
            var mode = new EnumField("GPU Skinning (Project)", PlayerSettings.meshDeformation)
            {
                name = "projectGpuSkinning",
                tooltip = "Player > Other Settings > GPU Skinning / Mesh Deformation. Global project setting affecting all characters. GPU Batched is the equivalent of enabling the legacy GPU Skinning checkbox."
            };
            mode.RegisterValueChangedCallback(evt =>
            {
                PlayerSettings.meshDeformation = (MeshDeformation)evt.newValue;
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            });
            root.Add(mode);
            var actual = new Label();
            root.Add(actual);
            root.Add(new HelpBox("This changes the global Player setting for the whole project. GPU Batched corresponds to enabling GPU Skinning. SpriteSkin GPU deformation also requires URP, SRP Batcher, platform support and a compatible shader. Always Update controls offscreen updates; Force CPU Deformation adds a CPU copy when using GPU.", HelpBoxMessageType.Info));
            void Refresh()
            {
                mode.SetValueWithoutNotify(PlayerSettings.meshDeformation);
                var modes = targets.Cast<UniskinRigBinding>()
                    .SelectMany(binding => binding.renderers)
                    .Where(renderer => renderer != null)
                    .Select(renderer => SceneSpriteMeshInfo.Describe(renderer)[3]).Distinct().ToArray();
                actual.text = "Actual Deformation: " + (modes.Length == 0 ? "No bound renderers" : string.Join(", ", modes));
            }
            root.schedule.Execute(Refresh).Every(250);
            Refresh();
            return root;
        }
    }
}
