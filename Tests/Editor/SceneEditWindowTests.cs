using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UnityEditor.U2D.Animation.Tests
{
    internal class SceneEditWindowTests
    {
        const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase(typeof(SceneEditMeshWindow))]
        [TestCase(typeof(SceneEditBoneWindow))]
        [TestCase(typeof(SceneEditWeightsWindow))]
        public void ReloadClosesWindowAndSessionAndRestoresTools(Type type)
        {
            var previousTool = UnityEditor.Tools.current;
            var previousHidden = UnityEditor.Tools.hidden;
            var window = (SceneEditWindow)ScriptableObject.CreateInstance(type);
            var session = (ScriptableObject)type.GetField("m_Session", InstanceFields).GetValue(window);
            try
            {
                Assert.IsFalse(window.isRestoredWindow);
                Assert.IsNotNull(session);
                UnityEditor.Tools.current = Tool.Move;
                UnityEditor.Tools.hidden = false;
                var scope = (SceneEditToolScope)typeof(SceneEditWindow).GetProperty("toolScope", InstanceFields).GetValue(window);
                scope.Hide();
                window.ShowPopup();
                typeof(EditorWindow).GetProperty("hasUnsavedChanges").SetValue(window, true);
                window.CloseBeforeReload();
                Assert.IsTrue(window == null, "Reload must close even a window marked unsaved.");
                Assert.IsTrue(session == null, "The hidden editing session must be destroyed.");
                Assert.IsFalse(UnityEditor.Tools.hidden);
                Assert.AreEqual(Tool.Move, UnityEditor.Tools.current);
            }
            finally
            {
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                UnityEditor.Tools.current = previousTool;
                UnityEditor.Tools.hidden = previousHidden;
            }
        }

        [TestCase(typeof(SceneEditMeshWindow), true)]
        [TestCase(typeof(SceneEditBoneWindow), true)]
        [TestCase(typeof(SceneEditWeightsWindow), true)]
        [TestCase(typeof(SceneEditMeshWindow), false)]
        [TestCase(typeof(SceneEditBoneWindow), false)]
        [TestCase(typeof(SceneEditWeightsWindow), false)]
        public void RestoredWindowSkipsSessionAndUIInitialization(Type type, bool hasMarker)
        {
            var window = (SceneEditWindow)ScriptableObject.CreateInstance(type);
            var sessionField = type.GetField("m_Session", InstanceFields);
            var session = (ScriptableObject)sessionField.GetValue(window);
            try
            {
                type.GetMethod("OnDisable", InstanceFields).Invoke(window, null);
                if (hasMarker)
                {
                    // A startup layout can retain the window while losing its hidden session.
                    sessionField.SetValue(window, null);
                    UnityEngine.Object.DestroyImmediate(session);
                }
                else
                {
                    // Layouts written by the old implementation have no lifetime marker.
                    typeof(SceneEditWindow).GetField("m_WasEnabled", InstanceFields).SetValue(window, false);
                }
                type.GetMethod("OnEnable", InstanceFields).Invoke(window, null);
                Assert.IsTrue(window.isRestoredWindow);
                if (hasMarker) Assert.IsNull(sessionField.GetValue(window), "Do not create a new empty session.");
                type.GetMethod("CreateGUI").Invoke(window, null);
                Assert.AreEqual(0, window.rootVisualElement.childCount);
                window.CloseBeforeReload();
                Assert.IsTrue(window == null);
                Assert.IsTrue(session == null);
            }
            finally { if (window != null) UnityEngine.Object.DestroyImmediate(window); }
        }
    }
}
