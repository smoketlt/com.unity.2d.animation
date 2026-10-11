using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.U2D.Animation.ClipperLib;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal sealed class SceneMeshOutlineGenerator : IOutlineGenerator
    {
        const double kScale = 1000.0;

        public void GenerateOutline(ITextureDataProvider textureDataProvider, Rect rect, float detail,
            byte alphaTolerance, bool holeDetection, out Vector2[][] paths)
        {
            new OutlineGenerator().GenerateOutline(textureDataProvider, rect, 1f, alphaTolerance, holeDetection, out paths);
            var bounds = new Rect(-rect.size * 0.5f, rect.size);
            paths = paths.Select(path => Simplify(path, bounds, detail)).ToArray();
            var union = new Clipper();
            union.AddPaths(paths.Select(ToPath).ToList(), PolyType.ptSubject, true);
            var result = new List<List<IntPoint>>();
            union.Execute(ClipType.ctUnion, result, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
            paths = result.Select(path => path.Select(point => new Vector2((float)(point.X / kScale), (float)(point.Y / kScale))).ToArray()).ToArray();
        }

        internal static Vector2[] Simplify(Vector2[] source, Rect bounds, float detail)
        {
            if (source.Length <= 3 || detail >= 1f) return (Vector2[])source.Clone();
            var path = (Vector2[])source.Clone();
            var orientation = Mathf.Sign(Area(path));
            var limit = bounds.width * bounds.height * 0.02f * Mathf.Pow(1f - Mathf.Clamp01(detail), 3f);
            var original = ToPath(source);
            while (path.Length > 3)
            {
                var candidates = new List<(float cost, int index, bool merge, Vector2 point)>();
                for (var i = 0; i < path.Length; ++i)
                {
                    var previous = path[(i + path.Length - 1) % path.Length];
                    var point = path[i];
                    var next = path[(i + 1) % path.Length];
                    var following = path[(i + 2) % path.Length];
                    if (orientation * Cross(point - previous, next - point) <= 0.00001f)
                        AddCandidate((Cross(previous, next) - Cross(previous, point) - Cross(point, next)) * orientation * 0.5f,
                            i, false, default);
                    else if (orientation * Cross(next - point, following - next) > 0f)
                    {
                        var a = point - previous;
                        var b = following - next;
                        var denominator = Cross(a, b);
                        if (Mathf.Abs(denominator) < 0.00001f) continue;
                        var t = Cross(next - previous, b) / denominator;
                        var u = Cross(next - previous, a) / denominator;
                        var intersection = previous + t * a;
                        intersection += (intersection - (point + next) * 0.5f).normalized * 0.002f;
                        if (t < 1f || u > 0f || intersection.x < bounds.xMin || intersection.x > bounds.xMax ||
                            intersection.y < bounds.yMin || intersection.y > bounds.yMax) continue;
                        AddCandidate((Cross(previous, intersection) + Cross(intersection, following) -
                            Cross(previous, point) - Cross(point, next) - Cross(next, following)) * orientation * 0.5f,
                            i, true, intersection);
                    }
                }

                Vector2[] best = null;
                foreach (var candidate in candidates.OrderBy(candidate => candidate.cost))
                {
                    var reduced = new List<Vector2>(path.Length - 1);
                    for (var j = 0; j < path.Length; ++j)
                        if (j == candidate.index) { if (candidate.merge) reduced.Add(candidate.point); }
                        else if (!candidate.merge || j != (candidate.index + 1) % path.Length) reduced.Add(path[j]);
                    var candidatePath = reduced.ToArray();
                    if (!IsSimple(candidatePath)) continue;
                    var difference = new Clipper();
                    difference.AddPath(original, PolyType.ptSubject, true);
                    difference.AddPath(ToPath(candidatePath), PolyType.ptClip, true);
                    var uncovered = new List<List<IntPoint>>();
                    difference.Execute(ClipType.ctDifference, uncovered, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
                    if (uncovered.Any(polygon => Math.Abs(Clipper.Area(polygon)) > 1.0)) continue;
                    best = candidatePath;
                    break;
                }
                if (best == null) break;
                path = best;

                void AddCandidate(float cost, int index, bool merge, Vector2 point)
                {
                    if (cost >= -0.0001f && cost <= limit)
                        candidates.Add((Mathf.Max(0f, cost), index, merge, point));
                }
            }
            return path;
        }

        static List<IntPoint> ToPath(Vector2[] path) => path.Select(point => new IntPoint(point.x * kScale, point.y * kScale)).ToList();
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static float Area(Vector2[] path)
        {
            var area = 0f;
            for (var i = 0; i < path.Length; ++i) area += Cross(path[i], path[(i + 1) % path.Length]);
            return area * 0.5f;
        }

        static bool IsSimple(Vector2[] path)
        {
            for (var i = 0; i < path.Length; ++i)
            {
                var a = path[i];
                var b = path[(i + 1) % path.Length];
                if ((a - b).sqrMagnitude < 0.000001f) return false;
                for (var j = i + 2; j < path.Length; ++j)
                {
                    if (i == 0 && j == path.Length - 1) continue;
                    var c = path[j];
                    var d = path[(j + 1) % path.Length];
                    if (Cross(b - a, c - a) * Cross(b - a, d - a) <= 0f &&
                        Cross(d - c, a - c) * Cross(d - c, b - c) <= 0f &&
                        Mathf.Max(a.x, b.x) >= Mathf.Min(c.x, d.x) && Mathf.Max(c.x, d.x) >= Mathf.Min(a.x, b.x) &&
                        Mathf.Max(a.y, b.y) >= Mathf.Min(c.y, d.y) && Mathf.Max(c.y, d.y) >= Mathf.Min(a.y, b.y)) return false;
                }
            }
            return true;
        }
    }
}
