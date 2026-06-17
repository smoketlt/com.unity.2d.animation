using System;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal class MeshToolWrapper : BaseTool
    {
        private MeshTool m_MeshTool;
        private SkeletonTool m_SkeletonTool;
        private SpriteMeshViewMode m_MeshMode;
        private bool m_Disable = false;
        private bool m_DrawVertexWeights = false;
        private float m_VertexWeightOpacity = 0.5f;
        private SkeletonMode m_SkeletonMode;
        private bool m_ClearBoneSelectionOnEscape;
        private bool m_ClearBoneSelectionOnPrimaryEmptyClick;
        private bool m_UseMeshDefaultControlForBoneUnselection = true;
        protected MeshPreviewBehaviour m_MeshPreviewBehaviour = new MeshPreviewBehaviour();

        public MeshTool meshTool
        {
            get { return m_MeshTool; }
            set { m_MeshTool = value; }
        }

        public SkeletonTool skeletonTool
        {
            get { return m_SkeletonTool; }
            set { m_SkeletonTool = value; }
        }

        public SpriteMeshViewMode meshMode
        {
            get { return m_MeshMode; }
            set { m_MeshMode = value; }
        }

        public bool disableMeshEditor
        {
            get { return m_Disable; }
            set { m_Disable = value; }
        }

        protected bool drawVertexWeights
        {
            get { return m_DrawVertexWeights; }
            set { m_DrawVertexWeights = value; }
        }

        protected float vertexWeightOpacity
        {
            get { return m_VertexWeightOpacity; }
            set { m_VertexWeightOpacity = value; }
        }

        protected bool clearBoneSelectionOnEscape
        {
            get { return m_ClearBoneSelectionOnEscape; }
            set { m_ClearBoneSelectionOnEscape = value; }
        }

        protected bool clearBoneSelectionOnPrimaryEmptyClick
        {
            get { return m_ClearBoneSelectionOnPrimaryEmptyClick; }
            set { m_ClearBoneSelectionOnPrimaryEmptyClick = value; }
        }

        protected bool useMeshDefaultControlForBoneUnselection
        {
            get { return m_UseMeshDefaultControlForBoneUnselection; }
            set { m_UseMeshDefaultControlForBoneUnselection = value; }
        }

        public SkeletonMode skeletonMode
        {
            get { return m_SkeletonMode; }
            set { m_SkeletonMode = value; }
        }

        public override int defaultControlID
        {
            get
            {
                Debug.Assert(meshTool != null);

                return meshTool.defaultControlID;
            }
        }

        public override IMeshPreviewBehaviour previewBehaviour
        {
            get { return m_MeshPreviewBehaviour; }
        }

        protected override void OnActivate()
        {
            Debug.Assert(meshTool != null);
            ShowInfoOverlay(SkinningEditorInfoText.ForMeshMode(meshMode));
            skeletonTool.enableBoneInspector = false;
            skeletonTool.Activate();
            meshTool.Activate();
            m_MeshPreviewBehaviour.drawWireframe = true;
            m_MeshPreviewBehaviour.showWeightMap = false;
            m_MeshPreviewBehaviour.overlaySelected = false;
            m_MeshPreviewBehaviour.dimUnselectedSprites = meshMode == SpriteMeshViewMode.NewGeometry;
            m_MeshPreviewBehaviour.unselectedSpriteOpacity = 0.1f;
        }

        protected override void OnDeactivate()
        {
            HideInfoOverlay();
            skeletonTool.Deactivate();
            meshTool.Deactivate();
        }

        protected override void OnGUI()
        {
            DoSkeletonGUI();
            DoMeshGUI();
        }

        protected void DoSkeletonGUI()
        {
            Debug.Assert(skeletonTool != null);

            skeletonTool.mode = skeletonMode;
            skeletonTool.editBindPose = false;
            skeletonTool.clearSelectionOnEscape = clearBoneSelectionOnEscape;
            skeletonTool.clearSelectionOnPrimaryEmptyClick = clearBoneSelectionOnPrimaryEmptyClick;
            skeletonTool.secondaryEmptyControlID = useMeshDefaultControlForBoneUnselection && meshTool != null ? meshTool.defaultControlID : 0;
            skeletonTool.allowPrimaryEmptyClickFallback = !useMeshDefaultControlForBoneUnselection;
            skeletonTool.DoGUI();
        }

        protected void DoMeshGUI()
        {
            Debug.Assert(meshTool != null);

            meshTool.disable = disableMeshEditor;
            meshTool.drawVertexWeights = drawVertexWeights;
            meshTool.vertexWeightOpacity = vertexWeightOpacity;
            meshTool.mode = GetEffectiveMeshMode();
            meshTool.DoGUI();
        }

        SpriteMeshViewMode GetEffectiveMeshMode()
        {
            if (!SkinningEditorInput.altKeyDown)
                return meshMode;

            if (meshMode == SpriteMeshViewMode.EditGeometry)
                return SpriteMeshViewMode.CreateVertex;

            if (meshMode == SpriteMeshViewMode.CreateVertex)
                return SpriteMeshViewMode.EditGeometry;

            return meshMode;
        }
    }
}
