using System;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    [CustomEditor(typeof(UniskinRig))]
    internal sealed class UniskinRigEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var rig = (UniskinRig)target;
            var root = new VisualElement();
            var style = ResourceLoader.Load<StyleSheet>("SkinningModule/SceneMeshInspector.uss");
            if (style != null) root.styleSheets.Add(style);
            var status = new Label();
            root.Add(status);
            root.Add(new Button(() => Run(() => UniskinRigUtility.Enable(rig))) { text = "Use UniskinRig" });
            root.Add(new Button(() => Run(() => UniskinRigUtility.BakeWithConflictConfirmation(rig))) { name = "bakeToPsb", text = "Bake to PSB" });
            root.Add(new Button(() => Run(() => UniskinRigUtility.OpenSkinningEditor(rig))) { text = "Open Skinning Editor (Auto Bake)" });
            root.Add(new Button(() => Run(() => UniskinRigUtility.UsePsb(rig))) { text = "Bake and Use PSB" });
            root.Add(new Button(() => Run(() =>
            {
                if (!rig.hasUnbakedChanges || EditorUtility.DisplayDialog("Reload from PSB",
                    "Replace the rig's unbaked mesh, bones and weights with the current PSB data?", "Reload", "Cancel"))
                    UniskinRigUtility.ReloadFromPsb(rig);
            })) { text = "Reload from PSB" });
            root.Add(new Button(() => Run(() => UniskinRigUtility.CreateRigPrefab(rig))) { text = "Create Rig Prefab" });
            root.Query<Button>().ForEach(button => button.AddToClassList("uniskin-rig-action"));
            root.schedule.Execute(() =>
            {
                if (rig == null) return;
                status.text = (rig.editingEnabled ? "UniskinRig active. " : "PSB active. ") +
                    (rig.hasUnbakedChanges ? "Changes waiting to be baked." : "PSB is synchronized.");
                root.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
            }).Every(200);
            return root;
        }

        static void Run(Action action)
        {
            EditorApplication.delayCall += () =>
            {
                try { action(); }
                catch (Exception exception) { EditorUtility.DisplayDialog("UniskinRig", exception.Message, "OK"); }
            };
        }
    }
}
