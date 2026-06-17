using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.U2D.Common;
using UnityEditor.U2D.Layout;
using UnityEngine;
using Unity.Mathematics;

namespace UnityEditor.U2D.Animation
{
    internal enum WeightPainterMode
    {
        Brush,
        Slider
    }

    internal class WeightPainterTool : MeshToolWrapper
    {
        const float kWeightSliderVertexHitRadius = 16f;
        const float kWeightSliderDragWeightPerPixel = 0.0025f;
        const float kWeightSliderSmoothMinDistance = 0.0001f;
        const float kWeightSliderSmoothButtonStrength = 0.45f;
        const float kWeightSliderSmoothEdgeMinDistance = 0.0001f;

        private WeightPainterPanel m_WeightPainterPanel;
        private WeightEditor m_WeightEditor = new WeightEditor();
        private Brush m_Brush = new Brush(new GUIWrapper());
        private ISelection<int> m_BrushSelection = new IndexedSelection();
        private CircleVertexSelector m_CircleVertexSelector = new CircleVertexSelector();
        private readonly List<int> m_WeightSliderBoneIndices = new List<int>(4);
        private readonly List<int> m_WeightSliderSelectedChannels = new List<int>(4);
        private readonly List<int> m_WeightSliderOtherChannels = new List<int>(4);
        private readonly List<int> m_WeightSliderSmoothBoneIndices = new List<int>();
        private readonly List<float> m_WeightSliderSmoothTargetWeights = new List<float>();
        private readonly List<int> m_WeightSliderSmoothTargetVertices = new List<int>();
        private readonly HashSet<int> m_WeightSliderLockedBoneIndices = new HashSet<int>();
        private BoneWeight[] m_WeightSliderSmoothStartWeights;
        private float m_WeightSliderSmoothDragAmount;
        private bool m_WeightSliderDragActive;
        private bool m_WeightSliderDragUndoStarted;

        private struct WeightSliderSmoothNeighbor
        {
            public int vertexIndex;
            public float weight;
        }

        public WeightPainterMode paintMode
        {
            get { return m_WeightPainterPanel.paintMode; }
            set { m_WeightPainterPanel.paintMode = value; }
        }

        public override int defaultControlID
        {
            get { return m_Brush.controlID; }
        }

        internal override void OnCreate()
        {
            m_WeightEditor.cacheUndo = skinningCache;

            m_Brush.onMove += (brush) =>
            {
                UpdateBrushSelection(brush);
            };
            m_Brush.onRepaint += (brush) =>
            {
                DrawBrush(brush);
            };
            m_Brush.onSize += (brush) =>
            {
                UpdateBrushSelection(brush);
                m_WeightPainterPanel.size = Mathf.RoundToInt(brush.size);
            };
            m_Brush.onStrokeBegin += (brush) =>
            {
                UpdateBrushSelection(brush);
                EditStart(m_BrushSelection, true);
            };
            m_Brush.onStrokeDelta += (brush) =>
            {
                if (m_BrushSelection.Count > 0)
                    meshTool.UpdateWeights();
            };
            m_Brush.onStrokeStep += (brush) =>
            {
                UpdateBrushSelection(brush);

                float hardness = brush.hardness / 100f;

                if (EditorGUI.actionKey)
                    hardness *= -1f;

                EditWeights(hardness, false);
            };
            m_Brush.onStrokeEnd += (brush) =>
            {
                EditEnd();
            };
        }

        public string panelTitle
        {
            set { m_WeightPainterPanel.title = value; }
        }

        protected override void OnActivate()
        {
            base.OnActivate();
            m_WeightPainterPanel.SetHiddenFromLayout(false);

            skinningCache.events.selectedSpriteChanged.AddListener(OnSelectedSpriteChanged);
            skinningCache.events.skinningModeChanged.AddListener(OnSkinningModeChanged);
            skinningCache.events.boneSelectionChanged.AddListener(OnBoneSelectionChanged);

            m_Brush.size = skinningCache.brushSize;
            m_Brush.hardness = skinningCache.brushHardness;
            m_Brush.step = skinningCache.brushStep;
            m_WeightPainterPanel.size = (int)m_Brush.size;
            m_WeightPainterPanel.hardness = (int)m_Brush.hardness;
            m_WeightPainterPanel.step = (int)m_Brush.step;

            UpdatePanel();
        }

        protected override void OnDeactivate()
        {
            base.OnDeactivate();

            m_WeightSliderDragActive = false;
            m_WeightSliderDragUndoStarted = false;

            skinningCache.events.selectedSpriteChanged.RemoveListener(OnSelectedSpriteChanged);
            skinningCache.events.skinningModeChanged.RemoveListener(OnSkinningModeChanged);
            skinningCache.events.boneSelectionChanged.RemoveListener(OnBoneSelectionChanged);

            LayoutOverlayUtility.ResetDraggableOverlayPanel(m_WeightPainterPanel);
            m_WeightPainterPanel.SetHiddenFromLayout(true);
        }

        private void OnBoneSelectionChanged()
        {
            UpdateSelectedBone();
        }

        private void OnSelectedSpriteChanged(SpriteCache sprite)
        {
            UpdatePanel();
        }

        private void OnSkinningModeChanged(SkinningMode mode)
        {
            UpdatePanel();
        }

        private string[] GetSkeletonBonesNames()
        {
            List<string> names = new List<string>() { WeightPainterPanel.kNone };
            SkeletonCache skeleton = skinningCache.GetEffectiveSkeleton(skinningCache.selectedSprite);

            if (skeleton != null)
                names.AddRange(GetUniqueBoneNames(skeleton.bones, skeleton));

            return names.ToArray();
        }

        private string[] GetMeshBoneNames()
        {
            MeshCache mesh = meshTool.mesh;
            SkeletonCache skeleton = skinningCache.GetEffectiveSkeleton(skinningCache.selectedSprite);

            if (mesh != null && skeleton != null)
            {
                BoneCache[] bones = meshTool.mesh.bones.ToSpriteSheetIfNeeded();
                return GetUniqueBoneNames(bones, skeleton);
            }

            return new string[0];
        }

        private Color[] GetMeshBoneColors()
        {
            MeshCache mesh = meshTool.mesh;

            if (mesh != null)
            {
                BoneCache[] bones = mesh.bones.ToSpriteSheetIfNeeded();
                return Array.ConvertAll(bones, BoneColorUtility.GetWeightMapColor);
            }

            return new Color[0];
        }

        private int[] GetSelectedMeshBoneIndices()
        {
            MeshCache mesh = meshTool.mesh;
            if (mesh == null)
                return new int[0];

            List<int> indices = new List<int>();
            BoneCache[] meshBones = mesh.bones;
            BoneCache[] selectedBones = skinningCache.skeletonSelection.elements.ToSpriteSheetIfNeeded();

            for (int i = 0; i < selectedBones.Length; ++i)
            {
                int boneIndex = Array.IndexOf(meshBones, selectedBones[i]);
                if (boneIndex != -1 && !indices.Contains(boneIndex))
                    indices.Add(boneIndex);
            }

            return indices.ToArray();
        }

        private int[] GetLockedMeshBoneIndices()
        {
            MeshCache mesh = meshTool.mesh;
            if (mesh == null || m_WeightSliderLockedBoneIndices.Count == 0)
                return new int[0];

            List<int> indices = new List<int>();
            foreach (int boneIndex in m_WeightSliderLockedBoneIndices)
                if (boneIndex >= 0 && boneIndex < mesh.boneCount)
                    indices.Add(boneIndex);

            return indices.ToArray();
        }

        private string[] GetUniqueBoneNames(BoneCache[] bones, SkeletonCache skeleton)
        {
            return Array.ConvertAll(bones, b => skeleton.GetUniqueName(b));
        }

        private void UpdatePanel()
        {
            m_WeightPainterPanel.SetActive(skinningCache.selectedSprite != null);
            m_WeightPainterPanel.UpdateWeightInspector(meshTool.mesh, GetMeshBoneNames(), GetMeshBoneColors(), GetSelectedMeshBoneIndices(), GetLockedMeshBoneIndices(), skinningCache.vertexSelection, skinningCache);
            m_WeightPainterPanel.UpdatePanel(GetSkeletonBonesNames());
            UpdateSelectedBone();
        }

        private void UpdateSelectedBone()
        {
            string boneName = WeightPainterPanel.kNone;
            BoneCache bone = skinningCache.skeletonSelection.activeElement.ToSpriteSheetIfNeeded();
            SkeletonCache skeleton = skinningCache.GetEffectiveSkeleton(skinningCache.selectedSprite);

            if (skeleton != null && skeleton.Contains(bone))
                boneName = skeleton.GetUniqueName(bone);

            m_WeightPainterPanel.SetBoneSelectionByName(boneName);
            m_WeightPainterPanel.UpdateWeightInspector(meshTool.mesh, GetMeshBoneNames(), GetMeshBoneColors(), GetSelectedMeshBoneIndices(), GetLockedMeshBoneIndices(), skinningCache.vertexSelection, skinningCache);
        }

        public override void Initialize(LayoutOverlay layout)
        {
            base.Initialize(layout);

            m_WeightPainterPanel = WeightPainterPanel.GenerateFromUXML();
            m_WeightPainterPanel.SetHiddenFromLayout(true);
            layout.AddBottomOverlayPanel(m_WeightPainterPanel);

            m_WeightPainterPanel.sliderStarted += () =>
            {
                EditStart(skinningCache.vertexSelection, false);
            };
            m_WeightPainterPanel.sliderChanged += (value) =>
            {
                EditWeights(value, true);
                meshTool.UpdateWeights();
            };
            m_WeightPainterPanel.sliderEnded += () =>
            {
                EditEnd();
            };
            m_WeightPainterPanel.bonePopupChanged += (i) =>
            {
                SkeletonCache skeleton = skinningCache.GetEffectiveSkeleton(skinningCache.selectedSprite);

                if (skeleton != null)
                {
                    BoneCache bone = null;

                    if (i != -1)
                        bone = skeleton.GetBone(i).ToCharacterIfNeeded();

                    if (bone != skinningCache.skeletonSelection.activeElement)
                    {
                        using (skinningCache.UndoScope(TextContent.boneSelection))
                        {
                            skinningCache.skeletonSelection.activeElement = bone;
                            InvokeBoneSelectionChanged();
                        }
                    }
                }
            };
            m_WeightPainterPanel.weightsChanged += () => meshTool.UpdateWeights();
            m_WeightPainterPanel.smoothClicked += SmoothWeightSliderVerticesOnce;
            m_WeightPainterPanel.pruneClicked += ShowPruneWeightsWindow;
            m_WeightPainterPanel.boneButtonClicked += SelectWeightInspectorBone;
            m_WeightPainterPanel.lockButtonClicked += ToggleWeightInspectorBoneLock;
        }

        internal void SetWeightPainterPanelTitle(string title)
        {
            m_WeightPainterPanel.title = title;
        }

        private void SelectWeightInspectorBone(int meshBoneIndex, bool additive)
        {
            MeshCache mesh = meshTool.mesh;
            if (mesh == null || meshBoneIndex < 0 || meshBoneIndex >= mesh.boneCount)
                return;

            BoneCache bone = mesh.bones[meshBoneIndex].ToCharacterIfNeeded();

            using (skinningCache.UndoScope(TextContent.boneSelection))
            {
                if (additive)
                {
                    bool select = !skinningCache.skeletonSelection.Contains(bone);
                    skinningCache.skeletonSelection.Select(bone, select);
                }
                else
                {
                    skinningCache.skeletonSelection.activeElement = bone;
                }

                InvokeBoneSelectionChanged();
                UpdateSelectedBone();
            }
        }

        private void ToggleWeightInspectorBoneLock(int meshBoneIndex)
        {
            MeshCache mesh = meshTool.mesh;
            if (mesh == null || meshBoneIndex < 0 || meshBoneIndex >= mesh.boneCount)
                return;

            if (!m_WeightSliderLockedBoneIndices.Remove(meshBoneIndex))
                m_WeightSliderLockedBoneIndices.Add(meshBoneIndex);

            m_WeightPainterPanel.UpdateWeightInspector(meshTool.mesh, GetMeshBoneNames(), GetMeshBoneColors(), GetSelectedMeshBoneIndices(), GetLockedMeshBoneIndices(), skinningCache.vertexSelection, skinningCache);
        }

        private void ShowPruneWeightsWindow()
        {
            MeshCache mesh = meshTool.mesh;
            if (mesh == null || mesh.vertexCount == 0 || mesh.boneCount == 0)
                return;

            PruneWeightsWindow.ShowWindow(
                (maxBones, threshold) => CountPrunedWeights(mesh, maxBones, threshold),
                (maxBones, threshold) => ApplyPruneWeights(mesh, maxBones, threshold));
        }

        private int CountPrunedWeights(MeshCache mesh, int maxBones, float threshold)
        {
            int removedCount = 0;
            int[] vertexIndices = GetWeightSliderTargetVertexIndices(mesh);

            for (int i = 0; i < vertexIndices.Length; ++i)
            {
                float[] weights = GetWeightArray(mesh.vertexWeights[vertexIndices[i]], mesh.boneCount);
                removedCount += PruneWeightArray(weights, maxBones, threshold, false);
            }

            return removedCount;
        }

        private void ApplyPruneWeights(MeshCache mesh, int maxBones, float threshold)
        {
            int[] vertexIndices = GetWeightSliderTargetVertexIndices(mesh);
            if (vertexIndices.Length == 0)
                return;

            bool changed = false;
            skinningCache.BeginUndoOperation(TextContent.editWeights);

            for (int i = 0; i < vertexIndices.Length; ++i)
            {
                EditableBoneWeight editableBoneWeight = mesh.vertexWeights[vertexIndices[i]];
                float[] weights = GetWeightArray(editableBoneWeight, mesh.boneCount);

                if (PruneWeightArray(weights, maxBones, threshold, true) == 0)
                    continue;

                SetWeightArray(editableBoneWeight, weights);
                changed = true;
            }

            if (!changed)
                return;

            SpriteMeshDataController controller = new SpriteMeshDataController();
            controller.spriteMeshData = mesh;
            controller.SortTrianglesByDepth();
            meshTool.UpdateWeights();
            m_WeightPainterPanel.UpdateWeightInspector(mesh, GetMeshBoneNames(), GetMeshBoneColors(), GetSelectedMeshBoneIndices(), GetLockedMeshBoneIndices(), skinningCache.vertexSelection, skinningCache);
            skinningCache.events.meshChanged.Invoke(mesh.sprite.GetMesh());
        }

        private int[] GetWeightSliderTargetVertexIndices(MeshCache mesh)
        {
            if (skinningCache.vertexSelection.Count == 0)
                return Enumerable.Range(0, mesh.vertexCount).ToArray();

            return skinningCache.vertexSelection.elements.Where(i => i >= 0 && i < mesh.vertexCount).Distinct().ToArray();
        }

        private int PruneWeightArray(float[] weights, int maxBones, float threshold, bool apply)
        {
            List<int> unlockedWeights = new List<int>();
            List<int> removedWeights = new List<int>();
            int lockedWeightCount = 0;

            for (int i = 0; i < weights.Length; ++i)
            {
                if (weights[i] <= 0f)
                    continue;

                if (IsWeightSliderBoneLocked(i))
                    ++lockedWeightCount;
                else
                    unlockedWeights.Add(i);
            }

            for (int i = unlockedWeights.Count - 1; i >= 0; --i)
            {
                int boneIndex = unlockedWeights[i];
                if (weights[boneIndex] >= threshold)
                    continue;

                removedWeights.Add(boneIndex);
                unlockedWeights.RemoveAt(i);
            }

            int unlockedLimit = Mathf.Max(0, maxBones - lockedWeightCount);
            unlockedWeights.Sort((a, b) => weights[a].CompareTo(weights[b]));

            while (unlockedWeights.Count > unlockedLimit)
            {
                int boneIndex = unlockedWeights[0];
                removedWeights.Add(boneIndex);
                unlockedWeights.RemoveAt(0);
            }

            if (!apply)
                return removedWeights.Count;

            float removedWeightSum = 0f;
            for (int i = 0; i < removedWeights.Count; ++i)
            {
                int boneIndex = removedWeights[i];
                removedWeightSum += weights[boneIndex];
                weights[boneIndex] = 0f;
            }

            float recipientWeightSum = 0f;
            for (int i = 0; i < unlockedWeights.Count; ++i)
                recipientWeightSum += weights[unlockedWeights[i]];

            if (removedWeightSum > 0f && recipientWeightSum > 0f)
            {
                for (int i = 0; i < unlockedWeights.Count; ++i)
                {
                    int boneIndex = unlockedWeights[i];
                    weights[boneIndex] += removedWeightSum * weights[boneIndex] / recipientWeightSum;
                }
            }

            return removedWeights.Count;
        }

        private static float[] GetWeightArray(EditableBoneWeight editableBoneWeight, int boneCount)
        {
            float[] weights = new float[boneCount];
            for (int i = 0; i < editableBoneWeight.Count; ++i)
            {
                BoneWeightChannel channel = editableBoneWeight[i];
                if (channel.enabled && channel.boneIndex >= 0 && channel.boneIndex < boneCount)
                    weights[channel.boneIndex] += channel.weight;
            }

            return weights;
        }

        private static void SetWeightArray(EditableBoneWeight editableBoneWeight, float[] weights)
        {
            editableBoneWeight.Clear();
            for (int i = 0; i < weights.Length; ++i)
                if (weights[i] > 0f)
                    editableBoneWeight.AddChannel(i, weights[i], true);

            editableBoneWeight.UnifyChannelsWithSameBoneIndex();
            editableBoneWeight.FilterChannels(0f);
        }

        private void AssociateSelectedBoneToCharacterPart()
        {
            MeshCache mesh = meshTool.mesh;

            if (skinningCache.hasCharacter
                && skinningCache.mode == SkinningMode.Character
                && m_WeightPainterPanel.boneIndex != -1
                && mesh != null)
            {
                SkeletonCache skeleton = skinningCache.character.skeleton;

                Debug.Assert(skeleton != null);

                BoneCache bone = skeleton.GetBone(m_WeightPainterPanel.boneIndex);

                if (!mesh.ContainsBone(bone))
                {
                    using (skinningCache.UndoScope(TextContent.addBoneInfluence))
                    {
                        CharacterPartCache characterPart = mesh.sprite.GetCharacterPart();
                        List<BoneCache> characterBones = characterPart.bones.ToList();
                        characterBones.Add(bone);
                        characterPart.bones = characterBones.ToArray();
                        skinningCache.events.characterPartChanged.Invoke(characterPart);
                        mesh.sprite.CalculateMissingWeights();
                        skinningCache.events.meshChanged.Invoke(mesh.sprite.GetMesh());
                        m_WeightPainterPanel.UpdateWeightInspector(meshTool.mesh, GetMeshBoneNames(), GetMeshBoneColors(), GetSelectedMeshBoneIndices(), GetLockedMeshBoneIndices(), skinningCache.vertexSelection, skinningCache);
                    }
                }
            }
        }

        private void EditStart(ISelection<int> selection, bool relative)
        {
            AssociateSelectedBoneToCharacterPart();

            SetupWeightEditor(selection);

            if (m_WeightEditor.spriteMeshData != null)
                m_WeightEditor.OnEditStart(relative);
        }

        private void EditWeights(float hardness, bool emptySelectionEditsAll)
        {
            m_WeightEditor.emptySelectionEditsAll = emptySelectionEditsAll;

            if (m_WeightEditor.spriteMeshData != null)
                m_WeightEditor.DoEdit(hardness);
        }

        private void EditEnd()
        {
            if (m_WeightEditor.spriteMeshData != null)
            {
                m_WeightEditor.OnEditEnd();
                meshTool.UpdateWeights();
            }
        }

        private void InvokeBoneSelectionChanged()
        {
            skinningCache.events.boneSelectionChanged.RemoveListener(OnBoneSelectionChanged);
            skinningCache.events.boneSelectionChanged.Invoke();
            skinningCache.events.boneSelectionChanged.AddListener(OnBoneSelectionChanged);
        }

        private int ConvertBoneIndex(int index)
        {
            if (index != -1 && meshTool.mesh != null)
            {
                SkeletonCache skeleton = skinningCache.GetEffectiveSkeleton(meshTool.mesh.sprite);

                if (skeleton != null)
                {
                    BoneCache bone = skeleton.GetBone(index).ToCharacterIfNeeded();
                    index = Array.IndexOf(meshTool.mesh.bones, bone);
                }
            }

            return index;
        }

        private void SetupWeightEditor(ISelection<int> selection)
        {
            m_WeightEditor.spriteMeshData = meshTool.mesh;
            m_WeightEditor.mode = m_WeightPainterPanel.mode;
            m_WeightEditor.boneIndex = ConvertBoneIndex(m_WeightPainterPanel.boneIndex);
            m_WeightEditor.lockedBoneIndices = GetLockedMeshBoneIndices();
            m_WeightEditor.autoNormalize = true;
            m_WeightEditor.selection = selection;
            m_WeightEditor.emptySelectionEditsAll = true;
        }

        private void UpdateBrushSelection(Brush brush)
        {
            m_BrushSelection.Clear();
            m_CircleVertexSelector.spriteMeshData = meshTool.mesh;
            m_CircleVertexSelector.position = brush.position;
            m_CircleVertexSelector.radius = brush.size;
            m_CircleVertexSelector.selection = m_BrushSelection;
            m_CircleVertexSelector.Select();
        }

        private void DrawBrush(Brush brush)
        {
            Color oldColor = Handles.color;
            Handles.color = Color.white;

            if (EditorGUI.actionKey)
                Handles.color = Color.red;
            if (brush.isHot)
                Handles.color = Color.yellow;

            Handles.DrawWireDisc(brush.position, Vector3.forward, brush.size);
            Handles.color = oldColor;
        }

        protected override void OnGUI()
        {
            m_MeshPreviewBehaviour.showWeightMap = true;
            m_MeshPreviewBehaviour.overlaySelected = true;
            skeletonTool.skeletonStyle = SkeletonStyles.WeightMap;

            skeletonMode = SkeletonMode.EditPose;
            meshMode = SpriteMeshViewMode.EditGeometry;
            disableMeshEditor = true;
            clearBoneSelectionOnEscape = true;
            clearBoneSelectionOnPrimaryEmptyClick = true;

            bool isBoneHovered = skeletonTool.hoveredBone != null && !m_Brush.isHot;
            bool useBrush = paintMode == WeightPainterMode.Brush;

            meshTool.selectionOverride = null;

            if (useBrush)
                meshTool.selectionOverride = m_BrushSelection;

            drawVertexWeights = !useBrush;

            if (!useBrush)
                BeginWeightSliderDragIfNeeded();

            if (!useBrush)
                HandleWeightSliderDrag();

            DoSkeletonGUI();
            DoMeshGUI();

            if (useBrush && !isBoneHovered)
            {
                Matrix4x4 handlesMatrix = Handles.matrix;
                SpriteCache selectedSprite = skinningCache.selectedSprite;
                Matrix4x4 matrix = Matrix4x4.identity;

                if (selectedSprite != null)
                    matrix = selectedSprite.GetLocalToWorldMatrixFromMode();

                Handles.matrix *= matrix;

                skinningCache.brushSize = m_Brush.size = m_WeightPainterPanel.size;
                skinningCache.brushHardness = m_Brush.hardness = m_WeightPainterPanel.hardness;
                skinningCache.brushStep = m_Brush.step = m_WeightPainterPanel.step;

                if (m_Brush.isHot || !skinningCache.IsOnVisualElement())
                {
                    meshTool.BeginPositionOverride();
                    m_Brush.OnGUI();
                    meshTool.EndPositionOverride();
                }

                Handles.matrix = handlesMatrix;
            }
        }

        private void BeginWeightSliderDragIfNeeded()
        {
            Event evt = Event.current;
            MeshCache mesh = meshTool.mesh;

            if (evt.type != EventType.MouseDown || evt.button != 0)
                return;

            m_WeightSliderDragActive = false;
            m_WeightSliderDragUndoStarted = false;
            m_WeightSliderSmoothDragAmount = 0f;

            if (!CanStartWeightSliderDrag() || GetWeightSliderVertexAtMouse() == -1)
                return;

            if (m_WeightPainterPanel.mode == WeightEditorMode.Smooth)
                StoreWeightSliderSmoothStartWeights(mesh);

            m_WeightSliderDragActive = true;
        }

        private bool CanStartWeightSliderDrag()
        {
            MeshCache mesh = meshTool.mesh;
            if (mesh == null || mesh.boneCount == 0)
                return false;

            if (m_WeightPainterPanel.mode == WeightEditorMode.Smooth)
                return CanStartWeightSliderSmoothDrag();

            return GetWeightSliderBoneIndices().Count > 0;
        }

        private void HandleWeightSliderDrag()
        {
            Event evt = Event.current;

            if (!m_WeightSliderDragActive)
                return;

            if (evt.rawType == EventType.MouseUp)
            {
                EndWeightSliderDrag();
                return;
            }

            if (evt.type != EventType.MouseDrag || evt.button != 0)
                return;

            float deltaWeight = -evt.delta.y * kWeightSliderDragWeightPerPixel;
            if (Mathf.Approximately(deltaWeight, 0f))
                return;

            if (EditWeightSliderSelectedVertices(deltaWeight))
            {
                meshTool.UpdateWeights();
                evt.Use();
            }
        }

        private void EndWeightSliderDrag()
        {
            if (m_WeightSliderDragUndoStarted)
            {
                MeshCache mesh = meshTool.mesh;
                if (mesh != null)
                {
                    SpriteMeshDataController controller = new SpriteMeshDataController();
                    controller.spriteMeshData = mesh;
                    controller.SortTrianglesByDepth();
                    meshTool.UpdateWeights();
                }
            }

            m_WeightSliderDragActive = false;
            m_WeightSliderDragUndoStarted = false;
            m_WeightSliderSmoothDragAmount = 0f;
        }

        private List<int> GetWeightSliderBoneIndices()
        {
            m_WeightSliderBoneIndices.Clear();

            MeshCache mesh = meshTool.mesh;
            if (mesh == null)
                return m_WeightSliderBoneIndices;

            BoneCache[] meshBones = mesh.bones;
            BoneCache[] selectedBones = skinningCache.skeletonSelection.elements.ToSpriteSheetIfNeeded();

            for (int i = 0; i < selectedBones.Length; ++i)
            {
                BoneCache selectedBone = selectedBones[i];
                int boneIndex = Array.IndexOf(meshBones, selectedBone);

                if (boneIndex != -1 && !m_WeightSliderBoneIndices.Contains(boneIndex))
                    m_WeightSliderBoneIndices.Add(boneIndex);
            }

            return m_WeightSliderBoneIndices;
        }

        private int GetWeightSliderVertexAtMouse()
        {
            MeshCache mesh = meshTool.mesh;
            if (mesh == null)
                return -1;

            Matrix4x4 handlesMatrix = Handles.matrix;
            Handles.matrix *= mesh.sprite.GetLocalToWorldMatrixFromMode();
            meshTool.BeginPositionOverride();

            Vector2 mousePosition = Event.current.mousePosition;
            Vector2[] vertices = mesh.vertices;
            int nearestVertex = -1;
            float nearestDistance = kWeightSliderVertexHitRadius;

            for (int i = 0; i < mesh.vertexCount; ++i)
            {
                float distance = Vector2.Distance(HandleUtility.WorldToGUIPoint(vertices[i]), mousePosition);
                if (distance <= nearestDistance)
                {
                    nearestDistance = distance;
                    nearestVertex = i;
                }
            }

            meshTool.EndPositionOverride();
            Handles.matrix = handlesMatrix;
            return nearestVertex;
        }

        private bool AddWeightToSelectedVertices(float deltaWeight)
        {
            MeshCache mesh = meshTool.mesh;
            List<int> boneIndices = GetWeightSliderBoneIndices();

            if (mesh == null || boneIndices.Count == 0 || skinningCache.vertexSelection.Count == 0)
                return false;

            bool changed = false;
            int[] vertexIndices = skinningCache.vertexSelection.elements;

            for (int i = 0; i < vertexIndices.Length; ++i)
            {
                int vertexIndex = vertexIndices[i];
                if (vertexIndex < 0 || vertexIndex >= mesh.vertexCount)
                    continue;

                EditableBoneWeight weight = mesh.vertexWeights[vertexIndex];
                if (!CanTransferSelectedBoneWeight(weight, boneIndices, deltaWeight))
                    continue;

                if (!m_WeightSliderDragUndoStarted)
                {
                    skinningCache.BeginUndoOperation(TextContent.editWeights);
                    m_WeightSliderDragUndoStarted = true;
                }

                bool vertexChanged = TransferSelectedBoneWeight(weight, boneIndices, deltaWeight);

                if (vertexChanged)
                {
                    weight.Clamp(4);
                    weight.FilterChannels(0f);
                    changed = true;
                }
            }

            return changed;
        }

        private bool EditWeightSliderSelectedVertices(float deltaWeight)
        {
            if (m_WeightPainterPanel.mode == WeightEditorMode.Smooth)
                return SmoothWeightSliderSelectedVertices(deltaWeight);

            return AddWeightToSelectedVertices(deltaWeight);
        }

        private List<int> GetWeightSliderSmoothBoneIndices()
        {
            m_WeightSliderSmoothBoneIndices.Clear();

            MeshCache mesh = meshTool.mesh;
            if (mesh == null)
                return m_WeightSliderSmoothBoneIndices;

            BoneCache[] meshBones = mesh.bones;
            BoneCache[] selectedBones = skinningCache.skeletonSelection.elements.ToSpriteSheetIfNeeded();

            if (selectedBones.Length <= 1)
            {
                for (int i = 0; i < mesh.boneCount; ++i)
                    if (!IsWeightSliderBoneLocked(i))
                        m_WeightSliderSmoothBoneIndices.Add(i);

                return m_WeightSliderSmoothBoneIndices;
            }

            for (int i = 0; i < selectedBones.Length; ++i)
            {
                int boneIndex = Array.IndexOf(meshBones, selectedBones[i]);
                if (boneIndex != -1 && !IsWeightSliderBoneLocked(boneIndex) && !m_WeightSliderSmoothBoneIndices.Contains(boneIndex))
                    m_WeightSliderSmoothBoneIndices.Add(boneIndex);
            }

            return m_WeightSliderSmoothBoneIndices;
        }

        private bool CanStartWeightSliderSmoothDrag()
        {
            BoneCache[] selectedBones = skinningCache.skeletonSelection.elements.ToSpriteSheetIfNeeded();
            return selectedBones.Length <= 1
                ? GetWeightSliderSmoothBoneIndices().Count > 1
                : GetWeightSliderSmoothBoneIndices().Count > 1;
        }

        private bool SmoothWeightSliderSelectedVertices(float deltaWeight)
        {
            MeshCache mesh = meshTool.mesh;
            List<int> boneIndices = GetWeightSliderSmoothBoneIndices();

            if (mesh == null ||
                boneIndices.Count <= 1 ||
                skinningCache.vertexSelection.Count == 0 ||
                m_WeightSliderSmoothStartWeights == null ||
                m_WeightSliderSmoothStartWeights.Length != mesh.vertexCount)
                return false;

            float previousDragAmount = m_WeightSliderSmoothDragAmount;
            m_WeightSliderSmoothDragAmount = Mathf.Clamp01(m_WeightSliderSmoothDragAmount + deltaWeight);

            if (Mathf.Approximately(previousDragAmount, m_WeightSliderSmoothDragAmount))
                return false;

            bool changed = false;
            int[] vertexIndices = skinningCache.vertexSelection.elements;

            for (int i = 0; i < vertexIndices.Length; ++i)
            {
                int vertexIndex = vertexIndices[i];
                if (vertexIndex < 0 || vertexIndex >= mesh.vertexCount)
                    continue;

                if (!m_WeightSliderDragUndoStarted)
                {
                    skinningCache.BeginUndoOperation(TextContent.editWeights);
                    m_WeightSliderDragUndoStarted = true;
                }

                if (SmoothWeightSliderVertex(mesh, vertexIndex, boneIndices, m_WeightSliderSmoothDragAmount))
                    changed = true;
            }

            return changed;
        }

        private void StoreWeightSliderSmoothStartWeights(MeshCache mesh)
        {
            if (mesh == null)
            {
                m_WeightSliderSmoothStartWeights = null;
                return;
            }

            if (m_WeightSliderSmoothStartWeights == null || m_WeightSliderSmoothStartWeights.Length != mesh.vertexCount)
                m_WeightSliderSmoothStartWeights = new BoneWeight[mesh.vertexCount];

            for (int i = 0; i < mesh.vertexCount; ++i)
                m_WeightSliderSmoothStartWeights[i] = mesh.vertexWeights[i].ToBoneWeight(false);
        }

        private void SmoothWeightSliderVerticesOnce()
        {
            MeshCache mesh = meshTool.mesh;
            if (paintMode != WeightPainterMode.Slider || mesh == null || mesh.vertexCount == 0 || mesh.boneCount == 0)
                return;

            List<int> boneIndices = GetWeightSliderSmoothBoneIndices();
            if (boneIndices.Count == 0)
                return;

            List<WeightSliderSmoothNeighbor>[] neighbors = BuildWeightSliderSmoothNeighbors(mesh);
            BuildWeightSliderSmoothTargetVertices(mesh);

            if (m_WeightSliderSmoothTargetVertices.Count == 0)
                return;

            BoneWeight[] sourceWeights = new BoneWeight[mesh.vertexCount];
            for (int i = 0; i < mesh.vertexCount; ++i)
                sourceWeights[i] = mesh.vertexWeights[i].ToBoneWeight(false);

            bool changed = false;
            skinningCache.BeginUndoOperation(TextContent.editWeights);

            for (int i = 0; i < m_WeightSliderSmoothTargetVertices.Count; ++i)
            {
                int vertexIndex = m_WeightSliderSmoothTargetVertices[i];
                if (neighbors[vertexIndex].Count == 0)
                    continue;

                if (SmoothWeightSliderVertexFromNeighbors(mesh.vertexWeights[vertexIndex], sourceWeights[vertexIndex], sourceWeights, neighbors[vertexIndex], boneIndices))
                    changed = true;
            }

            if (!changed)
                return;

            SpriteMeshDataController controller = new SpriteMeshDataController();
            controller.spriteMeshData = mesh;
            controller.SortTrianglesByDepth();
            meshTool.UpdateWeights();
            m_WeightPainterPanel.UpdateWeightInspector(mesh, GetMeshBoneNames(), GetMeshBoneColors(), GetSelectedMeshBoneIndices(), GetLockedMeshBoneIndices(), skinningCache.vertexSelection, skinningCache);
            skinningCache.events.meshChanged.Invoke(mesh.sprite.GetMesh());
        }

        private void BuildWeightSliderSmoothTargetVertices(MeshCache mesh)
        {
            m_WeightSliderSmoothTargetVertices.Clear();

            if (skinningCache.vertexSelection.Count == 0)
            {
                for (int i = 0; i < mesh.vertexCount; ++i)
                    m_WeightSliderSmoothTargetVertices.Add(i);

                return;
            }

            int[] selectedVertices = skinningCache.vertexSelection.elements;
            for (int i = 0; i < selectedVertices.Length; ++i)
            {
                int vertexIndex = selectedVertices[i];
                if (vertexIndex >= 0 && vertexIndex < mesh.vertexCount && !m_WeightSliderSmoothTargetVertices.Contains(vertexIndex))
                    m_WeightSliderSmoothTargetVertices.Add(vertexIndex);
            }
        }

        private static List<WeightSliderSmoothNeighbor>[] BuildWeightSliderSmoothNeighbors(MeshCache mesh)
        {
            List<WeightSliderSmoothNeighbor>[] neighbors = new List<WeightSliderSmoothNeighbor>[mesh.vertexCount];
            for (int i = 0; i < neighbors.Length; ++i)
                neighbors[i] = new List<WeightSliderSmoothNeighbor>();

            AddWeightSliderSmoothEdges(neighbors, mesh.edges, mesh.vertices);
            AddWeightSliderSmoothEdges(neighbors, mesh.outlineEdges, mesh.vertices);
            return neighbors;
        }

        private static void AddWeightSliderSmoothEdges(List<WeightSliderSmoothNeighbor>[] neighbors, int2[] edges, Vector2[] vertices)
        {
            for (int i = 0; i < edges.Length; ++i)
            {
                int a = edges[i].x;
                int b = edges[i].y;

                if (a < 0 || b < 0 || a >= neighbors.Length || b >= neighbors.Length || a == b)
                    continue;

                float distance = Vector2.Distance(vertices[a], vertices[b]);
                float weight = 1f / Mathf.Max(distance, kWeightSliderSmoothEdgeMinDistance);

                AddWeightSliderSmoothNeighbor(neighbors[a], b, weight);
                AddWeightSliderSmoothNeighbor(neighbors[b], a, weight);
            }
        }

        private static void AddWeightSliderSmoothNeighbor(List<WeightSliderSmoothNeighbor> neighbors, int vertexIndex, float weight)
        {
            for (int i = 0; i < neighbors.Count; ++i)
            {
                if (neighbors[i].vertexIndex != vertexIndex)
                    continue;

                WeightSliderSmoothNeighbor neighbor = neighbors[i];
                neighbor.weight = Mathf.Max(neighbor.weight, weight);
                neighbors[i] = neighbor;
                return;
            }

            neighbors.Add(new WeightSliderSmoothNeighbor
            {
                vertexIndex = vertexIndex,
                weight = weight
            });
        }

        private bool SmoothWeightSliderVertexFromNeighbors(EditableBoneWeight target, BoneWeight currentWeight, BoneWeight[] sourceWeights, List<WeightSliderSmoothNeighbor> neighbors, List<int> boneIndices)
        {
            float[] targetBoneWeights = new float[boneIndices.Count];
            float selectedWeightSum = 0f;
            float lockedWeightSum = 0f;

            for (int i = 0; i < 4; ++i)
            {
                float channelWeight = currentWeight.GetWeight(i);
                if (channelWeight > 0f && IsWeightSliderBoneLocked(currentWeight.GetBoneIndex(i)))
                    lockedWeightSum += channelWeight;
            }

            for (int i = 0; i < boneIndices.Count; ++i)
            {
                float neighborWeightSum = 0f;
                float neighborWeightTotal = 0f;
                int boneIndex = boneIndices[i];
                float currentBoneWeight = GetBoneWeight(currentWeight, boneIndex);

                for (int j = 0; j < neighbors.Count; ++j)
                {
                    WeightSliderSmoothNeighbor neighbor = neighbors[j];
                    neighborWeightSum += GetBoneWeight(sourceWeights[neighbor.vertexIndex], boneIndex) * neighbor.weight;
                    neighborWeightTotal += neighbor.weight;
                }

                float neighborAverage = neighborWeightTotal > 0f ? neighborWeightSum / neighborWeightTotal : currentBoneWeight;
                float smoothedWeight = Mathf.Lerp(currentBoneWeight, neighborAverage, kWeightSliderSmoothButtonStrength);
                targetBoneWeights[i] = smoothedWeight;
                selectedWeightSum += smoothedWeight;
            }

            float availableWeight = Mathf.Max(0f, 1f - lockedWeightSum);
            if (selectedWeightSum > availableWeight && selectedWeightSum > 0f)
            {
                float selectedScale = availableWeight / selectedWeightSum;
                for (int i = 0; i < targetBoneWeights.Length; ++i)
                    targetBoneWeights[i] *= selectedScale;
                selectedWeightSum = availableWeight;
            }

            EditableBoneWeight nextWeight = new EditableBoneWeight();
            float unselectedWeightSum = 0f;

            for (int i = 0; i < 4; ++i)
            {
                float channelWeight = currentWeight.GetWeight(i);
                if (channelWeight <= 0f)
                    continue;

                int boneIndex = currentWeight.GetBoneIndex(i);
                if (IsWeightSliderBoneLocked(boneIndex))
                {
                    nextWeight.AddChannel(boneIndex, channelWeight, true);
                    continue;
                }

                if (boneIndices.Contains(boneIndex))
                    continue;

                unselectedWeightSum += channelWeight;
            }

            float unselectedScale = unselectedWeightSum > 0f && selectedWeightSum + lockedWeightSum < 1f ? (1f - selectedWeightSum - lockedWeightSum) / unselectedWeightSum : 0f;

            for (int i = 0; i < 4; ++i)
            {
                float channelWeight = currentWeight.GetWeight(i);
                if (channelWeight <= 0f)
                    continue;

                int boneIndex = currentWeight.GetBoneIndex(i);
                if (IsWeightSliderBoneLocked(boneIndex))
                    continue;

                if (boneIndices.Contains(boneIndex))
                    continue;

                float newWeight = channelWeight * unselectedScale;
                if (newWeight > 0f)
                    nextWeight.AddChannel(boneIndex, newWeight, true);
            }

            for (int i = 0; i < boneIndices.Count; ++i)
            {
                float newWeight = targetBoneWeights[i];
                if (newWeight > 0f)
                    nextWeight.AddChannel(boneIndices[i], newWeight, true);
            }

            nextWeight.UnifyChannelsWithSameBoneIndex();
            nextWeight.Clamp(4);
            nextWeight.Normalize();
            nextWeight.FilterChannels(0f);

            if (AreWeightsEqual(target, nextWeight))
                return false;

            target.Clear();
            foreach (BoneWeightChannel channel in nextWeight)
                target.AddChannel(channel.boneIndex, channel.weight, channel.enabled);

            return true;
        }

        private static float GetBoneWeight(BoneWeight weight, int boneIndex)
        {
            float sum = 0f;

            for (int i = 0; i < 4; ++i)
                if (weight.GetWeight(i) > 0f && weight.GetBoneIndex(i) == boneIndex)
                    sum += weight.GetWeight(i);

            return sum;
        }

        private static bool AreWeightsEqual(EditableBoneWeight first, EditableBoneWeight second)
        {
            for (int i = 0; i < first.Count; ++i)
            {
                BoneWeightChannel channel = first[i];
                if (!channel.enabled || channel.weight <= 0f)
                    continue;

                if (Mathf.Abs(channel.weight - GetEditableBoneWeight(second, channel.boneIndex)) > Mathf.Epsilon)
                    return false;
            }

            for (int i = 0; i < second.Count; ++i)
            {
                BoneWeightChannel channel = second[i];
                if (!channel.enabled || channel.weight <= 0f)
                    continue;

                if (Mathf.Abs(channel.weight - GetEditableBoneWeight(first, channel.boneIndex)) > Mathf.Epsilon)
                    return false;
            }

            return true;
        }

        private static float GetEditableBoneWeight(EditableBoneWeight weight, int boneIndex)
        {
            float sum = 0f;

            for (int i = 0; i < weight.Count; ++i)
            {
                BoneWeightChannel channel = weight[i];
                if (channel.enabled && channel.boneIndex == boneIndex)
                    sum += channel.weight;
            }

            return sum;
        }

        private bool SmoothWeightSliderVertex(MeshCache mesh, int vertexIndex, List<int> boneIndices, float blend)
        {
            EditableBoneWeight weight = mesh.vertexWeights[vertexIndex];
            BuildSmoothTargetWeights(mesh, mesh.vertices[vertexIndex], boneIndices);

            if (m_WeightSliderSmoothTargetWeights.Count == 0)
                return false;

            BoneWeight startWeight = m_WeightSliderSmoothStartWeights[vertexIndex];
            weight.Clear();
            float lockedWeightSum = 0f;

            for (int i = 0; i < 4; ++i)
            {
                float channelWeight = startWeight.GetWeight(i);
                if (channelWeight <= 0f)
                    continue;

                int boneIndex = startWeight.GetBoneIndex(i);
                bool isLocked = IsWeightSliderBoneLocked(boneIndex);
                float newWeight = isLocked ? channelWeight : channelWeight * (1f - blend);
                if (isLocked)
                    lockedWeightSum += channelWeight;

                if (newWeight > 0f)
                    weight.AddChannel(boneIndex, newWeight, true);
            }

            float targetWeightSum = 0f;
            for (int i = 0; i < boneIndices.Count; ++i)
                targetWeightSum += m_WeightSliderSmoothTargetWeights[i] * blend;

            float targetScale = targetWeightSum > 0f && targetWeightSum + lockedWeightSum > 1f ? Mathf.Max(0f, 1f - lockedWeightSum) / targetWeightSum : 1f;

            for (int i = 0; i < boneIndices.Count; ++i)
            {
                float targetWeight = m_WeightSliderSmoothTargetWeights[i];
                float newWeight = targetWeight * blend * targetScale;

                if (newWeight > 0f)
                    weight.AddChannel(boneIndices[i], newWeight, true);
            }

            weight.UnifyChannelsWithSameBoneIndex();
            weight.Clamp(4);
            weight.Normalize();
            weight.FilterChannels(0f);
            return true;
        }

        private void BuildSmoothTargetWeights(MeshCache mesh, Vector2 vertex, List<int> boneIndices)
        {
            m_WeightSliderSmoothTargetWeights.Clear();

            float totalInfluence = 0f;
            for (int i = 0; i < boneIndices.Count; ++i)
            {
                SpriteBoneData bone = mesh.GetBoneData(boneIndices[i]);
                float distance = GetDistanceToBone(vertex, bone);
                float influence = 1f / Mathf.Max(distance, kWeightSliderSmoothMinDistance);

                m_WeightSliderSmoothTargetWeights.Add(influence);
                totalInfluence += influence;
            }

            if (totalInfluence <= 0f)
            {
                m_WeightSliderSmoothTargetWeights.Clear();
                return;
            }

            float totalInfluenceInv = 1f / totalInfluence;
            for (int i = 0; i < m_WeightSliderSmoothTargetWeights.Count; ++i)
                m_WeightSliderSmoothTargetWeights[i] *= totalInfluenceInv;
        }

        private static float GetDistanceToBone(Vector2 vertex, SpriteBoneData bone)
        {
            Vector2 boneVector = bone.endPosition - bone.position;
            float sqrLength = boneVector.sqrMagnitude;

            if (sqrLength <= Mathf.Epsilon)
                return Vector2.Distance(vertex, bone.position);

            float t = Mathf.Clamp01(Vector2.Dot(vertex - bone.position, boneVector) / sqrLength);
            Vector2 closestPoint = bone.position + boneVector * t;
            return Vector2.Distance(vertex, closestPoint);
        }

        private bool CanTransferSelectedBoneWeight(EditableBoneWeight weight, List<int> boneIndices, float deltaWeight)
        {
            float selectedWeight = 0f;
            float otherWeight = 0f;

            for (int i = 0; i < weight.Count; ++i)
            {
                BoneWeightChannel channel = weight[i];
                if (!channel.enabled || channel.weight <= 0f)
                    continue;
                if (IsWeightSliderBoneLocked(channel.boneIndex))
                    continue;

                if (boneIndices.Contains(channel.boneIndex))
                    selectedWeight += channel.weight;
                else
                    otherWeight += channel.weight;
            }

            if (deltaWeight > 0f)
                return otherWeight > 0f;

            return selectedWeight > 0f && otherWeight > 0f;
        }

        private bool TransferSelectedBoneWeight(EditableBoneWeight weight, List<int> boneIndices, float deltaWeight)
        {
            BuildWeightTransferChannels(weight, boneIndices, deltaWeight > 0f);

            if (m_WeightSliderSelectedChannels.Count == 0 || m_WeightSliderOtherChannels.Count == 0)
                return false;

            float selectedWeight = SumChannelWeights(weight, m_WeightSliderSelectedChannels);
            float otherWeight = SumChannelWeights(weight, m_WeightSliderOtherChannels);

            if (deltaWeight > 0f)
                return TransferWeight(weight, m_WeightSliderOtherChannels, otherWeight, m_WeightSliderSelectedChannels, selectedWeight, Mathf.Min(deltaWeight, otherWeight));

            return TransferWeight(weight, m_WeightSliderSelectedChannels, selectedWeight, m_WeightSliderOtherChannels, otherWeight, Mathf.Min(-deltaWeight, selectedWeight));
        }

        private void BuildWeightTransferChannels(EditableBoneWeight weight, List<int> boneIndices, bool createMissingSelectedChannels)
        {
            m_WeightSliderSelectedChannels.Clear();
            m_WeightSliderOtherChannels.Clear();

            for (int i = 0; i < weight.Count; ++i)
            {
                BoneWeightChannel channel = weight[i];
                if (!channel.enabled || channel.weight <= 0f)
                    continue;
                if (IsWeightSliderBoneLocked(channel.boneIndex))
                    continue;

                if (boneIndices.Contains(channel.boneIndex))
                    m_WeightSliderSelectedChannels.Add(i);
                else
                    m_WeightSliderOtherChannels.Add(i);
            }

            if (!createMissingSelectedChannels)
                return;

            for (int i = 0; i < boneIndices.Count; ++i)
            {
                if (IsWeightSliderBoneLocked(boneIndices[i]))
                    continue;

                if (weight.GetChannelFromBoneIndex(boneIndices[i]) != -1)
                    continue;

                weight.AddChannel(boneIndices[i], 0f, true);
                int channel = weight.GetChannelFromBoneIndex(boneIndices[i]);
                if (channel != -1)
                    m_WeightSliderSelectedChannels.Add(channel);
            }
        }

        private float SumChannelWeights(EditableBoneWeight weight, List<int> channels)
        {
            float sum = 0f;
            for (int i = 0; i < channels.Count; ++i)
                sum += weight[channels[i]].weight;

            return sum;
        }

        private bool TransferWeight(EditableBoneWeight weight, List<int> fromChannels, float fromWeight, List<int> toChannels, float toWeight, float transferWeight)
        {
            if (transferWeight <= 0f || fromWeight <= 0f || toChannels.Count == 0)
                return false;

            for (int i = 0; i < fromChannels.Count; ++i)
            {
                BoneWeightChannel channel = weight[fromChannels[i]];
                channel.weight = Mathf.Max(0f, channel.weight - transferWeight * channel.weight / fromWeight);
                channel.enabled = channel.weight > 0f;
            }

            if (toWeight > 0f)
            {
                for (int i = 0; i < toChannels.Count; ++i)
                {
                    BoneWeightChannel channel = weight[toChannels[i]];
                    channel.weight = Mathf.Clamp01(channel.weight + transferWeight * channel.weight / toWeight);
                    channel.enabled = channel.weight > 0f;
                }
            }
            else
            {
                float distributedWeight = transferWeight / toChannels.Count;
                for (int i = 0; i < toChannels.Count; ++i)
                {
                    BoneWeightChannel channel = weight[toChannels[i]];
                    channel.weight = Mathf.Clamp01(channel.weight + distributedWeight);
                    channel.enabled = channel.weight > 0f;
                }
            }

            return true;
        }

        private bool IsWeightSliderBoneLocked(int boneIndex)
        {
            return m_WeightSliderLockedBoneIndices.Contains(boneIndex);
        }
    }
}
