using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Animation.Tests
{
    internal class SceneWeightEditingTests
    {
        string m_Path;
        Texture2D m_Texture;
        SceneWeightSession m_Session;
        SceneMeshSource Source() => new SceneMeshSource(AssetDatabase.LoadAssetAtPath<Sprite>(m_Path));

        [SetUp]
        public void Setup()
        {
            m_Path = AssetDatabase.GenerateUniqueAssetPath("Assets/UniskinWeightRoundTrip.png");
            m_Texture = new Texture2D(32, 32);
            m_Texture.SetPixels(Enumerable.Repeat(Color.white, 1024).ToArray()); m_Texture.Apply();
            File.WriteAllBytes(m_Path, m_Texture.EncodeToPNG());
            AssetDatabase.ImportAsset(m_Path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(m_Path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single; importer.SaveAndReimport();
            var source = Source();
            source.provider.GetDataProvider<ISpriteBoneDataProvider>().SetBones(source.rect.spriteID, Enumerable.Range(0, 5).Select(i => new SpriteBone
            { name = "Bone" + i, guid = GUID.Generate().ToString(), parentId = -1, position = new Vector3(4 + i * 4, 4), rotation = Quaternion.identity, length = 20, color = Color.HSVToRGB(i / 5f, 1, 1) }).ToList());
            source.provider.Apply(); source.importer.SaveAndReimport();
            source = Source();
            var mesh = source.ReadMesh(AssetDatabase.LoadAssetAtPath<Sprite>(m_Path));
            foreach (var weight in mesh.vertexWeights)
            {
                weight.Clear(); weight.AddChannel(0, .4f, true); weight.AddChannel(1, .3f, true); weight.AddChannel(2, .2f, true); weight.AddChannel(3, .1f, true);
            }
            source.WriteMesh(mesh);
            m_Session = ScriptableObject.CreateInstance<SceneWeightSession>();
            m_Session.Load(AssetDatabase.LoadAssetAtPath<Sprite>(m_Path), Source());
        }

        [TearDown]
        public void Cleanup()
        {
            if (m_Session != null) Object.DestroyImmediate(m_Session);
            if (!string.IsNullOrEmpty(m_Path)) AssetDatabase.DeleteAsset(m_Path);
            if (m_Texture != null) Object.DestroyImmediate(m_Texture);
        }

        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public void BindCompletionRefreshesCachesAndOnlyFirstBindUpdatesReference(bool firstBind, bool escape)
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(SceneEditWeightsWindow);
            var host = new GameObject("Weight Bind Test");
            var window = ScriptableObject.CreateInstance<SceneEditWeightsWindow>();
            var sessionField = type.GetField("m_Session", fields);
            Object.DestroyImmediate((SceneWeightSession)sessionField.GetValue(window));
            sessionField.SetValue(window, m_Session);
            type.GetField("m_DiscardOnClose", fields).SetValue(window, true);
            try
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(m_Path);
                var renderer = host.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
                var bones = sprite.GetBones();
                var transforms = bones.Select((bone, i) =>
                {
                    var transform = new GameObject(bone.name).transform;
                    transform.SetParent(host.transform, false);
                    transform.localPosition = new Vector3(1 + i * .2f, .5f);
                    transform.localRotation = Quaternion.Euler(0, 0, 20 + i * 10);
                    return transform;
                }).ToArray();
                var skin = host.AddComponent<SpriteSkin>(); skin.SetRootBone(host.transform); skin.SetBoneTransforms(transforms);
                type.GetField("m_Renderer", fields).SetValue(window, renderer);
                type.GetField("m_Source", fields).SetValue(window, Source());
                type.GetField("m_Preview", fields).SetValue(window, true);
                var target = m_Session.mesh.bones.Last();
                m_Session.mesh.bones = firstBind ? Array.Empty<BoneCache>() : m_Session.mesh.bones.Where(bone => bone != target).ToArray();
                var positions = (Vector2[])m_Session.mesh.vertices.Clone();
                var sourceFingerprint = Source().Fingerprint();
                type.GetMethod("SetBindMode", fields).Invoke(window, new object[] { true });
                Assert.IsTrue((bool)type.GetField("m_Bind", fields).GetValue(window));
                m_Session.Bind(target);
                type.GetField("m_RowKey", fields).SetValue(window, "stale rows");
                var liveBindings = (System.Collections.IDictionary)type.GetField("m_LiveBoneBindings", fields).GetValue(window);
                var characterBindings = (System.Collections.IDictionary)type.GetField("m_CharacterLiveBindings", fields).GetValue(window);
                liveBindings["stale"] = (host.transform, 99f);
                characterBindings["stale"] = (host.transform, 99f);
                type.GetField("m_CharacterBindingsLoaded", fields).SetValue(window, true);
                if (escape) type.GetMethod("Cancel", fields).Invoke(window, null);
                else type.GetMethod("SetBindMode", fields).Invoke(window, new object[] { false });
                Assert.IsFalse((bool)type.GetField("m_Bind", fields).GetValue(window));
                Assert.IsTrue((bool)type.GetField("m_Preview", fields).GetValue(window), "Bind must not toggle Preview.");
                Assert.IsNull(type.GetField("m_RowKey", fields).GetValue(window));
                Assert.IsFalse(liveBindings.Contains("stale"));
                Assert.IsFalse(characterBindings.Contains("stale"));
                Assert.IsTrue(liveBindings.Contains(target.guid), "Refresh must resolve the bound scene Transform.");
                Assert.IsTrue(m_Session.mesh.ContainsBone(target));
                Assert.AreEqual(firstBind, m_Session.bindingPose != null);
                CollectionAssert.AreEqual(positions, m_Session.mesh.vertices);
                if (firstBind)
                {
                    CollectionAssert.AreEqual(positions, m_Session.bindingPose.texturePositions);
                    var world = (Vector3[])type.GetMethod("WorldVertices", fields).Invoke(window, new object[] { false });
                    var toWorld = renderer.transform.localToWorldMatrix * SceneMeshBindingPose.PixelsToLocal(sprite, Source().rect.rect);
                    for (var i = 0; i < world.Length; ++i)
                        Assert.That(Vector3.Distance(world[i], toWorld.MultiplyPoint3x4(positions[i])), Is.LessThan(.0001f));
                }
                Assert.AreEqual(sourceFingerprint, Source().Fingerprint(), "Bind completion must remain staged.");
            }
            finally
            {
                sessionField.SetValue(window, null);
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(host);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ManualUpdateBindingsUsesSceneBonesWithoutAnEnabledSpriteSkin(bool disabledSkin)
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(SceneEditWeightsWindow);
            var host = new GameObject("Weight Binding Without Skin Test");
            var window = ScriptableObject.CreateInstance<SceneEditWeightsWindow>();
            var sessionField = type.GetField("m_Session", fields);
            Object.DestroyImmediate((SceneWeightSession)sessionField.GetValue(window));
            sessionField.SetValue(window, m_Session);
            type.GetField("m_DiscardOnClose", fields).SetValue(window, true);
            try
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(m_Path);
                var renderer = host.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
                var transforms = sprite.GetBones().Select((bone, i) =>
                {
                    var transform = new GameObject(bone.name).transform;
                    transform.SetParent(host.transform, false);
                    transform.localPosition = new Vector3(1 + i * .2f, .5f);
                    return transform;
                }).ToArray();
                if (disabledSkin)
                {
                    var skin = host.AddComponent<SpriteSkin>(); skin.enabled = false;
                    skin.SetRootBone(host.transform); skin.SetBoneTransforms(transforms);
                }
                type.GetField("m_Renderer", fields).SetValue(window, renderer);
                type.GetField("m_Source", fields).SetValue(window, Source());
                var positions = (Vector2[])m_Session.mesh.vertices.Clone();
                var weights = m_Session.mesh.vertexWeights.Select(weight => JsonUtility.ToJson(weight)).ToArray();
                var fingerprint = Source().Fingerprint();
                type.GetMethod("UpdateBindings", fields, null, Type.EmptyTypes, null).Invoke(window, null);
                Assert.IsNotNull(m_Session.bindingPose);
                for (var i = 0; i < positions.Length; ++i)
                    Assert.That(Vector2.Distance(positions[i], m_Session.mesh.vertices[i]), Is.LessThan(.0001f));
                CollectionAssert.AreEqual(positions, m_Session.bindingPose.texturePositions);
                CollectionAssert.AreEqual(weights, m_Session.mesh.vertexWeights.Select(weight => JsonUtility.ToJson(weight)).ToArray());
                Assert.AreEqual(fingerprint, Source().Fingerprint());
                Object.DestroyImmediate(transforms[0].gameObject);
                var snapshot = JsonUtility.ToJson(m_Session);
                var error = Assert.Throws<TargetInvocationException>(() =>
                    type.GetMethod("UpdateBindings", fields, null, Type.EmptyTypes, null).Invoke(window, null));
                Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
                StringAssert.Contains("No scene Transform", error.InnerException.Message);
                Assert.AreEqual(snapshot, JsonUtility.ToJson(m_Session));
            }
            finally
            {
                sessionField.SetValue(window, null);
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(host);
            }
        }

        [TestCase(-.05f)]
        [TestCase(.05f)]
        [TestCase(.0125f)]
        public void WeightStepPreservesMixedValuesAndUnselectedVerticesAndSupportsUndo(float amount)
        {
            m_Session.vertices.elements = new[] { 0 }; m_Session.SetRow(0, .2f);
            m_Session.vertices.elements = new[] { 1 }; m_Session.SetRow(0, .6f);
            m_Session.vertices.elements = new[] { 0, 1 };
            m_Session.SelectBone(m_Session.mesh.bones[0], false);
            var untouched = m_Session.mesh.vertexWeights.Skip(2).Select(weight => JsonUtility.ToJson(weight)).ToArray();
            var fingerprint = Source().Fingerprint();
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            m_Session.OffsetSelectedWeights(m_Session.selectedBones, amount);
            Assert.That(m_Session.Values(0)[0], Is.EqualTo(.2f + amount).Within(.00001f));
            Assert.That(m_Session.Values(1)[0], Is.EqualTo(.6f + amount).Within(.00001f));
            foreach (var vertex in new[] { 0, 1 })
            {
                Assert.That(m_Session.Values(vertex).Sum(), Is.EqualTo(1).Within(.00001f));
                Assert.That(m_Session.mesh.vertexWeights[vertex].Count(channel => channel.enabled && channel.weight > 0), Is.LessThanOrEqualTo(4));
            }
            CollectionAssert.AreEqual(untouched, m_Session.mesh.vertexWeights.Skip(2).Select(weight => JsonUtility.ToJson(weight)).ToArray());
            Assert.AreEqual(fingerprint, Source().Fingerprint());
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); m_Session.context.RestoreAfterUndo();
            Assert.That(m_Session.Values(0)[0], Is.EqualTo(.2f).Within(.00001f));
            Assert.That(m_Session.Values(1)[0], Is.EqualTo(.6f).Within(.00001f));
        }

        [Test]
        public void WeightStepHonorsLocksClampsAndEmptySelection()
        {
            m_Session.vertices.elements = new[] { 0, 1 };
            m_Session.ToggleLock(m_Session.mesh.bones[1]);
            m_Session.OffsetSelectedWeights(new[] { 0 }, 1);
            Assert.That(m_Session.Values(0)[0], Is.EqualTo(.7f).Within(.00001f));
            Assert.That(m_Session.Values(0)[1], Is.EqualTo(.3f).Within(.00001f));
            m_Session.OffsetSelectedWeights(new[] { 0 }, -1);
            Assert.That(m_Session.Values(0)[0], Is.Zero.Within(.00001f));
            Assert.That(m_Session.Values(0)[1], Is.EqualTo(.3f).Within(.00001f));
            var before = JsonUtility.ToJson(m_Session.mesh);
            m_Session.OffsetSelectedWeights(new[] { 1 }, .1f);
            m_Session.OffsetSelectedWeights(new[] { 0 }, float.NaN);
            m_Session.OffsetSelectedWeights(new[] { 0 }, 0);
            Assert.AreEqual(before, JsonUtility.ToJson(m_Session.mesh));
            m_Session.vertices.Clear();
            m_Session.OffsetSelectedWeights(new[] { 0 }, .1f);
            Assert.AreEqual(before, JsonUtility.ToJson(m_Session.mesh), "No vertex selection must not edit the whole mesh.");
        }

        [Test]
        public void WeightStepControlsRequireOneUnlockedBoneAndSelectedVertices()
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(SceneEditWeightsWindow);
            var host = new GameObject("Weight Step Controls Test");
            var window = ScriptableObject.CreateInstance<SceneEditWeightsWindow>();
            var sessionField = type.GetField("m_Session", fields);
            Object.DestroyImmediate((SceneWeightSession)sessionField.GetValue(window));
            sessionField.SetValue(window, m_Session);
            type.GetField("m_DiscardOnClose", fields).SetValue(window, true);
            type.GetField("m_SettingsLoaded", fields).SetValue(window, false);
            type.GetField("m_WeightStep", fields).SetValue(window, 5f);
            try
            {
                var renderer = host.AddComponent<SpriteRenderer>(); renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(m_Path);
                type.GetField("m_Renderer", fields).SetValue(window, renderer);
                type.GetField("m_Source", fields).SetValue(window, Source());
                window.CreateGUI();
                window.ShowPopup();
                var minus = window.rootVisualElement.Q<Button>("weightDecrease");
                var plus = window.rootVisualElement.Q<Button>("weightIncrease");
                var step = window.rootVisualElement.Q<FloatField>("weightStep");
                Assert.IsFalse(minus.enabledSelf, "Initial minus"); Assert.IsFalse(plus.enabledSelf, "Initial plus");
                m_Session.vertices.elements = new[] { 0, 1 };
                m_Session.SelectBone(m_Session.mesh.bones[0], false);
                type.GetMethod("UpdateUI", fields).Invoke(window, null);
                Assert.IsTrue(minus.enabledSelf); Assert.IsTrue(plus.enabledSelf);
                step.value = 12.5f;
                type.GetMethod("OffsetSelectedWeights", fields).Invoke(window, new object[] { -1f });
                Assert.That(m_Session.Values(0)[0], Is.EqualTo(.275f).Within(.00001f));
                Assert.That(m_Session.Values(1)[0], Is.EqualTo(.275f).Within(.00001f));
                Assert.That(m_Session.Values(2)[0], Is.EqualTo(.4f).Within(.00001f));
                m_Session.SelectBone(m_Session.mesh.bones[1], true);
                type.GetMethod("UpdateUI", fields).Invoke(window, null);
                Assert.IsFalse(plus.enabledSelf, "Multiple selected bones");
                m_Session.SelectBone(m_Session.mesh.bones[0], false, false);
                m_Session.ToggleLock(m_Session.mesh.bones[0]);
                type.GetMethod("UpdateUI", fields).Invoke(window, null);
                Assert.IsFalse(plus.enabledSelf, "Locked selected bone");
                m_Session.ToggleLock(m_Session.mesh.bones[0]);
                step.value = -1;
                Assert.AreEqual(0, step.value); Assert.IsFalse(plus.enabledSelf, "Zero step");
                step.value = float.NaN;
                Assert.AreEqual(0, step.value);
            }
            finally
            {
                sessionField.SetValue(window, null);
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(host);
            }
        }

        [TestCase(50f, false, false, 0f, .015f)]
        [TestCase(50f, true, false, 0f, -.015f)]
        [TestCase(50f, false, true, 0f, -.015f)]
        [TestCase(100f, false, false, 0f, .03f)]
        [TestCase(.5f, false, false, 0f, .00015f)]
        [TestCase(50f, false, false, 75f, .0075f)]
        [TestCase(0f, false, false, 0f, 0f)]
        public void BrushStampsUseGentleStrengthWithRemoveAltAndFeather(float strength, bool remove, bool alt, float distance, float expected)
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(SceneEditWeightsWindow);
            var window = ScriptableObject.CreateInstance<SceneEditWeightsWindow>();
            var sessionField = type.GetField("m_Session", fields);
            Object.DestroyImmediate((SceneWeightSession)sessionField.GetValue(window));
            sessionField.SetValue(window, m_Session);
            type.GetField("m_DiscardOnClose", fields).SetValue(window, true);
            type.GetField("m_SettingsLoaded", fields).SetValue(window, false);
            try
            {
                type.GetField("m_Strength", fields).SetValue(window, strength);
                type.GetField("m_RemoveMode", fields).SetValue(window, remove);
                type.GetField("m_Feather", fields).SetValue(window, 50f);
                m_Session.vertices.elements = new[] { 0 };
                m_Session.SelectBone(m_Session.mesh.bones[0], false);
                var points = Enumerable.Repeat(new Vector2(1000, 1000), m_Session.mesh.vertexCount).ToArray(); points[0] = new Vector2(distance, 0);
                type.GetMethod("Stamp", fields).Invoke(window, new object[] { points, Vector2.zero, 100f, alt });
                Assert.That(m_Session.Values(0)[0], Is.EqualTo(.4f + expected).Within(.000001f));
                Assert.That(m_Session.Values(1)[0], Is.EqualTo(.4f).Within(.000001f));
                Assert.That(m_Session.Values(0).Sum(), Is.EqualTo(1).Within(.000001f));
            }
            finally
            {
                m_Session.cache.EndUndoOperation();
                sessionField.SetValue(window, null);
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void EachWeightModeRemembersStrengthAndMigratesLegacyBrushStrength()
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(SceneEditWeightsWindow);
            var key = (string)type.GetProperty("PreferencesKey", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var hadPreferences = EditorPrefs.HasKey(key); var previousPreferences = EditorPrefs.GetString(key);
            var host = new GameObject("Weight Mode Strength Test");
            var window = ScriptableObject.CreateInstance<SceneEditWeightsWindow>();
            var sessionField = type.GetField("m_Session", fields);
            Object.DestroyImmediate((SceneWeightSession)sessionField.GetValue(window));
            sessionField.SetValue(window, m_Session);
            type.GetField("m_DiscardOnClose", fields).SetValue(window, true);
            type.GetField("m_SettingsLoaded", fields).SetValue(window, false);
            try
            {
                var renderer = host.AddComponent<SpriteRenderer>(); renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(m_Path);
                type.GetField("m_Renderer", fields).SetValue(window, renderer); type.GetField("m_Source", fields).SetValue(window, Source());
                type.GetField("m_Direct", fields).SetValue(window, false); type.GetField("m_RemoveMode", fields).SetValue(window, false);
                type.GetField("m_Strength", fields).SetValue(window, 50f);
                type.GetField("m_DirectStrength", fields).SetValue(window, 100f);
                type.GetField("m_AddStrength", fields).SetValue(window, 50f); type.GetField("m_RemoveStrength", fields).SetValue(window, 50f);
                window.CreateGUI(); window.ShowPopup();
                var mode = window.rootVisualElement.Q<PopupField<string>>("weightMode"); var strength = window.rootVisualElement.Q<Slider>("strength");
                mode.value = "Direct"; strength.value = 33.25f;
                mode.value = "Remove"; strength.value = 12.5f;
                mode.value = "Add"; strength.value = 27.75f;
                mode.value = "Direct"; Assert.AreEqual(33.25f, strength.value);
                mode.value = "Remove"; Assert.AreEqual(12.5f, strength.value);
                mode.value = "Add"; Assert.AreEqual(27.75f, strength.value);
                window.position = new Rect(100, 100, 400, 450);
                type.GetField("m_SettingsLoaded", fields).SetValue(window, true);
                type.GetMethod("SaveWindowPreferences", fields).Invoke(window, null);
                type.GetField("m_SettingsLoaded", fields).SetValue(window, false);
                type.GetField("m_AddStrength", fields).SetValue(window, 0f);
                type.GetMethod("LoadWindowPreferences", fields).Invoke(window, null);
                Assert.AreEqual(33.25f, type.GetField("m_DirectStrength", fields).GetValue(window));
                Assert.AreEqual(12.5f, type.GetField("m_RemoveStrength", fields).GetValue(window));
                Assert.AreEqual(27.75f, type.GetField("m_AddStrength", fields).GetValue(window));
                EditorPrefs.SetString(key, "{\"strength\":0.5,\"direct\":false,\"remove\":false}");
                type.GetMethod("LoadWindowPreferences", fields).Invoke(window, null);
                Assert.AreEqual(.5f, type.GetField("m_DirectStrength", fields).GetValue(window));
                Assert.AreEqual(50f, type.GetField("m_AddStrength", fields).GetValue(window));
                Assert.AreEqual(50f, type.GetField("m_RemoveStrength", fields).GetValue(window));
                Assert.AreEqual(50f, type.GetField("m_Strength", fields).GetValue(window));
            }
            finally
            {
                type.GetField("m_SettingsLoaded", fields).SetValue(window, false);
                sessionField.SetValue(window, null);
                Object.DestroyImmediate(window); Object.DestroyImmediate(host);
                if (hadPreferences) EditorPrefs.SetString(key, previousPreferences); else EditorPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void SwapIsStagedSelectedAndUndoable()
        {
            var original = Source().Fingerprint();
            m_Session.vertices.elements = new[] { 0 };
            m_Session.SelectBone(m_Session.mesh.bones[0], false); m_Session.SelectBone(m_Session.mesh.bones[1], true);
            m_Session.Swap(); Undo.FlushUndoRecordObjects();
            Assert.AreEqual(.3f, m_Session.Values(0)[0]); Assert.AreEqual(.4f, m_Session.Values(0)[1]);
            Assert.AreEqual(.4f, m_Session.Values(1)[0]); Assert.AreEqual(original, Source().Fingerprint());
            Assert.IsTrue(m_Session.dirty);
            Undo.PerformUndo(); m_Session.context.RestoreAfterUndo();
            Assert.AreEqual(.4f, m_Session.Values(0)[0]); Assert.IsFalse(m_Session.dirty);
        }

        [Test]
        public void RowEditingPreservesLocksAndFourInfluences()
        {
            m_Session.vertices.elements = new[] { 0 };
            m_Session.ToggleLock(m_Session.mesh.bones[0]);
            m_Session.SetRow(4, .5f);
            Assert.AreEqual(.4f, m_Session.Values(0)[0]); Assert.That(m_Session.Values(0)[4], Is.GreaterThan(0));
            Assert.That(m_Session.mesh.vertexWeights[0].Count(channel => channel.enabled && channel.weight > 0), Is.LessThanOrEqualTo(4));
            Assert.That(m_Session.Values(0).Sum(), Is.EqualTo(1).Within(.00001f));
            Assert.AreEqual(0, m_Session.Values(1)[4]);
        }

        [Test]
        public void AutoUsesSelectedBonesAndVertices()
        {
            m_Session.vertices.elements = new[] { 0 };
            m_Session.SelectBone(m_Session.mesh.bones[4], false);
            m_Session.Auto();
            Assert.That(m_Session.Values(0)[4], Is.EqualTo(1).Within(.00001f));
            Assert.AreEqual(.4f, m_Session.Values(1)[0]); Assert.AreEqual(0, m_Session.Values(1)[4]);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void DirectCanIntroduceZeroWeightBoneWithFourExistingInfluences(int lockCount)
        {
            const float amount = .0003625f; // One pixel at the reported 14.5% strength.
            var before = m_Session.Values(0);
            var untouched = JsonUtility.ToJson(m_Session.mesh.vertexWeights[1]);
            m_Session.vertices.elements = new[] { 0 };
            m_Session.SelectBone(m_Session.mesh.bones[4], false);
            for (var i = 0; i < lockCount; ++i) m_Session.ToggleLock(m_Session.mesh.bones[i]);
            using (m_Session.cache.UndoScope("Drag Vertex Weights"))
            {
                foreach (var vertex in m_Session.targets) m_Session.Adjust(vertex, m_Session.selectedBones, amount);
                m_Session.Changed();
            }
            Undo.FlushUndoRecordObjects();
            var after = m_Session.Values(0);
            var expected = lockCount == 4 ? 0 : lockCount == 3 ? 1 - before.Take(3).Sum() : amount;
            Assert.That(after[4], Is.EqualTo(expected).Within(.000001f));
            for (var i = 0; i < lockCount; ++i) Assert.AreEqual(before[i], after[i]);
            Assert.That(after.Sum(), Is.EqualTo(1).Within(.00001f));
            Assert.That(after.Count(value => value > 0), Is.LessThanOrEqualTo(4));
            Assert.AreEqual(untouched, JsonUtility.ToJson(m_Session.mesh.vertexWeights[1]));
            if (lockCount == 4) { Assert.IsFalse(m_Session.dirty); return; }
            Assert.IsTrue(m_Session.dirty);
            Undo.PerformUndo(); m_Session.context.RestoreAfterUndo();
            CollectionAssert.AreEqual(before, m_Session.Values(0));
            Assert.IsFalse(m_Session.dirty);
        }

        [Test]
        public void DirectSmallStepsAccumulateAndReverseWithoutSliderInitialization()
        {
            m_Session.SelectBone(m_Session.mesh.bones[4], false);
            for (var i = 0; i < 10; ++i) m_Session.Adjust(0, m_Session.selectedBones, .001f);
            Assert.That(m_Session.Values(0)[4], Is.EqualTo(.01f).Within(.000001f));
            m_Session.Adjust(0, m_Session.selectedBones, -.004f);
            Assert.That(m_Session.Values(0)[4], Is.EqualTo(.006f).Within(.000001f));
            Assert.That(m_Session.Values(0).Sum(), Is.EqualTo(1).Within(.00001f));
        }

        [Test]
        public void RowCanSetSmallWeightOnFifthBoneExactly()
        {
            m_Session.vertices.elements = new[] { 0 };
            m_Session.SetRow(4, .001f);
            Assert.That(m_Session.Values(0)[4], Is.EqualTo(.001f).Within(.000001f));
            Assert.That(m_Session.Values(0)[3], Is.Zero);
            Assert.That(m_Session.Values(0)[0] / m_Session.Values(0)[1], Is.EqualTo(4f / 3f).Within(.00001f));
            Assert.That(m_Session.Values(0).Sum(), Is.EqualTo(1).Within(.00001f));
        }

        [Test]
        public void DirectReservesSlotsForMultipleSelectedBones()
        {
            m_Session.Adjust(0, new[] { 0, 4 }, .001f);
            Assert.That(m_Session.Values(0)[0], Is.EqualTo(.401f).Within(.000001f));
            Assert.That(m_Session.Values(0)[4], Is.EqualTo(.001f).Within(.000001f));
            Assert.That(m_Session.Values(0)[3], Is.Zero);
            Assert.That(m_Session.Values(0).Sum(), Is.EqualTo(1).Within(.00001f));
            Assert.That(m_Session.Values(0).Count(value => value > 0), Is.EqualTo(4));
        }

        [Test]
        public void RowReversingFullTransferRestoresPreviousDonor()
        {
            m_Session.vertices.elements = new[] { 0 };
            m_Session.SetRow(3, 1);
            m_Session.SetRow(4, 1);
            Assert.That(m_Session.Values(0)[4], Is.EqualTo(1));
            m_Session.SetRow(4, 0);
            Assert.That(m_Session.Values(0)[3], Is.EqualTo(1));
            Assert.That(m_Session.Values(0).Count(value => value > 0), Is.EqualTo(1));
            Assert.That(m_Session.Values(1)[3], Is.EqualTo(.1f));
        }

        [Test]
        public void DirectReversingAtFullWeightRestoresDonorProportions()
        {
            var weight = m_Session.mesh.vertexWeights[0];
            weight.Clear(); weight.AddChannel(2, .6f, true); weight.AddChannel(3, .4f, true);
            m_Session.Adjust(0, new[] { 4 }, 1);
            m_Session.Adjust(0, new[] { 4 }, .1f); // Continue upward at the clamp.
            m_Session.Adjust(0, new[] { 4 }, -.001f);
            Assert.That(m_Session.Values(0)[2], Is.EqualTo(.0006f).Within(.000001f));
            Assert.That(m_Session.Values(0)[3], Is.EqualTo(.0004f).Within(.000001f));
            m_Session.Adjust(0, new[] { 4 }, -1);
            Assert.That(m_Session.Values(0)[2], Is.EqualTo(.6f).Within(.00001f));
            Assert.That(m_Session.Values(0)[3], Is.EqualTo(.4f).Within(.00001f));
            Assert.That(m_Session.Values(0)[0], Is.Zero);
            Assert.That(m_Session.Values(0)[1], Is.Zero);
        }

        [Test]
        public void FullTransferRemembersDonorsIndependentlyForEachVertex()
        {
            m_Session.vertices.elements = new[] { 0 }; m_Session.SetRow(3, 1);
            m_Session.vertices.elements = new[] { 1 }; m_Session.SetRow(2, 1);
            m_Session.vertices.elements = new[] { 0, 1 };
            m_Session.SetRow(4, 1); m_Session.SetRow(4, 0);
            Assert.That(m_Session.Values(0)[3], Is.EqualTo(1));
            Assert.That(m_Session.Values(1)[2], Is.EqualTo(1));
        }

        [Test]
        public void FullTransferDonorHistorySurvivesUndoAndRedo()
        {
            m_Session.vertices.elements = new[] { 0 }; m_Session.SetRow(3, 1);
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            m_Session.SetRow(4, 1);
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            m_Session.SetRow(4, .5f);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo(); m_Session.context.RestoreAfterUndo();
            Assert.That(m_Session.Values(0)[4], Is.EqualTo(1));
            Undo.PerformRedo(); m_Session.context.RestoreAfterUndo();
            Assert.That(m_Session.Values(0)[3], Is.EqualTo(.5f).Within(.00001f));
            Undo.PerformUndo(); m_Session.context.RestoreAfterUndo();
            m_Session.SetRow(4, 0);
            Assert.That(m_Session.Values(0)[3], Is.EqualTo(1));
        }

        [Test]
        public void SwapDiscardsPreviousManualDonorHistory()
        {
            m_Session.vertices.elements = new[] { 0 }; m_Session.SetRow(3, 1); m_Session.SetRow(4, 1);
            m_Session.SelectBone(m_Session.mesh.bones[2], false);
            m_Session.SelectBone(m_Session.mesh.bones[4], true);
            m_Session.Swap();
            m_Session.SetRow(4, .5f); m_Session.SetRow(4, 1); m_Session.SetRow(4, 0);
            Assert.That(m_Session.Values(0)[2], Is.EqualTo(1));
            Assert.That(m_Session.Values(0)[3], Is.Zero);
        }

        [Test]
        public void RemovePreservesSkeletonAndGeometryAndPersistsBindings()
        {
            var source = Source();
            var beforeBones = JsonUtility.ToJson(new Bones { bones = source.provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(source.rect.spriteID).ToArray() });
            var beforeVertices = source.meshProvider.GetVertices(source.rect.spriteID).Select(vertex => vertex.position).ToArray();
            var indices = source.meshProvider.GetIndices(source.rect.spriteID).ToArray(); var edges = source.meshProvider.GetEdges(source.rect.spriteID).ToArray();
            source.importer.userData = "OtherTool:preserve";
            Object.DestroyImmediate(m_Session); m_Session = ScriptableObject.CreateInstance<SceneWeightSession>(); m_Session.Load(AssetDatabase.LoadAssetAtPath<Sprite>(m_Path), Source());
            var removed = m_Session.mesh.bones[0].guid;
            m_Session.SelectBone(m_Session.mesh.bones[0], false); m_Session.Remove();
            Assert.AreEqual(5, m_Session.context.skeleton.boneCount); Assert.AreEqual(4, m_Session.mesh.boneCount);
            foreach (var weight in m_Session.mesh.vertexWeights) Assert.That(weight.Sum(), Is.EqualTo(1).Within(.00001f));
            m_Session.Save(Source()); source = Source();
            Assert.IsTrue(source.importer.userData.StartsWith("OtherTool:preserve"));
            Assert.AreEqual(beforeBones, JsonUtility.ToJson(new Bones { bones = source.provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(source.rect.spriteID).ToArray() }));
            CollectionAssert.AreEqual(beforeVertices, source.meshProvider.GetVertices(source.rect.spriteID).Select(vertex => vertex.position));
            CollectionAssert.AreEqual(indices, source.meshProvider.GetIndices(source.rect.spriteID)); CollectionAssert.AreEqual(edges, source.meshProvider.GetEdges(source.rect.spriteID));
            Object.DestroyImmediate(m_Session); m_Session = ScriptableObject.CreateInstance<SceneWeightSession>(); m_Session.Load(AssetDatabase.LoadAssetAtPath<Sprite>(m_Path), source);
            Assert.IsFalse(m_Session.mesh.bones.Any(bone => bone.guid == removed));
            m_Session.Bind(m_Session.context.skeleton.bones.First(bone => bone.guid == removed));
            Assert.AreEqual(5, m_Session.mesh.boneCount);
        }

        [Serializable] sealed class Bones { public SpriteBone[] bones; }

        [Test]
        public void SmoothUsesSharedSnapshotAlgorithmAndSelection()
        {
            m_Session.vertices.elements = new[] { 0 };
            m_Session.SetRow(0, 1);
            var mesh = m_Session.mesh;
            var neighbors = WeightSmoothing.BuildNeighbors(mesh);
            var source = mesh.vertexWeights.Select(weight => weight.ToBoneWeight(false)).ToArray();
            var expected = EditableBoneWeightUtility.CreateFromBoneWeight(source[0]);
            WeightSmoothing.SmoothVertex(expected, source[0], source, neighbors[0], Enumerable.Range(0, mesh.boneCount).ToList(), m_Session.lockedBones);
            var untouched = JsonUtility.ToJson(mesh.vertexWeights[1]);
            m_Session.Smooth();
            Assert.AreEqual(JsonUtility.ToJson(expected), JsonUtility.ToJson(mesh.vertexWeights[0]));
            Assert.AreEqual(untouched, JsonUtility.ToJson(mesh.vertexWeights[1]));
        }

        [Test]
        public void PsbBindingsAndWeightsPreserveOtherLayersAndCharacterSkeleton()
        {
            var original = AssetDatabase.GetAllAssetPaths().FirstOrDefault(path => path.StartsWith("Assets/") && path.EndsWith(".psb", StringComparison.OrdinalIgnoreCase) &&
                AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Any(sprite => sprite.GetBones().Length > 1));
            if (original == null) Assert.Ignore("No rigged PSB fixture is available.");
            var bytes = File.ReadAllBytes(original); var meta = File.ReadAllBytes(original + ".meta");
            var copy = AssetDatabase.GenerateUniqueAssetPath("Assets/UniskinWeightPsbRoundTrip.psb");
            Assert.IsTrue(AssetDatabase.CopyAsset(original, copy));
            try
            {
                var sprite = AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Sprite>().First(item => item.GetBones().Length > 1 && new SceneMeshSource(item).hasAuthoredMesh);
                var source = new SceneMeshSource(sprite); var id = source.rect.spriteID;
                var character = source.provider.GetDataProvider<ICharacterDataProvider>().GetCharacterData();
                var originalCharacterBones = JsonUtility.ToJson(new Bones { bones = character.bones });
                var geometry = source.provider.GetSpriteRects().ToDictionary(rect => rect.spriteID, rect => JsonUtility.ToJson(new Geometry
                    { positions = source.meshProvider.GetVertices(rect.spriteID).Select(vertex => vertex.position).ToArray(), indices = source.meshProvider.GetIndices(rect.spriteID), edges = source.meshProvider.GetEdges(rect.spriteID) }));
                var otherWeights = source.provider.GetSpriteRects().Where(rect => rect.spriteID != id).ToDictionary(rect => rect.spriteID, rect => JsonUtility.ToJson(new Weights
                    { vertices = source.meshProvider.GetVertices(rect.spriteID), bones = source.provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(rect.spriteID).ToArray() }));
                Object.DestroyImmediate(m_Session); m_Session = ScriptableObject.CreateInstance<SceneWeightSession>(); m_Session.Load(sprite, source);
                var removed = m_Session.mesh.bones[0].guid;
                m_Session.SelectBone(m_Session.mesh.bones[0], false); m_Session.Remove();
                m_Session.Save(new SceneMeshSource(sprite));
                sprite = AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Sprite>().First(item => item.GetSpriteID() == id);
                source = new SceneMeshSource(sprite);
                Assert.AreEqual(originalCharacterBones, JsonUtility.ToJson(new Bones { bones = source.provider.GetDataProvider<ICharacterDataProvider>().GetCharacterData().bones }));
                foreach (var rect in source.provider.GetSpriteRects())
                {
                    Assert.AreEqual(geometry[rect.spriteID], JsonUtility.ToJson(new Geometry
                        { positions = source.meshProvider.GetVertices(rect.spriteID).Select(vertex => vertex.position).ToArray(), indices = source.meshProvider.GetIndices(rect.spriteID), edges = source.meshProvider.GetEdges(rect.spriteID) }), rect.name);
                    if (rect.spriteID != id) Assert.AreEqual(otherWeights[rect.spriteID], JsonUtility.ToJson(new Weights
                        { vertices = source.meshProvider.GetVertices(rect.spriteID), bones = source.provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(rect.spriteID).ToArray() }), rect.name);
                }
                Object.DestroyImmediate(m_Session); m_Session = ScriptableObject.CreateInstance<SceneWeightSession>(); m_Session.Load(sprite, source);
                Assert.IsFalse(m_Session.mesh.bones.Any(bone => bone.guid == removed));
                m_Session.Bind(m_Session.context.skeleton.bones.First(bone => bone.guid == removed));
                m_Session.vertices.elements = new[] { 0 }; m_Session.SetRow(Array.FindIndex(m_Session.mesh.bones, bone => bone.guid == removed), .8f);
                m_Session.Save(new SceneMeshSource(sprite));
                sprite = AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Sprite>().First(item => item.GetSpriteID() == id);
                Object.DestroyImmediate(m_Session); m_Session = ScriptableObject.CreateInstance<SceneWeightSession>(); m_Session.Load(sprite, new SceneMeshSource(sprite));
                Assert.That(m_Session.Values(0)[Array.FindIndex(m_Session.mesh.bones, bone => bone.guid == removed)], Is.EqualTo(.8f).Within(.00001f));
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(original)); CollectionAssert.AreEqual(meta, File.ReadAllBytes(original + ".meta"));
            }
            finally { AssetDatabase.DeleteAsset(copy); }
        }

        [Serializable] sealed class Geometry { public Vector2[] positions; public int[] indices; public Vector2Int[] edges; }
        [Serializable] sealed class Weights { public Vertex2DMetaData[] vertices; public SpriteBone[] bones; }

        [Test]
        public void LocalDrawOrderSortsTrianglesWithoutChangingBoneDepthsAndSupportsUndo()
        {
            var mesh = m_Session.mesh;
            Assert.That(mesh.vertexCount, Is.GreaterThanOrEqualTo(4));
            for (var vertex = 0; vertex < mesh.vertexCount; ++vertex)
            {
                mesh.vertexWeights[vertex].Clear();
                mesh.vertexWeights[vertex].AddChannel(vertex == 2 || vertex == 3 ? 1 : 0, 1, true);
            }
            var topology = new[] { 0, 1, 2, 1, 3, 2 }; mesh.SetIndices(topology);
            var depths = mesh.bones.Select(bone => bone.depth).ToArray();
            var weights = mesh.vertexWeights.Select(weight => JsonUtility.ToJson(weight)).ToArray();
            var source = Source().Fingerprint();
            m_Session.MoveBone(mesh.bones[0], 2); Undo.FlushUndoRecordObjects();
            CollectionAssert.AreEqual(new[] { 1, 3, 2, 0, 1, 2 }, m_Session.DrawIndices());
            CollectionAssert.AreEqual(depths, mesh.bones.Select(bone => bone.depth));
            CollectionAssert.AreEqual(weights, mesh.vertexWeights.Select(weight => JsonUtility.ToJson(weight)));
            CollectionAssert.AreEqual(topology, mesh.indices);
            Assert.AreEqual(source, Source().Fingerprint());
            Undo.PerformUndo(); m_Session.InvalidateDrawOrder(); m_Session.context.RestoreAfterUndo();
            CollectionAssert.AreEqual(Enumerable.Range(0, mesh.boneCount), m_Session.orderedBoneIndices);
            CollectionAssert.AreEqual(topology, m_Session.DrawIndices());
        }

        [Test]
        public void LocalDrawOrderSurvivesSaveReloadWithoutChangingSkeleton()
        {
            var source = Source();
            var skeleton = JsonUtility.ToJson(new Bones { bones = source.provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(source.rect.spriteID).ToArray() });
            var positions = source.meshProvider.GetVertices(source.rect.spriteID).Select(vertex => vertex.position).ToArray();
            var edges = source.meshProvider.GetEdges(source.rect.spriteID);
            m_Session.MoveBone(m_Session.mesh.bones[0], m_Session.mesh.boneCount);
            var order = m_Session.orderedBoneIndices.Select(i => m_Session.mesh.bones[i].guid).ToArray();
            var triangles = m_Session.DrawIndices();
            m_Session.Save(Source());
            source = Source();
            Assert.AreEqual(skeleton, JsonUtility.ToJson(new Bones { bones = source.provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(source.rect.spriteID).ToArray() }));
            CollectionAssert.AreEqual(positions, source.meshProvider.GetVertices(source.rect.spriteID).Select(vertex => vertex.position));
            CollectionAssert.AreEqual(edges, source.meshProvider.GetEdges(source.rect.spriteID));
            CollectionAssert.AreEqual(triangles, source.meshProvider.GetIndices(source.rect.spriteID));
            Object.DestroyImmediate(m_Session); m_Session = ScriptableObject.CreateInstance<SceneWeightSession>();
            m_Session.Load(AssetDatabase.LoadAssetAtPath<Sprite>(m_Path), source);
            CollectionAssert.AreEqual(order, m_Session.orderedBoneIndices.Select(i => m_Session.mesh.bones[i].guid));
            Assert.IsFalse(m_Session.dirty);
        }

        [Test]
        public void SaveRejectsExternalChanges()
        {
            m_Session.SetRow(0, .8f);
            var source = Source(); source.importer.userData = "External";
            Assert.Throws<InvalidOperationException>(() => m_Session.Save(Source()));
        }

        static EditableBoneWeight WeldWeight(params float[] values)
        {
            var weight = new EditableBoneWeight();
            for (var bone = 0; bone < values.Length; ++bone)
                if (values[bone] > 0) weight.AddChannel(bone, values[bone], true);
            return weight;
        }

        [Test]
        public void WeldCharacterPreservesSourceSelectionLocksAndSupportsUndoAndSave()
        {
            var original = AssetDatabase.GetAllAssetPaths().FirstOrDefault(path => path.StartsWith("Assets/") &&
                path.EndsWith(".psb", StringComparison.OrdinalIgnoreCase) && AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Count() > 1);
            if (original == null) Assert.Ignore("No multilayer PSB fixture is available.");
            var copy = AssetDatabase.GenerateUniqueAssetPath("Assets/UniskinWeldRoundTrip.psb");
            Assert.IsTrue(AssetDatabase.CopyAsset(original, copy));
            try
            {
                SpriteCache sourceSprite = null;
                foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Sprite>())
                {
                    var source = new SceneMeshSource(sprite);
                    if (!source.hasAuthoredMesh) continue;
                    Object.DestroyImmediate(m_Session); m_Session = ScriptableObject.CreateInstance<SceneWeightSession>();
                    m_Session.Load(sprite, source);
                    sourceSprite = m_Session.mesh.boneCount >= 3 ? m_Session.weldSources.FirstOrDefault() : null;
                    if (sourceSprite != null) break;
                }
                if (sourceSprite == null) Assert.Ignore("The PSB fixture has no pair with two common bound bones.");
                var mesh = m_Session.mesh; var sourceMesh = sourceSprite.GetMesh();
                var sourceBones = sourceMesh.bones;
                var common = mesh.bones.Select((bone, index) => (index, source: Array.FindIndex(sourceBones, item => item.guid == bone.guid)))
                    .Where(pair => pair.source >= 0).Take(2).ToArray();
                foreach (var weight in sourceMesh.vertexWeights)
                { weight.Clear(); weight.AddChannel(common[0].source, 1, true); }
                mesh.vertexWeights[0].Clear();
                mesh.vertexWeights[0].AddChannel(common[0].index, .5f, true);
                mesh.vertexWeights[0].AddChannel(common[1].index, .25f, true);
                mesh.vertexWeights[0].AddChannel(Enumerable.Range(0, mesh.boneCount)
                    .First(bone => bone != common[0].index && bone != common[1].index), .25f, true);
                m_Session.ToggleLock(mesh.bones[common[1].index]);
                m_Session.vertices.elements = new[] { 0 };
                var untouched = mesh.vertexWeights.Skip(1).Select(JsonUtility.ToJson).ToArray();
                var sourceWeights = sourceMesh.vertexWeights.Select(JsonUtility.ToJson).ToArray();
                var positions = (Vector2[])mesh.vertices.Clone(); var indices = (int[])mesh.indices.Clone();
                var savedSource = new SceneMeshSource(AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Sprite>()
                    .First(sprite => sprite.GetSpriteID().ToString() == sourceSprite.id)).Fingerprint();
                var id = m_Session.context.spriteId;
                m_Session.Weld(sourceSprite, false); Undo.FlushUndoRecordObjects();
                Assert.That(m_Session.Values(0)[common[0].index], Is.EqualTo(.75f).Within(.00001f));
                Assert.That(m_Session.Values(0)[common[1].index], Is.EqualTo(.25f).Within(.00001f));
                CollectionAssert.AreEqual(untouched, mesh.vertexWeights.Skip(1).Select(JsonUtility.ToJson));
                CollectionAssert.AreEqual(sourceWeights, sourceMesh.vertexWeights.Select(JsonUtility.ToJson));
                CollectionAssert.AreEqual(positions, mesh.vertices); CollectionAssert.AreEqual(indices, mesh.indices);
                CollectionAssert.AreEqual(new[] { 0 }, m_Session.vertices.elements);
                Undo.PerformUndo(); m_Session.context.RestoreAfterUndo();
                Assert.That(m_Session.Values(0)[common[0].index], Is.EqualTo(.5f).Within(.00001f));
                Undo.PerformRedo(); m_Session.context.RestoreAfterUndo();
                Assert.That(m_Session.Values(0)[common[0].index], Is.EqualTo(.75f).Within(.00001f));
                var target = AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Sprite>().First(sprite => sprite.GetSpriteID().ToString() == id);
                var expected = m_Session.Values(0);
                m_Session.Save(new SceneMeshSource(target));
                Assert.AreEqual(savedSource, new SceneMeshSource(AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Sprite>()
                    .First(sprite => sprite.GetSpriteID().ToString() == sourceSprite.id)).Fingerprint());
                Object.DestroyImmediate(m_Session); m_Session = ScriptableObject.CreateInstance<SceneWeightSession>();
                target = AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Sprite>().First(sprite => sprite.GetSpriteID().ToString() == id);
                m_Session.Load(target, new SceneMeshSource(target));
                CollectionAssert.AreEqual(expected, m_Session.Values(0));
            }
            finally { AssetDatabase.DeleteAsset(copy); }
        }

        [Test]
        public void WeldInterpolatesAndRemapsBoneOrder()
        {
            var values = new float[2];
            var positions = new[] { Vector2.zero, new Vector2(2, 0), new Vector2(0, 2) };
            var weights = new[] { WeldWeight(1, 0), WeldWeight(0, 1), WeldWeight(0, 1) };
            Assert.IsTrue(WeightWelding.Sample(new Vector2(.5f, .5f), positions, new[] { 0, 1, 2 }, weights,
                new[] { 1, 0 }, true, values));
            Assert.That(values[0], Is.EqualTo(.5f).Within(.00001f));
            Assert.That(values[1], Is.EqualTo(.5f).Within(.00001f));
            Assert.AreEqual(1, weights[0].Sum());
        }

        [Test]
        public void WeldAllProjectsOutsideVerticesWhileOverlappingSkipsThem()
        {
            var positions = new[] { Vector2.zero, new Vector2(2, 0), new Vector2(0, 2) };
            var weights = new[] { WeldWeight(1, 0), WeldWeight(0, 1), WeldWeight(1, 0) };
            var values = new float[2];
            Assert.IsFalse(WeightWelding.Sample(new Vector2(1, -1), positions, new[] { 0, 1, 2 }, weights, new[] { 0, 1 }, true, values));
            CollectionAssert.AreEqual(new[] { 0f, 0f }, values);
            Assert.IsTrue(WeightWelding.Sample(new Vector2(1, -1), positions, new[] { 0, 1, 2 }, weights, new[] { 0, 1 }, false, values));
            CollectionAssert.AreEqual(new[] { .5f, .5f }, values);
        }

        [Test]
        public void WeldSkipsDegenerateTrianglesAndUnmappedWeights()
        {
            var positions = new[] { Vector2.zero, Vector2.right, Vector2.up };
            var weights = new[] { WeldWeight(1), WeldWeight(1), WeldWeight(1) };
            Assert.IsFalse(WeightWelding.Sample(Vector2.zero, positions, new[] { 0, 0, 0 }, weights, new[] { 0 }, false, new float[1]));
            Assert.IsFalse(WeightWelding.Sample(Vector2.zero, positions, new[] { 0, 1, 2 }, weights, new[] { -1 }, false, new float[1]));
        }

        [Test]
        public void WeldRejectsNonCharacterSourceWithoutMutations()
        {
            var weights = m_Session.mesh.vertexWeights.Select(JsonUtility.ToJson).ToArray();
            Assert.IsEmpty(m_Session.weldSources);
            Assert.Throws<InvalidOperationException>(() => m_Session.Weld(m_Session.cache.selectedSprite, false));
            CollectionAssert.AreEqual(weights, m_Session.mesh.vertexWeights.Select(JsonUtility.ToJson));
            Assert.IsFalse(m_Session.dirty);
        }

        [Test]
        public void WindowResourcesMatchRequestedControls()
        {
            var tree = ResourceLoader.Load<VisualTreeAsset>("SkinningModule/SceneEditWeightsWindow.uxml").CloneTree();
            foreach (var name in new[] { "strength", "size", "feather", "pieces", "overlay", "selected", "bind", "swap", "remove", "smooth", "auto", "weld", "bones", "updateBindings", "weightDecrease", "weightIncrease", "weightStep" }) Assert.IsNotNull(tree.Q(name), name);
            Assert.AreEqual(5, tree.Q<FloatField>("weightStep").value);
            foreach (var name in new[] { "strength", "size", "feather" }) Assert.IsInstanceOf<Slider>(tree.Q(name), name);
            Assert.IsNull(tree.Q("influence")); Assert.IsNull(tree.Q("update"));
            foreach (var icon in new[] { "Weights", "Bind", "Smooth", "UpdateBind" }) Assert.IsNotNull(ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/" + icon + ".png"));
            var update = tree.Q<Button>("updateBindings");
            Assert.AreEqual("Update Bindings", update.text);
            Assert.AreSame(tree.Q<Button>("remove").parent.parent, update.parent.parent);
            Assert.Greater(update.parent.parent.IndexOf(update.parent), update.parent.parent.IndexOf(tree.Q<Button>("remove").parent));
            var shader = ResourceLoader.Load<Shader>("SkinningModule/SceneWeightsPreview.shader"); Assert.IsNotNull(shader); Assert.IsFalse(ShaderUtil.ShaderHasError(shader));
        }

        [Test]
        public void UpdateBindingsOnOrdinarySpritePreservesTextureAndBoneIdentityAfterReimport()
        {
            var source = Source(); var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(m_Path);
            var skeleton = source.provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(source.rect.spriteID).Select(bone => JsonUtility.ToJson(bone)).ToArray();
            var uv = sprite.uv;
            var toLocal = SceneMeshBindingPose.PixelsToLocal(sprite, source.rect.rect);
            var poses = sprite.GetBindPoses();
            var boneMatrices = poses.Select((pose, i) => Matrix4x4.TRS(new Vector3(i * .07f, i * -.02f, 0), Quaternion.Euler(0, 0, i * 13), Vector3.one) * pose.inverse).ToArray();
            var bindings = boneMatrices.Select(matrix => Matrix4x4.Scale(Vector3.one * source.provider.pixelsPerUnit) * matrix.inverse * toLocal).ToArray();
            var positions = m_Session.mesh.vertices.Select((position, vertex) =>
            {
                var point = Vector3.zero;
                foreach (var channel in m_Session.mesh.vertexWeights[vertex])
                    if (channel.enabled && channel.weight > 0)
                        point += (boneMatrices[channel.boneIndex] * poses[channel.boneIndex] * toLocal).MultiplyPoint3x4(position) * channel.weight;
                return (Vector2)toLocal.inverse.MultiplyPoint3x4(point);
            }).ToArray();
            var weights = m_Session.mesh.vertexWeights.Select(JsonUtility.ToJson).ToArray();
            m_Session.UpdateBindings(positions, bindings);
            CollectionAssert.AreEqual(weights, m_Session.mesh.vertexWeights.Select(JsonUtility.ToJson));
            m_Session.SetRow(4, 1);
            m_Session.Save(source);
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(m_Path);
            for (var i = 0; i < uv.Length; ++i) Assert.That(Vector2.Distance(uv[i], sprite.uv[i]), Is.LessThan(.00001f));
            var posed = boneMatrices[4] * sprite.GetBindPoses()[4];
            for (var i = 0; i < positions.Length; ++i)
                Assert.That(Vector3.Distance(toLocal.MultiplyPoint3x4(positions[i]), posed.MultiplyPoint3x4(sprite.vertices[i])), Is.LessThan(.00001f));
            CollectionAssert.AreEqual(skeleton, Source().provider.GetDataProvider<ISpriteBoneDataProvider>().GetBones(Source().rect.spriteID).Select(bone => JsonUtility.ToJson(bone)));
            Object.DestroyImmediate(m_Session); m_Session = ScriptableObject.CreateInstance<SceneWeightSession>(); m_Session.Load(sprite, Source());
            Assert.IsFalse(m_Session.dirty); Assert.NotNull(m_Session.bindingPose);
            m_Session.SelectBone(m_Session.mesh.bones[0], false); m_Session.Remove(); m_Session.Save(Source());
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(m_Path);
            for (var i = 0; i < bindings.Length; ++i)
                for (var c = 0; c < 16; ++c)
                    Assert.That(sprite.GetBindPoses()[i][c], Is.EqualTo(boneMatrices[i].inverse[c]).Within(.0001f));
        }

        [Test]
        public void UpdateBindingsRejectsSingularPoseWithoutChangingStagedData()
        {
            var positions = (Vector2[])m_Session.mesh.vertices.Clone();
            var weights = m_Session.mesh.vertexWeights.Select(JsonUtility.ToJson).ToArray();
            var matrices = Enumerable.Repeat(Matrix4x4.identity, m_Session.mesh.boneCount).ToArray();
            matrices[1] = Matrix4x4.zero;
            Assert.Throws<InvalidOperationException>(() => m_Session.UpdateBindings(positions, matrices));
            CollectionAssert.AreEqual(positions, m_Session.mesh.vertices);
            CollectionAssert.AreEqual(weights, m_Session.mesh.vertexWeights.Select(JsonUtility.ToJson));
            Assert.IsNull(m_Session.bindingPose); Assert.IsFalse(m_Session.dirty);
        }
    }
}
