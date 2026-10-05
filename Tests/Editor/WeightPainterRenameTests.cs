using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation.Tests
{
    internal class WeightPainterRenameTests
    {
        class PopupTestWindow : EditorWindow { }
        PopupTestWindow m_Window;

        [SetUp]
        public void SetUp()
        {
            m_Window = ScriptableObject.CreateInstance<PopupTestWindow>();
            m_Window.Show();
        }

        [TearDown]
        public void TearDown()
        {
            m_Window.Close();
        }

        WeightPainterPanel CreatePanel()
        {
            var panel = WeightPainterPanel.GenerateFromUXML();
            m_Window.rootVisualElement.Add(panel);
            return panel;
        }

        [TestCase(WeightPainterMode.Brush)]
        [TestCase(WeightPainterMode.Slider)]
        public void RenameRefreshRetainsTargetWithoutIssuingSelectionCommands(WeightPainterMode paintMode)
        {
            var panel = CreatePanel();
            panel.paintMode = paintMode;
            panel.mode = WeightEditorMode.Smooth;
            panel.UpdatePanel(new[] { WeightPainterPanel.kNone, "bone_BLUE", "bone_RED" });
            int selectionCommands = 0;
            panel.bonePopupChanged += _ => ++selectionCommands;
            panel.SetBoneSelectionByName("bone_BLUE");

            // A selection refresh may arrive with the new name before choices are rebuilt.
            panel.SetBoneSelectionByName("bone_BLUE_BLUE");
            Assert.AreEqual(-1, panel.boneIndex);
            Assert.AreEqual(0, selectionCommands);

            panel.UpdatePanel(new[] { WeightPainterPanel.kNone, "bone_BLUE_BLUE", "bone_RED" });
            panel.SetBoneSelectionByName("bone_BLUE_BLUE");
            Assert.AreEqual(0, panel.boneIndex);
            Assert.AreEqual("bone_BLUE_BLUE", panel.Q<PopupField<string>>("BonePopupField").value);
            Assert.AreEqual(0, selectionCommands);
        }

        [Test]
        public void UserPopupSelectionStillIssuesSelectionCommand()
        {
            var panel = CreatePanel();
            panel.UpdatePanel(new[] { WeightPainterPanel.kNone, "bone_BLUE", "bone_RED" });
            int selectedIndex = -1;
            int selectionCommands = 0;
            panel.bonePopupChanged += index => { selectedIndex = index; ++selectionCommands; };

            panel.Q<PopupField<string>>("BonePopupField").value = "bone_RED";

            Assert.AreEqual(1, selectedIndex);
            Assert.AreEqual(1, selectionCommands);
        }
    }
}
