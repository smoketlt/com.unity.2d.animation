using System;
using System.Linq;
using Unity.Mathematics;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    internal sealed class SceneMeshSession : ScriptableObject, ICacheUndo
    {
        public SpriteMeshData mesh = new SpriteMeshData();
        public IndexedSelection selection = new IndexedSelection();
        public string assetPath;
        public string spriteId;
        public string savedMesh;
        public string sourceMesh;
        public string newMeshBackup;
        public int mode;
        public bool pendingCreation;
        public bool symmetry;
        [SerializeField] float m_SymmetryAxisOffset;
        [SerializeField] System.Collections.Generic.List<Vector2> m_SymmetryHull = new System.Collections.Generic.List<Vector2>();
        [SerializeField] System.Collections.Generic.List<int> m_SymmetrySources = new System.Collections.Generic.List<int>();

        internal bool symmetryActive => symmetry;
        internal bool symmetryHullActive => symmetry && mode == 3;
        float symmetryTolerance => Mathf.Max(0.0001f, Mathf.Max(mesh.frame.width, mesh.frame.height) * 0.0000005f);
        // Existing cages can be slightly asymmetric. Keep this separate from the
        // precision used to reuse created vertices and restore triangulated data.
        float symmetryPartnerTolerance => Mathf.Max(symmetryTolerance, MeshEditorSettings.symmetryThreshold);
        internal float symmetryAxis => mesh.frame.width * 0.5f + m_SymmetryAxisOffset;
        internal Vector2 MirrorHullPoint(Vector2 point) => new Vector2(2f * symmetryAxis - point.x, point.y);

        internal void SetSymmetryAxis(float axis)
        {
            if (float.IsNaN(axis) || float.IsInfinity(axis)) return;
            float previous = symmetryAxis;
            float minimum = 0f, maximum = mesh.frame.width;
            if (symmetryHullActive)
                foreach (var point in m_SymmetryHull)
                    if (point.x != previous)
                    {
                        minimum = Mathf.Max(minimum, point.x + mesh.frame.width * 0.0051f);
                        maximum = Mathf.Min(maximum, (mesh.frame.width + point.x) * 0.5f);
                    }
            axis = Mathf.Clamp(axis, minimum, maximum);
            if (axis == previous) return;
            BeginUndoOperation("Move Sprite Mesh Symmetry Axis");
            m_SymmetryAxisOffset = axis - mesh.frame.width * 0.5f;
            axis = symmetryAxis;
            if (symmetryHullActive)
            {
                for (var i = 0; i < m_SymmetryHull.Count; ++i)
                    if (m_SymmetryHull[i].x == previous)
                        m_SymmetryHull[i] = new Vector2(axis, m_SymmetryHull[i].y);
                RebuildSymmetryHull();
            }
        }

        internal Vector2 SymmetryHullPoint(Vector2 point)
        {
            float center = symmetryAxis;
            point.x = Mathf.Clamp(point.x, 0f, mesh.frame.width);
            point.y = Mathf.Clamp(point.y, 0f, mesh.frame.height);
            if (Mathf.Abs(point.x - center) <= mesh.frame.width * 0.005f) point.x = center;
            else if (point.x > center) point = MirrorHullPoint(point);
            point.x = Mathf.Max(point.x, 2f * center - mesh.frame.width);
            return point;
        }

        internal void SetSymmetry(bool value)
        {
            if (value == symmetry) return;
            BeginUndoOperation("Sprite Mesh Symmetry");
            if (value && mode == 3)
            {
                m_SymmetryHull = mesh.vertices.Select(SymmetryHullPoint).Distinct().ToList();
                RebuildSymmetryHull();
            }
            symmetry = value;
            selection.Clear();
            EndUndoOperation();
        }

        internal void AddSymmetryVertex(Vector2 position)
        {
            position = SymmetryHullPoint(position);
            if (m_SymmetryHull.Contains(position)) return;
            BeginUndoOperation("Create Symmetric Sprite Mesh Vertex");
            m_SymmetryHull.Add(position);
            RebuildSymmetryHull();
            selection.Clear();
            selection.Select(m_SymmetryHull.Count - 1, true);
            if (m_SymmetryHull.Count >= 3 && m_SymmetryHull[0].x == symmetryAxis &&
                position.x == symmetryAxis)
                CompleteNew();
            EndUndoOperation();
        }

        internal bool IsNewHullClosureVertex(int index) => index == 0 ||
            (symmetryHullActive && index >= 0 && index < m_SymmetrySources.Count && m_SymmetrySources[index] == 0);

        internal void RemoveSymmetryVertices(int[] indices)
        {
            var sources = indices.Where(i => i >= 0 && i < m_SymmetrySources.Count)
                .Select(i => m_SymmetrySources[i]).Distinct().OrderByDescending(i => i).ToArray();
            if (sources.Length == 0) return;
            BeginUndoOperation("Delete Symmetric Sprite Mesh Vertices");
            foreach (var index in sources) m_SymmetryHull.RemoveAt(index);
            RebuildSymmetryHull();
            selection.Clear();
            EndUndoOperation();
        }

        internal void MoveSymmetryVertices(int[] indices, Vector2[] positions)
        {
            var moved = new System.Collections.Generic.HashSet<int>();
            for (var i = 0; i < indices.Length; ++i)
            {
                int index = indices[i];
                int source = m_SymmetrySources[index];
                if (!moved.Add(source)) continue;
                var point = index < m_SymmetryHull.Count ? positions[i] : MirrorHullPoint(positions[i]);
                float center = symmetryAxis;
                // Keep handle indices stable throughout a drag: axis vertices stay
                // single, and paired vertices stop just short of merging on the axis.
                point.x = m_SymmetryHull[source].x == center ? center :
                    Mathf.Min(point.x, center - mesh.frame.width * 0.0051f);
                m_SymmetryHull[source] = SymmetryHullPoint(point);
            }
            var selected = indices.Select(i => m_SymmetrySources[i]).Distinct().ToArray();
            int previousCount = mesh.vertexCount;
            RebuildSymmetryHull();
            selection.elements = previousCount == mesh.vertexCount ? indices : selected;
        }

        internal Vector2? symmetryHullTip => m_SymmetryHull.Count > 0 ? m_SymmetryHull[m_SymmetryHull.Count - 1] : (Vector2?)null;

        void RebuildSymmetryHull()
        {
            mesh.Clear();
            m_SymmetrySources.Clear();
            for (var i = 0; i < m_SymmetryHull.Count; ++i)
            {
                mesh.AddVertex(m_SymmetryHull[i], default);
                m_SymmetrySources.Add(i);
            }
            for (var i = m_SymmetryHull.Count - 1; i >= 0; --i)
            {
                var point = MirrorHullPoint(m_SymmetryHull[i]);
                if (point.Equals(m_SymmetryHull[i])) continue;
                mesh.AddVertex(point, default);
                m_SymmetrySources.Add(i);
            }
            var controller = new SpriteMeshDataController { spriteMeshData = mesh };
            for (var i = 1; i < mesh.vertexCount; ++i) controller.CreateEdge(i - 1, i);
        }

        internal int FindSymmetryVertex(Vector2 position)
        {
            float distance = symmetryTolerance * symmetryTolerance;
            int result = -1;
            for (var i = 0; i < mesh.vertexCount; ++i)
            {
                float candidate = (mesh.vertices[i] - position).sqrMagnitude;
                if (candidate > distance) continue;
                distance = candidate;
                result = i;
            }
            return result;
        }

        internal int FindSymmetryPartner(int index)
        {
            if (index < 0 || index >= mesh.vertexCount) return -1;
            if (Mathf.Abs(mesh.vertices[index].x - symmetryAxis) <= symmetryTolerance) return index;
            int partner = NearestSymmetryPartner(index);
            // Require agreement in both directions so two nearby handles cannot
            // drive the same mirrored vertex.
            return partner >= 0 && NearestSymmetryPartner(partner) == index ? partner : -1;
        }

        int NearestSymmetryPartner(int index)
        {
            var original = mesh.vertices[index];
            var reflected = MirrorHullPoint(original);
            float distance = symmetryPartnerTolerance * symmetryPartnerTolerance;
            int result = -1;
            for (var i = 0; i < mesh.vertexCount; ++i)
            {
                var candidate = mesh.vertices[i];
                if ((original.x < symmetryAxis) == (candidate.x < symmetryAxis) ||
                    Mathf.Abs(candidate.x - symmetryAxis) <= symmetryTolerance) continue;
                float squared = (candidate - reflected).sqrMagnitude;
                if (squared > distance || (squared == distance && result >= 0)) continue;
                distance = squared;
                result = i;
            }
            return result;
        }

        internal int[] SymmetrySelection(int[] selected) => selected.Concat(selected
            .Where(i => i >= 0 && i < mesh.vertexCount).Select(FindSymmetryPartner)
            .Where(i => i >= 0)).Distinct().ToArray();

        internal static EditableBoneWeight SampleGeometryWeight(BaseSpriteMeshData data, Vector2 position, int edgeIndex = -1)
        {
            var result = new EditableBoneWeight();
            void Add(int index, float amount)
            {
                foreach (var channel in data.vertexWeights[index])
                    if (channel.enabled && channel.weight * amount > 0f)
                        result.AddChannel(channel.boneIndex, channel.weight * amount, true);
            }
            if (edgeIndex >= 0)
            {
                var edge = data.edges[edgeIndex];
                var a = data.vertices[edge.x];
                var direction = data.vertices[edge.y] - a;
                float t = direction.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(position - a, direction) / direction.sqrMagnitude) : 0f;
                Add(edge.x, 1f - t);
                Add(edge.y, t);
            }
            else if (new SpriteMeshDataController { spriteMeshData = data }.FindTriangle(position, out var triangle, out var barycentric))
            {
                Add(triangle.x, barycentric.x);
                Add(triangle.y, barycentric.y);
                Add(triangle.z, barycentric.z);
            }
            result.UnifyChannelsWithSameBoneIndex();
            result.FilterChannels(0f);
            result.Clamp(4, true);
            return result;
        }

        int EnsureSymmetryVertex(Vector2 position)
        {
            int existing = FindSymmetryVertex(position);
            if (existing >= 0) return existing;
            var controller = new SpriteMeshDataController { spriteMeshData = mesh };
            int edgeIndex = -1;
            for (var i = 0; i < mesh.edges.Length; ++i)
            {
                var edge = mesh.edges[i];
                var a = mesh.vertices[edge.x];
                var direction = mesh.vertices[edge.y] - a;
                float t = direction.sqrMagnitude > 0f ? Vector2.Dot(position - a, direction) / direction.sqrMagnitude : -1f;
                if (t >= 0f && t <= 1f && (position - a - t * direction).sqrMagnitude <= symmetryTolerance * symmetryTolerance)
                { edgeIndex = i; break; }
            }
            if (edgeIndex < 0 && !controller.FindTriangle(position, out _, out _))
                throw new InvalidOperationException("The mirrored vertex is outside the mesh. Adjust the outline or disable Symmetry.");
            var weight = SampleGeometryWeight(mesh, position, edgeIndex).ToBoneWeight(true);
            int index = mesh.vertexCount;
            controller.CreateVertex(position, edgeIndex);
            mesh.vertexWeights[index].SetFromBoneWeight(weight);
            return index;
        }

        internal void CreateClippingContourVertex(Vector2 position)
        {
            var boundary = mesh.outlineEdges;
            if (boundary.Length == 0) throw new InvalidOperationException("Close the contour before inserting points.");
            int2 edge = boundary[0];
            var nearest = Vector2.zero; var distance = float.PositiveInfinity;
            foreach (var candidate in boundary)
            {
                var a = mesh.vertices[candidate.x]; var direction = mesh.vertices[candidate.y] - a;
                var t = direction.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(position - a, direction) / direction.sqrMagnitude) : 0;
                var point = a + t * direction; var squared = (position - point).sqrMagnitude;
                if (squared >= distance) continue;
                distance = squared; edge = candidate; nearest = point;
            }
            if ((position - mesh.vertices[edge.x]).sqrMagnitude < 0.0001f ||
                (position - mesh.vertices[edge.y]).sqrMagnitude < 0.0001f) return;
            BeginUndoOperation("Insert Clipping Contour Point");
            var controller = new SpriteMeshDataController { spriteMeshData = mesh };
            int index = Array.FindIndex(mesh.edges, item => (item.x == edge.x && item.y == edge.y) || (item.x == edge.y && item.y == edge.x));
            if (index < 0)
            {
                controller.CreateEdge(edge.x, edge.y);
                index = Array.FindIndex(mesh.edges, item => (item.x == edge.x && item.y == edge.y) || (item.x == edge.y && item.y == edge.x));
            }
            var weight = SampleGeometryWeight(mesh, nearest, index).ToBoneWeight(true);
            controller.CreateVertex(position, index);
            mesh.vertexWeights[mesh.vertexCount - 1].SetFromBoneWeight(weight);
            controller.Triangulate(new Triangulator()); controller.SortTrianglesByDepth();
            SceneMeshSource.Validate(mesh);
        }

        internal void CreateSymmetricGeometry(Vector2 position, int fromVertex = -1)
        {
            var backup = JsonUtility.ToJson(mesh);
            var selected = selection.elements;
            try { CreateSymmetricGeometryCore(position, fromVertex); }
            catch
            {
                JsonUtility.FromJsonOverwrite(backup, mesh);
                selection.elements = selected;
                throw;
            }
        }

        void CreateSymmetricGeometryCore(Vector2 position, int fromVertex)
        {
            float center = symmetryAxis;
            if (Mathf.Abs(position.x - center) <= mesh.frame.width * 0.005f) position.x = center;
            var mirrored = MirrorHullPoint(position);
            BeginUndoOperation("Create Symmetric Sprite Mesh Geometry");
            int fromMirror = FindSymmetryPartner(fromVertex);
            if (fromVertex >= 0 && fromMirror < 0)
                fromMirror = EnsureSymmetryVertex(MirrorHullPoint(mesh.vertices[fromVertex]));
            int primary = EnsureSymmetryVertex(position);
            int partner = EnsureSymmetryVertex(mirrored);
            var controller = new SpriteMeshDataController { spriteMeshData = mesh };
            if (fromVertex >= 0)
            {
                if (fromVertex != primary) controller.CreateEdge(fromVertex, primary);
                if (fromMirror != partner) controller.CreateEdge(fromMirror, partner);
            }
            TriangulateSymmetryGeometry();
            selection.elements = new[] { FindSymmetryVertex(position) };
        }

        internal bool MoveSymmetricGeometry(int[] selected, Vector2[] positions)
        {
            var backup = JsonUtility.ToJson(mesh);
            var previousSelection = selection.elements;
            try { return MoveSymmetricGeometryCore(selected, positions); }
            catch
            {
                JsonUtility.FromJsonOverwrite(backup, mesh);
                selection.elements = previousSelection;
                throw;
            }
        }

        bool MoveSymmetricGeometryCore(int[] selected, Vector2[] positions)
        {
            var updates = new System.Collections.Generic.Dictionary<int, Vector2>();
            float center = symmetryAxis;
            for (var i = 0; i < selected.Length; ++i)
            {
                int index = selected[i];
                if (updates.ContainsKey(index)) continue;
                var original = mesh.vertices[index];
                int partner = FindSymmetryPartner(index);
                var point = positions[i];
                point.y = Mathf.Clamp(point.y, 0f, mesh.frame.height);
                if (Mathf.Abs(original.x - center) <= symmetryTolerance) point.x = center;
                else if (original.x < center) point.x = Mathf.Clamp(point.x, 0f, center - mesh.frame.width * 0.0051f);
                else point.x = Mathf.Clamp(point.x, center + mesh.frame.width * 0.0051f, mesh.frame.width);
                if (partner >= 0 && partner != index)
                    point.x = Mathf.Clamp(point.x, Mathf.Max(0f, 2f * center - mesh.frame.width), Mathf.Min(mesh.frame.width, 2f * center));
                updates[index] = point;
                if (partner >= 0 && partner != index) updates[partner] = MirrorHullPoint(point);
            }
            int[] indices = updates.Keys.ToArray();
            Vector2[] moved = indices.Select(i => updates[i]).ToArray();
            if (SpriteMeshController.IsMovedEdgeIntersectingWithOtherEdge(indices, moved, mesh.edges, mesh.vertices) ||
                SpriteMeshController.IsMovedVertexIntersectingWithOutline(indices, moved, mesh.outlineEdges, mesh.vertices)) return false;
            BeginUndoOperation("Move Symmetric Sprite Mesh Vertices");
            foreach (var update in updates) mesh.vertices[update.Key] = update.Value;
            var selectedPositions = selected.Select(i => mesh.vertices[i]).ToArray();
            TriangulateSymmetryGeometry();
            selection.elements = selectedPositions.Select(FindSymmetryVertex).ToArray();
            return true;
        }

        void TriangulateSymmetryGeometry()
        {
            var positions = (Vector2[])mesh.vertices.Clone();
            var weights = mesh.vertexWeights.Select(w => w.ToBoneWeight(false)).ToArray();
            var controller = new SpriteMeshDataController { spriteMeshData = mesh };
            controller.Triangulate(new Triangulator());
            var used = new System.Collections.Generic.HashSet<int>();
            for (var i = 0; i < positions.Length; ++i)
            {
                int index = FindSymmetryVertex(positions[i]);
                if (index < 0 || !used.Add(index))
                    throw new InvalidOperationException("The symmetric geometry could not be triangulated. Previous mesh restored.");
                mesh.vertices[index] = positions[i];
                mesh.vertexWeights[index].SetFromBoneWeight(weights[i]);
            }
            controller.SortTrianglesByDepth();
            SceneMeshSource.Validate(mesh);
        }

        public bool dirty => pendingCreation || savedMesh != JsonUtility.ToJson(mesh);
        public bool hasBoneWeights => mesh.vertexWeights.Any(weight => weight != null &&
            weight.Any(channel => channel.enabled && channel.weight > 0f &&
                channel.boneIndex >= 0 && channel.boneIndex < mesh.bones.Count));
        public IUndo undoOverride { get; set; }
        public bool isUndoOperationSet { get; private set; }

        // Version 1 could contain cage vertices; do not paste those older snapshots.
        const string ClipboardPrefix = "Uniskin.SceneMeshVertices/2:";
        const string MeshClipboardPrefix = "Uniskin.SceneMeshGeometry/1:";

        [Serializable]
        sealed class MeshClipboard
        {
            public Vector2 size;
            public Vector2[] positions;
            public int[] indices;
            public int2[] edges;
        }

        internal string CopyMeshGeometry()
        {
            SceneMeshSource.Validate(mesh);
            return MeshClipboardPrefix + JsonUtility.ToJson(new MeshClipboard
            {
                size = mesh.frame.size, positions = mesh.vertices,
                indices = mesh.indices, edges = mesh.edges
            });
        }

        internal SpriteMeshData PreviewMeshGeometry(string clipboard, bool mirror, Vector2 offset, Vector2 scale, float angle)
        {
            if (string.IsNullOrEmpty(clipboard) || !clipboard.StartsWith(MeshClipboardPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Use Copy in Edit Mesh to copy a complete mesh first.");
            var data = JsonUtility.FromJson<MeshClipboard>(clipboard.Substring(MeshClipboardPrefix.Length));
            bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
            if (data?.positions == null || data.indices == null || data.edges == null ||
                data.positions.Length < 3 || data.positions.Length > 65535 ||
                !Finite(data.size.x) || !Finite(data.size.y) || data.size.x <= 0 || data.size.y <= 0 ||
                !Finite(offset.x) || !Finite(offset.y) || !Finite(scale.x) || !Finite(scale.y) ||
                scale.x <= 0 || scale.y <= 0 || !Finite(angle) ||
                data.positions.Any(p => !Finite(p.x) || !Finite(p.y)) ||
                data.indices.Length == 0 || data.indices.Length % 3 != 0 ||
                data.indices.Any(i => i < 0 || i >= data.positions.Length) ||
                data.edges.Any(e => e.x < 0 || e.y < 0 || e.x >= data.positions.Length || e.y >= data.positions.Length || e.x == e.y))
                throw new InvalidOperationException("Invalid mesh clipboard or transform. Scale must be positive.");
            var result = new SpriteMeshData();
            result.SetFrame(mesh.frame);
            result.bones = mesh.bones;
            float radians = angle * Mathf.Deg2Rad;
            var positions = data.positions.Select(p =>
            {
                var local = p - data.size * 0.5f;
                if (mirror) local.x = -local.x;
                local = Vector2.Scale(local, Vector2.Scale(new Vector2(mesh.frame.width / data.size.x, mesh.frame.height / data.size.y), scale));
                return mesh.frame.size * 0.5f + offset + new Vector2(
                    local.x * Mathf.Cos(radians) - local.y * Mathf.Sin(radians),
                    local.x * Mathf.Sin(radians) + local.y * Mathf.Cos(radians));
            }).ToArray();
            result.SetVertices(positions, positions.Select(_ => EditableBoneWeightUtility.CreateFromBoneWeight(default)).ToArray());
            var indices = (int[])data.indices.Clone();
            for (int i = 0; i < indices.Length; i += 3)
            {
                if (mirror) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
                var a = positions[indices[i + 1]] - positions[indices[i]];
                var b = positions[indices[i + 2]] - positions[indices[i]];
                if (Mathf.Abs(a.x * b.y - a.y * b.x) < 0.000001f)
                    throw new InvalidOperationException("The pasted mesh contains a collapsed triangle.");
            }
            result.SetEdges((int2[])data.edges.Clone());
            result.SetIndices(indices);
            SceneMeshSource.Validate(result);
            return result;
        }

        internal void ApplyMeshGeometry(SpriteMeshData preview)
        {
            SceneMeshSource.Validate(preview);
            BeginUndoOperation("Paste Sprite Mesh Geometry");
            try
            {
                // Keep target metadata/bones; only replace geometry and clear weights.
                mesh.SetVertices((Vector2[])preview.vertices.Clone(), preview.vertices.Select(_ => EditableBoneWeightUtility.CreateFromBoneWeight(default)).ToArray());
                mesh.SetEdges((int2[])preview.edges.Clone());
                mesh.SetIndices((int[])preview.indices.Clone());
                selection.Clear();
                mode = 0;
            }
            finally { EndUndoOperation(); }
        }

        [Serializable]
        sealed class VertexClipboard
        {
            public string assetPath;
            public string spriteId;
            public string sourceMesh;
            public Vector2[] positions;
            public BoneWeight[] weights;
            public int2[] edges;
        }

        internal string CopySelectedVertices()
        {
            var cage = new System.Collections.Generic.HashSet<int>(mesh.outlineEdges.SelectMany(e => new[] { e.x, e.y }));
            var selected = selection.elements.Where(i => i >= 0 && i < mesh.vertexCount && !cage.Contains(i)).Distinct().OrderBy(i => i).ToArray();
            if (selected.Length == 0) return null;
            var map = selected.Select((index, offset) => new { index, offset }).ToDictionary(v => v.index, v => v.offset);
            return ClipboardPrefix + JsonUtility.ToJson(new VertexClipboard
            {
                assetPath = assetPath, spriteId = spriteId, sourceMesh = sourceMesh,
                positions = selected.Select(i => mesh.vertices[i]).ToArray(),
                weights = selected.Select(i => mesh.vertexWeights[i].ToBoneWeight(false)).ToArray(),
                edges = mesh.edges.Where(e => map.ContainsKey(e.x) && map.ContainsKey(e.y))
                    .Select(e => new int2(map[e.x], map[e.y])).ToArray()
            });
        }

        internal bool PasteVertices(string clipboard, bool mirror)
        {
            if (string.IsNullOrEmpty(clipboard) || !clipboard.StartsWith(ClipboardPrefix, StringComparison.Ordinal)) return false;
            VertexClipboard data;
            try { data = JsonUtility.FromJson<VertexClipboard>(clipboard.Substring(ClipboardPrefix.Length)); }
            catch (ArgumentException) { return false; }
            if (data?.positions == null || data.positions.Length == 0 || data.weights == null ||
                data.weights.Length != data.positions.Length || data.edges == null) return false;
            if (data.positions.Any(p => float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.y)) ||
                data.edges.Any(e => e.x < 0 || e.y < 0 || e.x >= data.positions.Length || e.y >= data.positions.Length || e.x == e.y)) return false;
            if (mesh.vertexCount + data.positions.Length > 65535)
                throw new InvalidOperationException("Paste exceeds the Sprite mesh vertex limit.");

            // Only paste strictly inside the current cage. This also prevents a
            // mirrored interior point from becoming a new hull point on another mesh.
            var eligible = data.positions.Select((p, i) => new { p = mirror ? new Vector2(mesh.frame.width - p.x, p.y) : p, i })
                .Where(v => IsInsideCage(v.p)).ToArray();
            if (eligible.Length == 0) return false;
            var eligibleMap = eligible.Select((v, i) => new { v.i, offset = i }).ToDictionary(v => v.i, v => v.offset);
            data.weights = eligible.Select(v => data.weights[v.i]).ToArray();
            data.edges = data.edges.Where(e => eligibleMap.ContainsKey(e.x) && eligibleMap.ContainsKey(e.y))
                .Select(e => new int2(eligibleMap[e.x], eligibleMap[e.y])).ToArray();
            data.positions = eligible.Select(v => v.p).ToArray();

            var backup = EditorJsonUtility.ToJson(this);
            BeginUndoOperation(mirror ? "Mirror Paste Sprite Mesh Vertices" : "Paste Sprite Mesh Vertices");
            try
            {
                var map = new int[data.positions.Length];
                bool sameSource = data.assetPath == assetPath && data.spriteId == spriteId && data.sourceMesh == sourceMesh;
                for (var i = 0; i < map.Length; ++i)
                {
                    var position = data.positions[i];
                    // Coincident points cannot form distinct triangulated vertices.
                    map[i] = Array.FindIndex(mesh.vertices, p => p.Equals(position));
                    if (map[i] >= 0) continue;
                    map[i] = mesh.vertexCount;
                    mesh.AddVertex(position, sameSource ? data.weights[i] : default);
                }
                var controller = new SpriteMeshDataController { spriteMeshData = mesh };
                foreach (var edge in data.edges)
                    if (map[edge.x] != map[edge.y]) controller.CreateEdge(map[edge.x], map[edge.y]);
                var selectedPositions = map.Select(i => mesh.vertices[i]).Distinct().ToArray();
                var authoredWeights = mesh.vertices.Select((p, i) => new { p, weight = mesh.vertexWeights[i].ToBoneWeight(false) })
                    .GroupBy(v => v.p).ToDictionary(g => g.Key, g => g.First().weight);
                controller.Triangulate(new Triangulator());
                // Planar validation can round fractional pixel coordinates by a few
                // float ULPs. Match surviving vertices before restoring exact authored
                // positions/weights, rather than treating that rounding as deletion.
                var restored = new System.Collections.Generic.Dictionary<Vector2, int>();
                var used = new bool[mesh.vertexCount];
                foreach (var position in authoredWeights.Keys)
                {
                    int index = Array.FindIndex(mesh.vertices, p => p.Equals(position));
                    if (index < 0) continue;
                    restored.Add(position, index);
                    used[index] = true;
                }
                float tolerance = Mathf.Max(0.0001f, Mathf.Max(mesh.frame.width, mesh.frame.height) * 0.0000005f);
                foreach (var position in authoredWeights.Keys)
                {
                    if (restored.ContainsKey(position)) continue;
                    int nearest = -1;
                    float distance = tolerance * tolerance;
                    for (var i = 0; i < mesh.vertexCount; ++i)
                    {
                        float candidate = (mesh.vertices[i] - position).sqrMagnitude;
                        if (used[i] || candidate > distance) continue;
                        nearest = i;
                        distance = candidate;
                    }
                    if (nearest < 0)
                        throw new InvalidOperationException("The pasted geometry could not be triangulated. Existing mesh restored.");
                    restored.Add(position, nearest);
                    used[nearest] = true;
                }
                foreach (var vertex in restored)
                {
                    mesh.vertices[vertex.Value] = vertex.Key;
                    mesh.vertexWeights[vertex.Value].SetFromBoneWeight(authoredWeights[vertex.Key]);
                }
                controller.SortTrianglesByDepth();
                SceneMeshSource.Validate(mesh);
                selection.elements = selectedPositions.Select(p => restored[p]).ToArray();
            }
            catch
            {
                EditorJsonUtility.FromJsonOverwrite(backup, this);
                throw;
            }
            finally { EndUndoOperation(); }
            return true;
        }

        bool IsInsideCage(Vector2 position)
        {
            bool inside = false;
            const float boundaryTolerance = 0.0001f;
            foreach (var edge in mesh.outlineEdges)
            {
                var a = mesh.vertices[edge.x];
                var b = mesh.vertices[edge.y];
                var segment = b - a;
                float t = segment.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(position - a, segment) / segment.sqrMagnitude) : 0f;
                if ((position - (a + t * segment)).sqrMagnitude <= boundaryTolerance * boundaryTolerance) return false;
                if ((a.y > position.y) != (b.y > position.y) &&
                    position.x < a.x + (position.y - a.y) * segment.x / segment.y)
                    inside = !inside;
            }
            return inside;
        }

        public void BeginUndoOperation(string name)
        {
            if (isUndoOperationSet)
                return;
            Undo.RegisterCompleteObjectUndo(this, name);
            isUndoOperationSet = true;
        }

        public void EndUndoOperation() => isUndoOperationSet = false;
        public void IncrementCurrentGroup() => EndUndoOperation();

        public void Load(Sprite sprite, SceneMeshSource source)
        {
            assetPath = source.assetPath;
            spriteId = source.rect.spriteID.ToString();
            mesh = source.ReadMesh(sprite);
            savedMesh = JsonUtility.ToJson(mesh);
            sourceMesh = source.Fingerprint();
            newMeshBackup = null;
            pendingCreation = false;
            mode = 0;
            symmetry = false;
            m_SymmetryAxisOffset = 0f;
            selection.Clear();
        }

        public void CreateMesh()
        {
            BeginUndoOperation("Create Sprite Mesh");
            mesh.Clear();
            var controller = new SpriteMeshDataController { spriteMeshData = mesh };
            controller.CreateQuad();
            controller.Triangulate(new Triangulator());
            controller.SortTrianglesByDepth();
            pendingCreation = true;
            mode = 0;
            selection.Clear();
            EndUndoOperation();
        }

        public void BeginNew()
        {
            BeginUndoOperation("New Sprite Mesh");
            newMeshBackup = JsonUtility.ToJson(mesh);
            mesh.Clear();
            selection.Clear();
            mode = 3;
            m_SymmetryHull.Clear();
            m_SymmetrySources.Clear();
            EndUndoOperation();
        }

        public void CancelNew()
        {
            BeginUndoOperation("Cancel New Sprite Mesh");
            if (!string.IsNullOrEmpty(newMeshBackup))
                JsonUtility.FromJsonOverwrite(newMeshBackup, mesh);
            newMeshBackup = null;
            mode = 0;
            selection.Clear();
            EndUndoOperation();
        }

        public void FinishNew()
        {
            if (mode != 3) return;
            // Leaving an unfinished hull must not trap the user in New mode.
            if (mesh.vertexCount < 3)
                CancelNew();
            else
                CompleteNew();
        }

        public void CompleteNew()
        {
            if (mesh.vertexCount < 3)
                throw new InvalidOperationException("Place at least three vertices to close the hull.");
            float twiceArea = 0f;
            for (var i = 0; i < mesh.vertexCount; ++i)
            {
                var a = mesh.vertices[i];
                var b = mesh.vertices[(i + 1) % mesh.vertexCount];
                twiceArea += a.x * b.y - b.x * a.y;
                for (var j = i + 2; j < mesh.vertexCount; ++j)
                {
                    if (i == 0 && j == mesh.vertexCount - 1) continue;
                    var c = mesh.vertices[j];
                    var d = mesh.vertices[(j + 1) % mesh.vertexCount];
                    if (SegmentsCross(a, b, c, d))
                        throw new InvalidOperationException("Hull edges cannot cross. Move or delete a vertex before closing.");
                }
            }
            if (Mathf.Abs(twiceArea) < 0.001f)
                throw new InvalidOperationException("The hull needs a non-zero area.");
            BeginUndoOperation("Complete Sprite Mesh Hull");
            var controller = new SpriteMeshDataController { spriteMeshData = mesh };
            controller.CreateEdge(mesh.vertexCount - 1, 0);
            controller.Triangulate(new Triangulator());
            controller.SortTrianglesByDepth();
            newMeshBackup = null;
            mode = 0;
            selection.Clear();
            EndUndoOperation();
        }

        static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float Cross(Vector2 p, Vector2 q) => p.x * q.y - p.y * q.x;
            return Cross(b - a, c - a) * Cross(b - a, d - a) <= 0f &&
                Cross(d - c, a - c) * Cross(d - c, b - c) <= 0f &&
                Mathf.Max(a.x, b.x) >= Mathf.Min(c.x, d.x) && Mathf.Max(c.x, d.x) >= Mathf.Min(a.x, b.x) &&
                Mathf.Max(a.y, b.y) >= Mathf.Min(c.y, d.y) && Mathf.Max(c.y, d.y) >= Mathf.Min(a.y, b.y);
        }

        internal void GenerateInteriorVertices()
        {
            if (mesh.vertexCount + mesh.indices.Length / 3 > 10000)
                throw new InvalidOperationException("Generate is limited to 10000 vertices per mesh.");
            var indices = (int[])mesh.indices.Clone();
            var controller = new SpriteMeshDataController { spriteMeshData = mesh };
            controller.ClearWeights(null);
            for (var i = 0; i < indices.Length; i += 3)
                controller.CreateVertex((mesh.vertices[indices[i]] + mesh.vertices[indices[i + 1]] + mesh.vertices[indices[i + 2]]) / 3f);
            controller.Triangulate(new Triangulator());
            controller.SortTrianglesByDepth();
        }

        internal static Matrix4x4 PixelToWorld(SpriteRenderer renderer, Rect sourceFrame)
        {
            var sprite = renderer.sprite;
            Vector2 scale = sprite.rect.size / sourceFrame.size / sprite.pixelsPerUnit;
            Vector2 pivot = sprite.pivot / sprite.rect.size * sourceFrame.size;
            if (renderer.flipX) scale.x = -scale.x;
            if (renderer.flipY) scale.y = -scale.y;
            return renderer.transform.localToWorldMatrix *
                Matrix4x4.Scale(new Vector3(scale.x, scale.y, 1f)) *
                Matrix4x4.Translate(-pivot);
        }
    }

    internal sealed class SceneMeshSource
    {
        [Serializable]
        sealed class MeshRecord
        {
            public Vertex2DMetaData[] vertices;
            public int[] indices;
            public Vector2Int[] edges;
            public Rect frame;
            public Vector2 pivot;
            public SpriteBone[] bones;
        }

        public readonly string assetPath;
        public readonly AssetImporter importer;
        public readonly ISpriteEditorDataProvider provider;
        public readonly ISpriteMeshDataProvider meshProvider;
        public readonly ITextureDataProvider textureProvider;
        public readonly SpriteRect rect;
        internal readonly UniskinRig rig;
        internal readonly UniskinClippingMaskData clipping;
        string m_BindingUserData;
        SceneMeshBindingPose m_BindingPose;
        internal SceneMeshBindingPose bindingPose
        {
            get
            {
                var data = userData;
                if (m_BindingUserData != data)
                {
                    m_BindingUserData = data;
                    m_BindingPose = SceneMeshBindingPose.Read(data, rect.spriteID.ToString());
                }
                return m_BindingPose;
            }
        }
        internal UnityEngine.Object saveTarget => clipping != null ? (UnityEngine.Object)clipping : rig != null ? (UnityEngine.Object)rig : importer;
        internal string userData
        {
            get => provider is UniskinRigDataProvider rigProvider ? rigProvider.data.userData : importer.userData;
            set { if (provider is UniskinRigDataProvider rigProvider) rigProvider.data.userData = value; else importer.userData = value; }
        }

        // Every Sprite has runtime triangles, including untouched ordinary sprites.
        // Only explicit importer mesh data signifies an authored Skinning Editor mesh.
        public bool hasAuthoredMesh => meshProvider.GetVertices(rect.spriteID)?.Length >= 3 &&
            meshProvider.GetIndices(rect.spriteID)?.Length >= 3;

        public SceneMeshSource(Sprite sprite, bool useRig = true)
        {
            clipping = useRig ? UniskinClippingMaskUtility.Find(sprite) : null;
            assetPath = UniskinRigUtility.SourcePath(sprite);
            if (string.IsNullOrEmpty(assetPath))
                throw new InvalidOperationException("Select an imported Sprite asset in the scene.");
            importer = AssetImporter.GetAtPath(clipping != null ? AssetDatabase.GetAssetPath(clipping.source) : assetPath);
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null)
                throw new InvalidOperationException("This importer does not support Sprite Editor data.");
            provider.InitSpriteEditorDataProvider();
            rig = useRig ? UniskinRigUtility.FindAtSource(assetPath) : null;
            if (clipping != null)
            {
                UniskinClippingMaskUtility.CheckSource(clipping);
                rig = clipping.rig;
                provider = new UniskinRigDataProvider(clipping, provider);
            }
            else if (rig != null && rig.editingEnabled)
            {
                UniskinRigUtility.CheckSource(rig);
                provider = new UniskinRigDataProvider(rig, provider);
            }
            else rig = null;
            meshProvider = provider.GetDataProvider<ISpriteMeshDataProvider>();
            textureProvider = provider.GetDataProvider<ITextureDataProvider>();
            rect = provider.GetSpriteRects().FirstOrDefault(r => r.spriteID == sprite.GetSpriteID());
            if (meshProvider == null || rect == null)
                throw new InvalidOperationException("This Sprite has no editable Skinning Editor mesh provider.");
        }

        public string Fingerprint()
        {
            return JsonUtility.ToJson(new MeshRecord
            {
                vertices = meshProvider.GetVertices(rect.spriteID),
                indices = meshProvider.GetIndices(rect.spriteID),
                edges = meshProvider.GetEdges(rect.spriteID),
                frame = rect.rect,
                pivot = rect.pivot,
                bones = provider.GetDataProvider<ISpriteBoneDataProvider>()?.GetBones(rect.spriteID)?.ToArray()
            });
        }

        public SpriteMeshData ReadMesh(Sprite sprite)
        {
            var mesh = new SpriteMeshData();
            mesh.SetFrame(rect.rect);
            var vertices = meshProvider.GetVertices(rect.spriteID);
            if (vertices != null && vertices.Length > 0)
            {
                foreach (var vertex in vertices)
                    mesh.AddVertex(vertex.position, vertex.boneWeight);
                mesh.SetIndices(meshProvider.GetIndices(rect.spriteID) ?? Array.Empty<int>());
                mesh.SetEdges(EditorUtilities.ToInt2(meshProvider.GetEdges(rect.spriteID) ?? Array.Empty<Vector2Int>()));
            }
            else if (clipping == null)
            {
                // Runtime sprite positions are in units; importer geometry is in source pixels.
                Vector2 scale = rect.rect.size / sprite.rect.size;
                foreach (var vertex in sprite.vertices)
                    mesh.AddVertex(Vector2.Scale(vertex * sprite.pixelsPerUnit + sprite.pivot, scale), default);
                mesh.SetIndices(Array.ConvertAll(sprite.triangles, i => (int)i));
                mesh.SetEdges((int2[])mesh.outlineEdges.Clone());
            }
            var bones = provider.GetDataProvider<ISpriteBoneDataProvider>()?.GetBones(rect.spriteID);
            if (bones != null)
            {
                mesh.bones = ModuleUtility.CreateSpriteBoneData(bones.ToArray(), Matrix4x4.identity);
            }
            if (clipping == null || mesh.vertexCount > 0) Validate(mesh);
            return mesh;
        }

        public bool WriteMesh(SpriteMeshData mesh)
        {
            if (clipping == null || mesh.vertexCount > 0) Validate(mesh);
            var vertices = new Vertex2DMetaData[mesh.vertexCount];
            for (var i = 0; i < vertices.Length; ++i)
            {
                vertices[i].position = mesh.vertices[i];
                // Keep the importer's full SpriteBone index space, including service bones.
                vertices[i].boneWeight = mesh.vertexWeights[i].ToBoneWeight(false);
            }
            var indices = mesh.indices;
            var edges = EditorUtilities.ToVector2Int(mesh.edges);
            // Compare the actual provider payload, not session JSON (which also includes
            // derived outlines and editable weight channels). Do not reimport a PSB merely
            // because a gesture changed those transient details, then returned to its start.
            var storedVertices = meshProvider.GetVertices(rect.spriteID);
            if (storedVertices != null && storedVertices.Length == vertices.Length &&
                storedVertices.Zip(vertices, (a, b) => a.position.Equals(b.position) && a.boneWeight.Equals(b.boneWeight)).All(equal => equal) &&
                (meshProvider.GetIndices(rect.spriteID) ?? Array.Empty<int>()).SequenceEqual(indices) &&
                (meshProvider.GetEdges(rect.spriteID) ?? Array.Empty<Vector2Int>()).SequenceEqual(edges))
                return false;
            meshProvider.SetVertices(rect.spriteID, vertices);
            meshProvider.SetIndices(rect.spriteID, (int[])indices.Clone());
            meshProvider.SetEdges(rect.spriteID, edges);
            ApplyAndSave();
            return true;
        }

        internal void ApplyAndSave()
        {
            provider.Apply();
            if (rig == null) importer.SaveAndReimport();
        }

        internal Sprite ResolveSprite(string id) => clipping != null ? clipping.sprite : rig != null ? rig.Find(id).sprite :
            AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().First(sprite => sprite.GetSpriteID().ToString() == id);

        internal static void Validate(BaseSpriteMeshData mesh)
        {
            if (mesh.vertexCount < 3 || mesh.vertexCount > 65535 || mesh.indices.Length == 0 || mesh.indices.Length % 3 != 0)
                throw new InvalidOperationException("A Sprite mesh needs valid triangles and 3–65535 vertices.");
            if (mesh.vertexWeights.Length != mesh.vertexCount)
                throw new InvalidOperationException("Vertex and weight counts do not match.");
            foreach (var vertex in mesh.vertices)
                if (float.IsNaN(vertex.x) || float.IsInfinity(vertex.x) || float.IsNaN(vertex.y) || float.IsInfinity(vertex.y))
                    throw new InvalidOperationException("Mesh positions must be finite.");
            foreach (var index in mesh.indices)
                if (index < 0 || index >= mesh.vertexCount)
                    throw new InvalidOperationException("A triangle references a missing vertex.");
            foreach (var edge in mesh.edges)
                if (edge.x < 0 || edge.y < 0 || edge.x >= mesh.vertexCount || edge.y >= mesh.vertexCount || edge.x == edge.y)
                    throw new InvalidOperationException("An edge references an invalid vertex.");
        }
    }
}
