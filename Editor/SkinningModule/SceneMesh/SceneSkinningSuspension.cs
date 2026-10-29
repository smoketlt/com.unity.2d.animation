using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    [InitializeOnLoad]
    internal static class SceneSkinningSuspension
    {
        [Serializable] sealed class NativeSource { public string guid; public string userData; }
        [Serializable] sealed class NativeSources { public List<NativeSource> sources = new List<NativeSource>(); }
        const string k_UndoSources = "Uniskin.SuspendSkinning.NativeUndoSources";
        static NativeSources s_NativeSources;
        static SpriteRenderer s_Renderer;
        static Sprite s_SourceSprite, s_PreviewSprite;
        static GameObject s_Preview;
        static SceneWeightSession s_Session;
        static Vector2[] s_Positions;
        static Transform[] s_Bones;
        static Vector3 s_Position, s_Scale;
        static Quaternion s_Rotation;
        static bool s_ForceRenderingOff;

        internal static bool active => s_Session != null;
        internal static bool IsSuspended(SpriteRenderer renderer) => active && s_Renderer == renderer;

        static SceneSkinningSuspension()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            EditorApplication.quitting += Cancel;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode) Cancel(); };
            EditorApplication.update += Update;
            Undo.undoRedoPerformed += QueueNativeRefresh;
        }

        internal static void Begin(SpriteRenderer renderer, SceneEditMeshWindow owner = null)
        {
            if (active) throw new InvalidOperationException("Resume or cancel Suspend Skinning first.");
            if (renderer == null || renderer.sprite == null || !renderer.gameObject.scene.IsValid() ||
                EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("Suspend Skinning requires a scene mesh in Edit Mode with animation preview stopped.");
            if (SkinningModule.IsEditingAsset(UniskinRigUtility.SourcePath(renderer.sprite)))
                throw new InvalidOperationException("Close Skinning Editor before suspending skinning.");
            foreach (var window in Resources.FindObjectsOfTypeAll<SceneEditMeshWindow>()) if (window != owner && !window.RequestClose()) return;
            foreach (var window in Resources.FindObjectsOfTypeAll<SceneEditBoneWindow>()) if (!window.RequestClose()) return;
            foreach (var window in Resources.FindObjectsOfTypeAll<SceneEditWeightsWindow>()) if (!window.RequestClose()) return;

            var source = new SceneMeshSource(renderer.sprite);
            var skin = renderer.GetComponent<SpriteSkin>();
            if (skin != null) skin.BatchValidate();
            if (source.clipping != null || skin == null || !skin.isActiveAndEnabled || !skin.isValid)
                throw new InvalidOperationException("Suspend Skinning requires a valid enabled SpriteSkin.");
            try
            {
                s_Renderer = renderer;
                s_SourceSprite = renderer.sprite;
                s_ForceRenderingOff = renderer.forceRenderingOff;
                s_Position = renderer.transform.localPosition;
                s_Rotation = renderer.transform.localRotation;
                s_Scale = renderer.transform.localScale;
                s_Session = ScriptableObject.CreateInstance<SceneWeightSession>();
                s_Session.hideFlags = HideFlags.HideAndDontSave;
                s_Session.Load(renderer.sprite, source);
                s_Session.context.CaptureScenePose(renderer, source.rect.rect);
                var spriteBones = renderer.sprite.GetBones();
                s_Bones = s_Session.mesh.bones.Select(bone =>
                {
                    var index = Array.FindIndex(spriteBones, item => item.guid == bone.guid);
                    var transform = index >= 0 && index < skin.boneTransforms.Length ? skin.boneTransforms[index] : null;
                    if (transform == null || transform.IsChildOf(renderer.transform))
                        throw new InvalidOperationException("Bound bones must exist outside the mesh object's hierarchy.");
                    return transform;
                }).ToArray();
                var sample = new SceneSpriteDeformedMesh();
                if (!sample.Sample(renderer) || sample.vertices.Length != s_Session.mesh.vertexCount)
                    throw new InvalidOperationException("The displayed mesh does not match its saved geometry.");
                var toPixels = SceneMeshBindingPose.PixelsToLocal(renderer.sprite, source.rect.rect).inverse;
                var local = sample.vertices.Select(point => renderer.transform.InverseTransformPoint(point)).ToArray();
                for (var i = 0; i < local.Length; ++i)
                {
                    if (renderer.flipX) local[i].x = -local[i].x;
                    if (renderer.flipY) local[i].y = -local[i].y;
                }
                s_Positions = local.Select(point => (Vector2)toPixels.MultiplyPoint3x4(point)).ToArray();
                var sourceIndices = sample.indices.ToArray();
                var sourceUV = renderer.sprite.GetVertexAttribute<Vector2>(VertexAttribute.TexCoord0).ToArray();
                s_PreviewSprite = Sprite.Create(renderer.sprite.texture,
                    new Rect(0, 0, renderer.sprite.texture.width, renderer.sprite.texture.height), Vector2.zero,
                    renderer.sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                s_PreviewSprite.hideFlags = HideFlags.HideAndDontSave;
                s_PreviewSprite.name = "Suspended " + renderer.sprite.name;
                s_PreviewSprite.SetVertexCount(local.Length);
                var previewPositions = new NativeArray<Vector3>(local, Allocator.Temp);
                try { s_PreviewSprite.SetVertexAttribute(VertexAttribute.Position, previewPositions); }
                finally { previewPositions.Dispose(); }
                var indices = new NativeArray<ushort>(sourceIndices, Allocator.Temp);
                try { s_PreviewSprite.SetIndices(indices); }
                finally { indices.Dispose(); }
                var uv = new NativeArray<Vector2>(sourceUV, Allocator.Temp);
                try { s_PreviewSprite.SetVertexAttribute(VertexAttribute.TexCoord0, uv); }
                finally { uv.Dispose(); }
                if (s_PreviewSprite.GetVertexCount() != local.Length ||
                    !s_PreviewSprite.GetIndices().SequenceEqual(sourceIndices) ||
                    !s_PreviewSprite.GetVertexAttribute<Vector3>(VertexAttribute.Position).SequenceEqual(local) ||
                    !s_PreviewSprite.GetVertexAttribute<Vector2>(VertexAttribute.TexCoord0).SequenceEqual(sourceUV))
                    throw new InvalidOperationException("Could not build the suspended mesh preview. The original renderer has been retained.");
                s_Preview = new GameObject("Suspended Skinning Preview") { hideFlags = HideFlags.HideAndDontSave };
                s_Preview.transform.SetParent(renderer.transform, false);
                var preview = s_Preview.AddComponent<SpriteRenderer>();
                preview.sprite = s_PreviewSprite;
                var bounds = new Bounds(local[0], Vector3.zero);
                foreach (var point in local) bounds.Encapsulate(point);
                preview.localBounds = bounds;
                preview.sharedMaterials = renderer.sharedMaterials;
                preview.color = renderer.color;
                preview.flipX = renderer.flipX;
                preview.flipY = renderer.flipY;
                preview.sortingLayerID = renderer.sortingLayerID;
                preview.sortingOrder = renderer.sortingOrder;
                preview.maskInteraction = renderer.maskInteraction;
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                preview.SetPropertyBlock(properties);
                renderer.forceRenderingOff = true;
                Selection.activeGameObject = renderer.gameObject;
                UnityEditor.Tools.current = Tool.Move;
                SceneView.RepaintAll();
            }
            catch { Cleanup(); throw; }
        }

        internal static void Resume()
        {
            if (!active) return;
            Validate();
            var source = new SceneMeshSource(s_SourceSprite);
            var toWorld = s_Renderer.transform.localToWorldMatrix * SceneMeshBindingPose.PixelsToLocal(s_SourceSprite, source.rect.rect);
            var matrices = s_Bones.Select(bone => Matrix4x4.Scale(Vector3.one * source.provider.pixelsPerUnit) * bone.worldToLocalMatrix * toWorld).ToArray();
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Resume Skinning");
            try
            {
                s_Session.UpdateBindings(s_Positions, matrices);
                s_Session.Save(source);
                if (source.rig == null)
                {
                    LoadNativeSources();
                    var guid = AssetDatabase.AssetPathToGUID(source.assetPath);
                    s_NativeSources.sources.RemoveAll(item => item.guid == guid);
                    s_NativeSources.sources.Add(new NativeSource { guid = guid, userData = source.importer.userData });
                    SessionState.SetString(k_UndoSources, JsonUtility.ToJson(s_NativeSources));
                }
            }
            finally { Undo.CollapseUndoOperations(undoGroup); }
            Cleanup();
        }

        internal static void TrackNativeSource(AssetImporter importer)
        {
            if (importer == null) return;
            LoadNativeSources();
            var guid = AssetDatabase.AssetPathToGUID(importer.assetPath);
            s_NativeSources.sources.RemoveAll(item => item.guid == guid);
            s_NativeSources.sources.Add(new NativeSource { guid = guid, userData = importer.userData });
            SessionState.SetString(k_UndoSources, JsonUtility.ToJson(s_NativeSources));
        }

        static void LoadNativeSources()
        {
            if (s_NativeSources == null)
                s_NativeSources = JsonUtility.FromJson<NativeSources>(SessionState.GetString(k_UndoSources, "{}")) ?? new NativeSources();
        }

        static bool s_NativeRefreshQueued;

        static void QueueNativeRefresh()
        {
            if (s_NativeRefreshQueued) return;
            s_NativeRefreshQueued = true;
            EditorApplication.delayCall += RefreshNativeSources;
        }

        internal static void RefreshNativeSources()
        {
            s_NativeRefreshQueued = false;
            EditorApplication.delayCall -= RefreshNativeSources;
            LoadNativeSources();
            foreach (var item in s_NativeSources.sources)
            {
                var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(item.guid));
                if (importer == null || importer.userData == item.userData) continue;
                try
                {
                    importer.SaveAndReimport();
                    item.userData = importer.userData;
                }
                catch (Exception ex) { Debug.LogError("Suspend Skinning Undo refresh failed: " + ex.Message); }
            }
            SessionState.SetString(k_UndoSources, JsonUtility.ToJson(s_NativeSources));
            SceneView.RepaintAll();
        }

        static void Validate()
        {
            if (s_Renderer == null || s_Renderer.sprite != s_SourceSprite || s_Bones.Any(bone => bone == null))
                throw new InvalidOperationException("The mesh or its bones changed. Cancel Suspend Skinning and reopen it.");
            if (AnimationMode.InAnimationMode())
                throw new InvalidOperationException("Stop animation preview before resuming skinning.");
            if (!SceneMeshBindingPose.IsFinite(s_Renderer.transform.localToWorldMatrix) ||
                Mathf.Abs(s_Renderer.transform.localToWorldMatrix.determinant) < 1e-12f ||
                s_Bones.Any(bone => !SceneMeshBindingPose.IsFinite(bone.localToWorldMatrix) || Mathf.Abs(bone.localToWorldMatrix.determinant) < 1e-12f))
                throw new InvalidOperationException("The mesh and bound bones require non-zero scale.");
        }

        internal static void Cancel()
        {
            if (!active) return;
            if (s_Renderer != null)
            {
                Undo.RecordObject(s_Renderer.transform, "Cancel Suspend Skinning");
                s_Renderer.transform.localPosition = s_Position;
                s_Renderer.transform.localRotation = s_Rotation;
                s_Renderer.transform.localScale = s_Scale;
            }
            Cleanup();
        }

        static void Update()
        {
            if (!active) return;
            if (s_Renderer == null || s_Renderer.sprite != s_SourceSprite) { Cancel(); return; }
            if (Selection.activeGameObject != s_Renderer.gameObject || Selection.objects.Length != 1)
                Selection.activeGameObject = s_Renderer.gameObject;
        }

        static void Cleanup()
        {
            if (s_Renderer != null) s_Renderer.forceRenderingOff = s_ForceRenderingOff;
            if (s_Preview != null) UnityEngine.Object.DestroyImmediate(s_Preview);
            if (s_PreviewSprite != null) UnityEngine.Object.DestroyImmediate(s_PreviewSprite);
            if (s_Session != null) UnityEngine.Object.DestroyImmediate(s_Session);
            s_Renderer = null; s_SourceSprite = null; s_Preview = null; s_PreviewSprite = null; s_Session = null;
            s_Positions = null; s_Bones = null;
            SceneView.RepaintAll();
        }
    }
}
