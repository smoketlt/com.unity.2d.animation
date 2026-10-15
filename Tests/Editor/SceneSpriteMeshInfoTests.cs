using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Animation.Tests
{
    public sealed class SceneSpriteMeshInfoTests
    {
        Scene m_Scene;
        Sprite m_Sprite;
        Texture2D m_Texture;
        SpriteRenderer m_Renderer;

        [SetUp]
        public void Setup()
        {
            m_Scene = EditorSceneManager.NewPreviewScene();
            var go = new GameObject("Mesh information fixture"); SceneManager.MoveGameObjectToScene(go, m_Scene);
            m_Texture = new Texture2D(16, 16);
            m_Sprite = Sprite.Create(m_Texture, new Rect(0, 0, 16, 16), Vector2.one * .5f, 100, 0, SpriteMeshType.FullRect);
            m_Renderer = go.AddComponent<SpriteRenderer>(); m_Renderer.sprite = m_Sprite;
        }

        [TearDown]
        public void Cleanup()
        {
            if (m_Scene.IsValid()) EditorSceneManager.ClosePreviewScene(m_Scene);
            Object.DestroyImmediate(m_Sprite); Object.DestroyImmediate(m_Texture);
        }

        [Test]
        public void CountsRuntimeGeometryAndDoesNotCallUnskinnedSpritesCpu()
        {
            var values = SceneSpriteMeshInfo.Describe(m_Renderer);
            Assert.AreEqual(m_Sprite.GetVertexCount().ToString(), values[1]);
            Assert.AreEqual((m_Sprite.GetIndices().Length / 3).ToString(), values[2]);
            Assert.AreEqual("Static", values[3]); Assert.AreEqual("No SpriteSkin", values[4]);
            m_Renderer.sprite = null;
            values = SceneSpriteMeshInfo.Describe(m_Renderer);
            Assert.AreEqual("0", values[1]); Assert.AreEqual("0", values[2]);
        }

        [Test]
        public void DisabledAndInvalidSkinsDoNotAdvertiseActiveCpuOrGpu()
        {
            var skin = m_Renderer.gameObject.AddComponent<SpriteSkin>();
            Assert.AreEqual("Not ready", SceneSpriteMeshInfo.Describe(m_Renderer)[3]);
            skin.enabled = false;
            var values = SceneSpriteMeshInfo.Describe(m_Renderer);
            Assert.AreEqual("Not active", values[3]); Assert.AreEqual("Disabled", values[4]);
        }

        [Test]
        public void PanelFollowsSpriteSwapAndAggregatesSelectedRendererInstances()
        {
            Object[] targets = { m_Renderer };
            var panel = new SceneSpriteMeshInfo(() => targets);
            Assert.AreEqual("4", panel.Q<Label>("meshInfo1").text);
            m_Renderer.sprite = null; panel.Refresh();
            Assert.AreEqual("0", panel.Q<Label>("meshInfo1").text);
            m_Renderer.sprite = m_Sprite;
            var second = Object.Instantiate(m_Renderer.gameObject).GetComponent<SpriteRenderer>();
            second.sortingOrder = 10; targets = new Object[] { m_Renderer, second }; panel.Refresh();
            Assert.AreEqual("8 total (2 renderers)", panel.Q<Label>("meshInfo1").text);
            Assert.AreEqual("4 total (2 renderers)", panel.Q<Label>("meshInfo2").text);
            Assert.AreEqual("Mixed", panel.Q<Label>("meshInfo11").text);
        }
    }
}
