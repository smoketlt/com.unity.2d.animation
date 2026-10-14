using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace UnityEditor.U2D.Animation.Tests
{
    internal class AutoWeightsGeneratorTests
    {
        [Serializable] sealed class Fixture { public Vector2[] vertices; public int[] indices; public Vector2[] points; public int[] bones; }

        internal static float Weight(BoneWeight value, int bone) =>
            (value.boneIndex0 == bone ? value.weight0 : 0) + (value.boneIndex1 == bone ? value.weight1 : 0) +
            (value.boneIndex2 == bone ? value.weight2 : 0) + (value.boneIndex3 == bone ? value.weight3 : 0);

        [Test]
        public void SparseTorsoProducesSmoothNearlySymmetricWeightsWithoutChangingInputs()
        {
            var package = PackageManager.PackageInfo.FindForAssembly(typeof(SceneWeightSession).Assembly);
            var fixture = JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.Combine(package.resolvedPath, "Tests/Editor/Data/AutoWeightsTorso.json")));
            var vertices = fixture.vertices.Select(v => new float2(v.x, v.y)).ToArray();
            var points = fixture.points.Select(v => new float2(v.x, v.y)).ToArray();
            var bones = new[] { new int2(0, 1), new int2(1, 2) };
            var vertexCopy = (float2[])vertices.Clone(); var indexCopy = (int[])fixture.indices.Clone(); var pointCopy = (float2[])points.Clone();
            var weights = new RefinedBiharmonicWeightsGenerator().Calculate("Torso regression", vertices, fixture.indices, Array.Empty<int2>(), points, bones, Array.Empty<int>());
            Assert.AreEqual(vertices.Length, weights.Length);
            foreach (var weight in weights)
            {
                Assert.That(weight.Sum(), Is.EqualTo(1).Within(.00001f));
                Assert.That(Weight(weight, 0), Is.InRange(0, 1)); Assert.That(Weight(weight, 1), Is.InRange(0, 1));
            }
            foreach (var pair in new[] { new int2(5,6), new int2(19,20), new int2(35,36), new int2(37,38), new int2(39,40), new int2(41,42) })
                Assert.That(Mathf.Abs(Weight(weights[pair.x], 0) - Weight(weights[pair.y], 0)), Is.LessThan(.05f), "Mirror pair " + pair);
            Assert.That(Weight(weights[5], 0), Is.GreaterThan(.95f));
            Assert.That(Weight(weights[6], 0), Is.GreaterThan(.95f));
            Assert.That(Weight(weights[4], 1), Is.GreaterThan(.9f));
            Assert.That(Weight(weights[7], 1), Is.GreaterThan(.9f));
            // The transfer follows the spine instead of collapsing to one bone.
            Assert.That(Weight(weights[39], 0), Is.GreaterThan(Weight(weights[37], 0)));
            Assert.That(Weight(weights[37], 0), Is.GreaterThan(Weight(weights[35], 0)));
            CollectionAssert.AreEqual(vertexCopy, vertices); CollectionAssert.AreEqual(indexCopy, fixture.indices); CollectionAssert.AreEqual(pointCopy, points);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SampleInsertionReplacesParentsAndSplitsBothSidesOfSharedEdges(bool onSharedEdge)
        {
            var vertices = new List<float2> { new float2(0,0), new float2(2,0), new float2(2,2), new float2(0,2) };
            var indices = new List<int> { 0,1,2, 0,2,3 };
            var point = onSharedEdge ? new float2(1,1) : new float2(1.5f,.5f);
            RefinedBiharmonicWeightsGenerator.InsertPoint(point, vertices, indices);
            Assert.AreEqual(5, vertices.Count); Assert.AreEqual(12, indices.Count);
            var area = 0f;
            for (var i = 0; i < indices.Count; i += 3)
            {
                var ab = vertices[indices[i+1]] - vertices[indices[i]]; var ac = vertices[indices[i+2]] - vertices[indices[i]];
                var triangleArea = (ab.x * ac.y - ab.y * ac.x) * .5f;
                Assert.That(triangleArea, Is.GreaterThan(0), "Keep winding and exclude zero-area child triangles."); area += triangleArea;
            }
            Assert.That(area, Is.EqualTo(4).Within(.00001f), "Overlapping parent triangles must not remain.");
            if (onSharedEdge)
                for (var i = 0; i < indices.Count; i += 3)
                    Assert.IsFalse(indices.Skip(i).Take(3).Contains(0) && indices.Skip(i).Take(3).Contains(2), "No T-junction on the shared diagonal.");
            var count = indices.Count;
            RefinedBiharmonicWeightsGenerator.InsertPoint(point, vertices, indices);
            RefinedBiharmonicWeightsGenerator.InsertPoint(vertices[0], vertices, indices);
            Assert.AreEqual(5, vertices.Count); Assert.AreEqual(count, indices.Count);
        }

        [Test]
        public void UntriangulatedMeshIsRejectedInsteadOfAssigningEverythingToTheFirstBone()
        {
            Assert.Throws<InvalidOperationException>(() => new RefinedBiharmonicWeightsGenerator().Calculate("empty",
                new[] { new float2(0,0), new float2(1,0), new float2(0,1) }, Array.Empty<int>(), Array.Empty<int2>(),
                new[] { new float2(0,0), new float2(1,0) }, new[] { new int2(0,1) }, Array.Empty<int>()));
        }
    }
}
