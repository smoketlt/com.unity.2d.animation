using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace UnityEditor.U2D.Animation
{
    internal partial class SceneEditMeshWindow
    {
        SpriteRenderer m_ClippingPreview;
        Sprite m_ClippingPreviewSprite;

        void UpdateClippingPreview()
        {
            if (m_ClippingOwner == null) return;
            if (!m_Masked || m_Session.mode == 3 || m_Session.mesh.indices.Length == 0 || BlockReason() != null)
            {
                ClearClippingPreview(); return;
            }
            if (m_ClippingPreview == null)
            {
                var go = new GameObject("Clipping Mask Editor Preview") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(m_Renderer.transform, false);
                m_ClippingPreview = go.AddComponent<SpriteRenderer>(); m_ClippingPreview.enabled = false;
                m_ClippingPreviewSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), Vector2.one * .5f, 1);
                m_ClippingPreviewSprite.hideFlags = HideFlags.HideAndDontSave;
                m_ClippingPreview.sprite = m_ClippingPreviewSprite;
            }
            var mesh = Deformation()?.mesh ?? m_Session.mesh;
            var positions = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp);
            var uv = new NativeArray<Vector2>(mesh.vertexCount, Allocator.Temp);
            var indices = new NativeArray<ushort>(Array.ConvertAll(mesh.indices, i => checked((ushort)i)), Allocator.Temp);
            var matrix = m_Renderer.transform.worldToLocalMatrix * SceneMeshSession.PixelToWorld(m_Renderer, m_Session.mesh.frame);
            var bounds = new Bounds();
            try
            {
                for (var i = 0; i < mesh.vertexCount; ++i)
                {
                    positions[i] = matrix.MultiplyPoint3x4(mesh.vertices[i]);
                    if (i == 0) bounds = new Bounds(positions[i], Vector3.zero); else bounds.Encapsulate(positions[i]);
                }
                m_ClippingPreviewSprite.SetVertexCount(mesh.vertexCount);
                m_ClippingPreviewSprite.SetVertexAttribute(VertexAttribute.Position, positions);
                m_ClippingPreviewSprite.SetVertexAttribute(VertexAttribute.TexCoord0, uv);
                m_ClippingPreviewSprite.SetIndices(indices);
                m_ClippingPreview.localBounds = bounds;
            }
            finally { positions.Dispose(); uv.Dispose(); indices.Dispose(); }
            m_ClippingOwner.SetEditingPreview(m_ClippingPreview);
        }

        void ClearClippingPreview()
        {
            if (m_ClippingPreview == null) return;
            if (m_ClippingOwner != null) m_ClippingOwner.SetEditing(true);
            DestroyImmediate(m_ClippingPreview.gameObject); DestroyImmediate(m_ClippingPreviewSprite);
            m_ClippingPreview = null; m_ClippingPreviewSprite = null;
        }
    }
}
