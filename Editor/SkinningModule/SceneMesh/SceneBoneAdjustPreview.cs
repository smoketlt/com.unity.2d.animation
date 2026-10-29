using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    internal sealed class SceneBoneAdjustPreview
    {
        readonly SceneBoneSession m_Session;
        readonly SpriteRenderer m_Renderer;
        readonly SceneMeshSource m_Source;
        readonly Dictionary<string, Matrix4x4> m_Baseline;
        readonly Dictionary<Transform, (Vector3 position, Quaternion rotation, Vector3 scale)> m_Local = new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();
        readonly Dictionary<Transform, string> m_Bones;
        readonly Dictionary<Sprite, Matrix4x4[]> m_Bindings = new Dictionary<Sprite, Matrix4x4[]>();
        internal bool updating { get; private set; }

        internal SceneBoneAdjustPreview(SceneBoneSession session, SpriteRenderer renderer, SceneMeshSource source, Dictionary<string, Matrix4x4> baseline)
        {
            m_Session = session; m_Renderer = renderer; m_Source = source; m_Baseline = baseline;
            m_Bones = session.FindSceneBindings(renderer).GroupBy(pair => pair.Value.transform).ToDictionary(group => group.Key, group => group.First().Key);
            var roots = new HashSet<Transform>(m_Bones.Keys);
            foreach (var item in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
            {
                if (!item.gameObject.scene.IsValid() || EditorUtility.IsPersistent(item) || item.sprite == null) continue;
                var contour = UniskinClippingMaskUtility.Find(item.sprite);
                if (UniskinRigUtility.SourcePath(item.sprite) != source.assetPath && (source.rig == null || contour?.rig != source.rig)) continue;
                var skin = item.GetComponent<SpriteSkin>();
                if (skin != null && skin.boneTransforms != null)
                    foreach (var bone in skin.boneTransforms) if (bone != null) roots.Add(bone);
            }
            foreach (var root in roots)
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    m_Local[transform] = (transform.localPosition, transform.localRotation, transform.localScale);
            foreach (var rect in source.provider.GetSpriteRects()) Capture(source.ResolveSprite(rect.spriteID.ToString()));
            if (source.rig != null)
                foreach (var guid in AssetDatabase.FindAssets("t:UniskinClippingMaskData"))
                {
                    var contour = AssetDatabase.LoadAssetAtPath<UniskinClippingMaskData>(AssetDatabase.GUIDToAssetPath(guid));
                    if (contour != null && contour.rig == source.rig && contour.sprite != null) Capture(contour.sprite);
                }
        }

        void Capture(Sprite sprite) { if (sprite != null && !m_Bindings.ContainsKey(sprite)) m_Bindings[sprite] = sprite.GetBindPoses().ToArray(); }

        internal void Restore()
        {
            var wasUpdating = updating; updating = true;
            try
            {
                foreach (var pair in m_Local)
                {
                    if (pair.Key == null) continue;
                    pair.Key.localPosition = pair.Value.position;
                    pair.Key.localRotation = pair.Value.rotation;
                    pair.Key.localScale = pair.Value.scale;
                }
                foreach (var pair in m_Bindings)
                {
                    if (pair.Key == null) continue;
                    var poses = new NativeArray<Matrix4x4>(pair.Value, Allocator.Temp);
                    try { pair.Key.SetBindPoses(poses); }
                    finally { poses.Dispose(); }
                }
            }
            finally { updating = wasUpdating; }
            RefreshSkins();
        }

        void RefreshSkins()
        {
            foreach (var skin in Resources.FindObjectsOfTypeAll<SpriteSkin>())
            {
                var sprite = skin.GetComponent<SpriteRenderer>()?.sprite;
                if (skin.isActiveAndEnabled && sprite != null && m_Bindings.ContainsKey(sprite)) skin.RefreshBindPoseData();
            }
        }

        internal void Update()
        {
            if (updating) return;
            updating = true;
            try
            {
                Restore();
                m_Session.CreateAdjustmentPlan(m_Source, m_Baseline)?.ApplyPreview();
                RefreshSkins();
            }
            catch { Restore(); throw; }
            finally { updating = false; }
        }

        internal bool ReadInspectorChanges(IEnumerable<Transform> transforms)
        {
            if (updating) return false;
            var toSkeleton = m_Session.SkeletonToWorld(m_Renderer, m_Source.rect.rect).inverse;
            var changes = transforms.Where(transform => transform != null && m_Bones.ContainsKey(transform)).Distinct()
                .Select(transform => (guid: m_Bones[transform], position: toSkeleton.MultiplyPoint3x4(transform.position),
                    direction: toSkeleton.MultiplyVector(transform.TransformVector(Vector3.right)))).ToArray();
            if (changes.Length == 0) return false;
            updating = true;
            try
            {
                foreach (var change in changes)
                {
                    var bone = m_Session.skeleton.bones.FirstOrDefault(item => item.guid == change.guid);
                    if (bone != null) m_Session.AdjustPose(bone, change.position,
                        Quaternion.Euler(0, 0, Mathf.Atan2(change.direction.y, change.direction.x) * Mathf.Rad2Deg));
                }
            }
            finally { updating = false; }
            Update();
            return true;
        }
    }
}
