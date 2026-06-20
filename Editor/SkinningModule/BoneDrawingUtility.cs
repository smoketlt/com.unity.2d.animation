using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal static class BoneDrawingUtility
    {
        const float kCircleSize = 24f;
        const float kHeadWidth = 26f;
        const float kHeadLength = 20f;
        const float kStretchWidth = 26f;
        const float kEndCapSize = 10f;
        const float kSliceOverlap = 0f;
        const float kMinimumTexturedBoneLength = kHeadLength + kEndCapSize + 2f;
        const float kParentLinkDashLength = 4f;
        const float kParentLinkDashGap = 3f;
        const float kParentLinkLineWidth = 2f;
        const float kParentLinkArrowLength = 12f;
        const float kParentLinkArrowHalfWidth = 6f;
        const float kParentLinkMinimumArrowLength = 5f;

        static readonly int[] s_BonePartIndices = { 0, 1, 2, 0, 2, 3 };
        static readonly Vector2[] s_BonePartUVs =
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        };
        static Mesh s_BonePartMesh;
        static Material s_BonePartMaterial;

        public static float GetBoneRadius(Vector3 position, float scale = 1.0f)
        {
            if (Camera.current != null)
            {
                return 0.15f * scale * HandleUtility.GetHandleSize(position);
            }

            return 10f * scale / Handles.matrix.GetColumn(0).magnitude;
        }

        public static void DrawBoneNode(Vector3 position, Vector3 forward, Color color, float scale = 1.0f)
        {
            float displayScale = scale * SkinningModuleSettings.boneDisplayScale;

            if (scale <= 1f)
                DrawTextureAt(position, B_Circle, color, kCircleSize * displayScale, kCircleSize * displayScale);
            else
                DrawTextureAt(position, B_Circle_Selected, color, kCircleSize * displayScale, kCircleSize * displayScale);
        }

        public static void DrawBone(Vector3 position, Vector3 endPosition, Vector3 forward, Color color, float scale = 1.0f)
        {
            DrawTexturedBone(position, endPosition, forward, color, color, false, scale);
        }

        public static void DrawBone(Vector3 position, Vector3 endPosition, Vector3 forward, Color color, Color endCapColor, float scale = 1.0f)
        {
            DrawTexturedBone(position, endPosition, forward, color, endCapColor, false, scale);
        }

        public static void DrawBoneParentLink(Vector3 position, Vector3 endPosition, Vector3 forward, Color color)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            Vector3 link = endPosition - position;
            if (link.sqrMagnitude <= Mathf.Epsilon)
                return;

            Color handlesColor = Handles.color;
            Handles.color = color;
            DrawingUtility.BeginSolidLines();
            DrawParentLinkDashes(position, endPosition, forward);
            DrawParentLinkArrow(position, endPosition, forward);
            DrawingUtility.EndLines();
            Handles.color = handlesColor;
        }

        public static void DrawBoneOutline(Vector3 position, Vector3 endPosition, Vector3 forward, Color color, float outlineScale = 1.35f, float scale = 1.0f)
        {
            float displayScale = scale * SkinningModuleSettings.boneDisplayScale;
            DrawTextureAt(position, B_Circle_Selected, color, kCircleSize * displayScale, kCircleSize * displayScale);
            DrawTexturedBone(position, endPosition, forward, color, color, true, scale);
        }

        public static bool ShouldDrawBoneBody(Vector3 position, Vector3 endPosition, float scale = 1.0f)
        {
            return ShouldDrawBoneBodyAtScale(position, endPosition, scale * SkinningModuleSettings.boneDisplayScale);
        }

        static bool ShouldDrawBoneBodyAtScale(Vector3 position, Vector3 endPosition, float scale)
        {
            Vector2 start = HandleUtility.WorldToGUIPoint(position);
            Vector2 end = HandleUtility.WorldToGUIPoint(endPosition);
            return (end - start).magnitude >= kMinimumTexturedBoneLength * scale;
        }

        static void DrawTexturedBone(Vector3 position, Vector3 endPosition, Vector3 forward, Color color, Color endCapColor, bool selected, float scale)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            scale *= SkinningModuleSettings.boneDisplayScale;

            Vector2 start = HandleUtility.WorldToGUIPoint(position);
            Vector2 end = HandleUtility.WorldToGUIPoint(endPosition);
            Vector2 direction = end - start;
            float length = direction.magnitude;

            if (!ShouldDrawBoneBodyAtScale(position, endPosition, scale))
                return;

            Texture2D head = selected ? B_Head_Selected : B_Head;
            Texture2D stretch = selected ? B_Stretch_Selected : B_Stretch;
            Texture2D endCap = selected ? B_EndCap_Selected : B_EndCap;
            float stretchLength = Mathf.Max(1f, length - (kHeadLength + kEndCapSize) * scale);

            DrawBonePartTexture(position, endPosition, forward, length, color, 0f, kHeadLength * scale, kHeadWidth * scale, head);
            DrawBonePartTexture(position, endPosition, forward, length, color, kHeadLength * scale - kSliceOverlap * scale, kHeadLength * scale + stretchLength + kSliceOverlap * scale, kStretchWidth * scale, stretch);
            DrawBonePartTexture(position, endPosition, forward, length, endCapColor, length - kEndCapSize * scale, length, kEndCapSize * scale, endCap);
        }

        static void DrawParentLinkDashes(Vector3 position, Vector3 endPosition, Vector3 forward)
        {
            Vector2 startGUI = HandleUtility.WorldToGUIPoint(position);
            Vector2 endGUI = HandleUtility.WorldToGUIPoint(endPosition);
            float guiLength = (endGUI - startGUI).magnitude;
            if (guiLength <= 0f)
                return;

            Vector3 link = endPosition - position;
            Vector3 direction = link.normalized;
            float worldPerGuiPixel = link.magnitude / guiLength;
            float arrowLength = GetParentLinkArrowLength(guiLength);
            float dashedLength = Mathf.Max(0f, guiLength - arrowLength);

            for (float dashStart = 0f; dashStart < dashedLength; dashStart += kParentLinkDashLength + kParentLinkDashGap)
            {
                float dashEnd = Mathf.Min(dashStart + kParentLinkDashLength, dashedLength);
                Vector3 start = position + direction * (dashStart * worldPerGuiPixel);
                Vector3 end = position + direction * (dashEnd * worldPerGuiPixel);
                DrawingUtility.DrawSolidLine(kParentLinkLineWidth * worldPerGuiPixel, start, end);
            }
        }

        static void DrawParentLinkArrow(Vector3 position, Vector3 endPosition, Vector3 forward)
        {
            Vector2 startGUI = HandleUtility.WorldToGUIPoint(position);
            Vector2 endGUI = HandleUtility.WorldToGUIPoint(endPosition);
            float guiLength = (endGUI - startGUI).magnitude;
            float arrowLength = GetParentLinkArrowLength(guiLength);
            if (arrowLength <= 0f)
                return;

            Vector3 link = endPosition - position;
            Vector3 direction = link.normalized;
            Vector3 normal = Vector3.Cross(forward, direction).normalized;
            float worldPerGuiPixel = link.magnitude / guiLength;
            Vector3 arrowBack = endPosition - direction * (arrowLength * worldPerGuiPixel);
            Vector3 arrowWing = normal * (kParentLinkArrowHalfWidth * (arrowLength / kParentLinkArrowLength) * worldPerGuiPixel);

            GL.Color(Handles.color);
            GL.Vertex(endPosition);
            GL.Vertex(arrowBack + arrowWing);
            GL.Vertex(arrowBack - arrowWing);
        }

        static float GetParentLinkArrowLength(float guiLength)
        {
            if (guiLength < kParentLinkMinimumArrowLength)
                return 0f;

            return Mathf.Min(kParentLinkArrowLength, guiLength * 0.45f);
        }

        static void DrawBonePartTexture(Vector3 position, Vector3 endPosition, Vector3 forward, float length, Color color, float startDistance, float endDistance, float width, Texture2D texture)
        {
            Vector3 boneVector = endPosition - position;
            if (boneVector.sqrMagnitude <= 0f || length <= 0f)
                return;

            Vector3 right = boneVector.normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            float worldPerGuiPixel = boneVector.magnitude / length;
            Vector3 partStart = position + right * (startDistance * worldPerGuiPixel);
            Vector3 partEnd = position + right * (endDistance * worldPerGuiPixel);
            Vector3 halfWidth = up * (width * worldPerGuiPixel * 0.5f);

            EnsureBonePartMesh();

            s_BonePartMesh.vertices = new[]
            {
                partStart - halfWidth,
                partStart + halfWidth,
                partEnd + halfWidth,
                partEnd - halfWidth
            };

            s_BonePartMesh.uv = s_BonePartUVs;
            s_BonePartMesh.SetIndices(s_BonePartIndices, MeshTopology.Triangles, 0);
            s_BonePartMesh.RecalculateBounds();

            s_BonePartMaterial.mainTexture = texture;
            s_BonePartMaterial.SetFloat("_Opacity", 1f);
            s_BonePartMaterial.SetFloat("_VertexColorBlend", 0f);
            s_BonePartMaterial.color = color;

            DrawingUtility.DrawMesh(s_BonePartMesh, s_BonePartMaterial, Matrix4x4.identity);
        }

        static void EnsureBonePartMesh()
        {
            if (s_BonePartMesh == null)
            {
                s_BonePartMesh = new Mesh
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            if (s_BonePartMaterial == null)
            {
                s_BonePartMaterial = new Material(Shader.Find("Hidden/SkinningModule-BoneTexture"))
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        static void DrawTextureAt(Vector3 position, Texture2D texture, Color color, float width, float height)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            Vector2 guiPosition = HandleUtility.WorldToGUIPoint(position);

            Handles.BeginGUI();

            Color guiColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(guiPosition.x - width * 0.5f, guiPosition.y - height * 0.5f, width, height), texture, ScaleMode.StretchToFill, true);
            GUI.color = guiColor;

            Handles.EndGUI();
        }

        static Texture2D MakeTexture(ref string base64)
        {
            if (string.IsNullOrEmpty(base64))
                return null;

            byte[] bytes = System.Convert.FromBase64String(base64);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);

            if (texture.LoadImage(bytes, false))
            {
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.hideFlags = HideFlags.HideAndDontSave;
                return texture;
            }

            Object.DestroyImmediate(texture);
            return null;
        }

        public static Texture2D B_Circle => B_Circle_Texture ??= MakeTexture(ref B_Circle_PNG);
        static string B_Circle_PNG = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAABZElEQVRIDbVUO05DQQzchzhG0gVxBgT0kagRtyAFF6HgEFR0KNRQcAYEHVyC6jEjrTcT7zpvhfQsWZ7n8SfZtXcYxzHNKUdzFmft2Rscd/yDJWKuoedQ4l/oD/Qd+pQxTCC8gwN6D25KGBPWiIgVkrZTlYX/BGZOVW+gsyFb+Nbi/wJ+gH5k3ykseR9zkvmdaXT1x7JpxNgvXYNTqY7LAs0uNBr4UHHL8U2WyDMuFZCdt9KAd+D56Fvv607z/B6c7Q4vvQieghp7ocG+wUJIu1BxhfBZGO5KEd9gKExKnJz/yF7NvQ9U44aaXBnosCuJ+RZcvUVcfxOdcfNFdiPEm+BqimYfU46hXzTOeTSefgcmF80K8W1R4Zxz6fjeUFlYZx+fI3Msv9gCHMkivglcoYSPXdTA/P64Wh2qY0GQ5afoNdVB4OLcQC+hxBxtjuIr9DFjmLb0NGhndnr9onWm9YfN3uAPX+JIGW8Zfe0AAAAASUVORK5CYII=";
        static Texture2D B_Circle_Texture;

        public static Texture2D B_Circle_Selected => B_Circle_Selected_Texture ??= MakeTexture(ref B_Circle_Selected_PNG);
        static string B_Circle_Selected_PNG = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAABWklEQVRIDbWUYWrDMAyF07GjdOwqhV2l0Kts0HMU9mOsVxntXbL3uZHnJLIjsvaBYudZlmxJ1qbv++6ReA4Y30rnTbKTML9KwFnyJbF/uDm4QUV24r8lS0AHXdeOS0r5fcmqs86emb0ZIaWTszlKfU6dTB2sOfnUOSHLdvNEJHG8Fw4ylGw/FWk/FPP/Tqm4hA2eBMrvkpj650NLlCbAwNKBXqRztRBxpRa2WjRdG+FaSGGyEOUrcbwJOLn3mOBYqyHZNAc1JXgLi6dz9MiBI+xdxIF3+obd8VLEwX68ZfRHj6rhdjBliaStTfKlkeWU5GhFYIdXTuUgbG4Z1/KtAZoDxkjnZGMEuV2UOWiVXC3ONf6v8nSc8hYPbXbmiJa7FmdtNDtpHP0Ui2tukuNe2OlqDuCplEji0UklWRq2uXXTWrLg6Smvw5iev+Y8IhL5M4wafEQc+DuD7C8S+4sVzwRfvAAAAABJRU5ErkJggg==";
        static Texture2D B_Circle_Selected_Texture;

        public static Texture2D B_EndCap => B_EndCap_Texture ??= MakeTexture(ref B_EndCap_PNG);
        static string B_EndCap_PNG = "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAKCAYAAACNMs+9AAAATklEQVQYGWP8//8/AzGAiRhFIDVkK/QEan4GxSA2AoDciISfAdkwAGLD5fBZzYgwDshC1gVkewHxcyB+AmXD5RmBAigacXHwWY2ih2iFAIQfSlUnx6T/AAAAAElFTkSuQmCC";
        static Texture2D B_EndCap_Texture;

        public static Texture2D B_EndCap_Selected => B_EndCap_Selected_Texture ??= MakeTexture(ref B_EndCap_Selected_PNG);
        static string B_EndCap_Selected_PNG = "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAKCAYAAACNMs+9AAAAcUlEQVQYGY2OWwqAIBREtR2E7f87bBktxH7CdmFzMEUuCA0cHzPD5fpSivujZSgFvaPIH7zxqpgogkjCKskgc5QgipnIejHPWvLJ3Lhj28beHqMVT5sO/5oxVrDwJazwNtF3pIxxiEfcYherIHOe449e00O+WhsFrmsAAAAASUVORK5CYII=";
        static Texture2D B_EndCap_Selected_Texture;

        public static Texture2D B_Head => B_Head_Texture ??= MakeTexture(ref B_Head_PNG);
        static string B_Head_PNG = "iVBORw0KGgoAAAANSUhEUgAAABoAAAAUCAYAAACTQC2+AAABPklEQVRIDb2UPU7DQBCFE4hEQ5cyNQpNJC7AoWjSpaSm4wa5QU6QC6SgAVFTUoUmUhTzPmvtTFa79qyFGOlp/t680dqrHVVVNTLYKP4rQ6vVHpMYu1P8JtyY2pDwoKGF8NkMXzVB8DReotqQFI12CQLxiajdCu/CjGSAfWnmXvixs/GJ6EFYWlJhzOzFknre/jATc9KtUGrM1F9Jvr0IxBdJ1HxQfhS8BpeZpGayaMiv3i3iwc3qpS6D/SVTJR8Cvsu+1ZwL+KSlLoMlMriyhUwMJ7uknuk6buhdy++EnNGDk/1s9DqbZvhR8UmIjRq9Xp1eghFZx1uUU3NpuEhBbCa/FxojpubS6LsM9t/ztDybAjE1l/Vd71iEV53XHVsIvNIum7hYZxLCTyF1L4FfeqKwo9yV/KNydTPxb4t+ARW85RjZQx+pAAAAAElFTkSuQmCC";
        static Texture2D B_Head_Texture;

        public static Texture2D B_Head_Selected => B_Head_Selected_Texture ??= MakeTexture(ref B_Head_Selected_PNG);
        static string B_Head_Selected_PNG = "iVBORw0KGgoAAAANSUhEUgAAABoAAAAUCAYAAACTQC2+AAABGklEQVRIDbVTwQ3CMAwMLNANYAI26I58EIIPb558EB9+IBZggrIBExRfahe3SWjSEkuRHft8V6eJqeva0DrTymXgNnPT2I19DtdwQ43XNcNI4LT8MhEmWWcY58spiuyPf5wKXHJaZoaNsgXFldpPCZfU/BICfXTIobCT4gQPjlbE8ujxVPygeKyhtz0yifsTyRAbCUZ4f68oevyJcqmGHmca5LxJBq9SVQiPHi+nN6nA+wQxYIN8wQI34fo/I8SAsU+F+xze0GWQO4BHtpXNDw9M50E62NAX9PIX2ocMNWeCfm4QwA1lSIXyqA3yDAIUycEjhlwURxSIyQrylRJDjFwUh70pzo8LJwoqlVy+k3+Hod1KqlC3O2H3AdcAUFEBl9wwAAAAAElFTkSuQmCC";
        static Texture2D B_Head_Selected_Texture;

        public static Texture2D B_Stretch => B_Stretch_Texture ??= MakeTexture(ref B_Stretch_PNG);
        static string B_Stretch_PNG = "iVBORw0KGgoAAAANSUhEUgAAABoAAAA1CAYAAABfsPstAAAB6UlEQVRYCc2Ya07EMAyEu4gLAF2JeyxIcALufw5ePzhAyYi6ShM/xgFVRIqSTe35mnqalXpalmU6ol0dAQHjX4PO5QbRU21kR8+FgJ5qI6BLIaCn2nUq+if4YSBnOg3Y+2MF3WaA2Uc3F/GbtWNOtyyoNkE9D4FZUF2fev7noNpt9TwEZc3wXhTFBDDFXUhYAzKPDsUXCNIxpw2RAWnF19ZwE13LgLSaaGsdBAsZ0KOioK0pYVPqZKiNIGK0IdgdwV21EQSENcp5LOhJlJXRu7aFsyCvFt61NMg7brxrG4g9GTQjiAhlCObRWUYQEGUIBsQUO4xhQEwNwhgGxLgqjGHM8FaKEb2UMMssRdPGaEdUodcb0U6OjRmB6L+BoujGRqCwyNstT5MbexgoMgNjBNmUawhvR6wRBOSeIB7ILa6oN6OZ44EujQjz08zxQOHbrpDNHM8MGSMI0zSEtaOsEQRkGsIChce+KCujmmuB3LdcEa+X1FwLZLqnVjTmaq5lhtciMhtC0TJMdG6DtB3BCKMQ6CO3+8vQQGoxoZBonYYGUouZgCC00zgMpJnhN0aQjXeGaEH4hiAfLCRpdIQhPiW5fXRdESVwYNxptSDz9B0A7bRa0MuAoJWy02q/bt2XrC8rM7kOra19A5X7RGA0qCT8AAAAAElFTkSuQmCC";
        static Texture2D B_Stretch_Texture;

        public static Texture2D B_Stretch_Selected => B_Stretch_Selected_Texture ??= MakeTexture(ref B_Stretch_Selected_PNG);
        static string B_Stretch_Selected_PNG = "iVBORw0KGgoAAAANSUhEUgAAABoAAAA1CAYAAABfsPstAAACEUlEQVRYCb2YaU7DMBCFU07AUpZ7hBb4gTgwJwOx3SB4oo7ljN8sjqNaqhzbM+/LZF5axG6apuEc42IF5C7l0KdptILek/rH6UPX4bFreHRUBUHKcZ8Wn+WGdt1S0TMQQXsgbBhaQCNQQHsgrA10AApoD4QNQ0uPfpPCpVD5S+srsQeX0Ud3m7IlhARpj87cEQW9GErWWU6LgqymW2fNoMecUV9YZzk6aoaflKE1nUxynRWVi8ijo2ZrEJKlM9cQEVCk2W5MBBRpthsTAR3o+TjDjYmYwTIC811DeBXtk5JlBAZRDMWqwwM9qZn1gRnrgY61nrpjxnqg0Ft/QpuxnhkiRuASTUNYFUWNwCDTEBbIbC6ri1nNsUDuSyggtFRzzgayzPCd7tD9+hdVkXluxN681Cqi4FYICVJOEyj8hyGpiwFztYrUpgpRtIS5Gsh8y5F6sQdzNTOsMQKzoCFQRWuNwCBoCASCzWSV4FxpINAYFLPCKg0EMn9XLPXirNJAZugxArMqQ8iKeo3AoMoQElQ1kTNXzAstCaqauALAKQstCYJvNWc2zgstaYavJEZ92mKQqfYsVFZEgK0gpL/QK0GL5vGddM5ZswSNnaIoPWuWIPg7grIb9rJmaYYtjcD3kg3BFVVvMkd2zmQI0s7/C8pN6xRG6bM2V/SGIjbam7UZ9LqRKJKZtRn0gCI22pu1/wHc5kgRiJQ3+AAAAABJRU5ErkJggg==";
        static Texture2D B_Stretch_Selected_Texture;
    }
}
