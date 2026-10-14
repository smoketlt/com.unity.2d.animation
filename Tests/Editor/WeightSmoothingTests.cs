using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace UnityEditor.U2D.Animation.Tests
{
    internal class WeightSmoothingTests
    {
        static MeshCache CreateFan()
        {
            var mesh = new MeshCache();
            mesh.SetVertices(new[] { new Vector2(-1, -1), new Vector2(1, -1),
                new Vector2(1, 1), new Vector2(-1, 1), Vector2.zero },
                Enumerable.Range(0, 5).Select(i =>
                    EditableBoneWeightUtility.CreateFromBoneWeight(new BoneWeight
                    { boneIndex0 = i == 4 ? 1 : 0, weight0 = 1 })).ToArray());
            mesh.SetEdges(new[] { new int2(0, 1), new int2(1, 2), new int2(2, 3), new int2(3, 0) });
            mesh.SetIndices(new[] { 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4 });
            return mesh;
        }

        [Test]
        public void TriangleOnlyInteriorVertexSmoothsFromSnapshot()
        {
            var mesh = CreateFan();
            var neighbors = WeightSmoothing.BuildNeighbors(mesh);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, neighbors[4].Select(n => n.vertexIndex));
            var source = mesh.vertexWeights.Select(w => w.ToBoneWeight(false)).ToArray();
            // Simulate an earlier target changing during the same pass.
            mesh.vertexWeights[0] = EditableBoneWeightUtility.CreateFromBoneWeight(source[4]);
            Assert.IsTrue(WeightSmoothing.SmoothVertex(mesh.vertexWeights[4], source[4], source,
                neighbors[4], new List<int> { 0, 1 }, new HashSet<int>()));
            Assert.That(mesh.vertexWeights[4].Single(w => w.boneIndex == 1).weight, Is.EqualTo(.55f).Within(.00001f));
            Assert.That(mesh.vertexWeights[4].Sum(), Is.EqualTo(1).Within(.00001f));
            Assert.That(mesh.vertexWeights[4].Single(w => w.boneIndex == 0).weight, Is.EqualTo(.45f).Within(.00001f));
        }

        [Test]
        public void SharedTriangleAuthoredAndOutlineEdgesAreCountedOnce()
        {
            var mesh = CreateFan();
            mesh.SetEdges(mesh.edges.Concat(new[] { new int2(0, 4), new int2(4, 0), new int2(0, 1) }).ToArray());
            var neighbors = WeightSmoothing.BuildNeighbors(mesh);
            for (int i = 0; i < neighbors.Length; ++i)
                Assert.AreEqual(neighbors[i].Count, neighbors[i].Select(n => n.vertexIndex).Distinct().Count());
            CollectionAssert.AreEquivalent(new[] { 1, 3, 4 }, neighbors[0].Select(n => n.vertexIndex));
            Assert.That(neighbors[0].Single(n => n.vertexIndex == 1).weight, Is.EqualTo(.5f));
            Assert.That(neighbors[0].Single(n => n.vertexIndex == 4).weight, Is.EqualTo(1 / Mathf.Sqrt(2)).Within(.00001f));
        }

        [Test]
        public void AuthoredConnectionsOutsideTrianglesRemainAvailable()
        {
            var mesh = CreateFan();
            mesh.AddVertex(new Vector2(0, 2), new BoneWeight { boneIndex0 = 1, weight0 = 1 });
            mesh.AddVertex(new Vector2(0, 3), new BoneWeight { boneIndex0 = 1, weight0 = 1 });
            mesh.SetEdges(mesh.edges.Concat(new[] { new int2(4, 5) }).ToArray());
            var neighbors = WeightSmoothing.BuildNeighbors(mesh);
            CollectionAssert.AreEqual(new[] { 4 }, neighbors[5].Select(n => n.vertexIndex));
            Assert.IsEmpty(neighbors[6], "Disconnected vertices must not acquire spatial neighbors.");
        }

        [Test]
        public void CoincidentConnectedVerticesHaveFiniteNeighborWeights()
        {
            var mesh = CreateFan();
            mesh.AddVertex(Vector2.zero, new BoneWeight { boneIndex0 = 0, weight0 = 1 });
            mesh.SetEdges(new[] { new int2(4, 5), new int2(-1, 4), new int2(4, 6), new int2(4, 4) });
            var neighbors = WeightSmoothing.BuildNeighbors(mesh);
            Assert.AreEqual(5, neighbors[4].Count);
            Assert.That(neighbors[4].Single(n => n.vertexIndex == 5).weight, Is.EqualTo(10000f));
            Assert.IsTrue(neighbors.SelectMany(n => n).All(n => !float.IsNaN(n.weight) && !float.IsInfinity(n.weight)));
        }
    }
}
