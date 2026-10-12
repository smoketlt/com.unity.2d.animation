using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Animation.Tests
{
    internal class SceneBoneEditingTests
    {
        string m_Path;
        Texture2D m_Texture;
        SceneBoneSession m_Session;

        [SetUp]
        public void Setup()
        {
            m_Path = AssetDatabase.GenerateUniqueAssetPath("Assets/UniskinBoneRoundTrip.png");
            m_Texture = new Texture2D(32, 32);
            m_Texture.SetPixels(Enumerable.Repeat(Color.white, 1024).ToArray());
            m_Texture.Apply();
            File.WriteAllBytes(m_Path, m_Texture.EncodeToPNG());
            AssetDatabase.ImportAsset(m_Path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(m_Path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }

        [TearDown]
        public void Cleanup()
        {
            if (m_Session != null) Object.DestroyImmediate(m_Session);
            AssetDatabase.DeleteAsset(m_Path);
            if (m_Texture != null) Object.DestroyImmediate(m_Texture);
        }

        SceneMeshSource Source() => new SceneMeshSource(AssetDatabase.LoadAssetAtPath<Sprite>(m_Path));

        [Test]
        public void NamePollingPreservesPendingInputAndRefreshesChangedModelOrSelection()
        {
            Load();
            var bone = m_Session.skeleton.CreateBone(null, Vector3.zero, Vector3.right, false, "bone_3");
            var other = m_Session.skeleton.CreateBone(null, Vector3.up, Vector3.one, false, "bone_4");
            var window = ScriptableObject.CreateInstance<SceneEditBoneWindow>();
            try
            {
                var field = new TextField { name = "boneName", isDelayed = true };
                window.rootVisualElement.Clear();
                window.rootVisualElement.Add(field);
                window.RefreshNameField(bone);
                Assert.AreEqual("bone_3", field.value);
                field.SetValueWithoutNotify("bone_32");
                for (var i = 0; i < 5; ++i) window.RefreshNameField(bone);
                Assert.AreEqual("bone_32", field.value, "Periodic refresh must not replace an uncommitted name.");
                bone.name = "Renamed";
                window.RefreshNameField(bone);
                Assert.AreEqual("Renamed", field.value);
                field.SetValueWithoutNotify("Pending");
                window.RefreshNameField(other);
                Assert.AreEqual("bone_4", field.value);
                window.RefreshNameField(null);
                Assert.AreEqual(string.Empty, field.value);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void RenameUpdatesDescendantFloatAndObjectCurvesAndSupportsUndo()
        {
            var root = new GameObject("RenameTestRoot");
            var clip = new AnimationClip { legacy = true };
            try
            {
                var bone = new GameObject("Arm").transform;
                bone.SetParent(root.transform);
                var child = new GameObject("Hand").transform;
                child.SetParent(bone);
                var animation = root.AddComponent<UnityEngine.Animation>();
                animation.AddClip(clip, "Test");
                var binding = EditorCurveBinding.FloatCurve("Arm/Hand", typeof(Transform), "m_LocalPosition.x");
                var curve = AnimationCurve.Linear(0, 2, 1, 5);
                AnimationUtility.SetEditorCurve(clip, binding, curve);
                var objectBinding = EditorCurveBinding.PPtrCurve("Arm/Hand", typeof(SpriteRenderer), "m_Sprite");
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(m_Path);
                AnimationUtility.SetObjectReferenceCurve(clip, objectBinding, new[] { new ObjectReferenceKeyframe { time = 0, value = sprite } });
                var unrelated = EditorCurveBinding.FloatCurve("ArmOther", typeof(Transform), "m_LocalPosition.y");
                AnimationUtility.SetEditorCurve(clip, unrelated, AnimationCurve.Constant(0, 1, 9));
                Undo.IncrementCurrentGroup();
                var group = Undo.GetCurrentGroup();
                new SceneBoneRenamePlan(new System.Collections.Generic.Dictionary<Transform, string>
                    { [bone] = "UpperArm", [child] = "Palm" }).Apply();
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
                Assert.AreEqual("UpperArm", bone.name);
                Assert.AreEqual("Palm", child.name);
                Assert.IsNull(AnimationUtility.GetEditorCurve(clip, binding));
                binding.path = "UpperArm/Palm";
                Assert.AreEqual(3.5f, AnimationUtility.GetEditorCurve(clip, binding).Evaluate(.5f), .00001f);
                objectBinding.path = "UpperArm/Palm";
                Assert.AreSame(sprite, AnimationUtility.GetObjectReferenceCurve(clip, objectBinding)[0].value);
                Assert.AreEqual(9, AnimationUtility.GetEditorCurve(clip, unrelated).Evaluate(.5f));
                Undo.PerformUndo();
                Assert.AreEqual("Arm", bone.name);
                Assert.AreEqual("Hand", child.name);
                binding.path = "Arm/Hand";
                Assert.IsNotNull(AnimationUtility.GetEditorCurve(clip, binding));
                Assert.IsFalse(AnimationUtility.GetCurveBindings(clip).Any(item => item.path == "UpperArm/Palm"));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(clip); }
        }

        [Test]
        public void RenameRejectsSiblingCollisionWithoutChangingObjects()
        {
            var root = new GameObject("RenameTestRoot");
            try
            {
                var bone = new GameObject("Arm").transform;
                bone.SetParent(root.transform);
                new GameObject("Hand").transform.SetParent(root.transform);
                Assert.Throws<InvalidOperationException>(() => new SceneBoneRenamePlan(
                    new System.Collections.Generic.Dictionary<Transform, string> { [bone] = "Hand" }));
                Assert.AreEqual("Arm", bone.name);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void RenameUsesAnimatorOverrideClipsAndRejectsBindingCollision()
        {
            var root = new GameObject("RenameTestRoot");
            var controller = new UnityEditor.Animations.AnimatorController();
            var original = new AnimationClip();
            var replacement = new AnimationClip();
            AnimatorOverrideController overrides = null;
            try
            {
                controller.AddLayer("Base");
                controller.layers[0].stateMachine.AddState("Idle").motion = original;
                overrides = new AnimatorOverrideController(controller);
                overrides[original] = replacement;
                root.AddComponent<Animator>().runtimeAnimatorController = overrides;
                var bone = new GameObject("Arm").transform;
                bone.SetParent(root.transform);
                var binding = EditorCurveBinding.FloatCurve("Arm", typeof(Transform), "m_LocalPosition.x");
                AnimationUtility.SetEditorCurve(original, binding, AnimationCurve.Constant(0, 1, 1));
                AnimationUtility.SetEditorCurve(replacement, binding, AnimationCurve.Constant(0, 1, 2));
                var target = binding; target.path = "UpperArm";
                AnimationUtility.SetEditorCurve(replacement, target, AnimationCurve.Constant(0, 1, 3));
                var names = new System.Collections.Generic.Dictionary<Transform, string> { [bone] = "UpperArm" };
                Assert.Throws<InvalidOperationException>(() => new SceneBoneRenamePlan(names));
                Assert.AreEqual("Arm", bone.name);
                AnimationUtility.SetEditorCurve(replacement, target, null);
                new SceneBoneRenamePlan(names).Apply();
                Assert.IsNull(AnimationUtility.GetEditorCurve(replacement, binding));
                Assert.AreEqual(2, AnimationUtility.GetEditorCurve(replacement, target).Evaluate(0));
                Assert.IsNotNull(AnimationUtility.GetEditorCurve(original, binding));
            }
            finally
            {
                Object.DestroyImmediate(root);
                if (overrides != null) Object.DestroyImmediate(overrides);
                foreach (var layer in controller.layers) Object.DestroyImmediate(layer.stateMachine);
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(replacement);
            }
        }

        [Test]
        public void NameIsStagedAndSavedToSourceWithUndoAndValidation()
        {
            Load();
            var bone = m_Session.skeleton.CreateBone(null, Vector3.zero, Vector3.right * 10, false, "Arm");
            m_Session.Changed(true);
            m_Session.Save(Source());
            m_Session.sourceFingerprint = SceneBoneSession.Fingerprint(Source());
            m_Session.SelectBone(bone.guid);
            Undo.IncrementCurrentGroup();
            m_Session.RenameSelectedBone("UpperArm");
            Assert.AreEqual("Arm", Source().provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(Source().rect.spriteID)[0].name);
            Assert.IsTrue(m_Session.dirty);
            Undo.PerformUndo();
            m_Session.RestoreAfterUndo();
            Assert.AreEqual("Arm", bone.name);
            Assert.IsFalse(m_Session.dirty);
            m_Session.SelectBone(bone.guid);
            m_Session.RenameSelectedBone("UpperArm");
            Assert.Throws<InvalidOperationException>(() => m_Session.RenameSelectedBone("Bad/Path"));
            m_Session.Save(Source());
            Assert.AreEqual("UpperArm", Source().provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(Source().rect.spriteID)[0].name);
        }

        void Load()
        {
            m_Session = ScriptableObject.CreateInstance<SceneBoneSession>();
            m_Session.Load(AssetDatabase.LoadAssetAtPath<Sprite>(m_Path), Source());
        }

        [Test]
        public void CharacterLengthUsesAuthoringPpuWhenTextureIsDownscaled()
        {
            var texture = new Texture2D(128, 128);
            try
            {
                texture.SetPixels(Enumerable.Repeat(Color.white, 128 * 128).ToArray());
                texture.Apply();
                File.WriteAllBytes(m_Path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(m_Path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(m_Path);
                importer.spritePixelsPerUnit = 100;
                importer.maxTextureSize = 32;
                importer.SaveAndReimport();
                var source = Source();
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(m_Path);
                Assert.Less(sprite.pixelsPerUnit, source.provider.pixelsPerUnit,
                    "This regression requires different authoring and resized Sprite PPU values.");
                Assert.AreEqual(2.25f, SceneBoneInspector.CharacterBoneLocalLength(source.provider, 225), .00001f,
                    "Character hierarchy lengths must use the same authoring PPU as PSD Importer positions.");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Test]
        public void OrdinarySpriteSavesBonesWithoutCreatingMesh()
        {
            Load();
            Assert.IsFalse(m_Session.dirty);
            var skeleton = m_Session.skeleton;
            using (m_Session.cache.UndoScope("Create test bones"))
            {
                var root = skeleton.CreateBone(null, skeleton.position + new Vector3(3, 4), skeleton.position + new Vector3(19, 4), false, "Root");
                root.bindPoseColor = Color.green;
                skeleton.SplitBone(root, 8, "Tip");
                m_Session.Changed(true);
            }
            Assert.IsTrue(m_Session.dirty);
            Assert.IsEmpty(Source().provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(Source().rect.spriteID), "Staging must not modify the importer.");
            m_Session.Save(Source());
            var source = Source();
            var saved = source.provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(source.rect.spriteID);
            Assert.AreEqual(2, saved.Count);
            Assert.AreEqual("Root", saved[0].name);
            Assert.AreEqual(new Vector3(3, 4), saved[0].position);
            Assert.AreEqual(8, saved[0].length);
            Assert.AreEqual((Color32)Color.green, saved[0].color);
            Assert.AreEqual(0, saved[1].parentId);
            Assert.IsFalse(source.hasAuthoredMesh);
            Assert.IsFalse(m_Session.dirty);
        }

        [Test]
        public void FirstBonesInitializeAuthoredMeshWeightsWithoutChangingGeometry()
        {
            var source = Source();
            source.WriteMesh(source.ReadMesh(AssetDatabase.LoadAssetAtPath<Sprite>(m_Path)));
            source = Source();
            var positions = source.meshProvider.GetVertices(source.rect.spriteID).Select(vertex => vertex.position).ToArray();
            var indices = source.meshProvider.GetIndices(source.rect.spriteID).ToArray();
            var edges = source.meshProvider.GetEdges(source.rect.spriteID).ToArray();
            Load();
            using (m_Session.cache.UndoScope("Create first mesh bones"))
            {
                var root = m_Session.skeleton.CreateBone(null, new Vector3(8, 8), new Vector3(16, 16), false, "Root");
                m_Session.skeleton.CreateBone(root, new Vector3(16, 16), new Vector3(24, 24), true, "Tip");
                m_Session.Changed(true);
            }
            m_Session.Save(Source());
            source = Source();
            var vertices = source.meshProvider.GetVertices(source.rect.spriteID);
            Assert.IsNotEmpty(vertices);
            foreach (var vertex in vertices)
            {
                var weight = vertex.boneWeight;
                Assert.That(weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3, Is.EqualTo(1f).Within(0.00001f));
                for (var channel = 0; channel < 4; ++channel)
                    if (weight.GetWeight(channel) > 0f) Assert.That(weight.GetBoneIndex(channel), Is.InRange(0, 1));
            }
            CollectionAssert.AreEqual(positions, vertices.Select(vertex => vertex.position));
            CollectionAssert.AreEqual(indices, source.meshProvider.GetIndices(source.rect.spriteID));
            CollectionAssert.AreEqual(edges, source.meshProvider.GetEdges(source.rect.spriteID));
        }

        [Test]
        public void ReparentAndSplitPreserveWeightedBoneIdentityAndGeometry()
        {
            var source = Source();
            source.provider.GetDataProvider<ISpriteBoneDataProvider>().SetBones(source.rect.spriteID, new[]
            {
                new SpriteBone { name = "Root", guid = "root-guid", parentId = -1, rotation = Quaternion.identity, position = new Vector3(3,4), length = 8, color = Color.red },
                new SpriteBone { name = "Child", guid = "child-guid", parentId = 0, rotation = Quaternion.identity, position = new Vector3(8,0), length = 8, color = Color.blue },
                new SpriteBone { name = "Other", guid = "other-guid", parentId = -1, rotation = Quaternion.identity, position = new Vector3(1,1), length = 4, color = Color.green }
            }.ToList());
            source.provider.Apply(); source.importer.SaveAndReimport();
            source = Source();
            var mesh = source.ReadMesh(AssetDatabase.LoadAssetAtPath<Sprite>(m_Path));
            foreach (var weight in mesh.vertexWeights) weight.SetFromBoneWeight(new BoneWeight { boneIndex0 = 1, weight0 = 1 });
            source.WriteMesh(mesh);
            source = Source();
            var positions = source.meshProvider.GetVertices(source.rect.spriteID).Select(vertex => vertex.position).ToArray();
            var indices = source.meshProvider.GetIndices(source.rect.spriteID).ToArray();
            var edges = source.meshProvider.GetEdges(source.rect.spriteID).ToArray();
            Load();
            var root = m_Session.skeleton.bones.Single(bone => bone.guid == "root-guid");
            var child = m_Session.skeleton.bones.Single(bone => bone.guid == "child-guid");
            var other = m_Session.skeleton.bones.Single(bone => bone.guid == "other-guid");
            var position = root.position; var rotation = root.rotation;
            Assert.IsFalse(SceneBoneSession.CanSetParent(root, child));
            Assert.IsFalse(SceneBoneSession.CanSetParent(root, root));
            Assert.IsTrue(SceneBoneSession.CanSetParent(root, other));
            m_Session.SetParent(root, other);
            Assert.AreEqual(position, root.position);
            Assert.AreEqual(rotation, root.rotation);
            using (m_Session.cache.UndoScope("Split test bone"))
            { m_Session.skeleton.SplitBone(root, 4, "Split"); m_Session.Changed(true); }
            m_Session.Save(Source());
            source = Source();
            var bones = source.provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(source.rect.spriteID);
            var vertices = source.meshProvider.GetVertices(source.rect.spriteID);
            CollectionAssert.AreEqual(positions, vertices.Select(vertex => vertex.position));
            CollectionAssert.AreEqual(indices, source.meshProvider.GetIndices(source.rect.spriteID));
            CollectionAssert.AreEqual(edges, source.meshProvider.GetEdges(source.rect.spriteID));
            Assert.AreEqual("child-guid", bones[vertices[0].boneWeight.boneIndex0].guid);
            Assert.AreEqual(1, vertices[0].boneWeight.weight0);
            Assert.AreEqual(4, bones.Count);
            for (var i = 0; i < bones.Count; ++i) Assert.Less(bones[i].parentId, i);
        }

        [Test]
        public void ReparentUndoRestoresHierarchyAndCleanSession()
        {
            Load();
            BoneCache root, child;
            using (m_Session.cache.UndoScope("Create test bones"))
            {
                root = m_Session.skeleton.CreateBone(null, Vector3.zero, Vector3.right * 16, false, "Root");
                child = m_Session.skeleton.CreateBone(root, Vector3.right * 16, Vector3.right * 24, true, "Child");
                m_Session.Changed(true);
            }
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            var position = child.position;
            m_Session.SetParent(child, null);
            Assert.IsNull(child.parentBone);
            Undo.RevertAllDownToGroup(group);
            m_Session.RestoreAfterUndo();
            Assert.AreSame(root, child.parentBone);
            Assert.AreEqual(position, child.position);
        }

        [Test]
        public void ExternalAssetChangeRejectsSave()
        {
            Load();
            using (m_Session.cache.UndoScope("Create test bone"))
            { m_Session.skeleton.CreateBone(null, Vector3.zero, Vector3.right * 8, false, "Pending"); m_Session.Changed(true); }
            var source = Source();
            source.provider.GetDataProvider<ISpriteBoneDataProvider>().SetBones(source.rect.spriteID, new[]
            { new SpriteBone { name = "External", guid = "external-guid", parentId = -1, rotation = Quaternion.identity } }.ToList());
            source.provider.Apply(); source.importer.SaveAndReimport();
            Assert.Throws<InvalidOperationException>(() => m_Session.Save(Source()));
            Assert.AreEqual("External", Source().provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(Source().rect.spriteID)[0].name);
        }

        [Test]
        public void SkeletonMappingIncludesRendererScaleRotationAndFlip()
        {
            Load();
            var go = new GameObject("Mapping test");
            try
            {
                var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(m_Path);
                renderer.flipX = true; renderer.flipY = true;
                go.transform.position = new Vector3(4, 2, 0); go.transform.rotation = Quaternion.Euler(0, 0, 35);
                go.transform.localScale = new Vector3(2, 3, 1);
                var pixel = new Vector3(7, 9);
                var matrix = m_Session.SkeletonToWorld(renderer, Source().rect.rect);
                var expected = SceneMeshSession.PixelToWorld(renderer, Source().rect.rect).MultiplyPoint3x4(pixel);
                Assert.That(Vector3.Distance(expected, matrix.MultiplyPoint3x4(m_Session.cache.selectedSprite.localToWorldMatrix.MultiplyPoint3x4(pixel))), Is.LessThan(0.00001));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void PosedSceneEditingPreservesSourcePoseAndConvertsNewEdits()
        {
            var source = Source();
            source.provider.GetDataProvider<ISpriteBoneDataProvider>().SetBones(source.rect.spriteID, new[]
            {
                new SpriteBone { name = "Root", guid = "posed-root", parentId = -1, position = new Vector3(4, 8), rotation = Quaternion.identity, length = 12 },
                new SpriteBone { name = "Child", guid = "posed-child", parentId = 0, position = new Vector3(12, 0), rotation = Quaternion.identity, length = 8 }
            }.ToList());
            source.provider.Apply(); source.importer.SaveAndReimport();
            Load();
            var sourcePose = m_Session.skeleton.GetWorldPose();
            var host = new GameObject("Posed editing fixture");
            try
            {
                var renderer = host.AddComponent<SpriteRenderer>(); renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(m_Path);
                var root = new GameObject("Root").transform; root.SetParent(host.transform, false);
                var child = new GameObject("Child").transform; child.SetParent(root, false);
                root.localPosition = new Vector3(.1f, .2f); root.localRotation = Quaternion.Euler(0, 0, 75);
                child.localPosition = new Vector3(.12f, 0); child.localRotation = Quaternion.Euler(0, 0, -35);
                var skin = host.AddComponent<UnityEngine.U2D.Animation.SpriteSkin>();
                skin.SetRootBone(host.transform); skin.SetBoneTransforms(new[] { root, child });
                m_Session.CaptureScenePose(renderer, Source().rect.rect);
                var matrix = m_Session.SkeletonToWorld(renderer, Source().rect.rect);
                var displayed = m_Session.skeleton.GetWorldPose();
                Assert.IsFalse(m_Session.dirty, "Opening a posed skeleton must remain clean.");
                Assert.That(Vector3.Distance(matrix.MultiplyPoint3x4(displayed[0].pose.position), root.position), Is.LessThan(.00001f));
                Assert.That(Vector3.Distance(matrix.MultiplyPoint3x4(displayed[1].pose.position), child.position), Is.LessThan(.00001f));
                m_Session.WithSourcePose(() => CollectionAssert.AreEqual(sourcePose, m_Session.skeleton.GetWorldPose()));
                CollectionAssert.AreEqual(displayed, m_Session.skeleton.GetWorldPose());
                var cacheChild = m_Session.skeleton.bones.Single(bone => bone.guid == "posed-child");
                Undo.IncrementCurrentGroup();
                var moveGroup = Undo.GetCurrentGroup();
                using (m_Session.cache.UndoScope("Move posed child"))
                {
                    cacheChild.position += cacheChild.right * 3;
                    m_Session.Changed();
                }
                m_Session.WithSourcePose(() => Assert.That(Vector3.Distance(cacheChild.position, sourcePose[1].pose.position + Vector3.right * 3), Is.LessThan(.0001f)));
                Undo.RevertAllDownToGroup(moveGroup);
                m_Session.RestoreAfterUndo();
                CollectionAssert.AreEqual(displayed, m_Session.skeleton.GetWorldPose());
                Assert.IsFalse(m_Session.dirty);
                using (m_Session.cache.UndoScope("Move posed child again"))
                { cacheChild.position += cacheChild.right * 3; m_Session.Changed(); }
                var movedPosition = cacheChild.position;
                m_Session.SetParent(cacheChild, null);
                Assert.That(Vector3.Distance(movedPosition, cacheChild.position), Is.LessThan(.00001f));
                m_Session.WithSourcePose(() => Assert.That(Vector3.Distance(cacheChild.position, sourcePose[1].pose.position + Vector3.right * 3), Is.LessThan(.0001f)));
                var beforeSplit = cacheChild.worldPose;
                using (m_Session.cache.UndoScope("Split posed child"))
                {
                    m_Session.skeleton.SplitBone(cacheChild, cacheChild.length / 2, "Tip");
                    m_Session.Changed(true);
                }
                var tip = m_Session.skeleton.bones.Single(bone => bone.name == "Tip");
                m_Session.WithSourcePose(() =>
                {
                    Assert.That(Vector3.Distance(tip.position, sourcePose[1].pose.position + Vector3.right * (3 + beforeSplit.length / 2)), Is.LessThan(.0001f));
                    Assert.That(Quaternion.Angle(tip.rotation, Quaternion.identity), Is.LessThan(.001f));
                });
                var sceneRootPose = root.localRotation; var sceneChildPose = child.localRotation;
                m_Session.Save(Source());
                var saved = Source().provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(Source().rect.spriteID);
                Assert.That(Quaternion.Angle(saved[0].rotation, Quaternion.identity), Is.LessThan(.001f));
                Assert.AreEqual(sceneRootPose, root.localRotation); Assert.AreEqual(sceneChildPose, child.localRotation);
                Assert.IsFalse(m_Session.dirty);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void AnimatedScenePosePreservesStagedEditsUndoAndSourceSave()
        {
            var source = Source();
            source.provider.GetDataProvider<ISpriteBoneDataProvider>().SetBones(source.rect.spriteID, new[]
            {
                new SpriteBone { name = "Root", guid = "animated-root", parentId = -1, position = new Vector3(4, 8), rotation = Quaternion.identity, length = 12 },
                new SpriteBone { name = "Child", guid = "animated-child", parentId = 0, position = new Vector3(12, 0), rotation = Quaternion.identity, length = 8 }
            }.ToList());
            source.provider.Apply(); source.importer.SaveAndReimport();
            Load();
            var sourcePose = m_Session.skeleton.GetWorldPose();
            var host = new GameObject("Animated bone editing fixture");
            var clip = new AnimationClip();
            try
            {
                var renderer = host.AddComponent<SpriteRenderer>(); renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(m_Path);
                var root = new GameObject("Root").transform; root.SetParent(host.transform, false);
                var child = new GameObject("Child").transform; child.SetParent(root, false);
                root.localPosition = new Vector3(.04f, .08f); child.localPosition = new Vector3(.12f, 0);
                var skin = host.AddComponent<UnityEngine.U2D.Animation.SpriteSkin>();
                skin.SetRootBone(host.transform); skin.SetBoneTransforms(new[] { root, child });
                clip.SetCurve("Root", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Linear(0, 0, 1, 95));
                clip.SetCurve("Root", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, .04f, 1, .2f));
                clip.SetCurve("Root/Child", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Linear(0, 0, 1, -40));
                m_Session.CaptureScenePose(renderer, source.rect.rect);
                var cachedChild = m_Session.skeleton.bones.Single(bone => bone.guid == "animated-child");
                void Sample(float time)
                {
                    clip.SampleAnimation(host, time);
                    m_Session.RefreshScenePose(renderer, source.rect.rect);
                }
                void AssertAligned(BoneCache bone, Transform transform)
                {
                    var matrix = m_Session.SkeletonToWorld(renderer, source.rect.rect);
                    Assert.Less(Vector3.Distance(matrix.MultiplyPoint3x4(bone.position), transform.position), .0001f);
                    Assert.Less(Vector3.Distance(matrix.MultiplyVector(bone.right).normalized, transform.right.normalized), .00001f);
                }
                for (var frame = 0; frame <= 20; ++frame)
                {
                    Sample(frame / 20f);
                    AssertAligned(cachedChild, child);
                    Assert.IsFalse(m_Session.dirty, "Animation alone must not dirty the source skeleton.");
                    m_Session.WithSourcePose(() => CollectionAssert.AreEqual(sourcePose, m_Session.skeleton.GetWorldPose()));
                }
                Undo.IncrementCurrentGroup();
                var editGroup = Undo.GetCurrentGroup();
                using (m_Session.cache.UndoScope("Edit animated bone"))
                { cachedChild.position += cachedChild.right * 3; m_Session.Changed(); }
                for (var frame = 0; frame <= 20; ++frame)
                {
                    Sample(frame / 20f);
                    Assert.IsTrue(m_Session.dirty);
                    m_Session.WithSourcePose(() => Assert.Less(Vector3.Distance(cachedChild.position,
                        sourcePose[1].pose.position + Vector3.right * 3), .0001f));
                }
                Undo.RevertAllDownToGroup(editGroup);
                m_Session.RestoreAfterUndo();
                Sample(.7f);
                AssertAligned(cachedChild, child);
                Assert.IsFalse(m_Session.dirty, "Undo must restore the source pose even after the animation advances.");
                using (m_Session.cache.UndoScope("Split animated bone"))
                { m_Session.skeleton.SplitBone(cachedChild, cachedChild.length / 2, "Tip"); m_Session.Changed(true); }
                var tip = m_Session.skeleton.bones.Single(bone => bone.name == "Tip");
                for (var frame = 0; frame <= 20; ++frame)
                {
                    Sample(frame / 20f);
                    var matrix = m_Session.SkeletonToWorld(renderer, source.rect.rect);
                    Assert.Less(Vector3.Distance(matrix.MultiplyPoint3x4(tip.position), child.TransformPoint(Vector3.right * .04f)), .0001f,
                        "New split bones must follow their animated parent's deformation.");
                    m_Session.WithSourcePose(() => Assert.Less(Vector3.Distance(tip.position,
                        sourcePose[1].pose.position + Vector3.right * 4), .0001f));
                }
                var sceneRotation = child.localRotation;
                m_Session.Save(Source());
                Assert.AreEqual(sceneRotation, child.localRotation);
                Assert.IsFalse(m_Session.dirty);
                var saved = Source().provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(Source().rect.spriteID);
                foreach (var bone in saved) Assert.Less(Quaternion.Angle(bone.rotation, Quaternion.identity), .001f);
                Sample(.1f);
                Assert.IsFalse(m_Session.dirty, "Playback after saving must also remain clean.");
            }
            finally { Object.DestroyImmediate(clip); Object.DestroyImmediate(host); }
        }

        [Test]
        public void CharacterMappingIgnoresLayerPlacementOverrides()
        {
            var path = AssetDatabase.GetAllAssetPaths().FirstOrDefault(item => item.StartsWith("Assets/") &&
                item.EndsWith(".psb", StringComparison.OrdinalIgnoreCase) && AssetDatabase.LoadAllAssetsAtPath(item)
                    .OfType<Sprite>().Any(sprite => sprite.GetBones().Length > 0));
            if (path == null) Assert.Ignore("No rigged PSB fixture is available.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try
            {
                instance.transform.position = new Vector3(7, -3);
                instance.transform.rotation = Quaternion.Euler(0, 0, 25);
                instance.transform.localScale = new Vector3(2, 3, 1);
                var skin = instance.GetComponentsInChildren<UnityEngine.U2D.Animation.SpriteSkin>(true).First();
                var renderer = skin.GetComponent<SpriteRenderer>();
                var source = new SceneMeshSource(renderer.sprite);
                m_Session = ScriptableObject.CreateInstance<SceneBoneSession>();
                m_Session.Load(renderer.sprite, source);
                var before = m_Session.SkeletonToWorld(renderer, source.rect.rect);
                var runtimeBones = renderer.sprite.GetBones();
                for (var i = 0; i < runtimeBones.Length; ++i)
                {
                    var bone = m_Session.skeleton.bones.Single(item => item.guid == runtimeBones[i].guid);
                    Assert.That(Vector3.Distance(before.MultiplyPoint3x4(bone.position), skin.boneTransforms[i].position), Is.LessThan(.0001f));
                }
                renderer.transform.localPosition += new Vector3(8, -4);
                renderer.transform.localRotation = Quaternion.Euler(0, 0, 60);
                renderer.transform.localScale = new Vector3(.5f, 1.7f, 1);
                var after = m_Session.SkeletonToWorld(renderer, source.rect.rect);
                foreach (var bone in m_Session.skeleton.bones)
                    Assert.That(Vector3.Distance(before.MultiplyPoint3x4(bone.position), after.MultiplyPoint3x4(bone.position)), Is.LessThan(.0001f));
                Assert.IsFalse(m_Session.dirty);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void WindowResourcesContainRequestedCommandsAndProperties()
        {
            var template = ResourceLoader.Load<VisualTreeAsset>("SkinningModule/SceneEditBoneWindow.uxml");
            Assert.IsNotNull(template);
            var root = template.CloneTree();
            Assert.AreEqual("Edit Bones", root.Q<Label>("windowTitle").text);
            Assert.IsNotNull(root.Q<Image>("windowIcon"));
            Assert.IsNotNull(ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/EditBone.png"));
            foreach (var name in new[] { "create", "split", "setParent" }) Assert.IsNotNull(root.Q<Button>(name));
            Assert.IsNotNull(root.Q<TextField>("boneName"));
            Assert.IsNotNull(root.Q<ColorField>("boneColor"));
            Assert.IsNotNull(root.Q<FloatField>("boneLength"));
            foreach (var name in new[] { "Bone_Create", "Bone_Split", "Bone_SetParent" })
                Assert.IsNotNull(ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/" + name + ".png"));
        }

        [Test]
        public void PsbFirstBonesKeepPreviouslyUnriggedLayerRenderable()
        {
            var originalPath = AssetDatabase.GetAllAssetPaths().FirstOrDefault(path => path.StartsWith("Assets/") &&
                path.EndsWith(".psb", StringComparison.OrdinalIgnoreCase));
            if (originalPath == null) Assert.Ignore("No PSB fixture is available.");
            var copy = AssetDatabase.GenerateUniqueAssetPath("Assets/UniskinFirstBones.psb");
            Assert.IsTrue(AssetDatabase.CopyAsset(originalPath, copy));
            try
            {
                var sprite = AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Sprite>().First();
                var source = new SceneMeshSource(sprite);
                var characterProvider = source.provider.GetDataProvider<ICharacterDataProvider>();
                if (characterProvider == null) Assert.Ignore("The PSB fixture has no character provider.");
                var character = characterProvider.GetCharacterData();
                character.bones = Array.Empty<SpriteBone>();
                for (var i = 0; i < character.parts.Length; ++i)
                    character.parts[i].bones = Array.Empty<int>();
                characterProvider.SetCharacterData(character);
                foreach (var rect in source.provider.GetSpriteRects())
                {
                    source.provider.GetDataProvider<ISpriteBoneDataProvider>().SetBones(rect.spriteID, new System.Collections.Generic.List<SpriteBone>());
                    var vertices = source.meshProvider.GetVertices(rect.spriteID);
                    for (var i = 0; i < vertices.Length; ++i) vertices[i].boneWeight = default;
                    source.meshProvider.SetVertices(rect.spriteID, vertices);
                }
                source.provider.Apply(); source.importer.SaveAndReimport();
                sprite = AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Sprite>().First(item => item.name == sprite.name);
                source = new SceneMeshSource(sprite);
                var positions = source.meshProvider.GetVertices(source.rect.spriteID).Select(vertex => vertex.position).ToArray();
                Assert.IsNotEmpty(positions);
                m_Session = ScriptableObject.CreateInstance<SceneBoneSession>();
                m_Session.Load(sprite, source);
                var part = m_Session.cache.selectedSprite.GetCharacterPart();
                var start = part.localToWorldMatrix.MultiplyPoint3x4(new Vector3(source.rect.rect.width * .25f, source.rect.rect.height * .5f));
                var end = part.localToWorldMatrix.MultiplyPoint3x4(new Vector3(source.rect.rect.width * .75f, source.rect.rect.height * .5f));
                using (m_Session.cache.UndoScope("Create PSB first bones"))
                {
                    var root = m_Session.skeleton.CreateBone(null, start, end, false, "Root");
                    m_Session.skeleton.SplitBone(root, root.length * .5f, "Tip");
                    m_Session.Changed(true);
                }
                m_Session.Save(source);
                sprite = AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Sprite>().First(item => item.name == sprite.name);
                source = new SceneMeshSource(sprite);
                Assert.AreEqual(2, sprite.GetBones().Length);
                CollectionAssert.AreEqual(positions, source.meshProvider.GetVertices(source.rect.spriteID).Select(vertex => vertex.position));
                var runtimeWeights = sprite.GetVertexAttribute<BoneWeight>(UnityEngine.Rendering.VertexAttribute.BlendWeight);
                Assert.AreEqual(positions.Length, runtimeWeights.Length);
                foreach (var weight in runtimeWeights)
                    Assert.That(weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3, Is.EqualTo(1f).Within(.00001f));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(copy);
                var skin = prefab.GetComponentsInChildren<UnityEngine.U2D.Animation.SpriteSkin>(true)
                    .Single(item => item.GetComponent<SpriteRenderer>().sprite.name == sprite.name);
                Assert.AreEqual(2, skin.boneTransforms.Length);
                Assert.IsTrue(skin.boneTransforms.All(bone => bone != null));
            }
            finally { AssetDatabase.DeleteAsset(copy); }
        }

        [Test]
        public void PsbSplitPreservesAllLayerGeometryWeightsAndCharacterMetadata()
        {
            var originalPath = AssetDatabase.GetAllAssetPaths().FirstOrDefault(path => path.StartsWith("Assets/") &&
                path.EndsWith(".psb", StringComparison.OrdinalIgnoreCase) && AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                    .Any(sprite => sprite.GetBones().Length > 0));
            if (originalPath == null) Assert.Ignore("No rigged PSB fixture is available.");
            var originalBytes = File.ReadAllBytes(originalPath);
            var originalMeta = File.ReadAllBytes(originalPath + ".meta");
            var copy = AssetDatabase.GenerateUniqueAssetPath("Assets/UniskinBonePsbRoundTrip.psb");
            Assert.IsTrue(AssetDatabase.CopyAsset(originalPath, copy));
            try
            {
                var sprite = AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Sprite>().First(item => item.GetBones().Length > 0);
                var source = new SceneMeshSource(sprite);
                var characterProvider = source.provider.GetDataProvider<ICharacterDataProvider>();
                if (characterProvider == null) Assert.Ignore("The PSB fixture has no character provider.");
                var metadata = CharacterMetadata(characterProvider.GetCharacterData());
                var geometry = source.provider.GetSpriteRects().ToDictionary(rect => rect.spriteID.ToString(), rect =>
                    JsonUtility.ToJson(new GeometryRecord { positions = source.meshProvider.GetVertices(rect.spriteID).Select(vertex => vertex.position).ToArray(),
                        indices = source.meshProvider.GetIndices(rect.spriteID), edges = source.meshProvider.GetEdges(rect.spriteID) }));
                var weights = source.provider.GetSpriteRects().ToDictionary(rect => rect.spriteID.ToString(), rect => BoneWeights(source, rect.spriteID));
                m_Session = ScriptableObject.CreateInstance<SceneBoneSession>();
                m_Session.Load(sprite, source);
                Assert.IsTrue(m_Session.cache.hasCharacter);
                var target = m_Session.skeleton.bones.First(bone => bone.length > 1 && !bone.IsConstraintParent());
                using (m_Session.cache.UndoScope("Split PSB test bone"))
                { m_Session.skeleton.SplitBone(target, target.length / 2, "TestSplit"); m_Session.Changed(true); }
                var boneCount = m_Session.skeleton.boneCount;
                m_Session.Save(new SceneMeshSource(sprite));
                sprite = AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Sprite>().First(item => item.name == sprite.name);
                source = new SceneMeshSource(sprite);
                var savedCharacter = source.provider.GetDataProvider<ICharacterDataProvider>().GetCharacterData();
                Assert.AreEqual(boneCount, savedCharacter.bones.Length);
                Assert.AreEqual(metadata, CharacterMetadata(savedCharacter));
                foreach (var rect in source.provider.GetSpriteRects())
                {
                    var id = rect.spriteID.ToString();
                    Assert.AreEqual(geometry[id], JsonUtility.ToJson(new GeometryRecord
                    { positions = source.meshProvider.GetVertices(rect.spriteID).Select(vertex => vertex.position).ToArray(),
                        indices = source.meshProvider.GetIndices(rect.spriteID), edges = source.meshProvider.GetEdges(rect.spriteID) }), rect.name);
                    var savedWeights = BoneWeights(source, rect.spriteID);
                    for (var i = 0; i < weights[id].Length; ++i)
                        if (!string.IsNullOrEmpty(weights[id][i])) Assert.AreEqual(weights[id][i], savedWeights[i], rect.name);
                }
                CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(originalPath));
                CollectionAssert.AreEqual(originalMeta, File.ReadAllBytes(originalPath + ".meta"));
            }
            finally { AssetDatabase.DeleteAsset(copy); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PsbCreationUsesEntryBindingPolicy(bool skeletonOnly)
        {
            var originalPath = AssetDatabase.GetAllAssetPaths().FirstOrDefault(path => path.StartsWith("Assets/") &&
                path.EndsWith(".psb", StringComparison.OrdinalIgnoreCase) && AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<Sprite>().Any(sprite => sprite.GetBones().Length > 0));
            if (originalPath == null) Assert.Ignore("No rigged PSB fixture is available.");
            var copy = AssetDatabase.GenerateUniqueAssetPath("Assets/UniskinSkeletonEntry.psb");
            Assert.IsTrue(AssetDatabase.CopyAsset(originalPath, copy));
            GameObject instance = null;
            try
            {
                var sprite = AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Sprite>().First(item => item.GetBones().Length > 0);
                var source = new SceneMeshSource(sprite);
                var characterProvider = source.provider.GetDataProvider<ICharacterDataProvider>();
                if (characterProvider == null) Assert.Ignore("The fixture has no shared character skeleton.");
                m_Session = ScriptableObject.CreateInstance<SceneBoneSession>();
                m_Session.Load(sprite, source, skeletonOnly);
                var bindings = m_Session.cache.character.parts.ToDictionary(part => part.sprite.id,
                    part => part.bones.Select(bone => bone.guid).ToArray());
                var weights = source.provider.GetSpriteRects().ToDictionary(rect => rect.spriteID.ToString(), rect => BoneWeights(source, rect.spriteID));
                var parent = m_Session.cache.selectedSprite.GetCharacterPart().bones.First(bone => !bone.IsConstraintParent());
                BoneCache added;
                using (m_Session.cache.UndoScope("Create entry-policy test bone"))
                {
                    added = m_Session.skeleton.CreateBone(parent, parent.endPosition, parent.endPosition + Vector3.right * 8, false, "UniskinUnboundTest");
                    m_Session.Changed(true);
                }
                var guid = added.guid;
                Assert.AreEqual(!skeletonOnly, m_Session.cache.selectedSprite.GetCharacterPart().bones.Contains(added));
                if (skeletonOnly)
                    foreach (var part in m_Session.cache.character.parts)
                        CollectionAssert.AreEqual(bindings[part.sprite.id], part.bones.Select(bone => bone.guid).ToArray());
                m_Session.Save(new SceneMeshSource(sprite));
                sprite = AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Sprite>().First(item => item.name == sprite.name);
                source = new SceneMeshSource(sprite);
                var character = source.provider.GetDataProvider<ICharacterDataProvider>().GetCharacterData();
                Assert.IsTrue(character.bones.Any(bone => bone.guid == guid));
                if (skeletonOnly)
                {
                    foreach (var part in character.parts)
                        CollectionAssert.AreEqual(bindings[part.spriteId], part.bones.Select(index => character.bones[index].guid).ToArray());
                    foreach (var rect in source.provider.GetSpriteRects())
                        CollectionAssert.AreEqual(weights[rect.spriteID.ToString()], BoneWeights(source, rect.spriteID));
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(copy);
                    Assert.IsNotNull(prefab);
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    var bone = instance.GetComponentsInChildren<Transform>(true).Single(transform => transform.name == "UniskinUnboundTest");
                    Assert.IsTrue(SceneBoneInspector.TryGetSource(bone, out var renderer, out var resolvedGuid));
                    Assert.AreEqual(copy, AssetDatabase.GetAssetPath(renderer.sprite));
                    Assert.AreEqual(guid, resolvedGuid);
                    Assert.IsTrue(SceneBoneInspector.TryGetSource(instance.transform, out renderer, out _));
                    Object.DestroyImmediate(m_Session);
                    m_Session = ScriptableObject.CreateInstance<SceneBoneSession>();
                    m_Session.Load(sprite, source, skeletonOnly: true);
                    Assert.IsTrue(m_Session.skeleton.bones.Any(item => item.guid == guid));
                    Assert.IsFalse(m_Session.dirty);
                    instance.transform.position = new Vector3(3, -2, 0);
                    instance.transform.rotation = Quaternion.Euler(0, 0, 23);
                    instance.transform.localScale = Vector3.one * 1.7f;
                    m_Session.CaptureScenePose(renderer, source.rect.rect);
                    var cachedBone = m_Session.skeleton.bones.Single(item => item.guid == guid);
                    var matrix = m_Session.SkeletonToWorld(renderer, source.rect.rect);
                    var localLength = character.bones.Single(item => item.guid == guid).length / source.provider.pixelsPerUnit;
                    Assert.Less(Vector3.Distance(bone.position, matrix.MultiplyPoint3x4(cachedBone.position)), .0001f);
                    Assert.Less(Vector3.Distance(bone.TransformPoint(Vector3.right * localLength), matrix.MultiplyPoint3x4(cachedBone.endPosition)), .0001f,
                        "Unbound source-pixel length must not be treated as a Unity-unit length when capturing the pose.");
                    Assert.IsFalse(m_Session.dirty);
                    bone.localRotation = Quaternion.Euler(0, 0, 61);
                    bone.localPosition += new Vector3(.15f, -.08f);
                    m_Session.RefreshScenePose(renderer, source.rect.rect);
                    Assert.Less(Vector3.Distance(bone.position, matrix.MultiplyPoint3x4(cachedBone.position)), .0001f);
                    Assert.Less(Vector3.Distance(bone.TransformPoint(Vector3.right * localLength), matrix.MultiplyPoint3x4(cachedBone.endPosition)), .0001f);
                    Assert.IsFalse(m_Session.dirty, "Animation of an unbound character bone must remain clean.");
                }
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(copy);
            }
        }

        [Serializable] sealed class GeometryRecord
        { public Vector2[] positions; public int[] indices; public Vector2Int[] edges; }

        static string CharacterMetadata(CharacterData character)
        {
            character.bones = Array.Empty<SpriteBone>();
            character.parts = character.parts.Select(part => { part.bones = Array.Empty<int>(); return part; }).ToArray();
            return JsonUtility.ToJson(character);
        }

        static string[] BoneWeights(SceneMeshSource source, GUID id)
        {
            var bones = source.provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(id);
            return source.meshProvider.GetVertices(id).Select(vertex => string.Join("|", Enumerable.Range(0, 4)
                .Where(i => vertex.boneWeight.GetWeight(i) > 0)
                .Select(i => bones[vertex.boneWeight.GetBoneIndex(i)].guid + ":" + vertex.boneWeight.GetWeight(i).ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                .OrderBy(value => value))).ToArray();
        }
    }
}
