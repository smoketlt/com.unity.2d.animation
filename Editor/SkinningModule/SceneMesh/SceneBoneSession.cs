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
        }
        [SerializeField] List<ScenePoseOffset> m_ScenePoseOffsets = new List<ScenePoseOffset>();
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
        }

        internal SkeletonCache skeleton => cache.GetEffectiveSkeleton(cache.selectedSprite);
        internal bool dirty => cache != null && m_Saved != Snapshot();

        internal void Load(Sprite sprite, SceneMeshSource source, bool skeletonOnly = false)
        {
            this.skeletonOnly = skeletonOnly;
            scenePoseCaptured = false;
            m_ScenePoseOffsets.Clear();
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
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(renderer.gameObject);
            var host = root != null ? root.transform : renderer.transform.root;
            var poses = skeleton.GetWorldPose();
            var bindings = new Dictionary<string, (Transform transform, float length)>();
            foreach (var skin in host.GetComponentsInChildren<SpriteSkin>(true))
            {
                var sprite = skin.GetComponent<SpriteRenderer>()?.sprite;
                if (sprite == null || AssetDatabase.GetAssetPath(sprite) != assetPath) continue;
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
            for (var i = 0; i < skeleton.boneCount; ++i)
            {
                var bone = skeleton.GetBone(i);
                var offset = new ScenePoseOffset { guid = bone.guid, sourcePose = poses[i] };
                if (bindings.TryGetValue(bone.guid, out var binding))
                {
                    var position = toWorld.inverse.MultiplyPoint3x4(binding.transform.position);
                    var direction = toWorld.inverse.MultiplyVector(binding.transform.TransformVector(Vector3.right));
                    var rotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                    var length = direction.magnitude * binding.length;
                    offset.rotation = rotation * Quaternion.Inverse(poses[i].pose.rotation);
                    offset.scale = poses[i].length > .00001f && length > .00001f ? length / poses[i].length : 1f;
                    offset.matrix = Matrix4x4.TRS(position, offset.rotation, Vector3.one * offset.scale) *
                        Matrix4x4.Translate(-poses[i].pose.position);
                    poses[i] = BonePose.Create(Pose.Create(position, rotation), length);
                }
                offset.scenePose = poses[i];
                m_ScenePoseOffsets.Add(offset);
            }
            skeleton.SetWorldPose(poses);
            for (var i = 0; i < skeleton.boneCount; ++i)
                m_ScenePoseOffsets[i].scenePose = skeleton.GetBone(i).worldPose;
            skeleton.SetDefaultPose();
            if (cache.hasCharacter) cache.SyncSpriteSheetSkeletons();
            m_Saved = Snapshot();
            scenePoseCaptured = true;
        }

        ScenePoseOffset PoseOffset(BoneCache bone)
        {
            var offset = m_ScenePoseOffsets.Find(item => item.guid == bone.guid);
            if (offset != null) return offset;
            var parent = bone.parentBone != null ? PoseOffset(bone.parentBone) : new ScenePoseOffset();
            offset = new ScenePoseOffset { guid = bone.guid, matrix = parent.matrix, rotation = parent.rotation, scale = parent.scale };
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
                var offset = PoseOffset(skeleton.GetBone(i));
                source[i] = displayed[i] == offset.scenePose ? offset.sourcePose : BonePose.Create(Pose.Create(offset.matrix.inverse.MultiplyPoint3x4(displayed[i].pose.position),
                    Quaternion.Inverse(offset.rotation) * displayed[i].pose.rotation), displayed[i].length / offset.scale);
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
                character = source.provider.GetDataProvider<ICharacterDataProvider>()?.GetCharacterData() ?? default
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
            characterBones = cache.hasCharacter ? cache.character.skeleton.bones.ToSpriteBone(Matrix4x4.identity) : null
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

        internal void Save(SceneMeshSource source)
        {
            if (Fingerprint(source) != sourceFingerprint)
                throw new InvalidOperationException("The source asset changed while Edit Bone was open. Revert and reopen it to load the latest data.");
            if (!dirty) return;
            var renames = SceneBoneRenamePlan.Create(source, skeleton);
            Undo.RegisterCompleteObjectUndo(source.importer, "Save Sprite Bones");
            WithSourcePose(() => SkinningModule.ApplySceneBoneChanges(cache, source.provider, initializeMissingWeights: !skeletonOnly));
            source.provider.Apply();
            // Set instance overrides before prefab regeneration so renamed objects retain
            // the exact names used by the rewritten animation paths.
            renames.Apply();
            source.importer.SaveAndReimport();
            m_Saved = Snapshot();
        }

        internal void RestoreAfterUndo() { cache.EndUndoOperation(); RememberBones(); }

        void OnDestroy()
        {
            if (cache != null) Cache.Destroy(cache);
            Undo.ClearUndo(this);
        }
    }
}
