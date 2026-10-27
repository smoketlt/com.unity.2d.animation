using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    internal static class SceneMeshTargetPicking
    {
        internal const float meshEdgePadding = 12f;

        // Work in GUI points so the protected contour does not shrink when zooming out.
        internal static bool NearBoundary(Vector2 point, Vector2[] vertices, int[] indices, float padding)
        {
            var edges = new Dictionary<(int, int), int>();
            for (var i = 0; i + 2 < indices.Length; i += 3)
                for (var j = 0; j < 3; ++j)
                {
                    var a = indices[i + j];
                    var b = indices[i + (j + 1) % 3];
                    var key = a < b ? (a, b) : (b, a);
                    edges.TryGetValue(key, out var count);
                    edges[key] = count + 1;
                }
            foreach (var edge in edges)
            {
                if (edge.Value != 1) continue;
                var a = vertices[edge.Key.Item1];
                var delta = vertices[edge.Key.Item2] - a;
                var t = delta.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude) : 0f;
                if ((point - a - t * delta).sqrMagnitude <= padding * padding) return true;
            }
            return false;
        }

        internal static SpriteRenderer HierarchyTarget(SpriteRenderer current)
        {
            if (Selection.objects.Length != 1 || Selection.activeGameObject == null) return null;
            var target = Selection.activeGameObject.GetComponent<SpriteRenderer>();
            var root = RigRoot(current);
            return target != null && target != current && target.sprite != null &&
                target.gameObject.scene.IsValid() && root != null && RigRoot(target) == root ? target : null;
        }
        internal static Transform RigRoot(SpriteRenderer renderer)
        {
            if (renderer == null) return null;
            var binding = renderer.GetComponentInParent<UniskinRigBinding>();
            if (binding != null && binding.rig != null) return binding.transform;
            var skin = renderer.GetComponent<SpriteSkin>();
            return skin != null && skin.rootBone != null ? skin.rootBone.parent ?? skin.rootBone : renderer.transform.parent;
        }

        internal static void ValidateTarget(SpriteRenderer renderer, SceneMeshSource source)
        {
            if (!renderer.gameObject.scene.IsValid() || renderer.drawMode != SpriteDrawMode.Simple)
                throw new InvalidOperationException("Select a scene SpriteRenderer with Draw Mode set to Simple.");
            var scale = renderer.transform.lossyScale;
            if (Mathf.Abs(scale.x * scale.y) < .000001f)
                throw new InvalidOperationException("The selected mesh needs non-zero X/Y scale.");
            if (SkinningModule.IsEditingAsset(source.assetPath))
                throw new InvalidOperationException("Close Skinning Editor for this asset before switching meshes.");
        }

        // No native picking render: safe in the early Scene callback, including GPU skins.
        internal static SpriteRenderer Pick(SceneView view, SpriteRenderer current, Vector3[] vertices, int[] indices, float edgePadding = 0f)
        {
            var evt = Event.current;
            if (evt == null || evt.type != EventType.MouseDown || evt.button != 0 || evt.alt || evt.shift ||
                evt.control || evt.command || GUIUtility.hotControl != 0 || UnityEditor.Tools.current == Tool.View || UnityEditor.Tools.viewToolActive || view.camera == null)
                return null;
            var root = RigRoot(current);
            if (root == null) return null;
            var ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            // Vertex handles on the active mesh always retain their gesture.
            if (vertices.Any(vertex => Vector2.Distance(HandleUtility.WorldToGUIPoint(vertex), evt.mousePosition) < 10f)) return null;
            // Let the active tool own clicks on either side of the displayed contour.
            if (edgePadding > 0f && NearBoundary(evt.mousePosition,
                vertices.Select(vertex => HandleUtility.WorldToGUIPoint(vertex)).ToArray(), indices, edgePadding)) return null;
            SpriteRenderer best = null;
            for (var i = 0; i + 2 < indices.Length; i += 3)
                if (SceneSpriteDeformedMesh.Intersect(ray, vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]], out _))
                {
                    // Edit Mesh owns its entire displayed surface, even when a
                    // sibling sprite sorts above it. Otherwise switching consumes
                    // Create clicks before the geometry controller receives them.
                    if (edgePadding > 0f) return null;
                    best = current;
                    break;
                }
            var mesh = new SceneSpriteDeformedMesh();
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>())
            {
                if (renderer == current || RigRoot(renderer) != root || !SceneSpriteGpuPicking.CanPick(renderer, view.camera)) continue;
                if (mesh.Sample(renderer) && mesh.Hit(ray, out _) && (best == null || SceneSpriteGpuPicking.Compare(renderer, best, view.camera) > 0))
                    best = renderer;
            }
            return best == current ? null : best;
        }
    }

    internal partial class SceneEditMeshWindow
    {
        bool m_TargetSwitchQueued;

        internal void SwitchHierarchyMeshTarget()
        {
            if (m_SuspendingSkinning) return;
            if (m_TargetSwitchQueued || m_Source?.clipping != null || m_TraceActive || pasteActive ||
                m_Session == null || m_Session.mode == 3 || BlockReason() != null) return;
            var target = SceneMeshTargetPicking.HierarchyTarget(m_Renderer);
            if (target == null) return;
            if (m_Session.selection.Count > 0)
            {
                m_Session.selection.Clear(); UpdateUI(); SceneView.RepaintAll();
            }
            else SwitchMeshTarget(target);
        }

        bool PickMeshTarget(SceneView view)
        {
            if (Event.current.type != EventType.MouseDown || Event.current.button != 0) return false;
            if (m_TargetSwitchQueued) { Event.current.Use(); return true; }
            if (m_Source.clipping != null || m_TraceActive || pasteActive || m_Session.mode == 3) return false;
            var displayed = Deformation()?.mesh ?? m_Session.mesh;
            var matrix = SceneMeshSession.PixelToWorld(m_Renderer, displayed.frame);
            var target = SceneMeshTargetPicking.Pick(view, m_Renderer,
                displayed.vertices.Select(vertex => matrix.MultiplyPoint3x4(vertex)).ToArray(), displayed.indices,
                SceneMeshTargetPicking.meshEdgePadding);
            if (target == null) return false;
            Event.current.Use();
            QueueMeshTarget(target);
            return true;
        }

        internal void QueueMeshTarget(SpriteRenderer target)
        {
            if (m_Session.selection.Count > 0)
            {
                m_Session.selection.Clear(); UpdateUI(); SceneView.RepaintAll(); return;
            }
            m_TargetSwitchQueued = true;
            EditorApplication.delayCall += () =>
            {
                if (this != null && !isClosingForReload) SwitchMeshTarget(target);
            };
        }

        internal void SwitchMeshTarget(SpriteRenderer target)
        {
            try
            {
                if (m_SuspendingSkinning) return;
                if (target == null || target.sprite == null || SceneMeshTargetPicking.RigRoot(target) != SceneMeshTargetPicking.RigRoot(m_Renderer)) return;
                var source = new SceneMeshSource(target.sprite);
                if (!source.hasAuthoredMesh || source.clipping != null) return;
                SceneMeshTargetPicking.ValidateTarget(target, source);
                rootVisualElement.focusController?.focusedElement?.Blur();
                EndSymmetryAxisDrag(false);
                if (m_Session.dirty && !Apply()) return;
                source = new SceneMeshSource(target.sprite); // Save may regenerate sibling Sprites.
                var session = CreateInstance<SceneMeshSession>();
                session.hideFlags = HideFlags.HideAndDontSave;
                try { session.Load(target.sprite, source); }
                catch { DestroyImmediate(session); throw; }
                RestoreScene();
                var previous = m_Session;
                m_Session = session; m_Renderer = target; m_Source = source;
                m_Controller.selection = session.selection; m_Controller.cacheUndo = session;
                m_AppliedUndoGroup = -1; m_Error = null;
                m_LastImporterFingerprint = source.Fingerprint();
                m_SelectionObjects = new UnityEngine.Object[] { target.gameObject };
                Selection.objects = m_SelectionObjects;
                DestroyImmediate(previous);
                UpdateUI(); SceneView.RepaintAll();
            }
            catch (Exception exception) { ReportError(exception); }
            finally { m_TargetSwitchQueued = false; }
        }
    }

    internal sealed partial class SceneEditWeightsWindow
    {
        bool m_TargetSwitchQueued;
        SpriteRenderer m_WeightClickTarget;
        Vector2 m_WeightClickStart;
        bool m_DeferredWeightStamp;

        internal void SwitchHierarchyWeightTarget()
        {
            if (m_TargetSwitchQueued || m_Bind || m_Weld || BlockReason() != null) return;
            var target = SceneMeshTargetPicking.HierarchyTarget(m_Renderer);
            if (target == null) return;
            if (m_Session.vertices.Count > 0)
            {
                m_Session.SelectVertices(Array.Empty<int>()); UpdateUI(); SceneView.RepaintAll();
            }
            else SwitchWeightTarget(target);
        }

        bool PickWeightTarget(SceneView view, Event evt)
        {
            // Any drag cancels the click permanently, even if it returns to its origin.
            if (evt.rawType == EventType.MouseDrag || evt.rawType == EventType.Ignore ||
                evt.rawType == EventType.MouseLeaveWindow || evt.rawType == EventType.KeyDown)
                m_WeightClickTarget = null;
            if (evt.rawType == EventType.MouseUp)
            {
                var target = m_WeightClickTarget;
                m_WeightClickTarget = null;
                if (target == null || evt.button != 0 || evt.alt || evt.shift || evt.control || evt.command ||
                    Vector2.Distance(m_WeightClickStart, evt.mousePosition) > 3f || m_Bind || m_Weld ||
                    (GUIUtility.hotControl != 0 && GUIUtility.hotControl != m_Control) ||
                    UnityEditor.Tools.current == Tool.View || UnityEditor.Tools.viewToolActive) return false;
                EndGesture();
                evt.Use();
                QueueWeightTarget(target);
                return true;
            }
            if (evt.type != EventType.MouseDown || evt.button != 0) return false;
            m_WeightClickTarget = null;
            if (m_TargetSwitchQueued) { evt.Use(); return true; }
            if (m_Bind || m_Weld) return false;
            RefreshLiveBoneBindings();
            if (view.drawGizmos && (!m_Preview || m_PreviewPose) && NearestBone(evt.mousePosition) != null) return false;
            m_WeightClickTarget = SceneMeshTargetPicking.Pick(view, m_Renderer, WorldVertices(), m_Session.mesh.indices);
            m_WeightClickStart = evt.mousePosition;
            // Leave mouse down to the weight tool. A brush stamp waits until a
            // drag proves this is painting rather than a click on another mesh.
            return false;
        }

        internal void QueueWeightTarget(SpriteRenderer target)
        {
            if (m_Session.vertices.Count > 0)
            {
                m_Session.SelectVertices(Array.Empty<int>()); UpdateUI(); SceneView.RepaintAll(); return;
            }
            m_TargetSwitchQueued = true;
            EditorApplication.delayCall += () =>
            {
                if (this != null && !isClosingForReload) SwitchWeightTarget(target);
            };
        }

        internal void SwitchWeightTarget(SpriteRenderer target)
        {
            SceneWeightSession next = null;
            try
            {
                if (target == null || target.sprite == null || SceneMeshTargetPicking.RigRoot(target) != SceneMeshTargetPicking.RigRoot(m_Renderer)) return;
                var source = new SceneMeshSource(target.sprite);
                if (!source.hasAuthoredMesh) return;
                SceneMeshTargetPicking.ValidateTarget(target, source);
                rootVisualElement.focusController?.focusedElement?.Blur(); EndGesture(); ClearBoneDrop();
                if (m_Bind) SetBindMode(false);
                if (m_Session.dirty && !Apply()) return;
                source = new SceneMeshSource(target.sprite);
                next = CreateInstance<SceneWeightSession>(); next.hideFlags = HideFlags.HideAndDontSave;
                next.Load(target.sprite, source);
                RestorePreviewPose(false); RestoreTool();
                next.context.CaptureScenePose(target, source.rect.rect);
                var previous = m_Session;
                m_Session = next; next = null; m_Renderer = target; m_Source = source;
                m_PreviewPose = false; m_Weld = false; m_Bind = false; m_FirstBindPositions = null;
                m_PreviewPoseBaseline.Clear(); m_PreviewPoseLastWritten.Clear(); m_PreviewPoseSample.Clear();
                m_PreviewPoseAnimated.Clear(); m_PreviewPoseTouched.Clear(); m_PoseView = null;
                m_LiveBoneBindings.Clear(); m_CharacterLiveBindings.Clear(); m_CharacterBindingsLoaded = false;
                m_RowKey = null; m_Error = null;
                m_EntrySelection = new UnityEngine.Object[] { target.gameObject };
                Selection.objects = m_EntrySelection;
                DestroyImmediate(previous);
                UpdateUI(); SceneView.RepaintAll();
            }
            catch (Exception exception) { m_Error = exception.Message; EditorUtility.DisplayDialog("Edit Weights", m_Error, "OK"); }
            finally { if (next != null) DestroyImmediate(next); m_TargetSwitchQueued = false; }
        }
    }
}
