using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    // Refine only the solver's copy. Authored positions, edges and triangles stay intact.
    internal sealed class RefinedBiharmonicWeightsGenerator : IWeightsGenerator
    {
        const int k_MaxTriangles = 8192;
        const int k_MaxVertices = 8192;
        const float k_BarycentricTolerance = .000001f;

        public BoneWeight[] Calculate(string name, in float2[] vertices, in int[] indices, in int2[] edges,
            in float2[] controlPoints, in int2[] bones, in int[] pins)
        {
            if (vertices.Length < 3 || indices.Length < 3)
                throw new InvalidOperationException("Auto requires a triangulated mesh.");
            var solverVertices = new List<float2>(vertices);
            var solverIndices = new List<int>(indices);
            // Three uniform midpoint passes make sparse, uneven authored cages
            // suitable for BBW. Shared edges receive one midpoint, avoiding cracks.
            Refine(solverVertices, ref solverIndices);
            // Insert unreferenced originals as well as bone samples, without
            // leaving overlapping parents or splitting only one side of a shared edge.
            var referenced = new HashSet<int>(solverIndices);
            for (var i = 0; i < vertices.Length; ++i)
                if (!referenced.Contains(i)) InsertPoint(vertices[i], solverVertices, solverIndices, i);
            foreach (var bone in bones)
                for (var sample = 1; sample <= 4; ++sample)
                    InsertPoint(math.lerp(controlPoints[bone.x], controlPoints[bone.y], sample / 5f), solverVertices, solverIndices);
            foreach (var point in controlPoints) InsertPoint(point, solverVertices, solverIndices);
            return BoundedBiharmonicWeightsGenerator.CalculatePrepared(solverVertices.ToArray(), solverIndices.ToArray(),
                (float2[])controlPoints.Clone(), (int2[])bones.Clone(), (int[])pins.Clone(), vertices.Length);
        }

        public JobHandle CalculateJob(string name, in float2[] vertices, in int[] indices, in int2[] edges,
            in float2[] controlPoints, in int2[] bones, in int[] pins, SpriteJobData data)
        {
            var weights = Calculate(name, vertices, indices, edges, controlPoints, bones, pins);
            for (var i = 0; i < weights.Length; ++i) data.weights[i] = weights[i];
            return default;
        }

        static void Refine(List<float2> vertices, ref List<int> indices)
        {
            for (var pass = 0; pass < 3; ++pass)
            {
                var triangleCount = indices.Count / 3;
                if (triangleCount * 4 > k_MaxTriangles || vertices.Count + triangleCount * 3 > k_MaxVertices) break;
                var midpoints = new Dictionary<ulong, int>();
                var refined = new List<int>(indices.Count * 4);
                for (var i = 0; i < indices.Count; i += 3)
                {
                    var a = indices[i]; var b = indices[i + 1]; var c = indices[i + 2];
                    var ab = Midpoint(a, b, vertices, midpoints);
                    var bc = Midpoint(b, c, vertices, midpoints);
                    var ca = Midpoint(c, a, vertices, midpoints);
                    AddTriangle(refined, a, ab, ca); AddTriangle(refined, ab, b, bc);
                    AddTriangle(refined, ca, bc, c); AddTriangle(refined, ab, bc, ca);
                }
                indices = refined;
            }
        }

        static int Midpoint(int a, int b, List<float2> vertices, Dictionary<ulong, int> midpoints)
        {
            var key = ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
            if (!midpoints.TryGetValue(key, out var index))
            {
                index = vertices.Count; vertices.Add((vertices[a] + vertices[b]) * .5f); midpoints.Add(key, index);
            }
            return index;
        }

        static void AddTriangle(List<int> indices, int a, int b, int c)
        {
            indices.Add(a); indices.Add(b); indices.Add(c);
        }

        internal static void InsertPoint(float2 point, List<float2> vertices, List<int> indices, int existingIndex = -1)
        {
            if (existingIndex < 0)
                for (var i = 0; i < vertices.Count; ++i)
                    if (math.distancesq(vertices[i], point) <= 1e-10f) return;
            var originalIndexCount = indices.Count;
            var insertedIndex = existingIndex;
            for (var i = originalIndexCount - 3; i >= 0; i -= 3)
            {
                var a = indices[i]; var b = indices[i + 1]; var c = indices[i + 2];
                var ab = (double2)(vertices[b] - vertices[a]); var ac = (double2)(vertices[c] - vertices[a]);
                var ap = (double2)(point - vertices[a]);
                var determinant = ab.x * ac.y - ab.y * ac.x;
                if (Math.Abs(determinant) < 1e-12) continue;
                var y = (ap.x * ac.y - ap.y * ac.x) / determinant;
                var z = (ab.x * ap.y - ab.y * ap.x) / determinant;
                var x = 1 - y - z;
                if (Math.Min(x, Math.Min(y, z)) < -k_BarycentricTolerance) continue;
                if (insertedIndex < 0) { insertedIndex = vertices.Count; vertices.Add(point); }
                indices.RemoveRange(i, 3);
                if (z > k_BarycentricTolerance) AddTriangle(indices, a, b, insertedIndex);
                if (x > k_BarycentricTolerance) AddTriangle(indices, b, c, insertedIndex);
                if (y > k_BarycentricTolerance) AddTriangle(indices, c, a, insertedIndex);
            }
        }
    }
}
