using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation.Tests
{
    internal class UnityCompatibilityTests
    {
        [Test]
        public void GpuBoneOffsetsUsePackedRangesAndClearInactiveSkins()
        {
            using (var active = new NativeArray<bool>(new[] { true, false, true }, Allocator.Temp))
            using (var data = new NativeArray<PerSkinJobData>(new[]
            {
                new PerSkinJobData { bindPosesIndex = new int2(0, 2) },
                new PerSkinJobData { bindPosesIndex = new int2(2, 2) },
                new PerSkinJobData { bindPosesIndex = new int2(2, 5) }
            }, Allocator.Temp))
            using (var offsets = new NativeArray<int>(3, Allocator.Temp))
            {
                GpuDeformationSystem.FillBoneTransformIndices(active, data, offsets);
                CollectionAssert.AreEqual(new[] { 0, -1, 2 }, offsets.ToArray());
            }
        }

        [Test]
        public void GeometryToolbarResourcesRetainForkCommands()
        {
            var toolbar = MeshToolbar.GenerateFromUXML();
            Assert.IsNotNull(toolbar);
            Assert.AreEqual("Modify", toolbar.Q<Button>("SelectGeometry").Q<Label>().text);
            Assert.AreEqual("Create", toolbar.Q<Button>("CreateVertex").Q<Label>().text);
            Assert.AreEqual("New", toolbar.Q<Button>("CreateEdge").Q<Label>().text);
            Assert.AreEqual("Reset", toolbar.Q<Button>("SplitEdge").Q<Label>().text);
            Assert.AreEqual("Generate", toolbar.Q<Button>("GenerateGeometry").Q<Label>().text);
        }

        [Test]
        public void QuadTriangulationWorksWithResolvedCommonPackage()
        {
            var vertices = new[] { new float2(0, 0), new float2(1, 0), new float2(1, 1), new float2(0, 1) };
            var edges = new[] { new int2(0, 1), new int2(1, 2), new int2(2, 3), new int2(3, 0) };
            TriangulationUtility.Triangulate(ref edges, ref vertices, out var indices, Allocator.Temp);
            Assert.AreEqual(6, indices.Length);
            foreach (var index in indices)
                Assert.That(index, Is.InRange(0, vertices.Length - 1));
        }

        [Test]
        public void ObjectIdentityRoundTripsThroughEditor()
        {
            var go = new GameObject("Compatibility identity test");
            try
            {
                Assert.AreSame(go, UnityEditorObjectCompatibility.FindObject(go.GetObjectId()));
                Assert.AreNotEqual(go.GetObjectId(), go.transform.GetObjectId());
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TransformCacheRetainsIdentityAndReferenceCounts()
        {
            var go = new GameObject("Compatibility transform test");
            var cache = new TransformAccessJob();
            try
            {
                go.transform.position = new Vector3(2, 3, 4);
                var id = go.transform.GetObjectId();
                cache.AddTransform(go.transform);
                cache.AddTransform(go.transform);
                cache.StartLocalToWorldJob().Complete();
                Assert.AreEqual(2, cache.transformData[id].refCount);
                var position = cache.transformMatrix[cache.transformData[id].transformIndex].c3;
                Assert.AreEqual(2f, position.x);
                Assert.AreEqual(3f, position.y);
                Assert.AreEqual(4f, position.z);
                cache.RemoveTransformById(id);
                Assert.AreEqual(1, cache.transformData[id].refCount);
                cache.RemoveTransformById(id);
                Assert.IsFalse(cache.transformData.ContainsKey(id));
            }
            finally
            {
                cache.Destroy();
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void VertexBuffersDoNotTruncate64BitKeys()
        {
            var manager = ScriptableObject.CreateInstance<BufferManager>();
            try
            {
                var a = manager.GetBuffer(0x100000001UL, 16);
                var b = manager.GetBuffer(0x200000001UL, 32);
                Assert.AreNotSame(a, b);
                Assert.AreEqual(16, a.Length);
                Assert.AreEqual(32, b.Length);
                manager.ReturnBuffer(0x100000001UL);
                Assert.AreEqual(32, manager.GetBuffer(0x200000001UL, 32).Length);
            }
            finally { Object.DestroyImmediate(manager); }
        }

        [Test]
        public void ToolbarCheckedStateCanBeSetAndCleared()
        {
            var button = new Button();
            button.SetAnimationChecked(true);
            Assert.IsTrue(button.IsAnimationChecked());
            button.SetAnimationChecked(false);
            Assert.IsFalse(button.IsAnimationChecked());
        }

        [Test]
        public void VisibilityRowsPreserveCacheObjectIdentity()
        {
            var cacheObject = ScriptableObject.CreateInstance<BaseObject>();
            try
            {
                var id = cacheObject.GetObjectId();
                var row = new TreeViewItemBase<BaseObject>(id, 0, "test", cacheObject);
                Assert.AreEqual(id, row.id);
                Assert.AreSame(cacheObject, BaseObject.InstanceIDToObject(row.id));
            }
            finally { Object.DestroyImmediate(cacheObject); }
        }
    }
}
