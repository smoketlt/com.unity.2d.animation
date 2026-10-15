using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Animation.Tests
{
    public sealed class UniskinMeshMaskTests
    {
        readonly List<Object> m_Objects = new List<Object>();
        Scene m_Scene;
        Camera m_Camera;
        RenderTexture m_Target;
        Texture2D m_Read;
        SpriteRenderer m_Source, m_Inside;
        UniskinMeshMask m_Mask;

        [SetUp]
        public void Setup()
        {
            m_Scene = EditorSceneManager.NewPreviewScene();
            m_Camera = Go("Camera").AddComponent<Camera>();
            m_Camera.scene = m_Scene; m_Camera.orthographic = true; m_Camera.orthographicSize = 1;
            m_Camera.transform.position = new Vector3(0, 0, -10);
            m_Camera.clearFlags = CameraClearFlags.SolidColor; m_Camera.backgroundColor = Color.clear;
            m_Target = new RenderTexture(128, 128, 24); m_Target.Create(); m_Objects.Add(m_Target);
            m_Camera.targetTexture = m_Target;
            m_Read = new Texture2D(128, 128, TextureFormat.RGBA32, false); m_Objects.Add(m_Read);
            m_Source = Renderer("Head", .8f); m_Source.color = Color.clear;
            m_Mask = m_Source.gameObject.AddComponent<UniskinMeshMask>();
            m_Inside = Renderer("Inside", 1.8f); m_Inside.sortingOrder = 1;
            m_Inside.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            m_Mask.Refresh();
        }

        [TearDown]
        public void Cleanup()
        {
            if (m_Scene.IsValid()) EditorSceneManager.ClosePreviewScene(m_Scene);
            foreach (var obj in m_Objects) if (obj != null) Object.DestroyImmediate(obj);
            m_Objects.Clear();
        }

        GameObject Go(string name)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, m_Scene); return go;
        }

        SpriteRenderer Renderer(string name, float size)
        {
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var pixels = new Color[256]; for (var i = 0; i < pixels.Length; ++i) pixels[i] = Color.white;
            texture.SetPixels(pixels); texture.Apply(); m_Objects.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.one * .5f, 16 / size, 0, SpriteMeshType.FullRect);
            m_Objects.Add(sprite);
            var renderer = Go(name).AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
            var material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")); m_Objects.Add(material);
            renderer.sharedMaterial = material; return renderer;
        }

        Color Pixel(float x, float y = 0)
        {
            SortingGroup.UpdateAllSortingGroups();
            m_Camera.Render(); var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = m_Target; m_Read.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); m_Read.Apply();
                return m_Read.GetPixel(Mathf.Clamp((int)((x / 2 + .5f) * 128), 0, 127), Mathf.Clamp((int)((y / 2 + .5f) * 128), 0, 127));
            }
            finally { RenderTexture.active = previous; }
        }

        [Test]
        public void NativeInteractionClipsMultipleSpritesAndSupportsOutside()
        {
            Assert.That(Pixel(0).a, Is.GreaterThan(.9f));
            Assert.That(Pixel(.65f).a, Is.LessThan(.01f));
            var second = Renderer("Second", 1.8f); second.sortingOrder = 2; second.color = Color.red;
            second.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            Assert.That(Pixel(0).r, Is.GreaterThan(.9f)); Assert.That(Pixel(.65f).a, Is.LessThan(.01f));
            second.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            Assert.That(Pixel(.65f).r, Is.GreaterThan(.9f));
            second.maskInteraction = SpriteMaskInteraction.None;
            Assert.That(Pixel(.65f).r, Is.GreaterThan(.9f));
        }

        [Test]
        public void BonePoseMovesMaskGeometryWithoutChangingSourceOrUVs()
        {
            var sprite = m_Source.sprite;
            var bone = Go("Bone").transform;
            var bones = new[] { new SpriteBone { name = "Bone", length = 1, parentId = -1, rotation = Quaternion.identity } };
            sprite.SetBones(bones);
            using (var poses = new NativeArray<Matrix4x4>(new[] { Matrix4x4.identity }, Allocator.Temp)) sprite.SetBindPoses(poses);
            var weights = new NativeArray<BoneWeight>(sprite.GetVertexCount(), Allocator.Temp);
            for (var i = 0; i < weights.Length; ++i) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
            sprite.SetVertexAttribute(VertexAttribute.BlendWeight, weights); weights.Dispose();
            var skin = m_Source.gameObject.AddComponent<SpriteSkin>(); skin.SetRootBone(bone); skin.SetBoneTransforms(new[] { bone });
            var vertices = sprite.vertices; var uv = sprite.uv;
            bone.position = Vector3.right * .5f;
            m_Mask.Refresh();
            Assert.That(Pixel(-.2f).a, Is.LessThan(.01f));
            Assert.That(Pixel(.65f).a, Is.GreaterThan(.9f));
            CollectionAssert.AreEqual(vertices, sprite.vertices); CollectionAssert.AreEqual(uv, sprite.uv);
            var helper = m_Source.GetComponentInChildren<SpriteMask>();
            CollectionAssert.AreEqual(uv, helper.sprite.uv);
            m_Source.flipX = true; m_Mask.Refresh();
            Assert.That(Pixel(-.65f).a, Is.GreaterThan(.9f)); Assert.That(Pixel(.65f).a, Is.LessThan(.01f));
        }

        [Test]
        public void AlphaFollowsUVsAfterGeometryChanges()
        {
            var texture = m_Source.sprite.texture;
            var pixels = texture.GetPixels();
            for (var i = 0; i < pixels.Length; ++i) if (i % 16 >= 8) pixels[i] = Color.clear;
            texture.SetPixels(pixels); texture.Apply();
            Assert.That(Pixel(-.2f).a, Is.GreaterThan(.9f)); Assert.That(Pixel(.2f).a, Is.LessThan(.01f));
            m_Source.flipX = true; m_Mask.Refresh();
            Assert.That(Pixel(-.2f).a, Is.LessThan(.01f)); Assert.That(Pixel(.2f).a, Is.GreaterThan(.9f));
        }

        [Test]
        public void DisableAndSpriteSwapPreserveSourceAndTargetSettings()
        {
            var material = m_Source.sharedMaterial;
            m_Mask.enabled = false;
            Assert.That(Pixel(0).a, Is.LessThan(.01f));
            Assert.AreSame(material, m_Source.sharedMaterial);
            Assert.AreEqual(SpriteMaskInteraction.VisibleInsideMask, m_Inside.maskInteraction);
            m_Mask.enabled = true;
            m_Source.sprite = m_Inside.sprite; m_Mask.Refresh();
            Assert.That(Pixel(.65f).a, Is.GreaterThan(.9f));
            m_Source.enabled = false; m_Mask.Refresh();
            Assert.That(Pixel(0).a, Is.LessThan(.01f));
        }

        [Test]
        public void IdleRefreshAllocatesNoManagedMemory()
        {
            for (var i = 0; i < 30; ++i) m_Mask.Refresh();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; ++i) m_Mask.Refresh();
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Test]
        public void SortingGroupsIsolateOverlappingCharacters()
        {
            var root = Go("First character"); root.AddComponent<SortingGroup>();
            m_Source.transform.SetParent(root.transform, false); m_Inside.transform.SetParent(root.transform, false);
            m_Mask.Refresh();
            var other = Renderer("Second character", 1.8f);
            var otherRoot = Go("Second root");
            var group = otherRoot.AddComponent<SortingGroup>(); group.sortingOrder = 5;
            other.transform.SetParent(otherRoot.transform, false);
            other.color = Color.red; other.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            Assert.That(Pixel(0).g, Is.GreaterThan(.9f), "Another character must not see the first character's mask.");
            Assert.That(Pixel(.65f).a, Is.LessThan(.01f));
            other.maskInteraction = SpriteMaskInteraction.None;
            Assert.That(Pixel(0).g, Is.LessThan(.01f));
        }

        [Test]
        public void InPlaceMeshAndUVEditsRefreshWithoutTouchingTheSource()
        {
            var sprite = m_Source.sprite;
            var positions = new NativeArray<Vector3>(sprite.GetVertexCount(), Allocator.Temp);
            sprite.GetVertexAttribute<Vector3>(VertexAttribute.Position).CopyTo(positions);
            for (var i = 0; i < positions.Length; ++i) positions[i] += Vector3.right * .5f;
            sprite.SetVertexAttribute(VertexAttribute.Position, positions); positions.Dispose();
            m_Mask.Refresh();
            Assert.That(Pixel(-.2f).a, Is.LessThan(.01f)); Assert.That(Pixel(.65f).a, Is.GreaterThan(.9f));
            var expected = sprite.vertices;
            m_Mask.enabled = false;
            CollectionAssert.AreEqual(expected, sprite.vertices);
        }

        [Test]
        public void CloneOwnsItsHelperAndDeactivationRemovesMaskImmediately()
        {
            var root = Go("Character");
            m_Source.transform.SetParent(root.transform, false);
            var clone = Object.Instantiate(root); SceneManager.MoveGameObjectToScene(clone, m_Scene);
            var mask = clone.GetComponentInChildren<UniskinMeshMask>(); mask.Refresh();
            var originalHelper = m_Source.GetComponentInChildren<SpriteMask>();
            var cloneHelper = clone.GetComponentInChildren<SpriteMask>();
            Assert.AreNotSame(originalHelper, cloneHelper); Assert.AreNotSame(originalHelper.sprite, cloneHelper.sprite);
            clone.SetActive(false);
            Assert.IsTrue(originalHelper.enabled); Assert.IsFalse(cloneHelper.enabled);
            root.SetActive(false);
            Assert.That(Pixel(0).a, Is.LessThan(.01f));
            root.SetActive(true); m_Mask.Refresh();
            typeof(UniskinMeshMask).GetMethod("DestroyDeferred", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(m_Mask, new object[] { originalHelper.gameObject });
            Assert.IsNotNull(originalHelper, "Deferred cleanup must not destroy a reclaimed helper.");
            Assert.That(Pixel(0).a, Is.GreaterThan(.9f));
        }

        [Test]
        public void PosedBoundsRemainVisibleWhenOriginalSourceIsOutsideCamera()
        {
            m_Source.transform.position = Vector3.right * 5;
            var sprite = m_Source.sprite;
            var positions = new NativeArray<Vector3>(sprite.GetVertexCount(), Allocator.Temp);
            sprite.GetVertexAttribute<Vector3>(VertexAttribute.Position).CopyTo(positions);
            for (var i = 0; i < positions.Length; ++i) positions[i] -= Vector3.right * 5;
            sprite.SetVertexAttribute(VertexAttribute.Position, positions); positions.Dispose();
            m_Mask.Refresh();
            Assert.That(Pixel(0).a, Is.GreaterThan(.9f));
            Assert.That(Pixel(.65f).a, Is.LessThan(.01f));
        }

        [Test]
        public void InspectorUsesLayerNamesAndOffersNoContourEditor()
        {
            var editor = UnityEditor.Editor.CreateEditor(m_Mask, typeof(UniskinMeshMaskEditor));
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root);
                Assert.AreEqual(2, root.Query<UnityEngine.UIElements.PopupField<string>>().ToList().Count);
                Assert.AreEqual(0, root.Query<UnityEngine.UIElements.Button>().ToList().Count);
            }
            finally { Object.DestroyImmediate(editor); }
        }

        [Test]
        public void BlendedBonesDeformTheSilhouetteWithoutChangingSkinningSettings()
        {
            var sprite = m_Source.sprite;
            var root = Go("Root bone").transform; var upper = Go("Upper bone").transform;
            upper.SetParent(root, false);
            sprite.SetBones(new[] {
                new SpriteBone { name = "Root bone", parentId = -1, rotation = Quaternion.identity },
                new SpriteBone { name = "Upper bone", parentId = 0, rotation = Quaternion.identity }
            });
            using (var poses = new NativeArray<Matrix4x4>(new[] { Matrix4x4.identity, Matrix4x4.identity }, Allocator.Temp)) sprite.SetBindPoses(poses);
            var vertices = sprite.vertices;
            var weights = new NativeArray<BoneWeight>(vertices.Length, Allocator.Temp);
            try
            {
                for (var i = 0; i < weights.Length; ++i)
                    weights[i] = vertices[i].y > 0
                        ? new BoneWeight { boneIndex0 = 0, weight0 = .5f, boneIndex1 = 1, weight1 = .5f }
                        : new BoneWeight { boneIndex0 = 0, weight0 = 1 };
                sprite.SetVertexAttribute(VertexAttribute.BlendWeight, weights);
            }
            finally { weights.Dispose(); }
            var skin = m_Source.gameObject.AddComponent<SpriteSkin>(); skin.SetRootBone(root); skin.SetBoneTransforms(new[] { root, upper });
            var forceCpu = skin.forceCpuDeformation; var material = m_Source.sharedMaterial;
            upper.localPosition = Vector3.right;
            m_Mask.Refresh();
            Assert.That(Pixel(.65f, .3f).a, Is.GreaterThan(.9f));
            Assert.That(Pixel(.65f, -.3f).a, Is.LessThan(.01f));
            Assert.AreEqual(forceCpu, skin.forceCpuDeformation); Assert.AreSame(material, m_Source.sharedMaterial);
            CollectionAssert.AreEqual(vertices, sprite.vertices);
        }

        [Test]
        public void CustomSortingRangeRestrictsWhichSpritesSeeTheMask()
        {
            var settings = new SerializedObject(m_Mask);
            settings.FindProperty("m_BackSortingOrder").intValue = 0;
            settings.FindProperty("m_FrontSortingOrder").intValue = 2;
            settings.ApplyModifiedPropertiesWithoutUndo(); m_Mask.Refresh();
            m_Inside.sortingOrder = 1;
            Assert.That(Pixel(0).a, Is.GreaterThan(.9f));
            m_Inside.sortingOrder = 3;
            Assert.That(Pixel(0).a, Is.LessThan(.01f));
        }
    }
}
