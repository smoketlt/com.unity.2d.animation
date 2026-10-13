using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace UnityEditor.U2D.Animation.Tests
{
    internal class GeometryTriangulationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ClosingInteriorLoopPreservesCenterAndSpokes(bool centerFirst)
        {
            var mesh = CreateEyeMesh(centerFirst);
            var controller = new SpriteMeshDataController { spriteMeshData = mesh };
            controller.Triangulate(new Triangulator());
            var vertices = mesh.vertices.ToArray();
            var weights = mesh.vertexWeights.Select(weight => weight.ToBoneWeight(true)).ToArray();
            int offset = centerFirst ? 1 : 0;
            int center = centerFirst ? 0 : 8;

            controller.CreateEdge(4 + offset, 5 + offset);
            controller.Triangulate(new Triangulator());

            CollectionAssert.AreEqual(vertices, mesh.vertices);
            CollectionAssert.AreEqual(weights, mesh.vertexWeights.Select(weight => weight.ToBoneWeight(true)));
            Assert.AreEqual(12, mesh.edges.Length);
            Assert.IsTrue(HasEdge(mesh.edges, 4 + offset, 5 + offset));
            for (int i = 4; i < 8; ++i)
                Assert.IsTrue(HasEdge(mesh.edges, i + offset, center), "Missing center spoke " + i);
            AssertValidGeometry(mesh.vertices.Length, mesh.edges, mesh.indices);

            controller.Triangulate(new Triangulator());
            CollectionAssert.AreEqual(vertices, mesh.vertices);
            Assert.AreEqual(12, mesh.edges.Length);
        }

        [Test]
        public void ScheduledTriangulationPreservesClosedInteriorGraph()
        {
            var mesh = CreateEyeMesh(false);
            new SpriteMeshDataController { spriteMeshData = mesh }.CreateEdge(4, 5);
            var vertices = mesh.vertices.Select(vertex => (float2)vertex).ToArray();
            var expectedEdges = mesh.edges.ToArray();
            var outputVertices = new NativeArray<float2>(256, Allocator.TempJob);
            var outputEdges = new NativeArray<int2>(256, Allocator.TempJob);
            var outputIndices = new NativeArray<int>(256, Allocator.TempJob);
            var result = new NativeArray<int4>(1, Allocator.TempJob);
            try
            {
                new Triangulator().ScheduleTriangulate(vertices, expectedEdges, ref outputVertices,
                    ref outputIndices, ref outputEdges, ref result).Complete();

                Assert.AreEqual(vertices.Length, result[0].x);
                Assert.AreEqual(expectedEdges.Length, result[0].z);
                CollectionAssert.AreEqual(vertices, outputVertices.Take(result[0].x));
                var edges = outputEdges.Take(result[0].z).ToArray();
                foreach (var edge in expectedEdges)
                    Assert.IsTrue(HasEdge(edges, edge.x, edge.y));
                AssertValidGeometry(result[0].x, edges, outputIndices.Take(result[0].y).ToArray());
            }
            finally
            {
                result.Dispose();
                outputIndices.Dispose();
                outputEdges.Dispose();
                outputVertices.Dispose();
            }
        }

        [Test]
        public void PlanarGraphStillSplitsIntersectingEdges()
        {
            var vertices = new[] { new float2(0, 0), new float2(10, 0), new float2(10, 10), new float2(0, 10) };
            var edges = new[] { new int2(0, 1), new int2(1, 2), new int2(2, 3), new int2(3, 0), new int2(0, 2), new int2(1, 3) };

            new Triangulator().Triangulate(ref edges, ref vertices, out var indices);

            Assert.AreEqual(5, vertices.Length);
            Assert.AreEqual(new float2(5, 5), vertices[4]);
            Assert.AreEqual(8, edges.Length);
            for (int i = 0; i < 4; ++i)
                Assert.IsTrue(HasEdge(edges, i, 4));
            Assert.IsFalse(HasEdge(edges, 0, 2));
            Assert.IsFalse(HasEdge(edges, 1, 3));
            AssertValidGeometry(vertices.Length, edges, indices);
        }

        [Test]
        public void SafeTriangulationPreservesUnreferencedTrailingVertex()
        {
            var vertices = new[]
            {
                new float2(0, 0), new float2(10, 0), new float2(10, 10), new float2(0, 10),
                new float2(20, 20)
            };
            var expected = vertices.ToArray();
            var edges = new[] { new int2(0, 1), new int2(1, 2), new int2(2, 3), new int2(3, 0) };

            Assert.IsTrue(TriangulationUtility.TriangulateSafe(ref vertices, ref edges, out var indices));

            CollectionAssert.AreEqual(expected, vertices);
            Assert.AreEqual(4, edges.Length);
            Assert.AreEqual(6, indices.Length);
            Assert.IsFalse(indices.Contains(4));
            AssertValidGeometry(vertices.Length, edges, indices);
        }

        static SpriteMeshData CreateEyeMesh(bool centerFirst)
        {
            var mesh = new SpriteMeshData();
            mesh.SetFrame(new Rect(0, 0, 20, 20));
            var vertices = new[]
            {
                new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 20), new Vector2(0, 20),
                new Vector2(10, 14), new Vector2(14, 10), new Vector2(10, 6), new Vector2(6, 10)
            };
            if (centerFirst)
                mesh.AddVertex(new Vector2(10, 10), new BoneWeight { boneIndex0 = 3, weight0 = 1 });
            for (int i = 0; i < vertices.Length; ++i)
                mesh.AddVertex(vertices[i], new BoneWeight { boneIndex0 = i % 3, weight0 = 1 });
            if (!centerFirst)
                mesh.AddVertex(new Vector2(10, 10), new BoneWeight { boneIndex0 = 3, weight0 = 1 });
            int offset = centerFirst ? 1 : 0;
            int center = centerFirst ? 0 : 8;
            var controller = new SpriteMeshDataController { spriteMeshData = mesh };
            for (int i = 0; i < 4; ++i)
            {
                controller.CreateEdge(i + offset, (i + 1) % 4 + offset);
                controller.CreateEdge(i + 4 + offset, center);
                if (i != 0)
                    controller.CreateEdge(i + 4 + offset, (i + 1) % 4 + 4 + offset);
            }
            return mesh;
        }

        static bool HasEdge(int2[] edges, int a, int b) =>
            edges.Any(edge => (edge.x == a && edge.y == b) || (edge.x == b && edge.y == a));

        static void AssertValidGeometry(int vertexCount, int2[] edges, int[] indices)
        {
            Assert.Greater(indices.Length, 0);
            Assert.AreEqual(0, indices.Length % 3);
            Assert.IsTrue(indices.All(index => index >= 0 && index < vertexCount));
            Assert.IsTrue(edges.All(edge => edge.x >= 0 && edge.y >= 0 && edge.x < vertexCount && edge.y < vertexCount));
        }
    }
}
