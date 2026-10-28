using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor.U2D.Animation.ClipperLib;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Animation.Tests
{
    internal class SceneMeshEditingTests
    {
        [Test]
        public void RecoveryBackupPreservesStagedMeshWhenSourceIsUnavailable()
        {
            var window = ScriptableObject.CreateInstance<SceneEditMeshWindow>();
            string path = null;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var session = (SceneMeshSession)typeof(SceneEditMeshWindow).GetField("m_Session", flags).GetValue(window);
            try
            {
                session.mesh.SetFrame(new Rect(0, 0, 32, 32));
                session.CreateMesh();
                session.mesh.vertices[0] = new Vector2(7, 9);
                session.sourceMesh = "entry fingerprint";
                path = window.BackUpSession();
                var record = JsonUtility.FromJson<SceneEditMeshWindow.RecoveryRecord>(File.ReadAllText(path));
                var restored = ScriptableObject.CreateInstance<SceneMeshSession>();
                try
                {
                    EditorJsonUtility.FromJsonOverwrite(record.session, restored);
                    Assert.AreEqual(JsonUtility.ToJson(session.mesh), JsonUtility.ToJson(restored.mesh));
                    Assert.AreEqual(session.sourceMesh, restored.sourceMesh);
                    Assert.IsTrue(restored.pendingCreation);
                }
                finally { Object.DestroyImmediate(restored); }
            }
            finally
            {
                window.DiscardChanges();
                Object.DestroyImmediate(window);
                if (path != null) File.Delete(path);
            }
        }

        [Test]
        public void ImageBoundsFollowDeformedVerticesAndReturnToSourceFrame()
        {
            var source = new SpriteMeshData();
            source.SetFrame(new Rect(100, 200, 64, 32));
            var posed = new SpriteMeshData();
            posed.AddVertex(new Vector2(-20, 40), default);
            posed.AddVertex(new Vector2(10, 70), default);
            posed.AddVertex(new Vector2(5, 25), default);
            Assert.AreEqual(new Rect(-20, 25, 30, 45), SceneEditMeshWindow.ImageBounds(source, posed));
            posed.vertices[1] = new Vector2(80, 100);
            Assert.AreEqual(new Rect(-20, 25, 100, 75), SceneEditMeshWindow.ImageBounds(source, posed));
            Assert.AreEqual(new Rect(0, 0, 64, 32), SceneEditMeshWindow.ImageBounds(source, null));
            Assert.AreEqual(new Rect(0, 0, 64, 32), SceneEditMeshWindow.ImageBounds(source, new SpriteMeshData()));
            Assert.AreEqual(new Rect(100, 200, 64, 32), source.frame);
        }

        [Test]
        public void RemappedImageBoundsFollowSavedGeometryWithoutDeformedDisplay()
        {
            var mesh = new SpriteMeshData();
            mesh.SetFrame(new Rect(100, 200, 64, 32));
            mesh.AddVertex(new Vector2(-30, -70), default);
            mesh.AddVertex(new Vector2(90, -20), default);
            mesh.AddVertex(new Vector2(10, -50), default);
            Assert.AreEqual(new Rect(-30, -70, 120, 50), SceneEditMeshWindow.ImageBounds(mesh, null, true));
            Assert.AreEqual(new Rect(0, 0, 64, 32), SceneEditMeshWindow.ImageBounds(mesh, null));
            Assert.AreEqual(new Rect(0, 0, 64, 32), SceneEditMeshWindow.ImageBounds(mesh, new SpriteMeshData(), true));
        }

        sealed class MeshPreferencesScope : IDisposable
        {
            readonly string[] m_Keys = { MeshEditorSettings.kSelectedVertexColorKey, MeshEditorSettings.kHoveredVertexColorKey, MeshEditorSettings.kMeshLineColorKey };
            readonly string[] m_Colors;
            readonly bool m_HadThreshold = EditorPrefs.HasKey(MeshEditorSettings.kSymmetryThresholdKey);
            readonly float m_Threshold = EditorPrefs.GetFloat(MeshEditorSettings.kSymmetryThresholdKey);
            public MeshPreferencesScope()
            {
                m_Colors = m_Keys.Select(key => EditorPrefs.HasKey(key) ? EditorPrefs.GetString(key) : null).ToArray();
                MeshEditorSettings.ResetDefaults();
            }
            public void Dispose()
            {
                for (int i = 0; i < m_Keys.Length; ++i)
                    if (m_Colors[i] == null) EditorPrefs.DeleteKey(m_Keys[i]);
                    else EditorPrefs.SetString(m_Keys[i], m_Colors[i]);
                if (m_HadThreshold) EditorPrefs.SetFloat(MeshEditorSettings.kSymmetryThresholdKey, m_Threshold);
                else EditorPrefs.DeleteKey(MeshEditorSettings.kSymmetryThresholdKey);
            }
        }

        sealed class DragGUI : IGUIWrapper
        {
            readonly Dictionary<int, int> m_Ids = new Dictionary<int, int>();
            float m_Distance;
            public int hot;
            public int nearest;
            public Vector2 mousePosition { get; private set; }
            public EventType eventType { get; private set; }
            public int button;
            public int mouseButton => button;
            public int clickCount => 1;
            public bool isShiftDown => false;
            public bool isAltDown => false;
            public bool isActionKeyDown => false;
            public string commandName => string.Empty;
            public void Event(EventType type, Vector2 position)
            {
                eventType = type; mousePosition = position;
                if (type == EventType.Layout) { nearest = 99; m_Distance = 5f; }
            }
            public int GetControlID(int hint, FocusType focusType)
            {
                if (!m_Ids.ContainsKey(hint)) m_Ids.Add(hint, 100 + m_Ids.Count);
                return m_Ids[hint];
            }
            public void LayoutControl(int id, float distance)
            {
                if (eventType == EventType.Layout && distance <= m_Distance) { nearest = id; m_Distance = distance; }
            }
            public bool IsControlNearest(int id) => nearest == id;
            public bool IsControlHot(int id) => hot == id;
            public bool IsMultiStepControlHot(int id) => false;
            public void SetControlHot(int id) => hot = id;
            public void SetMultiStepControlHot(int id) { }
            public bool IsMouseDown(int button) => eventType == EventType.MouseDown && button == mouseButton;
            public bool IsMouseUp(int button) => eventType == EventType.MouseUp && button == mouseButton;
            public bool IsKeyDown(KeyCode key) => false;
            public bool DoSlider(int id, SliderData data, out Vector3 position) { position = mousePosition; return hot == id; }
            public void UseCurrentEvent() => eventType = EventType.Used;
            public float DistanceToCircle(Vector3 center, float radius) => Mathf.Max(0f, Vector2.Distance(center, mousePosition) - radius);
            public float DistanceToSegment(Vector3 a, Vector3 b) => float.MaxValue;
            public float DistanceToSegmentClamp(Vector3 a, Vector3 b) => float.MaxValue;
            public Vector3 GUIToWorld(Vector2 position) => position;
            public Vector3 GUIToWorld(Vector2 position, Vector3 normal, Vector3 origin) => position;
            public void Repaint() { }
            public bool IsRepainting() => eventType == EventType.Repaint;
            public bool IsEventOutsideWindow() => eventType == EventType.Ignore;
            public void SetGuiChanged(bool changed) { }
            public float GetHandleSize(Vector3 position) => 1f;
            public bool IsViewToolActive() => false;
            public bool HasCurrentCamera() => false;
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void OutsideBoundsOptionControlsVertexDragWithoutChangingFrame(bool allowOutside, bool newHull)
        {
            var session = Quad();
            try
            {
                session.selection.elements = new[] { 0 };
                var gui = new DragGUI { hot = 123 };
                gui.Event(EventType.MouseDrag, new Vector2(-20, 40));
                var view = new SpriteMeshView(gui) { selection = session.selection,
                    mode = newHull ? SpriteMeshViewMode.NewGeometry : SpriteMeshViewMode.EditGeometry };
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(SpriteMeshView).GetField("m_MoveVertexControlID", flags).SetValue(view, 123);
                Vector2[] moved = null;
                var frame = new Rect(0, 0, 32, 32);
                var controller = new SpriteMeshController { spriteMeshView = view, spriteMeshData = session.mesh,
                    selection = session.selection, cacheUndo = session, frame = frame, restrictMovementToFrame = !allowOutside,
                    geometryVerticesMoved = (indices, positions) => moved = positions,
                    newGeometryVerticesMoved = (indices, positions) => moved = positions };
                typeof(SpriteMeshController).GetMethod("HandleMoveVertexAndEdge", flags).Invoke(controller, null);
                Assert.IsNotNull(moved);
                Assert.AreEqual(allowOutside ? new Vector2(-20, 40) : new Vector2(0, 32), moved[0]);
                Assert.AreEqual(frame, controller.frame);
                Assert.AreEqual(frame, session.mesh.frame);
            }
            finally { Object.DestroyImmediate(session); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OutsideBoundsOptionControlsSymmetricMovement(bool allowOutside)
        {
            var session = Quad();
            try
            {
                session.SetSymmetry(true);
                session.EndUndoOperation();
                var indices = new[] { session.FindSymmetryVertex(new Vector2(0, 0)), session.FindSymmetryVertex(new Vector2(0, 32)) };
                Assert.IsTrue(session.MoveSymmetricGeometry(indices, new[] { new Vector2(-8, -8), new Vector2(-8, 40) }, allowOutside));
                var x = allowOutside ? -8 : 0;
                CollectionAssert.AreEquivalent(new[] { new Vector2(x, x), new Vector2(x, 32 - x),
                    new Vector2(32 - x, x), new Vector2(32 - x, 32 - x) }, session.mesh.vertices);
                Assert.AreEqual(new Rect(0, 0, 32, 32), session.mesh.frame);
                SceneMeshSource.Validate(session.mesh);
            }
            finally { Object.DestroyImmediate(session); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OutsideBoundsOptionControlsSymmetricNewMovement(bool allowOutside)
        {
            var session = Quad();
            try
            {
                session.BeginNew(); session.SetSymmetry(true);
                session.AddSymmetryVertex(new Vector2(4, 28));
                session.AddSymmetryVertex(new Vector2(4, 4));
                session.MoveSymmetryVertices(new[] { 0 }, new[] { new Vector2(-8, 40) }, allowOutside);
                CollectionAssert.Contains(session.mesh.vertices, allowOutside ? new Vector2(-8, 40) : new Vector2(0, 32));
                CollectionAssert.Contains(session.mesh.vertices, allowOutside ? new Vector2(40, 40) : new Vector2(32, 32));
                Assert.IsEmpty(session.mesh.indices);
                Assert.AreEqual(new Rect(0, 0, 32, 32), session.mesh.frame);
            }
            finally { Object.DestroyImmediate(session); }
        }

        [Test]
        public void OutsideBoundsToggleStartsUncheckedAndUpdatesSessionOption()
        {
            var window = ScriptableObject.CreateInstance<SceneEditMeshWindow>();
            try
            {
                window.ShowPopup();
                window.CreateGUI();
                window.rootVisualElement.Q("mainControls").SetEnabled(true);
                var toggle = window.rootVisualElement.Q<Toggle>("allowOutsideBounds");
                Assert.IsNotNull(toggle);
                Assert.IsFalse(toggle.value);
                var field = typeof(SceneEditMeshWindow).GetField("m_AllowOutsideBounds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                toggle.value = true;
                Assert.IsTrue((bool)field.GetValue(window));
                toggle.value = false;
                Assert.IsFalse((bool)field.GetValue(window));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void SceneMeshPrimaryPressIsReservedBeforePickingAndReleasedForMeshTools()
        {
            var gui = new DragGUI();
            gui.Event(EventType.MouseDown, new Vector2(32, 32));
            Assert.IsTrue(SceneEditMeshWindow.CapturePrimaryScenePress(gui, 321));
            Assert.AreEqual(321, gui.hot, "Native handles must see an owned press before mesh input runs.");
            Assert.AreEqual(EventType.MouseDown, gui.eventType, "The mesh controller must still receive the original press.");
            Assert.IsFalse(SceneEditMeshWindow.CapturePrimaryScenePress(gui, 654), "Do not steal another control's press.");
            SceneEditMeshWindow.ReleasePrimaryScenePress(gui, 321);
            var view = new SpriteMeshView(gui) { useSceneHandles = true, mode = SpriteMeshViewMode.CreateVertex,
                selection = new IndexedSelection(), defaultControlID = 99, frame = new Rect(0, 0, 64, 64) };
            view.BeginLayout(); view.EndLayout();
            Assert.IsTrue(view.DoCreateVertex(), "After handoff, the original interior click must reach Create.");
            gui.Event(EventType.MouseDown, new Vector2(32, 32));
            gui.hot = 654;
            SceneEditMeshWindow.ReleasePrimaryScenePress(gui, 321);
            Assert.AreEqual(654, gui.hot, "Cleanup must preserve another tool's capture.");
            gui.hot = 0;
            gui.button = 1;
            Assert.IsFalse(SceneEditMeshWindow.CapturePrimaryScenePress(gui, 321));
            gui.button = 2;
            Assert.IsFalse(SceneEditMeshWindow.CapturePrimaryScenePress(gui, 321));
            gui.button = 0;
            gui.Event(EventType.MouseUp, new Vector2(32, 32));
            Assert.IsFalse(SceneEditMeshWindow.CapturePrimaryScenePress(gui, 321));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CreateEdgeDragKeepsPreviewActiveAndReleasesMouseCapture(bool scene)
        {
            CheckCreateEdgeDrag(scene, new Vector2(48, 48));
        }

        [Test]
        public void SceneCreateEdgeDragCanReleaseOutsideFrame()
        {
            CheckCreateEdgeDrag(true, new Vector2(-80, 100));
        }

        [TestCase(true, -80f, 32f)]
        [TestCase(true, 100f, 100f)]
        [TestCase(true, 64f, 32f)]
        [TestCase(false, -80f, 32f)]
        public void SceneCreateVertexAcceptsOutsideFrameClicks(bool scene, float x, float y)
        {
            var gui = new DragGUI();
            var view = new SpriteMeshView(gui) { useSceneHandles = scene, mode = SpriteMeshViewMode.CreateVertex,
                selection = new IndexedSelection(), defaultControlID = 99, frame = new Rect(0, 0, 64, 64) };
            var position = new Vector2(x, y);
            gui.Event(EventType.Layout, position);
            view.BeginLayout(); view.EndLayout();
            gui.Event(EventType.MouseDown, position);
            view.BeginLayout(); view.EndLayout();
            Assert.AreEqual(scene, view.DoCreateVertex());
            Assert.AreEqual(scene ? EventType.Used : EventType.MouseDown, gui.eventType);
        }

        void CheckCreateEdgeDrag(bool scene, Vector2 target)
        {
            var gui = new DragGUI();
            var selection = new IndexedSelection(); selection.Select(0, true);
            var view = new SpriteMeshView(gui) { useSceneHandles = scene, mode = SpriteMeshViewMode.CreateVertex,
                selection = selection, defaultControlID = 99, frame = new Rect(0, 0, 64, 64) };
            var source = new Vector2(8, 8);
            Layout(source);
            gui.Event(EventType.MouseDown, source);
            view.BeginLayout(); view.EndLayout();
            Assert.IsFalse(view.IsActionActive(MeshEditorAction.CreateEdge), "Do not create an edge back to its source.");
            Assert.IsTrue(view.isCreateEdgeDragActive);
            Assert.AreEqual(scene, gui.hot != 0, "Scene must capture the gesture before native picking runs.");
            Assert.IsFalse(view.DoMoveVertex(out _));
            Assert.IsFalse(view.DoMoveEdge(out _));
            if (!scene) Layout(target);
            gui.Event(EventType.MouseDrag, target);
            view.BeginLayout();
            if (view.CanLayout()) view.LayoutVertex(source, 0);
            view.EndLayout();
            Assert.IsFalse(view.DoCreateEdge(), "Dragging previews; releasing creates.");
            gui.Event(EventType.Repaint, target);
            view.BeginLayout();
            if (view.CanLayout()) view.LayoutVertex(source, 0);
            view.EndLayout();
            Assert.IsTrue(view.IsActionActive(MeshEditorAction.CreateEdge), "The controller uses this gate to draw its edge preview.");
            Assert.AreEqual(target, view.mouseWorldPosition);
            gui.Event(EventType.MouseUp, target);
            view.BeginLayout();
            if (view.CanLayout()) view.LayoutVertex(source, 0);
            view.EndLayout();
            Assert.IsTrue(view.DoCreateEdge());
            Assert.AreEqual(0, gui.hot);
            Assert.IsFalse(view.isCreateEdgeDragActive);
            Assert.AreEqual(SpriteMeshViewMode.CreateVertex, view.mode);

            void Layout(Vector2 position)
            {
                gui.Event(EventType.Layout, position);
                view.BeginLayout(); view.LayoutVertex(source, 0); view.EndLayout();
            }
        }

        [Test]
        public void CancelingSceneEdgeDragReleasesOnlyItsOwnCapture()
        {
            var gui = new DragGUI();
            var selection = new IndexedSelection(); selection.Select(0, true);
            var view = new SpriteMeshView(gui) { useSceneHandles = true, mode = SpriteMeshViewMode.CreateVertex,
                selection = selection, defaultControlID = 99, frame = new Rect(0, 0, 64, 64) };
            gui.Event(EventType.Layout, new Vector2(8, 8));
            view.BeginLayout(); view.LayoutVertex(new Vector2(8, 8), 0); view.EndLayout();
            gui.Event(EventType.MouseDown, new Vector2(8, 8));
            view.IsActionActive(MeshEditorAction.CreateEdge);
            Assert.AreNotEqual(0, gui.hot);
            view.CancelCreateEdgeDrag();
            Assert.AreEqual(0, gui.hot);
            Assert.IsFalse(view.isCreateEdgeDragActive);
            gui.hot = 777;
            view.CancelCreateEdgeDrag();
            Assert.AreEqual(777, gui.hot, "Cleanup must not release another editor's control.");
        }

        static SceneMeshSession Quad()
        {
            var session = ScriptableObject.CreateInstance<SceneMeshSession>();
            session.mesh.SetFrame(new Rect(0, 0, 32, 32));
            var controller = new SpriteMeshDataController { spriteMeshData = session.mesh };
            controller.CreateQuad();
            controller.Triangulate(new Triangulator());
            session.savedMesh = JsonUtility.ToJson(session.mesh);
            return session;
        }

        [Test]
        public void WholeMeshClipboardPreviewsMirroredTopologyAndAppliesWithUndo()
        {
            var source = Quad();
            var target = Quad();
            try
            {
                source.mesh.vertices[0] = new Vector2(2, 3);
                target.mesh.SetFrame(new Rect(100, 200, 64, 64));
                target.mesh.bones.Add(new SpriteBoneData());
                target.mesh.vertexWeights[0].SetFromBoneWeight(new BoneWeight { boneIndex0 = 0, weight0 = 1 });
                var before = JsonUtility.ToJson(target.mesh);
                var clipboard = source.CopyMeshGeometry();
                var preview = target.PreviewMeshGeometry(clipboard, true, Vector2.zero, Vector2.one, 0);
                Assert.AreEqual(new Vector2(60, 6), preview.vertices[0]);
                Assert.AreEqual(before, JsonUtility.ToJson(target.mesh), "Preview must not stage changes.");
                Assert.AreEqual(source.mesh.indices[1], preview.indices[2]);
                Assert.AreEqual(source.mesh.indices[2], preview.indices[1]);
                Assert.AreEqual(source.mesh.edges, preview.edges);
                Assert.AreEqual(1, preview.boneCount);
                Undo.IncrementCurrentGroup();
                target.ApplyMeshGeometry(preview);
                Assert.AreEqual(preview.vertices, target.mesh.vertices);
                Assert.IsFalse(target.hasBoneWeights);
                Assert.AreEqual(1, target.mesh.boneCount);
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(before, JsonUtility.ToJson(target.mesh));
                Undo.PerformRedo();
                Assert.AreEqual(preview.vertices, target.mesh.vertices);
                source.mesh.vertices[0] = Vector2.zero;
                Assert.AreEqual(new Vector2(60, 6), target.PreviewMeshGeometry(clipboard, true, Vector2.zero, Vector2.one, 0).vertices[0]);
            }
            finally
            {
                Undo.ClearUndo(source); Undo.ClearUndo(target);
                Object.DestroyImmediate(source); Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void WholeMeshClipboardTransformsAndRejectsInvalidInputWithoutMutation()
        {
            var session = Quad();
            try
            {
                var before = JsonUtility.ToJson(session.mesh);
                var clipboard = session.CopyMeshGeometry();
                var preview = session.PreviewMeshGeometry(clipboard, false, new Vector2(2, 0), new Vector2(0.5f, 0.5f), 90);
                var local = session.mesh.vertices[0] - new Vector2(16, 16);
                Assert.That(Vector2.Distance(preview.vertices[0], new Vector2(18 - local.y * 0.5f, 16 + local.x * 0.5f)), Is.LessThan(0.0001f));
                Assert.Throws<InvalidOperationException>(() => session.PreviewMeshGeometry("unrelated", true, Vector2.zero, Vector2.one, 0));
                Assert.Throws<InvalidOperationException>(() => session.PreviewMeshGeometry(clipboard, true, Vector2.zero, Vector2.zero, 0));
                var outside = session.PreviewMeshGeometry(clipboard, true, new Vector2(100, 0), Vector2.one, 45);
                Assert.That(outside.vertices.All(p => p.x > session.mesh.frame.width), Is.True);
                Assert.Throws<InvalidOperationException>(() => session.PreviewMeshGeometry(clipboard, true, Vector2.zero, Vector2.one, float.NaN));
                Assert.AreEqual(before, JsonUtility.ToJson(session.mesh));
            }
            finally { Object.DestroyImmediate(session); }
        }

        [Test]
        public void EscapeInMeshPastePreviewPreservesMeshAndSelection()
        {
            var original = Quad();
            var window = ScriptableObject.CreateInstance<SceneEditMeshWindow>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            try
            {
                var session = (SceneMeshSession)typeof(SceneEditMeshWindow).GetField("m_Session", flags).GetValue(window);
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(original), session);
                session.selection.elements = new[] { 0 };
                var before = EditorJsonUtility.ToJson(session);
                window.CreateGUI();
                window.ShowPopup();
                var clipboard = session.CopyMeshGeometry();
                typeof(SceneEditMeshWindow).GetField("m_MeshClipboard", flags).SetValue(window, clipboard);
                typeof(SceneEditMeshWindow).GetField("m_PastePreview", flags).SetValue(window, session.PreviewMeshGeometry(clipboard, true, Vector2.zero, Vector2.one, 0));
                using (var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
                {
                    evt.target = window.rootVisualElement;
                    window.rootVisualElement.SendEvent(evt);
                }
                Assert.IsNull(typeof(SceneEditMeshWindow).GetField("m_MeshClipboard", flags).GetValue(window));
                Assert.IsNull(typeof(SceneEditMeshWindow).GetField("m_PastePreview", flags).GetValue(window));
                Assert.AreEqual(before, EditorJsonUtility.ToJson(session));
                Assert.IsTrue(window.rootVisualElement.Q("pasteSettings").ClassListContains("mesh-hidden"));
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(original); }
        }

        [Test]
        public void MeshPastePlacementKeepsPreviewAcrossMoveRotationAndMirrorChanges()
        {
            var original = Quad();
            var window = ScriptableObject.CreateInstance<SceneEditMeshWindow>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            try
            {
                var type = typeof(SceneEditMeshWindow);
                var session = (SceneMeshSession)type.GetField("m_Session", flags).GetValue(window);
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(original), session);
                var before = EditorJsonUtility.ToJson(session);
                window.CreateGUI();
                type.GetField("m_MeshClipboard", flags).SetValue(window, session.CopyMeshGeometry());
                foreach (float angle in new[] { 5f, 45f, 90f, 180f, 270f })
                {
                    type.GetField("m_PasteOffset", flags).SetValue(window, new Vector2(50, -10));
                    type.GetField("m_PasteAngle", flags).SetValue(window, angle);
                    type.GetMethod("RefreshMeshPaste", flags).Invoke(window, null);
                    var preview = (SpriteMeshData)type.GetField("m_PastePreview", flags).GetValue(window);
                    Assert.IsNotNull(preview);
                    Assert.IsNull(type.GetField("m_Error", flags).GetValue(window));
                    var corners = (Vector2[])type.GetMethod("PasteCorners", flags).Invoke(window, null);
                    Assert.That(Vector2.Distance((corners[0] + corners[2]) * 0.5f, new Vector2(66, 6)), Is.LessThan(0.0001f));
                    Assert.AreEqual(before, EditorJsonUtility.ToJson(session));
                }
                window.rootVisualElement.Q<Toggle>("pasteMirror").value = false;
                Assert.IsNotNull(type.GetField("m_PastePreview", flags).GetValue(window));
                Assert.AreEqual(before, EditorJsonUtility.ToJson(session));
                Assert.That(SceneEditMeshWindow.PasteRotation(new Vector2(12, 10), new Vector2(10, 12), new Vector2(10, 10)), Is.EqualTo(90).Within(0.0001f));
                Assert.That(Vector2.Distance(SceneEditMeshWindow.RotatePastePoint(new Vector2(2, 0), 90), new Vector2(0, 2)), Is.LessThan(0.0001f));
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(original); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ScenePickingRestoresEntrySelectionWithoutExitingMeshEditing(bool pickAnotherObject)
        {
            var previousSelection = Selection.objects;
            var entry = new GameObject("Mesh entry selection");
            var other = new GameObject("Scene pick target");
            var window = ScriptableObject.CreateInstance<SceneEditMeshWindow>();
            EditorApplication.CallbackFunction restore = null;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            try
            {
                // Exercise only this host's callback, without live Scene GUI/selection subscriptions.
                typeof(SceneEditMeshWindow).GetMethod("OnDisable", flags).Invoke(window, null);
                var renderer = entry.AddComponent<SpriteRenderer>();
                typeof(SceneEditMeshWindow).GetField("m_Renderer", flags).SetValue(window, renderer);
                typeof(SceneEditMeshWindow).GetField("m_SelectionObjects", flags).SetValue(window, new Object[] { entry });
                var session = (SceneMeshSession)typeof(SceneEditMeshWindow).GetField("m_Session", flags).GetValue(window);
                session.pendingCreation = true;
                var before = EditorJsonUtility.ToJson(session);
                Selection.objects = pickAnotherObject ? new Object[] { other } : Array.Empty<Object>();
                var queuedBefore = EditorApplication.delayCall;
                typeof(SceneEditMeshWindow).GetMethod("OnSelectionChanged", flags).Invoke(window, null);
                var added = EditorApplication.delayCall.GetInvocationList().Except(queuedBefore?.GetInvocationList() ?? Array.Empty<Delegate>()).ToArray();
                Assert.AreEqual(1, added.Length);
                restore = (EditorApplication.CallbackFunction)added[0];
                EditorApplication.delayCall -= restore;
                restore();
                CollectionAssert.AreEqual(new Object[] { entry }, Selection.objects);
                Assert.IsTrue(window != null, "Scene picking must keep the mesh editor alive.");
                Assert.AreEqual(before, EditorJsonUtility.ToJson(session), "Picking must preserve staged edits.");
                Assert.IsFalse((bool)typeof(SceneEditMeshWindow).GetField("m_SelectionRestoreQueued", flags).GetValue(window));
            }
            finally
            {
                if (restore != null) EditorApplication.delayCall -= restore;
                if (window != null) Object.DestroyImmediate(window);
                Selection.objects = previousSelection;
                Object.DestroyImmediate(other);
                Object.DestroyImmediate(entry);
            }
        }

        [Test]
        public void DefaultSpriteRendererInspectorIncludesMeshEntry()
        {
            var go = new GameObject("Uniskin Inspector registration test");
            Editor editor = null;
            try
            {
                var renderer = go.AddComponent<SpriteRenderer>();
                // Exercise Unity's actual custom-editor lookup, not an explicit type.
                editor = Editor.CreateEditor(renderer);
                Assert.IsInstanceOf<Fedorov.Uniskin.UniskinSpriteRendererEditor>(editor);
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root.Q<Button>("uniskinEditMesh"));
                Assert.IsFalse(SceneEditMeshWindow.IsEditingRenderer(renderer));
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(go);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SelectedVertexClipboardPastesSourceCoordinatesAndSupportsUndo(bool mirror)
        {
            var source = Quad();
            var target = Quad();
            try
            {
                source.assetPath = target.assetPath = "source";
                source.spriteId = target.spriteId = "sprite";
                source.sourceMesh = target.sourceMesh = "matching source fingerprint";
                source.mesh.bones.Add(new SpriteBoneData());
                target.mesh.bones.Add(new SpriteBoneData());
                source.mesh.AddVertex(new Vector2(6, 8), new BoneWeight { boneIndex0 = 0, weight0 = 1f });
                source.mesh.AddVertex(new Vector2(9, 12), default);
                new SpriteMeshDataController { spriteMeshData = source.mesh }.CreateEdge(4, 5);
                source.selection.elements = new[] { 4, 5 };
                var clipboard = source.CopySelectedVertices();
                source.mesh.vertices[4] = new Vector2(1, 1); // Clipboard must be a snapshot.
                var before = JsonUtility.ToJson(target.mesh);
                Assert.IsTrue(target.PasteVertices(clipboard, mirror));
                var expected = new[] { new Vector2(mirror ? 26 : 6, 8), new Vector2(mirror ? 23 : 9, 12) };
                CollectionAssert.AreEquivalent(expected, target.selection.elements.Select(i => target.mesh.vertices[i]));
                Assert.AreEqual(6, target.mesh.vertexCount);
                var first = Array.FindIndex(target.mesh.vertices, p => p.Equals(expected[0]));
                Assert.AreEqual(1f, target.mesh.vertexWeights[first].Sum());
                Assert.IsTrue(target.mesh.edges.Any(e => target.selection.Contains(e.x) && target.selection.Contains(e.y)));
                SceneMeshSource.Validate(target.mesh);
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(before, JsonUtility.ToJson(target.mesh));
                Assert.AreEqual(0, target.selection.Count);
            }
            finally
            {
                Undo.ClearUndo(target);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void FractionalPixelMirrorPastePreservesAuthoredVerticesAcrossRepeatedPaste()
        {
            var session = ScriptableObject.CreateInstance<SceneMeshSession>();
            try
            {
                session.mesh.SetFrame(new Rect(3570, 1633, 454, 429));
                var controller = new SpriteMeshDataController { spriteMeshData = session.mesh };
                controller.CreateQuad();
                var positions = new[]
                {
                    new Vector2(140.108139f, 398.430206f), new Vector2(73.9220123f, 370.463898f),
                    new Vector2(20.7867088f, 294.023468f), new Vector2(17.0579376f, 198.006775f),
                    new Vector2(31.9731159f, 101.990845f), new Vector2(103.752373f, 39.5335693f),
                    new Vector2(224.006042f, 14.3641968f), new Vector2(219.345032f, 412.412598f)
                };
                foreach (var p in positions) session.mesh.AddVertex(p, default);
                foreach (var edge in new[] { new Vector2Int(4, 5), new Vector2Int(4, 11), new Vector2Int(5, 6),
                    new Vector2Int(6, 7), new Vector2Int(7, 8), new Vector2Int(8, 9), new Vector2Int(9, 10) })
                    controller.CreateEdge(edge.x, edge.y);
                controller.Triangulate(new Triangulator());
                session.selection.elements = new[] { 6, 7, 8 };
                var original = session.mesh.vertices.ToArray();
                var selected = session.selection.elements.Select(i => session.mesh.vertices[i]).ToArray();
                var clipboard = session.CopySelectedVertices();
                var mirrored = selected.Select(p => new Vector2(454 - p.x, p.y)).ToArray();
                for (var repeat = 0; repeat < 3; ++repeat)
                {
                    Assert.IsTrue(session.PasteVertices(clipboard, true));
                    Assert.AreEqual(original.Length + 3, session.mesh.vertexCount);
                    foreach (var p in original) Assert.IsTrue(session.mesh.vertices.Any(v => v.Equals(p)), "Existing coordinates must remain exact.");
                    CollectionAssert.AreEquivalent(mirrored, session.selection.elements.Select(i => session.mesh.vertices[i]));
                    Assert.IsTrue(session.mesh.edges.Any(e => session.selection.Contains(e.x) && session.selection.Contains(e.y)));
                }
                Assert.IsTrue(session.PasteVertices(clipboard, false));
                Assert.AreEqual(original.Length + 3, session.mesh.vertexCount);
                CollectionAssert.AreEquivalent(selected, session.selection.elements.Select(i => session.mesh.vertices[i]));
                SceneMeshSource.Validate(session.mesh);
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void VertexClipboardReusesCoincidentVerticesAndClearsForeignWeights()
        {
            var source = Quad();
            var target = Quad();
            try
            {
                Assert.IsNull(source.CopySelectedVertices());
                source.mesh.AddVertex(new Vector2(6, 8), default);
                target.mesh.AddVertex(new Vector2(6, 8), default);
                source.selection.elements = new[] { 4 };
                Assert.IsTrue(target.PasteVertices(source.CopySelectedVertices(), false));
                Assert.AreEqual(5, target.mesh.vertexCount);
                Assert.AreEqual(1, target.selection.Count);
                source.assetPath = "another source";
                source.mesh.AddVertex(new Vector2(7, 9), new BoneWeight { boneIndex0 = 3, weight0 = 1f });
                source.selection.elements = new[] { 5 };
                Assert.IsTrue(target.PasteVertices(source.CopySelectedVertices(), false));
                Assert.AreEqual(6, target.mesh.vertexCount);
                Assert.AreEqual(0f, target.mesh.vertexWeights[target.selection.elements[0]].Sum());
                var before = EditorJsonUtility.ToJson(target);
                Assert.IsFalse(target.PasteVertices("ordinary clipboard text", false));
                Assert.IsFalse(target.PasteVertices("Uniskin.SceneMeshVertices/1:{}", false));
                Assert.IsFalse(target.PasteVertices("Uniskin.SceneMeshVertices/2:{}", false));
                Assert.AreEqual(before, EditorJsonUtility.ToJson(target));
            }
            finally { Undo.ClearUndo(target); Object.DestroyImmediate(source); Object.DestroyImmediate(target); }
        }

        [Test]
        public void VertexClipboardExcludesCageFromMixedSelectionAndRejectsOldSnapshots()
        {
            var session = Quad();
            try
            {
                session.selection.elements = new[] { 0, 1, 2, 3 };
                Assert.IsNull(session.CopySelectedVertices());
                session.mesh.AddVertex(new Vector2(6, 8), default);
                session.mesh.AddVertex(new Vector2(9, 12), default);
                var controller = new SpriteMeshDataController { spriteMeshData = session.mesh };
                controller.CreateEdge(0, 4);
                controller.CreateEdge(4, 5);
                session.selection.elements = new[] { 0, 1, 4, 5 };
                var clipboard = session.CopySelectedVertices();
                var before = EditorJsonUtility.ToJson(session);
                Assert.IsFalse(session.PasteVertices(clipboard.Replace("Vertices/2:", "Vertices/1:"), true));
                Assert.AreEqual(before, EditorJsonUtility.ToJson(session));
                Assert.IsTrue(session.PasteVertices(clipboard, true));
                Assert.AreEqual(8, session.mesh.vertexCount, "Only two interior vertices may be pasted.");
                Assert.AreEqual(2, session.selection.Count);
                var cage = session.mesh.outlineEdges.SelectMany(e => new[] { e.x, e.y }).Distinct().ToArray();
                Assert.IsFalse(session.selection.elements.Any(i => cage.Contains(i)));
                Assert.IsFalse(session.mesh.edges.Any(e => session.selection.Contains(e.x) && cage.Contains(e.y) ||
                    session.selection.Contains(e.y) && cage.Contains(e.x)), "Copied cage connections must be excluded.");
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void VertexPasteCannotReuseTargetCageVertex(bool mirror)
        {
            var source = Quad();
            var target = Quad();
            try
            {
                source.mesh.AddVertex(new Vector2(8, 8), default);
                source.selection.elements = new[] { 4 };
                target.mesh.vertices[mirror ? 1 : 0] = new Vector2(mirror ? 24 : 8, 8);
                var before = EditorJsonUtility.ToJson(target);
                Assert.IsFalse(target.PasteVertices(source.CopySelectedVertices(), mirror));
                Assert.AreEqual(before, EditorJsonUtility.ToJson(target));
            }
            finally { Undo.ClearUndo(target); Object.DestroyImmediate(source); Object.DestroyImmediate(target); }
        }

        [Test]
        public void CreateMeshStagesQuadEvenWhenRuntimeGeometryAlreadyMatches()
        {
            var session = Quad();
            try
            {
                Assert.IsFalse(session.dirty);
                session.CreateMesh();
                Assert.IsTrue(session.pendingCreation);
                Assert.IsTrue(session.dirty);
                Assert.AreEqual(4, session.mesh.vertexCount);
                CollectionAssert.AreEquivalent(new[] { Vector2.zero, new Vector2(32, 0), new Vector2(32, 32), new Vector2(0, 32) }, session.mesh.vertices);
                SceneMeshSource.Validate(session.mesh);
            }
            finally { Object.DestroyImmediate(session); }
        }

        [Test]
        public void NewCancelRestoresGeometryAndFullBoneIndices()
        {
            var session = Quad();
            try
            {
                session.mesh.vertexWeights[0].SetFromBoneWeight(new BoneWeight { boneIndex0 = 7, weight0 = 1f });
                var original = JsonUtility.ToJson(session.mesh);
                session.BeginNew();
                session.mesh.AddVertex(new Vector2(4, 4), default);
                session.CancelNew();
                Assert.AreEqual(original, JsonUtility.ToJson(session.mesh));
                Assert.AreEqual(0, session.mode);
                Assert.AreEqual(7, session.mesh.vertexWeights[0].ToBoneWeight(false).boneIndex0);
            }
            finally { Object.DestroyImmediate(session); }
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void ImagePreviewClipsToMeshUnlessAuthoringHull(bool authoringHull, bool deformed, bool savedBindings)
        {
            var source = new SpriteMeshData();
            source.SetFrame(new Rect(64, 128, 32, 32));
            var vertices = new[] { new Vector2(4, 4), new Vector2(28, 4), new Vector2(16, 28) };
            foreach (var vertex in vertices) source.AddVertex(vertex, default);
            source.SetIndices(new[] { 0, 1, 2 });
            var posed = deformed ? new SpriteMeshData() : null;
            if (posed != null)
            {
                foreach (var vertex in vertices) posed.AddVertex(vertex + new Vector2(8, -12), default);
                posed.SetIndices(source.indices.ToArray());
            }
            var textureOffset = savedBindings ? new Vector2(2, 3) : Vector2.zero;
            var pose = savedBindings ? new SceneMeshBindingPose
            {
                positions = vertices.ToArray(),
                texturePositions = vertices.Select(vertex => vertex + textureOffset).ToArray(),
                indices = source.indices.ToArray()
            } : null;
            var image = new Mesh();
            var original = JsonUtility.ToJson(source);
            try
            {
                SceneEditMeshWindow.BuildImageMesh(image, source, posed, pose, authoringHull, 256, 256);
                var expected = authoringHull ? new[] { Vector2.zero, new Vector2(32, 0), new Vector2(32, 32), new Vector2(0, 32) } :
                    (posed ?? source).vertices.ToArray();
                CollectionAssert.AreEqual(expected.Select(vertex => (Vector3)vertex).ToArray(), image.vertices);
                CollectionAssert.AreEqual(authoringHull ? new[] { 0, 1, 2, 0, 2, 3 } : source.indices, image.triangles);
                var textureVertices = authoringHull ? expected : vertices;
                for (var i = 0; i < textureVertices.Length; ++i)
                    Assert.Less(Vector2.Distance((source.frame.position + textureVertices[i] + textureOffset) / 256, image.uv[i]), 0.00001f);
                Assert.AreEqual(original, JsonUtility.ToJson(source), "Preview must not change authored geometry or weights.");
            }
            finally { Object.DestroyImmediate(image); }
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        public void NewHullKeepsSourceImageVisibleWithSavedBindings(int vertexCount, bool savedBindings)
        {
            var session = Quad();
            var image = new Mesh();
            try
            {
                var pose = savedBindings ? new SceneMeshBindingPose
                {
                    positions = session.mesh.vertices.ToArray(),
                    texturePositions = session.mesh.vertices.Select(vertex => vertex + new Vector2(4, 8)).ToArray(),
                    indices = session.mesh.indices.ToArray()
                } : null;
                session.BeginNew();
                for (var i = 0; i < vertexCount; ++i)
                    session.mesh.AddVertex(new Vector2(i * 8, i % 2 * 8), default);
                SceneEditMeshWindow.BuildImageMesh(image, session.mesh, null, pose, session.mode == 3, 64, 64);
                Assert.AreEqual(4, image.vertexCount);
                CollectionAssert.AreEqual(new[] { 0, 1, 2, 0, 2, 3 }, image.triangles);
                CollectionAssert.AreEqual(new[] { Vector3.zero, new Vector3(32, 0), new Vector3(32, 32), new Vector3(0, 32) }, image.vertices);
                var offset = savedBindings ? new Vector2(4, 8) : Vector2.zero;
                CollectionAssert.AreEqual(new[] { Vector2.zero, new Vector2(32, 0), new Vector2(32, 32), new Vector2(0, 32) }
                    .Select(vertex => (vertex + offset) / 64).ToArray(), image.uv);
                Assert.AreEqual(vertexCount, session.mesh.vertexCount, "The image fallback must not add authored geometry.");
                Assert.IsEmpty(session.mesh.indices);
                session.CancelNew();
                SceneEditMeshWindow.BuildImageMesh(image, session.mesh, null, pose, false, 64, 64);
                Assert.AreEqual(6, image.triangles.Length);
                if (savedBindings)
                {
                    CollectionAssert.AreEqual(session.mesh.indices, image.triangles);
                    CollectionAssert.AreEqual(pose.texturePositions.Select(vertex => vertex / 64).ToArray(), image.uv);
                }
            }
            finally { Object.DestroyImmediate(image); Object.DestroyImmediate(session); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void LeavingNewRestoresIncompleteHullOrCompletesValidHull(int vertexCount)
        {
            var session = Quad();
            try
            {
                session.mesh.vertexWeights[0].SetFromBoneWeight(new BoneWeight { boneIndex0 = 7, weight0 = 1f });
                var original = JsonUtility.ToJson(session.mesh);
                session.BeginNew();
                var positions = new[] { Vector2.zero, new Vector2(32, 0), new Vector2(0, 32) };
                var controller = new SpriteMeshDataController { spriteMeshData = session.mesh };
                for (var i = 0; i < vertexCount; ++i)
                {
                    session.mesh.AddVertex(positions[i], default);
                    if (i > 0) controller.CreateEdge(i - 1, i);
                }
                session.selection.elements = vertexCount > 0 ? new[] { 0 } : new int[0];
                session.FinishNew();
                Assert.AreEqual(0, session.mode);
                Assert.IsNull(session.newMeshBackup);
                Assert.IsEmpty(session.selection.elements);
                SceneMeshSource.Validate(session.mesh);
                if (vertexCount < 3)
                    Assert.AreEqual(original, JsonUtility.ToJson(session.mesh));
                else
                {
                    Assert.AreEqual(3, session.mesh.vertexCount);
                    Assert.AreEqual(3, session.mesh.indices.Length);
                }
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void EscapeDuringAxisDragRestoresNewHullWithoutCancelingNew()
        {
            var window = ScriptableObject.CreateInstance<SceneEditMeshWindow>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(SceneEditMeshWindow);
            var session = (SceneMeshSession)type.GetField("m_Session", flags).GetValue(window);
            try
            {
                session.mesh.SetFrame(new Rect(0, 0, 32, 32));
                session.BeginNew();
                session.SetSymmetry(true);
                session.AddSymmetryVertex(new Vector2(16, 32));
                session.AddSymmetryVertex(new Vector2(4, 24));
                var mesh = JsonUtility.ToJson(session.mesh);
                var backup = EditorJsonUtility.ToJson(session);
                Undo.IncrementCurrentGroup();
                type.GetField("m_SymmetryAxisUndoGroup", flags).SetValue(window, Undo.GetCurrentGroup());
                type.GetField("m_SymmetryAxisBackup", flags).SetValue(window, backup);
                type.GetField("m_SymmetryAxisControl", flags).SetValue(window, 12345);
                session.SetSymmetryAxis(18);
                Assert.IsTrue(window.HandleSceneEscape(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }));
                Assert.AreEqual(16, session.symmetryAxis);
                Assert.AreEqual(mesh, JsonUtility.ToJson(session.mesh));
                Assert.AreEqual(3, session.mode);
                Assert.IsFalse(session.isUndoOperationSet);
                Assert.AreEqual(0, type.GetField("m_SymmetryAxisControl", flags).GetValue(window));
                // A subsequent drag must use the restored authored half, not the canceled preview.
                session.SetSymmetryAxis(17);
                Assert.AreEqual(new Vector2(30, 24), session.mesh.vertices[2]);
            }
            finally
            {
                Undo.ClearUndo(session);
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void MovingSymmetryAxisPreservesExistingGeometryAndUsesNewPairsWithUndo()
        {
            var session = Quad();
            try
            {
                session.SetSymmetry(true);
                var original = JsonUtility.ToJson(session.mesh);
                bool dirty = session.dirty;
                Undo.IncrementCurrentGroup();
                session.SetSymmetryAxis(12);
                session.SetSymmetryAxis(10);
                session.EndUndoOperation();
                Assert.AreEqual(original, JsonUtility.ToJson(session.mesh));
                Assert.AreEqual(dirty, session.dirty);
                Assert.AreEqual(new Vector2(16, 8), session.MirrorHullPoint(new Vector2(4, 8)));
                Undo.PerformUndo();
                Assert.AreEqual(16, session.symmetryAxis);
                Undo.PerformRedo();
                Assert.AreEqual(10, session.symmetryAxis);
                session.CreateSymmetricGeometry(new Vector2(6, 12));
                Assert.GreaterOrEqual(session.FindSymmetryVertex(new Vector2(14, 12)), 0);
                session.EndUndoOperation();
                session.SetSymmetryAxis(20);
                session.EndUndoOperation();
                session.CreateSymmetricGeometry(new Vector2(14, 12));
                int left = session.FindSymmetryVertex(new Vector2(14, 12));
                // Reaching the unsplit outline is still rejected by the shared collision check.
                var beforeMove = JsonUtility.ToJson(session.mesh);
                Assert.IsFalse(session.MoveSymmetricGeometry(new[] { left }, new[] { new Vector2(0, 12) }));
                Assert.AreEqual(beforeMove, JsonUtility.ToJson(session.mesh));
                Assert.IsTrue(session.MoveSymmetricGeometry(new[] { left }, new[] { new Vector2(9, 12) }));
                Assert.GreaterOrEqual(session.FindSymmetryVertex(new Vector2(9, 12)), 0);
                Assert.GreaterOrEqual(session.FindSymmetryVertex(new Vector2(31, 12)), 0);
                SceneMeshSource.Validate(session.mesh);
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void MovingNewSymmetryAxisPreservesAuthoredSideAndKeepsMirrorsInsideFrame()
        {
            var session = Quad();
            try
            {
                session.BeginNew();
                session.SetSymmetry(true);
                session.AddSymmetryVertex(new Vector2(16, 32));
                session.AddSymmetryVertex(new Vector2(4, 24));
                session.AddSymmetryVertex(new Vector2(4, 8));
                var before = JsonUtility.ToJson(session.mesh);
                Undo.IncrementCurrentGroup();
                session.SetSymmetryAxis(30);
                session.EndUndoOperation();
                Assert.AreEqual(18, session.symmetryAxis);
                CollectionAssert.AreEqual(new[] { new Vector2(18, 32), new Vector2(4, 24),
                    new Vector2(4, 8), new Vector2(32, 8), new Vector2(32, 24) }, session.mesh.vertices);
                Assert.IsEmpty(session.mesh.indices);
                Undo.PerformUndo();
                Assert.AreEqual(before, JsonUtility.ToJson(session.mesh));
                Assert.AreEqual(16, session.symmetryAxis);
                Undo.PerformRedo();
                session.SetSymmetryAxis(18);
                session.AddSymmetryVertex(new Vector2(18, 0));
                Assert.AreEqual(0, session.mode);
                SceneMeshSource.Validate(session.mesh);
                session.SetSymmetryAxis(float.NaN);
                Assert.AreEqual(18, session.symmetryAxis);
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void OffsetSymmetryNewClampsClicksToReflectableBoundsAndRetainsAxisAcrossModes()
        {
            var session = Quad();
            try
            {
                session.SetSymmetryAxis(24);
                session.EndUndoOperation();
                session.BeginNew();
                session.SetSymmetry(true);
                session.AddSymmetryVertex(new Vector2(0, 28));
                CollectionAssert.AreEqual(new[] { new Vector2(16, 28), new Vector2(32, 28) }, session.mesh.vertices);
                session.SetSymmetry(false);
                Assert.AreEqual(24, session.symmetryAxis);
                session.CancelNew();
                Assert.AreEqual(24, session.symmetryAxis);
                session.SetSymmetryAxis(16);
                session.EndUndoOperation();
                Assert.AreEqual(16, session.symmetryAxis);
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void SymmetricNewHullUsesOneAxisVertexAndClosesInContourOrder()
        {
            var session = Quad();
            try
            {
                Assert.IsFalse(session.symmetryActive);
                session.BeginNew();
                Assert.IsFalse(session.symmetry);
                session.SetSymmetry(true);
                session.AddSymmetryVertex(new Vector2(16.1f, 32));
                session.AddSymmetryVertex(new Vector2(4, 24));
                session.AddSymmetryVertex(new Vector2(28, 8));
                Assert.IsEmpty(session.mesh.indices);
                session.AddSymmetryVertex(new Vector2(16, 0));
                CollectionAssert.AreEqual(new[] { new Vector2(16, 32), new Vector2(4, 24),
                    new Vector2(4, 8), new Vector2(16, 0), new Vector2(28, 8), new Vector2(28, 24) }, session.mesh.vertices);
                Assert.AreEqual(0, session.mode);
                session.FinishNew();
                Assert.AreEqual(0, session.mode);
                Assert.IsTrue(session.symmetryActive);
                Assert.AreEqual(12, session.mesh.indices.Length);
                SceneMeshSource.Validate(session.mesh);
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void SymmetricNewHullMovesAndDeletesBothSidesWithUndoAndCancel()
        {
            var session = Quad();
            try
            {
                var original = JsonUtility.ToJson(session.mesh);
                session.BeginNew();
                session.SetSymmetry(true);
                session.AddSymmetryVertex(new Vector2(4, 28));
                session.AddSymmetryVertex(new Vector2(4, 4));
                session.BeginUndoOperation("Move Symmetry Test");
                session.MoveSymmetryVertices(new[] { 3 }, new[] { new Vector2(26, 26) });
                session.EndUndoOperation();
                Assert.AreEqual(new Vector2(6, 26), session.mesh.vertices[0]);
                Assert.AreEqual(new Vector2(26, 26), session.mesh.vertices[3]);
                var beforeDelete = JsonUtility.ToJson(session.mesh);
                Undo.IncrementCurrentGroup();
                session.RemoveSymmetryVertices(new[] { 0, 3 });
                Assert.AreEqual(2, session.mesh.vertexCount);
                Undo.PerformUndo();
                Assert.AreEqual(beforeDelete, JsonUtility.ToJson(session.mesh));
                Assert.IsTrue(session.symmetryActive);
                session.AddSymmetryVertex(new Vector2(16, 0));
                Assert.AreEqual(5, session.mesh.vertexCount);
                session.CancelNew();
                Assert.AreEqual(original, JsonUtility.ToJson(session.mesh));
                Assert.IsTrue(session.symmetry);
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void SymmetryDragKeepsAxisAndPairHandleIndicesStable()
        {
            var session = Quad();
            try
            {
                session.BeginNew();
                session.mesh.AddVertex(new Vector2(16, 32), default);
                session.mesh.AddVertex(new Vector2(4, 24), default);
                session.mesh.AddVertex(new Vector2(16, 0), default);
                session.SetSymmetry(true);
                session.BeginUndoOperation("Move Symmetry Test");
                session.MoveSymmetryVertices(new[] { 0 }, new[] { new Vector2(8, 30) });
                Assert.AreEqual(new Vector2(16, 30), session.mesh.vertices[0]);
                session.MoveSymmetryVertices(new[] { 3 }, new[] { new Vector2(12, 20) });
                Assert.AreEqual(4, session.mesh.vertexCount);
                Assert.Greater(session.mesh.vertices[3].x, 16);
                Assert.Less(session.mesh.vertices[1].x, 16);
                Assert.AreEqual(32, session.mesh.vertices[1].x + session.mesh.vertices[3].x);
                CollectionAssert.AreEqual(new[] { 3 }, session.selection.elements);
                session.EndUndoOperation();
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void SymmetryToggleRetainsHullAndStaysActiveAcrossModes()
        {
            var session = Quad();
            try
            {
                session.BeginNew();
                session.mesh.AddVertex(new Vector2(4, 28), default);
                session.mesh.AddVertex(new Vector2(4, 4), default);
                session.SetSymmetry(true);
                Assert.AreEqual(4, session.mesh.vertexCount);
                var mirrored = JsonUtility.ToJson(session.mesh);
                session.SetSymmetry(false);
                Assert.AreEqual(mirrored, JsonUtility.ToJson(session.mesh));
                session.SetSymmetry(true);
                Assert.AreEqual(mirrored, JsonUtility.ToJson(session.mesh));
                session.FinishNew();
                Assert.IsTrue(session.symmetryActive);
                session.BeginNew();
                Assert.IsTrue(session.symmetryActive);
                session.AddSymmetryVertex(new Vector2(4, 4));
                session.FinishNew();
                Assert.AreEqual(0, session.mode);
                Assert.IsTrue(session.symmetry);
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void SymmetricCreateUsesLocalWeightsSplitsBothEdgesAndReusesAxisVertices()
        {
            var session = Quad();
            try
            {
                session.SetSymmetry(true);
                for (var i = 0; i < session.mesh.vertexCount; ++i)
                    session.mesh.vertexWeights[i].SetFromBoneWeight(new BoneWeight {
                        boneIndex0 = session.mesh.vertices[i].x < 16 ? 0 : 1, weight0 = 1f });
                var originalWeights = session.mesh.vertexWeights.Take(4).Select(w => w.ToBoneWeight(false)).ToArray();
                session.CreateSymmetricGeometry(new Vector2(8, 12));
                int left = session.FindSymmetryVertex(new Vector2(8, 12));
                int right = session.FindSymmetryVertex(new Vector2(24, 12));
                Assert.AreEqual(6, session.mesh.vertexCount);
                Assert.AreNotEqual(session.mesh.vertexWeights[left].ToBoneWeight(false), session.mesh.vertexWeights[right].ToBoneWeight(false));
                session.CreateSymmetricGeometry(new Vector2(8, 12));
                Assert.AreEqual(6, session.mesh.vertexCount);
                session.CreateSymmetricGeometry(new Vector2(16.1f, 16));
                Assert.AreEqual(7, session.mesh.vertexCount);
                Assert.GreaterOrEqual(session.FindSymmetryVertex(new Vector2(16, 16)), 0);
                session.CreateSymmetricGeometry(new Vector2(0, 16));
                Assert.AreEqual(9, session.mesh.vertexCount);
                foreach (var point in new[] { new Vector2(0, 16), new Vector2(32, 16) })
                {
                    int vertex = session.FindSymmetryVertex(point);
                    Assert.AreEqual(2, session.mesh.outlineEdges.Count(e => e.x == vertex || e.y == vertex));
                }
                for (var i = 0; i < 4; ++i)
                    Assert.AreEqual(originalWeights[i], session.mesh.vertexWeights[i].ToBoneWeight(false));
                SceneMeshSource.Validate(session.mesh);
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void UniskinMeshPreferencesPersistValidateAndResetWithoutChangingGeometry()
        {
            using var preferences = new MeshPreferencesScope();
            Assert.AreEqual("Preferences/Uniskin", new UserSettings().settingsPath);
            Assert.AreEqual(2f, MeshEditorSettings.symmetryThreshold);
            Assert.AreEqual(Color.white, MeshEditorSettings.selectedVertexColor);
            Assert.AreEqual(new Color(1f, 1f, 0f), MeshEditorSettings.hoveredVertexColor);
            Assert.AreEqual(Color.cyan, MeshEditorSettings.meshLineColor);
            MeshEditorSettings.symmetryThreshold = 3.5f;
            Assert.AreEqual(3.5f, EditorPrefs.GetFloat(MeshEditorSettings.kSymmetryThresholdKey));
            MeshEditorSettings.selectedVertexColor = Color.red;
            MeshEditorSettings.hoveredVertexColor = Color.green;
            MeshEditorSettings.meshLineColor = Color.blue;
            Assert.AreEqual(Color.red, MeshEditorSettings.selectedVertexColor);
            Assert.AreEqual(Color.green, MeshEditorSettings.hoveredVertexColor);
            Assert.AreEqual(Color.blue, MeshEditorSettings.meshLineColor);
            MeshEditorSettings.symmetryThreshold = -1f;
            Assert.AreEqual(0f, MeshEditorSettings.symmetryThreshold);
            MeshEditorSettings.symmetryThreshold = float.NaN;
            Assert.AreEqual(2f, MeshEditorSettings.symmetryThreshold);
            EditorPrefs.SetFloat(MeshEditorSettings.kSymmetryThresholdKey, float.PositiveInfinity);
            Assert.AreEqual(2f, MeshEditorSettings.symmetryThreshold);
            EditorPrefs.SetString(MeshEditorSettings.kMeshLineColorKey, "invalid");
            Assert.AreEqual(Color.cyan, MeshEditorSettings.meshLineColor);
            MeshEditorSettings.ResetDefaults();
            Assert.AreEqual(Color.white, MeshEditorSettings.selectedVertexColor);
            Assert.AreEqual(new Color(1f, 1f, 0f), MeshEditorSettings.hoveredVertexColor);
            Assert.AreEqual(Color.cyan, MeshEditorSettings.meshLineColor);
            Assert.AreEqual(2f, MeshEditorSettings.symmetryThreshold);
        }

        [Test]
        public void SymmetricModifyMatchesApproximatePartnersWithoutRelaxingCreationPrecision()
        {
            using var preferences = new MeshPreferencesScope();
            var session = Quad();
            try
            {
                session.SetSymmetry(true);
                session.CreateSymmetricGeometry(new Vector2(8, 12));
                int left = session.FindSymmetryVertex(new Vector2(8, 12));
                int right = session.FindSymmetryVertex(new Vector2(24, 12));
                session.mesh.vertices[right] = new Vector2(24.6f, 12.4f);
                Assert.AreEqual(-1, session.FindSymmetryVertex(new Vector2(24, 12)), "Creation lookup remains precise.");
                Assert.AreEqual(right, session.FindSymmetryPartner(left));
                Assert.AreEqual(left, session.FindSymmetryPartner(right));
                Assert.AreEqual(-1, session.FindSymmetryPartner(-1));
                CollectionAssert.AreEquivalent(new[] { left, right }, session.SymmetrySelection(new[] { left }));
                session.EndUndoOperation();
                var before = JsonUtility.ToJson(session.mesh);
                Undo.IncrementCurrentGroup();
                Assert.IsTrue(session.MoveSymmetricGeometry(new[] { left, right },
                    new[] { new Vector2(10, 14), new Vector2(26.6f, 14.4f) }));
                session.EndUndoOperation();
                Assert.AreEqual(new Vector2(10, 14), session.mesh.vertices[left]);
                Assert.AreEqual(new Vector2(22, 14), session.mesh.vertices[right]);
                Undo.PerformUndo();
                Assert.AreEqual(before, JsonUtility.ToJson(session.mesh));
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [TestCase(32f)]
        [TestCase(1000f)]
        public void SymmetryPartnersRespectConfiguredThresholdAndRejectUnmatchedOrAmbiguousVertices(float width)
        {
            using var preferences = new MeshPreferencesScope();
            var session = ScriptableObject.CreateInstance<SceneMeshSession>();
            try
            {
                session.mesh.SetFrame(new Rect(0, 0, width, width));
                float tolerance = Mathf.Max(1f, width * 0.01f);
                MeshEditorSettings.symmetryThreshold = tolerance;
                var left = new Vector2(width * 0.25f, width * 0.4f);
                var right = new Vector2(width * 0.75f, left.y);
                session.mesh.AddVertex(left, default);
                session.mesh.AddVertex(right + new Vector2(0, tolerance * 0.75f), default);
                Assert.AreEqual(1, session.FindSymmetryPartner(0));
                MeshEditorSettings.symmetryThreshold = 0f;
                Assert.AreEqual(-1, session.FindSymmetryPartner(0), "Zero threshold uses precise matching.");
                MeshEditorSettings.symmetryThreshold = tolerance;
                session.mesh.vertices[1] = right + new Vector2(0, tolerance * 1.25f);
                Assert.AreEqual(-1, session.FindSymmetryPartner(0));
                session.mesh.vertices[1] = right;
                session.mesh.AddVertex(left + new Vector2(0, tolerance * 0.25f), default);
                Assert.AreEqual(1, session.FindSymmetryPartner(0));
                Assert.AreEqual(0, session.FindSymmetryPartner(1));
                Assert.AreEqual(-1, session.FindSymmetryPartner(2), "A second nearby handle cannot share the partner.");
                session.mesh.AddVertex(new Vector2(width * 0.5f - 0.1f, width * 0.8f), default);
                Assert.AreEqual(-1, session.FindSymmetryPartner(3), "A near-axis vertex cannot pair with itself.");
                session.mesh.AddVertex(new Vector2(width * 0.5f, width * 0.9f), default);
                Assert.AreEqual(4, session.FindSymmetryPartner(4));
            }
            finally { Object.DestroyImmediate(session); }
        }

        [Test]
        public void SymmetryHoverUsesSourcePartnersAndClearsWhenDisabledOrInNew()
        {
            using var preferences = new MeshPreferencesScope();
            var session = Quad();
            var window = ScriptableObject.CreateInstance<SceneEditMeshWindow>();
            try
            {
                session.SetSymmetry(true);
                session.CreateSymmetricGeometry(new Vector2(8, 12));
                int left = session.FindSymmetryVertex(new Vector2(8, 12));
                int right = session.FindSymmetryVertex(new Vector2(24, 12));
                session.mesh.vertices[right] += new Vector2(0.5f, 0.25f);
                session.mesh.vertexWeights[left].SetFromBoneWeight(new BoneWeight { boneIndex0 = 0, weight0 = 1 });
                session.mesh.vertexWeights[right].SetFromBoneWeight(new BoneWeight { boneIndex0 = 1, weight0 = 1 });
                var deformation = new SceneMeshDeformation(session.mesh, new[] {
                    Matrix4x4.Translate(new Vector3(2, 3)), Matrix4x4.Translate(new Vector3(-4, 5)) });
                var controller = new SpriteMeshController();
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(SceneEditMeshWindow).GetField("m_Session", flags).SetValue(window, session);
                typeof(SceneEditMeshWindow).GetField("m_Controller", flags).SetValue(window, controller);
                var configure = typeof(SceneEditMeshWindow).GetMethod("ConfigureSymmetryEditing", flags);
                configure.Invoke(window, new object[] { deformation });
                Assert.AreEqual(right, controller.geometryHoveredVertexPartner(left));
                Assert.AreEqual(left, controller.geometryHoveredVertexPartner(right));
                Assert.AreNotEqual(session.mesh.vertices[right], deformation.mesh.vertices[right]);
                session.symmetry = false;
                configure.Invoke(window, new object[] { deformation });
                Assert.IsNull(controller.geometryHoveredVertexPartner);
                session.symmetry = true;
                session.mode = 3;
                configure.Invoke(window, new object[] { null });
                Assert.IsNull(controller.geometryHoveredVertexPartner);
            }
            finally
            {
                typeof(SceneEditMeshWindow).GetField("m_Session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(window, null);
                Object.DestroyImmediate(window);
                Undo.ClearUndo(session); Object.DestroyImmediate(session);
            }
        }

        [Test]
        public void SymmetricModifyMovesPairsOnceKeepsAxisAndSupportsUndo()
        {
            var session = Quad();
            try
            {
                session.SetSymmetry(true);
                session.CreateSymmetricGeometry(new Vector2(8, 12));
                session.CreateSymmetricGeometry(new Vector2(16, 20));
                session.EndUndoOperation();
                int left = session.FindSymmetryVertex(new Vector2(8, 12));
                int right = session.FindSymmetryVertex(new Vector2(24, 12));
                int axis = session.FindSymmetryVertex(new Vector2(16, 20));
                var before = JsonUtility.ToJson(session.mesh);
                Undo.IncrementCurrentGroup();
                Assert.IsTrue(session.MoveSymmetricGeometry(new[] { right, left }, new[] { new Vector2(22, 14), new Vector2(6, 14) }));
                session.EndUndoOperation();
                Assert.GreaterOrEqual(session.FindSymmetryVertex(new Vector2(10, 14)), 0);
                Assert.GreaterOrEqual(session.FindSymmetryVertex(new Vector2(22, 14)), 0);
                Undo.PerformUndo();
                Assert.AreEqual(before, JsonUtility.ToJson(session.mesh));
                Assert.IsTrue(session.MoveSymmetricGeometry(new[] { axis }, new[] { new Vector2(10, 22) }));
                session.EndUndoOperation();
                Assert.AreEqual(new Vector2(16, 22), session.mesh.vertices[axis]);
                CollectionAssert.AreEquivalent(new[] { left, right }, session.SymmetrySelection(new[] { right }));
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void SymmetricEdgeCreationMirrorsEdgesAndFailedCreationRollsBack()
        {
            var session = Quad();
            try
            {
                session.SetSymmetry(true);
                session.CreateSymmetricGeometry(new Vector2(8, 8));
                int left = session.FindSymmetryVertex(new Vector2(8, 8));
                int right = session.FindSymmetryVertex(new Vector2(24, 8));
                session.CreateSymmetricGeometry(new Vector2(8, 24), left);
                int leftEnd = session.FindSymmetryVertex(new Vector2(8, 24));
                int rightEnd = session.FindSymmetryVertex(new Vector2(24, 24));
                Assert.IsTrue(session.mesh.edges.ContainsAny(new Unity.Mathematics.int2(left, leftEnd)));
                Assert.IsTrue(session.mesh.edges.ContainsAny(new Unity.Mathematics.int2(right, rightEnd)));
                session.EndUndoOperation();
                // Trim the right boundary so the first point is legal and its mirror isn't.
                for (var i = 0; i < session.mesh.vertexCount; ++i)
                    if (session.mesh.vertices[i].x == 32) session.mesh.vertices[i].x = 28;
                new SpriteMeshDataController { spriteMeshData = session.mesh }.Triangulate(new Triangulator());
                var before = JsonUtility.ToJson(session.mesh);
                var selection = session.selection.elements;
                Assert.Throws<InvalidOperationException>(() => session.CreateSymmetricGeometry(new Vector2(2, 16)));
                Assert.AreEqual(before, JsonUtility.ToJson(session.mesh));
                CollectionAssert.AreEqual(selection, session.selection.elements);
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void MovingBothEdgeEndpointsTestsTheEntireMovedSegment()
        {
            var vertices = new[] { new Vector2(8, 8), new Vector2(8, 24), new Vector2(24, 8), new Vector2(24, 24) };
            var edges = new[] { new Unity.Mathematics.int2(0, 1), new Unity.Mathematics.int2(2, 3) };
            Assert.IsFalse(SpriteMeshController.IsMovedEdgeIntersectingWithOtherEdge(new[] { 0, 1 },
                new[] { new Vector2(26, 8), new Vector2(26, 24) }, edges, vertices));
        }

        [Test]
        public void SymmetricEditingMapsThroughEachSidesDeformation()
        {
            var session = Quad();
            try
            {
                session.SetSymmetry(true);
                session.CreateSymmetricGeometry(new Vector2(8, 12));
                int left = session.FindSymmetryVertex(new Vector2(8, 12));
                int right = session.FindSymmetryVertex(new Vector2(24, 12));
                session.mesh.vertexWeights[left].SetFromBoneWeight(new BoneWeight { boneIndex0 = 0, weight0 = 1 });
                session.mesh.vertexWeights[right].SetFromBoneWeight(new BoneWeight { boneIndex0 = 1, weight0 = 1 });
                var deformation = new SceneMeshDeformation(session.mesh, new[] {
                    Matrix4x4.Translate(new Vector3(2, 3)), Matrix4x4.Translate(new Vector3(-4, 5)) });
                var source = deformation.SourcePosition(new Vector2(12, 17), deformation.mesh.vertexWeights[left]);
                Assert.AreEqual(new Vector2(10, 14), source);
                Assert.IsTrue(session.MoveSymmetricGeometry(new[] { left }, new[] { source }));
                Assert.AreEqual(new Vector2(22, 14), session.mesh.vertices[right]);
                Assert.AreEqual(new Vector2(18, 19), deformation.DisplayPosition(session.mesh.vertices[right], session.mesh.vertexWeights[right]));
                SceneMeshSource.Validate(session.mesh);
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void SymmetricNewCanCloseAtEitherStartHandleUsingFreshHitTesting()
        {
            var session = Quad();
            try
            {
                session.BeginNew();
                session.SetSymmetry(true);
                session.AddSymmetryVertex(new Vector2(4, 28));
                session.AddSymmetryVertex(new Vector2(4, 4));
                int mirrorStart = session.mesh.vertexCount - 1;
                var gui = new DragGUI();
                var view = new SpriteMeshView(gui) { mode = SpriteMeshViewMode.NewGeometry,
                    selection = session.selection, defaultControlID = 99, frame = new Rect(0, 0, 32, 32),
                    newGeometryClosureVertex = session.IsNewHullClosureVertex };
                var point = session.mesh.vertices[mirrorStart];
                gui.Event(EventType.Layout, point);
                view.BeginLayout(); view.LayoutVertex(point, mirrorStart); view.EndLayout();
                gui.Event(EventType.MouseDown, point);
                view.BeginLayout(); view.EndLayout();
                Assert.IsTrue(view.DoCompleteNewGeometry());
                var controller = new SpriteMeshController { newGeometryCompletionRequested = session.CompleteNew };
                typeof(SpriteMeshController).GetMethod("CompleteNewGeometry", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).Invoke(controller, null);
                Assert.AreEqual(0, session.mode);
                Assert.IsTrue(session.symmetryActive);
                Assert.AreEqual(6, session.mesh.indices.Length);
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void SceneEscapeConsumesKeyAndClearsVerticesBeforeObjectDeselection(int mode)
        {
            var original = Quad();
            var window = ScriptableObject.CreateInstance<SceneEditMeshWindow>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var objectSelection = Selection.objects;
            try
            {
                var session = (SceneMeshSession)typeof(SceneEditMeshWindow).GetField("m_Session", flags).GetValue(window);
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(original), session);
                session.mode = mode;
                session.selection.Select(0, true);
                session.selection.Select(1, true);
                var before = JsonUtility.ToJson(session.mesh);
                var evt = new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape };
                Assert.IsTrue(window.HandleSceneEscape(evt));
                Assert.AreEqual(EventType.Used, evt.type, "Native Scene deselection must not receive Escape.");
                Assert.AreEqual(0, session.selection.Count);
                Assert.AreEqual(mode, session.mode);
                Assert.AreEqual(before, JsonUtility.ToJson(session.mesh));
                CollectionAssert.AreEqual(objectSelection, Selection.objects);
            }
            finally { Object.DestroyImmediate(window); Undo.ClearUndo(original); Object.DestroyImmediate(original); }
        }

        [Test]
        public void EscapeInMeshPopupCancelsNewAndRestoresGeometry()
        {
            var original = Quad();
            var window = ScriptableObject.CreateInstance<SceneEditMeshWindow>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            try
            {
                var session = (SceneMeshSession)typeof(SceneEditMeshWindow).GetField("m_Session", flags).GetValue(window);
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(original), session);
                var before = JsonUtility.ToJson(session.mesh);
                window.CreateGUI();
                window.ShowPopup(); // UI Toolkit dispatch requires an attached panel.
                session.BeginNew();
                session.mesh.AddVertex(new Vector2(4, 4), default);
                using (var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
                {
                    evt.target = window.rootVisualElement;
                    window.rootVisualElement.SendEvent(evt);
                }
                Assert.AreEqual(0, session.mode);
                Assert.IsNull(session.newMeshBackup);
                Assert.AreEqual(before, JsonUtility.ToJson(session.mesh));
            }
            finally { Object.DestroyImmediate(window); Undo.ClearUndo(original); Object.DestroyImmediate(original); }
        }

        [Test]
        public void NewHullClosesAndRejectsCrossingEdges()
        {
            var session = Quad();
            try
            {
                session.BeginNew();
                session.mesh.AddVertex(new Vector2(0, 0), default);
                session.mesh.AddVertex(new Vector2(32, 32), default);
                session.mesh.AddVertex(new Vector2(0, 32), default);
                session.mesh.AddVertex(new Vector2(32, 0), default);
                Assert.Throws<InvalidOperationException>(() => session.CompleteNew());
                Assert.AreEqual(3, session.mode);
                session.CancelNew();
                session.BeginNew();
                var controller = new SpriteMeshDataController { spriteMeshData = session.mesh };
                session.mesh.AddVertex(new Vector2(0, 0), default);
                session.mesh.AddVertex(new Vector2(32, 0), default);
                session.mesh.AddVertex(new Vector2(0, 32), default);
                controller.CreateEdge(0, 1); controller.CreateEdge(1, 2);
                session.CompleteNew();
                SceneMeshSource.Validate(session.mesh);
                Assert.AreEqual(3, session.mesh.vertexCount);
                Assert.AreEqual(3, session.mesh.indices.Length);
                Assert.AreEqual(0, session.mode);
            }
            finally { Object.DestroyImmediate(session); }
        }

        [Test]
        public void TraceDetectsOnlyWeightsBoundToExistingBones()
        {
            var session = Quad();
            try
            {
                session.mesh.vertexWeights[0].SetFromBoneWeight(new BoneWeight { weight0 = 1f });
                Assert.IsFalse(session.hasBoneWeights, "Default importer weights without bones are not skinning.");
                session.mesh.bones.Add(new SpriteBoneData());
                Assert.IsTrue(session.hasBoneWeights);
                session.mesh.vertexWeights[0].Clear();
                Assert.IsFalse(session.hasBoneWeights, "Bones alone do not make a mesh weighted.");
                session.mesh.vertexWeights[0].SetFromBoneWeight(new BoneWeight { boneIndex0 = 7, weight0 = 1f });
                Assert.IsFalse(session.hasBoneWeights, "An invalid bone reference is not a bound weight.");
            }
            finally { Object.DestroyImmediate(session); }
        }

        [TestCase(false, 50f)]
        [TestCase(true, 50f)]
        [TestCase(false, 40f)]
        public void LowDetailTraceEnclosesRoundContourWithFewVertices(bool reverse, float frameRadius)
        {
            var contour = Enumerable.Range(0, 64).Select(i =>
                new Vector2(Mathf.Cos(i * Mathf.PI / 32f), Mathf.Sin(i * Mathf.PI / 32f)) * 40f).ToArray();
            if (reverse) Array.Reverse(contour);
            var bounds = new Rect(-frameRadius, -frameRadius, frameRadius * 2f, frameRadius * 2f);
            var simplified = SceneMeshOutlineGenerator.Simplify(contour, bounds, 0f);
            Assert.That(simplified.Length, Is.InRange(3, 12));
            AssertContourCovered(contour, simplified);
            Assert.IsTrue(simplified.All(point => point.x >= bounds.xMin && point.x <= bounds.xMax &&
                point.y >= bounds.yMin && point.y <= bounds.yMax));
            CollectionAssert.AreEqual(contour, SceneMeshOutlineGenerator.Simplify(contour, bounds, 1f));
        }

        [Test]
        public void TracePreservesMinimumTriangleAndConcaveBoundaryCoverage()
        {
            var triangle = new[] { Vector2.zero, new Vector2(32, 0), new Vector2(0, 32) };
            CollectionAssert.AreEqual(triangle, SceneMeshOutlineGenerator.Simplify(triangle, new Rect(0, 0, 32, 32), 0f));
            var concave = new[] { Vector2.zero, new Vector2(32, 0), new Vector2(32, 8),
                new Vector2(8, 8), new Vector2(8, 32), new Vector2(0, 32) };
            var simplified = SceneMeshOutlineGenerator.Simplify(concave, new Rect(0, 0, 32, 32), 0f);
            Assert.That(simplified.Length, Is.InRange(3, concave.Length));
            AssertContourCovered(concave, simplified);
        }

        static void AssertContourCovered(Vector2[] contour, Vector2[] simplified)
        {
            var clipper = new Clipper();
            clipper.AddPath(contour.Select(point => new IntPoint(point.x * 1000.0, point.y * 1000.0)).ToList(), PolyType.ptSubject, true);
            clipper.AddPath(simplified.Select(point => new IntPoint(point.x * 1000.0, point.y * 1000.0)).ToList(), PolyType.ptClip, true);
            var uncovered = new System.Collections.Generic.List<System.Collections.Generic.List<IntPoint>>();
            clipper.Execute(ClipType.ctDifference, uncovered, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
            Assert.IsFalse(uncovered.Any(path => Math.Abs(Clipper.Area(path)) > 1.0), "Simplification must not cut away the original contour.");
        }

        [Test]
        public void GeneratePreservesExistingPositionsAndAddsInteriorVertices()
        {
            var session = Quad();
            try
            {
                var positions = session.mesh.vertices.ToArray();
                session.mesh.vertexWeights[0].SetFromBoneWeight(new BoneWeight { boneIndex0 = 7, weight0 = 1f });
                session.GenerateInteriorVertices();
                Assert.AreEqual(6, session.mesh.vertexCount);
                foreach (var position in positions) CollectionAssert.Contains(session.mesh.vertices, position);
                foreach (var weight in session.mesh.vertexWeights) Assert.AreEqual(0f, weight.Sum());
                SceneMeshSource.Validate(session.mesh);
            }
            finally { Object.DestroyImmediate(session); }
        }

        [Test]
        public void PixelMappingHandlesPivotDownscaleRotationAndFlips()
        {
            var texture = new Texture2D(16, 16);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.25f, 0.75f), 8f);
            var go = new GameObject("Scene mesh mapping test");
            try
            {
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                go.transform.position = new Vector3(10, -4, 2);
                go.transform.rotation = Quaternion.Euler(0, 0, 90);
                go.transform.localScale = new Vector3(-2, 3, 1);
                renderer.flipX = true; renderer.flipY = true;
                var matrix = SceneMeshSession.PixelToWorld(renderer, new Rect(100, 200, 32, 32));
                Assert.That(Vector3.Distance(go.transform.position, matrix.MultiplyPoint(new Vector3(8, 24))), Is.LessThan(0.0001f));
                var localPoint = new Vector3(-1, -0.5f, 0);
                Assert.That(Vector3.Distance(go.transform.TransformPoint(localPoint), matrix.MultiplyPoint(new Vector3(24, 32))), Is.LessThan(0.0001f));
                var pixel = new Vector3(17, 21);
                Assert.That(Vector3.Distance(pixel, matrix.inverse.MultiplyPoint(matrix.MultiplyPoint(pixel))), Is.LessThan(0.0001f));
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
        }

        [Test]
        public void TracedWeightedSpriteKeepsEmptyAuthoringWeightsAndRenderableRuntimeWeights()
        {
            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/UniskinTraceWeightsTest.png");
            var texture = new Texture2D(32, 32);
            try
            {
                texture.SetPixels(Enumerable.Repeat(Color.white, 32 * 32).ToArray());
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
                var source = new SceneMeshSource(AssetDatabase.LoadAssetAtPath<Sprite>(path));
                source.provider.GetDataProvider<ISpriteBoneDataProvider>().SetBones(source.rect.spriteID, new[]
                {
                    new UnityEngine.U2D.SpriteBone { name = "Root", parentId = -1, rotation = Quaternion.identity, length = 8 },
                    new UnityEngine.U2D.SpriteBone { name = "Child", parentId = 0, rotation = Quaternion.identity, length = 8 }
                }.ToList());
                source.provider.Apply(); importer.SaveAndReimport();
                source = new SceneMeshSource(AssetDatabase.LoadAssetAtPath<Sprite>(path));
                var mesh = source.ReadMesh(AssetDatabase.LoadAssetAtPath<Sprite>(path));
                foreach (var weight in mesh.vertexWeights)
                    weight.SetFromBoneWeight(new BoneWeight { boneIndex0 = 1, weight0 = 1f });
                source.WriteMesh(mesh);
                source = new SceneMeshSource(AssetDatabase.LoadAssetAtPath<Sprite>(path));
                var controller = new SpriteMeshDataController { spriteMeshData = mesh };
                controller.OutlineFromAlpha(new SceneMeshOutlineGenerator(), source.textureProvider, 1f, 1);
                controller.Triangulate(new Triangulator());
                Assert.IsTrue(mesh.vertexWeights.All(weight => weight.Sum() == 0f));
                Assert.IsTrue(source.WriteMesh(mesh));

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                source = new SceneMeshSource(sprite);
                var loaded = source.ReadMesh(sprite);
                Assert.IsTrue(loaded.vertexWeights.All(weight => weight.Sum() == 0f), "Runtime fallback must not rebind authoring data.");
                Assert.AreEqual(2, sprite.GetBones().Length);
                foreach (var weight in sprite.GetVertexAttribute<BoneWeight>(UnityEngine.Rendering.VertexAttribute.BlendWeight))
                {
                    Assert.AreEqual(1f, weight.weight0);
                    Assert.AreEqual(0, weight.boneIndex0);
                    Assert.AreEqual(0f, weight.weight1 + weight.weight2 + weight.weight3);
                }
                Assert.IsFalse(source.WriteMesh(loaded), "The runtime fallback must not cause repeated saves.");
                foreach (var weight in loaded.vertexWeights)
                    weight.SetFromBoneWeight(new BoneWeight { boneIndex0 = 1, weight0 = 1f });
                Assert.IsTrue(source.WriteMesh(loaded));
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                Assert.IsTrue(sprite.GetVertexAttribute<BoneWeight>(UnityEngine.Rendering.VertexAttribute.BlendWeight)
                    .All(weight => weight.boneIndex0 == 1 && weight.weight0 == 1f), "Explicit weights must replace the runtime fallback.");
            }
            finally { AssetDatabase.DeleteAsset(path); Object.DestroyImmediate(texture); }
        }

        [Test]
        public void ImportedSpriteMeshRoundTripsThroughSkinningProvider()
        {
            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/UniskinMeshRoundTripTest.png");
            var texture = new Texture2D(32, 32);
            try
            {
                texture.SetPixels(Enumerable.Repeat(Color.white, 32 * 32).ToArray());
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                var source = new SceneMeshSource(sprite);
                Assert.IsFalse(source.hasAuthoredMesh, "Runtime sprite geometry must not count as an authored mesh.");
                var mesh = source.ReadMesh(sprite);
                Assert.IsTrue(source.WriteMesh(mesh), "First authoring must save even when it matches the runtime quad.");
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                source = new SceneMeshSource(sprite);
                Assert.IsTrue(source.hasAuthoredMesh);
                Assert.IsFalse(source.WriteMesh(source.ReadMesh(sprite)));
                var factory = new SpriteDataProviderFactories(); factory.Init();
                var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
                provider.InitSpriteEditorDataProvider();
                provider.GetDataProvider<ISpriteBoneDataProvider>().SetBones(source.rect.spriteID, new[]
                {
                    new UnityEngine.U2D.SpriteBone { name = "Root", parentId = -1, rotation = Quaternion.identity, length = 8 },
                    new UnityEngine.U2D.SpriteBone { name = "Child", parentId = 0, rotation = Quaternion.identity, length = 8 }
                }.ToList());
                provider.Apply(); importer.SaveAndReimport();
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                source = new SceneMeshSource(sprite);
                mesh = source.ReadMesh(sprite);
                mesh.vertices[0] = new Vector2(-3, -4);
                mesh.vertexWeights[0].SetFromBoneWeight(new BoneWeight { boneIndex0 = 1, weight0 = 1f });
                var vertices = mesh.vertices.ToArray();
                var indices = mesh.indices.ToArray();
                var edges = mesh.edges.ToArray();
                Assert.IsTrue(source.WriteMesh(mesh), "A real geometry/weight edit must be imported.");
                var reopened = new SceneMeshSource(AssetDatabase.LoadAssetAtPath<Sprite>(path));
                Assert.IsTrue(reopened.hasAuthoredMesh, "Saved provider geometry must be recognized by the Inspector.");
                var loaded = reopened.ReadMesh(AssetDatabase.LoadAssetAtPath<Sprite>(path));
                CollectionAssert.AreEqual(vertices, loaded.vertices);
                CollectionAssert.AreEqual(indices, loaded.indices);
                CollectionAssert.AreEqual(edges, loaded.edges);
                Assert.AreEqual(1, loaded.vertexWeights[0].ToBoneWeight(false).boneIndex0);
                Assert.AreEqual(1f, loaded.vertexWeights[0].Sum());
                Assert.AreEqual(2, reopened.provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(reopened.rect.spriteID).Count);
                Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(path).vertices.Length, Is.EqualTo(loaded.vertexCount));
                var savedMeta = File.ReadAllBytes(path + ".meta");
                var savedFingerprint = reopened.Fingerprint();
                Assert.IsFalse(reopened.WriteMesh(loaded), "An identical provider payload must skip Apply and reimport.");
                CollectionAssert.AreEqual(savedMeta, File.ReadAllBytes(path + ".meta"));
                Assert.AreEqual(savedFingerprint, reopened.Fingerprint());

                var staged = reopened.ReadMesh(AssetDatabase.LoadAssetAtPath<Sprite>(path));
                staged.indices[0] = (staged.indices[0] + 1) % staged.vertexCount;
                Assert.AreEqual(savedFingerprint, reopened.Fingerprint(), "Staged triangle edits must not mutate provider backing arrays.");

                // Unity's approximate Vector2 == would lose this legitimate small edit.
                loaded.vertices[0] += new Vector2(0.000002f, 0);
                Assert.IsTrue(reopened.WriteMesh(loaded));
                reopened = new SceneMeshSource(AssetDatabase.LoadAssetAtPath<Sprite>(path));
                CollectionAssert.AreEqual(loaded.vertices, reopened.ReadMesh(AssetDatabase.LoadAssetAtPath<Sprite>(path)).vertices);
                Assert.IsFalse(reopened.WriteMesh(loaded));

                loaded.vertexWeights[0].SetFromBoneWeight(new BoneWeight { boneIndex0 = 0, weight0 = 1f });
                Assert.IsTrue(reopened.WriteMesh(loaded), "Weight-only changes must be saved.");
                reopened = new SceneMeshSource(AssetDatabase.LoadAssetAtPath<Sprite>(path));
                Assert.AreEqual(0, reopened.ReadMesh(AssetDatabase.LoadAssetAtPath<Sprite>(path)).vertexWeights[0].ToBoneWeight(false).boneIndex0);

                Array.Reverse(loaded.indices);
                Assert.IsTrue(reopened.WriteMesh(loaded), "Triangle order must be saved even with unchanged vertices.");
                reopened = new SceneMeshSource(AssetDatabase.LoadAssetAtPath<Sprite>(path));
                CollectionAssert.AreEqual(loaded.indices, reopened.ReadMesh(AssetDatabase.LoadAssetAtPath<Sprite>(path)).indices);

                loaded.SetEdges(Array.Empty<Unity.Mathematics.int2>());
                Assert.IsTrue(reopened.WriteMesh(loaded), "Explicit edge changes must be saved.");
                reopened = new SceneMeshSource(AssetDatabase.LoadAssetAtPath<Sprite>(path));
                Assert.IsEmpty(reopened.ReadMesh(AssetDatabase.LoadAssetAtPath<Sprite>(path)).edges);
                Assert.IsFalse(reopened.WriteMesh(loaded));
            }
            finally { AssetDatabase.DeleteAsset(path); Object.DestroyImmediate(texture); }
        }

        [Test]
        public void DeformedSpriteSkinMappingMatchesRuntimeWithScaledSourceAndRendererFlips()
        {
            var session = Quad();
            var texture = new Texture2D(16, 16);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.3f, 0.6f), 10);
            var root = new GameObject("Transient mesh deformation test") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                root.SetActive(false);
                var renderer = root.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.flipX = renderer.flipY = true;
                root.transform.position = new Vector3(8, -4, 0);
                root.transform.rotation = Quaternion.Euler(0, 0, 32);
                root.transform.localScale = new Vector3(1.5f, 0.7f, 1);
                var bone = new GameObject("Transient test bone") { hideFlags = HideFlags.HideAndDontSave };
                bone.transform.SetParent(root.transform, false);
                bone.transform.localPosition = new Vector3(0.2f, -0.3f, 0);
                var bindPose = bone.transform.worldToLocalMatrix * root.transform.localToWorldMatrix;
                using (var bindPoses = new NativeArray<Matrix4x4>(new[] { bindPose }, Allocator.Temp))
                    sprite.SetBindPoses(bindPoses);
                sprite.SetBones(new[] { new SpriteBone { name = "Transient test bone", parentId = -1, rotation = Quaternion.identity, length = 1 } });
                var skin = root.AddComponent<SpriteSkin>();
                skin.SetRootBone(bone.transform);
                skin.SetBoneTransforms(new[] { bone.transform });
                session.mesh.bones.Add(new SpriteBoneData());
                foreach (var weight in session.mesh.vertexWeights)
                    weight.SetFromBoneWeight(new BoneWeight { boneIndex0 = 0, weight0 = 1 });
                session.savedMesh = JsonUtility.ToJson(session.mesh);
                bone.transform.localPosition += new Vector3(0.4f, 0.6f, 0);
                bone.transform.localRotation = Quaternion.Euler(0, 0, -43);
                var before = bone.transform.localToWorldMatrix;
                var displayed = SceneMeshDeformation.Create(renderer, session.mesh);
                Assert.IsNotNull(displayed);
                var draw = SceneMeshSession.PixelToWorld(renderer, session.mesh.frame);
                for (var i = 0; i < session.mesh.vertexCount; ++i)
                {
                    var sourceLocal = ((Vector3)(session.mesh.vertices[i] * 0.5f - sprite.pivot)) / sprite.pixelsPerUnit;
                    var expectedLocal = (root.transform.worldToLocalMatrix * before * bindPose).MultiplyPoint3x4(sourceLocal);
                    expectedLocal.x = -expectedLocal.x;
                    expectedLocal.y = -expectedLocal.y;
                    var expected = root.transform.TransformPoint(expectedLocal);
                    Assert.That(Vector3.Distance(expected, draw.MultiplyPoint3x4(displayed.mesh.vertices[i])), Is.LessThan(0.0001f));
                }
                Assert.IsFalse(displayed.Commit(session));
                Assert.IsFalse(session.dirty);
                Assert.AreEqual(before, bone.transform.localToWorldMatrix, "Preview must not reset scene bones.");
                skin.enabled = false;
                Assert.IsNull(SceneMeshDeformation.Create(renderer, session.mesh));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); Object.DestroyImmediate(session); }
        }

        [Test]
        public void DeformedPlaybackStaysCleanAndEditsSurviveFrameChangesAndUndo()
        {
            var session = Quad();
            try
            {
                foreach (var weight in session.mesh.vertexWeights)
                    weight.SetFromBoneWeight(new BoneWeight { boneIndex0 = 0, weight0 = 1f });
                session.savedMesh = JsonUtility.ToJson(session.mesh);
                var original = session.savedMesh;
                for (var frame = 0; frame < 20; ++frame)
                {
                    var pose = Matrix4x4.TRS(new Vector3(frame * 3, frame, 0), Quaternion.Euler(0, 0, frame * 7), Vector3.one);
                    var displayed = new SceneMeshDeformation(session.mesh, new[] { pose });
                    Assert.That(Vector2.Distance(displayed.mesh.vertices[2], pose.MultiplyPoint3x4(session.mesh.vertices[2])), Is.LessThan(0.0001f));
                    Assert.IsFalse(displayed.Commit(session));
                    Assert.IsFalse(session.dirty, "Sampling alone must not stage mesh changes.");
                }
                var editedPose = Matrix4x4.TRS(new Vector3(40, -12, 0), Quaternion.Euler(0, 0, 90), Vector3.one);
                var editing = new SceneMeshDeformation(session.mesh, new[] { editedPose });
                session.BeginUndoOperation("Edit Deformed Mesh");
                editing.mesh.vertices[0] += new Vector2(0, 4);
                Assert.IsTrue(editing.Commit(session));
                session.EndUndoOperation();
                Assert.That(Vector2.Distance(session.mesh.vertices[0], new Vector2(4, 0)), Is.LessThan(0.0001f));
                Assert.IsTrue(session.dirty);
                var nextPose = Matrix4x4.Translate(new Vector3(-50, 60, 0));
                var next = new SceneMeshDeformation(session.mesh, new[] { nextPose });
                Assert.That(Vector2.Distance(next.mesh.vertices[0], new Vector2(-46, 60)), Is.LessThan(0.0001f));
                Assert.IsFalse(next.Commit(session));
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(original, JsonUtility.ToJson(session.mesh));
                Assert.IsFalse(session.dirty);
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void DeformedMixedWeightsCreationAndDeletionRetainSourceVertices()
        {
            var session = Quad();
            try
            {
                foreach (var weight in session.mesh.vertexWeights)
                    weight.SetFromBoneWeight(new BoneWeight { boneIndex0 = 0, weight0 = 0.25f, boneIndex1 = 1, weight1 = 0.75f });
                var original = (Vector2[])session.mesh.vertices.Clone();
                var poses = new[] { Matrix4x4.Translate(new Vector3(8, 0, 0)), Matrix4x4.Translate(new Vector3(0, 12, 0)) };
                var displayed = new SceneMeshDeformation(session.mesh, poses);
                Assert.AreEqual(original[0] + new Vector2(2, 9), displayed.mesh.vertices[0]);
                displayed.mesh.vertices[1] += new Vector2(3, -2);
                // The shared controller interpolates weights for new triangle/edge vertices.
                displayed.mesh.AddVertex(new Vector2(18, 25), new BoneWeight { boneIndex0 = 0, weight0 = 0.25f, boneIndex1 = 1, weight1 = 0.75f });
                var converted = displayed.ToSourceMesh();
                Assert.AreEqual(new Vector2(16, 16), converted.vertices[4]);
                Assert.AreEqual(original[1] + new Vector2(3, -2), converted.vertices[1]);
                Assert.AreEqual(original[2], converted.vertices[2]);
                Assert.AreEqual(session.mesh.vertexWeights[2].ToBoneWeight(false), converted.vertexWeights[2].ToBoneWeight(false));
                displayed.mesh.RemoveVertex(0);
                converted = displayed.ToSourceMesh();
                Assert.AreEqual(original[2], converted.vertices[1], "Removal must preserve exact coordinates of surviving vertices.");
                Assert.AreEqual(original[3], converted.vertices[2]);
            }
            finally { Object.DestroyImmediate(session); }
        }

        [Test]
        public void DeformedCollapsedPoseRejectsEditsAndUnweightedVerticesStayInSourcePlane()
        {
            var session = Quad();
            try
            {
                session.mesh.vertexWeights[0].SetFromBoneWeight(new BoneWeight { boneIndex0 = 0, weight0 = 1 });
                session.savedMesh = JsonUtility.ToJson(session.mesh);
                var before = JsonUtility.ToJson(session.mesh);
                var displayed = new SceneMeshDeformation(session.mesh, new[] { Matrix4x4.Scale(new Vector3(0, 1, 1)) });
                Assert.IsFalse(displayed.Commit(session), "Collapsed playback alone must remain clean.");
                Assert.AreEqual(session.mesh.vertices[2], displayed.mesh.vertices[2]);
                displayed.mesh.vertices[0] += Vector2.one;
                Assert.Throws<InvalidOperationException>(() => displayed.Commit(session));
                Assert.AreEqual(before, JsonUtility.ToJson(session.mesh));
                Assert.IsFalse(session.dirty);
            }
            finally { Object.DestroyImmediate(session); }
        }

        [Test]
        public void WindowTemplateHasCommandsAndNativeControls()
        {
            var template = ResourceLoader.Load<VisualTreeAsset>("SkinningModule/SceneEditMeshWindow.uxml");
            Assert.IsNotNull(template);
            var root = template.CloneTree();
            foreach (var name in new[] { "modify", "create", "delete", "newMesh", "reset", "generate", "trace", "traceOK", "traceCancel", "copy", "paste", "pasteApply", "pasteCancel" })
                Assert.IsNotNull(root.Q<Button>(name), name);
            Assert.IsTrue(root.Q("pasteSettings").ClassListContains("mesh-hidden"));
            Assert.IsTrue(root.Q<Toggle>("pasteMirror").value);
            Assert.IsNull(root.Q("pasteOffset"));
            Assert.IsNull(root.Q("pasteScale"));
            Assert.IsNull(root.Q("pasteAngle"));
            Assert.AreEqual("Place the mesh and press Enter to confirm or Esc to cancel.", root.Q<Label>("pasteInstructions").text);
            Assert.IsNotNull(ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/Copy.png"));
            Assert.IsNotNull(ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/Paste.png"));
            Assert.IsNull(root.Q<Foldout>("traceSettings"));
            Assert.IsTrue(root.Q("traceSettings").ClassListContains("mesh-hidden"));
            Assert.IsNotNull(root.Q<Slider>("detail"));
            Assert.IsNotNull(root.Q<SliderInt>("alpha"));
            Assert.IsNotNull(root.Q<Button>("closeWindow"));
            Assert.IsNull(root.Q<Label>("spriteName"));
            Assert.IsNull(root.Q<Label>("status"));
            Assert.AreEqual("Edit Mesh", root.Q<Label>("windowTitle").text);
            Assert.IsNotNull(root.Q<Image>("windowIcon"));
            Assert.IsNotNull(ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/CreateMesh.png"));
            Assert.IsNotNull(root.Q<Toggle>("deformed"));
            Assert.IsFalse(root.Q<Toggle>("deformed").value);
            Assert.IsNotNull(root.Q<Toggle>("symmetry"));
            Assert.IsFalse(root.Q<Toggle>("symmetry").value);
            foreach (var name in new[] { "apply", "revert", "frame" }) Assert.IsNull(root.Q<Button>(name));
            var window = ScriptableObject.CreateInstance<SceneEditMeshWindow>();
            try
            {
                window.CreateGUI();
                Assert.AreSame(ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/CreateMesh.png"), window.titleContent.image);
                Assert.AreSame(window.titleContent.image, window.rootVisualElement.Q<Image>("windowIcon").image);
                Assert.IsFalse(window.rootVisualElement.Q<Toggle>("deformed").value);
                Assert.IsFalse(window.rootVisualElement.Q<Toggle>("symmetry").value);
            }
            finally { Object.DestroyImmediate(window); }
        }
    }
}
