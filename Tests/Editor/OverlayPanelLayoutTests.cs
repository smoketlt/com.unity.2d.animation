using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor.U2D.Layout;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation.Tests
{
    internal class OverlayPanelLayoutTests
    {
        class PanelTestWindow : EditorWindow { }

        PanelTestWindow m_Window;
        VisualElement m_Host;
        VisualElement m_Panel;
        OverlayPanelLayout m_Layout;
        string m_Key;

        [SetUp]
        public void SetUp()
        {
            m_Key = "Test." + Guid.NewGuid().ToString("N");
            m_Window = ScriptableObject.CreateInstance<PanelTestWindow>();
            m_Window.position = new Rect(100, 100, 900, 700);
            m_Window.Show();
            m_Host = new VisualElement();
            m_Host.style.width = 800;
            m_Host.style.height = 600;
            m_Host.style.justifyContent = Justify.FlexEnd;
            m_Host.style.alignItems = Align.Center;
            m_Window.rootVisualElement.Add(m_Host);
            CreatePanel();
        }

        void CreatePanel()
        {
            m_Panel = new VisualElement();
            m_Panel.style.width = 300;
            m_Panel.style.height = 150;
            m_Layout = new OverlayPanelLayout(m_Panel, m_Key, new Vector2(300, 120));
            m_Host.Add(m_Panel);
        }

        [TearDown]
        public void TearDown()
        {
            m_Window.Close();
            EditorPrefs.DeleteKey(m_Layout.preferenceKey);
            EditorPrefs.DeleteKey("Unity.2D.Animation.SkinningPanel." + Application.dataPath + "." + m_Key + ".Other");
        }

        static IEnumerator WaitForLayout()
        {
            for (int i = 0; i < 5; ++i)
                yield return null;
        }

        void SetRect(Rect rect)
        {
            m_Layout.BeginInteraction();
            m_Layout.UpdateInteraction(rect);
            m_Layout.Save();
        }

        void AssertRect(Rect expected)
        {
            Assert.That(m_Panel.layout.x, Is.EqualTo(expected.x).Within(1));
            Assert.That(m_Panel.layout.y, Is.EqualTo(expected.y).Within(1));
            Assert.That(m_Panel.layout.width, Is.EqualTo(expected.width).Within(1));
            Assert.That(m_Panel.layout.height, Is.EqualTo(expected.height).Within(1));
        }

        [UnityTest]
        public IEnumerator UnmodifiedPanelKeepsDefaultBottomCenterPlacement()
        {
            yield return WaitForLayout();
            AssertRect(new Rect(250, 450, 300, 150));
            Assert.IsFalse(EditorPrefs.HasKey(m_Layout.preferenceKey));
        }

        [UnityTest]
        public IEnumerator LayoutSurvivesHidingAndRecreatingThePanel()
        {
            yield return WaitForLayout();
            var expected = new Rect(75, 60, 440, 260);
            SetRect(expected);
            yield return WaitForLayout();
            m_Panel.style.display = DisplayStyle.None;
            yield return WaitForLayout();
            m_Panel.style.display = DisplayStyle.Flex;
            yield return WaitForLayout();
            AssertRect(expected);
            m_Panel.RemoveFromHierarchy();
            CreatePanel();
            yield return WaitForLayout();
            AssertRect(expected);
        }

        [UnityTest]
        public IEnumerator SharedPanelKeepsSeparateModeLayouts()
        {
            yield return WaitForLayout();
            var first = new Rect(30, 40, 360, 200);
            var second = new Rect(400, 300, 350, 250);
            SetRect(first);
            m_Layout.SetLayoutKey(m_Key + ".Other");
            yield return WaitForLayout();
            AssertRect(new Rect(250, 450, 300, 150));
            SetRect(second);
            m_Layout.SetLayoutKey(m_Key);
            yield return WaitForLayout();
            AssertRect(first);
            m_Layout.SetLayoutKey(m_Key + ".Other");
            yield return WaitForLayout();
            AssertRect(second);
            m_Layout.SetLayoutKey(m_Key);
        }

        [UnityTest]
        public IEnumerator SmallerViewportDoesNotOverwritePreferredLayout()
        {
            yield return WaitForLayout();
            var expected = new Rect(300, 200, 440, 260);
            SetRect(expected);
            yield return WaitForLayout();
            string saved = EditorPrefs.GetString(m_Layout.preferenceKey);
            m_Host.style.width = 350;
            m_Host.style.height = 200;
            yield return WaitForLayout();
            AssertRect(new Rect(0, 0, 350, 200));
            m_Layout.Save();
            Assert.AreEqual(saved, EditorPrefs.GetString(m_Layout.preferenceKey));
            m_Host.style.width = 800;
            m_Host.style.height = 600;
            yield return WaitForLayout();
            AssertRect(expected);
        }

        [UnityTest]
        public IEnumerator ResizeRespectsMinimumSizeAndViewportBounds()
        {
            yield return WaitForLayout();
            SetRect(new Rect(-30, 800, 10, 20));
            yield return WaitForLayout();
            AssertRect(new Rect(0, 480, 300, 120));
            SetRect(new Rect(100, 100, 900, 700));
            yield return WaitForLayout();
            AssertRect(new Rect(0, 0, 800, 600));
        }

        [UnityTest]
        public IEnumerator CornerResizeKeepsPositionWhenItReachesViewportEdge()
        {
            yield return WaitForLayout();
            SetRect(new Rect(400, 300, 300, 150));
            yield return WaitForLayout();
            m_Layout.BeginInteraction();
            m_Layout.UpdateInteraction(new Rect(400, 300, 1000, 1000), true);
            yield return WaitForLayout();
            AssertRect(new Rect(400, 300, 400, 300));
        }

        [UnityTest]
        public IEnumerator ResizeHandleSavesOnPointerCaptureLoss()
        {
            yield return WaitForLayout();
            SetRect(new Rect(100, 100, 300, 150));
            yield return WaitForLayout();
            EditorPrefs.DeleteKey(m_Layout.preferenceKey);
            var handle = new VisualElement();
            handle.style.width = 16;
            handle.style.height = 16;
            handle.AddManipulator(new OverlayPanelDragger(m_Layout, true));
            m_Panel.Add(handle);
            yield return WaitForLayout();
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = new Vector2(550, 600) }))
            {
                down.target = handle;
                handle.SendEvent(down);
            }
            yield return null;
            using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, button = 0, mousePosition = new Vector2(650, 650) }))
            {
                move.target = handle;
                handle.SendEvent(move);
            }
            yield return null;
            handle.ReleasePointer(PointerId.mousePointerId);
            using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = new Vector2(650, 650) }))
            {
                move.target = handle;
                handle.SendEvent(move);
            }
            yield return WaitForLayout();
            AssertRect(new Rect(100, 100, 400, 200));
            Assert.IsTrue(EditorPrefs.HasKey(m_Layout.preferenceKey));
            m_Panel.RemoveFromHierarchy();
            CreatePanel();
            yield return WaitForLayout();
            AssertRect(new Rect(100, 100, 400, 200));
        }

        [UnityTest]
        public IEnumerator InvalidSavedLayoutFallsBackToDefaultPlacement()
        {
            yield return WaitForLayout();
            EditorPrefs.SetString(m_Layout.preferenceKey, "{broken json");
            m_Panel.RemoveFromHierarchy();
            CreatePanel();
            yield return WaitForLayout();
            AssertRect(new Rect(250, 450, 300, 150));
        }
    }
}
