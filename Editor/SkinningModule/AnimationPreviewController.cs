using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal sealed class AnimationPreviewController : IDisposable
    {
        sealed class TransformSnapshot
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        sealed class BoneCurves
        {
            public BoneCache bone;
            public TransformSnapshot snapshot;
            public AnimationCurve positionX;
            public AnimationCurve positionY;
            public AnimationCurve positionZ;
            public AnimationCurve rotationX;
            public AnimationCurve rotationY;
            public AnimationCurve rotationZ;
            public AnimationCurve rotationW;
            public AnimationCurve eulerX;
            public AnimationCurve eulerY;
            public AnimationCurve eulerZ;
            public AnimationCurve scaleX;
            public AnimationCurve scaleY;
            public AnimationCurve scaleZ;

            public bool hasPosition => positionX != null || positionY != null || positionZ != null;
            public bool hasRotation => rotationX != null || rotationY != null || rotationZ != null || rotationW != null;
            public bool hasEuler => eulerX != null || eulerY != null || eulerZ != null;
            public bool hasScale => scaleX != null || scaleY != null || scaleZ != null;
            public bool hasCurves => hasPosition || hasRotation || hasEuler || hasScale;
        }

        readonly SkinningCache m_SkinningCache;
        readonly AnimationPreviewPanel m_Panel;
        readonly Action m_RequestRepaint;
        readonly float m_PixelsPerUnit;
        readonly Dictionary<BoneCache, TransformSnapshot> m_Snapshots = new Dictionary<BoneCache, TransformSnapshot>();
        readonly List<BoneCurves> m_BoneCurves = new List<BoneCurves>();

        AnimationClip m_Clip;
        SkeletonCache m_Skeleton;
        bool m_WasPosePreview;
        bool m_IsPlaying;
        double m_LastUpdateTime;
        float m_CurrentTime;
        int m_MatchedBoneCount;

        public AnimationPreviewController(SkinningCache skinningCache, AnimationPreviewPanel panel, float pixelsPerUnit, Action requestRepaint)
        {
            m_SkinningCache = skinningCache;
            m_Panel = panel;
            m_PixelsPerUnit = pixelsPerUnit > 0f ? pixelsPerUnit : 100f;
            m_RequestRepaint = requestRepaint;

            m_Panel.clipChanged += SetClip;
            m_Panel.firstFrameClicked += OnFirstFrameClicked;
            m_Panel.previousFrameClicked += OnPreviousFrameClicked;
            m_Panel.playPauseClicked += TogglePlayback;
            m_Panel.stopClicked += StopAndRestore;
            m_Panel.nextFrameClicked += OnNextFrameClicked;
            m_Panel.lastFrameClicked += OnLastFrameClicked;
            m_Panel.frameChanged += SetFrame;

            m_SkinningCache.events.selectedSpriteChanged.AddListener(OnEditorTargetChanged);
            m_SkinningCache.events.skinningModeChanged.AddListener(OnSkinningModeChanged);
            m_SkinningCache.events.skeletonTopologyChanged.AddListener(OnSkeletonTopologyChanged);
            UpdatePanel();
        }

        int LastFrame
        {
            get
            {
                if (m_Clip == null)
                    return 0;
                return Mathf.Max(0, Mathf.RoundToInt(m_Clip.length * FrameRate));
            }
        }

        int CurrentFrame => m_Clip == null ? 0 : Mathf.Clamp(Mathf.RoundToInt(m_CurrentTime * FrameRate), 0, LastFrame);

        float FrameRate => m_Clip != null && m_Clip.frameRate > 0f ? m_Clip.frameRate : 60f;

        public void Dispose()
        {
            StopAndRestore();
            EditorApplication.update -= UpdatePlayback;
            m_SkinningCache.events.selectedSpriteChanged.RemoveListener(OnEditorTargetChanged);
            m_SkinningCache.events.skinningModeChanged.RemoveListener(OnSkinningModeChanged);
            m_SkinningCache.events.skeletonTopologyChanged.RemoveListener(OnSkeletonTopologyChanged);

            m_Panel.clipChanged -= SetClip;
            m_Panel.firstFrameClicked -= OnFirstFrameClicked;
            m_Panel.previousFrameClicked -= OnPreviousFrameClicked;
            m_Panel.playPauseClicked -= TogglePlayback;
            m_Panel.stopClicked -= StopAndRestore;
            m_Panel.nextFrameClicked -= OnNextFrameClicked;
            m_Panel.lastFrameClicked -= OnLastFrameClicked;
            m_Panel.frameChanged -= SetFrame;
        }

        void SetClip(AnimationClip clip)
        {
            StopAndRestore();
            m_Clip = clip;
            m_CurrentTime = 0f;
            if (m_Clip != null)
            {
                BindToCurrentSkeleton();
                if (m_BoneCurves.Count > 0)
                    SampleTime(0f);
            }
            UpdatePanel();
        }

        void OnFirstFrameClicked()
        {
            SetFrame(0);
        }

        void OnPreviousFrameClicked()
        {
            SetFrame(CurrentFrame - 1);
        }

        void OnNextFrameClicked()
        {
            SetFrame(CurrentFrame + 1);
        }

        void OnLastFrameClicked()
        {
            SetFrame(LastFrame);
        }

        void BindToCurrentSkeleton()
        {
            m_Skeleton = m_SkinningCache.GetEffectiveSkeleton(m_SkinningCache.selectedSprite);
            m_Snapshots.Clear();
            m_BoneCurves.Clear();
            m_MatchedBoneCount = 0;

            if (m_Clip == null || m_Skeleton == null)
                return;

            m_WasPosePreview = m_Skeleton.isPosePreview;
            BoneCache[] bones = m_Skeleton.bones;
            Dictionary<string, Dictionary<string, AnimationCurve>> curvesByPath = ReadTransformCurves(m_Clip);

            for (int i = 0; i < bones.Length; ++i)
            {
                BoneCache bone = bones[i];
                TransformSnapshot snapshot = Capture(bone);
                m_Snapshots.Add(bone, snapshot);

                string fullPath = GetBonePath(bone);
                string relativePath = GetRootRelativePath(fullPath);
                string bindingPath = FindBindingPath(curvesByPath, fullPath, relativePath);
                if (bindingPath == null)
                    continue;

                BoneCurves curves = CreateBoneCurves(bone, snapshot, curvesByPath[bindingPath]);
                if (!curves.hasCurves)
                    continue;

                m_BoneCurves.Add(curves);
                ++m_MatchedBoneCount;
            }
        }

        static Dictionary<string, Dictionary<string, AnimationCurve>> ReadTransformCurves(AnimationClip clip)
        {
            Dictionary<string, Dictionary<string, AnimationCurve>> result = new Dictionary<string, Dictionary<string, AnimationCurve>>();
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; ++i)
            {
                EditorCurveBinding binding = bindings[i];
                if (binding.type != typeof(Transform) || !IsSupportedTransformProperty(binding.propertyName))
                    continue;

                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null)
                    continue;

                if (!result.TryGetValue(binding.path, out Dictionary<string, AnimationCurve> pathCurves))
                {
                    pathCurves = new Dictionary<string, AnimationCurve>();
                    result.Add(binding.path, pathCurves);
                }
                pathCurves[binding.propertyName] = curve;
            }
            return result;
        }

        static bool IsSupportedTransformProperty(string propertyName)
        {
            return propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal) ||
                propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal) ||
                propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal) ||
                propertyName.StartsWith("localEulerAngles", StringComparison.Ordinal);
        }

        static BoneCurves CreateBoneCurves(BoneCache bone, TransformSnapshot snapshot, Dictionary<string, AnimationCurve> curves)
        {
            BoneCurves result = new BoneCurves
            {
                bone = bone,
                snapshot = snapshot,
                positionX = GetCurve(curves, "m_LocalPosition.x"),
                positionY = GetCurve(curves, "m_LocalPosition.y"),
                positionZ = GetCurve(curves, "m_LocalPosition.z"),
                rotationX = GetCurve(curves, "m_LocalRotation.x"),
                rotationY = GetCurve(curves, "m_LocalRotation.y"),
                rotationZ = GetCurve(curves, "m_LocalRotation.z"),
                rotationW = GetCurve(curves, "m_LocalRotation.w"),
                scaleX = GetCurve(curves, "m_LocalScale.x"),
                scaleY = GetCurve(curves, "m_LocalScale.y"),
                scaleZ = GetCurve(curves, "m_LocalScale.z")
            };

            result.eulerX = GetEulerCurve(curves, "x");
            result.eulerY = GetEulerCurve(curves, "y");
            result.eulerZ = GetEulerCurve(curves, "z");
            return result;
        }

        static AnimationCurve GetCurve(Dictionary<string, AnimationCurve> curves, string propertyName)
        {
            curves.TryGetValue(propertyName, out AnimationCurve curve);
            return curve;
        }

        static AnimationCurve GetEulerCurve(Dictionary<string, AnimationCurve> curves, string axis)
        {
            string[] prefixes = { "localEulerAnglesRaw.", "localEulerAnglesBaked.", "localEulerAngles." };
            for (int i = 0; i < prefixes.Length; ++i)
            {
                AnimationCurve curve = GetCurve(curves, prefixes[i] + axis);
                if (curve != null)
                    return curve;
            }
            return null;
        }

        static string FindBindingPath(Dictionary<string, Dictionary<string, AnimationCurve>> curvesByPath, string fullPath, string relativePath)
        {
            string bestPath = null;
            int bestScore = int.MaxValue;
            foreach (string path in curvesByPath.Keys)
            {
                int score = GetPathMatchScore(path, fullPath, relativePath);
                if (score < bestScore || (score == bestScore && bestPath != null && path.Length < bestPath.Length))
                {
                    bestScore = score;
                    bestPath = path;
                }
            }
            return bestScore == int.MaxValue ? null : bestPath;
        }

        static int GetPathMatchScore(string bindingPath, string fullPath, string relativePath)
        {
            if (bindingPath == fullPath)
                return 0;
            if (bindingPath == relativePath)
                return 1;
            if (!string.IsNullOrEmpty(fullPath) && bindingPath.EndsWith("/" + fullPath, StringComparison.Ordinal))
                return 2;
            if (!string.IsNullOrEmpty(relativePath) && bindingPath.EndsWith("/" + relativePath, StringComparison.Ordinal))
                return 3;
            return int.MaxValue;
        }

        static string GetBonePath(BoneCache bone)
        {
            List<string> names = new List<string>();
            BoneCache current = bone;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parentBone;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        static string GetRootRelativePath(string fullPath)
        {
            int separator = fullPath.IndexOf('/');
            return separator < 0 ? string.Empty : fullPath.Substring(separator + 1);
        }

        void TogglePlayback()
        {
            if (m_Clip == null || m_Skeleton == null || m_BoneCurves.Count == 0)
                return;

            if (m_IsPlaying)
            {
                Pause();
                return;
            }

            if (!m_Panel.loop && m_CurrentTime >= m_Clip.length)
            {
                m_CurrentTime = 0f;
                SampleTime(m_CurrentTime);
            }

            m_IsPlaying = true;
            m_LastUpdateTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= UpdatePlayback;
            EditorApplication.update += UpdatePlayback;
            UpdatePanel();
        }

        void Pause()
        {
            m_IsPlaying = false;
            EditorApplication.update -= UpdatePlayback;
            UpdatePanel();
        }

        void UpdatePlayback()
        {
            if (!m_IsPlaying || m_Clip == null)
                return;

            double now = EditorApplication.timeSinceStartup;
            float deltaTime = (float)(now - m_LastUpdateTime);
            m_LastUpdateTime = now;
            m_CurrentTime += Mathf.Max(0f, deltaTime);

            if (m_Clip.length <= 0f)
            {
                m_CurrentTime = 0f;
                Pause();
            }
            else if (m_CurrentTime > m_Clip.length)
            {
                if (m_Panel.loop)
                    m_CurrentTime = Mathf.Repeat(m_CurrentTime, m_Clip.length);
                else
                {
                    m_CurrentTime = m_Clip.length;
                    Pause();
                }
            }

            SampleTime(m_CurrentTime);
            UpdatePanel();
        }

        void SetFrame(int frame)
        {
            if (m_Clip == null)
                return;

            int targetFrame;
            if (m_Panel.loop && frame < 0)
                targetFrame = LastFrame;
            else if (m_Panel.loop && frame > LastFrame)
                targetFrame = 0;
            else
                targetFrame = Mathf.Clamp(frame, 0, LastFrame);

            m_CurrentTime = Mathf.Min(m_Clip.length, targetFrame / FrameRate);
            SampleTime(m_CurrentTime);
            UpdatePanel();
        }

        void SampleTime(float time)
        {
            if (m_Clip == null || m_Skeleton == null)
                return;

            RestoreSnapshots(false);
            for (int i = 0; i < m_BoneCurves.Count; ++i)
                ApplyCurves(m_BoneCurves[i], time);

            m_Skeleton.SetPosePreview();
            m_SkinningCache.events.skeletonPreviewPoseChanged.Invoke(m_Skeleton);
            m_RequestRepaint?.Invoke();
        }

        void ApplyCurves(BoneCurves curves, float time)
        {
            BoneCache bone = curves.bone;
            TransformSnapshot snapshot = curves.snapshot;

            if (curves.hasPosition)
            {
                Vector3 runtimePosition = EditorToRuntimePosition(bone, snapshot.position);
                runtimePosition.x = Evaluate(curves.positionX, time, runtimePosition.x);
                runtimePosition.y = Evaluate(curves.positionY, time, runtimePosition.y);
                runtimePosition.z = Evaluate(curves.positionZ, time, runtimePosition.z);
                bone.localPosition = RuntimeToEditorPosition(bone, runtimePosition, snapshot.position.z);
            }

            Quaternion rotation = snapshot.rotation;
            if (curves.hasRotation)
            {
                rotation.x = Evaluate(curves.rotationX, time, rotation.x);
                rotation.y = Evaluate(curves.rotationY, time, rotation.y);
                rotation.z = Evaluate(curves.rotationZ, time, rotation.z);
                rotation.w = Evaluate(curves.rotationW, time, rotation.w);
                rotation = Quaternion.Normalize(rotation);
            }
            if (curves.hasEuler)
            {
                Vector3 euler = snapshot.rotation.eulerAngles;
                euler.x = Evaluate(curves.eulerX, time, euler.x);
                euler.y = Evaluate(curves.eulerY, time, euler.y);
                euler.z = Evaluate(curves.eulerZ, time, euler.z);
                rotation = Quaternion.Euler(euler);
            }
            if (curves.hasRotation || curves.hasEuler)
                bone.localRotation = rotation;

            if (curves.hasScale)
            {
                Vector3 scale = snapshot.scale;
                scale.x = Evaluate(curves.scaleX, time, scale.x);
                scale.y = Evaluate(curves.scaleY, time, scale.y);
                scale.z = Evaluate(curves.scaleZ, time, scale.z);
                bone.localScale = scale;
            }
        }

        Vector3 EditorToRuntimePosition(BoneCache bone, Vector3 editorPosition)
        {
            Vector3 origin = GetRootOrigin(bone);
            return (editorPosition - origin) / m_PixelsPerUnit;
        }

        Vector3 RuntimeToEditorPosition(BoneCache bone, Vector3 runtimePosition, float originalZ)
        {
            Vector3 position = runtimePosition * m_PixelsPerUnit + GetRootOrigin(bone);
            position.z = originalZ;
            return position;
        }

        Vector3 GetRootOrigin(BoneCache bone)
        {
            if (bone.parentBone != null || m_SkinningCache.mode != SkinningMode.SpriteSheet || m_SkinningCache.selectedSprite == null)
                return Vector3.zero;
            return m_SkinningCache.selectedSprite.pivotRectSpace;
        }

        static float Evaluate(AnimationCurve curve, float time, float fallback)
        {
            return curve == null ? fallback : curve.Evaluate(time);
        }

        void StopAndRestore()
        {
            Pause();
            RestoreSnapshots(true);
            m_CurrentTime = 0f;
            UpdatePanel();
        }

        void RestoreSnapshots(bool notify)
        {
            if (m_Skeleton == null || m_Snapshots.Count == 0)
                return;

            foreach (KeyValuePair<BoneCache, TransformSnapshot> pair in m_Snapshots)
            {
                if (pair.Key == null)
                    continue;
                pair.Key.localPosition = pair.Value.position;
                pair.Key.localRotation = pair.Value.rotation;
                pair.Key.localScale = pair.Value.scale;
            }

            if (!notify)
                return;

            if (m_WasPosePreview)
            {
                m_Skeleton.SetPosePreview();
                m_SkinningCache.events.skeletonPreviewPoseChanged.Invoke(m_Skeleton);
            }
            else
                m_Skeleton.RestoreDefaultPose();

            m_RequestRepaint?.Invoke();
        }

        static TransformSnapshot Capture(BoneCache bone)
        {
            return new TransformSnapshot
            {
                position = bone.localPosition,
                rotation = bone.localRotation,
                scale = bone.localScale
            };
        }

        void OnEditorTargetChanged(SpriteCache sprite)
        {
            RebindCurrentClip();
        }

        void OnSkinningModeChanged(SkinningMode mode)
        {
            RebindCurrentClip();
        }

        void OnSkeletonTopologyChanged(SkeletonCache skeleton)
        {
            if (skeleton == m_Skeleton)
                RebindCurrentClip();
        }

        void RebindCurrentClip()
        {
            bool resume = m_IsPlaying;
            Pause();
            RestoreSnapshots(true);
            if (m_Clip != null)
            {
                BindToCurrentSkeleton();
                m_CurrentTime = Mathf.Clamp(m_CurrentTime, 0f, m_Clip.length);
                if (m_BoneCurves.Count > 0)
                    SampleTime(m_CurrentTime);
            }
            if (resume)
                TogglePlayback();
            UpdatePanel();
        }

        void UpdatePanel()
        {
            string status;
            if (m_Clip == null)
                status = "Select an AnimationClip";
            else if (m_Skeleton == null)
                status = "No skeleton selected";
            else if (m_BoneCurves.Count == 0)
                status = "No matching bone curves";
            else
                status = $"{m_MatchedBoneCount}/{m_Skeleton.boneCount} bones";

            m_Panel.SetClipState(m_Clip != null && m_Skeleton != null && m_BoneCurves.Count > 0,
                CurrentFrame, LastFrame, m_IsPlaying, status);
        }
    }
}
