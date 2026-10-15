using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
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

        [TestCase(false)]
        [TestCase(true)]
        public void SourceCheckAcceptsDependencyDriftWithUnchangedNativeData(bool legacy)
        {
            var authored = m_Rig.authoringData;
            var baseline = m_Rig.sourceBaseline;
            var fileHash = m_Rig.sourceFileHash;
            if (legacy) m_Rig.sourceFileHash = null;
            m_Rig.sourceDependencyHash = "different-project-dependency-hash";
            Assert.DoesNotThrow(() => UniskinRigUtility.CheckSource(m_Rig));
            Assert.AreEqual(AssetDatabase.GetAssetDependencyHash(m_Path).ToString(), m_Rig.sourceDependencyHash);
            Assert.AreEqual(fileHash, m_Rig.sourceFileHash);
            Assert.AreEqual(authored, m_Rig.authoringData);
            Assert.AreEqual(baseline, m_Rig.sourceBaseline);
        }

        [Test]
        public void SourceCheckRejectsChangedImageEvenWithUnchangedNativeData()
        {
            m_Rig.sourceDependencyHash = "different-project-dependency-hash";
            m_Rig.sourceFileHash = "different-image-content";
            Assert.Throws<InvalidOperationException>(() => UniskinRigUtility.CheckSource(m_Rig));
            Assert.AreEqual("different-image-content", m_Rig.sourceFileHash);
            Assert.AreEqual("different-project-dependency-hash", m_Rig.sourceDependencyHash);
        }

        [Test]
        public void LegacySourceCheckRejectsChangedNativeData()
        {
            m_Rig.sourceFileHash = null;
            m_Rig.sourceDependencyHash = "different-project-dependency-hash";
            m_Rig.sourceBaseline = "different-native-data";
            Assert.Throws<InvalidOperationException>(() => UniskinRigUtility.CheckSource(m_Rig));
            Assert.IsNull(m_Rig.sourceFileHash);
        }

        [Test]
        public void BoneIconSurvivesRigSaveBakeAndNativeReload()
        {
            var source = Source();
            var session = Session<SceneBoneSession>();
            session.Load(m_Renderer.sprite, source);
            var bone = session.skeleton.CreateBone(null, Vector3.zero, Vector3.right * 10, false, "Icon Bone");
            bone.iconId = "0008_Direction@90";
            bone.iconSize = 40f;
            session.Changed(true);
            var guid = bone.guid;
            session.Save(source);
            var reloaded = Session<SceneBoneSession>();
            reloaded.Load(m_Renderer.sprite, Source());
            Assert.AreEqual("0008_Direction@90", reloaded.skeleton.bones.Single(item => item.guid == guid).iconId);
            Assert.AreEqual(40f, reloaded.skeleton.bones.Single(item => item.guid == guid).iconSize);
            UniskinRigUtility.Bake(m_Rig);
            var importer = AssetImporter.GetAtPath(m_Path);
            Assert.AreEqual("0008_Direction@90", BoneIconUtility.Find(BoneIconUtility.Read(importer.userData), guid));
            Assert.AreEqual(40f, BoneIconUtility.FindSize(BoneIconUtility.Read(importer.userData), guid));
        }

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

        [Test]
        public void RigBoneLookupUsesGuidAcrossHierarchyMismatchAndHonorsFiltersAndAmbiguity()
        {
            var root = new GameObject("GuidLookupTest");
            var rig = ScriptableObject.CreateInstance<UniskinRig>();
            try
            {
                rig.characterBones = new[]
                {
                    new SpriteBone { guid = "head", name = "Head", parentId = -1, rotation = Quaternion.identity },
                    new SpriteBone { guid = "ear", name = "Ear_Pin_L", parentId = 0, rotation = Quaternion.identity }
                };
                var head = new GameObject("Head").transform;
                head.SetParent(root.transform);
                var face = new GameObject("Face").transform;
                face.SetParent(root.transform);
                var ear = new GameObject("Ear_Pin_L").transform;
                ear.SetParent(face);
                var binding = root.AddComponent<UniskinRigBinding>();
                binding.rig = rig;
                binding.bones = new[]
                {
                    new UniskinRigBinding.BoneBinding { guid = "head", transform = head, sourcePose = rig.characterBones[0] },
                    new UniskinRigBinding.BoneBinding { guid = "ear", transform = ear, sourcePose = rig.characterBones[1] }
                };
                Assert.AreSame(ear, SceneBoneInspector.FindCharacterTransform(root.transform, rig.characterBones, 1));
                Assert.AreSame(ear, SceneBoneInspector.FindCharacterTransform(face, rig.characterBones, 1));
                Assert.IsNull(SceneBoneInspector.FindCharacterTransform(root.transform, rig.characterBones, 1, candidate => candidate != ear));
                // Bake reconciliation excludes the authored Transform and must still
                // be able to find a regenerated native duplicate by its source path.
                var native = new GameObject("Ear_Pin_L").transform;
                native.SetParent(head);
                Assert.AreSame(ear, SceneBoneInspector.FindCharacterTransform(root.transform, rig.characterBones, 1));
                Assert.AreSame(native, SceneBoneInspector.FindCharacterTransform(root.transform, rig.characterBones, 1, candidate => candidate != ear));
                rig.editingEnabled = false;
                Assert.AreSame(native, SceneBoneInspector.FindCharacterTransform(root.transform, rig.characterBones, 1));
                rig.editingEnabled = true;
                var other = new GameObject("OtherRig");
                other.transform.SetParent(root.transform);
                var otherEar = new GameObject("Ear_Pin_L").transform;
                otherEar.SetParent(other.transform);
                var otherBinding = other.AddComponent<UniskinRigBinding>();
                otherBinding.rig = rig;
                otherBinding.bones = new[] { new UniskinRigBinding.BoneBinding { guid = "ear", transform = otherEar, sourcePose = rig.characterBones[1] } };
                Assert.IsNull(SceneBoneInspector.FindCharacterTransform(root.transform, rig.characterBones, 1));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(rig); }
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

        SceneEditWeightsWindow BindingWindow(SceneWeightSession session)
        {
            var window = Session<SceneEditWeightsWindow>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(SceneEditWeightsWindow).GetField("m_Renderer", flags).SetValue(window, m_Renderer);
            typeof(SceneEditWeightsWindow).GetField("m_Source", flags).SetValue(window, Source());
            typeof(SceneEditWeightsWindow).GetField("m_Session", flags).SetValue(window, session);
            typeof(SceneEditWeightsWindow).GetMethod("RefreshLiveBoneBindings", flags).Invoke(window, null);
            return window;
        }

        static Vector3[] BindingWorldVertices(SceneEditWeightsWindow window) => (Vector3[])typeof(SceneEditWeightsWindow)
            .GetMethod("WorldVertices", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { true });

        static void UpdateBindingWindow(SceneEditWeightsWindow window) => typeof(SceneEditWeightsWindow)
            .GetMethod("UpdateBindings", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);

        static void AssertPoints(Vector3[] expected, Vector3[] actual)
        {
            Assert.AreEqual(expected.Length, actual.Length);
            for (var i = 0; i < expected.Length; ++i) Assert.That(Vector3.Distance(expected[i], actual[i]), Is.LessThan(.0001f), "Vertex " + i);
        }

        Vector3[] RuntimeBindingVertices()
        {
            var sprite = m_Renderer.sprite;
            var skin = m_Renderer.GetComponent<SpriteSkin>();
            var positions = sprite.GetVertexAttribute<Vector3>(VertexAttribute.Position).ToArray();
            var weights = sprite.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight).ToArray();
            var poses = sprite.GetBindPoses();
            var flip = m_Renderer.transform.localToWorldMatrix * Matrix4x4.Scale(new Vector3(m_Renderer.flipX ? -1 : 1, m_Renderer.flipY ? -1 : 1, 1)) * m_Renderer.transform.worldToLocalMatrix;
            return positions.Select((position, i) =>
            {
                var point = Vector3.zero;
                for (var c = 0; c < 4; ++c)
                    if (weights[i].GetWeight(c) > 0)
                    {
                        var bone = weights[i].GetBoneIndex(c);
                        point += skin.boneTransforms[bone].TransformPoint(poses[bone].MultiplyPoint3x4(position)) * weights[i].GetWeight(c);
                    }
                return flip.MultiplyPoint3x4(point);
            }).ToArray();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UpdateBindingsPreservesShapeTextureAndWeightsThroughSaveReopenAndBake(bool flipped)
        {
            AddBones();
            m_Renderer.transform.position = new Vector3(3, -2, 0);
            m_Renderer.transform.rotation = Quaternion.Euler(0, 0, 23);
            m_Renderer.transform.localScale = new Vector3(1.4f, .8f, 1);
            m_Renderer.flipX = flipped;
            var skin = m_Renderer.GetComponent<SpriteSkin>();
            skin.boneTransforms[1].localPosition += new Vector3(.12f, .07f, 0);
            skin.boneTransforms[1].localRotation = Quaternion.Euler(0, 0, 39);
            skin.boneTransforms[1].localScale = new Vector3(1.2f, .9f, 1);
            var skeleton = JsonUtility.ToJson(Source().provider.GetDataProvider<ICharacterDataProvider>().GetCharacterData());
            var metadata = File.ReadAllBytes(m_Path + ".meta");
            var uv = m_Renderer.sprite.uv;
            var weights = Session<SceneWeightSession>(); weights.Load(m_Renderer.sprite, Source());
            weights.SetRow(1, .35f);
            var oldWeights = weights.mesh.vertexWeights.Select(JsonUtility.ToJson).ToArray();
            var window = BindingWindow(weights);
            var before = BindingWorldVertices(window);
            UpdateBindingWindow(window);
            CollectionAssert.AreEqual(oldWeights, weights.mesh.vertexWeights.Select(JsonUtility.ToJson));
            AssertPoints(before, BindingWorldVertices(window));
            weights.SetRow(1, 1);
            AssertPoints(before, BindingWorldVertices(window));
            weights.Save(Source());
            AssertPoints(before, RuntimeBindingVertices());
            Assert.AreEqual(skeleton, JsonUtility.ToJson(Source().provider.GetDataProvider<ICharacterDataProvider>().GetCharacterData()));
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(m_Path + ".meta"));
            for (var i = 0; i < uv.Length; ++i) Assert.That(Vector2.Distance(uv[i], m_Renderer.sprite.uv[i]), Is.LessThan(.00001f));
            var reloaded = Session<SceneWeightSession>(); reloaded.Load(m_Renderer.sprite, Source());
            Assert.IsFalse(reloaded.dirty); Assert.NotNull(reloaded.bindingPose);
            var reopened = BindingWindow(reloaded);
            AssertPoints(before, BindingWorldVertices(reopened));
            reloaded.SetRow(0, 1);
            AssertPoints(before, BindingWorldVertices(reopened));
            reloaded.Save(Source());
            Object.DestroyImmediate(window); Object.DestroyImmediate(reopened);
            Assert.IsTrue(UniskinRigUtility.Bake(m_Rig));
            var native = SourceSprite();
            for (var i = 0; i < uv.Length; ++i) Assert.That(Vector2.Distance(uv[i], native.uv[i]), Is.LessThan(.00001f), "Baked UV " + i);
            for (var i = 0; i < native.GetBindPoses().Length; ++i)
                for (var c = 0; c < 16; ++c) Assert.That(native.GetBindPoses()[i][c], Is.EqualTo(m_Renderer.sprite.GetBindPoses()[i][c]).Within(.0001f));
            UniskinRigUtility.UsePsb(m_Rig);
            AssertPoints(before, RuntimeBindingVertices());
        }

        [Test]
        public void UpdateBindingsIsUndoableAndRepeatedUpdatesRetainTextureMapping()
        {
            AddBones();
            var skin = m_Renderer.GetComponent<SpriteSkin>();
            skin.boneTransforms[1].localRotation = Quaternion.Euler(0, 0, 30);
            var weights = Session<SceneWeightSession>(); weights.Load(m_Renderer.sprite, Source());
            var window = BindingWindow(weights);
            var original = (Vector2[])weights.mesh.vertices.Clone();
            var before = BindingWorldVertices(window);
            Undo.IncrementCurrentGroup(); UpdateBindingWindow(window); Undo.FlushUndoRecordObjects();
            Assert.IsTrue(weights.dirty);
            Undo.PerformUndo(); weights.context.RestoreAfterUndo();
            Assert.IsNull(weights.bindingPose); Assert.IsFalse(weights.dirty);
            CollectionAssert.AreEqual(original, weights.mesh.vertices);
            AssertPoints(before, BindingWorldVertices(window));
            Undo.PerformRedo(); weights.context.RestoreAfterUndo();
            Assert.NotNull(weights.bindingPose); AssertPoints(before, BindingWorldVertices(window));
            var texture = weights.bindingPose.texturePositions.ToArray();
            skin.boneTransforms[1].localPosition += Vector3.right * .1f;
            before = BindingWorldVertices(window);
            Undo.IncrementCurrentGroup(); UpdateBindingWindow(window);
            AssertPoints(before, BindingWorldVertices(window));
            CollectionAssert.AreEqual(texture, weights.bindingPose.texturePositions);
            weights.SetRow(0, 1); AssertPoints(before, BindingWorldVertices(window));
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
        public void InspectorKeepsThreeIconButtonsAndRefreshesModeAndBakeAvailability()
        {
            var controls = new UniskinRigInspectorControls(() => m_Renderer.sprite);
            var useRig = controls.Q<Button>("uniskinUseRig");
            var usePsb = controls.Q<Button>("uniskinUsePsb");
            var bake = controls.Q<Button>("uniskinBakeRig");
            Assert.AreEqual(3, controls.Query<Button>().ToList().Count);
            foreach (var button in new[] { useRig, usePsb, bake }) Assert.NotNull(button.Q<Image>().image);
            Assert.AreEqual("Use Uniskin rig", useRig.Q<Label>().text);
            Assert.AreEqual("Use PSB Importer", usePsb.Q<Label>().text);
            Assert.AreEqual("Bake to PSB Importer", bake.Q<Label>().text);
            Assert.AreSame(useRig.parent, usePsb.parent);
            Assert.AreSame(controls, bake.parent);
            controls.Refresh(m_Renderer.sprite, true);
            Assert.IsFalse(useRig.enabledSelf);
            Assert.IsTrue(usePsb.enabledSelf);
            Assert.IsFalse(bake.enabledSelf);
            var mesh = Source().ReadMesh(m_Renderer.sprite);
            mesh.vertices[0] = new Vector2(3, 4);
            Source().WriteMesh(mesh);
            controls.Refresh(m_Renderer.sprite, true);
            Assert.IsTrue(bake.enabledSelf);
            UniskinRigUtility.UsePsb(m_Rig);
            controls.Refresh(m_Renderer.sprite, true);
            Assert.IsTrue(useRig.enabledSelf);
            Assert.IsFalse(usePsb.enabledSelf);
            Assert.IsFalse(bake.enabledSelf);
            controls.Refresh(m_Renderer.sprite, false);
            Assert.IsFalse(useRig.enabledSelf);
            Assert.IsFalse(usePsb.enabledSelf);
            Assert.IsFalse(bake.enabledSelf);
        }

        [Test]
        public void SwitchingModesBakesRigThenCopiesCurrentPsbDataBackIntoRig()
        {
            var mesh = Source().ReadMesh(m_Renderer.sprite);
            mesh.vertices[0] = new Vector2(3, 4);
            Source().WriteMesh(mesh);
            UniskinRigUtility.UsePsb(m_Rig);
            Assert.IsFalse(m_Rig.editingEnabled);
            Assert.IsFalse(m_Rig.hasUnbakedChanges);
            Assert.AreEqual(new Vector2(3, 4), Source().meshProvider.GetVertices(Source().rect.spriteID)[0].position);
            var provider = UniskinRigUtility.NativeProvider(m_Path);
            var id = provider.GetSpriteRects().First().spriteID;
            var nativeMesh = provider.GetDataProvider<ISpriteMeshDataProvider>();
            var vertices = nativeMesh.GetVertices(id);
            vertices[0].position = new Vector2(7, 9);
            nativeMesh.SetVertices(id, vertices);
            provider.Apply();
            AssetImporter.GetAtPath(m_Path).SaveAndReimport();
            var metadata = File.ReadAllBytes(m_Path + ".meta");
            UniskinRigUtility.Enable(m_Rig);
            Assert.IsTrue(m_Rig.editingEnabled);
            Assert.IsFalse(m_Rig.hasUnbakedChanges);
            Assert.AreSame(m_Rig.sprites[0].sprite, m_Renderer.sprite);
            Assert.AreEqual(new Vector2(7, 9), Source().meshProvider.GetVertices(id)[0].position);
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(m_Path + ".meta"));
        }

        [Test]
        public void RigRootResolvesWithoutBonesAndRejectsAmbiguousPsbSources()
        {
            var container = new GameObject("Rig Inspector Root");
            m_Root.transform.SetParent(container.transform);
            try
            {
                Assert.IsTrue(UniskinRigInspectorControls.TryGetRootSource(container, out var renderer));
                Assert.AreSame(m_Renderer, renderer);
                Assert.IsFalse(UniskinRigInspectorControls.TryGetRootSource(m_Root, out _));
                var otherPath = m_Folder + "/Other.psb";
                WritePsb(otherPath);
                AssetDatabase.ImportAsset(otherPath, ImportAssetOptions.ForceSynchronousImport);
                var other = new GameObject("Other Source");
                other.transform.SetParent(container.transform);
                other.AddComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAllAssetsAtPath(otherPath).OfType<Sprite>().First();
                Assert.IsFalse(UniskinRigInspectorControls.TryGetRootSource(container, out _));
            }
            finally
            {
                m_Root.transform.SetParent(null);
                Object.DestroyImmediate(container);
            }
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

        [Test]
        public void MeshSaveAndUndoRestoreSourceAtlasCoordinatesForChangedVertices()
        {
            var source = m_Rig.sprites[0].source;
            var originalUV = source.uv;
            AssertTextureCoordinates(originalUV, m_Renderer.sprite.uv);
            var metadata = File.ReadAllBytes(m_Path + ".meta");
            var mesh = Source().ReadMesh(m_Renderer.sprite);
            mesh.vertices[0] = (mesh.vertices[0] + mesh.vertices[1]) * .5f;
            Undo.IncrementCurrentGroup();
            Source().WriteMesh(mesh);
            var expected = originalUV.ToArray();
            expected[0] = (originalUV[0] + originalUV[1]) * .5f;
            AssertTextureCoordinates(expected, m_Renderer.sprite.uv);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            AssertTextureCoordinates(originalUV, m_Renderer.sprite.uv);
            Undo.PerformRedo();
            AssertTextureCoordinates(expected, m_Renderer.sprite.uv);
            AssertTextureCoordinates(originalUV, source.uv);
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(m_Path + ".meta"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StagedWeightUndoRedoAndWindowClosePreserveEveryLayerTextureCoordinates(bool multilayer)
        {
            if (multilayer) LoadCharacterFixture(); else AddBones();
            var sprites = m_Rig.sprites.Select(entry => entry.sprite).ToArray();
            var coordinates = sprites.Select(sprite => sprite.uv).ToArray();
            Assert.IsTrue(coordinates.All(uv => uv.Any(point => point != Vector2.zero)));
            var metadata = File.ReadAllBytes(m_Path + ".meta");
            var authoring = m_Rig.authoringData;
            var renderers = m_Root.GetComponentsInChildren<SpriteRenderer>(true);
            var suppression = renderers.Select(renderer => renderer.forceRenderingOff).ToArray();
            var window = ScriptableObject.CreateInstance<SceneEditWeightsWindow>();
            try
            {
                var source = Source();
                SetWindowField(window, "m_Renderer", m_Renderer);
                SetWindowField(window, "m_Source", source);
                var session = GetWindowSession<SceneWeightSession>(window);
                session.Load(m_Renderer.sprite, source);
                var before = session.Values(0);
                session.vertices.elements = new[] { 0 };
                Undo.IncrementCurrentGroup();
                session.SetRow(0, before[0] > .5f ? .25f : .75f);
                var changed = session.Values(0);
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                CollectionAssert.AreEqual(before, session.Values(0));
                for (var i = 0; i < sprites.Length; ++i) AssertTextureCoordinates(coordinates[i], sprites[i].uv);
                Undo.PerformRedo();
                CollectionAssert.AreEqual(changed, session.Values(0));
                for (var i = 0; i < sprites.Length; ++i) AssertTextureCoordinates(coordinates[i], sprites[i].uv);
                Undo.PerformUndo();
                Assert.IsFalse(session.dirty);
            }
            finally { Object.DestroyImmediate(window); }
            Assert.AreEqual(authoring, m_Rig.authoringData);
            for (var i = 0; i < sprites.Length; ++i) AssertTextureCoordinates(coordinates[i], sprites[i].uv);
            CollectionAssert.AreEqual(suppression, renderers.Select(renderer => renderer.forceRenderingOff));
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(m_Path + ".meta"));
        }

        static void AssertTextureCoordinates(Vector2[] expected, Vector2[] actual)
        {
            Assert.AreEqual(expected.Length, actual.Length);
            for (var i = 0; i < expected.Length; ++i)
                Assert.That(Vector2.Distance(expected[i], actual[i]), Is.LessThan(.00001f), "UV at vertex " + i);
        }

        [Test]
        public void TextureMappingSupportsRotatedAtlasCoordinatesAndNewVertices()
        {
            var source = Object.Instantiate(SourceSprite());
            var destination = Object.Instantiate(m_Renderer.sprite);
            try
            {
                Vector2 Map(Vector2 position) => new Vector2(.7f - position.y * .04f, .2f + position.x * .03f);
                var original = source.vertices;
                var sourceUV = original.Select(Map).ToArray();
                var positions = original.Select(point => (Vector3)point).Concat(new[] { (Vector3)((original[0] + original[1] + original[2]) / 3f) }).ToArray();
                var uv = new NativeArray<Vector2>(sourceUV, Allocator.Temp);
                var vertices = new NativeArray<Vector3>(positions, Allocator.Temp);
                try
                {
                    source.SetVertexAttribute<Vector2>(VertexAttribute.TexCoord0, uv);
                    destination.SetVertexCount(positions.Length);
                    destination.SetVertexAttribute<Vector3>(VertexAttribute.Position, vertices);
                    UniskinRigUtility.RestoreTextureCoordinates(source, destination);
                    AssertTextureCoordinates(positions.Select(point => Map(point)).ToArray(), destination.uv);
                    AssertTextureCoordinates(sourceUV, source.uv);
                }
                finally { uv.Dispose(); vertices.Dispose(); }
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(destination); }
        }

        [Test]
        public void ReloadRepairRestoresClearedCoordinatesWithoutChangingAuthoringData()
        {
            var expected = m_Renderer.sprite.uv;
            var authoring = m_Rig.authoringData;
            var metadata = File.ReadAllBytes(m_Path + ".meta");
            var uv = new NativeArray<Vector2>(expected.Length, Allocator.Temp);
            try { m_Renderer.sprite.SetVertexAttribute<Vector2>(VertexAttribute.TexCoord0, uv); }
            finally { uv.Dispose(); }
            Assert.IsTrue(m_Renderer.sprite.uv.All(point => point == Vector2.zero));
            UniskinRigUtility.RepairMissingTextureCoordinates();
            AssertTextureCoordinates(expected, m_Renderer.sprite.uv);
            var rigPath = AssetDatabase.GetAssetPath(m_Rig);
            AssetDatabase.ImportAsset(rigPath, ImportAssetOptions.ForceSynchronousImport);
            AssertTextureCoordinates(expected, AssetDatabase.LoadAssetAtPath<UniskinRig>(rigPath).sprites[0].sprite.uv);
            Assert.AreEqual(authoring, m_Rig.authoringData);
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

        [TestCase(0, false, false)]
        [TestCase(0, true, false)]
        [TestCase(1, false, false)]
        [TestCase(1, true, false)]
        [TestCase(2, false, false)]
        [TestCase(2, true, false)]
        [TestCase(0, false, true)]
        [TestCase(0, true, true)]
        [TestCase(1, false, true)]
        [TestCase(1, true, true)]
        [TestCase(2, false, true)]
        [TestCase(2, true, true)]
        public void ReparentingPosedBonePreservesAuthoredWorldPose(int pathway, bool detach, bool move)
        {
            var provider = (UniskinRigDataProvider)Source().provider;
            provider.data.sprites[0].bones = new[]
            {
                new SpriteBone { guid = "old-parent", name = "OldParent", parentId = -1, position = new Vector3(4, 4, 0), rotation = Quaternion.Euler(0, 0, 30), length = 8 },
                new SpriteBone { guid = "new-parent", name = "NewParent", parentId = -1, position = new Vector3(18, 7, 0), rotation = Quaternion.Euler(0, 0, -40), length = 8 },
                new SpriteBone { guid = "ear", name = "Ear", parentId = 0, position = new Vector3(8, 0, 0), rotation = Quaternion.Euler(0, 0, 15), length = 8 },
                new SpriteBone { guid = "tip", name = "Tip", parentId = 2, position = new Vector3(8, 0, 0), rotation = Quaternion.identity, length = 4 }
            };
            UpdateCharacterBoneMap(provider);
            provider.Apply();
            var binding = m_Root.GetComponent<UniskinRigBinding>();
            var oldParent = binding.bones.Single(link => link.guid == "old-parent").transform;
            var newParent = binding.bones.Single(link => link.guid == "new-parent").transform;
            var ear = binding.bones.Single(link => link.guid == "ear").transform;
            var tip = binding.bones.Single(link => link.guid == "tip").transform;
            m_Root.transform.SetPositionAndRotation(new Vector3(2, -3, 0), Quaternion.Euler(0, 0, 17));
            m_Root.transform.localScale = Vector3.one * 1.7f;
            oldParent.localRotation *= Quaternion.Euler(0, 0, 43);
            newParent.localPosition += new Vector3(.4f, -.2f, 0);
            newParent.localRotation *= Quaternion.Euler(0, 0, -26);
            ear.localRotation *= Quaternion.Euler(0, 0, 12);
            var position = ear.position;
            var rotation = ear.rotation;
            var tipPosition = tip.position;
            var movePixels = move ? new Vector3(6, 3, 0) : Vector3.zero;
            var sceneDelta = ear.TransformVector(movePixels / m_Rig.pixelsPerUnit);
            position += sceneDelta;
            tipPosition += sceneDelta;
            provider = (UniskinRigDataProvider)Source().provider;
            var bones = provider.data.sprites[0].bones;
            var worldPosition = bones[0].position + bones[0].rotation * bones[2].position;
            var worldRotation = bones[0].rotation * bones[2].rotation;
            worldPosition += worldRotation * movePixels;
            bones[2].parentId = detach ? -1 : 1;
            bones[2].position = detach ? worldPosition : Quaternion.Inverse(bones[1].rotation) * (worldPosition - bones[1].position);
            bones[2].rotation = detach ? worldRotation : Quaternion.Inverse(bones[1].rotation) * worldRotation;
            UpdateCharacterBoneMap(provider);
            Action apply;
            if (pathway == 1)
            {
                m_Rig.characterBones = UniskinRigData.Clone(bones);
                apply = () => typeof(UniskinRigBinding).GetMethod("UpdateRuntimeBones", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(binding, null);
            }
            else if (pathway == 2)
            {
                var source = Source();
                var window = ScriptableObject.CreateInstance<SceneEditBoneWindow>();
                try
                {
                    SetWindowField(window, "m_Renderer", m_Renderer);
                    SetWindowField(window, "m_Source", source);
                    var session = GetWindowSession<SceneBoneSession>(window);
                    session.Load(m_Renderer.sprite, source, skeletonOnly: provider.data.hasCharacter);
                    session.CaptureScenePose(m_Renderer, source.rect.rect);
                    var child = session.skeleton.bones.Single(bone => bone.guid == "ear");
                    session.SetParent(child, detach ? null : session.skeleton.bones.Single(bone => bone.guid == "new-parent"));
                    child.position += session.SkeletonToWorld(m_Renderer, source.rect.rect).inverse.MultiplyVector(sceneDelta);
                    session.Changed();
                    Assert.IsTrue(session.dirty);
                }
                catch { Object.DestroyImmediate(window); throw; }
                apply = () => Object.DestroyImmediate(window);
            }
            else apply = () => provider.Apply();
            apply();
            Assert.AreSame(detach ? m_Root.transform : newParent, ear.parent);
            Assert.That(Vector3.Distance(position, ear.position), Is.LessThan(.00001f));
            Assert.That(Quaternion.Angle(rotation, ear.rotation), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(tipPosition, tip.position), Is.LessThan(.00001f));
            if (pathway == 1) apply();
            else UniskinRigUtility.RefreshInstances(m_Rig);
            Assert.That(Vector3.Distance(position, ear.position), Is.LessThan(.00001f), "Repeated synchronization must not apply another pose delta.");
            Assert.That(Quaternion.Angle(rotation, ear.rotation), Is.LessThan(.001f));
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
