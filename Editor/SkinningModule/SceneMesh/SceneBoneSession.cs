using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    // A private persistent state keeps this host independent of Sprite Editor preferences.
    internal sealed class SceneBoneSession : ScriptableObject, ISkinningCachePersistentState
    {
        [SerializeField] internal SkinningCache cache;
        [SerializeField] internal string assetPath;
        [SerializeField] internal string spriteId;
        [SerializeField] string m_Saved;
        [SerializeField] internal string sourceFingerprint;
        [SerializeField] List<string> m_KnownBones = new List<string>();
        [Serializable] sealed class ScenePoseOffset
        {
            public string guid;
            public Matrix4x4 matrix = Matrix4x4.identity;
            public Quaternion rotation = Quaternion.identity;
            public float scale = 1f;
            public BonePose sourcePose;
            public BonePose scenePose;
            public BonePose authoredPose;
            public BonePose displayedPose;
            public bool hasAuthoredPose;
            public bool inherited;
            public bool hasSceneBinding;
            public float sceneLocalLength;
        }
        [SerializeField] List<ScenePoseOffset> m_ScenePoseOffsets = new List<ScenePoseOffset>();
        Dictionary<string, (Transform transform, float length)> m_SceneBindings;
        [SerializeField] internal bool scenePoseCaptured;
        [SerializeField] internal bool skeletonOnly;

        [field: SerializeField] public string lastSpriteId { get; set; }
        [field: SerializeField] public Tools lastUsedTool { get; set; }
        [field: SerializeField] public Texture2D lastTexture { get; set; }
        [field: SerializeField] public SkinningMode lastMode { get; set; }
        [field: SerializeField] public bool lastVisibilityToolActive { get; set; }
        [field: SerializeField] public int lastVisibilityToolIndex { get; set; }
        [field: SerializeField] public float lastBrushSize { get; set; }
        [field: SerializeField] public float lastBrushHardness { get; set; }
        [field: SerializeField] public float lastBrushStep { get; set; }
        [field: SerializeField] public List<int> lastBoneSelectionIds { get; private set; } = new List<int>();
        [field: SerializeField] public SerializableDictionary<int, BonePose> lastPreviewPose { get; private set; } = new SerializableDictionary<int, BonePose>();
        [field: SerializeField] public SerializableDictionary<int, bool> lastBoneVisibility { get; private set; } = new SerializableDictionary<int, bool>();
        [field: SerializeField] public SerializableDictionary<int, bool> lastBoneExpansion { get; private set; } = new SerializableDictionary<int, bool>();
        [field: SerializeField] public SerializableDictionary<string, bool> lastSpriteVisibility { get; private set; } = new SerializableDictionary<string, bool>();
        [field: SerializeField] public SerializableDictionary<int, bool> lastGroupVisibility { get; private set; } = new SerializableDictionary<int, bool>();
        [field: SerializeField] public IndexedSelection lastVertexSelection { get; private set; } = new IndexedSelection();

        [Serializable] sealed class SpriteRecord
        {
            public string id;
            public SpriteBone[] bones;
            public Vertex2DMetaData[] vertices;
            public int[] indices;
            public Vector2Int[] edges;
            public string[] influences;
        }
        [Serializable] sealed class Record
        {
            public SpriteRect[] rects;
            public SpriteRecord[] sprites;
            public SpriteBone[] characterBones;
            public CharacterData character;
            public BoneIconUtility.Entry[] icons;
            public string bindingData;
        }

        internal SkeletonCache skeleton => cache.GetEffectiveSkeleton(cache.selectedSprite);
        internal bool dirty => cache != null && m_Saved != SourceSnapshot();

        sealed class CopiedBone
        {
            public string name, parentGuid, icon;
            public int parent, chainedChild;
            public Vector3 start, end;
            public Color color;
            public float depth, iconSize;
        }

        static CopiedBone[] s_BoneClipboard;
        internal static bool hasBoneClipboard => s_BoneClipboard != null && s_BoneClipboard.Length > 0;

        internal bool CopySelectedBones()
        {
            var selected = new HashSet<BoneCache>(cache.skeletonSelection.elements.Where(b => b != null && !b.IsConstraintParent()));
            bool Included(BoneCache bone) => bone != null && (selected.Contains(bone) || Included(bone.parentBone));
            var bones = new List<BoneCache>();
            void Visit(BoneCache bone)
            {
                if (bone.IsConstraintParent() || bones.Contains(bone)) return;
                if (bone.parentBone != null && Included(bone.parentBone)) Visit(bone.parentBone);
                bones.Add(bone);
            }
            foreach (var bone in skeleton.bones.Where(Included)) Visit(bone);
            if (bones.Count == 0) return false;
            s_BoneClipboard = bones.Select(bone =>
            {
                var parent = bones.IndexOf(bone.parentBone);
                var root = bone;
                while (bones.Contains(root.parentBone)) root = root.parentBone;
                var anchor = root.parentBone;
                var frame = anchor != null ? Matrix4x4.TRS(anchor.position, anchor.rotation, Vector3.one) : Matrix4x4.identity;
                return new CopiedBone
                {
                    name = bone.name, parent = parent, parentGuid = anchor?.guid,
                    chainedChild = bones.IndexOf(bone.chainedChild),
                    start = frame.inverse.MultiplyPoint3x4(bone.position),
                    end = frame.inverse.MultiplyPoint3x4(bone.endPosition),
                    color = bone.bindPoseColor, depth = bone.depth, icon = bone.iconId, iconSize = bone.iconSize
                };
            }).ToArray();
            return true;
        }

        internal static string MirrorBoneName(string name)
        {
            return System.Text.RegularExpressions.Regex.Replace(name,
                @"Left|Right|left|right|LEFT|RIGHT|(?<![A-Za-z])[LRlr](?![A-Za-z])",
                match =>
                {
                    switch (match.Value)
                    {
                        case "Left": return "Right";
                        case "Right": return "Left";
                        case "left": return "right";
                        case "right": return "left";
                        case "LEFT": return "RIGHT";
                        case "RIGHT": return "LEFT";
                        case "L": return "R";
                        case "R": return "L";
                        case "l": return "r";
                        default: return "l";
                    }
                });
        }

        internal BoneCache[] PasteBones(bool mirror)
        {
            if (!hasBoneClipboard) return Array.Empty<BoneCache>();
            var created = new List<BoneCache>();
            var names = new HashSet<string>(skeleton.bones.Select(bone => bone.name));
            using (cache.UndoScope(mirror ? "Paste Mirror Bones" : "Paste Bones"))
            {
                foreach (var item in s_BoneClipboard)
                {
                    var anchor = skeleton.bones.FirstOrDefault(bone => bone.guid == item.parentGuid && !bone.IsConstraintParent());
                    var frame = anchor != null ? Matrix4x4.TRS(anchor.position, anchor.rotation, Vector3.one) : Matrix4x4.identity;
                    Vector3 Place(Vector3 point)
                    {
                        if (mirror) point.y = -point.y;
                        return frame.MultiplyPoint3x4(point);
                    }
                    var baseName = mirror ? MirrorBoneName(item.name) : item.name;
                    var name = baseName;
                    for (var suffix = 1; !names.Add(name); ++suffix) name = baseName + "_" + suffix;
                    var parent = item.parent >= 0 ? created[item.parent] : anchor;
                    var bone = skeleton.CreateBone(parent, Place(item.start), Place(item.end), false, name);
                    bone.bindPoseColor = item.color;
                    bone.depth = item.depth;
                    bone.iconId = item.icon;
                    bone.iconSize = item.iconSize;
                    created.Add(bone);
                }
                for (var i = 0; i < created.Count; ++i)
                    if (s_BoneClipboard[i].chainedChild >= 0)
                        created[i].chainedChild = created[s_BoneClipboard[i].chainedChild];
                cache.skeletonSelection.elements = created.ToArray();
                Changed(true);
            }
            return created.ToArray();
        }

        internal void Load(Sprite sprite, SceneMeshSource source, bool skeletonOnly = false)
        {
            this.skeletonOnly = skeletonOnly;
            scenePoseCaptured = false;
            m_ScenePoseOffsets.Clear();
            m_SceneBindings = null;
            assetPath = source.assetPath;
            spriteId = source.rect.spriteID.ToString();
            cache = Cache.Create<SkinningCache>();
            cache.Create(source.provider, this);
            cache.selectedSprite = cache.GetSprite(spriteId);
            cache.mode = cache.hasCharacter ? SkinningMode.Character : SkinningMode.SpriteSheet;
            if (cache.bonesReadOnly)
                throw new InvalidOperationException("This asset uses a shared skeleton. Edit its skeleton source instead.");
            if (skeletonOnly && !cache.hasCharacter)
                throw new InvalidOperationException("Skeleton-only editing requires a character source with a shared skeleton.");
            RememberBones();
            Attach();
            m_Saved = Snapshot();
            sourceFingerprint = Fingerprint(source);
        }

        internal void Attach()
        {
            if (cache == null) return;
            cache.AttachPersistentState(this);
            cache.events.skeletonTopologyChanged.RemoveListener(TopologyChanged);
            cache.events.skeletonTopologyChanged.AddListener(TopologyChanged);
            cache.events.skeletonBindPoseChanged.RemoveListener(PoseChanged);
            cache.events.skeletonBindPoseChanged.AddListener(PoseChanged);
        }

        internal bool SelectBone(string guid)
        {
            if (cache == null || string.IsNullOrEmpty(guid)) return false;
            var bone = skeleton.bones.FirstOrDefault(item => item.guid == guid && !item.IsConstraintParent());
            if (bone == null) return false;
            cache.skeletonSelection.elements = new[] { bone };
            return true;
        }

        internal void CaptureScenePose(SpriteRenderer renderer, Rect frame)
        {
            if (scenePoseCaptured) return;
            m_ScenePoseOffsets.Clear();
            var toWorld = SkeletonToWorld(renderer, frame);
            var poses = skeleton.GetWorldPose();
            m_SceneBindings = FindSceneBindings(renderer);
            for (var i = 0; i < skeleton.boneCount; ++i)
            {
                var bone = skeleton.GetBone(i);
                var offset = new ScenePoseOffset { guid = bone.guid, sourcePose = poses[i] };
                UpdateSceneOffset(offset, toWorld);
                poses[i] = offset.scenePose;
                m_ScenePoseOffsets.Add(offset);
            }
            skeleton.SetWorldPose(poses);
            for (var i = 0; i < skeleton.boneCount; ++i)
            {
                var offset = m_ScenePoseOffsets[i];
                offset.scenePose = offset.displayedPose = skeleton.GetBone(i).worldPose;
                offset.authoredPose = offset.sourcePose;
                offset.hasAuthoredPose = true;
            }
            skeleton.SetDefaultPose();
            if (cache.hasCharacter) cache.SyncSpriteSheetSkeletons();
            scenePoseCaptured = true;
            m_Saved = SourceSnapshot();
        }

        internal Dictionary<string, (Transform transform, float length)> FindSceneBindings(SpriteRenderer renderer)
        {
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(renderer.gameObject);
            var host = root != null ? root.transform : renderer.transform.root;
            var bindings = new Dictionary<string, (Transform transform, float length)>();
            foreach (var skin in host.GetComponentsInChildren<SpriteSkin>(true))
            {
                var sprite = skin.GetComponent<SpriteRenderer>()?.sprite;
                if (sprite == null || UniskinRigUtility.SourcePath(sprite) != assetPath) continue;
                var bones = sprite.GetBones();
                var transforms = skin.boneTransforms;
                if (transforms == null) continue;
                for (var i = 0; i < Mathf.Min(bones.Length, transforms.Length); ++i)
                    if (transforms[i] != null && !string.IsNullOrEmpty(bones[i].guid))
                        bindings[bones[i].guid] = (transforms[i], bones[i].length);
            }
            if (cache.hasCharacter)
            {
                var source = new SceneMeshSource(renderer.sprite);
                var characterProvider = source.provider.GetDataProvider<ICharacterDataProvider>();
                var bones = characterProvider != null ? characterProvider.GetCharacterData().bones : null;
                if (bones != null)
                    for (var i = 0; i < bones.Length; ++i)
                    {
                        if (string.IsNullOrEmpty(bones[i].guid) || bindings.ContainsKey(bones[i].guid)) continue;
                        var transform = SceneBoneInspector.FindCharacterTransform(host, bones, i);
                        if (transform != null) bindings[bones[i].guid] = (transform, SceneBoneInspector.CharacterBoneLocalLength(source.provider, bones[i].length));
                    }
            }
            return bindings;
        }

        void UpdateSceneOffset(ScenePoseOffset offset, Matrix4x4 toWorld)
        {
            if (m_SceneBindings.TryGetValue(offset.guid, out var binding) && binding.transform != null)
            {
                if (!offset.hasSceneBinding)
                {
                    offset.sceneLocalLength = binding.length;
                    offset.hasSceneBinding = true;
                }
                var position = toWorld.inverse.MultiplyPoint3x4(binding.transform.position);
                var direction = toWorld.inverse.MultiplyVector(binding.transform.TransformVector(Vector3.right));
                var rotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                var length = direction.magnitude * offset.sceneLocalLength;
                offset.rotation = rotation * Quaternion.Inverse(offset.sourcePose.pose.rotation);
                offset.scale = offset.sourcePose.length > .00001f && length > .00001f ? length / offset.sourcePose.length : 1f;
                offset.matrix = Matrix4x4.TRS(position, offset.rotation, Vector3.one * offset.scale) *
                    Matrix4x4.Translate(-offset.sourcePose.pose.position);
                offset.scenePose = BonePose.Create(Pose.Create(position, rotation), length);
            }
            else if (!offset.hasSceneBinding) offset.scenePose = offset.sourcePose;
        }

        BonePose SourcePose(BoneCache bone)
        {
            var pose = bone.worldPose;
            var offset = PoseOffset(bone);
            if (offset.hasAuthoredPose && pose == offset.displayedPose) return offset.authoredPose;
            if (!offset.inherited && pose == offset.scenePose) return offset.sourcePose;
            return BonePose.Create(Pose.Create(offset.matrix.inverse.MultiplyPoint3x4(pose.pose.position),
                Quaternion.Inverse(offset.rotation) * pose.pose.rotation), pose.length / offset.scale);
        }

        // Refresh only the private cache. Source poses and their exact projected pairs
        // survive animation, staged edits and Undo without accumulating round-trip drift.
        internal bool RefreshScenePose(SpriteRenderer renderer, Rect frame)
        {
            if (!scenePoseCaptured) { CaptureScenePose(renderer, frame); return true; }
            if (m_SceneBindings == null || m_SceneBindings.Values.Any(binding => binding.transform == null))
                m_SceneBindings = FindSceneBindings(renderer);
            var bones = skeleton.bones;
            var source = bones.Select(SourcePose).ToArray();
            var before = skeleton.GetWorldPose();
            var toWorld = SkeletonToWorld(renderer, frame);
            foreach (var offset in m_ScenePoseOffsets)
                if (!offset.inherited) UpdateSceneOffset(offset, toWorld);
            var refreshed = new HashSet<string>();
            ScenePoseOffset RefreshInherited(BoneCache bone)
            {
                var offset = PoseOffset(bone);
                if (!offset.inherited || !refreshed.Add(bone.guid)) return offset;
                var parent = bone.parentBone != null ? RefreshInherited(bone.parentBone) : new ScenePoseOffset();
                offset.matrix = parent.matrix;
                offset.rotation = parent.rotation;
                offset.scale = parent.scale;
                return offset;
            }
            var poses = new BonePose[bones.Length];
            for (var i = 0; i < bones.Length; ++i)
            {
                var offset = RefreshInherited(bones[i]);
                offset.authoredPose = source[i];
                offset.hasAuthoredPose = true;
                poses[i] = !offset.inherited && source[i] == offset.sourcePose ? offset.scenePose :
                    BonePose.Create(Pose.Create(offset.matrix.MultiplyPoint3x4(source[i].pose.position),
                        offset.rotation * source[i].pose.rotation), source[i].length * offset.scale);
            }
            skeleton.SetWorldPose(poses);
            for (var i = 0; i < bones.Length; ++i) PoseOffset(bones[i]).displayedPose = bones[i].worldPose;
            skeleton.SetDefaultPose();
            if (cache.hasCharacter) cache.SyncSpriteSheetSkeletons();
            return !before.SequenceEqual(skeleton.GetWorldPose());
        }

        ScenePoseOffset PoseOffset(BoneCache bone)
        {
            var offset = m_ScenePoseOffsets.Find(item => item.guid == bone.guid);
            if (offset != null) return offset;
            var parent = bone.parentBone != null ? PoseOffset(bone.parentBone) : new ScenePoseOffset();
            offset = new ScenePoseOffset { guid = bone.guid, matrix = parent.matrix, rotation = parent.rotation, scale = parent.scale, inherited = true };
            m_ScenePoseOffsets.Add(offset);
            return offset;
        }

        internal Matrix4x4 CapturedDeformation(BoneCache bone) => PoseOffset(bone).matrix;

        internal void WithSourcePose(Action action)
        {
            var displayed = skeleton.GetWorldPose();
            var displayedLocal = skeleton.GetLocalPose();
            var source = new BonePose[displayed.Length];
            for (var i = 0; i < source.Length; ++i)
            {
                source[i] = SourcePose(skeleton.GetBone(i));
            }
            try
            {
                skeleton.SetWorldPose(source);
                skeleton.SetDefaultPose();
                if (cache.hasCharacter) cache.SyncSpriteSheetSkeletons();
                action();
            }
            finally
            {
                skeleton.SetLocalPose(displayedLocal);
                skeleton.SetDefaultPose();
                if (cache.hasCharacter) cache.SyncSpriteSheetSkeletons();
            }
        }

        string SourceSnapshot()
        {
            if (!scenePoseCaptured) return Snapshot();
            string snapshot = null;
            WithSourcePose(() => snapshot = Snapshot());
            return snapshot;
        }

        internal static string Fingerprint(SceneMeshSource source)
        {
            var bones = source.provider.GetDataProvider<ISpriteBoneDataProvider>();
            return JsonUtility.ToJson(new Record
            {
                rects = source.provider.GetSpriteRects(),
                sprites = source.provider.GetSpriteRects().Select(rect => new SpriteRecord
                {
                    id = rect.spriteID.ToString(), bones = bones.GetBones(rect.spriteID).ToArray(),
                    vertices = source.meshProvider.GetVertices(rect.spriteID),
                    indices = source.meshProvider.GetIndices(rect.spriteID), edges = source.meshProvider.GetEdges(rect.spriteID)
                }).ToArray(),
                character = source.provider.GetDataProvider<ICharacterDataProvider>()?.GetCharacterData() ?? default,
                icons = BoneIconUtility.Read(source.userData), bindingData = source.userData
            });
        }

        string Snapshot() => JsonUtility.ToJson(new Record
        {
            sprites = (cache.hasCharacter ? cache.GetSprites() : new[] { cache.selectedSprite })
                .OrderBy(sprite => sprite.id).Select(sprite => new SpriteRecord
                {
                    id = sprite.id,
                    bones = sprite.GetSkeleton().bones.ToSpriteBone(sprite.localToWorldMatrix),
                    // Geometry is immutable in this host. Avoid serializing every
                    // layer's vertex payload during the popup's property refresh.
                    influences = sprite.GetMesh().bones.Select(bone => bone.guid).ToArray()
                }).ToArray(),
            characterBones = cache.hasCharacter ? cache.character.skeleton.bones.ToSpriteBone(Matrix4x4.identity) : null,
            icons = BoneIconUtility.Capture(skeleton.bones)
        });

        void RememberBones() => m_KnownBones = skeleton.bones.Select(bone => bone.guid).ToList();

        void TopologyChanged(SkeletonCache changed)
        {
            if (changed != skeleton) return;
            foreach (var bone in skeleton.bones) PoseOffset(bone);
            if (cache.hasCharacter)
            {
                var added = skeleton.bones.Where(bone => !m_KnownBones.Contains(bone.guid)).ToArray();
                foreach (var part in cache.character.parts)
                {
                    if (skeletonOnly) continue;
                    var additions = added.Where(bone => part == cache.selectedSprite.GetCharacterPart() ||
                        (bone.parentBone != null && part.bones.Contains(bone.parentBone))).ToArray();
                    if (additions.Length > 0)
                    {
                        part.bones = part.bones.Concat(additions).Distinct().ToArray();
                        part.sprite.UpdateMesh(part.bones);
                    }
                }
                cache.CreateSpriteSheetSkeletons();
            }
            else cache.selectedSprite.UpdateMesh(skeleton.bones);
            RememberBones();
            foreach (var sprite in cache.GetSprites()) cache.events.meshChanged.Invoke(sprite.GetMesh());
        }

        void PoseChanged(SkeletonCache changed)
        {
            if (cache.hasCharacter && changed == skeleton) cache.SyncSpriteSheetSkeletons();
        }

        internal void Changed(bool topology = false)
        {
            skeleton.SetDefaultPose();
            if (topology) cache.events.skeletonTopologyChanged.Invoke(skeleton);
            cache.events.skeletonBindPoseChanged.Invoke(skeleton);
        }

        internal void RecreateBone(BoneCache bone, Vector3 start, Vector3 end)
        {
            if (bone == null || bone.IsConstraintParent() || !skeleton.Contains(bone)) return;
            if (!float.IsFinite(start.x) || !float.IsFinite(start.y) || !float.IsFinite(start.z) ||
                !float.IsFinite(end.x) || !float.IsFinite(end.y) || !float.IsFinite(end.z)) return;
            // Redraw the existing identity so every mesh retains its influence slot
            // and exact weights. Children keep their world poses and hierarchy.
            var direction = end - start;
            if (direction.sqrMagnitude < .0001f) direction = Vector3.right * .01f;
            using (cache.UndoScope("Recreate Bone"))
            {
                var children = bone.GetChildrenWoldPose();
                bone.worldPose = BonePose.Create(Pose.Create(start,
                    Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg)), direction.magnitude);
                bone.SetChildrenWorldPose(children);
                if (bone.parentBone != null && bone.parentBone.chainedChild == bone &&
                    (bone.parentBone.endPosition - start).sqrMagnitude >= .001f)
                    bone.parentBone.chainedChild = null;
                if (bone.chainedChild != null && (bone.endPosition - bone.chainedChild.position).sqrMagnitude >= .001f)
                    bone.chainedChild = null;
                Changed();
                foreach (var sprite in cache.GetSprites()) cache.events.meshChanged.Invoke(sprite.GetMesh());
            }
        }

        internal void RecreateBoneAndSave(BoneCache bone, Vector3 start, Vector3 end,
            SpriteRenderer renderer, SceneMeshSource source)
        {
            cache.EndUndoOperation();
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            try
            {
                // Moving the joint also changes its children's local coordinates.
                var positions = SceneBoneAnimationPositionPlan.Create(source,
                    new[] { bone }.Concat(bone.children.OfType<BoneCache>()));
                RecreateBone(bone, start, end);
                Save(source);
                positions.Apply();
                // Saving refreshes runtime bind poses and scene bone Transforms.
                // Keep the same cache objects for Undo, but capture the new source
                // baseline so live animation no longer rotates about the old joint
                // or projects the authored movement a second time.
                // Save advances Unity's Undo group. Record this later baseline in
                // that group too, before collapsing the whole gesture into one step.
                using (cache.UndoScope("Refresh Recreated Bone Pose"))
                {
                    var savedPose = skeleton.bones.Select(SourcePose).ToArray();
                    skeleton.SetWorldPose(savedPose);
                    skeleton.SetDefaultPose();
                    if (cache.hasCharacter) cache.SyncSpriteSheetSkeletons();
                    scenePoseCaptured = false;
                    m_SceneBindings = null;
                    var refreshed = new SceneMeshSource(renderer.sprite);
                    CaptureScenePose(renderer, refreshed.rect.rect);
                    sourceFingerprint = Fingerprint(refreshed);
                }
                Undo.FlushUndoRecordObjects();
                Undo.SetCurrentGroupName("Recreate Bone");
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                RestoreAfterUndo();
                throw;
            }
        }

        internal Dictionary<string, Quaternion> SourceRotations() => skeleton.bones.ToDictionary(bone => bone.guid, bone => SourcePose(bone).pose.rotation);

        internal Dictionary<string, Matrix4x4> SourceFrames() => skeleton.bones.ToDictionary(bone => bone.guid,
            bone => Matrix4x4.TRS(SourcePose(bone).pose.position, SourcePose(bone).pose.rotation, Vector3.one));

        internal void AdjustRotation(BoneCache bone, Quaternion rotation) => AdjustPose(bone, bone.position, rotation);

        internal void AdjustPose(BoneCache bone, Vector3 position, Quaternion rotation)
        {
            if (bone == null || bone.IsConstraintParent() || !skeleton.Contains(bone)) return;
            using (cache.UndoScope("Adjust Bone Axes"))
            {
                var children = bone.GetChildrenWoldPose();
                bone.position = position;
                bone.rotation = rotation;
                bone.SetChildrenWorldPose(children);
                Changed();
            }
        }

        internal void SaveAdjusted(SceneMeshSource source, SpriteRenderer renderer, Dictionary<string, Quaternion> baseline, int adjustmentUndoGroup = -1)
            => SaveAdjusted(source, renderer, baseline.ToDictionary(pair => pair.Key, pair =>
                Matrix4x4.TRS(SourceFrames()[pair.Key].GetColumn(3), pair.Value, Vector3.one)), adjustmentUndoGroup);

        internal void SaveAdjusted(SceneMeshSource source, SpriteRenderer renderer, Dictionary<string, Matrix4x4> baseline, int adjustmentUndoGroup = -1)
        {
            if (AnimationMode.InAnimationMode()) throw new InvalidOperationException("Stop animation preview before applying Adjust.");
            var plan = CreateAdjustmentPlan(source, baseline);
            if (plan == null) return;
            cache.EndUndoOperation();
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            try
            {
                Save(source, plan);
                plan.ApplyScenePoses();
                using (cache.UndoScope("Refresh Adjusted Bone Pose"))
                {
                    var pose = skeleton.bones.Select(SourcePose).ToArray();
                    skeleton.SetWorldPose(pose);
                    skeleton.SetDefaultPose();
                    if (cache.hasCharacter) cache.SyncSpriteSheetSkeletons();
                    scenePoseCaptured = false;
                    m_SceneBindings = null;
                    var refreshed = new SceneMeshSource(renderer.sprite);
                    CaptureScenePose(renderer, refreshed.rect.rect);
                    sourceFingerprint = Fingerprint(refreshed);
                }
                if (source.rig == null) SceneSkinningSuspension.TrackNativeSource(source.importer);
                Undo.FlushUndoRecordObjects();
                Undo.SetCurrentGroupName("Adjust Bone Axes");
                Undo.CollapseUndoOperations(adjustmentUndoGroup >= 0 ? adjustmentUndoGroup : group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                RestoreAfterUndo();
                throw;
            }
        }

        internal SceneBoneAdjustPlan CreateAdjustmentPlan(SceneMeshSource source, Dictionary<string, Matrix4x4> baseline)
        {
            var deltas = SourceFrames().Where(pair => baseline.ContainsKey(pair.Key) && pair.Value != baseline[pair.Key])
                .ToDictionary(pair => pair.Key, pair =>
                {
                    var delta = baseline[pair.Key].inverse * pair.Value;
                    delta.m03 /= source.provider.pixelsPerUnit;
                    delta.m13 /= source.provider.pixelsPerUnit;
                    delta.m23 /= source.provider.pixelsPerUnit;
                    return delta;
                });
            return deltas.Count == 0 ? null : SceneBoneAdjustPlan.Create(source, deltas);
        }

        internal static bool CanSetParent(BoneCache child, BoneCache parent)
        {
            if (child == null || child.IsConstraintParent() || (parent != null && parent.skeleton != child.skeleton)) return false;
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.parentBone)
                if (ancestor == child || ancestor.IsConstraintParent()) return false;
            return true;
        }

        internal void SetParent(BoneCache child, BoneCache parent)
        {
            if (!CanSetParent(child, parent) || child.parentBone == parent) return;
            using (cache.UndoScope("Set Bone Parent"))
            {
                child.SetParent(parent != null ? (TransformCache)parent : skeleton, true);
                Changed(true);
            }
        }

        internal void RenameSelectedBone(string value)
        {
            var selected = cache.skeletonSelection.elements.Where(bone => bone != null && !bone.IsConstraintParent()).ToArray();
            if (selected.Length != 1) return;
            var name = value?.Trim();
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(new[] { '/', '\\', '\n', '\r' }) >= 0)
                throw new InvalidOperationException("Enter a non-empty bone name without slashes or line breaks.");
            var bone = selected[0];
            if (bone.name == name) return;
            // Source names must also remain unique for importer-generated hierarchy lookup.
            if (skeleton.bones.Any(other => other != bone && other.name == name))
                throw new InvalidOperationException("Another bone already uses this name.");
            using (cache.UndoScope("Rename Bone"))
            {
                bone.name = name;
                Changed();
            }
        }

        internal Matrix4x4 SkeletonToWorld(SpriteRenderer renderer, Rect frame)
        {
            var localToWorld = cache.hasCharacter ? cache.selectedSprite.GetCharacterPart().localToWorldMatrix : cache.selectedSprite.localToWorldMatrix;
            var pixelsToWorld = SceneMeshSession.PixelToWorld(renderer, frame);
            if (cache.hasCharacter)
            {
                var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(renderer.gameObject);
                var sourceRenderer = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
                var sourceRoot = instanceRoot != null ? PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot) : null;
                if (sourceRenderer != null && sourceRoot != null &&
                    AssetDatabase.GetAssetPath(sourceRenderer) == assetPath &&
                    sourceRenderer.transform.IsChildOf(sourceRoot.transform))
                {
                    // Character bones belong to the prefab root, not a layer's
                    // scene placement override. Keep the imported layer placement.
                    var importedToWorld = instanceRoot.transform.localToWorldMatrix *
                        sourceRoot.transform.worldToLocalMatrix * sourceRenderer.transform.localToWorldMatrix;
                    pixelsToWorld = importedToWorld * renderer.transform.worldToLocalMatrix * pixelsToWorld;
                }
            }
            return pixelsToWorld * localToWorld.inverse;
        }

        internal void Save(SceneMeshSource source, SceneBoneAdjustPlan adjustment = null)
        {
            if (Fingerprint(source) != sourceFingerprint)
                throw new InvalidOperationException("The source asset changed while Edit Bones was open. Revert and reopen it to load the latest data.");
            if (!dirty) return;
            var renames = SceneBoneRenamePlan.Create(source, skeleton);
            Undo.RegisterCompleteObjectUndo(source.saveTarget, "Save Sprite Bones");
            adjustment?.WriteBindings(source);
            WithSourcePose(() => SkinningModule.ApplySceneBoneChanges(cache, source.provider, initializeMissingWeights: !skeletonOnly));
            source.userData = BoneIconUtility.Write(source.userData, BoneIconUtility.Bones(cache));
            if (source.provider is UniskinRigDataProvider rigProvider)
            {
                UniskinRigUtility.CheckSource(rigProvider.rig);
                UniskinRigUtility.Validate(rigProvider.data);
                Undo.IncrementCurrentGroup();
                var undoGroup = Undo.GetCurrentGroup();
                try
                {
                    renames.Apply();
                    source.ApplyAndSave();
                    Undo.CollapseUndoOperations(undoGroup);
                }
                catch
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                    throw;
                }
                m_Saved = SourceSnapshot();
                sourceFingerprint = Fingerprint(new SceneMeshSource(source.ResolveSprite(spriteId)));
                BoneGizmo.instance.ClearSpriteBoneCache();
                return;
            }
            // Set instance overrides before prefab regeneration so renamed objects retain
            // the exact names used by the rewritten animation paths.
            renames.Apply();
            source.ApplyAndSave();
            m_Saved = SourceSnapshot();
            sourceFingerprint = Fingerprint(new SceneMeshSource(source.ResolveSprite(spriteId)));
            BoneGizmo.instance.ClearSpriteBoneCache();
        }

        internal void RestoreAfterUndo() { cache.EndUndoOperation(); RememberBones(); }

        void OnDestroy()
        {
            if (cache != null) Cache.Destroy(cache);
            Undo.ClearUndo(this);
        }
    }
}
