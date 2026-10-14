using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace UnityEditor.U2D.Animation
{
    internal sealed partial class SceneEditWeightsWindow
    {
        SpriteRenderer m_SortedPreview;
        Sprite m_SortedPreviewSprite;
        Texture2D m_SortedPreviewTexture;
        MaterialPropertyBlock m_SortedPreviewProperties;

        void UpdateSortedPreview(Vector3[] world)
        {
            var source = m_Renderer.sprite;
            if (m_SortedPreview == null)
            {
                var preview = new GameObject("Edit Weights Preview") { hideFlags = HideFlags.HideAndDontSave };
                preview.transform.SetParent(m_Renderer.transform, false);
                m_SortedPreview = preview.AddComponent<SpriteRenderer>();
            }
            if (m_SortedPreviewSprite == null || m_SortedPreviewTexture != source.texture)
            {
                if (m_SortedPreviewSprite != null) DestroyImmediate(m_SortedPreviewSprite);
                m_SortedPreviewTexture = source.texture;
                m_SortedPreviewSprite = Sprite.Create(source.texture, source.textureRect, Vector2.one * .5f, source.pixelsPerUnit);
                m_SortedPreviewSprite.hideFlags = HideFlags.HideAndDontSave;
            }
            var positions = new NativeArray<Vector3>(world.Length, Allocator.Temp);
            var uv = new NativeArray<Vector2>(world.Length, Allocator.Temp);
            var indices = new NativeArray<ushort>(Array.ConvertAll(m_Session.DrawIndices(), index => checked((ushort)index)), Allocator.Temp);
            var bounds = new Bounds();
            try
            {
                var mesh = m_Session.mesh;
                var textureSize = new Vector2(source.texture.width, source.texture.height);
                for (var i = 0; i < world.Length; ++i)
                {
                    positions[i] = m_Renderer.transform.InverseTransformPoint(world[i]);
                    if (i == 0) bounds = new Bounds(positions[i], Vector3.zero);
                    else bounds.Encapsulate(positions[i]);
                    var pixel = m_Session.bindingPose?.TexturePosition(mesh.vertices[i], i) ?? mesh.vertices[i];
                    uv[i] = (source.textureRect.position - source.textureRectOffset + Vector2.Scale(pixel / m_Source.rect.rect.size, source.rect.size)) / textureSize;
                }
                m_SortedPreviewSprite.SetVertexCount(world.Length);
                m_SortedPreviewSprite.SetVertexAttribute(VertexAttribute.Position, positions);
                m_SortedPreviewSprite.SetVertexAttribute(VertexAttribute.TexCoord0, uv);
                m_SortedPreviewSprite.SetIndices(indices);
            }
            finally { positions.Dispose(); uv.Dispose(); indices.Dispose(); }
            m_SortedPreview.gameObject.layer = m_Renderer.gameObject.layer;
            m_SortedPreview.sprite = m_SortedPreviewSprite;
            m_SortedPreview.localBounds = bounds;
            m_SortedPreview.color = m_Renderer.color;
            m_SortedPreview.sharedMaterials = m_Renderer.sharedMaterials;
            m_SortedPreview.sortingLayerID = m_Renderer.sortingLayerID;
            m_SortedPreview.sortingOrder = m_Renderer.sortingOrder;
            m_SortedPreview.spriteSortPoint = m_Renderer.spriteSortPoint;
            m_SortedPreview.maskInteraction = m_Renderer.maskInteraction;
            m_SortedPreview.renderingLayerMask = m_Renderer.renderingLayerMask;
            if (m_SortedPreviewProperties == null) m_SortedPreviewProperties = new MaterialPropertyBlock();
            m_Renderer.GetPropertyBlock(m_SortedPreviewProperties);
            m_SortedPreview.SetPropertyBlock(m_SortedPreviewProperties);
            m_SortedPreview.enabled = m_Renderer.enabled && !m_PreviousForceRenderingOff;
        }

        void ClearSortedPreview()
        {
            if (m_SortedPreview != null) DestroyImmediate(m_SortedPreview.gameObject);
            if (m_SortedPreviewSprite != null) DestroyImmediate(m_SortedPreviewSprite);
            m_SortedPreview = null;
            m_SortedPreviewSprite = null;
            m_SortedPreviewTexture = null;
        }
    }
}
