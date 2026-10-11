using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    // Shared by the Sprite Editor and Scene Edit Weights hosts.
    internal static class WeightSmoothing
    {
        const float kWeightSliderSmoothButtonStrength = 0.45f;
        const float kWeightSliderSmoothEdgeMinDistance = 0.0001f;
        internal struct Neighbor { public int vertexIndex; public float weight; }
        internal static List<Neighbor>[] BuildNeighbors(MeshCache mesh)
        {
            List<Neighbor>[] neighbors = new List<Neighbor>[mesh.vertexCount];
            for (int i = 0; i < neighbors.Length; ++i)
                neighbors[i] = new List<Neighbor>();

            AddWeightSliderSmoothEdges(neighbors, mesh.edges, mesh.vertices);
            AddWeightSliderSmoothEdges(neighbors, mesh.outlineEdges, mesh.vertices);
            return neighbors;
        }

        private static void AddWeightSliderSmoothEdges(List<Neighbor>[] neighbors, int2[] edges, Vector2[] vertices)
        {
            for (int i = 0; i < edges.Length; ++i)
            {
                int a = edges[i].x;
                int b = edges[i].y;

                if (a < 0 || b < 0 || a >= neighbors.Length || b >= neighbors.Length || a == b)
                    continue;

                float distance = Vector2.Distance(vertices[a], vertices[b]);
                float weight = 1f / Mathf.Max(distance, kWeightSliderSmoothEdgeMinDistance);

                AddNeighbor(neighbors[a], b, weight);
                AddNeighbor(neighbors[b], a, weight);
            }
        }

        private static void AddNeighbor(List<Neighbor> neighbors, int vertexIndex, float weight)
        {
            for (int i = 0; i < neighbors.Count; ++i)
            {
                if (neighbors[i].vertexIndex != vertexIndex)
                    continue;

                Neighbor neighbor = neighbors[i];
                neighbor.weight = Mathf.Max(neighbor.weight, weight);
                neighbors[i] = neighbor;
                return;
            }

            neighbors.Add(new Neighbor
            {
                vertexIndex = vertexIndex,
                weight = weight
            });
        }

        internal static bool SmoothVertex(EditableBoneWeight target, BoneWeight currentWeight, BoneWeight[] sourceWeights, List<Neighbor> neighbors, List<int> boneIndices, ISet<int> lockedBones)
        {
            float[] targetBoneWeights = new float[boneIndices.Count];
            float selectedWeightSum = 0f;
            float lockedWeightSum = 0f;

            for (int i = 0; i < 4; ++i)
            {
                float channelWeight = currentWeight.GetWeight(i);
                if (channelWeight > 0f && lockedBones.Contains(currentWeight.GetBoneIndex(i)))
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
                    Neighbor neighbor = neighbors[j];
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
                if (lockedBones.Contains(boneIndex))
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
                if (lockedBones.Contains(boneIndex))
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

    }
}
