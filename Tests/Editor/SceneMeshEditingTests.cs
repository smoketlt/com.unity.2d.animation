using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.U2D.Animation.ClipperLib;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Animation.Tests
{
    internal class SceneMeshEditingTests
    {
        sealed class DragGUI : IGUIWrapper
        {
            readonly Dictionary<int, int> m_Ids = new Dictionary<int, int>();
            float m_Distance;
            public int hot;
            public int nearest;
            public Vector2 mousePosition { get; private set; }
            public EventType eventType { get; private set; }
            public int mouseButton => 0;
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
            public bool IsMouseDown(int button) => eventType == EventType.MouseDown && button == 0;
            public bool IsMouseUp(int button) => eventType == EventType.MouseUp && button == 0;
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

        [TestCase(false)]
        [TestCase(true)]
        public void CreateEdgeDragKeepsPreviewActiveAndReleasesMouseCapture(bool scene)
        {
            var gui = new DragGUI();
            var selection = new IndexedSelection(); selection.Select(0, true);
            var view = new SpriteMeshView(gui) { useSceneHandles = scene, mode = SpriteMeshViewMode.CreateVertex,
                selection = selection, defaultControlID = 99, frame = new Rect(0, 0, 64, 64) };
            var source = new Vector2(8, 8);
            var target = new Vector2(48, 48);
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
                mesh.vertices[0] = new Vector2(3, 4);
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
        public void WindowTemplateHasCommandsAndNativeControls()
        {
            var template = ResourceLoader.Load<VisualTreeAsset>("SkinningModule/SceneEditMeshWindow.uxml");
            Assert.IsNotNull(template);
            var root = template.CloneTree();
            foreach (var name in new[] { "modify", "create", "delete", "newMesh", "reset", "generate", "trace", "traceOK", "traceCancel" })
                Assert.IsNotNull(root.Q<Button>(name), name);
            Assert.IsNull(root.Q<Foldout>("traceSettings"));
            Assert.IsTrue(root.Q("traceSettings").ClassListContains("mesh-hidden"));
            Assert.IsNotNull(root.Q<Slider>("detail"));
            Assert.IsNotNull(root.Q<SliderInt>("alpha"));
            Assert.IsNotNull(root.Q<Button>("closeWindow"));
            Assert.IsNull(root.Q<Label>("spriteName"));
            Assert.IsNull(root.Q<Label>("status"));
            Assert.AreEqual("Edit Mesh", root.Q<Label>("windowTitle").text);
            foreach (var name in new[] { "apply", "revert", "frame" }) Assert.IsNull(root.Q<Button>(name));
        }
    }
}
