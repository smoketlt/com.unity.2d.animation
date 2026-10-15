using Unity.Collections;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace UnityEngine.U2D.Animation
{
    /// <summary>Exposes a SpriteRenderer's posed mesh and alpha to ordinary SpriteMask interaction.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer))]
    [AddComponentMenu("2D Animation/Uniskin Mesh Mask")]
    [DefaultExecutionOrder(12)]
    public sealed class UniskinMeshMask : MonoBehaviour
    {
        [SerializeField, Range(0, 1), Tooltip("Mask pixels below this alpha are discarded.")]
        float m_AlphaCutoff = .01f;
        [SerializeField] int m_BackSortingLayerID = 0;
        [SerializeField] int m_BackSortingOrder = short.MinValue;
        [SerializeField] int m_FrontSortingLayerID = 0;
        [SerializeField] int m_FrontSortingOrder = short.MaxValue;

        SpriteRenderer m_Source;
        SpriteSkin m_Skin;
        Sprite m_SourceSprite, m_PosedSprite;
        // Helpers are transient and owned by this instance, never by the source asset.
        [SerializeField, HideInInspector] SpriteMask m_Mask;
        NativeArray<Vector3> m_PosedVertices;
        NativeArray<Vector3> m_SourceVertices;
        NativeArray<BoneWeight> m_Weights;
        NativeArray<ushort> m_Indices;
        NativeArray<Vector2> m_UVs;
        Texture2D m_Texture;
        Matrix4x4[] m_BoneMatrices;
        bool m_Dirty = true;

        public float alphaCutoff { get => m_AlphaCutoff; set { m_AlphaCutoff = Mathf.Clamp01(value); } }

        void OnEnable()
        {
            m_Source = GetComponent<SpriteRenderer>();
            SpriteAtlasManager.atlasRegistered += AtlasRegistered;
            m_Dirty = true;
        }

        void OnValidate() => m_Dirty = true;
        void AtlasRegistered(SpriteAtlas atlas) => m_Dirty = true;
        void LateUpdate() => Refresh();

        /// <summary>Refresh after changing a source mesh in place, or after sampling an animation manually.</summary>
        public void Refresh()
        {
            if (m_Source == null) m_Source = GetComponent<SpriteRenderer>();
            // Instantiate can copy a DontSave child while omitting its serialized reference.
            // Reclaim that instance's helper instead of leaving a second active mask behind.
            if (m_Mask != null && m_Mask.transform.parent != transform) m_Mask = null;
            if (m_Mask == null)
                for (var i = 0; i < transform.childCount; ++i)
                {
                    var child = transform.GetChild(i);
                    if (child.name == "Uniskin Mesh Mask (generated)" && child.gameObject.hideFlags == HideFlags.HideAndDontSave)
                    {
                        m_Mask = child.GetComponent<SpriteMask>();
                        if (m_Mask != null) break;
                    }
                }
            if (!isActiveAndEnabled || m_Source.sprite == null || !m_Source.enabled)
            {
                if (m_Mask != null) m_Mask.enabled = false;
                return;
            }
            var source = m_Source.sprite;
            if (source.GetVertexCount() == 0 || source.GetIndices().Length == 0)
            {
                if (m_Mask != null) m_Mask.enabled = false;
                return;
            }
            if (m_Dirty || m_SourceSprite != source || m_PosedSprite == null || source.texture != m_Texture ||
                source.GetVertexCount() != m_PosedVertices.Length || source.GetIndices().Length != m_Indices.Length)
                Rebuild(source);
            if (m_Mask == null)
            {
                var helper = new GameObject("Uniskin Mesh Mask (generated)") { hideFlags = HideFlags.HideAndDontSave };
                helper.transform.SetParent(transform, false);
                m_Mask = helper.AddComponent<SpriteMask>();
            }
            m_Skin = GetComponent<SpriteSkin>();
            var bindPoses = source.GetBindPoses();
            var bones = m_Skin != null && m_Skin.isActiveAndEnabled ? m_Skin.boneTransforms : null;
            bool skinned = bones != null && bindPoses.Length > 0 && bones.Length == bindPoses.Length &&
                m_Skin.rootBone != null && source.HasVertexAttribute(VertexAttribute.BlendWeight);
            if (skinned)
                for (var i = 0; i < bones.Length; ++i) if (bones[i] == null) { skinned = false; break; }
            if (skinned)
            {
                if (m_BoneMatrices == null || m_BoneMatrices.Length != bones.Length) m_BoneMatrices = new Matrix4x4[bones.Length];
                var inverse = transform.worldToLocalMatrix;
                for (var i = 0; i < bones.Length; ++i) m_BoneMatrices[i] = inverse * bones[i].localToWorldMatrix * bindPoses[i];
            }
            if (skinned)
            {
                var weights = source.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight);
                skinned = weights.Length == m_PosedVertices.Length;
                if (skinned) weights.CopyTo(m_Weights);
            }
            bool changed = m_Dirty;
            // Unity's Sprite getters can invalidate earlier borrowed stream views.
            // Consume each view before obtaining the next one.
            var indices = source.GetIndices();
            for (var i = 0; i < indices.Length; ++i)
            {
                if (m_Indices[i] != indices[i]) changed = true;
                m_Indices[i] = indices[i];
            }
            var uvs = source.GetVertexAttribute<Vector2>(VertexAttribute.TexCoord0);
            for (var i = 0; i < uvs.Length; ++i)
            {
                if (!m_UVs[i].Equals(uvs[i])) changed = true;
                m_UVs[i] = uvs[i];
            }
            source.GetVertexAttribute<Vector3>(VertexAttribute.Position).CopyTo(m_SourceVertices);
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (var i = 0; i < m_SourceVertices.Length; ++i)
            {
                Vector3 p = m_SourceVertices[i];
                if (skinned)
                {
                    var w = m_Weights[i];
                    p = Weighted(p, w.boneIndex0, w.weight0) + Weighted(p, w.boneIndex1, w.weight1) +
                        Weighted(p, w.boneIndex2, w.weight2) + Weighted(p, w.boneIndex3, w.weight3);
                }
                if (m_Source.flipX) p.x = -p.x;
                if (m_Source.flipY) p.y = -p.y;
                if (!m_PosedVertices[i].Equals(p)) changed = true;
                m_PosedVertices[i] = p;
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }
            if (changed)
            {
                // Keep UVs from the original mesh, including packed/rotated atlas UVs.
                m_PosedSprite.SetVertexAttribute(VertexAttribute.Position, m_PosedVertices);
                m_PosedSprite.SetIndices(m_Indices);
                m_PosedSprite.SetVertexAttribute(VertexAttribute.TexCoord0, m_UVs);
                m_Mask.sprite = null; // invalidate the native mask's cached geometry
            }
            m_Mask.sprite = m_PosedSprite;
            m_Mask.localBounds = new Bounds((min + max) * .5f, max - min + Vector3.one * .001f);
            m_Mask.alphaCutoff = m_AlphaCutoff;
            m_Mask.isCustomRangeActive = true;
            m_Mask.backSortingLayerID = m_BackSortingLayerID;
            m_Mask.backSortingOrder = m_BackSortingOrder;
            m_Mask.frontSortingLayerID = m_FrontSortingLayerID;
            m_Mask.frontSortingOrder = m_FrontSortingOrder;
            m_Mask.gameObject.layer = gameObject.layer;
            m_Mask.enabled = true;
            m_Dirty = false;
        }

        Vector3 Weighted(Vector3 vertex, int index, float weight)
        {
            return weight > 0 && index >= 0 && index < m_BoneMatrices.Length
                ? m_BoneMatrices[index].MultiplyPoint3x4(vertex) * weight : Vector3.zero;
        }

        void Rebuild(Sprite source)
        {
            ReleaseSprite();
            m_SourceSprite = source;
            m_Texture = source.texture;
            m_Indices = new NativeArray<ushort>(source.GetIndices().Length, Allocator.Persistent);
            m_PosedVertices = new NativeArray<Vector3>(source.GetVertexCount(), Allocator.Persistent);
            m_SourceVertices = new NativeArray<Vector3>(source.GetVertexCount(), Allocator.Persistent);
            m_Weights = new NativeArray<BoneWeight>(source.GetVertexCount(), Allocator.Persistent);
            m_UVs = new NativeArray<Vector2>(source.GetVertexCount(), Allocator.Persistent);
            m_PosedSprite = Sprite.Create(source.texture, new Rect(0, 0, source.texture.width, source.texture.height),
                Vector2.one * .5f, source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            m_PosedSprite.name = source.name + " (Uniskin Mesh Mask pose)";
            m_PosedSprite.hideFlags = HideFlags.HideAndDontSave;
            m_PosedSprite.SetVertexCount(source.GetVertexCount());
            m_Dirty = true;
        }

        void ReleaseSprite()
        {
            if (m_Mask != null) m_Mask.sprite = null;
            if (m_PosedSprite != null) DestroyOwned(m_PosedSprite);
            m_PosedSprite = null;
            if (m_UVs.IsCreated) m_UVs.Dispose();
            if (m_Indices.IsCreated) m_Indices.Dispose();
            if (m_PosedVertices.IsCreated) m_PosedVertices.Dispose();
            if (m_SourceVertices.IsCreated) m_SourceVertices.Dispose();
            if (m_Weights.IsCreated) m_Weights.Dispose();
        }

        void OnDisable()
        {
            SpriteAtlasManager.atlasRegistered -= AtlasRegistered;
            if (m_Mask != null && m_Mask.transform.parent != transform) m_Mask = null;
            if (m_Mask != null && m_Mask.transform.parent == transform)
            {
                m_Mask.enabled = false;
                DestroyOwned(m_Mask.gameObject, !gameObject.activeInHierarchy);
                m_Mask = null;
            }
            ReleaseSprite();
            m_SourceSprite = null;
        }

        void DestroyOwned(Object item, bool defer = false)
        {
            if (Application.isPlaying) Destroy(item);
            else
            {
#if UNITY_EDITOR
                if (defer)
                {
                    UnityEditor.EditorApplication.delayCall += () => DestroyDeferred(item);
                    return;
                }
#endif
                DestroyImmediate(item);
            }
        }

#if UNITY_EDITOR
        void DestroyDeferred(Object item)
        {
            if (item == null) return;
            // Undo or a rapid reactivation may reclaim the disabled native helper.
            if (this != null && m_Mask != null && item == m_Mask.gameObject) return;
            DestroyImmediate(item);
        }
#endif
    }
}
