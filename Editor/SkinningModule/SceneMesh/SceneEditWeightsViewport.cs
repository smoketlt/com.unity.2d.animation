using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    internal sealed partial class SceneEditWeightsWindow
    {
        enum Gesture { None, Select, Piece, Paint }
        const float k_BrushStrengthScale = .03f;
        Gesture m_Gesture;
        Vector2 m_GestureStart, m_LastStamp;
        int[] m_SelectionBefore;
        int m_Control;
        bool m_Additive;
        bool m_EscapeHandledBeforeSceneGUI;
        Mesh m_OverlayMesh;
        Material m_OverlayMaterial;
        [SerializeField] bool m_PreviousForceRenderingOff, m_HidesRenderer;
        readonly Dictionary<string, (Transform transform, float length)> m_LiveBoneBindings = new Dictionary<string, (Transform, float)>();
        readonly Dictionary<string, (Transform transform, float length)> m_CharacterLiveBindings = new Dictionary<string, (Transform, float)>();
        bool m_CharacterBindingsLoaded;
        Vector2[] m_FirstBindPositions;
        bool m_PreviewPose;
        bool spriteOnlyPreview => m_Preview || m_PreviewPose;
        readonly Dictionary<Transform, (Vector3 position, Quaternion rotation, Vector3 scale)> m_PreviewPoseBaseline =
            new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();
        readonly Dictionary<Transform, (Vector3 position, Quaternion rotation, Vector3 scale)> m_PreviewPoseLastWritten =
            new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();
        readonly Dictionary<Transform, (Vector3 position, Quaternion rotation, Vector3 scale)> m_PreviewPoseSample =
            new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();
        readonly Dictionary<Transform, (Vector3Int position, bool rotation, Vector3Int scale)> m_PreviewPoseAnimated =
            new Dictionary<Transform, (Vector3Int, bool, Vector3Int)>();
        readonly HashSet<Transform> m_PreviewPoseTouched = new HashSet<Transform>();
        SkeletonView m_PoseView;
        bool m_WasAnimationMode;

        sealed class PoseGUI : GUIWrapper
        {
            public override float DistanceToCircle(Vector3 center, float radius) =>
                Mathf.Max(0, Vector2.Distance(Event.current.mousePosition, HandleUtility.WorldToGUIPoint(center)) - 8f / EditorGUIUtility.pixelsPerPoint);
        }

        void CapturePreviewBindPose()
        {
            var world = new Dictionary<Transform, Matrix4x4>();
            var sprite = m_Renderer.sprite;
            var bones = sprite.GetBones(); var bind = sprite.GetBindPoses();
            var pixelToWorld = m_Renderer.transform.localToWorldMatrix * SceneMeshBindingPose.PixelsToLocal(sprite, m_Source.rect.rect);
            m_Session.context.WithSourcePose(() =>
            {
                var skeletonToWorld = m_Session.context.SkeletonToWorld(m_Renderer, m_Source.rect.rect);
                foreach (var bone in m_Session.context.skeleton.bones)
                {
                    if (!m_LiveBoneBindings.TryGetValue(bone.guid, out var live) || live.transform == null) continue;
                    var reference = m_Session.bindingPose?.FindBone(bone.guid);
                    var index = Array.FindIndex(bones, item => item.guid == bone.guid);
                    world[live.transform] = reference.HasValue ? pixelToWorld * reference.Value.inverse * Matrix4x4.Scale(Vector3.one * m_Source.provider.pixelsPerUnit) :
                        index >= 0 && index < bind.Length ? m_Renderer.transform.localToWorldMatrix * bind[index].inverse :
                        // Cache bones use pixel units; native bind matrices use bone-local
                        // world units. Cancel the pixel scale on the local basis as well as
                        // converting the origin, or an unbound parent makes its bound child
                        // reset at pixelsPerUnit times its intended scale and offset.
                        skeletonToWorld * bone.localToWorldMatrix * Matrix4x4.Scale(new Vector3(
                            m_Source.provider.pixelsPerUnit, m_Source.provider.pixelsPerUnit, 1));
                }
            });
            foreach (var item in world)
            {
                var bone = item.Key;
                var known = m_PreviewPoseBaseline.ContainsKey(bone);
                var parent = bone.parent;
                var parentWorld = parent == null ? Matrix4x4.identity : world.TryGetValue(parent, out var pose) ? pose : parent.localToWorldMatrix;
                var local = parentWorld.inverse * item.Value;
                if (!SceneMeshBindingPose.IsFinite(local)) continue;
                m_PreviewPoseBaseline[bone] = (local.GetColumn(3), local.rotation, local.lossyScale);
                if (!known) m_PreviewPoseSample[bone] = m_PreviewPoseLastWritten[bone] = (bone.localPosition, bone.localRotation, bone.localScale);
            }
        }

        void TrackPreviewAnimation()
        {
            var animationMode = AnimationMode.InAnimationMode();
            var animationStopped = m_WasAnimationMode && !animationMode;
            if (animationStopped) m_PreviewPoseAnimated.Clear();
            m_WasAnimationMode = animationMode;
            foreach (var item in m_PreviewPoseLastWritten.ToArray())
            {
                var bone = item.Key; if (bone == null) continue;
                if (animationStopped)
                {
                    m_PreviewPoseLastWritten[bone] = (bone.localPosition, bone.localRotation, bone.localScale);
                    continue;
                }
                var last = item.Value; var sample = m_PreviewPoseSample[bone];
                var positionChanged = ChangedAxes(bone.localPosition, last.position);
                var rotationChanged = Quaternion.Angle(bone.localRotation, last.rotation) > .0001f;
                var scaleChanged = ChangedAxes(bone.localScale, last.scale);
                if (positionChanged != Vector3Int.zero || rotationChanged || scaleChanged != Vector3Int.zero)
                {
                    m_PreviewPoseAnimated.TryGetValue(bone, out var animated);
                    m_PreviewPoseAnimated[bone] = (Vector3Int.Max(animated.position, positionChanged), animated.rotation || rotationChanged, Vector3Int.Max(animated.scale, scaleChanged));
                    m_PreviewPoseSample[bone] = (SampleAxes(sample.position, bone.localPosition, positionChanged),
                        rotationChanged ? bone.localRotation : sample.rotation, SampleAxes(sample.scale, bone.localScale, scaleChanged));
                    m_PreviewPoseLastWritten[bone] = (bone.localPosition, bone.localRotation, bone.localScale);
                }
            }
        }

        static Vector3Int ChangedAxes(Vector3 current, Vector3 previous) => new Vector3Int(
            Mathf.Abs(current.x - previous.x) > .000001f ? 1 : 0,
            Mathf.Abs(current.y - previous.y) > .000001f ? 1 : 0,
            Mathf.Abs(current.z - previous.z) > .000001f ? 1 : 0);

        static Vector3 SampleAxes(Vector3 reference, Vector3 sample, Vector3Int changed) => new Vector3(
            changed.x != 0 ? sample.x : reference.x, changed.y != 0 ? sample.y : reference.y, changed.z != 0 ? sample.z : reference.z);

        static Vector3 ResetSampleAxes(Transform bone, string property, Vector3 reference, Vector3 sample, Vector3Int sampled) => new Vector3(
            sampled.x != 0 || AnimationMode.IsPropertyAnimated(bone, property + ".x") ? sample.x : reference.x,
            sampled.y != 0 || AnimationMode.IsPropertyAnimated(bone, property + ".y") ? sample.y : reference.y,
            sampled.z != 0 || AnimationMode.IsPropertyAnimated(bone, property + ".z") ? sample.z : reference.z);

        void RememberPreviewWrite(Transform bone)
        {
            m_PreviewPoseTouched.Add(bone);
            m_PreviewPoseLastWritten[bone] = (bone.localPosition, bone.localRotation, bone.localScale);
        }

        void RestorePreviewPose(bool recordUndo)
        {
            TrackPreviewAnimation();
            var bones = (recordUndo ? (IEnumerable<Transform>)m_PreviewPoseBaseline.Keys : m_PreviewPoseTouched).Where(bone => bone != null).ToArray();
            if (recordUndo && bones.Length > 0 && !AnimationMode.InAnimationMode()) { Undo.IncrementCurrentGroup(); Undo.RecordObjects(bones, "Reset Preview Pose"); }
            foreach (var bone in bones)
            {
                var pose = m_PreviewPoseBaseline[bone]; var sample = m_PreviewPoseSample[bone];
                m_PreviewPoseAnimated.TryGetValue(bone, out var animated);
                var animatedRotation = animated.rotation || AnimationMode.IsPropertyAnimated(bone, "m_LocalRotation.x") || AnimationMode.IsPropertyAnimated(bone, "m_LocalRotation.y") || AnimationMode.IsPropertyAnimated(bone, "m_LocalRotation.z") || AnimationMode.IsPropertyAnimated(bone, "m_LocalRotation.w");
                bone.localPosition = ResetSampleAxes(bone, "m_LocalPosition", pose.position, sample.position, animated.position);
                bone.localRotation = animatedRotation ? sample.rotation : pose.rotation;
                bone.localScale = ResetSampleAxes(bone, "m_LocalScale", pose.scale, sample.scale, animated.scale);
                RememberPreviewWrite(bone);
            }
        }

        void SetPreviewPose(bool enabled)
        {
            if (m_Saving || BlockReason() != null || m_PreviewPose == enabled) return;
            EndGesture(); ClearBoneDrop(); m_Weld = false;
            if (enabled)
            {
                if (m_Bind) Run(() => SetBindMode(false));
                if (m_Bind) return;
                RefreshLiveBoneBindings();
                CapturePreviewBindPose();
                if (m_PreviewPoseBaseline.Count == 0) { m_Error = "No scene bone Transforms are available."; UpdateUI(); return; }
            }
            m_PreviewPose = enabled; m_Error = null;
            UpdateUI(); SceneView.RepaintAll();
        }

        void ResetPreviewPose()
        {
            if (m_Saving || BlockReason() != null) return;
            EndGesture();
            RefreshLiveBoneBindings(); CapturePreviewBindPose();
            RestorePreviewPose(true);
            m_Error = null; UpdateUI(); SceneView.RepaintAll();
        }

        void PreviewPoseGUI(SceneView scene)
        {
            var evt = Event.current;
            if (!scene.drawGizmos) return;
            if (evt.type == EventType.Repaint) DrawBones();
            if (UnityEditor.Tools.current == Tool.Move)
            {
                PreviewBoneMoveGUI(scene);
                return;
            }
            var selected = m_Session.cache.skeletonSelection.elements
                .Where(bone => bone != null && m_LiveBoneBindings.ContainsKey(bone.guid))
                .Select(bone => m_LiveBoneBindings[bone.guid].transform).Where(bone => bone != null).Distinct().ToArray();
            // Transform only selection roots, so a selected child inherits its parent's change once.
            var roots = selected.Where(bone => !selected.Any(parent => parent != bone && bone.IsChildOf(parent))).ToArray();
            if (roots.Length > 0 && UnityEditor.Tools.current != Tool.View && !evt.alt)
            {
                var active = roots[0];
                var pivot = active.position;
                var orientation = UnityEditor.Tools.pivotRotation == PivotRotation.Local ? active.rotation : Quaternion.identity;
                EditorGUI.BeginChangeCheck();
                var position = pivot; var rotation = orientation; var scale = active.localScale;
                switch (UnityEditor.Tools.current)
                {
                    case Tool.Move: position = Handles.PositionHandle(pivot, orientation); break;
                    case Tool.Scale: scale = Handles.ScaleHandle(active.localScale, pivot, active.rotation, HandleUtility.GetHandleSize(pivot)); break;
                    default: rotation = Handles.RotationHandle(orientation, pivot); break;
                }
                if (EditorGUI.EndChangeCheck())
                {
                    if (!AnimationMode.InAnimationMode()) Undo.RecordObjects(roots, "Preview Bone Pose");
                    var delta = rotation * Quaternion.Inverse(orientation);
                    var oldScale = active.localScale;
                    var ratio = new Vector3(oldScale.x != 0 ? scale.x / oldScale.x : 1,
                        oldScale.y != 0 ? scale.y / oldScale.y : 1, oldScale.z != 0 ? scale.z / oldScale.z : 1);
                    foreach (var bone in roots)
                    {
                        if (UnityEditor.Tools.current == Tool.Move) bone.position += position - pivot;
                        else if (UnityEditor.Tools.current == Tool.Scale) bone.localScale = bone == active ? scale : Vector3.Scale(bone.localScale, ratio);
                        else { bone.position = pivot + delta * (bone.position - pivot); bone.rotation = delta * bone.rotation; }
                        RememberPreviewWrite(bone);
                    }
                    SceneView.RepaintAll();
                }
            }
            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt &&
                GUIUtility.hotControl == 0 && HandleUtility.nearestControl == m_Control)
            {
                var bone = NearestBone(evt.mousePosition);
                if (bone != null) m_Session.SelectBone(bone, evt.control || evt.command || evt.shift, toggleSelected: false);
                else if (!evt.control && !evt.command && !evt.shift) m_Session.cache.skeletonSelection.Clear();
                evt.Use(); UpdateUI(); scene.Repaint();
            }
        }

        Transform[] PreviewSelectionRoots()
        {
            var selected = m_Session.cache.skeletonSelection.elements
                .Where(bone => bone != null && m_LiveBoneBindings.ContainsKey(bone.guid))
                .Select(bone => m_LiveBoneBindings[bone.guid].transform).Where(bone => bone != null).Distinct().ToArray();
            return selected.Where(bone => !selected.Any(parent => parent != bone && bone.IsChildOf(parent))).ToArray();
        }

        void MovePreviewBones(Vector3 movement, Quaternion rotation, Vector3 pivot)
        {
            var roots = PreviewSelectionRoots();
            if (roots.Length == 0) return;
            if (!AnimationMode.InAnimationMode()) Undo.RecordObjects(roots, "Preview Bone Pose");
            foreach (var bone in roots)
            {
                bone.position = pivot + rotation * (bone.position - pivot) + movement;
                bone.rotation = rotation * bone.rotation;
                RememberPreviewWrite(bone);
            }
        }

        void PreviewBoneMoveGUI(SceneView scene)
        {
            var evt = Event.current;
            m_PoseView ??= new SkeletonView(new PoseGUI());
            m_PoseView.defaultControlID = m_Control;
            m_PoseView.mode = SkeletonMode.EditPose;
            m_PoseView.BeginLayout();
            if (m_PoseView.CanLayout())
                foreach (var bone in m_Session.mesh.bones)
                {
                    WorldBoneSegment(bone, out var start, out var end);
                    m_PoseView.LayoutBone(bone.GetObjectId(), start, end, Vector3.forward, Vector3.up, Vector3.right, false,
                        BoneIconUtility.Normalize(bone.iconId) != BoneIconUtility.Default);
                }
            m_PoseView.EndLayout();
            if (m_PoseView.DoSelectBone(out var id, out var additive))
            {
                var bone = UnityEditorObjectCompatibility.FindObject(id) as BoneCache;
                if (bone != null) m_Session.SelectBone(bone, additive, toggleSelected: false);
            }
            var hot = UnityEditorObjectCompatibility.FindObject(m_PoseView.hotBoneID) as BoneCache;
            var hovered = UnityEditorObjectCompatibility.FindObject(m_PoseView.hoveredBoneID) as BoneCache;
            var target = hot != null ? hot : hovered;
            if (target != null && m_LiveBoneBindings.TryGetValue(target.guid, out var binding))
            {
                var roots = PreviewSelectionRoots();
                var pivot = roots.FirstOrDefault(root => binding.transform == root || binding.transform.IsChildOf(root));
                if (pivot != null && m_PoseView.DoRotateBone(pivot.position, Vector3.forward, out var angle))
                    MovePreviewBones(Vector3.zero, Quaternion.AngleAxis(angle, Vector3.forward), pivot.position);
            }
            if (m_PoseView.DoMoveBone(out var delta)) MovePreviewBones(delta, Quaternion.identity, Vector3.zero);
            m_PoseView.DrawCursors(true);
            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt && GUIUtility.hotControl == 0 && hovered == null)
            {
                if (!evt.control && !evt.command && !evt.shift) m_Session.cache.skeletonSelection.Clear();
                evt.Use();
            }
            if (evt.rawType == EventType.MouseUp && hot != null) GUIUtility.hotControl = 0;
            if (evt.type != EventType.Layout && evt.type != EventType.Repaint) { UpdateUI(); scene.Repaint(); }
        }

        void SetBindMode(bool enabled)
        {
            if (m_Bind == enabled) return;
            EndGesture();
            if (enabled)
            {
                m_Session.mesh.ClearVertexPositionOverride();
                m_FirstBindPositions = m_Session.mesh.boneCount == 0 ? (Vector2[])m_Session.mesh.vertices.Clone() : null;
                m_Bind = true;
                return;
            }
            var firstPositions = m_FirstBindPositions;
            m_LiveBoneBindings.Clear();
            m_CharacterLiveBindings.Clear();
            m_CharacterBindingsLoaded = false;
            m_RowKey = null;
            m_Session.mesh.ClearVertexPositionOverride();
            m_Session.context.RestoreAfterUndo();
            m_Session.InvalidateDrawOrder();
            RefreshLiveBoneBindings();
            ClearSortedPreview();
            if (firstPositions != null && m_Session.mesh.boneCount > 0)
                UpdateBindings(firstPositions);
            m_Session.DrawIndices();
            m_FirstBindPositions = null;
            m_Bind = false;
        }

        void RefreshLiveBoneBindings()
        {
            m_LiveBoneBindings.Clear();
            var instance = PrefabUtility.GetNearestPrefabInstanceRoot(m_Renderer.gameObject);
            var host = instance != null ? instance.transform : m_Renderer.transform.root;
            if (!m_CharacterBindingsLoaded)
            {
                m_CharacterBindingsLoaded = true;
                var characterProvider = m_Source.provider.GetDataProvider<ICharacterDataProvider>();
                var bones = characterProvider != null ? characterProvider.GetCharacterData().bones : m_Renderer.sprite.GetBones();
                if (bones != null)
                    for (var i = 0; i < bones.Length; ++i)
                    {
                        if (string.IsNullOrEmpty(bones[i].guid)) continue;
                        var transform = SceneBoneInspector.FindCharacterTransform(host, bones, i);
                        if (transform != null)
                            m_CharacterLiveBindings[bones[i].guid] = (transform, characterProvider != null ?
                                SceneBoneInspector.CharacterBoneLocalLength(m_Source.provider, bones[i].length) : bones[i].length);
                    }
            }
            foreach (var skin in host.GetComponentsInChildren<SpriteSkin>(true))
            {
                var sprite = skin.GetComponent<SpriteRenderer>()?.sprite;
                if (sprite == null || UniskinRigUtility.SourcePath(sprite) != m_Session.context.assetPath) continue;
                var bones = sprite.GetBones(); var transforms = skin.boneTransforms;
                if (transforms == null) continue;
                for (var i = 0; i < Mathf.Min(bones.Length, transforms.Length); ++i)
                    if (transforms[i] != null && !string.IsNullOrEmpty(bones[i].guid))
                        if (skin.gameObject == m_Renderer.gameObject || !m_LiveBoneBindings.ContainsKey(bones[i].guid))
                            m_LiveBoneBindings[bones[i].guid] = (transforms[i], bones[i].length);
            }
            foreach (var binding in m_CharacterLiveBindings)
                if (binding.Value.transform != null && !m_LiveBoneBindings.ContainsKey(binding.Key))
                    m_LiveBoneBindings[binding.Key] = binding.Value;
        }

        void WorldBoneSegment(BoneCache bone, out Vector3 start, out Vector3 end)
        {
            if (m_LiveBoneBindings.TryGetValue(bone.guid, out var binding) && binding.transform != null)
            {
                start = binding.transform.position;
                end = binding.transform.TransformPoint(Vector3.right * binding.length);
                return;
            }
            var matrix = m_Session.context.SkeletonToWorld(m_Renderer, m_Source.rect.rect);
            start = matrix.MultiplyPoint3x4(bone.position);
            end = matrix.MultiplyPoint3x4(bone.endPosition);
        }

        void Cancel()
        {
            if (m_Weld)
            {
                m_Weld = false; m_Error = null;
                EndGesture(); UpdateUI(); SceneView.RepaintAll(); return;
            }
            ClearBoneDrop();
            EndGesture();
            if (m_Bind) Run(() => SetBindMode(false));
            m_Session.vertices.EndSelection(false);
            m_Session.SelectVertices(Array.Empty<int>());
            UpdateUI(); SceneView.RepaintAll();
        }

        void EndGesture()
        {
            if (m_PoseView != null && (m_PoseView.IsActionHot(SkeletonAction.RotateBone) || m_PoseView.IsActionHot(SkeletonAction.MoveBone)))
                GUIUtility.hotControl = 0;
            m_PoseView = null;
            if (m_Control != 0 && GUIUtility.hotControl == m_Control) GUIUtility.hotControl = 0;
            m_Gesture = Gesture.None;
            m_Session?.context?.cache?.EndUndoOperation();
        }

        Matrix4x4[] BoneDeformationMatrices()
        {
            var mesh = m_Session.mesh;
            var result = new Matrix4x4[mesh.boneCount];
            var pixelToLocal = SceneMeshBindingPose.PixelsToLocal(m_Renderer.sprite, m_Source.rect.rect);
            var pixelToWorld = m_Renderer.transform.localToWorldMatrix * pixelToLocal;
            var skin = m_Renderer.GetComponent<SpriteSkin>();
            var transforms = skin != null ? skin.boneTransforms : null;
            var bones = m_Renderer.sprite.GetBones();
            var poses = m_Renderer.sprite.GetBindPoses();
            var skeletonToWorld = m_Session.context.SkeletonToWorld(m_Renderer, m_Source.rect.rect);
            var localToSkeleton = m_Session.cache.selectedSprite.GetLocalToWorldMatrixFromMode();
            for (var i = 0; i < result.Length; ++i)
            {
                result[i] = pixelToWorld;
                var bone = mesh.bones[i];
                var binding = m_Session.bindingPose?.FindBone(bone.guid);
                if (binding.HasValue && m_LiveBoneBindings.TryGetValue(bone.guid, out var live) && live.transform != null)
                    result[i] = live.transform.localToWorldMatrix * Matrix4x4.Scale(Vector3.one / m_Source.provider.pixelsPerUnit) * binding.Value;
                else
                {
                    if (skin == null || !skin.isActiveAndEnabled) continue;
                    var index = Array.FindIndex(bones, item => item.guid == bone.guid);
                    result[i] = transforms != null && index >= 0 && index < transforms.Length && index < poses.Length && transforms[index] != null ?
                        transforms[index].localToWorldMatrix * poses[index] * pixelToLocal :
                        skeletonToWorld * m_Session.context.CapturedDeformation(bone) * localToSkeleton;
                }
            }
            return result;
        }

        Vector3[] WorldVertices(bool applyFlip = true)
        {
            var mesh = m_Session.mesh;
            mesh.ClearVertexPositionOverride();
            var matrices = BoneDeformationMatrices();
            var result = new Vector3[mesh.vertexCount];
            var pixelToWorld = m_Renderer.transform.localToWorldMatrix * SceneMeshBindingPose.PixelsToLocal(m_Renderer.sprite, m_Source.rect.rect);
            var flip = m_Renderer.transform.localToWorldMatrix * Matrix4x4.Scale(new Vector3(m_Renderer.flipX ? -1 : 1, m_Renderer.flipY ? -1 : 1, 1)) * m_Renderer.transform.worldToLocalMatrix;
            for (var i = 0; i < result.Length; ++i)
            {
                var rest = pixelToWorld.MultiplyPoint3x4(mesh.vertices[i]);
                var sum = 0f; var posed = Vector3.zero;
                foreach (var channel in mesh.vertexWeights[i])
                {
                    if (!channel.enabled || channel.weight <= 0 || channel.boneIndex < 0 || channel.boneIndex >= matrices.Length) continue;
                    posed += matrices[channel.boneIndex].MultiplyPoint3x4(mesh.vertices[i]) * channel.weight;
                    sum += channel.weight;
                }
                result[i] = sum > 0 ? posed / sum : rest;
                if (applyFlip) result[i] = flip.MultiplyPoint3x4(result[i]);
            }
            return result;
        }

        void UpdateBindings() => UpdateBindings(null);

        void UpdateBindings(Vector2[] referencePositions)
        {
            RefreshLiveBoneBindings();
            var toWorld = m_Renderer.transform.localToWorldMatrix * SceneMeshBindingPose.PixelsToLocal(m_Renderer.sprite, m_Source.rect.rect);
            var worldToPixels = toWorld.inverse;
            var matrices = m_Session.mesh.bones.Select(bone =>
            {
                if (!m_LiveBoneBindings.TryGetValue(bone.guid, out var binding) || binding.transform == null)
                    throw new InvalidOperationException("No scene Transform is available for " + bone.name + ". Save newly created bones before updating bindings.");
                var matrix = binding.transform.localToWorldMatrix;
                if (!SceneMeshBindingPose.IsFinite(matrix) || Mathf.Abs(matrix.determinant) < 1e-12f)
                    throw new InvalidOperationException("The bound bones need non-zero scale.");
                return Matrix4x4.Scale(Vector3.one * m_Source.provider.pixelsPerUnit) * matrix.inverse * toWorld;
            }).ToArray();
            var positions = referencePositions ?? WorldVertices(false).Select(point => (Vector2)worldToPixels.MultiplyPoint3x4(point)).ToArray();
            m_Session.UpdateBindings(positions, matrices);
            if (m_PreviewPoseBaseline.Count > 0) CapturePreviewBindPose();
        }

        void BeforeSceneGUI(SceneView scene)
        {
            TrackPreviewAnimation();
            m_EscapeHandledBeforeSceneGUI = false;
            if (m_Session?.context?.cache == null || m_Renderer == null || Event.current == null)
            {
                RestoreTool();
                return;
            }
            if (!m_Saving && BlockReason() == null)
            {
                if (!m_HidesRenderer) { m_PreviousForceRenderingOff = m_Renderer.forceRenderingOff; m_HidesRenderer = true; }
                m_Renderer.forceRenderingOff = true;
                if (spriteOnlyPreview && Event.current.type == EventType.Repaint)
                {
                    RefreshLiveBoneBindings();
                    UpdateSortedPreview(WorldVertices());
                }
                else if (!spriteOnlyPreview) ClearSortedPreview();
            }
            if (!m_Saving)
            {
                if (BlockReason() != null) RestoreTool();
                else toolScope.Hide();
            }
            var evt = Event.current;
            if (!m_Saving && BlockReason() == null && PickWeightTarget(scene)) return;
            if (m_Weld && !m_Saving && BlockReason() == null && evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
            {
                PickWeldSource(evt.mousePosition);
                evt.Use(); scene.Repaint(); return;
            }
            if (m_Saving ||
                evt.type != EventType.KeyDown || evt.keyCode != KeyCode.Escape) return;
            // Unimator's duringSceneGui handler clears Unity selection and consumes Escape.
            // Claim the key in the earlier phase, while it is still a KeyDown event.
            m_EscapeHandledBeforeSceneGUI = true;
            Cancel(); evt.Use();
        }

        void OnSceneGUI(SceneView scene)
        {
            if (m_Saving) return;
            if (m_Session?.context?.cache == null || m_Renderer == null || Event.current == null)
            {
                RestoreTool();
                return;
            }
            // Scene View can consume Escape before duringSceneGui runs. rawType survives Use().
            // Handle it before preview/blocking/control setup so local unselection still works.
            var evt = Event.current;
            if (evt.rawType == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                // The same event reaches both phases; never unselect twice for one key press.
                if (!m_EscapeHandledBeforeSceneGUI && m_Session?.cache != null) Cancel();
                evt.Use(); return;
            }
            if (BlockReason() != null) { RestoreTool(); return; }
            RefreshLiveBoneBindings();
            var world = WorldVertices();
            toolScope.Hide();
            if (!m_HidesRenderer) { m_PreviousForceRenderingOff = m_Renderer.forceRenderingOff; m_HidesRenderer = true; m_Renderer.forceRenderingOff = true; }
            m_ModePopup?.SetValueWithoutNotify(m_Direct ? "Direct" : m_RemoveMode || evt.alt ? "Remove" : "Add");
            m_Control = GUIUtility.GetControlID("UniskinSceneWeights".GetHashCode(), FocusType.Passive);
            if (evt.type == EventType.Layout) HandleUtility.AddDefaultControl(m_Control);
            if (m_PreviewPose)
            {
                if (evt.type == EventType.Repaint && m_Triangles)
                    DrawMeshEdges(world.Select(HandleUtility.WorldToGUIPoint).ToArray(), false);
                PreviewPoseGUI(scene);
                return;
            }
            var screen = world.Select(HandleUtility.WorldToGUIPoint).ToArray();
            if (evt.type == EventType.Repaint)
            {
                if (!m_Preview) DrawPreview(world);
                if (!m_Preview)
                {
                    DrawMeshEdges(screen);
                    if (scene.drawGizmos) DrawBones();
                    DrawPieces(screen);
                    if (!m_Weld && !m_Bind && !m_Direct && !evt.control && !evt.command) DrawBrush(evt.mousePosition);
                    if (m_Gesture == Gesture.Select) DrawSelectionRect(m_GestureStart, evt.mousePosition);
                }
                if (scene.drawGizmos && m_Preview && m_Bind) DrawBones();
            }
            if (m_Direct) EditorGUIUtility.AddCursorRect(new Rect(Vector2.zero, scene.position.size), MouseCursor.Arrow);
            if (m_Weld)
            {
                EditorGUIUtility.AddCursorRect(new Rect(Vector2.zero, scene.position.size), MouseCursor.Link);
                if (evt.type == EventType.MouseMove) scene.Repaint();
                return;
            }
            if (evt.rawType == EventType.MouseUp && m_Gesture != Gesture.None)
            { EndGesture(); UpdateUI(); scene.Repaint(); evt.Use(); return; }
            if (evt.button != 0 && evt.type != EventType.MouseMove) return;
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                m_GestureStart = evt.mousePosition;
                m_Additive = evt.control || evt.command || evt.shift;
                var hitBone = scene.drawGizmos ? NearestBone(evt.mousePosition) : null;
                if ((evt.control || evt.command) && hitBone == null && !m_Bind)
                {
                    m_Gesture = Gesture.Select;
                    m_SelectionBefore = m_Session.orderedVertices;
                    var vertex = NearestVertex(screen, evt.mousePosition);
                    if (vertex >= 0)
                        m_Session.SelectVertices(m_SelectionBefore.Contains(vertex) ? m_SelectionBefore.Except(new[] { vertex }).ToArray() : m_SelectionBefore.Concat(new[] { vertex }).ToArray());
                }
                else
                {
                    var selectable = screen;
                    if (m_Selected)
                    {
                        var selected = m_Session.selectedBones;
                        selectable = screen.Select((point, i) => m_Session.vertices.Contains(i) || selected.Any(b => m_Session.Values(i)[b] > 0)
                            ? point : new Vector2(float.PositiveInfinity, float.PositiveInfinity)).ToArray();
                    }
                    var vertex = m_Direct && !m_Bind && !evt.control && !evt.command ? NearestVertex(selectable, evt.mousePosition) : -1;
                    var bone = hitBone;
                    if (vertex >= 0)
                    {
                        if (!m_Session.vertices.Contains(vertex))
                            m_Session.SelectVertices(m_Additive ? m_Session.orderedVertices.Concat(new[] { vertex }).ToArray() : new[] { vertex });
                        m_Gesture = Gesture.Piece;
                    }
                    else if (bone != null)
                    {
                        if (m_Bind) Run(() => m_Session.Bind(bone));
                        else m_Session.SelectBone(bone, m_Additive, toggleSelected: false);
                    }
                    else if (m_Direct && !m_Bind)
                    {
                        m_Gesture = Gesture.Select;
                        m_SelectionBefore = m_Session.orderedVertices;
                        if (!m_Additive)
                        {
                            m_Session.vertices.EndSelection(false);
                            m_Session.SelectVertices(Array.Empty<int>());
                        }
                    }
                    else if (!m_Bind && !m_Direct && m_Session.selectedBones.Length > 0)
                    {
                        m_Gesture = Gesture.Paint; m_LastStamp = evt.mousePosition;
                        Stamp(screen, evt.mousePosition, BrushRadius(), evt.alt);
                    }
                }
                if (m_Gesture != Gesture.None) GUIUtility.hotControl = m_Control;
                evt.Use(); UpdateUI(); scene.Repaint(); return;
            }
            if (evt.type == EventType.MouseDrag && m_Gesture != Gesture.None && GUIUtility.hotControl == m_Control)
            {
                if (m_Gesture == Gesture.Select && Vector2.Distance(m_GestureStart, evt.mousePosition) > 3)
                {
                    var rect = SelectionRect(m_GestureStart, evt.mousePosition);
                    var inside = Enumerable.Range(0, screen.Length).Where(i => rect.Contains(screen[i]));
                    m_Session.SelectVertices(m_Additive ? m_SelectionBefore.Union(inside).ToArray() : inside.ToArray());
                }
                else if (m_Gesture == Gesture.Piece && m_Session.selectedBones.Length > 0)
                {
                    m_Session.BeginWeightUndo("Drag Vertex Weights");
                    foreach (var vertex in m_Session.targets) m_Session.Adjust(vertex, m_Session.selectedBones, -evt.delta.y * .0025f * m_Strength / 100f);
                    m_Session.Changed();
                }
                else if (m_Gesture == Gesture.Paint)
                {
                    var radius = BrushRadius();
                    var distance = Vector2.Distance(m_LastStamp, evt.mousePosition);
                    var step = Mathf.Max(2, radius * .25f);
                    var count = Mathf.Min(64, Mathf.FloorToInt(distance / step));
                    var origin = m_LastStamp;
                    for (var i = 1; i <= count; ++i)
                    {
                        m_LastStamp = Vector2.Lerp(origin, evt.mousePosition, Mathf.Min(1, i * step / distance));
                        Stamp(screen, m_LastStamp, radius, evt.alt);
                    }
                }
                evt.Use(); UpdateUI(); scene.Repaint();
            }
            if (evt.type == EventType.MouseMove) scene.Repaint();
        }

        static int NearestVertex(Vector2[] vertices, Vector2 point)
        {
            var nearest = -1; var distance = 12f / EditorGUIUtility.pixelsPerPoint;
            for (var i = 0; i < vertices.Length; ++i)
            {
                var next = Vector2.Distance(vertices[i], point);
                if (next < distance) { nearest = i; distance = next; }
            }
            return nearest;
        }

        BoneCache NearestBone(Vector2 point)
        {
            BoneCache nearest = null; var distance = 10f / EditorGUIUtility.pixelsPerPoint;
            foreach (var bone in m_Session.context.skeleton.bones.Where(bone => !bone.IsConstraintParent()))
            {
                // Bind picks only new candidates; ordinary editing picks only this mesh's bones.
                if (m_Session.mesh.ContainsBone(bone) == m_Bind) continue;
                WorldBoneSegment(bone, out var start, out var end);
                var a = HandleUtility.WorldToGUIPoint(start);
                var b = BoneIconUtility.Normalize(bone.iconId) == BoneIconUtility.Default ? HandleUtility.WorldToGUIPoint(end) : a;
                var delta = b - a;
                var t = delta.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude) : 0;
                var next = Vector2.Distance(point, a + delta * t);
                if (next < distance) { distance = next; nearest = bone; }
            }
            return nearest;
        }

        float BrushRadius()
        {
            var matrix = SceneMeshSession.PixelToWorld(m_Renderer, m_Source.rect.rect);
            var center = m_Session.mesh.frame.size * .5f;
            return Mathf.Max(2, Vector2.Distance(HandleUtility.WorldToGUIPoint(matrix.MultiplyPoint3x4(center)),
                HandleUtility.WorldToGUIPoint(matrix.MultiplyPoint3x4(center + Vector2.right * m_Size))));
        }

        void Stamp(Vector2[] points, Vector2 center, float radius, bool altRemove)
        {
            var selected = m_Session.selectedBones;
            if (selected.Length == 0) return;
            var inner = radius * (1 - m_Feather / 100f);
            foreach (var vertex in m_Session.targets)
            {
                var distance = Vector2.Distance(points[vertex], center);
                if (distance > radius) continue;
                var falloff = distance <= inner || radius <= inner ? 1 : (radius - distance) / (radius - inner);
                if (falloff <= 0 || m_Strength == 0) continue;
                m_Session.BeginWeightUndo("Paint Mesh Weights");
                m_Session.Adjust(vertex, selected, m_Strength / 100f * k_BrushStrengthScale * falloff * (m_RemoveMode || altRemove ? -1 : 1));
            }
            m_Session.Changed();
        }

        void DrawBones()
        {
            using (new Handles.DrawingScope(Color.white, Matrix4x4.identity))
                foreach (var bone in m_Session.context.skeleton.bones.Where(bone => !bone.IsConstraintParent()))
                {
                    var bound = m_Session.mesh.ContainsBone(bone);
                    var color = bound ? WeightColor(bone) : new Color(.65f, .65f, .65f, m_Bind ? 1f : .25f);
                    if (m_Bind && bound) color = new Color(.3f, .3f, .3f, .4f);
                    var scale = bound && !m_Bind && m_Session.cache.skeletonSelection.Contains(bone) ? 1.15f : 1;
                    WorldBoneSegment(bone, out var start, out var end);
                    if (BoneIconUtility.Normalize(bone.iconId) == BoneIconUtility.Default)
                    {
                        BoneDrawingUtility.DrawBone(start, end, Vector3.forward, color, scale);
                        BoneDrawingUtility.DrawBoneNode(start, Vector3.forward, color, scale);
                    }
                    else
                    {
                        if (scale > 1) BoneIconUtility.Draw(start, bone.iconId, Handles.selectedColor, true, bone.iconSize);
                        BoneIconUtility.Draw(start, bone.iconId, color, iconSize: bone.iconSize);
                    }
                }
        }

        readonly HashSet<int2> m_TriangleEdges = new HashSet<int2>();

        void DrawMeshEdges(Vector2[] positions, bool drawAuthoredEdges = true)
        {
            Handles.BeginGUI();
            try
            {
                using (new Handles.DrawingScope(Color.white, Matrix4x4.identity))
                {
                    if (m_Triangles)
                    {
                        Handles.color = MeshEditorSettings.weightTriangleColor;
                        m_TriangleEdges.Clear();
                        var indices = m_Session.mesh.indices;
                        for (var i = 0; i + 2 < indices.Length; i += 3)
                        {
                            DrawTriangleEdge(indices[i], indices[i + 1]);
                            DrawTriangleEdge(indices[i + 1], indices[i + 2]);
                            DrawTriangleEdge(indices[i + 2], indices[i]);
                        }
                    }
                    if (drawAuthoredEdges)
                    {
                        DrawEdges(m_Session.mesh.edges, new Color(1f, .65f, 0f));
                        // Draw last so boundary edges also present in the authored list remain cyan.
                        DrawEdges(m_Session.mesh.outlineEdges, Color.cyan);
                    }
                }
            }
            finally { Handles.EndGUI(); }

            void DrawTriangleEdge(int a, int b)
            {
                if (a < 0 || b < 0 || a >= positions.Length || b >= positions.Length || a == b) return;
                if (!m_TriangleEdges.Add(new int2(Math.Min(a, b), Math.Max(a, b)))) return;
                Handles.DrawAAPolyLine(2f, positions[a], positions[b]);
            }

            void DrawEdges(int2[] edges, Color color)
            {
                Handles.color = color;
                foreach (var edge in edges)
                {
                    if (edge.x < 0 || edge.y < 0 || edge.x >= positions.Length || edge.y >= positions.Length) continue;
                    Handles.DrawAAPolyLine(2f, positions[edge.x], positions[edge.y]);
                }
            }
        }

        void DrawPieces(Vector2[] positions)
        {
            var selected = m_Session.selectedBones;
            var boneOrder = m_Session.orderedBoneIndices;
            var bones = m_Session.mesh.bones;
            Handles.BeginGUI();
            try
            {
                for (var vertex = 0; vertex < positions.Length; ++vertex)
                {
                    var values = m_Session.Values(vertex);
                    var active = m_Session.vertices.Contains(vertex);
                    if (m_Selected && !active && !selected.Any(i => values[i] > 0)) continue;
                    var radius = (m_Pieces ? (active ? 6f : 5f) : (active ? 3f : 2f)) / EditorGUIUtility.pixelsPerPoint;
                    var start = 0f;
                    if (!m_Pieces)
                    {
                        // Pieces changes only the visual; dots use the same selection and weight gestures.
                        Handles.color = Color.cyan;
                        Handles.DrawSolidDisc(positions[vertex], Vector3.forward, radius);
                    }
                    else
                    {
                        foreach (var bone in boneOrder)
                        {
                            if (values[bone] <= 0) continue;
                            var color = WeightColor(bones[bone]);
                            Handles.color = color;
                            Handles.DrawSolidArc(positions[vertex], Vector3.forward, Quaternion.Euler(0, 0, start) * Vector3.up, values[bone] * 360, radius);
                            start += values[bone] * 360;
                        }
                        if (start == 0) { Handles.color = Color.gray; Handles.DrawSolidDisc(positions[vertex], Vector3.forward, radius); }
                    }
                    if (active) { Handles.color = Color.white; Handles.DrawWireDisc(positions[vertex], Vector3.forward, radius + 1); }
                }
            }
            finally { Handles.EndGUI(); Handles.color = Color.white; }
        }

        void DrawPreview(Vector3[] vertices)
        {
            if (m_Source?.clipping != null && !m_Overlay) return;
            if (m_OverlayMesh == null) m_OverlayMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            if (m_OverlayMaterial == null)
            {
                var shader = ResourceLoader.Load<Shader>("SkinningModule/SceneWeightsPreview.shader");
                if (shader == null) return;
                m_OverlayMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            var mesh = m_Session.mesh; var bones = mesh.bones; var selected = m_Session.selectedBones;
            var colors = new Color[vertices.Length]; var uv = new Vector2[vertices.Length];
            var textureRect = m_Renderer.sprite.textureRect;
            var texture = m_Source.clipping != null ? Texture2D.whiteTexture : m_Renderer.sprite.texture;
            for (var vertex = 0; vertex < vertices.Length; ++vertex)
            {
                var color = Color.black;
                foreach (var channel in mesh.vertexWeights[vertex])
                    if (channel.enabled && channel.boneIndex >= 0 && channel.boneIndex < bones.Length && (!m_Selected || selected.Contains(channel.boneIndex)))
                        color += WeightColor(bones[channel.boneIndex]) * channel.weight;
                color.a = m_Renderer.color.a; colors[vertex] = color;
                var texturePosition = m_Session.bindingPose?.TexturePosition(mesh.vertices[vertex], vertex) ?? mesh.vertices[vertex];
                var pixel = textureRect.position - m_Renderer.sprite.textureRectOffset + Vector2.Scale(texturePosition / m_Source.rect.rect.size, m_Renderer.sprite.rect.size);
                uv[vertex] = pixel / new Vector2(texture.width, texture.height);
            }
            m_OverlayMesh.Clear(); m_OverlayMesh.vertices = vertices; m_OverlayMesh.colors = colors; m_OverlayMesh.uv = uv; m_OverlayMesh.triangles = m_Session.DrawIndices();
            m_OverlayMaterial.SetTexture("_MainTex", texture);
            m_OverlayMaterial.SetFloat("_Overlay", m_Overlay && !m_Preview ? 1 : 0);
            m_OverlayMaterial.SetColor("_Tint", m_Renderer.color);
            m_OverlayMaterial.SetFloat("_AdjustLinearForGamma", m_Preview && QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);
            if (m_OverlayMaterial.SetPass(0)) Graphics.DrawMeshNow(m_OverlayMesh, Matrix4x4.identity);
        }

        void DrawBrush(Vector2 center)
        {
            var selected = m_Session.selectedBones; var color = selected.Length > 0 ? WeightColor(m_Session.mesh.bones[selected[0]]) : Color.gray;
            Handles.BeginGUI();
            Handles.color = color; Handles.DrawWireDisc(center, Vector3.forward, BrushRadius());
            Handles.color = Color.gray; Handles.DrawWireDisc(center, Vector3.forward, BrushRadius() * (1 - m_Feather / 100f));
            Handles.EndGUI(); Handles.color = Color.white;
        }
        static Rect SelectionRect(Vector2 a, Vector2 b) => Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        static void DrawSelectionRect(Vector2 a, Vector2 b)
        {
            Handles.BeginGUI(); GUI.Box(SelectionRect(a, b), GUIContent.none, "SelectionRect"); Handles.EndGUI();
        }
    }
}
