using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using UnityEngine.TestTools;
using UnityEditor.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Animation.Tests
{
    internal sealed class UniskinRigTests
    {
        string m_Folder, m_Path;
        UniskinRig m_Rig;
        GameObject m_Root;
        SpriteRenderer m_Renderer;
        readonly List<ScriptableObject> m_Sessions = new List<ScriptableObject>();

        [SetUp]
        public void Setup()
        {
            m_Folder = "Assets/UniskinRigTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(m_Folder));
            m_Path = m_Folder + "/Character.psb";
            WritePsb(m_Path);
            AssetDatabase.ImportAsset(m_Path, ImportAssetOptions.ForceSynchronousImport);
            if (UniskinRigUtility.NativeProvider(m_Path) == null) Assert.Fail("PSB Importer is required.");
            var provider = UniskinRigUtility.NativeProvider(m_Path);
            var id = provider.GetSpriteRects().First().spriteID;
            var mesh = provider.GetDataProvider<ISpriteMeshDataProvider>();
            mesh.SetVertices(id, new[] { Vertex(0, 0), Vertex(32, 0), Vertex(0, 32), Vertex(32, 32) });
            mesh.SetIndices(id, new[] { 0, 1, 2, 2, 1, 3 });
            mesh.SetEdges(id, new[] { new Vector2Int(0, 1), new Vector2Int(1, 3), new Vector2Int(3, 2), new Vector2Int(2, 0) });
            provider.Apply(); AssetImporter.GetAtPath(m_Path).SaveAndReimport();
            m_Root = new GameObject("Rig Test Character");
            m_Renderer = m_Root.AddComponent<SpriteRenderer>();
            m_Renderer.sprite = SourceSprite();
            m_Rig = UniskinRigUtility.Create(m_Renderer.sprite);
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var session in m_Sessions) if (session != null) Object.DestroyImmediate(session);
            m_Sessions.Clear();
            if (m_Root != null) Object.DestroyImmediate(m_Root);
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(m_Folder);
            UniskinRigUtility.InvalidateIndex();
        }

        Sprite SourceSprite() => AssetDatabase.LoadAllAssetsAtPath(m_Path).OfType<Sprite>().First();
        SceneMeshSource Source() => new SceneMeshSource(m_Renderer.sprite);
        T Session<T>() where T : ScriptableObject
        {
            var session = ScriptableObject.CreateInstance<T>(); m_Sessions.Add(session); return session;
        }
        static Vertex2DMetaData Vertex(float x, float y) => new Vertex2DMetaData { position = new Vector2(x, y) };

        [Test]
        public void MeshSavePersistsNativeSubassetWithoutChangingPsbOrItsMetadata()
        {
            var image = File.ReadAllBytes(m_Path);
            var metadata = File.ReadAllBytes(m_Path + ".meta");
            var source = Source();
            Assert.AreSame(m_Rig, source.rig);
            Assert.AreSame(m_Rig.sprites[0].sprite, m_Renderer.sprite);
            var mesh = source.ReadMesh(m_Renderer.sprite);
            mesh.vertices[0] = new Vector2(2.25f, 3.5f);
            Assert.IsTrue(source.WriteMesh(mesh));
            Assert.IsTrue(m_Rig.hasUnbakedChanges);
            CollectionAssert.AreEqual(image, File.ReadAllBytes(m_Path));
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(m_Path + ".meta"));
            var rigPath = AssetDatabase.GetAssetPath(m_Rig);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m_Rig.sprites[0].sprite, out string guid, out long localId);
            var expectedVertices = m_Renderer.sprite.vertices;
            AssetDatabase.ImportAsset(rigPath, ImportAssetOptions.ForceSynchronousImport);
            var reloaded = AssetDatabase.LoadAssetAtPath<UniskinRig>(rigPath);
            CollectionAssert.AreEqual(expectedVertices, reloaded.sprites[0].sprite.vertices);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reloaded.sprites[0].sprite, out string reloadGuid, out long reloadId);
            Assert.AreEqual(guid, reloadGuid); Assert.AreEqual(localId, reloadId);
            Assert.AreEqual(new Vector2(2.25f, 3.5f), Source().meshProvider.GetVertices(Source().rect.spriteID)[0].position);
        }

        [Test]
        public void BoneThenWeightSaveUpdatesSceneBindingsAndDoesNotImportPsb()
        {
            var metadata = File.ReadAllBytes(m_Path + ".meta");
            AddBones();
            var source = Source();
            var weights = Session<SceneWeightSession>();
            weights.Load(m_Renderer.sprite, source);
            var bone = weights.context.skeleton.bones.Single(item => item.name == "Child");
            if (!weights.mesh.bones.Contains(bone)) weights.Bind(bone);
            var index = Array.IndexOf(weights.mesh.bones, bone);
            weights.SetRow(index, .75f);
            weights.Save(source);
            var native = m_Renderer.sprite;
            var bones = native.GetBones();
            var childIndex = Array.FindIndex(bones, item => item.name == "Child");
            Assert.GreaterOrEqual(childIndex, 0);
            Assert.AreEqual(bones.Length, native.GetBindPoses().Length);
            var skin = m_Renderer.GetComponent<SpriteSkin>();
            Assert.NotNull(skin);
            Assert.AreEqual(bones.Length, skin.boneTransforms.Length);
            Assert.AreEqual("Child", skin.boneTransforms[childIndex].name);
            var weight = native.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight)[0];
            var value = Enumerable.Range(0, 4).Where(channel => weight.GetBoneIndex(channel) == childIndex).Sum(channel => weight.GetWeight(channel));
            Assert.That(value, Is.EqualTo(.75f).Within(.00001f));
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(m_Path + ".meta"));
            Assert.IsTrue(m_Rig.hasUnbakedChanges);
        }

        void AddBones()
        {
            var source = Source();
            var bones = Session<SceneBoneSession>();
            bones.Load(m_Renderer.sprite, source);
            bones.CaptureScenePose(m_Renderer, source.rect.rect);
            using (bones.cache.UndoScope("Create Rig Test Bones"))
            {
                var root = bones.skeleton.CreateBone(null, new Vector3(8, 8, 0), new Vector3(16, 8, 0), false, "Root");
                bones.skeleton.CreateBone(root, new Vector3(16, 8, 0), new Vector3(24, 8, 0), false, "Child");
                bones.Changed(true);
            }
            bones.Save(source);
        }

        [Test]
        public void ExplicitBakeAndReturnToPsbRoundTripAllAuthoringAndNativeData()
        {
            AddBones();
            var image = File.ReadAllBytes(m_Path);
            var expected = m_Renderer.sprite.vertices;
            var expectedBones = m_Renderer.sprite.GetBones();
            Assert.IsTrue(UniskinRigUtility.Bake(m_Rig));
            Assert.IsFalse(m_Rig.hasUnbakedChanges);
            Assert.AreEqual(m_Rig.authoringData, UniskinRigData.Read(UniskinRigUtility.NativeProvider(m_Path), AssetImporter.GetAtPath(m_Path).userData).Serialize());
            CollectionAssert.AreEqual(expected, SourceSprite().vertices);
            CollectionAssert.AreEqual(expectedBones, SourceSprite().GetBones());
            CollectionAssert.AreEqual(image, File.ReadAllBytes(m_Path));
            UniskinRigUtility.UsePsb(m_Rig);
            Assert.AreSame(SourceSprite(), m_Renderer.sprite);
            Assert.IsNull(m_Root.GetComponent<UniskinRigBinding>());
            Assert.IsNull(Source().rig);
            Assert.AreEqual(expectedBones.Length, m_Renderer.GetComponent<SpriteSkin>().boneTransforms.Length);
        }

        [Test]
        public void AutoBakeUsesTheActiveSkinningEditorProviderAndSkipsSynchronizedImports()
        {
            var mesh = Source().ReadMesh(m_Renderer.sprite);
            mesh.vertices[0] = new Vector2(3, 4);
            Source().WriteMesh(mesh);
            var provider = UniskinRigUtility.NativeProvider(m_Path);
            UniskinRigUtility.PrepareSkinningEditor(provider);
            Assert.IsFalse(m_Rig.hasUnbakedChanges);
            var id = provider.GetSpriteRects().First().spriteID;
            Assert.AreEqual(new Vector2(3, 4), provider.GetDataProvider<ISpriteMeshDataProvider>().GetVertices(id)[0].position);
            var metadata = File.ReadAllBytes(m_Path + ".meta");
            UniskinRigUtility.PrepareSkinningEditor(provider);
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(m_Path + ".meta"));
        }

        [Test]
        public void SkinningModuleEntryBakesNativeProviderBeforeCreatingItsMeshCache()
        {
            var mesh = Source().ReadMesh(m_Renderer.sprite);
            mesh.vertices[0] = new Vector2(3, 4);
            Source().WriteMesh(mesh);
            var host = new SpriteEditorHost(UniskinRigUtility.NativeProvider(m_Path));
            var module = new SkinningModule();
            typeof(SpriteEditorModuleBase).GetProperty("spriteEditor").SetValue(module, host);
            var activated = false;
            try
            {
                // Exercise the actual module lifecycle and native PSB provider through
                // the public host contract. Batch window creation also initializes
                // unrelated Sprite Frame GUI modes, which require an OnGUI context.
                module.OnModuleActivate();
                activated = true;
                Assert.IsFalse(m_Rig.hasUnbakedChanges);
                var cached = module.skinningCache.GetSprites().Single(sprite => sprite.id == Source().rect.spriteID.ToString()).GetMesh();
                Assert.AreEqual(new Vector2(3, 4), cached.vertices[0]);
            }
            finally
            {
                if (activated) module.OnModuleDeactivate();
            }
        }

        sealed class SpriteEditorHost : ISpriteEditor
        {
            readonly ISpriteEditorDataProvider m_Provider;
            readonly VisualElement m_Main = new VisualElement();
            readonly VisualElement m_Toolbar = new VisualElement();
            internal SpriteEditorHost(ISpriteEditorDataProvider provider)
            { m_Provider = provider; selectedSpriteRect = provider.GetSpriteRects().First(); }
            public List<SpriteRect> spriteRects { private get; set; }
            public SpriteRect selectedSpriteRect { get; set; }
            public bool enableMouseMoveEvent { private get; set; }
            public bool editingDisabled => false;
            public Rect windowDimension => new Rect(0, 0, 800, 600);
            public T GetDataProvider<T>() where T : class => m_Provider as T ?? m_Provider.GetDataProvider<T>();
            public bool HandleSpriteSelection() => false;
            public void RequestRepaint() { }
            public void SetDataModified() { }
            public void ApplyOrRevertModification(bool apply) { }
            public VisualElement GetMainVisualContainer() => m_Main;
            public VisualElement GetToolbarRootElement() => m_Toolbar;
            public void SetPreviewTexture(Texture2D texture, int width, int height) { }
            public void ResetZoomAndScroll() { }
            public float zoomLevel { get; set; } = 1;
            public Vector2 scrollPosition { get; set; }
            public bool showAlpha { get; set; }
            public float mipLevel { get; set; }
        }

        [Test]
        public void ExternalSourceChangesBlockBakeWithoutOverwritingEitherVersion()
        {
            var mesh = Source().ReadMesh(m_Renderer.sprite);
            mesh.vertices[0] = new Vector2(3, 4);
            Source().WriteMesh(mesh);
            var rigData = m_Rig.authoringData;
            var importer = AssetImporter.GetAtPath(m_Path);
            importer.userData += "\nExternal change";
            importer.SaveAndReimport();
            var metadata = File.ReadAllBytes(m_Path + ".meta");
            Assert.Throws<InvalidOperationException>(() => UniskinRigUtility.Bake(m_Rig));
            Assert.AreEqual(rigData, m_Rig.authoringData);
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(m_Path + ".meta"));
        }

        [Test]
        public void UndoRigSaveRestoresGeneratedMeshAndPreservesSource()
        {
            var before = m_Rig.authoringData;
            var vertices = m_Renderer.sprite.vertices;
            var metadata = File.ReadAllBytes(m_Path + ".meta");
            Undo.IncrementCurrentGroup();
            var mesh = Source().ReadMesh(m_Renderer.sprite);
            mesh.vertices[0] = new Vector2(3, 4);
            Source().WriteMesh(mesh);
            Undo.FlushUndoRecordObjects();
            UniskinRigUtility.InvalidateIndex();
            Undo.PerformUndo();
            Assert.AreEqual(before, m_Rig.authoringData);
            CollectionAssert.AreEqual(vertices, m_Renderer.sprite.vertices);
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(m_Path + ".meta"));
        }

        // Real character fixtures are optional in package-only CI. Validation projects
        // supply a copy here; tests only modify their own copy inside m_Folder.
        void LoadCharacterFixture()
        {
            const string fixture = "Assets/UniskinRigCharacterFixture.psb";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(fixture) == null) Assert.Ignore("No multilayer PSB character fixture is available.");
            Object.DestroyImmediate(m_Root);
            m_Path = m_Folder + "/MultilayerCharacter.psb";
            Assert.IsTrue(AssetDatabase.CopyAsset(fixture, m_Path));
            m_Root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(m_Path));
            m_Renderer = m_Root.GetComponentsInChildren<SpriteRenderer>(true).First(renderer => renderer.sprite != null && renderer.sprite.GetBones().Length > 0);
            m_Rig = UniskinRigUtility.Create(m_Renderer.sprite);
        }

        [Test]
        public void MultilayerCharacterBakesNewWeightedBonesWithoutReplacingAuthoredTransforms()
        {
            LoadCharacterFixture();
            var metadata = File.ReadAllBytes(m_Path + ".meta");
            var before = JsonUtility.FromJson<UniskinRigData>(m_Rig.authoringData);
            var bones = Session<SceneBoneSession>();
            bones.Load(m_Renderer.sprite, Source(), skeletonOnly: true);
            bones.CaptureScenePose(m_Renderer, Source().rect.rect);
            var parent = bones.cache.selectedSprite.GetCharacterPart().bones.First(item => !item.IsConstraintParent());
            BoneCache added;
            using (bones.cache.UndoScope("Add multilayer rig bone"))
            {
                added = bones.skeleton.CreateBone(parent, parent.endPosition, parent.endPosition + Vector3.right * 8, false, "UniskinRigNewBone");
                bones.Changed(true);
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bones.Save(Source());
            watch.Stop();
            TestContext.WriteLine("Multilayer rig bone Save: " + watch.ElapsedMilliseconds + " ms");
            var binding = m_Root.GetComponent<UniskinRigBinding>();
            var authored = binding.bones.Single(link => link.guid == added.guid).transform;
            Assert.NotNull(authored);
            var weights = Session<SceneWeightSession>();
            weights.Load(m_Renderer.sprite, Source());
            var newBone = weights.context.skeleton.bones.Single(item => item.guid == added.guid);
            weights.Bind(newBone);
            weights.SetRow(Array.IndexOf(weights.mesh.bones, newBone), .25f);
            watch.Restart(); weights.Save(Source()); watch.Stop();
            TestContext.WriteLine("Multilayer rig weight Save: " + watch.ElapsedMilliseconds + " ms");
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(m_Path + ".meta"));
            var after = JsonUtility.FromJson<UniskinRigData>(m_Rig.authoringData);
            var selectedId = Source().rect.spriteID;
            foreach (var item in before.sprites.Where(item => item.rect.spriteID != selectedId))
            {
                var saved = after.sprites.Single(candidate => candidate.rect.spriteID == item.rect.spriteID);
                CollectionAssert.AreEqual(item.vertices, saved.vertices, item.rect.name + " geometry and weights");
                CollectionAssert.AreEqual(item.indices, saved.indices, item.rect.name);
                CollectionAssert.AreEqual(item.edges, saved.edges, item.rect.name);
                CollectionAssert.AreEqual(item.bones.Select(bone => bone.guid), saved.bones.Select(bone => bone.guid), item.rect.name);
                CollectionAssert.AreEqual(item.bones.Select(bone => bone.parentId), saved.bones.Select(bone => bone.parentId), item.rect.name);
                for (var i = 0; i < item.bones.Length; ++i)
                {
                    Assert.That(Vector3.Distance(item.bones[i].position, saved.bones[i].position), Is.LessThan(.001f));
                    Assert.That(Mathf.Abs(item.bones[i].length - saved.bones[i].length), Is.LessThan(.001f));
                }
            }
            watch.Restart(); Assert.IsTrue(UniskinRigUtility.Bake(m_Rig)); watch.Stop();
            TestContext.WriteLine("Multilayer PSB Bake: " + watch.ElapsedMilliseconds + " ms");
            Assert.AreSame(authored, binding.bones.Single(link => link.guid == added.guid).transform);
            Assert.AreEqual(1, m_Root.GetComponentsInChildren<Transform>(true).Count(item => item.name == "UniskinRigNewBone"));
            Assert.AreEqual(m_Rig.authoringData, UniskinRigData.Read(UniskinRigUtility.NativeProvider(m_Path), AssetImporter.GetAtPath(m_Path).userData).Serialize());
            var skin = m_Renderer.GetComponent<SpriteSkin>();
            var boneIndex = Array.FindIndex(m_Renderer.sprite.GetBones(), item => item.guid == added.guid);
            Assert.AreSame(authored, skin.boneTransforms[boneIndex]);
            UniskinRigUtility.UsePsb(m_Rig);
            Assert.AreSame(authored, skin.boneTransforms[boneIndex]);
            Assert.AreEqual(m_Path, AssetDatabase.GetAssetPath(m_Renderer.sprite));
        }

        [Test]
        public void NativeCharacterPoseReloadDoesNotApplyImportedBoneMovementTwice()
        {
            LoadCharacterFixture();
            var binding = m_Root.GetComponent<UniskinRigBinding>();
            var link = binding.bones.First(item => !item.created);
            var boneTransform = link.transform;
            var initial = boneTransform.localPosition;
            var source = new SceneMeshSource(m_Rig.Find(m_Renderer.sprite).source, useRig: false);
            var bones = Session<SceneBoneSession>();
            bones.Load(m_Rig.Find(m_Renderer.sprite).source, source, skeletonOnly: true);
            var bone = bones.skeleton.bones.Single(item => item.guid == link.guid);
            using (bones.cache.UndoScope("Native fixture bone movement"))
            {
                bone.position += Vector3.right * 4;
                bones.Changed();
            }
            bones.Save(source);
            UniskinRigUtility.ReloadFromPsb(m_Rig);
            Assert.AreSame(boneTransform, binding.bones.Single(item => item.guid == link.guid).transform);
            Assert.That(Vector3.Distance(initial + Vector3.right * (4 / source.provider.pixelsPerUnit), boneTransform.localPosition), Is.LessThan(.00001f));
            Assert.IsFalse(m_Rig.hasUnbakedChanges);
        }

        [Test]
        public void ClosingAllThreeSceneWindowsAutomaticallyPersistsRigEdits()
        {
            var metadata = File.ReadAllBytes(m_Path + ".meta");
            var meshWindow = ScriptableObject.CreateInstance<SceneEditMeshWindow>();
            try
            {
                var source = Source();
                SetWindowField(meshWindow, "m_Renderer", m_Renderer);
                SetWindowField(meshWindow, "m_Source", source);
                var session = GetWindowSession<SceneMeshSession>(meshWindow);
                session.Load(m_Renderer.sprite, source);
                session.mesh.vertices[0] = new Vector2(3, 4);
            }
            finally { Object.DestroyImmediate(meshWindow); }
            Assert.AreEqual(new Vector2(3, 4), Source().meshProvider.GetVertices(Source().rect.spriteID)[0].position);
            var boneWindow = ScriptableObject.CreateInstance<SceneEditBoneWindow>();
            try
            {
                var source = Source();
                SetWindowField(boneWindow, "m_Renderer", m_Renderer);
                SetWindowField(boneWindow, "m_Source", source);
                var session = GetWindowSession<SceneBoneSession>(boneWindow);
                session.Load(m_Renderer.sprite, source);
                session.CaptureScenePose(m_Renderer, source.rect.rect);
                using (session.cache.UndoScope("Create auto-close bones"))
                {
                    var root = session.skeleton.CreateBone(null, new Vector3(8, 8, 0), new Vector3(16, 8, 0), false, "CloseRoot");
                    session.skeleton.CreateBone(root, new Vector3(16, 8, 0), new Vector3(24, 8, 0), false, "CloseChild");
                    session.Changed(true);
                }
            }
            finally { Object.DestroyImmediate(boneWindow); }
            Assert.IsTrue(m_Renderer.sprite.GetBones().Any(bone => bone.name == "CloseChild"));
            var weightsWindow = ScriptableObject.CreateInstance<SceneEditWeightsWindow>();
            try
            {
                var source = Source();
                SetWindowField(weightsWindow, "m_Renderer", m_Renderer);
                SetWindowField(weightsWindow, "m_Source", source);
                var session = GetWindowSession<SceneWeightSession>(weightsWindow);
                session.Load(m_Renderer.sprite, source);
                var child = session.mesh.bones.Single(bone => bone.name == "CloseChild");
                session.SetRow(Array.IndexOf(session.mesh.bones, child), .75f);
            }
            finally { Object.DestroyImmediate(weightsWindow); }
            var childIndex = Array.FindIndex(m_Renderer.sprite.GetBones(), bone => bone.name == "CloseChild");
            var weight = m_Renderer.sprite.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight)[0];
            Assert.That(Enumerable.Range(0, 4).Where(channel => weight.GetBoneIndex(channel) == childIndex).Sum(channel => weight.GetWeight(channel)), Is.EqualTo(.75f).Within(.00001f));
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(m_Path + ".meta"));
        }

        static void SetWindowField(EditorWindow window, string name, object value) =>
            window.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value);
        static T GetWindowSession<T>(EditorWindow window) =>
            (T)window.GetType().GetField("m_Session", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);

        [Test]
        public void RemovingAnEarlierBonePreservesThePoseOfAnUnchangedParentRelationship()
        {
            var provider = (UniskinRigDataProvider)Source().provider;
            provider.data.sprites[0].bones = new[]
            {
                new SpriteBone { guid = "unused", name = "Unused", parentId = -1, position = new Vector3(4, 4, 0), rotation = Quaternion.identity, length = 8 },
                new SpriteBone { guid = "root", name = "Root", parentId = -1, position = new Vector3(8, 8, 0), rotation = Quaternion.identity, length = 8 },
                new SpriteBone { guid = "child", name = "Child", parentId = 1, position = new Vector3(8, 0, 0), rotation = Quaternion.identity, length = 8 }
            };
            UpdateCharacterBoneMap(provider);
            provider.Apply();
            var binding = m_Root.GetComponent<UniskinRigBinding>();
            var child = binding.bones.Single(link => link.guid == "child").transform;
            var parent = child.parent;
            child.localPosition += new Vector3(.12f, .23f, 0);
            child.localRotation = Quaternion.Euler(0, 0, 37);
            var position = child.localPosition;
            var rotation = child.localRotation;
            provider = (UniskinRigDataProvider)Source().provider;
            provider.data.sprites[0].bones = provider.data.sprites[0].bones.Skip(1).ToArray();
            provider.data.sprites[0].bones[1].parentId = 0;
            UpdateCharacterBoneMap(provider);
            provider.Apply();
            Assert.AreSame(child, binding.bones.Single(link => link.guid == "child").transform);
            Assert.AreSame(parent, child.parent);
            Assert.That(Vector3.Distance(position, child.localPosition), Is.LessThan(.00001f));
            Assert.That(Quaternion.Angle(rotation, child.localRotation), Is.LessThan(.0001f));
            Assert.IsFalse(m_Root.GetComponentsInChildren<Transform>(true).Any(transform => transform.name == "Unused"));
        }

        static void UpdateCharacterBoneMap(UniskinRigDataProvider provider)
        {
            if (!provider.data.hasCharacter) return;
            provider.data.character.bones = UniskinRigData.Clone(provider.data.sprites[0].bones);
            for (var i = 0; i < provider.data.character.parts.Length; ++i)
                provider.data.character.parts[i].bones = Enumerable.Range(0, provider.data.character.bones.Length).ToArray();
        }

        [UnityTest]
        public IEnumerator SavedRigPrefabLoadsLaterAddedBonesAndSkinInPlayMode()
        {
            var prefabPath = m_Folder + "/RigSnapshot.prefab";
            PrefabUtility.SaveAsPrefabAsset(m_Root, prefabPath);
            AddBones();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Object.DestroyImmediate(m_Root);
            m_Root = null;
            var oldOptions = EditorSettings.enterPlayModeOptions;
            var oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            yield return new EnterPlayMode(false);
            var instance = Object.Instantiate(prefab);
            yield return null;
            var binding = instance.GetComponent<UniskinRigBinding>();
            var renderer = instance.GetComponent<SpriteRenderer>();
            var skin = renderer.GetComponent<SpriteSkin>();
            var valid = binding.bones.Length == 2 && binding.bones.All(link => link.transform != null) && skin != null &&
                skin.isValid && skin.boneTransforms.Length == renderer.sprite.GetBones().Length && skin.boneTransforms.All(bone => bone != null);
            Object.Destroy(instance);
            yield return new ExitPlayMode();
            // Switching the asset back to PSB must also keep older saved rig prefab
            // snapshots usable: their binding creates the baked bones on load.
            UniskinRigUtility.UsePsb(m_Rig);
            yield return new EnterPlayMode(false);
            instance = Object.Instantiate(prefab);
            yield return null;
            renderer = instance.GetComponent<SpriteRenderer>();
            skin = renderer.GetComponent<SpriteSkin>();
            var validPsb = skin != null && skin.isValid && AssetDatabase.GetAssetPath(renderer.sprite) == m_Path &&
                skin.boneTransforms.Length == renderer.sprite.GetBones().Length && skin.boneTransforms.All(bone => bone != null);
            Object.Destroy(instance);
            yield return new ExitPlayMode();
            EditorSettings.enterPlayModeOptions = oldOptions;
            EditorSettings.enterPlayModeOptionsEnabled = oldEnabled;
            Assert.IsTrue(valid, "The older prefab snapshot must resolve later rig bones and create its SpriteSkin in a player.");
            Assert.IsTrue(validPsb, "The older rig prefab must also resolve the baked skeleton after switching to PSB.");
        }

        static void WritePsb(string path)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(new byte[] { 56, 66, 80, 83, 0, 2, 0, 0, 0, 0, 0, 0, 0, 4 });
                writer.Write(new byte[] { 0, 0, 0, 32, 0, 0, 0, 32, 0, 8, 0, 3 });
                writer.Write(new byte[18]);
                foreach (var channel in new[] { (byte)200, (byte)100, (byte)50, (byte)255 })
                    for (var pixel = 0; pixel < 1024; ++pixel) writer.Write(channel);
            }
        }
    }
}
