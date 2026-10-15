using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    internal sealed class SceneSpriteWireframe
    {
        Vector3[] m_WireVertices;
        readonly SceneSpriteDeformedMesh m_Mesh = new SceneSpriteDeformedMesh();
        readonly Vector3[] m_WireTriangle = new Vector3[4];

        internal void Draw(SpriteRenderer renderer, bool hasAuthoredMesh)
        {
            if (Event.current.type != EventType.Repaint || renderer == null ||
                !Selection.Contains(renderer.gameObject)) return;
            var skin = renderer.GetComponent<SpriteSkin>();
            var binding = renderer.GetComponentInParent<UniskinRigBinding>(true);
            if (skin == null && (binding == null || binding.rig == null)) return;
            // Scene authoring windows draw their own staged mesh while hiding this renderer.
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || renderer.sprite == null) return;
            var sprite = renderer.sprite;
            if (!hasAuthoredMesh)
            {
                // Match Create Mesh's source-frame quad, not Unity's automatic tight outline.
                if (m_WireVertices == null || m_WireVertices.Length != 4) m_WireVertices = new Vector3[4];
                var minimum = -sprite.pivot / sprite.pixelsPerUnit;
                var maximum = (sprite.rect.size - sprite.pivot) / sprite.pixelsPerUnit;
                m_WireVertices[0] = ToWorld(renderer, new Vector3(minimum.x, minimum.y));
                m_WireVertices[1] = ToWorld(renderer, new Vector3(maximum.x, minimum.y));
                m_WireVertices[2] = ToWorld(renderer, new Vector3(minimum.x, maximum.y));
                m_WireVertices[3] = ToWorld(renderer, new Vector3(maximum.x, maximum.y));
                DrawTriangles(default, true);
                return;
            }
            if (!m_Mesh.Sample(renderer)) return;
            m_WireVertices = m_Mesh.vertices;
            DrawTriangles(m_Mesh.indices, false);
        }
        static Vector3 ToWorld(SpriteRenderer renderer, Vector3 position)
        {
            if (renderer.flipX) position.x = -position.x;
            if (renderer.flipY) position.y = -position.y;
            return renderer.transform.TransformPoint(position);
        }

        void DrawTriangles(NativeArray<ushort> indices, bool quad)
        {
            var previousDepth = Handles.zTest;
            try
            {
                Handles.zTest = CompareFunction.Always;
                using (new Handles.DrawingScope(MeshEditorSettings.meshLineColor, Matrix4x4.identity))
                {
                    if (quad)
                    {
                        DrawTriangle(0, 2, 1);
                        DrawTriangle(1, 2, 3);
                    }
                    else
                        for (var i = 0; i + 2 < indices.Length; i += 3)
                            DrawTriangle(indices[i], indices[i + 1], indices[i + 2]);
                }
            }
            finally { Handles.zTest = previousDepth; }
        }

        void DrawTriangle(int a, int b, int c)
        {
            m_WireTriangle[0] = m_WireVertices[a];
            m_WireTriangle[1] = m_WireVertices[b];
            m_WireTriangle[2] = m_WireVertices[c];
            m_WireTriangle[3] = m_WireTriangle[0];
            Handles.DrawAAPolyLine(1.5f, m_WireTriangle);
        }

    }
}
