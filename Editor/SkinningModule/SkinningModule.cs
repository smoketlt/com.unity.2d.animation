using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    [RequireSpriteDataProvider(typeof(ISpriteMeshDataProvider), typeof(ISpriteBoneDataProvider))]
    internal partial class SkinningModule : SpriteEditorModuleBase
    {
        private static class Styles
        {
            public static string moduleName = L10n.Tr("Skinning Editor");
        }

        private SkinningCache m_SkinningCache;
        private int m_PrevNearestControl = -1;
        private SpriteOutlineRenderer m_SpriteOutlineRenderer;
        private MeshPreviewTool m_MeshPreviewTool;
        private SkinningMode m_PreviousSkinningMode;
        private SpriteBoneInfluenceTool m_CharacterSpriteTool;
        private HorizontalToggleTools m_HorizontalToggleTools;
        private AnimationAnalytics m_Analytics;
        private ModuleToolGroup m_ModuleToolGroup;
        private AnimationPreviewPanel m_AnimationPreviewPanel;
        private AnimationPreviewController m_AnimationPreviewController;
        IMeshPreviewBehaviour m_MeshPreviewBehaviourOverride = null;
        bool m_CollapseToolbar;
        bool m_HasUnsavedChanges = false;
        Texture2D m_WorkspaceBackgroundTexture;

        internal SkinningCache skinningCache
        {
            get { return m_SkinningCache; }
        }

        private BaseTool currentTool
        {
            get { return skinningCache.selectedTool; }
            set { skinningCache.selectedTool = value; }
        }

        private BaseTool previousTool { get; set; }

        public override string moduleName
        {
            get { return Styles.moduleName; }
        }

        public override void OnModuleActivate()
        {
            m_SkinningCache = Cache.Create<SkinningCache>();
            m_WorkspaceBackgroundTexture = new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true);

            m_WorkspaceBackgroundTexture.hideFlags = HideFlags.HideAndDontSave;
            m_WorkspaceBackgroundTexture.SetPixel(1, 1, new Color(0, 0, 0, 0));
            m_WorkspaceBackgroundTexture.Apply();

            AddMainUI(spriteEditor.GetMainVisualContainer());

            using (skinningCache.DisableUndoScope())
            {
                skinningCache.Create(spriteEditor.GetDataProvider<ISpriteEditorDataProvider>(), SkinningCachePersistentState.instance);
                skinningCache.CreateToolCache(spriteEditor, m_LayoutOverlay);
                m_CharacterSpriteTool = skinningCache.CreateTool<SpriteBoneInfluenceTool>();
                m_CharacterSpriteTool.Initialize(m_LayoutOverlay);
                m_MeshPreviewTool = skinningCache.CreateTool<MeshPreviewTool>();
                SetupModuleToolGroup();
                m_MeshPreviewTool.Activate();

                ISpriteEditorDataProvider spriteEditorDataProvider = spriteEditor.GetDataProvider<ISpriteEditorDataProvider>();
                m_AnimationPreviewController = new AnimationPreviewController(
                    skinningCache,
                    m_AnimationPreviewPanel,
                    spriteEditorDataProvider.pixelsPerUnit,
                    spriteEditor.RequestRepaint);
                ConnectConstraintAnimationPreview(Tools.ConstraintsPosition);
                ConnectConstraintAnimationPreview(Tools.ConstraintsRotation);
                ConnectConstraintAnimationPreview(Tools.ConstraintsScale);

                m_SpriteOutlineRenderer = new SpriteOutlineRenderer(skinningCache.events);

                spriteEditor.enableMouseMoveEvent = true;
                EditorApplication.playModeStateChanged += PlayModeStateChanged;

                Undo.undoRedoPerformed += UndoRedoPerformed;
                skinningCache.events.skeletonTopologyChanged.AddListener(SkeletonTopologyChanged);
                skinningCache.events.skeletonPreviewPoseChanged.AddListener(SkeletonPreviewPoseChanged);
                skinningCache.events.skeletonBindPoseChanged.AddListener(SkeletonBindPoseChanged);
                skinningCache.events.characterPartChanged.AddListener(CharacterPartChanged);
                skinningCache.events.skinningModeChanged.AddListener(OnViewModeChanged);
                skinningCache.events.meshChanged.AddListener(OnMeshChanged);
                skinningCache.events.boneNameChanged.AddListener(OnBoneNameChanged);
                skinningCache.events.boneDepthChanged.AddListener(OnBoneDepthChanged);
                skinningCache.events.boneColorChanged.AddListener(OnBoneColorChanged);
                skinningCache.events.meshPreviewBehaviourChange.AddListener(OnMeshPreviewBehaviourChange);
                skinningCache.events.pivotChange.AddListener(OnPivotChanged);

                skinningCache.RestoreFromPersistentState();
                NormalizeRestoredNewGeometryTool();
                ActivateTool(skinningCache.selectedTool);
                skinningCache.RestoreToolStateFromPersistentState();

                // Set state for Switch Mode tool
                m_PreviousSkinningMode = skinningCache.mode;
                if (skinningCache.mode == SkinningMode.Character)
                {
                    skinningCache.GetTool(Tools.SwitchMode).Deactivate();
                }
                else
                {
                    skinningCache.GetTool(Tools.SwitchMode).Activate();
                }

                SetupSpriteEditor(true);

                m_HorizontalToggleTools = new HorizontalToggleTools(skinningCache)
                {
                    onActivateTool = (b) =>
                    {
                        using (skinningCache.UndoScope(TextContent.setTool))
                        {
                            ActivateTool(b);
                        }
                    }
                };

                AssetImporter ai = spriteEditor.GetDataProvider<ISpriteEditorDataProvider>() as AssetImporter;
                m_Analytics = new AnimationAnalytics(new UnityAnalyticsStorage(),
                    skinningCache.events,
                    new SkinningModuleAnalyticsModel(skinningCache),
                    ai == null ? -1 : ai.GetHashCode());

                UpdateCollapseToolbar();
            }
        }

        public override void OnModuleDeactivate()
        {
            CancelNewGeometryModeOnDeactivate();
            SkinningEditorInput.Reset();

            m_AnimationPreviewController?.Dispose();
            m_AnimationPreviewController = null;

            if (m_SpriteOutlineRenderer != null)
                m_SpriteOutlineRenderer.Dispose();

            spriteEditor.enableMouseMoveEvent = false;
            EditorApplication.playModeStateChanged -= PlayModeStateChanged;

            Undo.undoRedoPerformed -= UndoRedoPerformed;
            skinningCache.events.skeletonTopologyChanged.RemoveListener(SkeletonTopologyChanged);
            skinningCache.events.skeletonPreviewPoseChanged.RemoveListener(SkeletonPreviewPoseChanged);
            skinningCache.events.skeletonBindPoseChanged.RemoveListener(SkeletonBindPoseChanged);
            skinningCache.events.characterPartChanged.RemoveListener(CharacterPartChanged);
            skinningCache.events.skinningModeChanged.RemoveListener(OnViewModeChanged);
            skinningCache.events.meshChanged.RemoveListener(OnMeshChanged);
            skinningCache.events.boneNameChanged.RemoveListener(OnBoneNameChanged);
            skinningCache.events.boneDepthChanged.RemoveListener(OnBoneDepthChanged);
            skinningCache.events.boneColorChanged.RemoveListener(OnBoneColorChanged);
            skinningCache.events.meshPreviewBehaviourChange.RemoveListener(OnMeshPreviewBehaviourChange);
            skinningCache.events.pivotChange.RemoveListener(OnPivotChanged);

            RemoveMainUI(spriteEditor.GetMainVisualContainer());
            RestoreSpriteEditor();
            m_Analytics.Dispose();
            m_Analytics = null;

            Cache.Destroy(m_SkinningCache);
        }

        void PlayModeStateChanged(PlayModeStateChange newState)
        {
            if (newState == PlayModeStateChange.ExitingEditMode && m_HasUnsavedChanges)
            {
                bool shouldApply = EditorUtility.DisplayDialog(TextContent.savePopupTitle, TextContent.savePopupMessage, TextContent.savePopupOptionYes, TextContent.savePopupOptionNo);
                spriteEditor.ApplyOrRevertModification(shouldApply);
            }
        }

        private void UpdateCollapseToolbar()
        {
            m_CollapseToolbar = SkinningModuleSettings.compactToolBar;
            m_PoseToolbar.CollapseToolBar(m_CollapseToolbar);
            m_WeightToolbar.CollapseToolBar(m_CollapseToolbar);
            m_MeshToolbar.CollapseToolBar(m_CollapseToolbar);
            m_BoneToolbar.CollapseToolBar(m_CollapseToolbar);
            m_RigToolbar.CollapseToolBar(m_CollapseToolbar);
            m_LayoutOverlay.verticalToolbar.Collapse(m_CollapseToolbar);
            m_HorizontalToggleTools.collapseToolbar = m_CollapseToolbar;
        }

        private void OnBoneNameChanged(BoneCache bone)
        {
            CharacterCache character = skinningCache.character;

            if (character != null && character.skeleton == bone.skeleton)
                skinningCache.SyncSpriteSheetSkeletons();
            DataModified();
        }

        private void OnBoneDepthChanged(BoneCache bone)
        {
            SpriteCache[] sprites = skinningCache.GetSprites();
            SpriteMeshDataController controller = new SpriteMeshDataController();

            foreach (SpriteCache sprite in sprites)
            {
                MeshCache mesh = sprite.GetMesh();

                if (mesh.ContainsBone(bone))
                {
                    controller.spriteMeshData = mesh;
                    controller.SortTrianglesByDepth();
                    skinningCache.events.meshChanged.Invoke(mesh);
                }
            }

            DataModified();
        }

        private void OnBoneColorChanged(BoneCache bone)
        {
            DataModified();
        }

        private void OnMeshChanged(MeshCache mesh)
        {
            DataModified();
        }

        private void OnPivotChanged()
        {
            DataModified();
        }

        void DataModified()
        {
            spriteEditor.SetDataModified();
            m_HasUnsavedChanges = true;
        }

        private void OnViewModeChanged(SkinningMode mode)
        {
            SetupSpriteEditor();
        }

        private void SetupSpriteEditor(bool setPreviewTexture = false)
        {
            ITextureDataProvider textureProvider = spriteEditor.GetDataProvider<ITextureDataProvider>();
            if (textureProvider == null)
                return;

            int width = 0, height = 0;
            if (skinningCache.mode == SkinningMode.SpriteSheet)
            {
                textureProvider.GetTextureActualWidthAndHeight(out width, out height);
            }
            else
            {
                width = skinningCache.character.dimension.x;
                height = skinningCache.character.dimension.y;
            }

            if (m_PreviousSkinningMode != skinningCache.mode || setPreviewTexture)
            {
                spriteEditor.SetPreviewTexture(m_WorkspaceBackgroundTexture, width, height);
                if (m_PreviousSkinningMode != skinningCache.mode)
                {
                    m_PreviousSkinningMode = skinningCache.mode;
                    spriteEditor.ResetZoomAndScroll();
                }
            }

            spriteEditor.spriteRects = new List<SpriteRect>();
        }

        private void RestoreSpriteEditor()
        {
            ITextureDataProvider textureProvider = spriteEditor.GetDataProvider<ITextureDataProvider>();

            if (textureProvider != null)
            {
                int width, height;
                textureProvider.GetTextureActualWidthAndHeight(out width, out height);

                Texture2D texture = textureProvider.previewTexture;
                spriteEditor.SetPreviewTexture(texture, width, height);
            }

            ISpriteEditorDataProvider spriteRectProvider = spriteEditor.GetDataProvider<ISpriteEditorDataProvider>();

            if (spriteRectProvider != null)
                spriteEditor.spriteRects = new List<SpriteRect>(spriteRectProvider.GetSpriteRects());
        }

        public override bool CanBeActivated()
        {
            ISpriteEditorDataProvider dataProvider = spriteEditor.GetDataProvider<ISpriteEditorDataProvider>();
            return dataProvider == null ? false : dataProvider.spriteImportMode != SpriteImportMode.None;
        }

        public override void DoPostGUI()
        {
            if (!spriteEditor.windowDimension.Contains(Event.current.mousePosition))
                HandleUtility.nearestControl = 0;

            if (Event.current.type == EventType.Layout && m_PrevNearestControl != HandleUtility.nearestControl)
            {
                m_PrevNearestControl = HandleUtility.nearestControl;
                spriteEditor.RequestRepaint();
            }

            skinningCache.EndUndoOperation();
        }

        public override void DoMainGUI()
        {
            Debug.Assert(currentTool != null);
            if (SkinningEditorInput.Update(Event.current))
            {
                UpdateToggleState();
                spriteEditor.RequestRepaint();
            }
            DisableAltNavigationForTemporaryGeometryMode();

            DoViewGUI();

            if (!spriteEditor.editingDisabled)
                skinningCache.selectionTool.DoGUI();

            if (!spriteEditor.editingDisabled)
                ApplyConstraintPreviews();

            m_MeshPreviewTool.previewBehaviourOverride = m_MeshPreviewBehaviourOverride != null ? m_MeshPreviewBehaviourOverride : currentTool.previewBehaviour;
            m_MeshPreviewTool.DoGUI();
            m_MeshPreviewTool.DrawOverlay();

            if (Event.current.type == EventType.Repaint)
                m_SpriteOutlineRenderer.RenderSpriteOutline(spriteEditor, skinningCache.selectedSprite);

            m_MeshPreviewTool.OverlayWireframe();
            DrawRectGizmos();

            if (!spriteEditor.editingDisabled)
            {
                currentTool.DoGUI();
                // Run again after tool input so bone edits made in this GUI event update constraints immediately.
                ApplyConstraintPreviews();
                HandleNewGeometryExitRequest();
                DoCopyPasteKeyboardEventHandling();
            }

            ConsumeUnhandledAltMouseNavigation();
            DisableBaseSpriteEditorAltNavigation();

            if (SkinningModuleSettings.compactToolBar != m_CollapseToolbar)
                UpdateCollapseToolbar();
        }

        void ConnectConstraintAnimationPreview(Tools toolType)
        {
            ConstraintsTool tool = skinningCache.GetTool(toolType) as ConstraintsTool;
            if (tool == null)
                return;

            tool.stopAnimationPreview = m_AnimationPreviewController.StopForConstraintSetChange;
            tool.resumeAnimationPreview = m_AnimationPreviewController.ResumeAfterConstraintSetChange;
        }

        void DisableBaseSpriteEditorAltNavigation()
        {
            DisableBaseSpriteEditorAltNavigation(true);
        }

        void DisableBaseSpriteEditorAltNavigation(bool requireMouseInWindow)
        {
            Event evt = Event.current;
            if (evt == null || (!evt.alt && !SkinningEditorInput.altKeyDown))
                return;

            if (requireMouseInWindow && !spriteEditor.windowDimension.Contains(evt.mousePosition))
                return;

            evt.modifiers &= ~EventModifiers.Alt;
        }

        void DisableAltNavigationForTemporaryGeometryMode()
        {
            if (!SkinningEditorInput.altKeyDown || !IsModifyCreateGeometryToolActive())
                return;

            DisableBaseSpriteEditorAltNavigation(false);
        }

        void ConsumeUnhandledAltMouseNavigation()
        {
            Event evt = Event.current;
            if (evt == null || !IsModifyCreateGeometryToolActive() || (!evt.alt && !SkinningEditorInput.altKeyDown))
                return;

            if (!IsMouseNavigationEvent(evt))
                return;

            evt.Use();
        }

        static bool IsMouseNavigationEvent(Event evt)
        {
            return evt.type == EventType.MouseDown ||
                evt.type == EventType.MouseDrag ||
                evt.type == EventType.MouseUp;
        }

        bool IsModifyCreateGeometryToolActive()
        {
            return currentTool == skinningCache.GetTool(Tools.EditGeometry) ||
                currentTool == skinningCache.GetTool(Tools.CreateVertex);
        }

        void ApplyConstraintPreviews()
        {
            ApplyConstraintPreview(Tools.ConstraintsPosition);
            ApplyConstraintPreview(Tools.ConstraintsRotation);
            ApplyConstraintPreview(Tools.ConstraintsScale);
        }

        void ApplyConstraintPreview(Tools toolType)
        {
            ConstraintsTool tool = skinningCache.GetTool(toolType) as ConstraintsTool;
            if (tool != null)
                tool.ApplySharedPreview();
        }

        public override void DoToolbarGUI(Rect drawArea)
        {
            m_HorizontalToggleTools.DoGUI(drawArea, currentTool, spriteEditor.editingDisabled);
        }

        void DoCopyPasteKeyboardEventHandling()
        {
            Event evt = Event.current;
            CopyTool copyTool = skinningCache.GetTool(Tools.CopyPaste) as CopyTool;

            if (copyTool != null && evt.type == EventType.KeyDown && evt.keyCode == KeyCode.V && evt.shift && (evt.control || evt.command))
            {
                if (PasteSelectedBoneTransforms(true))
                {
                    evt.Use();
                    return;
                }

                if (skinningCache.vertexSelection.Count > 0)
                {
                    if (!copyTool.OnPasteMirroredVertexSelectionActivated())
                        Debug.LogWarning("Mirrored vertex paste requires copied and target vertex selections with the same vertex count.");
                }
                else
                {
                    bool boneReadOnly = skinningCache.bonesReadOnly;
                    copyTool.OnPasteActivated(!boneReadOnly, true, true, false);
                }

                evt.Use();
                return;
            }

            if (evt.type == EventType.ValidateCommand)
            {
                if (evt.commandName == "Copy" || evt.commandName == "Paste")
                    evt.Use();
                return;
            }

            if (evt.type == EventType.ExecuteCommand)
            {
                if (evt.commandName == "Copy" && CopySelectedBoneTransforms())
                {
                    evt.Use();
                }
                else if (evt.commandName == "Paste" && PasteSelectedBoneTransforms(false))
                {
                    evt.Use();
                }
                else if (copyTool != null && evt.commandName == "Copy")
                {
                    copyTool.OnCopyActivated();
                    evt.Use();
                }
                else if (copyTool != null && evt.commandName == "Paste")
                {
                    bool boneReadOnly = skinningCache.bonesReadOnly;
                    copyTool.OnPasteActivated(!boneReadOnly, true, false, false);
                    evt.Use();
                }
            }
        }

        private void DrawRectGizmos()
        {
            if (Event.current.type == EventType.Repaint)
            {
                SpriteCache selectedSprite = skinningCache.selectedSprite;
                SpriteCache[] sprites = skinningCache.GetSprites();
                Color unselectedRectColor = new Color(1f, 1f, 1f, 0.5f);

                foreach (SpriteCache sprite in sprites)
                {
                    SkeletonCache skeleton = skinningCache.GetEffectiveSkeleton(sprite);

                    Debug.Assert(skeleton != null);

                    if (skeleton.isPosePreview)
                        continue;

                    Color color = unselectedRectColor;

                    if (sprite == selectedSprite)
                        color = DrawingUtility.spriteBorderColor;

                    if (skinningCache.mode == SkinningMode.Character
                        && sprite != selectedSprite)
                        continue;

                    Matrix4x4 matrix = sprite.GetLocalToWorldMatrixFromMode();
                    Rect rect = new Rect(matrix.MultiplyPoint3x4(Vector3.zero), sprite.textureRect.size);

                    DrawingUtility.BeginLines(color);
                    DrawingUtility.DrawBox(rect);
                    DrawingUtility.EndLines();
                }
            }
        }

        private void UndoRedoPerformed()
        {
            using (new DisableUndoScope(skinningCache))
            {
                UpdateToggleState();
                skinningCache.UndoRedoPerformed();
                SetupSpriteEditor();
            }
        }

        #region CharacterConsistency

        //TODO: Bring this to a better place, maybe CharacterController
        private void SkeletonPreviewPoseChanged(SkeletonCache skeleton)
        {
            CharacterCache character = skinningCache.character;

            if (character != null && character.skeleton == skeleton)
                skinningCache.SyncSpriteSheetSkeletons();
        }

        private void SkeletonBindPoseChanged(SkeletonCache skeleton)
        {
            CharacterCache character = skinningCache.character;

            if (character != null && character.skeleton == skeleton)
                skinningCache.SyncSpriteSheetSkeletons();
            DataModified();
        }

        private void SkeletonTopologyChanged(SkeletonCache skeleton)
        {
            CharacterCache character = skinningCache.character;

            if (character == null)
            {
                SpriteCache sprite = FindSpriteFromSkeleton(skeleton);

                Debug.Assert(sprite != null);

                sprite.UpdateMesh(skeleton.bones);

                DataModified();
            }
            else if (character.skeleton == skeleton)
            {
                skinningCache.CreateSpriteSheetSkeletons();
                DataModified();
            }
        }

        private void CharacterPartChanged(CharacterPartCache characterPart)
        {
            CharacterCache character = skinningCache.character;

            Debug.Assert(character != null);

            using (new DefaultPoseScope(character.skeleton))
            {
                skinningCache.CreateSpriteSheetSkeleton(characterPart);
                DataModified();
            }

            if (skinningCache.mode == SkinningMode.Character)
                characterPart.SyncSpriteSheetSkeleton();
        }

        private SpriteCache FindSpriteFromSkeleton(SkeletonCache skeleton)
        {
            SpriteCache[] sprites = skinningCache.GetSprites();
            return sprites.FirstOrDefault(sprite => sprite.GetSkeleton() == skeleton);
        }

        #endregion

        public override bool ApplyRevert(bool apply)
        {
            if (apply)
            {
                m_Analytics.FlushEvent();
                ApplyChanges(skinningCache, spriteEditor.GetDataProvider<ISpriteEditorDataProvider>());
                DoApplyAnalytics();
            }
            else
                skinningCache.Revert();

            m_HasUnsavedChanges = false;
            return true;
        }

        internal static void ApplyChanges(SkinningCache skinningCache, ISpriteEditorDataProvider dataProvider)
        {
            skinningCache.applyingChanges = true;
            skinningCache.RestoreBindPose();
            ApplySpriteNames(skinningCache, dataProvider);
            ApplyBone(skinningCache, dataProvider);
            ApplyMesh(skinningCache, dataProvider);
            ApplyCharacter(skinningCache, dataProvider);
            skinningCache.applyingChanges = false;
        }

        static void ApplySpriteNames(SkinningCache skinningCache, ISpriteEditorDataProvider dataProvider)
        {
            SpriteRect[] spriteRects = dataProvider.GetSpriteRects();
            SpriteCache[] sprites = skinningCache.GetSprites();
            bool changed = false;

            foreach (SpriteCache sprite in sprites)
            {
                for (int i = 0; i < spriteRects.Length; ++i)
                {
                    if (spriteRects[i].spriteID.ToString() != sprite.id || spriteRects[i].name == sprite.name)
                        continue;

                    spriteRects[i].name = sprite.name;
                    changed = true;
                    break;
                }
            }

            if (changed)
                dataProvider.SetSpriteRects(spriteRects);
        }

        private void DoApplyAnalytics()
        {
            SpriteCache[] sprites = skinningCache.GetSprites();
            int[] spriteBoneCount = sprites.Select(s => s.GetSkeleton().boneCount).ToArray();
            BoneCache[] bones = null;

            if (skinningCache.hasCharacter)
                bones = skinningCache.character.skeleton.bones;
            else
                bones = sprites.SelectMany(s => s.GetSkeleton().bones).ToArray();

            m_Analytics.SendApplyEvent(sprites.Length, spriteBoneCount, bones);
        }

        static void ApplyBone(SkinningCache skinningCache, ISpriteEditorDataProvider dataProvider)
        {
            ISpriteBoneDataProvider boneDataProvider = dataProvider.GetDataProvider<ISpriteBoneDataProvider>();
            if (boneDataProvider != null)
            {
                SpriteCache[] sprites = skinningCache.GetSprites();
                foreach (SpriteCache sprite in sprites)
                {
                    BoneCache[] bones = GetBoneSaveOrder(sprite.GetSkeleton().bones);
                    boneDataProvider.SetBones(new GUID(sprite.id), bones.ToSpriteBone(sprite.localToWorldMatrix).ToList());
                }
            }
        }

        static void ApplyMesh(SkinningCache skinningCache, ISpriteEditorDataProvider dataProvider)
        {
            ISpriteMeshDataProvider meshDataProvider = dataProvider.GetDataProvider<ISpriteMeshDataProvider>();
            if (meshDataProvider != null)
            {
                SpriteCache[] sprites = skinningCache.GetSprites();
                foreach (SpriteCache sprite in sprites)
                {
                    MeshCache mesh = sprite.GetMesh();
                    GUID guid = new GUID(sprite.id);

                    Vertex2DMetaData[] vertices = new Vertex2DMetaData[mesh.vertexCount];
                    BoneCache[] spriteBones = GetBoneSaveOrder(sprite.GetSkeleton().bones);
                    BoneCache[] meshBones = mesh.bones;
                    for (int i = 0; i < vertices.Length; ++i)
                    {
                        vertices[i].position = mesh.vertices[i];
                        vertices[i].boneWeight = ToSpriteBoneWeight(mesh.vertexWeights[i], meshBones, spriteBones);
                    }

                    meshDataProvider.SetVertices(guid, vertices);
                    meshDataProvider.SetIndices(guid, mesh.indices);

                    Vector2Int[] edgeVectArr = EditorUtilities.ToVector2Int(mesh.edges);
                    meshDataProvider.SetEdges(guid, edgeVectArr);
                }
            }
        }

        static BoneWeight ToSpriteBoneWeight(EditableBoneWeight editableBoneWeight, BoneCache[] meshBones, BoneCache[] spriteBones)
        {
            BoneWeight boneWeight = editableBoneWeight.ToBoneWeight(false);

            for (int i = 0; i < 4; ++i)
            {
                float weight = boneWeight.GetWeight(i);
                if (weight <= 0f)
                    continue;

                int meshBoneIndex = boneWeight.GetBoneIndex(i);
                if (meshBoneIndex < 0 || meshBoneIndex >= meshBones.Length)
                    continue;

                int spriteBoneIndex = FindSpriteBoneIndex(meshBones[meshBoneIndex], spriteBones);
                if (spriteBoneIndex == -1)
                    continue;

                BoneWeightExtensions.SetBoneIndex(ref boneWeight, i, spriteBoneIndex);
            }

            return boneWeight;
        }

        static int FindSpriteBoneIndex(BoneCache meshBone, BoneCache[] spriteBones)
        {
            int spriteBoneIndex = Array.IndexOf(spriteBones, meshBone);
            if (spriteBoneIndex != -1 || meshBone == null)
                return spriteBoneIndex;

            // Character-mode meshes reference bones from the character skeleton while
            // SpriteBone data is written from the sprite skeleton's cloned BoneCache objects.
            // CharacterPart.bones is an influence list and may be in any order, so its list
            // index cannot be used as a sprite-bone index. GUID is the stable identity shared
            // by both caches.
            return Array.FindIndex(spriteBones, bone => bone != null && bone.guid == meshBone.guid);
        }

        static BoneCache[] GetBoneSaveOrder(BoneCache[] bones)
        {
            List<BoneCache> orderedBones = new List<BoneCache>(bones.Length);
            HashSet<BoneCache> visitedBones = new HashSet<BoneCache>();

            for (int i = 0; i < bones.Length; ++i)
                AddBoneAfterParent(bones[i], bones, orderedBones, visitedBones);

            return orderedBones.ToArray();
        }

        static void AddBoneAfterParent(BoneCache bone, BoneCache[] sourceBones, List<BoneCache> orderedBones, HashSet<BoneCache> visitedBones)
        {
            if (bone == null || visitedBones.Contains(bone))
                return;

            BoneCache parent = bone.parentBone;
            if (parent != null && Array.IndexOf(sourceBones, parent) != -1)
                AddBoneAfterParent(parent, sourceBones, orderedBones, visitedBones);

            visitedBones.Add(bone);
            orderedBones.Add(bone);
        }

        static void ApplyCharacter(SkinningCache skinningCache, ISpriteEditorDataProvider dataProvider)
        {
            ICharacterDataProvider characterDataProvider = dataProvider.GetDataProvider<ICharacterDataProvider>();
            CharacterCache character = skinningCache.character;
            if (characterDataProvider != null && character != null)
            {
                CharacterData data = new CharacterData();
                BoneCache[] characterBones = GetBoneSaveOrder(character.skeleton.bones);
                data.bones = characterBones.ToSpriteBone(Matrix4x4.identity);
                data.pivot = character.pivot;
                CharacterPartCache[] parts = character.parts;
                data.parts = parts.Select(x =>
                    new CharacterPart()
                    {
                        spriteId = x.sprite.id,
                        spritePosition = new RectInt((int)x.position.x, (int)x.position.y, (int)x.sprite.textureRect.width, (int)x.sprite.textureRect.height),
                        // CharacterPart.bones is not just an influence set: its order maps each
                        // serialized SpriteBone to the corresponding character-skeleton bone.
                        // Keep it aligned with the exact parent-first order written by ApplyBone.
                        bones = GetBoneSaveOrder(x.sprite.GetSkeleton().bones)
                            .Select(spriteBone => FindBoneIndexByGuid(characterBones, spriteBone))
                            .Where(boneIndex => boneIndex != -1)
                            .ToArray()
                    }
                ).ToArray();

                characterDataProvider.SetCharacterData(data);

            }
        }

        static int FindBoneIndexByGuid(BoneCache[] bones, BoneCache target)
        {
            if (target == null)
                return -1;

            return Array.FindIndex(bones, bone => bone != null && bone.guid == target.guid);
        }

        void OnMeshPreviewBehaviourChange(IMeshPreviewBehaviour meshPreviewBehaviour)
        {
            m_MeshPreviewBehaviourOverride = meshPreviewBehaviour;
        }

        private void SetupModuleToolGroup()
        {
            m_ModuleToolGroup = new ModuleToolGroup();
            m_ModuleToolGroup.AddToolToGroup(0, skinningCache.GetTool(Tools.Visibility), null);
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.EditGeometry), () => currentTool = skinningCache.GetTool(Tools.EditGeometry));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.CreateVertex), () => currentTool = skinningCache.GetTool(Tools.CreateVertex));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.CreateEdge), () => currentTool = skinningCache.GetTool(Tools.CreateEdge));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.SplitEdge), () => currentTool = skinningCache.GetTool(Tools.SplitEdge));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.GenerateGeometry), () => currentTool = skinningCache.GetTool(Tools.GenerateGeometry));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.EditPose), () => currentTool = skinningCache.GetTool(Tools.EditPose));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.EditJoints), () => currentTool = skinningCache.GetTool(Tools.EditJoints));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.CreateBone), () => currentTool = skinningCache.GetTool(Tools.CreateBone));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.SplitBone), () => currentTool = skinningCache.GetTool(Tools.SplitBone));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.WeightSlider), () => currentTool = skinningCache.GetTool(Tools.WeightSlider));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.WeightBrush), () => currentTool = skinningCache.GetTool(Tools.WeightBrush));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.GenerateWeights), () => currentTool = skinningCache.GetTool(Tools.GenerateWeights));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.BoneInfluence), () => currentTool = skinningCache.GetTool(Tools.BoneInfluence));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.SpriteInfluence), () => currentTool = skinningCache.GetTool(Tools.SpriteInfluence));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.ConstraintsPosition), () => currentTool = skinningCache.GetTool(Tools.ConstraintsPosition));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.ConstraintsRotation), () => currentTool = skinningCache.GetTool(Tools.ConstraintsRotation));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.ConstraintsScale), () => currentTool = skinningCache.GetTool(Tools.ConstraintsScale));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.CopyPaste), () => currentTool = skinningCache.GetTool(Tools.CopyPaste));
            m_ModuleToolGroup.AddToolToGroup(1, skinningCache.GetTool(Tools.CharacterPivotTool), () =>
            {
                if (skinningCache.hasCharacter)
                    currentTool = skinningCache.GetTool(Tools.CharacterPivotTool);
                else
                    ActivateTool(skinningCache.GetTool(Tools.EditPose));
            });
        }
    }
}
