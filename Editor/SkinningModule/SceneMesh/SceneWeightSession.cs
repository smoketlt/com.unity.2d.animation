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
        [SerializeField] SceneBoneSession m_WeldContext;
        [SerializeField] Matrix4x4 m_WeldTargetMatrix;
        [SerializeField] string m_Saved;
        [SerializeField] string m_Fingerprint;
        [SerializeField] List<string> m_Locked = new List<string>();
        [SerializeField] List<string> m_DrawOrder = new List<string>();
        [SerializeField] DonorHistory[] m_DonorHistory;
        [SerializeField] SceneMeshBindingPose m_BindingPose;
        [SerializeField] bool m_HasBindingPose;
        int[] m_DrawIndices;
        [SerializeField] int[] m_VertexOrder = Array.Empty<int>();
        static KeyValuePair<string, float>[][] s_WeightClipboard;

        internal int[] orderedVertices => m_VertexOrder.Where(vertices.Contains)
            .Concat(vertices.elements.Where(i => !m_VertexOrder.Contains(i)))
            .Where(i => i >= 0 && i < mesh.vertexCount).ToArray();

        internal void SelectVertices(IEnumerable<int> selection)
        {
            m_VertexOrder = selection.Distinct().ToArray();
            vertices.elements = m_VertexOrder;
        }

        internal bool canPasteWeights => s_WeightClipboard != null && orderedVertices.Length > 0 &&
            (s_WeightClipboard.Length == 1 || s_WeightClipboard.Length == orderedVertices.Length);

        internal void CopyWeights()
        {
            var selected = orderedVertices;
            if (selected.Length == 0) return;
            s_WeightClipboard = selected.Select(vertex => mesh.vertexWeights[vertex]
                .Where(channel => channel.enabled && channel.weight > 0)
                .Select(channel => new KeyValuePair<string, float>(mesh.bones[channel.boneIndex].guid, channel.weight)).ToArray()).ToArray();
        }

        internal void PasteWeights()
        {
            if (!canPasteWeights) return;
            var selected = orderedVertices;
            var bones = mesh.bones.Select(bone => bone.guid).ToArray();
            var locked = lockedBones;
            var changes = new List<(int vertex, EditableBoneWeight weight)>();
            for (var i = 0; i < selected.Length; ++i)
            {
                var source = s_WeightClipboard[s_WeightClipboard.Length == 1 ? 0 : i];
                if (source.Any(channel => Array.IndexOf(bones, channel.Key) < 0)) continue;
                var weight = new EditableBoneWeight();
                var values = new float[bones.Length];
                foreach (var channel in source)
                {
                    var bone = Array.IndexOf(bones, channel.Key);
                    weight.AddChannel(bone, channel.Value, true);
                    values[bone] += channel.Value;
                }
                var old = Values(selected[i]);
                if (locked.Any(bone => old[bone] != values[bone])) continue;
                if (old.SequenceEqual(values)) continue;
                changes.Add((selected[i], weight));
            }
            if (changes.Count == 0) return;
            using (cache.UndoScope("Paste Vertex Weights"))
            {
                ForgetDonors();
                foreach (var change in changes) mesh.vertexWeights[change.vertex] = change.weight;
                Changed();
            }
        }
        const string kBindingPrefix = "Uniskin.WeightBindings.v1:";
        [Serializable] sealed class Bindings { public List<Binding> sprites = new List<Binding>(); }
        [Serializable] sealed class Binding { public string id; public string[] bones; public string[] drawOrder; }
        [Serializable] sealed class State { public string[] bones; public EditableBoneWeight[] weights; public string[] drawOrder; public Vector2[] positions; public SceneMeshBindingPose bindingPose; }
        [Serializable] sealed class DonorHistory { public int[] edited; public float[] values; }

        internal SceneBoneSession context => m_Context;
        internal SkinningCache cache => m_Context.cache;
        internal MeshCache mesh => cache.selectedSprite.GetMesh();
        internal IndexedSelection vertices => cache.vertexSelection;
        internal SceneMeshBindingPose bindingPose => m_HasBindingPose ? m_BindingPose : null;
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
            m_DonorHistory = null;
            m_Context = CreateInstance<SceneBoneSession>();
            m_Context.hideFlags = HideFlags.HideAndDontSave;
            m_Context.Load(sprite, source);
            if (m_WeldContext != null) DestroyImmediate(m_WeldContext);
            m_WeldContext = null;
            if (source.clipping != null)
            {
                var contour = source.clipping;
                var rigSource = new SceneMeshSource(contour.source);
                m_WeldContext = CreateInstance<SceneBoneSession>();
                m_WeldContext.hideFlags = HideFlags.HideAndDontSave;
                m_WeldContext.Load(contour.source, rigSource);
                // Contour pixels -> contour plane -> rig plane -> character pixels.
                m_WeldTargetMatrix = Matrix4x4.Scale(Vector3.one * contour.rig.pixelsPerUnit) *
                    Matrix4x4.Translate(-contour.rig.rootOffset) * contour.rigToContour.inverse *
                    Matrix4x4.Scale(Vector3.one / source.provider.pixelsPerUnit) *
                    Matrix4x4.Translate(-Vector2.Scale(source.rect.rect.size, source.rect.pivot));
            }
            m_BindingPose = SceneMeshBindingPose.Read(source.userData, m_Context.spriteId);
            m_HasBindingPose = m_BindingPose != null;
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
            { bones = mesh.bones.Select(bone => bone.guid).ToArray(), weights = mesh.vertexWeights, drawOrder = m_DrawOrder.ToArray(), positions = mesh.vertices, bindingPose = bindingPose });
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

        internal void UpdateBindings(Vector2[] positions, Matrix4x4[] pixelsToBone)
        {
            mesh.ClearVertexPositionOverride();
            if (positions == null || positions.Length != mesh.vertexCount || pixelsToBone == null || pixelsToBone.Length != mesh.boneCount)
                throw new InvalidOperationException("Every bound bone must have an available scene Transform.");
            var pose = new SceneMeshBindingPose
            {
                id = context.spriteId,
                bones = mesh.bones.Select((bone, i) => new SceneMeshBindingPose.Bone { guid = bone.guid, pixelsToBone = pixelsToBone[i] }).ToArray(),
                positions = (Vector2[])positions.Clone(),
                texturePositions = mesh.vertices.Select((position, i) => bindingPose?.TexturePosition(position, i) ?? position).ToArray(),
                indices = (int[])mesh.indices.Clone()
            };
            pose.Validate();
            using (cache.UndoScope("Update Mesh Bindings"))
            {
                Undo.RegisterCompleteObjectUndo(this, "Update Mesh Bindings");
                m_BindingPose = pose;
                m_HasBindingPose = true;
                mesh.SetVertices((Vector2[])positions.Clone(), mesh.vertexWeights);
                Changed();
            }
        }

        internal void BeginWeightUndo(string name)
        {
            if (!cache.isUndoOperationSet) Undo.RegisterCompleteObjectUndo(this, name);
            cache.BeginUndoOperation(name);
        }

        void ForgetDonors()
        {
            if (m_DonorHistory == null) return;
            Undo.RegisterCompleteObjectUndo(this, "Edit Mesh Weights");
            m_DonorHistory = null;
        }

        internal void Bind(BoneCache bone)
        {
            if (bone == null || bone.IsConstraintParent() || mesh.ContainsBone(bone)) return;
            var firstBinding = mesh.boneCount == 0;
            using (cache.UndoScope("Bind Mesh Bone"))
            {
                SetBones(mesh.bones.Concat(new[] { bone }).ToArray());
                // Initialize only the first binding. An additive Bind must preserve
                // intentional zero weights as well as positive authored channels.
                if (firstBinding)
                    m_Context.WithSourcePose(() => cache.selectedSprite.CalculateMissingWeights());
                Changed();
            }
        }

        void SetBones(BoneCache[] bones)
        {
            ForgetDonors();
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
                ForgetDonors();
                foreach (var vertex in targets)
                    foreach (var channel in mesh.vertexWeights[vertex])
                        if (channel.boneIndex == bones[0]) channel.boneIndex = bones[1];
                        else if (channel.boneIndex == bones[1]) channel.boneIndex = bones[0];
                Changed();
            }
        }

        internal SpriteCache[] weldSources
        {
            get
            {
                var donorCache = m_WeldContext != null ? m_WeldContext.cache : cache;
                return !donorCache.hasCharacter ? Array.Empty<SpriteCache>() : donorCache.character.parts
                    .Select(part => part.sprite).Where(sprite => sprite != cache.selectedSprite && CanWeld(sprite)).ToArray();
            }
        }

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
            var targetMatrix = m_WeldContext != null ? m_WeldTargetMatrix : cache.selectedSprite.GetCharacterPart().localToWorldMatrix;
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
                ForgetDonors();
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
                ForgetDonors();
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
            // Resolve and solve before touching weights or donor history. A missing
            // reference must not leave a partially changed session.
            SpriteMeshData generated = null;
            m_Context.WithSourcePose(() =>
            {
                generated = new SpriteMeshData();
                generated.SetFrame(mesh.frame);
                generated.SetVertices((Vector2[])mesh.vertices.Clone(), mesh.vertexWeights.Select(w => new EditableBoneWeight()).ToArray());
                generated.SetIndices((int[])mesh.indices.Clone());
                generated.SetEdges((Unity.Mathematics.int2[])mesh.edges.Clone());
                generated.bones = bones.Select(GetAutoBoneData).ToList();
                new SpriteMeshDataController { spriteMeshData = generated }.CalculateWeights(new RefinedBiharmonicWeightsGenerator(), null, .01f);
            });
            using (cache.UndoScope("Auto Mesh Weights"))
            {
                Undo.RegisterCompleteObjectUndo(this, "Auto Mesh Weights");
                ForgetDonors();
                foreach (var vertex in targets)
                {
                    var values = new float[mesh.boneCount];
                    foreach (var channel in generated.vertexWeights[vertex])
                        if (channel.enabled && channel.boneIndex < bones.Length) values[bones[channel.boneIndex]] += channel.weight;
                    SetDistribution(vertex, values);
                }
                Changed();
            }
        }

        internal SpriteBoneData GetAutoBoneData(int index)
        {
            var data = mesh.GetBoneData(index);
            if (bindingPose == null) return data;
            var bone = mesh.bones[index];
            var binding = bindingPose.FindBone(bone.guid);
            if (!binding.HasValue || !SceneMeshBindingPose.IsFinite(binding.Value) || Mathf.Abs(binding.Value.determinant) < 1e-12f)
                throw new InvalidOperationException("Auto requires a valid mesh binding for " + bone.name + ". Use Update Bindings before Auto.");
            // pixelsToBone includes the provider PPU conversion: its inverse maps
            // bone-local pixel coordinates into this mesh's saved reference pose.
            // Never use the animated scene pose or mix source bones with rebound vertices.
            var boneToPixels = binding.Value.inverse;
            data.position = boneToPixels.MultiplyPoint3x4(Vector3.zero);
            data.endPosition = boneToPixels.MultiplyPoint3x4(new Vector3(data.length, 0, 0));
            return data;
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
                Undo.RegisterCompleteObjectUndo(this, "Set Bone Weight");
                foreach (var vertex in targets) Adjust(vertex, new[] { bone }, Mathf.Clamp01(value), true);
                Changed();
            }
        }

        internal void OffsetSelectedWeights(int[] bones, float amount)
        {
            if (!float.IsFinite(amount) || amount == 0 || vertices.Count == 0 || bones == null) return;
            var locked = lockedBones;
            var edited = bones.Where(i => i >= 0 && i < mesh.boneCount && !locked.Contains(i)).Distinct().ToArray();
            var selected = targets;
            if (edited.Length == 0 || selected.Length == 0) return;
            using (cache.UndoScope("Offset Selected Bone Weights"))
            {
                Undo.RegisterCompleteObjectUndo(this, "Offset Selected Bone Weights");
                foreach (var vertex in selected) Adjust(vertex, edited, amount);
                Changed();
            }
        }

        internal void Adjust(int vertex, int[] bones, float amount, bool absolute = false)
        {
            if (!float.IsFinite(amount) || (!absolute && amount == 0)) return;
            var locked = lockedBones;
            var edited = bones.Where(i => i >= 0 && i < mesh.boneCount && !locked.Contains(i)).Distinct().OrderBy(i => i).ToArray();
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
            // Reserve slots for the explicitly edited bones before choosing donors.
            // Otherwise each small drag/stamp on a zero-weight fifth bone is dropped
            // by SetDistribution and can never accumulate into a positive influence.
            var slots = Mathf.Max(0, 4 - locked.Count(i => values[i] > 0));
            var retained = edited.Where(i => values[i] > 0).OrderByDescending(i => values[i]).Take(slots).ToArray();
            foreach (var i in edited.Except(retained)) values[i] = 0;
            editedSum = retained.Sum(i => values[i]);
            var candidates = Enumerable.Range(0, mesh.boneCount).Where(i => !edited.Contains(i) && !locked.Contains(i)).ToArray();
            if (m_DonorHistory == null || m_DonorHistory.Length != mesh.vertexCount)
                m_DonorHistory = new DonorHistory[mesh.vertexCount];
            var history = m_DonorHistory[vertex];
            // At 100% the donor channels vanish. Retain their last proportions so
            // reversing this edit restores those bones instead of arbitrary list rows.
            if (!candidates.Any(i => values[i] > 0) && history?.edited != null && history.values != null &&
                history.edited.SequenceEqual(edited) && history.values.Length == values.Length)
                foreach (var i in candidates) values[i] = history.values[i];
            var others = candidates.OrderByDescending(i => values[i]).Take(slots - retained.Length).ToArray();
            foreach (var i in candidates.Except(others)) values[i] = 0;
            var otherSum = others.Sum(i => values[i]);
            m_DonorHistory[vertex] = otherSum > 0
                ? new DonorHistory { edited = edited, values = (float[])values.Clone() } : null;
            foreach (var i in others) values[i] = otherSum > 0 ? values[i] / otherSum * (available - editedSum) : (available - editedSum) / others.Length;
            SetDistribution(vertex, values);
        }

        internal void Prune(int maxBones, float threshold)
        {
            using (cache.UndoScope("Prune Mesh Weights"))
            {
                ForgetDonors();
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
            m_Context.WithSourcePose(() => SkinningModule.ApplySceneWeightChanges(cache, source.provider, m_DrawOrder.Count > 0 ? DrawIndices() : null, bindingPose != null));
            if (!cache.hasCharacter || m_DrawOrder.Count > 0)
            {
                var bindings = ReadBindings(source.userData);
                bindings.sprites.RemoveAll(item => item.id == m_Context.spriteId);
                bindings.sprites.Add(new Binding { id = m_Context.spriteId, bones = cache.hasCharacter ? null : mesh.bones.Select(bone => bone.guid).ToArray(), drawOrder = m_DrawOrder.ToArray() });
                var other = source.userData.Split('\n').Where(line => !line.StartsWith(kBindingPrefix)).ToList();
                other.Add(kBindingPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonUtility.ToJson(bindings))));
                source.userData = string.Join("\n", other);
            }
            if (bindingPose != null) source.userData = SceneMeshBindingPose.Write(source.userData, bindingPose);
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

        void OnDestroy()
        {
            Undo.ClearUndo(this);
            if (m_WeldContext != null) DestroyImmediate(m_WeldContext);
            if (m_Context != null) DestroyImmediate(m_Context);
        }
    }
}
