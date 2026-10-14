using System;
using System.Linq;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    internal sealed class UniskinRigInspectorControls : VisualElement
    {
        enum Command { UseRig, UsePsb, Bake }

        readonly Button m_UseRig, m_UsePsb, m_Bake;
        static GUIContent s_UseRig, s_UsePsb, s_Bake;

        internal UniskinRigInspectorControls(Func<Sprite> getSprite)
        {
            name = "uniskinRigControls";
            var row = new VisualElement();
            row.AddToClassList("uniskin-rig-mode-row");
            m_UseRig = CreateButton("uniskinUseRig", "Use Uniskin rig", "UniskinLogo", () => Run(getSprite, Command.UseRig));
            m_UsePsb = CreateButton("uniskinUsePsb", "Use PSB Importer", "PsbLogo", () => Run(getSprite, Command.UsePsb));
            m_Bake = CreateButton("uniskinBakeRig", "Bake to PSB Importer", "BakeToPsb", () => Run(getSprite, Command.Bake));
            m_UseRig.tooltip = "Copy the current PSB mesh, bones and weights into Uniskin rig and use it for editing.";
            m_UsePsb.tooltip = "Bake pending rig changes to PSB Importer, then use its native skinning data.";
            m_Bake.tooltip = "Bake pending rig changes to PSB Importer without switching editing mode.";
            row.Add(m_UseRig);
            row.Add(m_UsePsb);
            Add(row);
            Add(m_Bake);
        }

        static Button CreateButton(string name, string label, string iconName, Action action)
        {
            var button = new Button(action) { name = name };
            button.AddToClassList("uniskin-rig-action");
            var icon = new Image { image = LoadIcon(iconName), pickingMode = PickingMode.Ignore };
            icon.AddToClassList("uniskin-inspector-icon");
            button.Add(icon);
            button.Add(new Label(label) { pickingMode = PickingMode.Ignore });
            return button;
        }

        static Texture2D LoadIcon(string name) => ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/" + name + ".png");
        static bool IsPsb(Sprite sprite) => sprite != null && UniskinClippingMaskUtility.Find(sprite) == null &&
            UniskinRigUtility.SourcePath(sprite).EndsWith(".psb", StringComparison.OrdinalIgnoreCase);

        internal void Refresh(Sprite sprite, bool canEdit)
        {
            var psb = IsPsb(sprite);
            EnableInClassList("uniskin-rig-hidden", !psb);
            var rig = psb ? UniskinRigUtility.Find(sprite) : null;
            var useRig = rig != null && rig.editingEnabled;
            canEdit &= psb && !EditorApplication.isPlayingOrWillChangePlaymode;
            m_UseRig.SetEnabled(canEdit && !useRig);
            m_UsePsb.SetEnabled(canEdit && useRig);
            m_Bake.SetEnabled(canEdit && rig != null && rig.hasUnbakedChanges);
            m_UseRig.EnableInClassList("uniskin-rig-current", useRig);
            m_UsePsb.EnableInClassList("uniskin-rig-current", psb && !useRig);
        }

        internal static bool TryGetRootSource(GameObject root, out SpriteRenderer renderer)
        {
            renderer = null;
            if (root == null || EditorUtility.IsPersistent(root) || !root.scene.IsValid()) return false;
            var candidates = root.GetComponentsInChildren<SpriteRenderer>(true)
                .Where(item => IsPsb(item.sprite) &&
                    (PrefabUtility.GetNearestPrefabInstanceRoot(item.gameObject)?.transform ?? item.transform.root) == root.transform)
                .GroupBy(item => UniskinRigUtility.SourcePath(item.sprite)).Take(2).ToArray();
            if (candidates.Length != 1) return false;
            renderer = candidates[0].First();
            return true;
        }

        internal static void DrawRoot(GameObject root, Sprite sprite)
        {
            if (s_UseRig == null)
            {
                s_UseRig = new GUIContent("Use Uniskin rig", LoadIcon("UniskinLogo"),
                    "Copy current PSB skinning data into Uniskin rig and use it for editing.");
                s_UsePsb = new GUIContent("Use PSB Importer", LoadIcon("PsbLogo"),
                    "Bake pending rig changes, then use PSB Importer.");
                s_Bake = new GUIContent("Bake to PSB Importer", LoadIcon("BakeToPsb"),
                    "Bake pending changes without switching editing mode.");
            }
            var rig = UniskinRigUtility.Find(sprite);
            var useRig = rig != null && rig.editingEnabled;
            Func<Sprite> getSprite = () => TryGetRootSource(root, out var renderer) ? renderer.sprite : null;
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(useRig))
                        if (GUILayout.Button(s_UseRig, GUILayout.Height(24))) Run(getSprite, Command.UseRig);
                    using (new EditorGUI.DisabledScope(!useRig))
                        if (GUILayout.Button(s_UsePsb, GUILayout.Height(24))) Run(getSprite, Command.UsePsb);
                }
                using (new EditorGUI.DisabledScope(rig == null || !rig.hasUnbakedChanges))
                    if (GUILayout.Button(s_Bake, GUILayout.Height(24))) Run(getSprite, Command.Bake);
            }
        }

        static void Run(Func<Sprite> getSprite, Command command)
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                try
                {
                    var sprite = getSprite();
                    if (!IsPsb(sprite)) return;
                    var rig = UniskinRigUtility.Find(sprite);
                    switch (command)
                    {
                        case Command.UseRig:
                            rig = rig ?? UniskinRigUtility.Create(sprite);
                            if (rig != null && !rig.editingEnabled) UniskinRigUtility.Enable(rig);
                            break;
                        case Command.UsePsb:
                            if (rig != null && rig.editingEnabled) UniskinRigUtility.UsePsb(rig);
                            break;
                        case Command.Bake:
                            if (rig != null && rig.hasUnbakedChanges) UniskinRigUtility.Bake(rig);
                            break;
                    }
                }
                catch (Exception exception) { EditorUtility.DisplayDialog("UniskinRig", exception.Message, "OK"); }
            };
        }
    }
}
