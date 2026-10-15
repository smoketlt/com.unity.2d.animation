using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    /// <summary>Read-only runtime mesh and deformation diagnostics for the renderer Inspector.</summary>
    internal sealed class SceneSpriteMeshInfo : Foldout
    {
        static readonly string[] k_Names = {
            "Sprite", "Vertices", "Triangles", "Deformation", "SpriteSkin Status", "Bones",
            "SRP Batcher", "Render Pipeline", "Mask Interaction", "SpriteMask Source",
            "Shader", "Sorting", "Texture", "Pixels Per Unit"
        };
        readonly Label[] m_Values = new Label[k_Names.Length];
        readonly Func<UnityEngine.Object[]> m_Targets;

        internal SceneSpriteMeshInfo(Func<UnityEngine.Object[]> targets)
        {
            m_Targets = targets;
            name = "uniskinMeshInfo";
            text = "Mesh Information";
            value = true;
            viewDataKey = "uniskin-mesh-information";
            AddToClassList("uniskin-mesh-info");
            for (var i = 0; i < k_Names.Length; ++i)
            {
                var row = new VisualElement(); row.AddToClassList("uniskin-mesh-info-row");
                var title = new Label(k_Names[i]); title.AddToClassList("uniskin-mesh-info-title");
                var content = new Label { name = "meshInfo" + i };
                content.AddToClassList("uniskin-mesh-info-value");
                row.Add(title); row.Add(content); Add(row); m_Values[i] = content;
            }
            m_Values[1].tooltip = "Actual Sprite vertex count. Multiple selection shows the total, counting each renderer instance.";
            m_Values[2].tooltip = "Triangle index count / 3. Unity Sprite polygons are triangles. Multiple selection shows the total.";
            m_Values[3].tooltip = "SpriteSkin's registered CPU/GPU deformation system, not GPU eligibility. Disabled or invalid skins are not reported as actively deforming. GPU + CPU copy means forceCpuDeformation is enabled.";
            m_Values[6].tooltip = "Current render pipeline's global SRP Batcher state. On does not guarantee that this renderer uses batching or GPU deformation.";
            schedule.Execute(() => { if (value) Refresh(); }).Every(250);
            this.RegisterValueChangedCallback(evt => { if (evt.newValue) Refresh(); });
            Refresh();
        }

        internal void Refresh()
        {
            string[] result = null;
            int vertices = 0, triangles = 0, count = 0;
            foreach (var item in m_Targets())
            {
                if (!(item is SpriteRenderer renderer)) continue;
                var values = Describe(renderer);
                if (result == null) result = values;
                else for (var i = 0; i < result.Length; ++i) if (result[i] != values[i]) result[i] = "Mixed";
                if (renderer.sprite != null)
                {
                    vertices += renderer.sprite.GetVertexCount();
                    triangles += renderer.sprite.GetIndices().Length / 3;
                }
                ++count;
            }
            if (result == null) result = new string[k_Names.Length];
            if (count > 1) { result[1] = vertices + " total (" + count + " renderers)"; result[2] = triangles + " total (" + count + " renderers)"; }
            for (var i = 0; i < m_Values.Length; ++i)
                if (m_Values[i].text != (result[i] ?? "—")) m_Values[i].text = result[i] ?? "—";
        }

        internal static string[] Describe(SpriteRenderer renderer)
        {
            var sprite = renderer.sprite;
            var skin = renderer.GetComponent<SpriteSkin>();
            var mask = renderer.GetComponent<SpriteMask>();
            var pipeline = GraphicsSettings.currentRenderPipeline;
            var bones = skin != null ? skin.boneTransforms : null;
            int assigned = 0;
            if (bones != null) foreach (var bone in bones) if (bone != null) ++assigned;
            var status = skin == null ? "No SpriteSkin" : !skin.enabled ? "Disabled" :
                !skin.gameObject.activeInHierarchy ? "Inactive" : ObjectNames.NicifyVariableName(skin.Validate().ToString());
            var deformation = "Static";
            if (skin != null)
            {
                if (!skin.isActiveAndEnabled) deformation = "Not active";
                else if (skin.Validate() != SpriteSkinState.Ready) deformation = "Not ready";
                else if (skin.DeformationSystem == null) deformation = "Not registered";
                else deformation = skin.currentDeformationMethod == DeformationMethods.Gpu
                    ? (skin.forceCpuDeformation ? "GPU + CPU copy" : "GPU")
                    : skin.currentDeformationMethod == DeformationMethods.Cpu ? "CPU" : "Not registered";
            }
            return new[] {
                sprite != null ? sprite.name : "None",
                sprite != null ? sprite.GetVertexCount().ToString() : "0",
                sprite != null ? (sprite.GetIndices().Length / 3).ToString() : "0",
                deformation, status,
                skin == null ? "—" : assigned + "/" + (bones != null ? bones.Length : 0) + " assigned; " + (sprite != null ? sprite.GetBindPoses().Length : 0) + " bind poses",
                pipeline == null ? "N/A (Built-in)" : GraphicsSettings.useScriptableRenderPipelineBatching ? "On" : "Off",
                pipeline != null ? pipeline.name : "Built-in",
                ObjectNames.NicifyVariableName(renderer.maskInteraction.ToString()),
                mask == null ? "None" : mask.enabled && mask.gameObject.activeInHierarchy ? "Enabled" : "Disabled",
                renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null ? renderer.sharedMaterial.shader.name : "None",
                SortingLayer.IDToName(renderer.sortingLayerID) + " / " + renderer.sortingOrder,
                sprite != null && sprite.texture != null ? sprite.texture.width + " × " + sprite.texture.height : "—",
                sprite != null ? sprite.pixelsPerUnit.ToString("0.###") : "—"
            };
        }
    }
}
