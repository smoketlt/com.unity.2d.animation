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
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Animation.Tests
{
    public sealed class SceneSpriteGpuPickingTests
    {
        readonly List<Object> m_Objects = new List<Object>();
        Scene m_Scene;
        SpriteRenderer m_Renderer;
        Camera m_Camera;
        Object[] m_Selection;
        const BindingFlags k_Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static Type EditorType(string name) =>
            Array.Find(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "Unity.2D.Animation.Editor")
                .GetType("UnityEditor.U2D.Animation." + name, true);
        static object Call(string name, params object[] args) => EditorType("SceneSpriteGpuPicking").GetMethod(name, k_Flags).Invoke(null, args);

        GameObject Go(string name)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, m_Scene); return go;
        }

        [SetUp]
        public void Setup()
        {
            m_Selection = Selection.objects;
            m_Scene = EditorSceneManager.NewPreviewScene();
            var texture = new Texture2D(16, 16); m_Objects.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.one * .5f, 16, 0, SpriteMeshType.FullRect);
            m_Objects.Add(sprite);
            m_Renderer = Go("GPU picking fixture").AddComponent<SpriteRenderer>(); m_Renderer.sprite = sprite;
            m_Camera = Go("Camera").AddComponent<Camera>(); m_Camera.orthographic = true;
            m_Camera.transform.position = new Vector3(0, 0, -10);
        }

        [TearDown]
        public void Cleanup()
        {
            Selection.objects = m_Selection;
            if (m_Scene.IsValid()) EditorSceneManager.ClosePreviewScene(m_Scene);
            foreach (var obj in m_Objects) Object.DestroyImmediate(obj);
            m_Objects.Clear();
        }

        [Test]
        public void NativeOutlineFilterKeepsOtherRenderersAndNativeIdentityType()
        {
            var second = Go("Other outline").AddComponent<SpriteRenderer>(); second.sprite = m_Renderer.sprite;
            var identity = typeof(SpriteSkin).Assembly.GetType("UnityEngine.U2D.Animation.UnityObjectCompatibility")
                .GetMethod("GetObjectId", k_Flags);
            var firstId = identity.Invoke(null, new object[] { m_Renderer });
            var secondId = identity.Invoke(null, new object[] { second });
            var compatibility = EditorType("SceneSelectionOutlineCompatibility");
            var values = Array.CreateInstance(firstId.GetType(), 3);
            values.SetValue(firstId, 0); values.SetValue(secondId, 1); values.SetValue(firstId, 2);
            var removed = (Array)compatibility.GetMethod("Remove", k_Flags).Invoke(null,
                new object[] { values, new HashSet<object> { firstId } });
            Assert.AreEqual(values.GetType(), removed.GetType());
            Assert.AreEqual(1, removed.Length); Assert.AreEqual(secondId, removed.GetValue(0));
            Selection.objects = new Object[] { m_Renderer.gameObject, second.gameObject };
            try
            {
                Assert.IsTrue((bool)compatibility.GetMethod("Exclude", k_Flags).Invoke(null, new object[] { new[] { m_Renderer } }));
                var parents = (Array)typeof(SceneView).GetField("s_CachedParentRenderersForOutlining", k_Flags).GetValue(null);
                CollectionAssert.DoesNotContain(parents, firstId);
                CollectionAssert.Contains(parents, secondId);
            }
            finally { compatibility.GetMethod("Invalidate", k_Flags).Invoke(null, null); }
        }

        [Test]
        public void NativePickingCannotRunDuringLayoutRepaintOrOtherNonPrimaryEvents()
        {
            var selection = Selection.objects;
            foreach (var type in new[] { EventType.Layout, EventType.Repaint, EventType.MouseMove, EventType.MouseUp, EventType.KeyDown })
            {
                var evt = new Event { type = type };
                // No SceneView/camera is supplied: entering native picking would fail
                // or render outside a frame. The event and selection must remain untouched.
                Call("PickOnMouseDown", null, evt, null);
                Assert.AreEqual(type, evt.type);
                CollectionAssert.AreEqual(selection, Selection.objects);
            }
            {
                var evt = new Event { type = EventType.MouseDown, button = 1 };
                Call("PickOnMouseDown", null, evt, null);
                Assert.AreEqual(EventType.MouseDown, evt.type);
            }
        }

        [Test]
        public void SourceHitSupportsRendererFlipsAndTransforms()
        {
            var method = EditorType("SceneSpriteDeformedMesh").GetMethod("HitSource", k_Flags);
            bool Hits(Vector3 point) => (bool)method.Invoke(null, new object[] { m_Renderer, new Ray(point + Vector3.back * 10, Vector3.forward) });
            Assert.IsTrue(Hits(Vector3.zero)); Assert.IsFalse(Hits(Vector3.right * 2));
            m_Renderer.transform.SetPositionAndRotation(new Vector3(3, 1, 0), Quaternion.Euler(0, 0, 35));
            m_Renderer.transform.localScale = new Vector3(-2, 3, 1);
            m_Renderer.flipX = m_Renderer.flipY = true;
            Assert.IsTrue(Hits(m_Renderer.transform.position)); Assert.IsFalse(Hits(Vector3.zero));
        }

        [Test]
        public void TriangleHitIsDoubleSidedAndRejectsEmptyOrDegenerateAreas()
        {
            var method = EditorType("SceneSpriteDeformedMesh").GetMethod("Intersect", k_Flags);
            bool Hit(Vector3 a, Vector3 b, Vector3 c, Vector3 point)
            {
                var args = new object[] { new Ray(point + Vector3.back * 10, Vector3.forward), a, b, c, 0f };
                return (bool)method.Invoke(null, args);
            }
            Assert.IsTrue(Hit(Vector3.zero, Vector3.right, Vector3.up, Vector3.one * .2f));
            Assert.IsTrue(Hit(Vector3.zero, Vector3.up, Vector3.right, Vector3.one * .2f));
            Assert.IsFalse(Hit(Vector3.zero, Vector3.right, Vector3.up, Vector3.one * .8f));
            Assert.IsFalse(Hit(Vector3.zero, Vector3.zero, Vector3.up, Vector3.zero));
        }

        [Test]
        public void BonePoseMovesHitAreaAndFlipsWithoutChangingSpriteOrCpuSetting()
        {
            var sprite = m_Renderer.sprite;
            var bone = Go("Bone").transform;
            sprite.SetBones(new[] { new SpriteBone { name = "Bone", parentId = -1, length = 1, rotation = Quaternion.identity } });
            using (var poses = new NativeArray<Matrix4x4>(new[] { Matrix4x4.identity }, Allocator.Temp)) sprite.SetBindPoses(poses);
            var weights = new NativeArray<BoneWeight>(sprite.GetVertexCount(), Allocator.Temp);
            try
            {
                for (var i = 0; i < weights.Length; ++i) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
                sprite.SetVertexAttribute(VertexAttribute.BlendWeight, weights);
            }
            finally { weights.Dispose(); }
            var skin = m_Renderer.gameObject.AddComponent<SpriteSkin>(); skin.SetRootBone(bone); skin.SetBoneTransforms(new[] { bone });
            var original = sprite.vertices;
            var type = EditorType("SceneSpriteDeformedMesh"); var mesh = Activator.CreateInstance(type, true);
            var sample = type.GetMethod("Sample", k_Flags); var hit = type.GetMethod("Hit", k_Flags);
            bool Hits(float x) => (bool)hit.Invoke(mesh, new object[] { new Ray(new Vector3(x, 0, -10), Vector3.forward), 0f });
            Assert.IsTrue((bool)sample.Invoke(mesh, new object[] { m_Renderer })); Assert.IsTrue(Hits(0));
            bone.position = Vector3.right * 2;
            sample.Invoke(mesh, new object[] { m_Renderer }); Assert.IsFalse(Hits(0)); Assert.IsTrue(Hits(2));
            var sourceHit = type.GetMethod("HitSource", k_Flags);
            Assert.IsTrue((bool)sourceHit.Invoke(null, new object[] { m_Renderer, new Ray(Vector3.back * 10, Vector3.forward) }));
            Assert.IsFalse((bool)sourceHit.Invoke(null, new object[] { m_Renderer, new Ray(new Vector3(2, 0, -10), Vector3.forward) }));
            m_Renderer.flipX = true;
            sample.Invoke(mesh, new object[] { m_Renderer }); Assert.IsTrue(Hits(-2)); Assert.IsFalse(Hits(2));
            m_Renderer.transform.SetPositionAndRotation(new Vector3(3, 1, 0), Quaternion.Euler(0, 0, 90));
            m_Renderer.transform.localScale = new Vector3(-2, 3, 1);
            sample.Invoke(mesh, new object[] { m_Renderer });
            var source = sprite.GetVertexAttribute<Vector3>(VertexAttribute.Position);
            var expected = bone.TransformPoint(source[0]); expected = m_Renderer.transform.InverseTransformPoint(expected);
            expected.x = -expected.x; expected = m_Renderer.transform.TransformPoint(expected);
            var vertices = (Vector3[])type.GetField("vertices", k_Flags).GetValue(mesh);
            Assert.That(Vector3.Distance(expected, vertices[0]), Is.LessThan(.0001f));
            CollectionAssert.AreEqual(original, sprite.vertices); Assert.IsFalse(skin.forceCpuDeformation);
            skin.enabled = false; sample.Invoke(mesh, new object[] { m_Renderer });
            expected = source[0]; expected.x = -expected.x; expected = m_Renderer.transform.TransformPoint(expected);
            Assert.That(Vector3.Distance(expected, vertices[0]), Is.LessThan(.0001f));
        }

        [Test]
        public void SortingUsesSharedGroupContentsAndSeparateOuterGroups()
        {
            var second = Go("Second").AddComponent<SpriteRenderer>(); second.sprite = m_Renderer.sprite;
            int Compare() => (int)Call("Compare", m_Renderer, second, m_Camera);
            m_Renderer.sortingOrder = 10; Assert.Greater(Compare(), 0);
            var groupA = Go("Group A").AddComponent<SortingGroup>(); groupA.sortingOrder = -2;
            var groupB = Go("Group B").AddComponent<SortingGroup>(); groupB.sortingOrder = 2;
            m_Renderer.transform.SetParent(groupA.transform); second.transform.SetParent(groupB.transform);
            Assert.Less(Compare(), 0);
            second.transform.SetParent(groupA.transform); Assert.Greater(Compare(), 0);
            second.sortingOrder = m_Renderer.sortingOrder;
            second.transform.position = Vector3.forward; Assert.Greater(Compare(), 0);
            m_Camera.transparencySortMode = TransparencySortMode.CustomAxis; m_Camera.transparencySortAxis = Vector3.up;
            second.transform.position = Vector3.up; Assert.Greater(Compare(), 0);
            var inner = Go("Inner").AddComponent<SortingGroup>(); inner.transform.SetParent(groupA.transform);
            inner.sortingOrder = 5; m_Renderer.transform.SetParent(inner.transform); second.transform.SetParent(groupB.transform);
            Assert.Less(Compare(), 0);
            inner.sortAtRoot = true; Assert.Greater(Compare(), 0);
            inner.sortAtRoot = false; m_Renderer.transform.SetParent(groupA.transform); second.transform.SetParent(groupA.transform);
            groupA.enabled = false; second.sortingOrder = 20; Assert.Less(Compare(), 0);
        }

        [Test]
        public void SelectionModifiersPreserveExistingObjectsAndToggleOnlyClickedSprite()
        {
            var other = Go("Other"); Selection.activeGameObject = other;
            Call("Select", m_Renderer.gameObject, true, false);
            Assert.IsTrue(Selection.Contains(other)); Assert.IsTrue(Selection.Contains(m_Renderer.gameObject));
            Call("Select", m_Renderer.gameObject, false, true);
            Assert.IsTrue(Selection.Contains(other)); Assert.IsFalse(Selection.Contains(m_Renderer.gameObject));
            Call("Select", null, true, false); Assert.AreEqual(other, Selection.activeGameObject);
            Call("Select", null, false, false); Assert.AreEqual(0, Selection.objects.Length);
        }

        [Test]
        public void DisabledInvalidAndUnregisteredSkinsKeepNativePicking()
        {
            Assert.IsFalse((bool)Call("UsesGpu", m_Renderer));
            var skin = m_Renderer.gameObject.AddComponent<SpriteSkin>();
            Assert.IsFalse((bool)Call("UsesGpu", m_Renderer));
            skin.enabled = false; Assert.IsFalse((bool)Call("UsesGpu", m_Renderer));
        }
    }
}
