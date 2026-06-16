using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.U2D.Common;
using UnityEditor.U2D.Layout;
using UnityEngine;

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

        private WeightPainterPanel m_WeightPainterPanel;
        private WeightEditor m_WeightEditor = new WeightEditor();
        private Brush m_Brush = new Brush(new GUIWrapper());
        private ISelection<int> m_BrushSelection = new IndexedSelection();
        private CircleVertexSelector m_CircleVertexSelector = new CircleVertexSelector();
        private readonly List<int> m_WeightSliderBoneIndices = new List<int>(4);
        private readonly List<int> m_WeightSliderSelectedChannels = new List<int>(4);
        private readonly List<int> m_WeightSliderOtherChannels = new List<int>(4);
        private bool m_WeightSliderDragActive;
        private bool m_WeightSliderDragUndoStarted;

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

        private string[] GetUniqueBoneNames(BoneCache[] bones, SkeletonCache skeleton)
        {
            return Array.ConvertAll(bones, b => skeleton.GetUniqueName(b));
        }

        private void UpdatePanel()
        {
            m_WeightPainterPanel.SetActive(skinningCache.selectedSprite != null);
            m_WeightPainterPanel.UpdateWeightInspector(meshTool.mesh, GetMeshBoneNames(), skinningCache.vertexSelection, skinningCache);
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
        }

        public override void Initialize(LayoutOverlay layout)
        {
            base.Initialize(layout);

            m_WeightPainterPanel = WeightPainterPanel.GenerateFromUXML();
            m_WeightPainterPanel.SetHiddenFromLayout(true);
            layout.rightOverlay.Add(m_WeightPainterPanel);

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
        }

        internal void SetWeightPainterPanelTitle(string title)
        {
            m_WeightPainterPanel.title = title;
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
                        m_WeightPainterPanel.UpdateWeightInspector(meshTool.mesh, GetMeshBoneNames(), skinningCache.vertexSelection, skinningCache);
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
            m_WeightEditor.autoNormalize = m_WeightPainterPanel.normalize;
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

            if (evt.type != EventType.MouseDown || evt.button != 0)
                return;

            m_WeightSliderDragActive = false;
            m_WeightSliderDragUndoStarted = false;

            if (GetWeightSliderBoneIndices().Count == 0 || GetWeightSliderVertexAtMouse() == -1)
                return;

            m_WeightSliderDragActive = true;
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

            if (AddWeightToSelectedVertices(deltaWeight))
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

        private bool CanTransferSelectedBoneWeight(EditableBoneWeight weight, List<int> boneIndices, float deltaWeight)
        {
            float selectedWeight = 0f;
            float otherWeight = 0f;

            for (int i = 0; i < weight.Count; ++i)
            {
                BoneWeightChannel channel = weight[i];
                if (!channel.enabled || channel.weight <= 0f)
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

                if (boneIndices.Contains(channel.boneIndex))
                    m_WeightSliderSelectedChannels.Add(i);
                else
                    m_WeightSliderOtherChannels.Add(i);
            }

            if (!createMissingSelectedChannels)
                return;

            for (int i = 0; i < boneIndices.Count; ++i)
            {
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
    }
}
