using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal enum WeightEditorMode
    {
        AddAndSubtract,
        GrowAndShrink,
        Smooth
    }

    internal class WeightEditor
    {
        public BaseSpriteMeshData spriteMeshData
        {
            get => m_SpriteMeshDataController.spriteMeshData;
            set => m_SpriteMeshDataController.spriteMeshData = value;
        }

        public ICacheUndo cacheUndo { get; set; }
        public WeightEditorMode mode { get; set; }
        public int boneIndex { get; set; }
        public int[] lockedBoneIndices { get; set; }
        public ISelection<int> selection { get; set; }
        public bool emptySelectionEditsAll { get; set; }
        public bool autoNormalize { get; set; }

        WeightEditorMode currentMode { get; set; }
        bool useRelativeValues { get; set; }

        SpriteMeshDataController m_SpriteMeshDataController = new SpriteMeshDataController();
        const int k_MaxSmoothIterations = 8;
        float[] m_SmoothValues;
        readonly List<BoneWeight[]> m_SmoothedBoneWeights = new List<BoneWeight[]>();
        readonly List<BoneWeight> m_StoredBoneWeights = new List<BoneWeight>();
        int boneCount => spriteMeshData != null ? spriteMeshData.boneCount : 0;

        public WeightEditor()
        {
            autoNormalize = true;
        }

        public void OnEditStart(bool relative)
        {
            Validate();

            RegisterUndo();
            currentMode = mode;
            useRelativeValues = relative;

            if (!useRelativeValues)
                StoreBoneWeights();

            if (mode == WeightEditorMode.Smooth)
                PrepareSmoothingBuffers();
        }

        public void OnEditEnd()
        {
            Validate();

            if (currentMode == WeightEditorMode.AddAndSubtract)
            {
                for (int i = 0; i < spriteMeshData.vertexCount; ++i)
                    spriteMeshData.vertexWeights[i].Clamp(4);
            }

            if (autoNormalize)
                m_SpriteMeshDataController.NormalizeWeights(null);

            m_SpriteMeshDataController.SortTrianglesByDepth();
        }

        public void DoEdit(float value)
        {
            Validate();

            if (!useRelativeValues)
                RestoreBoneWeights();

            if (currentMode == WeightEditorMode.AddAndSubtract)
                SetWeight(value);
            else if (currentMode == WeightEditorMode.GrowAndShrink)
                SetWeight(value, false);
            else if (currentMode == WeightEditorMode.Smooth)
                SmoothWeights(value);
        }

        void Validate()
        {
            if (spriteMeshData == null)
                throw (new Exception(TextContent.noSpriteSelected));
        }

        void RegisterUndo()
        {
            Debug.Assert(cacheUndo != null);

            cacheUndo.BeginUndoOperation(TextContent.editWeights);
        }

        void SetWeight(float value, bool createNewChannel = true)
        {
            if (boneIndex == -1 || spriteMeshData == null || IsBoneLocked(boneIndex))
                return;

            Debug.Assert(selection != null);

            for (int i = 0; i < spriteMeshData.vertexCount; ++i)
            {
                if (selection.Count == 0 && emptySelectionEditsAll ||
                    selection.Count > 0 && selection.Contains(i))
                {
                    EditableBoneWeight editableBoneWeight = spriteMeshData.vertexWeights[i];
                    int channel = editableBoneWeight.GetChannelFromBoneIndex(boneIndex);

                    if (channel == -1)
                    {
                        if (createNewChannel && value > 0f)
                        {
                            editableBoneWeight.AddChannel(boneIndex, 0f, true);
                            channel = editableBoneWeight.GetChannelFromBoneIndex(boneIndex);
                        }
                        else
                        {
                            continue;
                        }
                    }

                    editableBoneWeight[channel].weight += value;

                    if (editableBoneWeight.Sum() > 1f)
                        CompensateOtherChannelsRespectingLocks(editableBoneWeight, channel);

                    editableBoneWeight.FilterChannels(0f);
                }
            }
        }

        void SmoothWeights(float value)
        {
            Debug.Assert(selection != null);

            for (int i = 0; i < spriteMeshData.vertexCount; ++i)
            {
                if (selection.Count == 0 && emptySelectionEditsAll ||
                    selection.Count > 0 && selection.Contains(i))
                {
                    float smoothValue = m_SmoothValues[i];

                    if (smoothValue >= k_MaxSmoothIterations)
                        continue;

                    m_SmoothValues[i] = Mathf.Clamp(smoothValue + value, 0f, k_MaxSmoothIterations);

                    float lerpValue = GetLerpValue(m_SmoothValues[i]);
                    int lerpIndex = GetLerpIndex(m_SmoothValues[i]);
                    BoneWeight[] smoothedBoneWeightsFloor = GetSmoothedBoneWeights(lerpIndex - 1);
                    BoneWeight[] smoothedBoneWeightsCeil = GetSmoothedBoneWeights(lerpIndex);

                    BoneWeight boneWeight = EditableBoneWeightUtility.Lerp(smoothedBoneWeightsFloor[i], smoothedBoneWeightsCeil[i], lerpValue);
                    EditableBoneWeight editableBoneWeight = spriteMeshData.vertexWeights[i];
                    BoneWeight lockedSourceWeight = m_StoredBoneWeights.Count == spriteMeshData.vertexCount ? m_StoredBoneWeights[i] : editableBoneWeight.ToBoneWeight(false);
                    editableBoneWeight.SetFromBoneWeight(boneWeight);
                    RestoreLockedWeights(editableBoneWeight, lockedSourceWeight);
                }
            }
        }

        private void CompensateOtherChannelsRespectingLocks(EditableBoneWeight editableBoneWeight, int masterChannel)
        {
            editableBoneWeight.ValidateChannels();

            float lockedWeight = 0f;
            int validChannelCount = 0;
            float sum = 0f;

            for (int i = 0; i < editableBoneWeight.Count; ++i)
            {
                if (!editableBoneWeight[i].enabled)
                    continue;

                if (i != masterChannel && IsBoneLocked(editableBoneWeight[i].boneIndex))
                {
                    lockedWeight += editableBoneWeight[i].weight;
                    continue;
                }

                if (i != masterChannel)
                {
                    sum += editableBoneWeight[i].weight;
                    ++validChannelCount;
                }
            }

            editableBoneWeight[masterChannel].weight = Mathf.Min(editableBoneWeight[masterChannel].weight, Mathf.Max(0f, 1f - lockedWeight));
            float targetSum = 1f - lockedWeight - editableBoneWeight[masterChannel].weight;

            for (int i = 0; i < editableBoneWeight.Count; ++i)
            {
                if (i == masterChannel || !editableBoneWeight[i].enabled || IsBoneLocked(editableBoneWeight[i].boneIndex))
                    continue;

                if (validChannelCount == 0)
                    editableBoneWeight[i].weight = 0f;
                else if (sum > 0f)
                    editableBoneWeight[i].weight *= targetSum / sum;
                else
                    editableBoneWeight[i].weight = targetSum / validChannelCount;
            }
        }

        private void RestoreLockedWeights(EditableBoneWeight editableBoneWeight, BoneWeight lockedSourceWeight)
        {
            if (lockedBoneIndices == null || lockedBoneIndices.Length == 0)
                return;

            float[] weights = new float[boneCount];
            for (int i = 0; i < editableBoneWeight.Count; ++i)
            {
                BoneWeightChannel channel = editableBoneWeight[i];
                if (channel.enabled && channel.boneIndex >= 0 && channel.boneIndex < boneCount)
                    weights[channel.boneIndex] += channel.weight;
            }

            float lockedWeight = 0f;
            for (int i = 0; i < lockedBoneIndices.Length; ++i)
            {
                int lockedBoneIndex = lockedBoneIndices[i];
                if (lockedBoneIndex < 0 || lockedBoneIndex >= boneCount)
                    continue;

                weights[lockedBoneIndex] = GetBoneWeight(lockedSourceWeight, lockedBoneIndex);
                lockedWeight += weights[lockedBoneIndex];
            }

            float unlockedWeight = 0f;
            for (int i = 0; i < boneCount; ++i)
                if (!IsBoneLocked(i))
                    unlockedWeight += weights[i];

            float unlockedTargetWeight = Mathf.Max(0f, 1f - lockedWeight);
            if (unlockedWeight > 0f)
            {
                for (int i = 0; i < boneCount; ++i)
                    if (!IsBoneLocked(i))
                        weights[i] *= unlockedTargetWeight / unlockedWeight;
            }

            editableBoneWeight.Clear();
            for (int i = 0; i < weights.Length; ++i)
                if (weights[i] > 0f)
                    editableBoneWeight.AddChannel(i, weights[i], true);

            editableBoneWeight.UnifyChannelsWithSameBoneIndex();
            editableBoneWeight.Clamp(4);
            editableBoneWeight.FilterChannels(0f);
        }

        private static float GetBoneWeight(BoneWeight boneWeight, int boneIndex)
        {
            float weight = 0f;
            for (int i = 0; i < 4; ++i)
                if (boneWeight.GetWeight(i) > 0f && boneWeight.GetBoneIndex(i) == boneIndex)
                    weight += boneWeight.GetWeight(i);

            return weight;
        }

        private bool IsBoneLocked(int boneIndex)
        {
            if (lockedBoneIndices == null)
                return false;

            for (int i = 0; i < lockedBoneIndices.Length; ++i)
                if (lockedBoneIndices[i] == boneIndex)
                    return true;

            return false;
        }

        void PrepareSmoothingBuffers()
        {
            if (m_SmoothValues == null || m_SmoothValues.Length != spriteMeshData.vertexCount)
                m_SmoothValues = new float[spriteMeshData.vertexCount];

            Array.Clear(m_SmoothValues, 0, m_SmoothValues.Length);

            m_SmoothedBoneWeights.Clear();

            BoneWeight[] boneWeights = new BoneWeight[spriteMeshData.vertexCount];

            for (int i = 0; i < spriteMeshData.vertexCount; i++)
            {
                EditableBoneWeight editableBoneWeight = spriteMeshData.vertexWeights[i];
                boneWeights[i] = editableBoneWeight.ToBoneWeight(false);
            }

            m_SmoothedBoneWeights.Add(boneWeights);
        }

        BoneWeight[] GetSmoothedBoneWeights(int lerpIndex)
        {
            Debug.Assert(lerpIndex >= 0);

            while (lerpIndex >= m_SmoothedBoneWeights.Count && lerpIndex <= k_MaxSmoothIterations)
            {
                SmoothingUtility.SmoothWeights(m_SmoothedBoneWeights[^1], spriteMeshData.indices, boneCount, out BoneWeight[] boneWeights);
                m_SmoothedBoneWeights.Add(boneWeights);
            }

            return m_SmoothedBoneWeights[Mathf.Min(lerpIndex, k_MaxSmoothIterations)];
        }

        static float GetLerpValue(float smoothValue)
        {
            Debug.Assert(smoothValue >= 0f);
            return smoothValue - Mathf.Floor(smoothValue);
        }

        static int GetLerpIndex(float smoothValue)
        {
            Debug.Assert(smoothValue >= 0f);
            return Mathf.RoundToInt(Mathf.Floor(smoothValue) + 1);
        }

        void StoreBoneWeights()
        {
            Debug.Assert(selection != null);

            m_StoredBoneWeights.Clear();

            for (int i = 0; i < spriteMeshData.vertexCount; i++)
            {
                EditableBoneWeight editableBoneWeight = spriteMeshData.vertexWeights[i];
                m_StoredBoneWeights.Add(editableBoneWeight.ToBoneWeight(false));
            }
        }

        void RestoreBoneWeights()
        {
            Debug.Assert(selection != null);

            for (int i = 0; i < spriteMeshData.vertexCount; i++)
            {
                EditableBoneWeight editableBoneWeight = spriteMeshData.vertexWeights[i];
                editableBoneWeight.SetFromBoneWeight(m_StoredBoneWeights[i]);
            }

            if (m_SmoothValues != null)
                Array.Clear(m_SmoothValues, 0, m_SmoothValues.Length);
        }
    }
}
