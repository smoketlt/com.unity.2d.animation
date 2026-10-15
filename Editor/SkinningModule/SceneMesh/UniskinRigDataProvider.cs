using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    [Serializable]
    internal sealed class UniskinRigSpriteData
    {
        public SpriteRect rect;
        public SpriteBone[] bones;
        public Vertex2DMetaData[] vertices;
        public int[] indices;
        public Vector2Int[] edges;
    }

    [Serializable]
    internal sealed class UniskinRigData
    {
        public float pixelsPerUnit;
        public bool hasCharacter;
        public CharacterData character;
        public string userData;
        public UniskinRigSpriteData[] sprites;

        internal static UniskinRigData Read(ISpriteEditorDataProvider provider, string userData)
        {
            var mesh = provider.GetDataProvider<ISpriteMeshDataProvider>();
            var bones = provider.GetDataProvider<ISpriteBoneDataProvider>();
            var character = provider.GetDataProvider<ICharacterDataProvider>();
            return Clone(new UniskinRigData
            {
                pixelsPerUnit = provider.pixelsPerUnit,
                hasCharacter = character != null,
                character = character?.GetCharacterData() ?? default,
                userData = userData ?? string.Empty,
                sprites = provider.GetSpriteRects().Select(rect => new UniskinRigSpriteData
                {
                    rect = rect,
                    bones = bones.GetBones(rect.spriteID)?.ToArray() ?? Array.Empty<SpriteBone>(),
                    vertices = mesh.GetVertices(rect.spriteID) ?? Array.Empty<Vertex2DMetaData>(),
                    indices = mesh.GetIndices(rect.spriteID) ?? Array.Empty<int>(),
                    edges = mesh.GetEdges(rect.spriteID) ?? Array.Empty<Vector2Int>()
                }).ToArray()
            });
        }

        [Serializable] sealed class Box<T> { public T value; }
        internal static T Clone<T>(T value) => JsonUtility.FromJson<Box<T>>(JsonUtility.ToJson(new Box<T> { value = value })).value;
        internal string Serialize() => JsonUtility.ToJson(this);

        internal bool MatchesSerialized(string baseline)
        {
            if (string.IsNullOrEmpty(baseline)) return false;
            try
            {
                // Compare through the current serializer. Unity releases can print
                // different decimal tails for the exact same stored float value.
                var previous = JsonUtility.FromJson<UniskinRigData>(baseline);
                return previous?.sprites != null && Serialize() == previous.Serialize();
            }
            catch (ArgumentException) { return false; }
        }
    }

    // Own a detached document per provider instance. Staged setters never mutate the
    // asset; Apply is the transaction boundary used by all three Scene tools.
    internal sealed class UniskinRigDataProvider : ISpriteEditorDataProvider, ISpriteMeshDataProvider, ISpriteBoneDataProvider
    {
        internal readonly UniskinRig rig;
        internal readonly UniskinClippingMaskData clipping;
        readonly ISpriteEditorDataProvider m_Source;
        internal UniskinRigData data;
        readonly CharacterProvider m_Character;

        internal UniskinRigDataProvider(UniskinRig rig, ISpriteEditorDataProvider source)
        {
            this.rig = rig;
            m_Source = source;
            if (rig.schema != 1) throw new InvalidOperationException("This UniskinRig schema is not supported.");
            data = JsonUtility.FromJson<UniskinRigData>(rig.authoringData);
            if (data == null || data.sprites == null) throw new InvalidOperationException("The UniskinRig authoring data is incomplete.");
            m_Character = data.hasCharacter ? new CharacterProvider(this) : null;
        }

        internal UniskinRigDataProvider(UniskinClippingMaskData clipping, ISpriteEditorDataProvider source)
        {
            this.clipping = clipping;
            rig = clipping.rig;
            m_Source = source;
            data = JsonUtility.FromJson<UniskinRigData>(clipping.authoringData);
            if (data?.sprites == null || !data.hasCharacter) throw new InvalidOperationException("The clipping contour data is incomplete.");
            // Contours not loaded during a domain reload still need corrected
            // pixel-space skeleton coordinates when first opened for editing.
            var borrowed = UniskinClippingMaskUtility.BorrowedBones(clipping, data.pixelsPerUnit, data.sprites[0].rect);
            if (data.character.bones.Select(bone => bone.guid).SequenceEqual(borrowed.Select(bone => bone.guid)))
                data.character.bones = borrowed;
            m_Character = new CharacterProvider(this);
        }

        public SpriteImportMode spriteImportMode => m_Source.spriteImportMode;
        public float pixelsPerUnit => data.pixelsPerUnit;
        public UnityEngine.Object targetObject => clipping != null ? (UnityEngine.Object)clipping : rig;
        public void InitSpriteEditorDataProvider() { }
        public SpriteRect[] GetSpriteRects() => UniskinRigData.Clone(data.sprites.Select(sprite => sprite.rect).ToArray());
        public void SetSpriteRects(SpriteRect[] rects)
        {
            if (rects.Length != data.sprites.Length) throw new InvalidOperationException("Layer creation/removal belongs to the source PSB.");
            foreach (var rect in rects) Find(rect.spriteID).rect = UniskinRigData.Clone(rect);
        }
        public T GetDataProvider<T>() where T : class
        {
            if (typeof(T) == typeof(ICharacterDataProvider)) return m_Character as T;
            if (this is T provider) return provider;
            // Texture, outlines and source identity remain owned by the source importer.
            return m_Source.GetDataProvider<T>();
        }
        public bool HasDataProvider(Type type) => type == typeof(ICharacterDataProvider) ? m_Character != null :
            type.IsInstanceOfType(this) || m_Source.HasDataProvider(type);
        public void Apply()
        {
            if (clipping != null) UniskinClippingMaskUtility.Save(this);
            else UniskinRigUtility.Save(this);
        }
        UniskinRigSpriteData Find(GUID id) => data.sprites.First(sprite => sprite.rect.spriteID == id);
        public List<SpriteBone> GetBones(GUID id) => UniskinRigData.Clone(Find(id).bones).ToList();
        public void SetBones(GUID id, List<SpriteBone> bones) => Find(id).bones = UniskinRigData.Clone(bones.ToArray());
        public Vertex2DMetaData[] GetVertices(GUID id) => (Vertex2DMetaData[])Find(id).vertices.Clone();
        public void SetVertices(GUID id, Vertex2DMetaData[] vertices) => Find(id).vertices = (Vertex2DMetaData[])vertices.Clone();
        public int[] GetIndices(GUID id) => (int[])Find(id).indices.Clone();
        public void SetIndices(GUID id, int[] indices) => Find(id).indices = (int[])indices.Clone();
        public Vector2Int[] GetEdges(GUID id) => (Vector2Int[])Find(id).edges.Clone();
        public void SetEdges(GUID id, Vector2Int[] edges) => Find(id).edges = (Vector2Int[])edges.Clone();

        sealed class CharacterProvider : ICharacterDataProvider
        {
            readonly UniskinRigDataProvider m_Owner;
            internal CharacterProvider(UniskinRigDataProvider owner) => m_Owner = owner;
            public CharacterData GetCharacterData() => UniskinRigData.Clone(m_Owner.data.character);
            public void SetCharacterData(CharacterData data) => m_Owner.data.character = UniskinRigData.Clone(data);
        }
    }
}
