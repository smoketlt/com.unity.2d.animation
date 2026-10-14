using System;
using UnityEngine;
using UnityEngine.U2D;

namespace UnityEngine.U2D.Animation
{
    /// <summary>Persistent skinning data and generated Sprites, independent of Photoshop import.</summary>
    public sealed class UniskinRig : ScriptableObject
    {
        [Serializable]
        public sealed class SpriteEntry
        {
            public string id;
            public Sprite source;
            public Sprite sprite;
        }

        [HideInInspector] public int schema = 1;
        [HideInInspector] public string sourceGuid;
        [HideInInspector] public string authoringData;
        [HideInInspector] public string bakedData;
        [HideInInspector] public string sourceBaseline;
        [HideInInspector] public string sourceDependencyHash;
        [HideInInspector] public string sourceFileHash;
        [HideInInspector] public bool editingEnabled = true;
        [HideInInspector] public int revision;
        [HideInInspector] public float pixelsPerUnit = 100f;
        [HideInInspector] public Vector2 rootOffset;
        [HideInInspector] public SpriteBone[] characterBones = Array.Empty<SpriteBone>();
        [HideInInspector] public SpriteEntry[] sprites = Array.Empty<SpriteEntry>();

        public bool hasUnbakedChanges => authoringData != bakedData;

        public SpriteEntry Find(Sprite sprite)
        {
            if (sprite == null) return null;
            return Array.Find(sprites, entry => entry.source == sprite || entry.sprite == sprite);
        }

        public SpriteEntry Find(string id) => Array.Find(sprites, entry => entry.id == id);
    }
}
