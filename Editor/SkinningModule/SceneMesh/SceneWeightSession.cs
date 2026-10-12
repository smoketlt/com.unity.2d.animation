using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal sealed class SceneWeightSession : ScriptableObject
    {
        [SerializeField] SceneBoneSession m_Context;
        [SerializeField] string m_Saved;
        [SerializeField] string m_Fingerprint;
        [SerializeField] List<string> m_Locked = new List<string>();
        [SerializeField] List<string> m_DrawOrder = new List<string>();
        int[] m_DrawIndices;
        const string kBindingPrefix = "Uniskin.WeightBindings.v1:";
        [Serializable] sealed class Bindings { public List<Binding> sprites = new List<Binding>(); }
        [Serializable] sealed class Binding { public string id; public string[] bones; public string[] drawOrder; }
        [Serializable] sealed class State { public string[] bones; public EditableBoneWeight[] weights; public string[] drawOrder; }

        internal SceneBoneSession context => m_Context;
        internal SkinningCache cache => m_Context.cache;
        internal MeshCache mesh => cache.selectedSprite.GetMesh();
        internal IndexedSelection vertices => cache.vertexSelection;
        internal bool dirty => m_Saved != Snapshot();
        internal int[] selectedBones => mesh.bones.Select((bone, i) => (bone, i))
            .Where(pair => cache.skeletonSelection.Contains(pair.bone)).Select(pair => pair.i).ToArray();
        internal HashSet<int> lockedBones => mesh.bones.Select((bone, i) => (bone, i))
            .Where(pair => m_Locked.Contains(pair.bone.guid)).Select(pair => pair.i).ToHashSet();
        internal int[] targets => vertices.Count == 0 ? Enumerable.Range(0, mesh.vertexCount).ToArray() :
            vertices.elements.Where(i => i >= 0 && i < mesh.vertexCount).ToArray();
        internal int[] orderedBoneIndices
        {
            get
            {
                var bones = mesh.bones;
                return Enumerable.Range(0, bones.Length)
                    .OrderBy(i => { var rank = m_DrawOrder.IndexOf(bones[i].guid); return rank < 0 ? m_DrawOrder.Count + i : rank; }).ToArray();
            }
        }

        internal void MoveBone(BoneCache bone, int insertionIndex)
        {
            var order = orderedBoneIndices.Select(i => mesh.bones[i].guid).ToList();
            var oldIndex = order.IndexOf(bone.guid);
            if (oldIndex < 0) return;
            insertionIndex = Mathf.Clamp(insertionIndex, 0, order.Count);
            if (oldIndex < insertionIndex) --insertionIndex;
            if (oldIndex == insertionIndex) return;
            using (cache.UndoScope("Reorder Mesh Bone Draw Order"))
            {
                Undo.RegisterCompleteObjectUndo(this, "Reorder Mesh Bone Draw Order");
                order.RemoveAt(oldIndex); order.Insert(insertionIndex, bone.guid);
                m_DrawOrder = order; Changed();
            }
        }

        internal void InvalidateDrawOrder() => m_DrawIndices = null;
        internal int[] DrawIndices()
        {
            if (m_DrawOrder.Count == 0) return mesh.indices;
            if (m_DrawIndices != null) return m_DrawIndices;
            var ranks = new int[mesh.boneCount]; var order = orderedBoneIndices;
            for (var i = 0; i < order.Length; ++i) ranks[order[i]] = i;
            var depths = new float[mesh.vertexCount];
            for (var vertex = 0; vertex < depths.Length; ++vertex)
                foreach (var channel in mesh.vertexWeights[vertex])
                    if (channel.enabled && channel.boneIndex >= 0 && channel.boneIndex < ranks.Length)
                        depths[vertex] += ranks[channel.boneIndex] * channel.weight;
            var indices = mesh.indices;
            m_DrawIndices = Enumerable.Range(0, indices.Length / 3)
                .OrderBy(triangle => (depths[indices[triangle * 3]] + depths[indices[triangle * 3 + 1]] + depths[indices[triangle * 3 + 2]]) / 3f)
                .SelectMany(triangle => new[] { indices[triangle * 3], indices[triangle * 3 + 1], indices[triangle * 3 + 2] }).ToArray();
            return m_DrawIndices;
        }

        internal void Load(Sprite sprite, SceneMeshSource source)
        {
            if (!source.hasAuthoredMesh) throw new InvalidOperationException("Create Mesh before opening Edit Weights.");
            m_Context = CreateInstance<SceneBoneSession>();
            m_Context.hideFlags = HideFlags.HideAndDontSave;
            m_Context.Load(sprite, source);
            var saved = ReadBindings(source.userData).sprites.FirstOrDefault(item => item.id == m_Context.spriteId);
            m_DrawOrder = saved?.drawOrder?.ToList() ?? new List<string>();
            InvalidateDrawOrder();
            if (!cache.hasCharacter)
            {
                if (saved?.bones != null)
                {
                    var weighted = mesh.vertexWeights.SelectMany(weight => weight).Where(channel => channel.enabled && channel.weight > 0)
                        .Select(channel => channel.boneIndex).ToHashSet();
                    // Also recognize bones subsequently weighted in Sprite Editor.
                    mesh.bones = mesh.bones.Where((bone, i) => saved.bones.Contains(bone.guid) || weighted.Contains(i)).ToArray();
                }
            }
            m_Saved = Snapshot();
            m_Fingerprint = Fingerprint(source);
        }

        internal void Attach() => m_Context?.Attach();
        string Snapshot() => cache == null ? string.Empty : JsonUtility.ToJson(new State
            { bones = mesh.bones.Select(bone => bone.guid).ToArray(), weights = mesh.vertexWeights, drawOrder = m_DrawOrder.ToArray() });
        static string Fingerprint(SceneMeshSource source) => SceneBoneSession.Fingerprint(source) + "\n" + source.userData;

        internal void SelectBone(BoneCache bone, bool additive, bool toggleSelected = true)
        {
            var selection = cache.skeletonSelection;
            if (toggleSelected && selection.Contains(bone))
                selection.elements = selection.elements.Where(item => item != bone).ToArray();
            else if (!additive) selection.elements = new[] { bone };
            else if (!selection.Contains(bone)) selection.elements = selection.elements.Concat(new[] { bone }).ToArray();
            cache.events.boneSelectionChanged.Invoke();
        }

        internal void ToggleLock(BoneCache bone)
        {
            if (!m_Locked.Remove(bone.guid)) m_Locked.Add(bone.guid);
        }

        internal void Changed() { InvalidateDrawOrder(); cache.events.meshChanged.Invoke(mesh); }

        internal void Bind(BoneCache bone)
        {
            if (bone == null || bone.IsConstraintParent() || mesh.ContainsBone(bone)) return;
            using (cache.UndoScope("Bind Mesh Bone"))
            {
                SetBones(mesh.bones.Concat(new[] { bone }).ToArray());
                // Bind preserves authored positive weights; Auto is an explicit command.
                m_Context.WithSourcePose(() => cache.selectedSprite.CalculateMissingWeights());
                Changed();
            }
        }

        void SetBones(BoneCache[] bones)
        {
            if (cache.hasCharacter)
            {
                var part = cache.selectedSprite.GetCharacterPart();
                part.bones = bones;
                cache.selectedSprite.UpdateMesh(bones);
                m_Context.WithSourcePose(() => cache.CreateSpriteSheetSkeleton(part));
                cache.events.characterPartChanged.Invoke(part);
            }
            else mesh.bones = bones;
        }

        internal void Remove()
        {
            var remove = selectedBones.Select(i => mesh.bones[i]).ToArray();
            if (remove.Length == 0) return;
            if (remove.Length == mesh.boneCount)
                throw new InvalidOperationException("Keep at least one bound bone so removed weights can be redistributed.");
            using (cache.UndoScope("Remove Mesh Bones"))
            {
                SetBones(mesh.bones.Except(remove).ToArray());
                foreach (var weight in mesh.vertexWeights)
                {
                    if (mesh.boneCount > 0 && weight.Sum() <= 0)
                        for (var i = 0; i < Mathf.Min(4, mesh.boneCount); ++i)
                            weight.AddChannel(i, 1f / Mathf.Min(4, mesh.boneCount), true);
                    weight.Normalize();
                }
                cache.skeletonSelection.Clear();
                Changed();
            }
        }

        internal void Swap()
        {
            var bones = selectedBones;
            if (bones.Length != 2 || bones.Any(i => lockedBones.Contains(i))) return;
            using (cache.UndoScope("Swap Bone Weights"))
            {
                foreach (var vertex in targets)
                    foreach (var channel in mesh.vertexWeights[vertex])
                        if (channel.boneIndex == bones[0]) channel.boneIndex = bones[1];
                        else if (channel.boneIndex == bones[1]) channel.boneIndex = bones[0];
                Changed();
            }
        }

        internal SpriteCache[] weldSources => !cache.hasCharacter ? Array.Empty<SpriteCache>() : cache.character.parts
            .Select(part => part.sprite).Where(sprite => sprite != cache.selectedSprite && CanWeld(sprite)).ToArray();

        bool CanWeld(SpriteCache source)
        {
            var sourceMesh = source.GetMesh();
            if (sourceMesh == null || sourceMesh.vertexCount == 0 || sourceMesh.indices.Length < 3 ||
                sourceMesh.vertexWeights.Length != sourceMesh.vertexCount) return false;
            var targetGuids = mesh.bones.Select(bone => bone.guid).ToHashSet();
            return sourceMesh.bones.Select(bone => bone.guid).Distinct().Count(targetGuids.Contains) >= 2;
        }

        internal void Weld(SpriteCache source, bool overlapping)
        {
            if (source == null || !weldSources.Contains(source))
                throw new InvalidOperationException("Choose another mesh in this character with at least two common bound bones.");
            var sourceMesh = source.GetMesh();
            var targetBones = mesh.bones;
            var map = sourceMesh.bones.Select(bone => Array.FindIndex(targetBones, target => target.guid == bone.guid)).ToArray();
            // Use authored positions, never temporary pose-preview vertices or scene transforms.
            sourceMesh.ClearVertexPositionOverride();
            mesh.ClearVertexPositionOverride();
            var sourceMatrix = source.GetCharacterPart().localToWorldMatrix;
            var targetMatrix = cache.selectedSprite.GetCharacterPart().localToWorldMatrix;
            var positions = sourceMesh.vertices.Select(position => (Vector2)sourceMatrix.MultiplyPoint3x4(position)).ToArray();
            var updates = new List<(int vertex, float[] values)>();
            var locked = lockedBones;
            foreach (var vertex in targets)
            {
                var values = new float[mesh.boneCount];
                if (WeightWelding.Sample(targetMatrix.MultiplyPoint3x4(mesh.vertices[vertex]), positions,
                    sourceMesh.indices, sourceMesh.vertexWeights, map, overlapping, values) &&
                    values.Where((value, bone) => !locked.Contains(bone)).Any(value => value > 0))
                    updates.Add((vertex, values));
            }
            if (updates.Count == 0) return;
            using (cache.UndoScope("Weld Mesh Weights"))
            {
                foreach (var update in updates) SetDistribution(update.vertex, update.values);
                Changed();
            }
        }

        internal void Smooth()
        {
            var selected = selectedBones;
            var locked = lockedBones;
            var bones = (selected.Length > 1 ? selected : Enumerable.Range(0, mesh.boneCount))
                .Where(i => !locked.Contains(i)).ToList();
            if (bones.Count == 0) return;
            var source = mesh.vertexWeights.Select(weight => weight.ToBoneWeight(false)).ToArray();
            var neighbors = WeightSmoothing.BuildNeighbors(mesh);
            using (cache.UndoScope("Smooth Mesh Weights"))
            {
                foreach (var vertex in targets)
                    if (neighbors[vertex].Count > 0)
                    {
                        WeightSmoothing.SmoothVertex(mesh.vertexWeights[vertex], source[vertex], source, neighbors[vertex], bones, locked);
                        if (locked.Count > 0)
                        {
                            var values = Values(vertex);
                            mesh.vertexWeights[vertex] = EditableBoneWeightUtility.CreateFromBoneWeight(source[vertex]);
                            SetDistribution(vertex, values);
                        }
                    }
                Changed();
            }
        }

        internal void Auto()
        {
            var selected = selectedBones;
            var bones = selected.Length > 0 ? selected : Enumerable.Range(0, mesh.boneCount).ToArray();
            if (bones.Length == 0) return;
            using (cache.UndoScope("Auto Mesh Weights"))
            {
                m_Context.WithSourcePose(() =>
                {
                    var generated = new SpriteMeshData();
                    generated.SetFrame(mesh.frame);
                    generated.SetVertices((Vector2[])mesh.vertices.Clone(), mesh.vertexWeights.Select(w => new EditableBoneWeight()).ToArray());
                    generated.SetIndices((int[])mesh.indices.Clone());
                    generated.SetEdges((Unity.Mathematics.int2[])mesh.edges.Clone());
                    generated.bones = bones.Select(i => mesh.GetBoneData(i)).ToList();
                    new SpriteMeshDataController { spriteMeshData = generated }.CalculateWeights(new BoundedBiharmonicWeightsGenerator(), null, .01f);
                    foreach (var vertex in targets)
                    {
                        var values = new float[mesh.boneCount];
                        foreach (var channel in generated.vertexWeights[vertex])
                            if (channel.enabled && channel.boneIndex < bones.Length) values[bones[channel.boneIndex]] += channel.weight;
                        SetDistribution(vertex, values);
                    }
                });
                Changed();
            }
        }

        internal float[] Values(int vertex)
        {
            var values = new float[mesh.boneCount];
            foreach (var channel in mesh.vertexWeights[vertex])
                if (channel.enabled && channel.boneIndex >= 0 && channel.boneIndex < values.Length) values[channel.boneIndex] += channel.weight;
            return values;
        }

        // Keep locked channels exact, reserve their slots, then keep the strongest
        // unlocked channels within Unity's four-influence limit.
        void SetDistribution(int vertex, float[] values)
        {
            var locked = lockedBones;
            var old = Values(vertex);
            foreach (var i in locked) values[i] = old[i];
            var fixedIndices = locked.Where(i => old[i] > 0).ToArray();
            var available = Mathf.Max(0, 1 - fixedIndices.Sum(i => old[i]));
            var editable = Enumerable.Range(0, values.Length).Where(i => !locked.Contains(i) && values[i] > 0)
                .OrderByDescending(i => values[i]).Take(Mathf.Max(0, 4 - fixedIndices.Length)).ToArray();
            if (editable.Length == 0 && available > 0)
            {
                editable = Enumerable.Range(0, values.Length).Where(i => !locked.Contains(i))
                    .Take(Mathf.Max(0, 4 - fixedIndices.Length)).ToArray();
                foreach (var i in editable) values[i] = 1;
            }
            var sum = editable.Sum(i => values[i]);
            var weight = mesh.vertexWeights[vertex];
            weight.Clear();
            foreach (var i in fixedIndices) weight.AddChannel(i, old[i], true);
            foreach (var i in editable)
                if (sum > 0 && available > 0) weight.AddChannel(i, values[i] / sum * available, true);
        }

        internal void SetRow(int bone, float value)
        {
            if (!float.IsFinite(value) || lockedBones.Contains(bone)) return;
            using (cache.UndoScope("Set Bone Weight"))
            {
                foreach (var vertex in targets) Adjust(vertex, new[] { bone }, Mathf.Clamp01(value), true);
                Changed();
            }
        }

        internal void Adjust(int vertex, int[] bones, float amount, bool absolute = false)
        {
            var locked = lockedBones;
            var edited = bones.Where(i => i >= 0 && i < mesh.boneCount && !locked.Contains(i)).ToArray();
            if (edited.Length == 0) return;
            var values = Values(vertex);
            var available = Mathf.Max(0, 1 - locked.Sum(i => values[i]));
            foreach (var i in edited) values[i] = Mathf.Clamp(absolute ? amount : values[i] + amount, 0, available);
            var editedSum = edited.Sum(i => values[i]);
            if (editedSum > available)
            {
                foreach (var i in edited) values[i] *= available / editedSum;
                editedSum = available;
            }
            var others = Enumerable.Range(0, mesh.boneCount).Where(i => !edited.Contains(i) && !locked.Contains(i)).ToArray();
            var otherSum = others.Sum(i => values[i]);
            foreach (var i in others) values[i] = otherSum > 0 ? values[i] / otherSum * (available - editedSum) : (available - editedSum) / others.Length;
            SetDistribution(vertex, values);
        }

        internal void Prune(int maxBones, float threshold)
        {
            using (cache.UndoScope("Prune Mesh Weights"))
            {
                foreach (var vertex in targets)
                {
                    var values = Values(vertex);
                    var locked = lockedBones;
                    var keep = Enumerable.Range(0, values.Length).Where(i => !locked.Contains(i) && values[i] >= threshold)
                        .OrderByDescending(i => values[i]).Take(Mathf.Max(0, maxBones - locked.Count(i => values[i] > 0))).ToArray();
                    foreach (var i in Enumerable.Range(0, values.Length))
                        if (!locked.Contains(i) && !keep.Contains(i)) values[i] = 0;
                    if (keep.Length == 0) continue; // Never prune a vertex to zero or invent influences.
                    SetDistribution(vertex, values);
                }
                Changed();
            }
        }

        internal int CountPruned(int maxBones, float threshold)
        {
            var count = 0; var locked = lockedBones;
            foreach (var vertex in targets)
            {
                var values = Values(vertex);
                var keep = Enumerable.Range(0, values.Length).Where(i => !locked.Contains(i) && values[i] >= threshold)
                    .OrderByDescending(i => values[i]).Take(Mathf.Max(0, maxBones - locked.Count(i => values[i] > 0))).ToArray();
                if (keep.Length == 0) continue;
                count += Enumerable.Range(0, values.Length).Count(i => !locked.Contains(i) && !keep.Contains(i) && values[i] > 0);
            }
            return count;
        }

        internal void Save(SceneMeshSource source)
        {
            if (Fingerprint(source) != m_Fingerprint) throw new InvalidOperationException("The source changed while Edit Weights was open. Revert and reopen it.");
            if (!dirty) return;
            cache.EndUndoOperation();
            Undo.RegisterCompleteObjectUndo(source.saveTarget, "Save Mesh Weights");
            m_Context.WithSourcePose(() => SkinningModule.ApplySceneWeightChanges(cache, source.provider, m_DrawOrder.Count > 0 ? DrawIndices() : null));
            if (!cache.hasCharacter || m_DrawOrder.Count > 0)
            {
                var bindings = ReadBindings(source.userData);
                bindings.sprites.RemoveAll(item => item.id == m_Context.spriteId);
                bindings.sprites.Add(new Binding { id = m_Context.spriteId, bones = cache.hasCharacter ? null : mesh.bones.Select(bone => bone.guid).ToArray(), drawOrder = m_DrawOrder.ToArray() });
                var other = source.userData.Split('\n').Where(line => !line.StartsWith(kBindingPrefix)).ToList();
                other.Add(kBindingPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonUtility.ToJson(bindings))));
                source.userData = string.Join("\n", other);
            }
            source.ApplyAndSave();
            m_Saved = Snapshot();
            m_Fingerprint = Fingerprint(new SceneMeshSource(source.ResolveSprite(m_Context.spriteId)));
        }

        static Bindings ReadBindings(string userData)
        {
            var line = userData.Split('\n').FirstOrDefault(item => item.StartsWith(kBindingPrefix));
            if (line == null) return new Bindings();
            try { return JsonUtility.FromJson<Bindings>(Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(kBindingPrefix.Length)))) ?? new Bindings(); }
            catch (Exception) { return new Bindings(); }
        }

        void OnDestroy() { if (m_Context != null) DestroyImmediate(m_Context); }
    }
}
