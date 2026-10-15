using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using UnityEngine.UIElements;
using Unity.Mathematics;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Animation.Tests
{
    public sealed class UniskinClippingMaskAuthoringTests
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        readonly List<Object> m_Transient = new List<Object>();
        string m_Folder, m_PsbPath, m_ImagePath;
        Scene m_Scene;
        UniskinClippingMask m_Mask;
        UniskinRig m_Rig;
        byte[] m_PsbMeta, m_ImageMeta;
        static Assembly EditorAssembly => AppDomain.CurrentDomain.GetAssemblies().First(assembly => assembly.GetName().Name == "Unity.2D.Animation.Editor");
        static Type Type(string name) => EditorAssembly.GetType("UnityEditor.U2D.Animation." + name, true);
        static object Get(object owner, string name)
        {
            for (var type = owner.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty(name, Flags | BindingFlags.DeclaredOnly);
                if (property != null) return property.GetValue(owner);
                var field = type.GetField(name, Flags | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(owner);
            }
            throw new MissingMemberException(name);
        }
        static object Call(object owner, string name, params object[] args)
        {
            for (var type = owner.GetType(); type != null; type = type.BaseType)
            {
                var method = type.GetMethods(Flags | BindingFlags.DeclaredOnly).SingleOrDefault(item => item.Name == name && item.GetParameters().Length == args.Length);
                if (method != null) return method.Invoke(owner, args);
            }
            throw new MissingMethodException(name);
        }
        static object Utility(string type, string name, params object[] args) => Type(type).GetMethods(Flags).Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(null, args);

        [SetUp]
        public void Setup()
        {
            m_Folder = "Assets/UniskinClipChecks_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", Path.GetFileName(m_Folder));
            m_PsbPath = m_Folder + "/Character.psb"; m_ImagePath = m_Folder + "/Mask.png";
            using (var writer = new BinaryWriter(File.Create(m_PsbPath)))
            {
                writer.Write(new byte[] { 56, 66, 80, 83, 0, 2, 0, 0, 0, 0, 0, 0, 0, 4 });
                writer.Write(new byte[] { 0, 0, 0, 32, 0, 0, 0, 32, 0, 8, 0, 3 }); writer.Write(new byte[18]);
                foreach (var channel in new[] { (byte)200, (byte)100, (byte)50, (byte)255 }) for (var pixel = 0; pixel < 1024; ++pixel) writer.Write(channel);
            }
            AssetDatabase.ImportAsset(m_PsbPath, ImportAssetOptions.ForceSynchronousImport);
            var native = (ISpriteEditorDataProvider)Utility("UniskinRigUtility", "NativeProvider", m_PsbPath);
            var id = native.GetSpriteRects()[0].spriteID;
            var bones = new[]
            {
                new SpriteBone { name = "Spine_1", guid = Guid.NewGuid().ToString("N"), position = new Vector3(16, 8, 0), rotation = Quaternion.identity, length = 8, parentId = -1 },
                new SpriteBone { name = "Spine_2", guid = Guid.NewGuid().ToString("N"), position = new Vector3(0, 8, 0), rotation = Quaternion.identity, length = 8, parentId = 0 },
                new SpriteBone { name = "Body_3D", guid = Guid.NewGuid().ToString("N"), position = new Vector3(0, 6, 0), rotation = Quaternion.identity, length = 4, parentId = 1 }
            };
            native.GetDataProvider<ISpriteBoneDataProvider>().SetBones(id, bones.ToList());
            var cp = native.GetDataProvider<ICharacterDataProvider>();
            if (cp != null) { var character = cp.GetCharacterData(); character.bones = bones; for (var i = 0; i < character.parts.Length; ++i) { var part = character.parts[i]; part.bones = new[] { 0, 1, 2 }; character.parts[i] = part; } cp.SetCharacterData(character); }
            var mesh = native.GetDataProvider<ISpriteMeshDataProvider>();
            mesh.SetVertices(id, new[] { Vector2.zero, Vector2.right * 32, Vector2.up * 32, Vector2.one * 32 }.Select(position => new Vertex2DMetaData { position = position, boneWeight = new BoneWeight { weight0 = 1 } }).ToArray());
            mesh.SetIndices(id, new[] { 0, 1, 2, 2, 1, 3 }); mesh.SetEdges(id, Array.Empty<Vector2Int>());
            native.Apply(); AssetImporter.GetAtPath(m_PsbPath).SaveAndReimport();
            var texture = new Texture2D(32, 32); texture.SetPixels(Enumerable.Repeat(Color.white, 1024).ToArray()); texture.Apply(); File.WriteAllBytes(m_ImagePath, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(m_ImagePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(m_ImagePath); importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single; importer.spritePixelsPerUnit = 200; importer.SaveAndReimport();
            m_Scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Character"); SceneManager.MoveGameObjectToScene(root, m_Scene);
            var body = new GameObject("Body").AddComponent<SpriteRenderer>(); body.transform.SetParent(root.transform, false); body.sprite = AssetDatabase.LoadAllAssetsAtPath(m_PsbPath).OfType<Sprite>().First();
            body.sharedMaterial = Resources.Load<Material>("UniskinMask");
            m_Rig = (UniskinRig)Utility("UniskinRigUtility", "Create", body.sprite);
            var image = new GameObject("Mask source").AddComponent<SpriteRenderer>(); image.transform.SetParent(root.transform, false); image.sprite = AssetDatabase.LoadAllAssetsAtPath(m_ImagePath).OfType<Sprite>().First(); image.enabled = false;
            m_Mask = body.gameObject.AddComponent<UniskinClippingMask>(); m_Mask.character = root.GetComponent<UniskinRigBinding>();
            m_PsbMeta = File.ReadAllBytes(m_PsbPath + ".meta"); m_ImageMeta = File.ReadAllBytes(m_ImagePath + ".meta");
            Utility("UniskinClippingMaskUtility", "Create", m_Mask);
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var obj in m_Transient) if (obj != null) Object.DestroyImmediate(obj); m_Transient.Clear();
            if (m_Scene.IsValid()) EditorSceneManager.ClosePreviewScene(m_Scene);
            if (!string.IsNullOrEmpty(m_Folder) && m_Folder.StartsWith("Assets/UniskinClipChecks_", StringComparison.Ordinal)) AssetDatabase.DeleteAsset(m_Folder);
            Utility("UniskinRigUtility", "InvalidateIndex");
        }

        object Source() => Activator.CreateInstance(Type("SceneMeshSource"), new object[] { m_Mask.contourRenderer.sprite, true });
        object Weights(object source)
        {
            var session = ScriptableObject.CreateInstance(Type("SceneWeightSession")); m_Transient.Add(session); Call(session, "Load", m_Mask.contourRenderer.sprite, source); return session;
        }

        void BindGreyBones(object session, object source)
        {
            var context = Get(session, "context");
            var skeleton = Get(context, "skeleton");
            foreach (var bone in ((Array)Get(skeleton, "bones")).Cast<object>().Where(bone => ((Object)bone).name == "Spine_1" || ((Object)bone).name == "Spine_2")) Call(session, "Bind", bone);
            Call(session, "Auto");
            var mesh = Get(session, "mesh"); var positions = (Vector2[])Get(mesh, "vertices");
            var provider = (ISpriteEditorDataProvider)Get(source, "provider"); var rect = provider.GetSpriteRects()[0].rect;
            var sprite = m_Mask.contourRenderer.sprite;
            var pixelToLocal = Matrix4x4.Scale(new Vector3(sprite.rect.width / rect.width / sprite.pixelsPerUnit, sprite.rect.height / rect.height / sprite.pixelsPerUnit, 1)) *
                Matrix4x4.Translate(-sprite.pivot / sprite.rect.size * rect.size);
            var toWorld = m_Mask.contourRenderer.transform.localToWorldMatrix * pixelToLocal;
            var matrices = ((Array)Get(mesh, "bones")).Cast<object>().Select(bone =>
                Matrix4x4.Scale(Vector3.one * provider.pixelsPerUnit) * m_Mask.character.bones.Single(link => link.guid == (string)Get(bone, "guid")).transform.worldToLocalMatrix * toWorld).ToArray();
            Call(session, "UpdateBindings", (Vector2[])positions.Clone(), matrices); Call(session, "Save", source);
        }

        void DrawContour()
        {
            var source = Source(); var provider = (ISpriteEditorDataProvider)Get(source, "provider");
            var id = provider.GetSpriteRects()[0].spriteID;
            var mesh = provider.GetDataProvider<ISpriteMeshDataProvider>();
            mesh.SetVertices(id, new[] { new Vector2(2, 2), new Vector2(30, 4), new Vector2(16, 30) }
                .Select(position => new Vertex2DMetaData { position = position }).ToArray());
            mesh.SetIndices(id, new[] { 0, 1, 2 });
            mesh.SetEdges(id, new[] { new Vector2Int(0, 1), new Vector2Int(1, 2), new Vector2Int(2, 0) });
            provider.Apply();
        }

        [Test]
        public void BoneInspectorAcceptsMultipleBonesFromOneCharacterAndRejectsMixedObjects()
        {
            var bones = m_Mask.character.bones.Where(link => link.transform != null).Take(2).ToArray();
            Assert.AreEqual(2, bones.Length);
            var method = Type("SceneBoneInspector").GetMethod("TryGetSources", Flags);
            var args = new object[] { bones.Select(link => link.transform.gameObject).ToArray(), null, null };
            Assert.IsTrue((bool)method.Invoke(null, args));
            CollectionAssert.AreEquivalent(bones.Select(link => link.guid), (string[])args[2]);
            var unrelated = new GameObject("Unrelated"); m_Transient.Add(unrelated);
            SceneManager.MoveGameObjectToScene(unrelated, m_Scene);
            args[0] = new[] { bones[0].transform.gameObject, unrelated };
            Assert.IsFalse((bool)method.Invoke(null, args));
            args[0] = new[] { bones[0].transform.gameObject, m_Mask.gameObject };
            Assert.IsFalse((bool)method.Invoke(null, args));
        }

        [Test]
        public void SavedContourVertexUndoSurvivesMeshSessionDestructionAndWeightEntry()
        {
            DrawContour();
            var before = m_Mask.contour.authoringData;
            var body = m_Rig.authoringData;
            Undo.IncrementCurrentGroup();
            var session = ScriptableObject.CreateInstance(Type("SceneMeshSession"));
            m_Transient.Add(session);
            Call(session, "Load", m_Mask.contour.sprite, Source());
            Call(session, "CreateClippingContourVertex", new Vector2(16, 3));
            Call(session, "EndUndoOperation");
            Undo.IncrementCurrentGroup();
            Call(Source(), "WriteMesh", Get(session, "mesh"));
            var after = m_Mask.contour.authoringData;
            Assert.AreNotEqual(before, after);
            Object.DestroyImmediate(session);
            var weights = Weights(Source());
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo();
            Assert.AreEqual(before, m_Mask.contour.authoringData);
            Assert.AreEqual(3, m_Mask.contour.sprite.vertices.Length);
            Undo.PerformRedo();
            Assert.AreEqual(after, m_Mask.contour.authoringData);
            Assert.AreEqual(4, m_Mask.contour.sprite.vertices.Length);
            Object.DestroyImmediate((Object)weights);
            Undo.PerformUndo();
            Assert.AreEqual(before, m_Mask.contour.authoringData);
            Assert.AreEqual(body, m_Rig.authoringData);
        }

        [Test]
        public void NewContourIsEmptyAndLeavesBodyRenderingUntouched()
        {
            var source = Source(); var provider = (ISpriteEditorDataProvider)Get(source, "provider");
            var mesh = provider.GetDataProvider<ISpriteMeshDataProvider>(); var id = provider.GetSpriteRects()[0].spriteID;
            Assert.IsEmpty(mesh.GetVertices(id)); Assert.IsEmpty(mesh.GetIndices(id));
            Assert.IsFalse(m_Mask.contour.hasGeometry); Assert.IsFalse(m_Mask.contourRenderer.enabled);
            Assert.IsNull(m_Mask.GetComponent<UnityEngine.Rendering.SortingGroup>());
            Assert.AreSame(Resources.Load<Material>("UniskinMask"), m_Mask.GetComponent<SpriteRenderer>().sharedMaterial);
            Assert.AreEqual(0, Get(Call(source, "ReadMesh", m_Mask.contour.sprite), "vertexCount"));
            DrawContour(); Assert.IsTrue(m_Mask.contour.hasGeometry);
            Assert.AreEqual(3, m_Mask.contour.sprite.vertices.Length);
        }

        sealed class GizmoLayoutGUI : GUIWrapper, IGUIWrapper
        {
            EventType IGUIWrapper.eventType => EventType.Layout;
            bool IGUIWrapper.IsControlHot(int id) => id == 0;
            bool IGUIWrapper.IsMultiStepControlHot(int id) => id == 0;
        }

        sealed class EnabledGizmoToggle : IBoneGizmoToggle
        {
            public bool enableGizmos => true;
            public void OnGUI() { }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BoneIconsAfterWindowCloseUseOwnerRigInsteadOfStaleContour(bool contourFirst)
        {
            DrawContour();
            var contourSource = Source(); BindGreyBones(Weights(contourSource), contourSource);
            var body = m_Mask.GetComponent<SpriteRenderer>();
            var source = new SceneMeshSource(body.sprite);
            var window = ScriptableObject.CreateInstance<SceneEditBoneWindow>(); m_Transient.Add(window);
            var session = (SceneBoneSession)Get(window, "m_Session");
            session.Load(body.sprite, source, skeletonOnly: true);
            typeof(SceneEditBoneWindow).GetField("m_Renderer", Flags).SetValue(window, body);
            typeof(SceneEditBoneWindow).GetField("m_Source", Flags).SetValue(window, source);
            var bound = session.skeleton.bones.First(bone => bone.name == "Spine_1");
            var pin = session.skeleton.CreateBone(bound, bound.position, bound.position + Vector3.right, false, "Icon Pin");
            bound.iconId = "0010_Arrow@270"; bound.iconSize = 40;
            pin.iconId = "0023_Heart"; pin.iconSize = 36;
            var boundGuid = bound.guid; var pinGuid = pin.guid;
            session.Changed(true);
            window.ShowPopup();
            Assert.IsTrue(window.RequestClose(), "Ordinary close must save the icon edit.");
            var ownerData = new SceneMeshSource(body.sprite).userData;
            var contourData = ((SceneMeshSource)Source()).userData;
            Assert.AreEqual("0023_Heart", BoneIconUtility.Find(BoneIconUtility.Read(ownerData), pinGuid));
            Assert.AreEqual(BoneIconUtility.Default, BoneIconUtility.Find(BoneIconUtility.Read(contourData), pinGuid), "The contour retains its private metadata snapshot.");
            var root = m_Mask.character.transform;
            if (contourFirst)
            {
                m_Mask.contourRenderer.transform.SetParent(root, false);
                m_Mask.contourRenderer.transform.SetSiblingIndex(0);
            }
            var controller = new BoneGizmoController(new SkeletonView(new GizmoLayoutGUI()), new UnityEngineUndo(), new EnabledGizmoToggle());
            ((HashSet<Transform>)Get(controller, "m_SelectionRoots")).Add(root);
            Call(controller, "FindUnboundCharacterBones");
            var pinTransform = m_Mask.character.bones.Single(link => link.guid == pinGuid).transform;
            var unbound = (Dictionary<Transform, (float length, Color color, string icon, float size)>)Get(controller, "m_UnboundCharacterBones");
            Assert.AreEqual("0023_Heart", unbound[pinTransform].icon);
            Assert.AreEqual(36, unbound[pinTransform].size);
            var contourSkin = m_Mask.contourRenderer.GetComponent<SpriteSkin>();
            Assert.IsTrue(contourSkin.isValid);
            Call(controller, "PrepareBones", contourSkin);
            var boundTransform = m_Mask.character.bones.Single(link => link.guid == boundGuid).transform;
            Assert.AreEqual("0010_Arrow@270", ((Dictionary<Transform, string>)Get(controller, "m_BoneIcons"))[boundTransform]);
            Assert.AreEqual(40, ((Dictionary<Transform, float>)Get(controller, "m_BoneIconSizes"))[boundTransform]);
            Call(controller, "ClearSpriteBoneCache");
            Call(controller, "FindUnboundCharacterBones");
            Assert.AreEqual("0023_Heart", unbound[pinTransform].icon, "Cache rebuild must retain the saved choice after close/reload.");
            Assert.AreEqual(ownerData, new SceneMeshSource(body.sprite).userData);
            Assert.AreEqual(contourData, ((SceneMeshSource)Source()).userData);
            CollectionAssert.AreEqual(m_PsbMeta, File.ReadAllBytes(m_PsbPath + ".meta"));
        }

        [Test]
        public void ReaddingComponentReplacesOnlyItsOrphanedContour()
        {
            DrawContour();
            var oldContour = m_Mask.contour; var oldRenderer = m_Mask.contourRenderer;
            var body = m_Mask.GetComponent<SpriteRenderer>(); var bodyData = m_Rig.authoringData;
            Object.DestroyImmediate(m_Mask);
            Assert.IsNotNull(oldRenderer); Assert.IsFalse(oldRenderer.enabled);
            m_Mask = body.gameObject.AddComponent<UniskinClippingMask>();
            Utility("UniskinClippingMaskUtility", "Create", m_Mask);
            Assert.IsTrue(oldRenderer == null);
            Assert.AreNotSame(oldContour, m_Mask.contour);
            Assert.IsFalse(m_Mask.contour.hasGeometry);
            Assert.AreEqual(1, body.GetComponentsInChildren<SpriteRenderer>(true).Count(renderer => renderer != body));
            Assert.AreEqual(bodyData, m_Rig.authoringData);
            Assert.AreSame(oldContour, AssetDatabase.LoadAssetAtPath<UniskinClippingMaskData>(AssetDatabase.GetAssetPath(oldContour)));
        }

        [Test]
        public void DrawContourOpensEmptyPointAuthoringAndCancelKeepsBodyIntact()
        {
            Utility("SceneEditMeshWindow", "OpenForRenderer", m_Mask.contourRenderer, false);
            var window = Resources.FindObjectsOfTypeAll(Type("SceneEditMeshWindow")).Single();
            try
            {
                var session = Get(window, "m_Session");
                Assert.AreEqual(3, Get(session, "mode"));
                Assert.AreEqual(0, Get(Get(session, "mesh"), "vertexCount"));
                Assert.IsFalse(m_Mask.contourRenderer.enabled);
                Assert.IsNull(m_Mask.GetComponent<UnityEngine.Rendering.SortingGroup>());
                Assert.AreEqual("Edit Clipping Mask", ((EditorWindow)window).titleContent.text);
                Assert.AreEqual("Edit Clipping Mask", ((EditorWindow)window).rootVisualElement.Q<Label>("windowTitle").text);
                Assert.AreEqual("SceneEditClippingMaskWindow", window.GetType().Name);
                var ui = ((EditorWindow)window).rootVisualElement;
                foreach (var name in new[] { "trace", "generate", "copy", "paste" }) Assert.IsNull(ui.Q(name));
                Assert.AreSame(ui.Q("symmetry").parent, ui.Q("deformed").parent);
                Assert.AreSame(ui.Q("symmetry").parent, ui.Q("triangles").parent);
                Assert.AreSame(ui.Q("symmetry").parent, ui.Q("masked").parent);
            }
            finally { ((EditorWindow)window).DiscardChanges(); ((EditorWindow)window).Close(); }
            Assert.IsFalse(m_Mask.contour.hasGeometry);
            Assert.AreSame(Resources.Load<Material>("UniskinMask"), m_Mask.GetComponent<SpriteRenderer>().sharedMaterial);
        }

        [Test]
        public void ClosingDrawnContourSavesOnlyTheNewPolygonAndReenablesClipping()
        {
            var bodyData = m_Rig.authoringData;
            Utility("SceneEditMeshWindow", "OpenForRenderer", m_Mask.contourRenderer, false);
            var window = Resources.FindObjectsOfTypeAll(Type("SceneEditMeshWindow")).Single();
            try
            {
                var session = Get(window, "m_Session"); var mesh = Get(session, "mesh");
                Call(mesh, "AddVertex", new Vector2(2, 2), default(BoneWeight));
                Call(mesh, "AddVertex", new Vector2(30, 4), default(BoneWeight));
                Call(mesh, "AddVertex", new Vector2(16, 30), default(BoneWeight));
                Call(mesh, "SetEdges", new[] { new int2(0, 1), new int2(1, 2) });
                Assert.IsTrue((bool)Call(window, "RequestClose"));
                Assert.IsTrue(m_Mask.contour.hasGeometry); Assert.AreEqual(3, m_Mask.contour.sprite.vertices.Length);
                Assert.IsTrue(m_Mask.contourRenderer.enabled);
                Assert.AreEqual(bodyData, m_Rig.authoringData);
                CollectionAssert.AreEqual(m_PsbMeta, File.ReadAllBytes(m_PsbPath + ".meta"));
            }
            finally { if (window != null) { ((EditorWindow)window).DiscardChanges(); ((EditorWindow)window).Close(); } }
        }

        [Test]
        public void BindGreyBonesPersistsSeparateWeightsWithoutChangingImagesOrPsb()
        {
            DrawContour();
            var source = Source(); var weights = Weights(source); BindGreyBones(weights, source);
            var bones = m_Mask.contour.sprite.GetBones(); CollectionAssert.AreEquivalent(new[] { "Spine_1", "Spine_2" }, bones.Select(bone => bone.name));
            Assert.IsTrue((bool)Get(m_Mask.contourRenderer.GetComponent<SpriteSkin>(), "isValid"));
            CollectionAssert.AreEqual(m_PsbMeta, File.ReadAllBytes(m_PsbPath + ".meta")); CollectionAssert.AreEqual(m_ImageMeta, File.ReadAllBytes(m_ImagePath + ".meta"));
            Assert.AreEqual(3, m_Rig.characterBones.Length);
            var reload = Weights(Source()); Assert.AreEqual(2, Get(Get(reload, "mesh"), "boneCount"));
            var asset = AssetDatabase.LoadAssetAtPath<UniskinClippingMaskData>(AssetDatabase.GetAssetPath(m_Mask.contour));
            Assert.AreSame(m_Mask.contour.sprite, asset.sprite); Assert.AreEqual(2, asset.sprite.GetBones().Length);
        }

        [Test]
        public void BorrowedChildOffsetsRemainPixelsInTransformedContourPlanes()
        {
            var rect = ((ISpriteEditorDataProvider)Get(Source(), "provider")).GetSpriteRects()[0];
            foreach (var ppu in new[] { 100f, 200f })
            foreach (var angle in new[] { 0f, 37f })
            {
                m_Mask.contour.rigToContour = Matrix4x4.TRS(new Vector3(.3f, -.2f, 0), Quaternion.Euler(0, 0, angle), new Vector3(1.5f, .75f, 1));
                var borrowed = (SpriteBone[])Utility("UniskinClippingMaskUtility", "BorrowedBones", m_Mask.contour, ppu, rect);
                var source = m_Rig.characterBones;
                Matrix4x4 RigWorld(int i) => (source[i].parentId >= 0 ? RigWorld(source[i].parentId) : Matrix4x4.Translate(m_Rig.rootOffset)) *
                    Matrix4x4.TRS(source[i].position / m_Rig.pixelsPerUnit, source[i].rotation, Vector3.one);
                Matrix4x4 ContourWorld(int i) => (borrowed[i].parentId >= 0 ? ContourWorld(borrowed[i].parentId) : Matrix4x4.identity) *
                    Matrix4x4.TRS(borrowed[i].position, borrowed[i].rotation, Vector3.one);
                var toPixels = Matrix4x4.Translate(Vector2.Scale(rect.rect.size, rect.pivot)) * Matrix4x4.Scale(Vector3.one * ppu) * m_Mask.contour.rigToContour;
                for (var i = 0; i < source.Length; ++i)
                {
                    var expected = toPixels * RigWorld(i);
                    Assert.That(Vector3.Distance(expected.MultiplyPoint3x4(Vector3.zero), ContourWorld(i).MultiplyPoint3x4(Vector3.zero)), Is.LessThan(.0001f), source[i].name);
                    Assert.That(borrowed[i].length, Is.EqualTo(expected.MultiplyVector(Vector3.right * source[i].length / m_Rig.pixelsPerUnit).magnitude).Within(.0001f));
                }
            }
        }

        [Test]
        public void AutoControlPointsUsePixelOffsetsAfterBinding()
        {
            DrawContour(); var session = Weights(Source());
            foreach (var bone in ((Array)Get(Get(Get(session, "context"), "skeleton"), "bones")).Cast<object>().Where(bone => ((Object)bone).name != "Body_3D"))
                Call(session, "Bind", bone);
            var controller = Activator.CreateInstance(Type("SpriteMeshDataController"));
            controller.GetType().GetField("spriteMeshData", Flags).SetValue(controller, Get(session, "mesh"));
            var args = new object[] { null, null, null };
            controller.GetType().GetMethod("GetControlPoints", Flags).Invoke(controller, args);
            var points = (float2[])args[0]; var segments = (int2[])args[1];
            Assert.AreEqual(2, segments.Length);
            Assert.That(math.distance(points[segments[1].x] - points[segments[0].x], new float2(0, 8)), Is.LessThan(.0001f),
                "Auto must receive the full eight-pixel child offset, rather than 0.08 world units.");
            Assert.That(math.distance(points[segments[1].y] - points[segments[1].x], new float2(8, 0)), Is.LessThan(.0001f));
        }
        [Test]
        public void RepairBorrowedSkeletonPreservesWeightsBindingsAndBody()
        {
            DrawContour(); var source = Source(); BindGreyBones(Weights(source), source);
            var provider = (ISpriteEditorDataProvider)Get(Source(), "provider");
            var id = provider.GetSpriteRects()[0].spriteID;
            var vertices = JsonUtility.ToJson(new VertexBox { vertices = provider.GetDataProvider<ISpriteMeshDataProvider>().GetVertices(id) });
            var userData = Get(Get(provider, "data"), "userData");
            var body = m_Rig.authoringData;
            var data = Get(Get(Source(), "provider"), "data");
            var character = (CharacterData)Get(data, "character");
            for (var i = 0; i < character.bones.Length; ++i)
                if (character.bones[i].parentId >= 0) { var bone = character.bones[i]; bone.position /= provider.pixelsPerUnit; character.bones[i] = bone; }
            data.GetType().GetField("character", Flags).SetValue(data, character);
            m_Mask.contour.authoringData = (string)Call(data, "Serialize");
            var savedBroken = m_Mask.contour.authoringData;
            var reopened = (ISpriteEditorDataProvider)Get(Source(), "provider");
            Assert.AreEqual(savedBroken, m_Mask.contour.authoringData, "Opening repairs only the detached provider document.");
            Assert.That(reopened.GetDataProvider<ICharacterDataProvider>().GetCharacterData().bones[1].position.magnitude,
                Is.GreaterThan(1), "Contours first opened after reload must also use pixel offsets.");
            Utility("UniskinClippingMaskUtility", "Synchronize", m_Rig, false);
            var repaired = (ISpriteEditorDataProvider)Get(Source(), "provider");
            CollectionAssert.AreEqual(((SpriteBone[])Utility("UniskinClippingMaskUtility", "BorrowedBones", m_Mask.contour, provider.pixelsPerUnit, provider.GetSpriteRects()[0])).Select(bone => bone.position),
                repaired.GetDataProvider<ICharacterDataProvider>().GetCharacterData().bones.Select(bone => bone.position));
            Assert.AreEqual(vertices, JsonUtility.ToJson(new VertexBox { vertices = repaired.GetDataProvider<ISpriteMeshDataProvider>().GetVertices(id) }));
            Assert.AreEqual(userData, Get(Get(repaired, "data"), "userData"));
            Assert.AreEqual(body, m_Rig.authoringData);
        }

        [Serializable] sealed class VertexBox { public Vertex2DMetaData[] vertices; }

        [Test]
        public void SynchronizeRestoredEmptyContourDoesNotGenerateImageGeometry()
        {
            var provider = (ISpriteEditorDataProvider)Get(Source(), "provider");
            var data = Get(provider, "data");
            var character = (CharacterData)Get(data, "character");
            var bone = character.bones[1]; bone.position /= provider.pixelsPerUnit; character.bones[1] = bone;
            data.GetType().GetField("character", Flags).SetValue(data, character);
            m_Mask.contour.authoringData = (string)Call(data, "Serialize");
            var body = m_Rig.authoringData;
            Utility("UniskinRigUtility", "RefreshAfterUndo");
            var restored = (ISpriteEditorDataProvider)Get(Source(), "provider");
            var mesh = restored.GetDataProvider<ISpriteMeshDataProvider>();
            var id = restored.GetSpriteRects()[0].spriteID;
            Assert.IsEmpty(mesh.GetVertices(id)); Assert.IsEmpty(mesh.GetIndices(id)); Assert.IsEmpty(mesh.GetEdges(id));
            Assert.IsFalse(m_Mask.contour.hasGeometry);
            Assert.AreEqual(body, m_Rig.authoringData);
            Assert.That(restored.GetDataProvider<ICharacterDataProvider>().GetCharacterData().bones[1].position.magnitude, Is.GreaterThan(1));
        }

        [Test]
        public void CreateInsertsAWeightedBoundaryPointInsteadOfAnInteriorVertex()
        {
            DrawContour(); var source = Source(); BindGreyBones(Weights(source), source);
            var bodyData = m_Rig.authoringData;
            Utility("SceneEditMeshWindow", "OpenForRenderer", m_Mask.contourRenderer, false);
            var window = Resources.FindObjectsOfTypeAll(Type("SceneEditMeshWindow")).Single();
            try
            {
                Call(window, "ConfigureSymmetryEditing", new object[] { null });
                var controller = Get(window, "m_Controller");
                var create = (Action<Vector2, int>)Get(controller, "geometryVertexCreated");
                var point = new Vector2(16, 4);
                create(point, -1);
                var mesh = Get(Get(window, "m_Session"), "mesh");
                var positions = (Vector2[])Get(mesh, "vertices"); var index = Array.FindIndex(positions, p => (p - point).sqrMagnitude < .0001f);
                Assert.GreaterOrEqual(index, 0);
                var boundary = (int2[])Get(mesh, "outlineEdges");
                Assert.AreEqual(2, boundary.Count(edge => edge.x == index || edge.y == index));
                Assert.Contains(index, (int[])Get(mesh, "indices"));
                var weight = (BoneWeight)Utility("EditableBoneWeightUtility", "ToBoneWeight", ((Array)Get(mesh, "vertexWeights")).GetValue(index), true);
                Assert.That(weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3, Is.EqualTo(1f).Within(.0001f));
                Assert.IsTrue((bool)Call(window, "RequestClose"));
                Assert.AreEqual(4, m_Mask.contour.sprite.vertices.Length);
                Assert.AreEqual(2, m_Mask.contour.sprite.GetBones().Length);
                Assert.AreEqual(bodyData, m_Rig.authoringData);
            }
            finally { if (window != null) { ((EditorWindow)window).DiscardChanges(); ((EditorWindow)window).Close(); } }
        }

        [Test]
        public void MaskedPreviewFollowsStagedContourWithoutSavingIt()
        {
            DrawContour(); var saved = m_Mask.contour.authoringData;
            Utility("SceneEditMeshWindow", "OpenForRenderer", m_Mask.contourRenderer, false);
            var window = Resources.FindObjectsOfTypeAll(Type("SceneEditMeshWindow")).Single();
            try
            {
                Type("SceneEditMeshWindow").GetField("m_Masked", Flags).SetValue(window, true);
                Call(window, "UpdateClippingPreview");
                var preview = (SpriteRenderer)Get(window, "m_ClippingPreview");
                Assert.IsTrue(preview.enabled); Assert.AreEqual(3, preview.sprite.vertices.Length);
                var session = Get(window, "m_Session"); Call(session, "CreateClippingContourVertex", new Vector2(16, 4));
                Call(window, "UpdateClippingPreview"); Assert.AreEqual(4, preview.sprite.vertices.Length);
                Assert.AreEqual(saved, m_Mask.contour.authoringData); Assert.AreEqual(3, m_Mask.contour.sprite.vertices.Length);
                Type("SceneEditMeshWindow").GetField("m_Masked", Flags).SetValue(window, false);
                Call(window, "UpdateClippingPreview"); Assert.IsTrue(preview == null);
                Assert.IsNull(m_Mask.GetComponent<SortingGroup>());
            }
            finally { ((EditorWindow)window).DiscardChanges(); ((EditorWindow)window).Close(); }
            Assert.AreEqual(saved, m_Mask.contour.authoringData); Assert.IsTrue(m_Mask.contourRenderer.enabled);
        }

        [Test]
        public void MainSkeletonChangesRemapContourByGuidAndKeepRedBoneUnbound()
        {
            DrawContour();
            var source = Source(); BindGreyBones(Weights(source), source);
            var rigProvider = Activator.CreateInstance(Type("UniskinRigDataProvider"), Flags, null,
                new object[] { m_Rig, Utility("UniskinRigUtility", "NativeProvider", m_PsbPath) }, null);
            var provider = (ISpriteEditorDataProvider)rigProvider; var bp = provider.GetDataProvider<ISpriteBoneDataProvider>(); var id = provider.GetSpriteRects()[0].spriteID;
            var bones = bp.GetBones(id); bones[1] = Rename(bones[1], "Spine_2 renamed"); bp.SetBones(id, bones);
            var cp = provider.GetDataProvider<ICharacterDataProvider>(); if (cp != null) { var data = cp.GetCharacterData(); data.bones[1] = Rename(data.bones[1], "Spine_2 renamed"); cp.SetCharacterData(data); }
            provider.Apply();
            CollectionAssert.AreEquivalent(new[] { "Spine_1", "Spine_2 renamed" }, m_Mask.contour.sprite.GetBones().Select(bone => bone.name));
            Assert.AreEqual(2, m_Mask.contour.sprite.GetBones().Length);
            CollectionAssert.AreEqual(m_ImageMeta, File.ReadAllBytes(m_ImagePath + ".meta"));
            SpriteBone Rename(SpriteBone bone, string name) { bone.name = name; return bone; }
        }

        [Test]
        public void WeldReadsBodyWeightsInCharacterSpaceWithoutChangingBody()
        {
            DrawContour();
            m_Mask.contour.rigToContour = Matrix4x4.TRS(new Vector3(.07f, -.03f, 0), Quaternion.Euler(0, 0, 23), new Vector3(1.3f, .8f, 1));
            var source = Source(); var session = Weights(source);
            var skeleton = Get(Get(session, "context"), "skeleton");
            foreach (var bone in ((Array)Get(skeleton, "bones")).Cast<object>().Take(2)) Call(session, "Bind", bone);
            var donors = (Array)Get(session, "weldSources");
            Assert.AreEqual(1, donors.Length, "The body's same Sprite ID is a valid donor in its separate document.");
            var donor = donors.GetValue(0);
            var window = (EditorWindow)ScriptableObject.CreateInstance(Type("SceneEditWeightsWindow"));
            try
            {
                Object.DestroyImmediate((Object)Get(window, "m_Session"));
                Type("SceneEditWeightsWindow").GetField("m_Session", Flags).SetValue(window, session);
                Type("SceneEditWeightsWindow").GetField("m_Renderer", Flags).SetValue(window, m_Mask.contourRenderer);
                Type("SceneEditWeightsWindow").GetField("m_DiscardOnClose", Flags).SetValue(window, true);
                Type("SceneEditWeightsWindow").GetField("m_SettingsLoaded", Flags).SetValue(window, false);
                Assert.AreSame(donor, Call(window, "WeldSource", m_Mask.GetComponent<SpriteRenderer>()), "Scene picking resolves the body, not the contour with its reused ID.");
                Assert.IsNull(Call(window, "WeldSource", m_Mask.contourRenderer));
            }
            finally
            {
                Type("SceneEditWeightsWindow").GetField("m_Session", Flags).SetValue(window, null);
                Object.DestroyImmediate(window);
            }
            var donorMesh = Utility("SpriteCacheExtensions", "GetMesh", donor);
            var targetMesh = Get(session, "mesh");
            var targetBones = ((Array)Get(targetMesh, "bones")).Cast<object>().ToArray();
            var donorBones = ((Array)Get(donorMesh, "bones")).Cast<object>().ToArray();
            var map = targetBones.Select(bone => Array.FindIndex(donorBones, other => (string)Get(other, "guid") == (string)Get(bone, "guid"))).ToArray();
            foreach (var weight in (Array)Get(donorMesh, "vertexWeights"))
            { Call(weight, "Clear"); Call(weight, "AddChannel", map[0], .25f, true); Call(weight, "AddChannel", map[1], .75f, true); }
            var bodyData = m_Rig.authoringData;
            var sourceMatrix = (Matrix4x4)Get(Utility("SpriteCacheExtensions", "GetCharacterPart", donor), "localToWorldMatrix");
            var targetMatrix = (Matrix4x4)Get(session, "m_WeldTargetMatrix");
            // Independent physical-plane conversion must agree with the cached conversion.
            var provider = (ISpriteEditorDataProvider)Get(source, "provider");
            var rect = provider.GetSpriteRects()[0];
            var testPixel = new Vector3(12, 9, 0);
            var physical = m_Mask.contour.rigToContour.inverse.MultiplyPoint3x4((testPixel - (Vector3)Vector2.Scale(rect.rect.size, rect.pivot)) / provider.pixelsPerUnit);
            Assert.Less(Vector3.Distance((physical - (Vector3)m_Rig.rootOffset) * m_Rig.pixelsPerUnit, targetMatrix.MultiplyPoint3x4(testPixel)), .0001f);
            var donorPositions = (Vector2[])Get(donorMesh, "vertices");
            var inside = (donorPositions[0] + donorPositions[1] + donorPositions[2]) / 3;
            var outside = new Vector2(1000, 1000);
            var positions = new[] { inside, outside, inside }.Select(point => (Vector2)targetMatrix.inverse.MultiplyPoint3x4(sourceMatrix.MultiplyPoint3x4(point))).ToArray();
            Call(targetMesh, "SetVertices", positions, Get(targetMesh, "vertexWeights"));
            var vertices = Get(session, "vertices");
            vertices.GetType().GetProperty("elements", Flags).SetValue(vertices, new[] { 0, 1 });
            Call(session, "Weld", donor, true);
            var values0 = (float[])Call(session, "Values", 0);
            Assert.That(values0[0], Is.EqualTo(.25f).Within(.0001f)); Assert.That(values0[1], Is.EqualTo(.75f).Within(.0001f));
            Assert.That(((float[])Call(session, "Values", 1))[0], Is.EqualTo(1).Within(.0001f), "Overlapping skips outside vertices.");
            Assert.That(((float[])Call(session, "Values", 2))[0], Is.EqualTo(1).Within(.0001f), "Selection excludes vertex 2.");
            Call(session, "Weld", donor, false);
            Assert.That(((float[])Call(session, "Values", 1))[1], Is.EqualTo(.75f).Within(.0001f), "All projects outside vertices.");
            Call(session, "ToggleLock", targetBones[0]);
            foreach (var weight in (Array)Get(donorMesh, "vertexWeights"))
            { Call(weight, "Clear"); Call(weight, "AddChannel", map[0], .8f, true); Call(weight, "AddChannel", map[1], .2f, true); }
            Call(session, "Weld", donor, false);
            Assert.That(((float[])Call(session, "Values", 0))[0], Is.EqualTo(.25f).Within(.0001f), "Locked weight retained.");
            Call(session, "Save", source);
            Assert.AreEqual(bodyData, m_Rig.authoringData, "Contour Weld never saves donor weights to the body rig.");
            Assert.AreEqual(2, Get(Get(Weights(Source()), "mesh"), "boneCount"));
            CollectionAssert.AreEqual(m_PsbMeta, File.ReadAllBytes(m_PsbPath + ".meta"));
            CollectionAssert.AreEqual(m_ImageMeta, File.ReadAllBytes(m_ImagePath + ".meta"));
        }

        public static string RunChecks()
        {
            var results = new List<string>();
            foreach (var method in typeof(UniskinClippingMaskAuthoringTests).GetMethods().Where(method => method.GetCustomAttribute<TestAttribute>() != null))
            {
                var fixture = new UniskinClippingMaskAuthoringTests();
                try { fixture.Setup(); method.Invoke(fixture, null); results.Add("PASS " + method.Name); }
                catch (Exception ex) { results.Add("FAIL " + method.Name + ": " + (ex.InnerException ?? ex)); }
                finally { fixture.Cleanup(); }
            }
            if (results.Any(result => result.StartsWith("FAIL"))) throw new InvalidOperationException(string.Join("\n", results));
            return string.Join("\n", results);
        }
    }
}
