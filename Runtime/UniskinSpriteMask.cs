using System;
using UnityEngine.U2D;

namespace UnityEngine.U2D.Animation
{
    /// <summary>Samples a separate alpha mask on the existing Sprite surface.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer))]
    [AddComponentMenu("2D Animation/Uniskin Sprite Mask")]
    [DefaultExecutionOrder(UpdateOrder.spriteResolverUpdateOrder + 2)]
    public sealed class UniskinSpriteMask : MonoBehaviour
    {
        [SerializeField, Tooltip("The mask's alpha channel reveals the Sprite. The image can be outside the PSB.")]
        Sprite m_Mask;
        [SerializeField, Tooltip("Optional renderer used by Capture Placement. Its Transform is captured once; it is not followed during animation.")]
        SpriteRenderer m_PlacementSource;
        [SerializeField] bool m_Invert;
        [SerializeField, Range(0, 1), Tooltip("Zero preserves soft alpha. A positive value discards mask alpha below this threshold.")]
        float m_AlphaCutoff;
        [SerializeField, Tooltip("Offset in normalized mask coordinates.")] Vector2 m_Offset;
        [SerializeField] Vector2 m_Scale = Vector2.one;
        [SerializeField] float m_Rotation;
        [SerializeField, Tooltip("Optional shared material using the Uniskin/UniskinMask shader.")]
        Material m_Material;
        [SerializeField, HideInInspector] Matrix4x4 m_Placement = Matrix4x4.identity;
        [SerializeField, HideInInspector] Material m_PreviousMaterial;
        [SerializeField, HideInInspector] SpriteMaskInteraction m_PreviousInteraction;
        [SerializeField, HideInInspector] bool m_OwnsRenderer;
        [SerializeField, HideInInspector] SpriteMask m_DisabledMask;
        [SerializeField, HideInInspector] bool m_PreviousMaskEnabled;

        static readonly int s_Texture = Shader.PropertyToID("_UniskinMaskTexture");
        static readonly int s_UV0 = Shader.PropertyToID("_UniskinMaskUV0");
        static readonly int s_UV1 = Shader.PropertyToID("_UniskinMaskUV1");
        static readonly int s_Local0 = Shader.PropertyToID("_UniskinMaskLocal0");
        static readonly int s_Local1 = Shader.PropertyToID("_UniskinMaskLocal1");
        static readonly int s_Enabled = Shader.PropertyToID("_UniskinMaskEnabled");
        static readonly int s_Invert = Shader.PropertyToID("_UniskinMaskInvert");
        static readonly int s_Cutoff = Shader.PropertyToID("_UniskinMaskCutoff");
        static Material s_LitMaterial, s_UnlitMaterial;

        SpriteRenderer m_Renderer;
        UniskinRigBinding m_RigBinding;
        MaterialPropertyBlock m_Properties;
        Material m_AppliedMaterial;
        Sprite m_LastSprite, m_LastMask;
        Texture m_LastTexture, m_LastMaskTexture;
        int m_LastRigRevision = -1;
        bool m_Dirty = true;

        public Sprite mask { get => m_Mask; set { if (m_Mask != value) { m_Mask = value; m_Dirty = true; } } }
        public SpriteRenderer placementSource { get => m_PlacementSource; set { m_PlacementSource = value; m_Dirty = true; } }
        public bool invert { get => m_Invert; set { m_Invert = value; m_Dirty = true; } }
        public float alphaCutoff { get => m_AlphaCutoff; set { m_AlphaCutoff = Mathf.Clamp01(value); m_Dirty = true; } }

        void OnEnable()
        {
            m_Renderer = GetComponent<SpriteRenderer>();
            m_RigBinding = GetComponentInParent<UniskinRigBinding>();
            SpriteAtlasManager.atlasRegistered += AtlasRegistered;
            Refresh();
        }

        void OnDisable()
        {
            SpriteAtlasManager.atlasRegistered -= AtlasRegistered;
            RestoreRenderer();
        }

        void OnValidate() => m_Dirty = true;
        void OnDidApplyAnimationProperties() => m_Dirty = true;
        void AtlasRegistered(SpriteAtlas atlas) => m_Dirty = true;

        void LateUpdate()
        {
            if (m_Renderer == null) return;
            var sprite = m_Renderer.sprite;
            var revision = m_RigBinding != null && m_RigBinding.rig != null ? m_RigBinding.rig.revision : -1;
            if (m_Dirty || sprite != m_LastSprite || m_Mask != m_LastMask ||
                (sprite != null && sprite.texture != m_LastTexture) ||
                (m_Mask != null && m_Mask.texture != m_LastMaskTexture) || revision != m_LastRigRevision)
                Refresh();
        }

        Sprite ReferenceSprite()
        {
            var sprite = m_Renderer.sprite;
            var entry = m_RigBinding != null && m_RigBinding.rig != null ? m_RigBinding.rig.Find(sprite) : null;
            return entry != null && entry.source != null ? entry.source : sprite;
        }

        public void CapturePlacement()
        {
            if (m_Renderer == null) m_Renderer = GetComponent<SpriteRenderer>();
            if (m_PlacementSource == null || m_PlacementSource == m_Renderer || m_PlacementSource.sprite == null || m_Renderer.sprite == null)
                throw new InvalidOperationException("Choose a separate mask SpriteRenderer as Placement Source.");
            var body = ReferenceSprite();
            var source = m_PlacementSource.sprite;
            var bodyFlip = Matrix4x4.Scale(new Vector3(m_Renderer.flipX ? -1 : 1, m_Renderer.flipY ? -1 : 1, 1));
            var maskFlip = Matrix4x4.Scale(new Vector3(m_PlacementSource.flipX ? -1 : 1, m_PlacementSource.flipY ? -1 : 1, 1));
            var placement = NormalizedToLocal(source).inverse * maskFlip * m_PlacementSource.transform.worldToLocalMatrix *
                m_Renderer.transform.localToWorldMatrix * bodyFlip * NormalizedToLocal(body);
            if (!Finite(placement) || Mathf.Abs(placement.determinant) < 1e-12f)
                throw new InvalidOperationException("Mask placement needs finite Transforms with non-zero scale.");
            m_Mask = source;
            m_Placement = placement;
            m_Offset = Vector2.zero; m_Scale = Vector2.one; m_Rotation = 0;
            var legacy = m_PlacementSource.GetComponent<SpriteMask>();
            if (legacy != null) m_AlphaCutoff = legacy.alphaCutoff;
            m_Dirty = true;
            if (isActiveAndEnabled) Refresh();
        }

        public void ResetPlacement()
        {
            m_Placement = Matrix4x4.identity;
            m_Offset = Vector2.zero; m_Scale = Vector2.one; m_Rotation = 0;
            m_Dirty = true;
            if (isActiveAndEnabled) Refresh();
        }

        public void Refresh()
        {
            if (m_Renderer == null) m_Renderer = GetComponent<SpriteRenderer>();
            if (!isActiveAndEnabled) return;
            if (m_Properties == null) m_Properties = new MaterialPropertyBlock();
            var sprite = m_Renderer.sprite;
            if (sprite == null || m_Mask == null)
            {
                RestoreRenderer(); Remember(); return;
            }
            var body = ReferenceSprite();
            // Generated rig meshes can have a mesh-specific reference pose. Use the
            // imported image's affine UV mapping, so the mask follows the texture.
            var bodyUVToNormalized = NormalizedToLocal(body).inverse * LocalToUV(body).inverse;
            var adjustment = Matrix4x4.TRS(new Vector3(.5f + m_Offset.x, .5f + m_Offset.y, 0),
                Quaternion.Euler(0, 0, m_Rotation), new Vector3(m_Scale.x, m_Scale.y, 1)) * Matrix4x4.Translate(new Vector3(-.5f, -.5f, 0));
            if (!Finite(adjustment) || Mathf.Abs(adjustment.determinant) < 1e-12f)
                throw new InvalidOperationException("Mask Scale must be finite and non-zero.");
            var normalized = adjustment.inverse * m_Placement * bodyUVToNormalized;
            var uv = LocalToUV(m_Mask) * NormalizedToLocal(m_Mask) * normalized;
            var material = m_Material != null ? m_Material : DefaultMaterial();
            if (material == null || material.shader == null || material.shader.name != "Uniskin/UniskinMask")
                throw new InvalidOperationException("Assign a material using the Uniskin/UniskinMask shader.");
            if (!m_OwnsRenderer)
            {
                m_PreviousMaterial = m_Renderer.sharedMaterial;
                m_PreviousInteraction = m_Renderer.maskInteraction;
                m_OwnsRenderer = true;
            }
            m_AppliedMaterial = material;
            m_Renderer.sharedMaterial = material;
            m_Renderer.maskInteraction = SpriteMaskInteraction.None;
            m_Renderer.GetPropertyBlock(m_Properties);
            m_Properties.SetTexture(s_Texture, m_Mask.texture);
            m_Properties.SetVector(s_UV0, Row(uv, 0)); m_Properties.SetVector(s_UV1, Row(uv, 1));
            m_Properties.SetVector(s_Local0, Row(normalized, 0)); m_Properties.SetVector(s_Local1, Row(normalized, 1));
            m_Properties.SetFloat(s_Enabled, 1); m_Properties.SetFloat(s_Invert, m_Invert ? 1 : 0);
            m_Properties.SetFloat(s_Cutoff, m_AlphaCutoff);
            m_Renderer.SetPropertyBlock(m_Properties);
            var legacy = m_PlacementSource != null && m_PlacementSource != m_Renderer ? m_PlacementSource.GetComponent<SpriteMask>() : null;
            if (legacy != m_DisabledMask)
            {
                RestoreLegacyMask();
                m_DisabledMask = legacy;
                if (legacy != null) m_PreviousMaskEnabled = legacy.enabled;
            }
            if (m_DisabledMask != null) m_DisabledMask.enabled = false;
            Remember();
        }

        Material DefaultMaterial()
        {
            var original = m_OwnsRenderer ? m_PreviousMaterial : m_Renderer.sharedMaterial;
            var unlit = original != null && original.shader != null &&
                (original.shader.name.Contains("Unlit") || original.shader.name == "Sprites/Default");
            if (unlit) return s_UnlitMaterial != null ? s_UnlitMaterial : s_UnlitMaterial = Resources.Load<Material>("UniskinMaskUnlit");
            return s_LitMaterial != null ? s_LitMaterial : s_LitMaterial = Resources.Load<Material>("UniskinMask");
        }

        void Remember()
        {
            m_LastSprite = m_Renderer.sprite; m_LastMask = m_Mask;
            m_LastTexture = m_LastSprite != null ? m_LastSprite.texture : null;
            m_LastMaskTexture = m_Mask != null ? m_Mask.texture : null;
            m_LastRigRevision = m_RigBinding != null && m_RigBinding.rig != null ? m_RigBinding.rig.revision : -1;
            m_Dirty = false;
        }

        void RestoreRenderer()
        {
            if (m_Renderer != null && m_OwnsRenderer)
            {
                if (m_AppliedMaterial == null || m_Renderer.sharedMaterial == m_AppliedMaterial)
                {
                    m_Renderer.sharedMaterial = m_PreviousMaterial;
                    m_Renderer.maskInteraction = m_PreviousInteraction;
                }
                if (m_Properties != null)
                {
                    m_Renderer.GetPropertyBlock(m_Properties);
                    m_Properties.SetFloat(s_Enabled, 0);
                    m_Renderer.SetPropertyBlock(m_Properties);
                }
            }
            m_OwnsRenderer = false; m_AppliedMaterial = null;
            RestoreLegacyMask();
        }

        void RestoreLegacyMask()
        {
            if (m_DisabledMask != null) m_DisabledMask.enabled = m_PreviousMaskEnabled;
            m_DisabledMask = null;
        }

        static Vector4 Row(Matrix4x4 matrix, int row) => new Vector4(matrix[row, 0], matrix[row, 1], matrix[row, 3], 0);
        internal static Matrix4x4 NormalizedToLocal(Sprite sprite) => Matrix4x4.TRS(-sprite.pivot / sprite.pixelsPerUnit,
            Quaternion.identity, new Vector3(sprite.rect.width / sprite.pixelsPerUnit, sprite.rect.height / sprite.pixelsPerUnit, 1));

        internal static Matrix4x4 LocalToUV(Sprite sprite)
        {
            var vertices = sprite.vertices; var uv = sprite.uv;
            if (vertices.Length < 3 || uv.Length != vertices.Length)
                throw new InvalidOperationException("The Sprite has no usable texture mapping.");
            var a = vertices[0];
            for (var i = 1; i < vertices.Length - 1; ++i)
                for (var j = i + 1; j < vertices.Length; ++j)
                {
                    var x = vertices[i] - a; var y = vertices[j] - a;
                    if (Mathf.Abs(x.x * y.y - x.y * y.x) < 1e-10f) continue;
                    var source = Matrix4x4.identity;
                    source.SetColumn(0, new Vector4(x.x, x.y, 0, 0));
                    source.SetColumn(1, new Vector4(y.x, y.y, 0, 0));
                    source.SetColumn(3, new Vector4(a.x, a.y, 0, 1));
                    x = uv[i] - uv[0]; y = uv[j] - uv[0];
                    var target = Matrix4x4.identity;
                    target.SetColumn(0, new Vector4(x.x, x.y, 0, 0));
                    target.SetColumn(1, new Vector4(y.x, y.y, 0, 0));
                    target.SetColumn(3, new Vector4(uv[0].x, uv[0].y, 0, 1));
                    var result = target * source.inverse;
                    if (!Finite(result) || Mathf.Abs(result.determinant) < 1e-12f)
                        throw new InvalidOperationException("The Sprite texture mapping is singular.");
                    return result;
                }
            throw new InvalidOperationException("The Sprite texture mapping needs a non-degenerate triangle.");
        }

        static bool Finite(Matrix4x4 matrix)
        {
            for (var i = 0; i < 16; ++i) if (!float.IsFinite(matrix[i])) return false;
            return true;
        }
    }
}
