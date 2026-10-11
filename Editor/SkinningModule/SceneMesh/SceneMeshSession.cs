using System;
using System.Linq;
using Unity.Mathematics;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;

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

        public bool dirty => pendingCreation || savedMesh != JsonUtility.ToJson(mesh);
        public bool hasBoneWeights => mesh.vertexWeights.Any(weight => weight != null &&
            weight.Any(channel => channel.enabled && channel.weight > 0f &&
                channel.boneIndex >= 0 && channel.boneIndex < mesh.bones.Count));
        public IUndo undoOverride { get; set; }
        public bool isUndoOperationSet { get; private set; }

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

        // Every Sprite has runtime triangles, including untouched ordinary sprites.
        // Only explicit importer mesh data signifies an authored Skinning Editor mesh.
        public bool hasAuthoredMesh => meshProvider.GetVertices(rect.spriteID)?.Length >= 3 &&
            meshProvider.GetIndices(rect.spriteID)?.Length >= 3;

        public SceneMeshSource(Sprite sprite)
        {
            assetPath = AssetDatabase.GetAssetPath(sprite);
            if (string.IsNullOrEmpty(assetPath))
                throw new InvalidOperationException("Select an imported Sprite asset in the scene.");
            importer = AssetImporter.GetAtPath(assetPath);
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null)
                throw new InvalidOperationException("This importer does not support Sprite Editor data.");
            provider.InitSpriteEditorDataProvider();
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
            else
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
            Validate(mesh);
            return mesh;
        }

        public void WriteMesh(SpriteMeshData mesh)
        {
            Validate(mesh);
            var vertices = new Vertex2DMetaData[mesh.vertexCount];
            for (var i = 0; i < vertices.Length; ++i)
            {
                vertices[i].position = mesh.vertices[i];
                // Keep the importer's full SpriteBone index space, including service bones.
                vertices[i].boneWeight = mesh.vertexWeights[i].ToBoneWeight(false);
            }
            meshProvider.SetVertices(rect.spriteID, vertices);
            meshProvider.SetIndices(rect.spriteID, (int[])mesh.indices.Clone());
            meshProvider.SetEdges(rect.spriteID, EditorUtilities.ToVector2Int(mesh.edges));
            provider.Apply();
            importer.SaveAndReimport();
        }

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
