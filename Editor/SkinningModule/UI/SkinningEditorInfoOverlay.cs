using System.Collections.Generic;
using UnityEditor.U2D.Layout;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    internal static class SkinningEditorInfoOverlay
    {
        private const string k_OverlayName = "SkinningEditorInfoOverlay";
        private const string k_LabelName = "SkinningEditorInfoOverlayLabel";
        private static readonly Dictionary<VisualElement, VisualElement> s_Overlays = new Dictionary<VisualElement, VisualElement>();

        public static void Show(LayoutOverlay layoutOverlay, string text)
        {
            Show((VisualElement)layoutOverlay, text);
        }

        public static void Show(VisualElement host, string text)
        {
            if (host == null)
                return;

            if (string.IsNullOrEmpty(text))
            {
                Hide(host);
                return;
            }

            VisualElement overlay = GetOrCreateOverlay(host);
            Label label = overlay.Q<Label>(k_LabelName);
            label.text = text;
            overlay.style.display = DisplayStyle.Flex;
            overlay.BringToFront();
        }

        public static void Hide(LayoutOverlay layoutOverlay)
        {
            Hide((VisualElement)layoutOverlay);
        }

        public static void Hide(VisualElement host)
        {
            if (host == null)
                return;

            if (s_Overlays.TryGetValue(host, out VisualElement overlay))
                overlay.style.display = DisplayStyle.None;
        }

        public static void Remove(LayoutOverlay layoutOverlay)
        {
            Remove((VisualElement)layoutOverlay);
        }

        public static void Remove(VisualElement host)
        {
            if (host == null)
                return;

            if (!s_Overlays.TryGetValue(host, out VisualElement overlay))
                return;

            overlay.RemoveFromHierarchy();
            s_Overlays.Remove(host);
        }

        private static VisualElement GetOrCreateOverlay(VisualElement host)
        {
            if (s_Overlays.TryGetValue(host, out VisualElement overlay) && overlay.parent == host)
                return overlay;

            overlay = new VisualElement
            {
                name = k_OverlayName,
                pickingMode = PickingMode.Ignore
            };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0;
            overlay.style.right = 0;
            overlay.style.top = 8;
            overlay.style.flexDirection = FlexDirection.Row;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.alignItems = Align.Center;

            Label label = new Label
            {
                name = k_LabelName,
                pickingMode = PickingMode.Ignore
            };
            label.style.maxWidth = 980;
            label.style.fontSize = 14;
            label.style.paddingLeft = 14;
            label.style.paddingRight = 14;
            label.style.paddingTop = 8;
            label.style.paddingBottom = 8;
            label.style.borderTopLeftRadius = 4;
            label.style.borderTopRightRadius = 4;
            label.style.borderBottomLeftRadius = 4;
            label.style.borderBottomRightRadius = 4;
            label.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.86f);
            label.style.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.Normal;

            overlay.Add(label);
            host.Add(overlay);
            s_Overlays[host] = overlay;
            return overlay;
        }
    }

    internal static class SkinningEditorInfoText
    {
        public const string GenerateGeometry = "Generate Geometry: set detail, alpha, and subdivision, then generate the selected sprite or all visible sprites.";
        public const string GenerateWeights = "Auto Weights: generate, normalize, or clear weights. Select vertices to limit the operation; otherwise the visible mesh is affected.";
        public const string WeightSlider = "Weight Slider: select vertices and bones, then adjust listed weights. Smooth and Prune work on the selected vertices.";
        public const string WeightBrush = "Weight Brush: select a bone to paint. Alt-click/drag selects vertices. S/B/F + drag or Ctrl/Shift/Ctrl+Shift + wheel adjust Strength/Size/Feather. Shift paints Smooth; Ctrl/Cmd subtracts.";
        public const string BoneInfluence = "Bone Influence: choose which bones affect the selected sprite. Weight tools use this bone list for painting and sliders.";
        public const string SpriteInfluence = "Sprite Influence: select a bone, then choose which sprites it influences.";
        public const string CopyPaste = "Copy/Paste: copy mesh, bones, and weights, then paste or mirror them onto the selected sprite or character.";
        public const string Visibility = "Visibility: toggle bone and sprite visibility, adjust opacity, and focus the scene while editing.";
        public const string Pivot = "Pivot: adjust the character pivot with the handle or numeric fields.";
        public const string ReparentBone = "Reparent Bones: drag bones in the hierarchy to change parents; rename or adjust visibility from the same list.";
        public const string Constraints = "Constraints: create or assign a constraint set. Select source and driven bones, then add or update a Position, Rotation, or Scale constraint.";

        public static string ForMeshMode(SpriteMeshViewMode mode)
        {
            switch (mode)
            {
                case SpriteMeshViewMode.EditGeometry:
                    return "Modify Geometry: select and move vertices or edges. Alt temporarily switches to Create. Ctrl+C copy selected vertex or bone transforms, Ctrl+V paste, Ctrl+Shift+V paste mirrored.";
                case SpriteMeshViewMode.CreateVertex:
                    return "Create Geometry: click to add vertices, or drag from a vertex to create edges. Alt temporarily switches to Modify.";
                case SpriteMeshViewMode.CreateEdge:
                    return "Create Edges: drag between existing vertices to connect them.";
                case SpriteMeshViewMode.SplitEdge:
                    return "Split Edges: click an edge to insert a vertex.";
                case SpriteMeshViewMode.NewGeometry:
                    return "New Geometry: click around the sprite to redraw the outline. Esc cancels the current outline.";
                default:
                    return null;
            }
        }

        public static string ForSkeletonMode(SkeletonMode mode, bool editBindPose)
        {
            switch (mode)
            {
                case SkeletonMode.EditPose:
                    return editBindPose
                        ? "Edit Joints: adjust bind-pose bones, joints, and tails. Esc or empty click clears selection."
                        : "Preview Pose: select, move, and rotate bones to test deformation. Esc or empty click clears selection.";
                case SkeletonMode.EditJoints:
                    return "Edit Joints: adjust bind-pose bones, joints, and tails. Esc or empty click clears selection.";
                case SkeletonMode.CreateBone:
                    return "Create Bone: click-drag to draw bones. Start from an existing tail to chain bones.";
                case SkeletonMode.SplitBone:
                    return "Split Bone: click a bone to insert a joint and split it.";
                default:
                    return null;
            }
        }
    }
}
