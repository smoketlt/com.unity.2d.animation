using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Animation.Tests
{
    public sealed class UniskinMaskTests
    {
        readonly List<Object> m_Objects = new List<Object>();
        Scene m_Scene;
        SpriteRenderer m_Body, m_Source;
        UniskinSpriteMask m_Mask;
        Material m_Original;

        [SetUp]
        public void Setup()
        {
            m_Scene = EditorSceneManager.NewPreviewScene();
            m_Body = Renderer("Body", Sprite(64, 64, new Rect(8, 4, 32, 48), new Vector2(.3f, .7f), 64));
            m_Source = Renderer("Mask", Sprite(64, 64, new Rect(10, 8, 40, 32), new Vector2(.4f, .6f), 100));
            m_Original = Resources.Load<Material>("UniskinMaskUnlit");
            Assert.IsNotNull(m_Original);
            m_Body.sharedMaterial = Resources.Load<Material>("UniskinSpriteMask");
            m_Original = m_Body.sharedMaterial;
            m_Body.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            m_Mask = m_Body.gameObject.AddComponent<UniskinSpriteMask>();
        }

        [TearDown]
        public void Cleanup()
        {
            if (m_Scene.IsValid()) EditorSceneManager.ClosePreviewScene(m_Scene);
            foreach (var item in m_Objects) if (item != null) Object.DestroyImmediate(item);
            m_Objects.Clear();
        }

        Sprite Sprite(int width, int height, Rect rect, Vector2 pivot, float ppu, Func<int, int, Color> pixel = null)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var colors = new Color[width * height];
            for (var y = 0; y < height; ++y) for (var x = 0; x < width; ++x) colors[y * width + x] = pixel != null ? pixel(x, y) : Color.white;
            texture.SetPixels(colors); texture.Apply(); m_Objects.Add(texture);
            var sprite = UnityEngine.Sprite.Create(texture, rect, pivot, ppu, 0, SpriteMeshType.FullRect);
            m_Objects.Add(sprite); return sprite;
        }

        SpriteRenderer Renderer(string name, Sprite sprite)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, m_Scene);
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
            return renderer;
        }

        static Vector2 Map(MaterialPropertyBlock block, string prefix, Vector2 uv)
        {
            var x = block.GetVector(prefix + "0"); var y = block.GetVector(prefix + "1");
            return new Vector2(x.x * uv.x + x.y * uv.y + x.z, y.x * uv.x + y.y * uv.y + y.z);
        }

        static MaterialPropertyBlock Properties(SpriteRenderer renderer)
        {
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); return block;
        }

        [Test]
        public void FitMappingRespectsBothSpriteRectsAndPixelsPerUnit()
        {
            m_Mask.mask = m_Source.sprite; m_Mask.Refresh();
            var block = Properties(m_Body);
            foreach (var p in new[] { Vector2.zero, Vector2.one, new Vector2(.2f, .7f) })
            {
                var uv = (m_Body.sprite.rect.position + Vector2.Scale(p, m_Body.sprite.rect.size)) / new Vector2(64, 64);
                var expected = (m_Source.sprite.rect.position + Vector2.Scale(p, m_Source.sprite.rect.size)) / new Vector2(64, 64);
                Assert.That(Vector2.Distance(p, Map(block, "_UniskinMaskLocal", uv)), Is.LessThan(.00001f));
                Assert.That(Vector2.Distance(expected, Map(block, "_UniskinMaskUV", uv)), Is.LessThan(.00001f));
            }
            Assert.AreSame(m_Source.sprite.texture, block.GetTexture("_UniskinMaskTexture"));
        }

        [Test]
        public void CapturePlacementPreservesTransformsFlipsAndDisablesOnlyTheLegacyMask()
        {
            m_Body.transform.localPosition = new Vector3(2, 3, 0);
            m_Body.transform.localRotation = Quaternion.Euler(0, 0, 17);
            m_Body.transform.localScale = new Vector3(1.3f, .7f, 1);
            m_Body.flipX = true;
            m_Source.transform.localPosition = new Vector3(2.1f, 3.03f, 0);
            m_Source.transform.localRotation = Quaternion.Euler(0, 0, -12);
            m_Source.transform.localScale = new Vector3(.81f, 1.1f, 1);
            m_Source.flipY = true;
            var legacy = m_Source.gameObject.AddComponent<SpriteMask>(); legacy.alphaCutoff = .203f;
            m_Mask.placementSource = m_Source; m_Mask.CapturePlacement();
            Assert.IsFalse(legacy.enabled); Assert.That(m_Mask.alphaCutoff, Is.EqualTo(.203f));
            var block = Properties(m_Body);
            var localToUV = (Matrix4x4)typeof(UniskinSpriteMask).GetMethod("LocalToUV", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { m_Source.sprite });
            for (var i = 0; i < m_Body.sprite.vertices.Length; ++i)
            {
                var p = m_Body.sprite.vertices[i]; p.x = -p.x;
                var world = m_Body.transform.TransformPoint(p);
                var local = m_Source.transform.InverseTransformPoint(world); local.y = -local.y;
                var expected = (Vector2)localToUV.MultiplyPoint3x4(local);
                Assert.That(Vector2.Distance(expected, Map(block, "_UniskinMaskUV", m_Body.sprite.uv[i])), Is.LessThan(.00002f));
            }
            var before = block.GetVector("_UniskinMaskUV0");
            m_Source.transform.position += Vector3.right * 10;
            m_Mask.Refresh();
            Assert.AreEqual(before, Properties(m_Body).GetVector("_UniskinMaskUV0"), "Placement must be captured, not followed each frame.");
            m_Mask.enabled = false;
            Assert.IsTrue(legacy.enabled); Assert.AreEqual(SpriteMaskInteraction.VisibleInsideMask, m_Body.maskInteraction);
            Assert.AreSame(m_Original, m_Body.sharedMaterial);
        }

        [Test]
        public void ClearingMaskRestoresRendererAndPreservesOtherPropertyBlockValues()
        {
            var block = new MaterialPropertyBlock(); block.SetVector("_UnrelatedProperty", new Vector4(2, 3, 4, 5)); m_Body.SetPropertyBlock(block);
            m_Mask.mask = m_Source.sprite; m_Mask.Refresh();
            Assert.AreEqual(new Vector4(2, 3, 4, 5), Properties(m_Body).GetVector("_UnrelatedProperty"));
            m_Mask.mask = null; m_Mask.Refresh();
            Assert.AreSame(m_Original, m_Body.sharedMaterial);
            Assert.AreEqual(SpriteMaskInteraction.VisibleInsideMask, m_Body.maskInteraction);
            Assert.AreEqual(0, Properties(m_Body).GetFloat("_UniskinMaskEnabled"));
            Assert.AreEqual(new Vector4(2, 3, 4, 5), Properties(m_Body).GetVector("_UnrelatedProperty"));
        }

        [Test]
        public void SharedMaterialKeepsMasksIndependentAndIdleFramesAllocateNothing()
        {
            m_Mask.mask = m_Source.sprite; m_Mask.Refresh();
            var other = Renderer("Other", m_Body.sprite); other.sharedMaterial = m_Original;
            var otherMask = other.gameObject.AddComponent<UniskinSpriteMask>(); otherMask.mask = m_Body.sprite; otherMask.Refresh();
            Assert.AreSame(m_Body.sharedMaterial, other.sharedMaterial);
            Assert.AreNotSame(Properties(m_Body).GetTexture("_UniskinMaskTexture"), Properties(other).GetTexture("_UniskinMaskTexture"));
            var update = (Action)Delegate.CreateDelegate(typeof(Action), m_Mask, typeof(UniskinSpriteMask).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance));
            for (var i = 0; i < 50; ++i) update();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; ++i) update();
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Test]
        public void RotatedAndTrimmedUVMappingIsAffine()
        {
            var sprite = m_Source.sprite;
            var vertices = sprite.vertices;
            var uv = new Unity.Collections.NativeArray<Vector2>(vertices.Length, Unity.Collections.Allocator.Temp);
            try
            {
                for (var i = 0; i < vertices.Length; ++i) uv[i] = new Vector2(.4f + vertices[i].y * .2f, .6f - vertices[i].x * .3f);
                sprite.SetVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0, uv);
            }
            finally { uv.Dispose(); }
            m_Mask.mask = sprite; m_Mask.Refresh();
            var block = Properties(m_Body);
            var p = new Vector2(.2f, .7f);
            var bodyUV = (m_Body.sprite.rect.position + Vector2.Scale(p, m_Body.sprite.rect.size)) / new Vector2(64, 64);
            var local = Vector2.Scale(p, sprite.rect.size) / sprite.pixelsPerUnit - sprite.pivot / sprite.pixelsPerUnit;
            var expected = new Vector2(.4f + local.y * .2f, .6f - local.x * .3f);
            Assert.That(Vector2.Distance(expected, Map(block, "_UniskinMaskUV", bodyUV)), Is.LessThan(.00001f));
        }

        [Test]
        public void ShaderRendersMaskAlphaInversionCutoffAndTransparentExterior()
        {
            m_Body.sprite = Sprite(64, 64, new Rect(0, 0, 64, 64), Vector2.one * .5f, 64);
            m_Body.sharedMaterial = Resources.Load<Material>("UniskinMaskUnlit");
            m_Body.maskInteraction = SpriteMaskInteraction.None;
            m_Source.enabled = false;
            m_Mask.mask = Sprite(64, 64, new Rect(16, 16, 32, 32), Vector2.one * .5f, 64,
                (x, y) => new Color(1, 1, 1, x < 32 ? .2f : .8f));
            m_Mask.Refresh();
            var go = new GameObject("Mask Check Camera"); SceneManager.MoveGameObjectToScene(go, m_Scene);
            var camera = go.AddComponent<Camera>(); camera.scene = m_Scene; camera.orthographic = true; camera.orthographicSize = .5f;
            camera.transform.position = new Vector3(0, 0, -10); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32); target.Create(); m_Objects.Add(target); camera.targetTexture = target;
            var read = new Texture2D(64, 64, TextureFormat.RGBA32, false); m_Objects.Add(read);
            Color Pixel(int x, int y)
            {
                camera.Render();
                var previous = RenderTexture.active;
                try { RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); read.Apply(); return read.GetPixel(x, y); }
                finally { RenderTexture.active = previous; }
            }
            Assert.That(Pixel(12, 32).a, Is.EqualTo(.2f).Within(.03f));
            Assert.That(Pixel(52, 32).a, Is.EqualTo(.8f).Within(.03f));
            m_Mask.invert = true; m_Mask.Refresh();
            Assert.That(Pixel(12, 32).a, Is.EqualTo(.8f).Within(.03f));
            m_Mask.invert = false; m_Mask.alphaCutoff = .5f; m_Mask.Refresh();
            Assert.That(Pixel(12, 32).a, Is.LessThan(.01f));
            Assert.That(Pixel(52, 32).a, Is.EqualTo(.8f).Within(.03f));
            var serialized = new SerializedObject(m_Mask); serialized.FindProperty("m_Offset").vector2Value = Vector2.right * 2;
            serialized.ApplyModifiedPropertiesWithoutUndo(); m_Mask.Refresh();
            Assert.That(Pixel(32, 32).a, Is.LessThan(.01f), "Texture clamp must not fill the area outside the mask Sprite.");
            Assert.IsFalse(ShaderUtil.ShaderHasError(m_Body.sharedMaterial.shader));
        }

        // Also allows exact fixture checks in a connected Editor whose manifest does
        // not enable this package's Test Runner assembly.
        public static string RunChecks()
        {
            var results = new List<string>();
            foreach (var method in typeof(UniskinMaskTests).GetMethods().Where(method => method.GetCustomAttribute<TestAttribute>() != null))
            {
                var fixture = new UniskinMaskTests();
                try { fixture.Setup(); method.Invoke(fixture, null); results.Add("PASS " + method.Name); }
                catch (Exception ex) { results.Add("FAIL " + method.Name + ": " + (ex.InnerException ?? ex)); }
                finally { fixture.Cleanup(); }
            }
            if (results.Any(result => result.StartsWith("FAIL"))) throw new InvalidOperationException(string.Join("\n", results));
            return string.Join("\n", results);
        }
    }
}
