using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    [InitializeOnLoad]
    internal static class SceneSpriteGpuPicking
    {
        static readonly SceneSpriteDeformedMesh s_Mesh = new SceneSpriteDeformedMesh();
        static readonly List<GameObject> s_Ignore = new List<GameObject>();
        static readonly HashSet<SpriteRenderer> s_HiddenOutlines = new HashSet<SpriteRenderer>();
        static readonly HashSet<SpriteRenderer> s_SelectedRenderers = new HashSet<SpriteRenderer>();
        static readonly List<SpriteRenderer> s_Restore = new List<SpriteRenderer>();
        static readonly Dictionary<ulong, int> s_Edges = new Dictionary<ulong, int>();
        static readonly Vector3[] s_Line = new Vector3[2];
        static bool s_NativeOutlineFiltered;
        static Texture2D s_ContourTexture;
        static readonly PropertyInfo s_ShowOutline = typeof(Editor).Assembly.GetType("UnityEditor.AnnotationUtility")?
            .GetProperty("showSelectionOutline", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        static SceneSpriteGpuPicking()
        {
            SceneView.beforeSceneGui += BeforeSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreOutlines;
            EditorApplication.quitting += RestoreOutlines;
        }

        internal static bool UsesGpu(SpriteRenderer renderer)
        {
            var skin = renderer != null ? renderer.GetComponent<SpriteSkin>() : null;
            return skin != null && skin.isActiveAndEnabled && skin.isValid && skin.DeformationSystem != null &&
                skin.currentDeformationMethod == DeformationMethods.Gpu;
        }

        static bool IsEditing() => Resources.FindObjectsOfTypeAll<SceneEditMeshWindow>().Length != 0 ||
            Resources.FindObjectsOfTypeAll<SceneEditBoneWindow>().Length != 0 ||
            Resources.FindObjectsOfTypeAll<SceneEditWeightsWindow>().Length != 0;

        internal static bool CanPick(SpriteRenderer renderer, Camera camera)
        {
            return renderer != null && renderer.sprite != null && renderer.enabled && renderer.gameObject.activeInHierarchy &&
                !renderer.forceRenderingOff && (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0 &&
                StageUtility.GetStageHandle(renderer.gameObject) == StageUtility.GetCurrentStageHandle() &&
                !SceneVisibilityManager.instance.IsHidden(renderer.gameObject) &&
                !SceneVisibilityManager.instance.IsPickingDisabled(renderer.gameObject) &&
                (renderer.gameObject.hideFlags & HideFlags.NotEditable) == 0;
        }

        static void OnSceneGUI(SceneView view)
        {
            if (Event.current.type == EventType.Repaint)
            {
                // The native camera/outline pass has finished; future cameras must
                // rebuild from their real selection rather than inherit our filter.
                if (s_NativeOutlineFiltered)
                {
                    SceneSelectionOutlineCompatibility.Invalidate();
                    s_NativeOutlineFiltered = false;
                }
                UpdateOutlines(view, true);
            }
        }

        static void BeforeSceneGUI(SceneView view)
        {
            var evt = Event.current;
            if (evt.type == EventType.Repaint) UpdateOutlines(view, false);
            // Actual transform/bone handles retain priority over our mesh hit.
            // Never claim navigation or popup input.
            var control = GUIUtility.GetControlID("UniskinGpuSpritePick".GetHashCode(), FocusType.Passive);
            if ((evt.type != EventType.Layout && evt.type != EventType.MouseDown) || evt.alt ||
                UnityEditor.Tools.current == Tool.View || UnityEditor.Tools.viewToolActive ||
                GUIUtility.hotControl != 0 || IsEditing() || view.camera == null) return;
            if (evt.type == EventType.MouseDown && (evt.button != 0 || HandleUtility.nearestControl != control)) return;

            var ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            s_Ignore.Clear();
            SpriteRenderer best = null;
            var sourceHit = false;
            foreach (var skin in UnityObjectCompatibility.FindObjectsByType<SpriteSkin>())
            {
                var renderer = skin.GetComponent<SpriteRenderer>();
                if (!UsesGpu(renderer) || !CanPick(renderer, view.camera)) continue;
                s_Ignore.Add(renderer.gameObject);
                if (evt.type == EventType.Layout && !sourceHit)
                    sourceHit = SceneSpriteDeformedMesh.HitSource(renderer, ray);
                if (s_Mesh.Sample(renderer) && s_Mesh.Hit(ray, out _) &&
                    (best == null || Compare(renderer, best, view.camera) > 0)) best = renderer;
            }
            if (s_Ignore.Count == 0) return;
            // PickGameObject renders a native picking buffer. Running it during Layout
            // nests graphics-frame work and causes device.IsInsideFrame / recursive GUI errors.
            if (evt.type == EventType.Layout)
            {
                // RectSelection registers its own default later in the same Layout.
                // A real hit must beat that default; actual handles registered later
                // still win equal zero-distance hits.
                if (best != null || sourceHit) HandleUtility.AddControl(control, 0f);
                return;
            }
            PickOnMouseDown(view, evt, best);
        }

        static void PickOnMouseDown(SceneView view, Event evt, SpriteRenderer best)
        {
            // Keep the native render behind its own event gate, including future callers.
            if (evt.type != EventType.MouseDown || evt.button != 0) return;
            var original = HandleUtility.PickGameObject(evt.mousePosition, false);
            var originalGpu = original != null && s_Ignore.Contains(original);
            // Once our default control owns the press, resolve even an unchanged native
            // result here; Unity's selection control no longer owns that press.
            if (best == null && !originalGpu)
            {
                Select(original, evt.shift, EditorGUI.actionKey);
                evt.Use();
                view.Repaint();
                return;
            }
            var native = HandleUtility.PickGameObject(evt.mousePosition, false, s_Ignore.ToArray());
            var nativeSprite = native != null ? native.GetComponent<SpriteRenderer>() : null;
            // Preserve Unity's bone/icon/non-sprite picks; only replace sprite-vs-sprite ordering.
            GameObject picked = best != null ? best.gameObject : null;
            if (native != null && (best == null || nativeSprite == null ||
                Compare(nativeSprite, best, view.camera) >= 0)) picked = native;
            Select(picked, evt.shift, EditorGUI.actionKey);
            evt.Use();
            view.Repaint();
        }

        internal static void Select(GameObject picked, bool add, bool toggle)
        {
            if (!add && !toggle) { Selection.activeGameObject = picked; return; }
            if (picked == null) return;
            var selection = new List<Object>(Selection.objects);
            if (selection.Contains(picked)) { if (toggle) selection.Remove(picked); }
            else selection.Add(picked);
            Selection.objects = selection.ToArray();
        }

        // Compare the outermost differing sorting units, then their contents for a shared group.
        internal static int Compare(SpriteRenderer a, SpriteRenderer b, Camera camera)
        {
            var groupsA = Groups(a); var groupsB = Groups(b);
            var common = 0;
            while (common < groupsA.Count && common < groupsB.Count && groupsA[common] == groupsB[common]) ++common;
            var ga = common < groupsA.Count ? groupsA[common] : null;
            var gb = common < groupsB.Count ? groupsB[common] : null;
            var layer = SortingLayer.GetLayerValueFromID(ga != null ? ga.sortingLayerID : a.sortingLayerID)
                .CompareTo(SortingLayer.GetLayerValueFromID(gb != null ? gb.sortingLayerID : b.sortingLayerID));
            if (layer != 0) return layer;
            var order = (ga != null ? ga.sortingOrder : a.sortingOrder).CompareTo(gb != null ? gb.sortingOrder : b.sortingOrder);
            if (order != 0) return order;
            var pa = ga != null ? ga.transform.position : a.spriteSortPoint == SpriteSortPoint.Pivot ? a.transform.position : a.bounds.center;
            var pb = gb != null ? gb.transform.position : b.spriteSortPoint == SpriteSortPoint.Pivot ? b.transform.position : b.bounds.center;
            var mode = camera.transparencySortMode;
            if (mode == TransparencySortMode.Default) mode = GraphicsSettings.transparencySortMode;
            if (mode == TransparencySortMode.Default) mode = camera.orthographic ? TransparencySortMode.Orthographic : TransparencySortMode.Perspective;
            var axis = camera.transparencySortMode == TransparencySortMode.CustomAxis ? camera.transparencySortAxis : GraphicsSettings.transparencySortAxis;
            float Distance(Vector3 point) => mode == TransparencySortMode.Perspective ? (point - camera.transform.position).sqrMagnitude :
                Vector3.Dot(point - camera.transform.position, mode == TransparencySortMode.CustomAxis ? axis : camera.transform.forward);
            return Distance(pb).CompareTo(Distance(pa));
        }

        static List<SortingGroup> Groups(SpriteRenderer renderer)
        {
            var result = new List<SortingGroup>();
            for (var parent = renderer.transform; parent != null; parent = parent.parent)
            {
                var group = parent.GetComponent<SortingGroup>();
                if (group == null || !group.isActiveAndEnabled) continue;
                result.Add(group);
                if (group.sortAtRoot) break;
            }
            result.Reverse();
            return result;
        }

        static void UpdateOutlines(SceneView view, bool draw)
        {
            var editing = IsEditing();
            s_SelectedRenderers.Clear();
            if (!editing)
                foreach (var renderer in Selection.GetFiltered<SpriteRenderer>(SelectionMode.Deep | SelectionMode.Editable))
                    if (UsesGpu(renderer) && CanPick(renderer, view.camera)) s_SelectedRenderers.Add(renderer);
            if (!draw) s_NativeOutlineFiltered = SceneSelectionOutlineCompatibility.Exclude(s_SelectedRenderers);
            s_Restore.Clear();
            foreach (var renderer in s_HiddenOutlines)
                if (renderer == null || !s_SelectedRenderers.Contains(renderer)) s_Restore.Add(renderer);
            foreach (var renderer in s_Restore)
            {
                Restore(renderer);
                s_HiddenOutlines.Remove(renderer);
            }
            foreach (var renderer in s_SelectedRenderers)
            {
                if (!s_Mesh.Sample(renderer)) continue;
                s_HiddenOutlines.Add(renderer);
                EditorUtility.SetSelectedRenderState(renderer, EditorSelectedRenderState.Hidden);
                if (!draw) continue;
                if (s_ShowOutline?.GetValue(null) is bool show && !show) continue;
                s_Edges.Clear();
                for (var i = 0; i + 2 < s_Mesh.indices.Length; i += 3)
                {
                    Edge(s_Mesh.indices[i], s_Mesh.indices[i + 1]);
                    Edge(s_Mesh.indices[i + 1], s_Mesh.indices[i + 2]);
                    Edge(s_Mesh.indices[i + 2], s_Mesh.indices[i]);
                }
                var contourColor = SceneSelectionOutlineCompatibility.color;
                // The texture overload applies .99 instead of the ordinary overload's
                // hard-coded .75. Compensate so the solid core keeps the native RGB.
                contourColor.a = 1f / .99f;
                using (new Handles.DrawingScope(contourColor, Matrix4x4.identity))
                    foreach (var edge in s_Edges)
                    {
                        if (edge.Value != 1) continue;
                        s_Line[0] = s_Mesh.vertices[(int)(edge.Key >> 32)];
                        s_Line[1] = s_Mesh.vertices[(int)(edge.Key & uint.MaxValue)];
                        Handles.DrawAAPolyLine(ContourTexture(), 4f, s_Line);
                    }
            }
        }

        static void Edge(int a, int b)
        {
            var key = ((ulong)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
            s_Edges.TryGetValue(key, out var count); s_Edges[key] = count + 1;
        }

        static Texture2D ContourTexture()
        {
            if (s_ContourTexture != null) return s_ContourTexture;
            s_ContourTexture = new Texture2D(1, 8, TextureFormat.RGBA32, false, true)
            {
                name = "Uniskin selection contour AA",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[8];
            for (var i = 0; i < pixels.Length; ++i) pixels[i] = new Color(1, 1, 1, i == 0 || i == 7 ? 0 : 1);
            s_ContourTexture.SetPixels(pixels);
            s_ContourTexture.Apply(false, true);
            return s_ContourTexture;
        }

        static void Restore(SpriteRenderer renderer)
        {
            if (renderer != null) EditorUtility.SetSelectedRenderState(renderer, EditorSelectedRenderState.Wireframe | EditorSelectedRenderState.Highlight);
        }

        static void RestoreOutlines()
        {
            SceneSelectionOutlineCompatibility.Invalidate();
            s_NativeOutlineFiltered = false;
            foreach (var renderer in s_HiddenOutlines) Restore(renderer);
            s_HiddenOutlines.Clear();
            if (s_ContourTexture != null) Object.DestroyImmediate(s_ContourTexture);
            s_ContourTexture = null;
        }
    }
}
