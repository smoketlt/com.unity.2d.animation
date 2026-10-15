using NUnit.Framework;
using UnityEngine;

namespace UnityEditor.U2D.Animation.Tests
{
    internal class SceneMeshTargetPickingTests
    {
        static readonly Vector2[] k_Vertices =
        {
            new Vector2(0, 0), new Vector2(100, 0),
            new Vector2(100, 100), new Vector2(0, 100)
        };
        static readonly int[] k_Indices = { 0, 1, 2, 0, 2, 3 };

        [TestCase(50, -12, true)]
        [TestCase(50, 0, true)]
        [TestCase(50, 12, true)]
        [TestCase(50, -13, false)]
        [TestCase(-8, -8, true)]
        [TestCase(-9, -9, false)]
        [TestCase(50, 50, false)] // Shared triangle diagonal is not a boundary.
        public void PaddingProtectsBothSidesAndCornersButNotInternalEdges(float x, float y, bool expected)
        {
            Assert.AreEqual(expected, SceneMeshTargetPicking.NearBoundary(new Vector2(x, y),
                k_Vertices, k_Indices, SceneMeshTargetPicking.meshEdgePadding));
        }

        [Test]
        public void EmptyMeshHasNoProtectedBoundary()
        {
            Assert.IsFalse(SceneMeshTargetPicking.NearBoundary(Vector2.zero,
                System.Array.Empty<Vector2>(), System.Array.Empty<int>(), 12f));
        }
    }
}
