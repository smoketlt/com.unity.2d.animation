using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Animation.Tests
{
    public sealed class UniskinClippingMaskTests
    {
        Scene m_Scene;
        readonly List<Object> m_Objects = new List<Object>();
        Camera m_Camera;
        RenderTexture m_Target;
        Texture2D m_Read;

        [SetUp]
        public void Setup()
        {
            m_Scene = EditorSceneManager.NewPreviewScene();
            var cameraObject = GameObject("Clip Check Camera"); m_Camera = cameraObject.AddComponent<Camera>();
            m_Camera.scene = m_Scene; m_Camera.orthographic = true; m_Camera.orthographicSize = .6f;
            m_Camera.transform.position = new Vector3(0, 0, -10); m_Camera.clearFlags = CameraClearFlags.SolidColor; m_Camera.backgroundColor = Color.clear;
            m_Target = new RenderTexture(96, 96, 24, RenderTextureFormat.ARGB32); m_Target.Create(); m_Objects.Add(m_Target); m_Camera.targetTexture = m_Target;
            m_Read = new Texture2D(96, 96, TextureFormat.RGBA32, false); m_Objects.Add(m_Read);
        }

        [TearDown]
        public void Cleanup()
        {
            if (m_Scene.IsValid()) EditorSceneManager.ClosePreviewScene(m_Scene);
            foreach (var obj in m_Objects) if (obj != null) Object.DestroyImmediate(obj);
            m_Objects.Clear();
        }

        GameObject GameObject(string name)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, m_Scene); return go;
        }

        Sprite Sprite(float size, Func<int, Color> pixel = null)
        {
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            texture.SetPixels(Enumerable.Range(0, 1024).Select(i => pixel != null ? pixel(i % 32) : Color.white).ToArray()); texture.Apply(); m_Objects.Add(texture);
            var sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, 32, 32), Vector2.one * .5f, 32 / size, 0, SpriteMeshType.FullRect);
            m_Objects.Add(sprite); return sprite;
        }

        UniskinClippingMask Clip(Color color, float position = 0, int order = 0, Sprite bodySprite = null)
        {
            var root = GameObject("Character");
            var rig = ScriptableObject.CreateInstance<UniskinRig>(); m_Objects.Add(rig);
            var character = root.AddComponent<UniskinRigBinding>(); character.rig = rig;
            var go = GameObject("Body"); go.transform.SetParent(root.transform, false); go.transform.localPosition = new Vector3(position, 0, 0);
            var body = go.AddComponent<SpriteRenderer>(); body.sprite = bodySprite != null ? bodySprite : Sprite(1, x => color); body.sortingOrder = order;
            var original = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")); m_Objects.Add(original); body.sharedMaterial = original;
            var writerObject = GameObject("Contour"); writerObject.transform.SetParent(go.transform, false);
            var writer = writerObject.AddComponent<SpriteRenderer>(); writer.sprite = Sprite(.5f);
            var data = ScriptableObject.CreateInstance<UniskinClippingMaskData>(); m_Objects.Add(data); data.rig = rig; data.source = writer.sprite; data.sprite = writer.sprite; data.hasGeometry = true;
            var clip = go.AddComponent<UniskinClippingMask>(); clip.character = character; clip.contour = data; clip.contourRenderer = writer; clip.Refresh(); return clip;
        }

        Color Pixel(float x, float y = 0)
        {
            m_Camera.Render();
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = m_Target; m_Read.ReadPixels(new Rect(0, 0, 96, 96), 0, 0); m_Read.Apply();
                return m_Read.GetPixel(Mathf.Clamp(Mathf.RoundToInt((x / 1.2f + .5f) * 96), 0, 95), Mathf.Clamp(Mathf.RoundToInt((y / 1.2f + .5f) * 96), 0, 95));
            }
            finally { RenderTexture.active = previous; }
        }

        [Test]
        public void StencilClipsTheBodyAndClearsAfterItsSortingGroup()
        {
            var clip = Clip(Color.red);
            Assert.That(Pixel(0).r, Is.GreaterThan(.95f)); Assert.That(Pixel(.4f).a, Is.LessThan(.01f));
            var probe = GameObject("Stencil leak probe").AddComponent<SpriteRenderer>(); probe.sprite = Sprite(1, x => Color.green);
            probe.sharedMaterial = Resources.Load<Material>("UniskinClipBodyUnlit"); probe.sortingOrder = 2;
            Assert.That(Pixel(0).r, Is.GreaterThan(.95f), "A later stencil-tested Sprite must not see this contour after cleanup.");
            Assert.That(Pixel(0).g, Is.LessThan(.01f));
            Assert.IsFalse(ShaderUtil.ShaderHasError(clip.GetComponent<SpriteRenderer>().sharedMaterial.shader));
            Assert.IsFalse(ShaderUtil.ShaderHasError(clip.contourRenderer.sharedMaterial.shader));
        }

        [Test]
        public void InvertHidesInsideAndTogglesWithoutChangingSharedMaterials()
        {
            var clip = Clip(Color.red);
            var body = clip.GetComponent<SpriteRenderer>();
            var update = (Action)Delegate.CreateDelegate(typeof(Action), clip, typeof(UniskinClippingMask).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance));
            var normal = body.sharedMaterial;
            foreach (var lit in new[] { false, true })
            {
                clip.enabled = false;
                var original = new Material(Resources.Load<Material>("UniskinMask")); m_Objects.Add(original);
                original.SetFloat("_UniskinUseLighting", lit ? 1 : 0); body.sharedMaterial = original;
                clip.enabled = true;
                clip.invert = true; update();
                Assert.AreSame(Resources.Load<Material>(lit ? "UniskinClipBodyInverted" : "UniskinClipBodyInvertedUnlit"), body.sharedMaterial);
                Assert.That(Pixel(0).a, Is.LessThan(.01f));
                Assert.That(Pixel(.4f).r, Is.GreaterThan(.95f));
                clip.invert = false; update();
                Assert.That(Pixel(0).r, Is.GreaterThan(.95f));
                Assert.That(Pixel(.4f).a, Is.LessThan(.01f));
            }
            Assert.AreEqual(128, normal.GetFloat("_UniskinStencilRef"));
        }

        [Test]
        public void InvertedContourClearsStencilBeforeTheNextCharacter()
        {
            var inverted = Clip(Color.red, -.2f); inverted.invert = true; inverted.Refresh();
            Clip(Color.blue, .2f, 1);
            Assert.That(Pixel(-.1f).a, Is.LessThan(.01f));
            Assert.That(Pixel(.1f).b, Is.GreaterThan(.95f), "The preceding inverted mask must clear its stencil.");
            Assert.That(Pixel(.55f).a, Is.LessThan(.01f));
        }

        [Test]
        public void InspectorInvertUsesSerializedStateAndUndo()
        {
            var clip = Clip(Color.red);
            var editor = Editor.CreateEditor(clip); m_Objects.Add(editor);
            var ui = editor.CreateInspectorGUI();
            var field = UnityEngine.UIElements.UQueryExtensions.Q<UnityEditor.UIElements.PropertyField>(ui, "uniskinClippingInvert");
            Assert.IsNotNull(field); Assert.AreEqual("invert", field.bindingPath);
            Assert.IsFalse(clip.invert);
            Undo.IncrementCurrentGroup();
            editor.serializedObject.Update();
            editor.serializedObject.FindProperty("invert").boolValue = true;
            editor.serializedObject.ApplyModifiedProperties(); Undo.FlushUndoRecordObjects();
            Assert.IsTrue(clip.invert);
            Undo.PerformUndo(); Assert.IsFalse(clip.invert);
            Undo.PerformRedo(); Assert.IsTrue(clip.invert);
            Undo.ClearUndo(clip);
        }

        [Test]
        public void MovingBodyInteriorVerticesChangesTextureWithoutMovingTheContour()
        {
            var sprite = Sprite(1.6f, x => x < 16 ? Color.red : Color.blue);
            var vertices = new[] { new Vector2(-.8f, -.8f), new Vector2(0, -.8f), new Vector2(.8f, -.8f), new Vector2(-.8f, .8f), new Vector2(0, .8f), new Vector2(.8f, .8f) };
            var uv = new[] { new Vector2(0, 0), new Vector2(.5f, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(.5f, 1), new Vector2(1, 1) };
            var triangles = new ushort[] { 0, 1, 3, 3, 1, 4, 1, 2, 4, 4, 2, 5 };
            void Geometry()
            {
                sprite.SetVertexCount(vertices.Length);
                var positions = new Unity.Collections.NativeArray<Vector3>(vertices.Select(v => (Vector3)v).ToArray(), Unity.Collections.Allocator.Temp);
                var indices = new Unity.Collections.NativeArray<ushort>(triangles, Unity.Collections.Allocator.Temp);
                try { sprite.SetVertexAttribute(VertexAttribute.Position, positions); sprite.SetIndices(indices); }
                finally { positions.Dispose(); indices.Dispose(); }
                var stream = new Unity.Collections.NativeArray<Vector2>(uv, Unity.Collections.Allocator.Temp);
                try { sprite.SetVertexAttribute(VertexAttribute.TexCoord0, stream); } finally { stream.Dispose(); }
            }
            Geometry(); var clip = Clip(Color.white, bodySprite: sprite);
            var initial = Pixel(-.08f);
            Assert.That(initial.r, Is.GreaterThan(.9f), "Initial body pixel: " + initial);
            vertices[1].x = vertices[4].x = -.2f; Geometry();
            Assert.That(Pixel(-.08f).b, Is.GreaterThan(.9f));
            Assert.That(Pixel(.3f).a, Is.LessThan(.01f)); Assert.That(Pixel(-.3f).a, Is.LessThan(.01f));
            Assert.That(clip.contourRenderer.bounds.size.x, Is.EqualTo(.5f).Within(.0001f));
        }

        [Test]
        public void OverlappingCharactersKeepIndependentClippingGroups()
        {
            Clip(Color.red, -.2f, 0); Clip(Color.blue, .2f, 1);
            Assert.That(Pixel(-.35f).r, Is.GreaterThan(.95f));
            Assert.That(Pixel(.35f).b, Is.GreaterThan(.95f));
            Assert.That(Pixel(.55f).a, Is.LessThan(.01f)); Assert.That(Pixel(-.55f).a, Is.LessThan(.01f));
        }

        [Test]
        public void ClonedCharactersReuseTheirOwnStencilHelpers()
        {
            var clip = Clip(Color.red);
            var root = Object.Instantiate(clip.transform.parent.gameObject);
            SceneManager.MoveGameObjectToScene(root, m_Scene);
            var clone = root.GetComponentInChildren<UniskinClippingMask>();
            clone.Refresh();
            Assert.AreSame(root.transform, clone.character.transform);
            Assert.AreEqual(1, clone.GetComponents<SortingGroup>().Length);
            Assert.AreEqual(1, clone.GetComponentsInChildren<SpriteRenderer>(true).Count(renderer => renderer.name == "Uniskin Clip Clear"));
            Object.DestroyImmediate(clip.transform.parent.gameObject);
            Assert.That(Pixel(0).r, Is.GreaterThan(.95f)); Assert.That(Pixel(.4f).a, Is.LessThan(.01f));
        }

        [Test]
        public void DeactivationAndImmediateReactivationReuseDisabledHelpers()
        {
            var clip = Clip(Color.red, order: 12);
            var body = clip.GetComponent<SpriteRenderer>();
            var group = body.GetComponent<SortingGroup>();
            var clear = body.GetComponentsInChildren<SpriteRenderer>(true).Single(renderer => renderer.name == "Uniskin Clip Clear");
            clip.gameObject.SetActive(false);
            Assert.IsFalse(group.enabled); Assert.IsFalse(clear.enabled);
            Assert.AreEqual("Universal Render Pipeline/2D/Sprite-Unlit-Default", body.sharedMaterial.shader.name);
            clip.gameObject.SetActive(true);
            Assert.AreSame(group, body.GetComponent<SortingGroup>()); Assert.IsTrue(group.enabled);
            Assert.AreEqual(1, body.GetComponents<SortingGroup>().Length);
            Assert.AreSame(clear, body.GetComponentsInChildren<SpriteRenderer>(true).Single(renderer => renderer.name == "Uniskin Clip Clear"));
            Assert.That(Pixel(0).r, Is.GreaterThan(.95f));
        }

        [Test]
        public void DisableRestoresMaterialSortingAndIdleUpdatesAllocateNothing()
        {
            var clip = Clip(Color.white, order: 12);
            var body = clip.GetComponent<SpriteRenderer>();
            var update = (Action)Delegate.CreateDelegate(typeof(Action), clip, typeof(UniskinClippingMask).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance));
            for (var i = 0; i < 50; ++i) update();
            var before = GC.GetAllocatedBytesForCurrentThread(); for (var i = 0; i < 1000; ++i) update();
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
            clip.enabled = false;
            Assert.AreEqual("Universal Render Pipeline/2D/Sprite-Unlit-Default", body.sharedMaterial.shader.name);
            Assert.AreEqual(12, body.sortingOrder); Assert.IsNull(body.GetComponent<SortingGroup>()); Assert.IsFalse(clip.contourRenderer.enabled);
        }

        [Test]
        public void SpriteRendererOrderControlsTheGroupIncludingZeroAndDisable()
        {
            var clip = Clip(Color.red, order: 15); var body = clip.GetComponent<SpriteRenderer>();
            var update = (Action)Delegate.CreateDelegate(typeof(Action), clip, typeof(UniskinClippingMask).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance));
            foreach (var order in new[] { 15, 0, -4, 22 })
            {
                body.sortingOrder = order; update();
                Assert.AreEqual(order, body.sortingOrder);
                Assert.AreEqual(order, body.GetComponent<SortingGroup>().sortingOrder);
                Assert.AreEqual(order - 1, clip.contourRenderer.sortingOrder);
                Assert.AreEqual(order + 1, body.GetComponentsInChildren<SpriteRenderer>(true).Single(r => r.name == "Uniskin Clip Clear").sortingOrder);
                Assert.That(Pixel(0).r, Is.GreaterThan(.95f)); Assert.That(Pixel(.4f).a, Is.LessThan(.01f));
            }
            body.sortingOrder = 7; clip.enabled = false; Assert.AreEqual(7, body.sortingOrder);
            clip.enabled = true; Assert.AreEqual(7, body.GetComponent<SortingGroup>().sortingOrder);
        }

        [Test]
        public void EditingPreviewClipsWithoutReplacingTheSavedContourSprite()
        {
            var clip = Clip(Color.red); var saved = clip.contour.sprite;
            clip.SetEditing(true); Assert.That(Pixel(.4f).r, Is.GreaterThan(.95f));
            var previewObject = GameObject("Temporary contour"); previewObject.transform.SetParent(clip.transform, false);
            var preview = previewObject.AddComponent<SpriteRenderer>(); preview.sprite = Sprite(.2f);
            clip.SetEditingPreview(preview);
            Assert.That(Pixel(0).r, Is.GreaterThan(.95f)); Assert.That(Pixel(.2f).a, Is.LessThan(.01f));
            clip.invert = true; clip.Refresh();
            Assert.That(Pixel(0).a, Is.LessThan(.01f)); Assert.That(Pixel(.2f).r, Is.GreaterThan(.95f));
            clip.invert = false; clip.Refresh();
            Assert.AreSame(saved, clip.contour.sprite); Assert.AreSame(saved, clip.contourRenderer.sprite);
            clip.SetEditing(true); Assert.IsFalse(preview.enabled); Assert.That(Pixel(.4f).r, Is.GreaterThan(.95f));
            clip.SetEditing(false); Assert.That(Pixel(.2f).r, Is.GreaterThan(.95f)); Assert.That(Pixel(.4f).a, Is.LessThan(.01f));
        }

        public static string RunChecks()
        {
            var results = new List<string>();
            foreach (var method in typeof(UniskinClippingMaskTests).GetMethods().Where(method => method.GetCustomAttribute<TestAttribute>() != null))
            {
                var fixture = new UniskinClippingMaskTests();
                try { fixture.Setup(); method.Invoke(fixture, null); results.Add("PASS " + method.Name); }
                catch (Exception ex) { results.Add("FAIL " + method.Name + ": " + (ex.InnerException ?? ex)); }
                finally { fixture.Cleanup(); }
            }
            if (results.Any(result => result.StartsWith("FAIL"))) throw new InvalidOperationException(string.Join("\n", results));
            return string.Join("\n", results);
        }
    }
}
