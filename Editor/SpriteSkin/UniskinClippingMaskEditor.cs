using System;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    [CustomEditor(typeof(UniskinClippingMask))]
    internal sealed class UniskinClippingMaskEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var style = ResourceLoader.Load<StyleSheet>("SkinningModule/SceneMeshInspector.uss");
            if (style != null) root.styleSheets.Add(style);
            var mesh = new Button(() => Run(1)) { name = "uniskinEditMesh" };
            var meshLabel = new Label("Draw Contour") { pickingMode = PickingMode.Ignore };
            AddIcon(mesh, "CreateMesh"); mesh.Add(meshLabel);
            var weights = new Button(() => Run(2)) { name = "uniskinEditWeights" };
            AddIcon(weights, "Weights"); weights.Add(new Label("Edit Weights") { pickingMode = PickingMode.Ignore });
            root.Add(mesh); root.Add(weights);
            void Refresh()
            {
                var mask = target as UniskinClippingMask;
                var ready = mask != null && mask.contour != null && mask.contour.hasGeometry;
                meshLabel.text = ready ? "Edit Contour" : "Draw Contour";
                mesh.SetEnabled(mask != null && !EditorApplication.isPlayingOrWillChangePlaymode);
                weights.SetEnabled(ready && mesh.enabledSelf);
            }
            root.schedule.Execute(Refresh).Every(200); Refresh(); return root;
        }

        static void AddIcon(Button button, string name)
        {
            var icon = new Image { image = ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/" + name + ".png"), pickingMode = PickingMode.Ignore };
            icon.AddToClassList("uniskin-inspector-icon"); button.Add(icon);
        }

        void Run(int command)
        {
            serializedObject.ApplyModifiedProperties(); var mask = target as UniskinClippingMask;
            EditorApplication.delayCall += () =>
            {
                if (mask == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
                try
                {
                    if (command == 1)
                    {
                        if (mask.contour == null) UniskinClippingMaskUtility.Create(mask);
                        if (mask.contourRenderer == null) throw new InvalidOperationException("The contour renderer is missing. Reattach this mask to its character.");
                        SceneEditMeshWindow.OpenForRenderer(mask.contourRenderer, false);
                    }
                    else SceneEditWeightsWindow.OpenForRenderer(mask.contourRenderer);
                }
                catch (Exception ex) { EditorUtility.DisplayDialog("Uniskin Clipping Mask", ex.Message, "OK"); }
            };
        }
    }
}
