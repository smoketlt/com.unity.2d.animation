using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UnityEditor.U2D.Animation.Tests
{
    internal class GeometryDeletionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void DeletingIslandPreservesMainContour(bool interleaved)
        {
            var mesh = new SpriteMeshData();
            mesh.SetFrame(new Rect(0, 0, 12, 10));
            var main = new[]
            {
                new Vector2(0, 0), new Vector2(8, 0), new Vector2(10, 4),
                new Vector2(8, 8), new Vector2(0, 8), new Vector2(-2, 4)
            };
            var island = new[]
            {
                new Vector2(10, 5), new Vector2(11, 5),
                new Vector2(11, 6), new Vector2(10, 6)
            };
            int[] mainIds = interleaved ? new[] { 0, 2, 4, 6, 8, 9 } : new[] { 0, 1, 2, 3, 4, 5 };
            int[] islandIds = interleaved ? new[] { 1, 3, 5, 7 } : new[] { 6, 7, 8, 9 };
            var vertices = new Vector2[10];
            for (int i = 0; i < main.Length; ++i)
                vertices[mainIds[i]] = main[i];
            for (int i = 0; i < island.Length; ++i)
                vertices[islandIds[i]] = island[i];
            foreach (var vertex in vertices)
                mesh.AddVertex(vertex, new BoneWeight { boneIndex0 = 0, weight0 = 1 });

            var controller = new SpriteMeshDataController { spriteMeshData = mesh };
            for (int i = 0; i < mainIds.Length; ++i)
                controller.CreateEdge(mainIds[i], mainIds[(i + 1) % mainIds.Length]);
            for (int i = 0; i < islandIds.Length; ++i)
                controller.CreateEdge(islandIds[i], islandIds[(i + 1) % islandIds.Length]);
            mesh.SetIndices(new[]
            {
                mainIds[0], mainIds[1], mainIds[2], mainIds[0], mainIds[2], mainIds[3],
                mainIds[0], mainIds[3], mainIds[4], mainIds[0], mainIds[4], mainIds[5],
                islandIds[0], islandIds[1], islandIds[2], islandIds[0], islandIds[2], islandIds[3]
            });

            controller.RemoveVertex(islandIds);

            CollectionAssert.AreEqual(main, mesh.vertices);
            Assert.AreEqual(6, mesh.edges.Length);
            for (int i = 0; i < 6; ++i)
                Assert.IsTrue(HasEdge(mesh, i, (i + 1) % 6));
            Assert.IsTrue(mesh.vertexWeights.All(weight => Mathf.Approximately(weight.Sum(), 1)));

            controller.Triangulate(new Triangulator());
            CollectionAssert.AreEquivalent(main, mesh.vertices);
            Assert.AreEqual(12, mesh.indices.Length);
            Assert.IsTrue(mesh.indices.All(index => index >= 0 && index < 6));
        }

        [Test]
        public void DeletingAdjacentBoundaryVerticesStitchesSurvivingNeighbors()
        {
            var mesh = new SpriteMeshData();
            foreach (var vertex in new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(2, 0),
                new Vector2(3, 0), new Vector2(3, 3), new Vector2(0, 3), new Vector2(1, 1)
            })
                mesh.AddVertex(vertex, default(BoneWeight));
            var controller = new SpriteMeshDataController { spriteMeshData = mesh };
            for (int i = 0; i < 6; ++i)
            {
                controller.CreateEdge(i, (i + 1) % 6);
                controller.CreateEdge(i, 6);
            }
            mesh.SetIndices(new[] { 0, 1, 6, 1, 2, 6, 2, 3, 6, 3, 4, 6, 4, 5, 6, 5, 0, 6 });

            controller.RemoveVertex(new[] { 1, 2 });

            Assert.IsTrue(HasEdge(mesh, 0, 1));
            Assert.IsTrue(HasEdge(mesh, 1, 2));
            Assert.IsTrue(HasEdge(mesh, 2, 3));
            Assert.IsTrue(HasEdge(mesh, 3, 0));
            Assert.AreEqual(8, mesh.edges.Length);
            Assert.IsTrue(mesh.edges.All(edge => edge.x >= 0 && edge.y >= 0 && edge.x < 5 && edge.y < 5));
        }

        static bool HasEdge(BaseSpriteMeshData mesh, int a, int b)
        {
            return mesh.edges.Any(edge => (edge.x == a && edge.y == b) || (edge.x == b && edge.y == a));
        }
    }
}
