using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using UnityEngine.UIElements;

namespace Fedorov.Uniskin.Tests
{
    public class AlternativePsbImporterTests
    {
        string folder, path, cache;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__UniskinPsbTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            path = folder + "/Fixture.psb";
            WritePsb(path, 200);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (PSDImporter)AssetImporter.GetAtPath(path);
            importer.useMosaicMode = false;
            importer.useCharacterMode = false;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.isReadable = true;
            importer.SaveAndReimport();
            var data = Provider();
            var rect = data.GetSpriteRects().Single();
            Mesh().SetVertices(rect.spriteID, new[] { Vertex(0, 0), Vertex(32, 0), Vertex(32, 32), Vertex(0, 32) });
            Mesh().SetIndices(rect.spriteID, new[] { 0, 1, 2, 0, 2, 3 });
            Mesh().SetEdges(rect.spriteID, new[] { new Vector2Int(0, 1), new Vector2Int(1, 2), new Vector2Int(2, 3), new Vector2Int(3, 0) });
            data.Apply();
            importer.SaveAndReimport();
            cache = "Library/Uniskin/PsbImportCache/" + AssetDatabase.AssetPathToGUID(path) + ".bin";
        }

        [TearDown]
        public void TearDown()
        {
            if (path != null && UniskinPsbImporterUtility.IsSupported(path)) UniskinPsbImporterUtility.UseUnity(path);
            if (folder != null) AssetDatabase.DeleteAsset(folder);
            if (cache != null && File.Exists(cache)) File.Delete(cache);
        }

        [Test]
        public void ExplicitSwitchPreservesIdentifiersAndMesh()
        {
            Assert.That(AssetImporter.GetAtPath(path).GetType(), Is.EqualTo(typeof(PSDImporter)));
            var before = Snapshot();
            UniskinPsbImporterUtility.UseUniskin(path);
            Assert.That(AssetImporter.GetAtPath(path), Is.TypeOf<UniskinPsbImporter>());
            Assert.That(File.Exists(cache), Is.True);
            Assert.That(Snapshot(), Is.EqualTo(before));
            UniskinPsbImporterUtility.UseUnity(path);
            Assert.That(Snapshot(), Is.EqualTo(before));
        }

        [Test]
        public void CachedTopologyAndWeightsEqualFullNativeImport()
        {
            var data = Provider();
            var id = data.GetSpriteRects().Single().spriteID;
            data.GetDataProvider<ISpriteBoneDataProvider>().SetBones(id, new List<SpriteBone>
            {
                new SpriteBone { name = "root", parentId = -1, position = new Vector3(16, 16), rotation = Quaternion.identity, length = 8 },
                new SpriteBone { name = "child", parentId = 0, position = new Vector3(8, 0), rotation = Quaternion.identity, length = 4 }
            });
            var initial = Mesh().GetVertices(id);
            for (var i = 0; i < initial.Length; ++i) initial[i].boneWeight = new BoneWeight { weight0 = 1 };
            Mesh().SetVertices(id, initial); data.Apply(); AssetImporter.GetAtPath(path).SaveAndReimport();
            UniskinPsbImporterUtility.UseUniskin(path);
            var mesh = Mesh();
            mesh.SetVertices(id, new[] { Vertex(1, 0), Vertex(32, 0), Vertex(32, 32), Vertex(0, 32), Vertex(16, 16) }
                .Select(v => { v.boneWeight = new BoneWeight { boneIndex0 = 0, weight0 = .25f, boneIndex1 = 1, weight1 = .75f }; return v; }).ToArray());
            mesh.SetIndices(id, new[] { 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4 });
            Provider().Apply(); AssetImporter.GetAtPath(path).SaveAndReimport();
            Assert.That(Sprite().vertices.Length, Is.EqualTo(5));
            Assert.That(Sprite().GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight)[0].weight1, Is.EqualTo(.75f));
            AssertFullNativeMatches();
        }

        [Test]
        public void ClearedWeightsKeepCachedAndFullImportsRenderableWithoutRebinding()
        {
            var data = Provider();
            var id = data.GetSpriteRects().Single().spriteID;
            data.GetDataProvider<ISpriteBoneDataProvider>().SetBones(id, new List<SpriteBone>
            {
                new SpriteBone { name = "root", parentId = -1, rotation = Quaternion.identity, length = 8 },
                new SpriteBone { name = "child", parentId = 0, rotation = Quaternion.identity, length = 4 }
            });
            var initial = Mesh().GetVertices(id);
            for (var i = 0; i < initial.Length; ++i) initial[i].boneWeight = new BoneWeight { boneIndex0 = 1, weight0 = 1f };
            Mesh().SetVertices(id, initial); data.Apply(); AssetImporter.GetAtPath(path).SaveAndReimport();
            UniskinPsbImporterUtility.UseUniskin(path);
            // A mesh-only save hits the cache established with nonzero weights.
            for (var i = 0; i < initial.Length; ++i) initial[i].boneWeight = default;
            Mesh().SetVertices(id, initial); Provider().Apply(); AssetImporter.GetAtPath(path).SaveAndReimport();
            Assert.That(Mesh().GetVertices(id).All(v => v.boneWeight.Equals(default(BoneWeight))), Is.True);
            Assert.That(Sprite().GetBones().Length, Is.EqualTo(2));
            Assert.That(Sprite().GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight)
                .All(weight => weight.weight0 == 1f && weight.boneIndex0 == 0 &&
                    weight.weight1 + weight.weight2 + weight.weight3 == 0f), Is.True);
            AssertFullNativeMatches();
        }

        [Test]
        public void SourceAndTextureSettingsInvalidateCache()
        {
            UniskinPsbImporterUtility.UseUniskin(path);
            WritePsb(path, 80);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var texture = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture2D>().Single();
            Assert.That(texture.GetPixels32()[0].r, Is.EqualTo(80).Within(2));
            ((PSDImporter)AssetImporter.GetAtPath(path)).spritePixelsPerUnit = 64;
            AssetImporter.GetAtPath(path).SaveAndReimport();
            Assert.That(Sprite().pixelsPerUnit, Is.EqualTo(64));
            AssertFullNativeMatches();
        }

        [Test]
        public void MissingCacheAndClearedGeometryUseNativeImport()
        {
            UniskinPsbImporterUtility.UseUniskin(path);
            File.Delete(cache);
            AssetImporter.GetAtPath(path).SaveAndReimport();
            Assert.That(File.Exists(cache), Is.True);
            var id = Provider().GetSpriteRects().Single().spriteID;
            Mesh().SetVertices(id, Array.Empty<Vertex2DMetaData>());
            Mesh().SetIndices(id, Array.Empty<int>());
            Mesh().SetEdges(id, Array.Empty<Vector2Int>());
            Provider().Apply(); AssetImporter.GetAtPath(path).SaveAndReimport();
            AssertFullNativeMatches();
        }

        [Test]
        public void InspectorIncludesPreservingSwitch()
        {
            foreach (var useUniskin in new[] { false, true })
            {
                if (useUniskin) UniskinPsbImporterUtility.UseUniskin(path);
                var editor = UnityEditor.Editor.CreateEditor(AssetImporter.GetAtPath(path));
                try
                {
                    Assert.That(editor, Is.InstanceOf<UniskinCompatiblePsdImporterEditor>());
                    Assert.That(editor.CreateInspectorGUI().Q<UnityEngine.UIElements.Button>("uniskinSwitchPsbImporter"), Is.Not.Null);
                }
                finally { UnityEngine.Object.DestroyImmediate(editor); }
            }
        }

        void AssertFullNativeMatches()
        {
            var cached = Snapshot();
            UniskinPsbImporterUtility.UseUnity(path);
            Assert.That(Snapshot(), Is.EqualTo(cached));
        }

        ISpriteEditorDataProvider Provider() => (ISpriteEditorDataProvider)AssetImporter.GetAtPath(path);
        ISpriteMeshDataProvider Mesh() => Provider().GetDataProvider<ISpriteMeshDataProvider>();
        Sprite Sprite() => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single();
        static Vertex2DMetaData Vertex(float x, float y) => new Vertex2DMetaData { position = new Vector2(x, y) };

        string Snapshot()
        {
            var sprite = Sprite();
            var id = Provider().GetSpriteRects().Single().spriteID;
            var state = JsonUtility.ToJson(new SpriteState
            {
                metadata = Mesh().GetVertices(id), indices = Mesh().GetIndices(id), edges = Mesh().GetEdges(id),
                vertices = sprite.vertices, uv = sprite.uv, triangles = sprite.triangles,
                weights = sprite.HasVertexAttribute(VertexAttribute.BlendWeight) ? sprite.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight).ToArray() : null,
                bindPoses = sprite.GetBindPoses().ToArray(), bones = sprite.GetBones(), ppu = sprite.pixelsPerUnit
            });
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path).OrderBy(a => a.GetType().FullName + a.name))
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId);
                state += asset.GetType().FullName + asset.name + localId;
            }
            return state;
        }

        [Serializable] class SpriteState
        {
            public Vertex2DMetaData[] metadata;
            public int[] indices;
            public Vector2Int[] edges;
            public Vector2[] vertices, uv;
            public ushort[] triangles;
            public BoneWeight[] weights;
            public Matrix4x4[] bindPoses;
            public SpriteBone[] bones;
            public float ppu;
        }

        // Minimal uncompressed RGB+alpha PSB with a flattened 32x32 image.
        static void WritePsb(string filename, byte red)
        {
            using (var stream = File.Create(filename))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { 56, 66, 80, 83, 0, 2, 0, 0, 0, 0, 0, 0, 0, 4 });
                writer.Write(new byte[] { 0, 0, 0, 32, 0, 0, 0, 32, 0, 8, 0, 3 });
                writer.Write(new byte[18]); // Color data, image resources, 64-bit layer/mask length, compression.
                foreach (var channel in new[] { red, (byte)100, (byte)50, (byte)255 })
                    for (var pixel = 0; pixel < 1024; ++pixel) writer.Write(channel);
            }
        }
    }
}
