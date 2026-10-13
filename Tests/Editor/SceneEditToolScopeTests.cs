using NUnit.Framework;

namespace UnityEditor.U2D.Animation.Tests
{
    internal class SceneEditToolScopeTests
    {
        Tool m_Tool;
        bool m_Hidden;
        SceneEditToolScope m_Scope;

        [SetUp]
        public void Setup()
        {
            m_Tool = UnityEditor.Tools.current;
            m_Hidden = UnityEditor.Tools.hidden;
            m_Scope = new SceneEditToolScope();
        }

        [TearDown]
        public void Cleanup()
        {
            m_Scope.Restore();
            UnityEditor.Tools.current = m_Tool;
            UnityEditor.Tools.hidden = m_Hidden;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RestoreRetainsOriginalVisibilityAndTool(bool hidden)
        {
            UnityEditor.Tools.current = Tool.Move;
            UnityEditor.Tools.hidden = hidden;
            m_Scope.Hide();
            m_Scope.Hide();
            Assert.IsTrue(UnityEditor.Tools.hidden);
            Assert.AreEqual(Tool.None, UnityEditor.Tools.current);
            Assert.IsTrue(m_Scope.Restore());
            Assert.AreEqual(hidden, UnityEditor.Tools.hidden);
            Assert.AreEqual(Tool.Move, UnityEditor.Tools.current);
            Assert.IsFalse(m_Scope.Restore());
        }

        [TestCase(Tool.Move)]
        [TestCase(Tool.Rotate)]
        [TestCase(Tool.Scale)]
        [TestCase(Tool.Rect)]
        [TestCase(Tool.Transform)]
        [TestCase(Tool.View)]
        public void ToolChangesStayHiddenAndSurviveCleanup(Tool tool)
        {
            UnityEditor.Tools.current = Tool.Move;
            UnityEditor.Tools.hidden = false;
            m_Scope.Hide();
            UnityEditor.Tools.current = tool;
            UnityEditor.Tools.hidden = false;
            m_Scope.Hide();
            Assert.IsTrue(UnityEditor.Tools.hidden);
            Assert.AreEqual(tool, UnityEditor.Tools.current);
            m_Scope.Restore();
            Assert.IsFalse(UnityEditor.Tools.hidden);
            Assert.AreEqual(tool, UnityEditor.Tools.current);
        }

        [Test]
        public void ReleasedScopeCapturesFreshStateWhenEditingResumes()
        {
            UnityEditor.Tools.current = Tool.Rotate;
            UnityEditor.Tools.hidden = false;
            m_Scope.Hide();
            m_Scope.Restore();
            UnityEditor.Tools.current = Tool.Scale;
            UnityEditor.Tools.hidden = true;
            m_Scope.Hide();
            m_Scope.Restore();
            Assert.AreEqual(Tool.Scale, UnityEditor.Tools.current);
            Assert.IsTrue(UnityEditor.Tools.hidden);
        }
    }
}
