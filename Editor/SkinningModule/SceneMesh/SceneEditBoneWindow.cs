using System;
using System.Linq;
using UnityEditor.UIElements;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    internal sealed class SceneEditBoneWindow : SceneEditWindow
    {
        [SerializeField] SceneBoneSession m_Session;
        [SerializeField] SpriteRenderer m_Renderer;
        [SerializeField] UnityEngine.Object[] m_EntrySelection;
        [SerializeField] int m_Mode;
        bool m_Adjust;
        SceneBoneAdjustPreview m_AdjustPreview;
        System.Collections.Generic.Dictionary<string, Matrix4x4> m_AdjustRotations;
        int m_AdjustUndoGroup = -1;
        VisualElement m_AdjustPulse;
        double m_AdjustPulseStart;
        SkeletonView m_View;
        SkeletonController m_Controller;
        SceneMeshSource m_Source;
        bool m_DiscardOnClose;
        bool m_Saving;
        bool m_CloseQueued;
        string m_Error;
        string m_DisplayedNameGuid;
        string m_DisplayedNameValue;
        double m_NextPoseRefresh;
        BoneCache m_RecreatingBone;
        Vector3 m_RecreateStart;
        Vector3 m_RecreateEnd;
        int m_RecreateControl;

        BoneCache recreateTarget
        {
            get
            {
                var selection = m_Session?.cache?.skeletonSelection;
                var bone = selection?.activeElement;
                return selection?.Count == 1 && bone != null && !bone.IsConstraintParent() ? bone : null;
            }
        }

        sealed class SceneBoneGUI : GUIWrapper
        {
            public override float DistanceToCircle(Vector3 center, float radius) =>
                Mathf.Max(0, Vector2.Distance(Event.current.mousePosition, HandleUtility.WorldToGUIPoint(center)) - 8f / EditorGUIUtility.pixelsPerPoint);
        }

        internal static bool IsEditingAsset(string path) => Resources.FindObjectsOfTypeAll<SceneEditBoneWindow>()
            .Any(window => window.m_Session != null && window.m_Session.assetPath == path);
        internal string editingAssetPath => m_Session != null ? m_Session.assetPath : null;

        internal static bool IsEditingRenderer(SpriteRenderer renderer) => renderer != null && Resources.FindObjectsOfTypeAll<SceneEditBoneWindow>()
            .Any(window => window.m_Renderer == renderer);

        internal static bool HasActiveSceneHost => Resources.FindObjectsOfTypeAll<SceneEditBoneWindow>()
            .Any(window => !window.m_Saving && window.BlockReason() == null);

        internal static void OpenForBone(SpriteRenderer renderer, string boneGuid)
        {
            OpenForBones(renderer, new[] { boneGuid });
        }

        internal static void OpenForBones(SpriteRenderer renderer, string[] boneGuids)
        {
            try
            {
                var source = new SceneMeshSource(renderer.sprite);
                // Ordinary Sprites retain their existing per-sprite skeleton entry.
                var characterProvider = source.provider.GetDataProvider<ICharacterDataProvider>();
                var character = characterProvider != null && characterProvider.GetCharacterData().bones != null;
                OpenForRenderer(renderer, skeletonOnly: character, boneGuids: boneGuids);
            }
            catch (Exception ex) { EditorUtility.DisplayDialog("Edit Bones", ex.Message, "OK"); }
        }

        internal static void OpenForRenderer(SpriteRenderer renderer, string boneGuid = null, bool skeletonOnly = false, string[] boneGuids = null)
        {
            SceneEditBoneWindow opening = null;
            try
            {
                if (SceneSkinningSuspension.active) throw new InvalidOperationException("Resume or cancel Suspend Skinning first.");
                if (renderer == null || renderer.sprite == null || !renderer.gameObject.scene.IsValid())
                    throw new InvalidOperationException("Select a SpriteRenderer with an imported Sprite in the scene.");
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Bone editing is available in Edit Mode.");
                if (UniskinClippingMaskUtility.Find(renderer.sprite) != null)
                {
                    var clipping = renderer.GetComponentInParent<UniskinClippingMask>();
                    renderer = clipping != null && clipping.character != null ? clipping.character.renderers.FirstOrDefault(item => item != null && item.sprite != null) : null;
                    if (renderer == null) throw new InvalidOperationException("Edit bones on the contour's character root.");
                    skeletonOnly = new SceneMeshSource(renderer.sprite).provider.GetDataProvider<ICharacterDataProvider>() != null;
                }
                var path = UniskinRigUtility.SourcePath(renderer.sprite);
                if (SkinningModule.IsEditingAsset(path))
                    throw new InvalidOperationException("Close Skinning Editor for this asset before opening Edit Bones.");
                foreach (var meshWindow in Resources.FindObjectsOfTypeAll<SceneEditMeshWindow>())
                    if (!meshWindow.RequestClose()) return;
                foreach (var weightWindow in Resources.FindObjectsOfTypeAll<SceneEditWeightsWindow>())
                    if (!weightWindow.RequestClose()) return;
                foreach (var existing in Resources.FindObjectsOfTypeAll<SceneEditBoneWindow>())
                {
                    if (existing.m_Renderer == renderer && existing.m_Session.skeletonOnly == skeletonOnly)
                    {
                        if (boneGuids != null) existing.SelectBones(boneGuids);
                        else if (boneGuid != null)
                        {
                            existing.SelectBone(boneGuid);
                        }
                        existing.m_EntrySelection = Selection.objects;
                        existing.Focus(); return;
                    }
                    if (!existing.RequestClose()) return;
                }
                var source = new SceneMeshSource(renderer.sprite);
                var window = CreateInstance<SceneEditBoneWindow>();
                opening = window;
                window.m_Renderer = renderer;
                window.m_EntrySelection = Selection.objects;
                window.m_Source = source;
                window.m_Session.Load(renderer.sprite, source, skeletonOnly);
                window.m_Session.CaptureScenePose(renderer, source.rect.rect);
                window.SetupController();
                if (boneGuids != null) window.SelectBones(boneGuids);
                else if (boneGuid != null) window.SelectBone(boneGuid);
                var blocked = window.BlockReason();
                if (blocked != null) throw new InvalidOperationException(blocked);
                var origin = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.position.position + new Vector2(35, 55) : new Vector2(100, 100);
                window.position = new Rect(origin, new Vector2(340, 150) / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint));
                window.ShowPopup();
                window.Focus();
                SceneView.RepaintAll();
            }
            catch (Exception ex)
            {
                if (opening != null) DestroyImmediate(opening);
                EditorUtility.DisplayDialog("Edit Bones", ex.Message, "OK");
            }
        }

        void SelectBone(string boneGuid)
        {
            if (!m_Session.SelectBone(boneGuid)) return;
            m_Controller?.Reset();
            m_Mode = 0;
            UpdateUI();
            SceneView.RepaintAll();
        }

        void SelectBones(string[] boneGuids)
        {
            m_Session.cache.skeletonSelection.elements = m_Session.skeleton.bones
                .Where(bone => boneGuids.Contains(bone.guid) && !bone.IsConstraintParent()).ToArray();
            m_Controller?.Reset();
            m_Mode = 0;
            UpdateUI();
            SceneView.RepaintAll();
        }

        void OnEnable()
        {
            if (!EnableWindowLifetime(m_Renderer != null || m_Session != null)) return;
            titleContent = new GUIContent("Edit Bones", ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/EditBone.png"));
            minSize = new Vector2(340, 50) / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
            if (m_Session == null)
            {
                m_Session = CreateInstance<SceneBoneSession>();
                m_Session.hideFlags = HideFlags.HideAndDontSave;
            }
            m_Session.Attach();
            if (m_Renderer != null && m_Renderer.sprite != null)
            {
                try { m_Source = new SceneMeshSource(m_Renderer.sprite); }
                catch (Exception ex) { m_Error = ex.Message; }
            }
            SetupController();
            Selection.selectionChanged += OnSelectionChanged;
            SceneView.beforeSceneGui += BeforeSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            Undo.postprocessModifications += OnAdjustInspectorModifications;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += UpdateScenePose;
        }

        void SetupController()
        {
            if (m_Session.cache == null) return;
            if (m_Renderer != null && m_Source != null && !m_Session.scenePoseCaptured)
                m_Session.CaptureScenePose(m_Renderer, m_Source.rect.rect);
            m_View = new SkeletonView(new SceneBoneGUI()) { selectExistingBonesInCreate = true };
            m_Controller = new SkeletonController
            {
                view = m_View, selection = m_Session.cache.skeletonSelection,
                skeleton = m_Session.skeleton, editBindPose = true,
                confirmBoneRemoval = ConfirmBoneRemoval
            };
        }

        void OnDisable()
        {
            m_AdjustPreview?.Restore(); m_AdjustPreview = null;
            CancelRecreate();
            DisableWindowLifetime();
            if (!isClosingForReload && !isRestoredWindow && !m_DiscardOnClose && !m_Saving && !EditorApplication.isPlaying && m_Source != null &&
                m_Renderer != null && m_Renderer.sprite != null && m_Session != null && m_Session.cache != null)
            {
                try { if (m_Adjust) m_Session.SaveAdjusted(new SceneMeshSource(m_Renderer.sprite), m_Renderer, m_AdjustRotations, m_AdjustUndoGroup);
                    else if (m_Session.dirty) m_Session.Save(new SceneMeshSource(m_Renderer.sprite)); }
                catch (Exception exception) { Debug.LogError("Edit Bones auto-save failed: " + exception.Message); }
            }
            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.beforeSceneGui -= BeforeSceneGUI;
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.postprocessModifications -= OnAdjustInspectorModifications;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.update -= UpdateScenePose;
            m_Controller?.Reset();
            RestoreTool();
        }

        void OnDestroy() { if (m_Session != null) DestroyImmediate(m_Session); }

        public void CreateGUI()
        {
            if (isRestoredWindow) return;
            rootVisualElement.Clear();
            m_DisplayedNameGuid = null;
            m_DisplayedNameValue = null;
            var template = ResourceLoader.Load<VisualTreeAsset>("SkinningModule/SceneEditBoneWindow.uxml");
            if (template == null) { rootVisualElement.Add(new Label("Edit Bones UI could not be loaded.")); return; }
            template.CloneTree(rootVisualElement);
            rootVisualElement.Q<Image>("windowIcon").image = titleContent.image;
            rootVisualElement.Q<Label>("windowTitle").text = m_Session.skeletonOnly ? "Edit Bones — Skeleton" : "Edit Bones";
            var iconSize = 16f / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
            rootVisualElement.Q("windowIcon").style.width = rootVisualElement.Q("windowIcon").style.height = iconSize;
            SceneMeshWindowStyle.Configure(rootVisualElement, this, () => RequestClose());
            Bind("create", "Bone_Create", 1);
            Bind("split", "Bone_Split", 2);
            Bind("setParent", "Bone_SetParent", 3);
            BindClipboard("copy", "Copy", () => m_Session.CopySelectedBones());
            BindClipboard("paste", "Paste", () => m_Session.PasteBones(false));
            BindClipboard("pasteMirror", "Paste", () => m_Session.PasteBones(true));
            var adjustButton = rootVisualElement.Q<Button>("adjust");
            adjustButton.text = string.Empty;
            adjustButton.Add(new Image { image = ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/Adjust.png"),
                name = "commandIcon", pickingMode = PickingMode.Ignore });
            adjustButton.Add(new Label("Adjust") { pickingMode = PickingMode.Ignore });
            adjustButton.clicked += ToggleAdjust;
            m_AdjustPulse = new VisualElement { name = "adjustPulse", pickingMode = PickingMode.Ignore };
            rootVisualElement.Q<Button>("adjust").Insert(0, m_AdjustPulse);
            rootVisualElement.schedule.Execute(UpdateAdjustPulse).Every(16);
            rootVisualElement.Q<Button>("boneIcon").clicked += ShowIconPicker;
            rootVisualElement.Q<TextField>("boneName").RegisterValueChangedCallback(evt =>
            {
                try
                {
                    m_Session.RenameSelectedBone(evt.newValue);
                    m_Error = null;
                }
                catch (Exception ex) { m_Error = ex.Message; EditorUtility.DisplayDialog("Rename Bone", ex.Message, "OK"); }
                rootVisualElement.Q<TextField>("boneName").SetValueWithoutNotify(m_Session.cache.skeletonSelection.activeElement?.name ?? string.Empty);
                UpdateUI();
                SceneView.RepaintAll();
            });
            rootVisualElement.Q<ColorField>("boneColor").RegisterValueChangedCallback(evt => EditProperty("Change Bone Color", bone => bone.bindPoseColor = evt.newValue, true));
            rootVisualElement.Q<FloatField>("boneIconSize").RegisterValueChangedCallback(evt => EditProperty("Change Bone Icon Size", bone =>
            {
                if (bone.iconId != BoneIconUtility.Default && float.IsFinite(evt.newValue)) bone.iconSize = Mathf.Clamp(evt.newValue, 4f, 128f);
            }, true));
            rootVisualElement.Q<FloatField>("boneLength").RegisterValueChangedCallback(evt => EditProperty("Change Bone Length", bone =>
            {
                if (float.IsFinite(evt.newValue)) bone.length = Mathf.Max(0, evt.newValue);
            }, false));
            rootVisualElement.Q<Vector2Field>("adjustPosition").RegisterValueChangedCallback(evt =>
            {
                var bone = recreateTarget;
                if (!m_Adjust || bone == null || !float.IsFinite(evt.newValue.x) || !float.IsFinite(evt.newValue.y)) return;
                m_Session.AdjustPose(bone, new Vector3(evt.newValue.x, evt.newValue.y, bone.position.z), bone.rotation);
                RefreshAdjustPreview(); SceneView.RepaintAll();
            });
            rootVisualElement.Q<FloatField>("adjustRotation").RegisterValueChangedCallback(evt =>
            {
                var bone = recreateTarget;
                if (!m_Adjust || bone == null || !float.IsFinite(evt.newValue)) return;
                m_Session.AdjustRotation(bone, Quaternion.Euler(0, 0, evt.newValue));
                RefreshAdjustPreview(); SceneView.RepaintAll();
            });
            rootVisualElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                CancelMode();
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            rootVisualElement.schedule.Execute(UpdateUI).Every(200);
            UpdateUI();
        }

        void Bind(string name, string iconName, int mode)
        {
            var button = rootVisualElement.Q<Button>(name);
            var label = new Label(button.text) { pickingMode = PickingMode.Ignore };
            button.text = string.Empty;
            button.Add(new Image { image = ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/" + iconName + ".png"),
                name = "commandIcon", pickingMode = PickingMode.Ignore });
            button.Add(label);
            if (name == "setParent") button.AddToClassList("mesh-last-action");
            button.clicked += () => { if (m_Adjust && !Apply()) return;
                CancelRecreate(); m_Controller?.Reset(); m_Mode = m_Mode == mode ? 0 : mode; UpdateUI(); SceneView.RepaintAll(); };
        }

        void BindClipboard(string name, string iconName, Action action)
        {
            var button = rootVisualElement.Q<Button>(name);
            var label = new Label(button.text) { pickingMode = PickingMode.Ignore };
            button.text = string.Empty;
            button.Add(new Image { image = ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/" + iconName + ".png"),
                name = "commandIcon", pickingMode = PickingMode.Ignore });
            button.Add(label);
            if (name == "pasteMirror") button.AddToClassList("mesh-last-action");
            button.clicked += () =>
            {
                if (m_Saving || BlockReason() != null) return;
                CancelRecreate();
                m_Controller?.Reset();
                action();
                UpdateUI();
                SceneView.RepaintAll();
            };
        }

        void ShowIconPicker()
        {
            if (m_Saving || BlockReason() != null) return;
            var selected = m_Session.cache.skeletonSelection.elements.Where(bone => bone != null && !bone.IsConstraintParent()).ToArray();
            if (selected.Length == 0) return;
            var mixed = selected.Any(bone => bone.iconId != selected[0].iconId);
            var anchor = rootVisualElement.Q<Button>("boneIcon").worldBound;
            anchor.position += position.position;
            PopupWindow.Show(GUIUtility.ScreenToGUIRect(anchor), new BoneIconPicker(mixed ? null : selected[0].iconId,
                id => { if (this != null) EditProperty("Change Bone Icon", bone => bone.iconId = id, true); }));
        }

        string BlockReason()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Bone editing is available in Edit Mode.";
            if (m_Renderer == null || m_Renderer.sprite == null || m_Session == null || m_Session.cache == null) return "The entry SpriteRenderer is unavailable.";
            if (m_Source == null) return m_Error ?? "The Sprite importer is unavailable. Close and reopen Edit Bones.";
            if (UniskinRigUtility.SourcePath(m_Renderer.sprite) != m_Session.assetPath || m_Renderer.sprite.GetSpriteID().ToString() != m_Session.spriteId)
                return "The renderer's Sprite changed. Close and reopen Edit Bones.";
            if (m_Renderer.drawMode != SpriteDrawMode.Simple) return "Set SpriteRenderer Draw Mode to Simple.";
            if (Mathf.Abs(m_Renderer.transform.lossyScale.x) < 0.000001f || Mathf.Abs(m_Renderer.transform.lossyScale.y) < 0.000001f)
                return "The SpriteRenderer needs non-zero X and Y scale.";
            if (SkinningModule.IsEditingAsset(m_Session.assetPath)) return "Close Skinning Editor for this asset before continuing.";
            return null;
        }

        void EditProperty(string operation, Action<BoneCache> edit, bool multiple)
        {
            if (m_Saving || BlockReason() != null) return;
            var selected = m_Session.cache.skeletonSelection.elements.Where(bone => bone != null && !bone.IsConstraintParent()).ToArray();
            if (selected.Length == 0 || (!multiple && selected.Length != 1)) return;
            using (m_Session.cache.UndoScope(operation))
            {
                foreach (var bone in selected) edit(bone);
                m_Session.Changed();
            }
            UpdateUI();
            SceneView.RepaintAll();
        }

        void UpdateUI()
        {
            if (m_Saving || m_Session == null || m_Session.cache == null || rootVisualElement.Q("commands") == null) return;
            var blocked = BlockReason();
            rootVisualElement.Q("commands").SetEnabled(blocked == null);
            rootVisualElement.Q("commands").tooltip = blocked ?? m_Error;
            var recreating = !m_Adjust && (m_RecreatingBone != null || (SkinningEditorInput.altKeyDown && recreateTarget != null));
            foreach (var pair in new[] { ("create", 1), ("split", 2), ("setParent", 3) })
                rootVisualElement.Q<Button>(pair.Item1).EnableInClassList("mesh-active", recreating ? pair.Item2 == 1 : m_Mode == pair.Item2);
            var adjust = rootVisualElement.Q<Button>("adjust");
            adjust.EnableInClassList("mesh-active", m_Adjust);
            adjust.SetEnabled(blocked == null && !AnimationMode.InAnimationMode());
            foreach (var command in new[] { "create", "split", "setParent", "copy", "paste", "pasteMirror" })
                rootVisualElement.Q<Button>(command).SetEnabled(blocked == null && !m_Adjust);
            UpdateAdjustPulse();
            rootVisualElement.Q<Button>("create").tooltip = "Create bones. Hold Alt with one bone selected to redraw it while preserving its weights.";
            var selected = m_Session.cache != null ? m_Session.cache.skeletonSelection.elements.Where(bone => bone != null).ToArray() : Array.Empty<BoneCache>();
            var color = rootVisualElement.Q<ColorField>("boneColor");
            var name = rootVisualElement.Q<TextField>("boneName");
            name.SetEnabled(!m_Adjust && selected.Length == 1 && !selected[0].IsConstraintParent());
            var length = rootVisualElement.Q<FloatField>("boneLength");
            length.SetEnabled(!m_Adjust && selected.Length == 1);
            color.SetEnabled(!m_Adjust && selected.Length > 0);
            var editableIcons = selected.Where(bone => !bone.IsConstraintParent()).ToArray();
            rootVisualElement.Q<Button>("copy").SetEnabled(!m_Adjust && editableIcons.Length > 0);
            rootVisualElement.Q<Button>("paste").SetEnabled(!m_Adjust && SceneBoneSession.hasBoneClipboard);
            rootVisualElement.Q<Button>("pasteMirror").SetEnabled(!m_Adjust && SceneBoneSession.hasBoneClipboard);
            var iconButton = rootVisualElement.Q<Button>("boneIcon");
            iconButton.SetEnabled(!m_Adjust && editableIcons.Length > 0);
            var iconId = editableIcons.Length > 0 ? editableIcons[0].iconId : BoneIconUtility.Default;
            var mixedIcons = editableIcons.Any(bone => bone.iconId != iconId);
            BoneIconUtility.SetImage(rootVisualElement.Q<Image>("boneIconImage"), iconId);
            iconButton.tooltip = mixedIcons ? "Mixed icons. Choose an icon for all selected bones." : "Choose bone icon: " + iconId.Substring(5);
            // Poll viewport edits without replacing partially typed text.
            var focused = rootVisualElement.panel?.focusController.focusedElement as VisualElement;
            var iconSize = rootVisualElement.Q<FloatField>("boneIconSize");
            var sizedBones = editableIcons.Where(bone => bone.iconId != BoneIconUtility.Default).ToArray();
            iconSize.SetEnabled(!m_Adjust && sizedBones.Length > 0);
            iconSize.showMixedValue = sizedBones.Length > 0 && sizedBones.Any(bone => bone.iconSize != sizedBones[0].iconSize);
            if (focused != iconSize && (focused == null || !iconSize.Contains(focused)))
                iconSize.SetValueWithoutNotify(sizedBones.Length > 0 ? sizedBones[0].iconSize : BoneIconUtility.DefaultSize);
            var adjustProperties = rootVisualElement.Q("adjustProperties");
            adjustProperties.EnableInClassList("mesh-hidden", !m_Adjust);
            adjustProperties.SetEnabled(recreateTarget != null);
            var adjustPosition = rootVisualElement.Q<Vector2Field>("adjustPosition");
            var adjustRotation = rootVisualElement.Q<FloatField>("adjustRotation");
            if (m_Adjust && recreateTarget != null)
            {
                if (focused != adjustPosition && (focused == null || !adjustPosition.Contains(focused)))
                    adjustPosition.SetValueWithoutNotify(recreateTarget.position);
                if (focused != adjustRotation && (focused == null || !adjustRotation.Contains(focused)))
                    adjustRotation.SetValueWithoutNotify(recreateTarget.rotation.eulerAngles.z);
            }
            RefreshNameField(selected.Length == 1 ? selected[0] : null);
            if (focused != length && (focused == null || !length.Contains(focused))) length.SetValueWithoutNotify(selected.Length == 1 ? selected[0].length : 0);
            if (selected.Length > 0)
            {
                color.SetValueWithoutNotify(selected[0].bindPoseColor);
                color.showMixedValue = selected.Any(bone => bone.bindPoseColor != selected[0].bindPoseColor);
            }
            else color.showMixedValue = false;
            hasUnsavedChanges = false; // Closing saves directly instead of prompting.
            if (blocked != null) RestoreTool();
        }

        internal void RefreshNameField(BoneCache bone)
        {
            var guid = bone?.guid;
            var value = bone?.name ?? string.Empty;
            // A delayed TextField can expose the field itself as the focused element.
            // Refresh only when selection/model changes, never on a periodic poll of
            // the same model value: SetValueWithoutNotify also replaces pending text.
            if (m_DisplayedNameGuid == guid && m_DisplayedNameValue == value) return;
            m_DisplayedNameGuid = guid;
            m_DisplayedNameValue = value;
            rootVisualElement.Q<TextField>("boneName").SetValueWithoutNotify(value);
        }

        void BeforeSceneGUI(SceneView scene)
        {
            if (m_Saving) return;
            if (BlockReason() != null || m_Controller == null) toolScope.Restore();
            else toolScope.Hide();
        }

        void OnSceneGUI(SceneView scene)
        {
            if (m_Saving || BlockReason() != null || m_Controller == null) { CancelRecreate(); return; }
            if (!m_Adjust && Event.current.type == EventType.Layout) m_Session.RefreshScenePose(m_Renderer, m_Source.rect.rect);
            toolScope.Hide();
            var evt = Event.current;
            SkinningEditorInput.Update(evt);
            if (m_RecreatingBone != null && (!SkinningEditorInput.altKeyDown || evt.type == EventType.MouseLeaveWindow ||
                (evt.type == EventType.MouseDown && evt.button != 0))) CancelRecreate();
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape) { CancelMode(); evt.Use(); return; }
            var recreate = !m_Adjust && (m_RecreatingBone != null || (SkinningEditorInput.altKeyDown && recreateTarget != null && GUIUtility.hotControl == 0));
            if (evt.button == 1 || evt.button == 2 || ((evt.alt || SkinningEditorInput.altKeyDown) && !recreate)) return;
            if (recreate) evt.modifiers &= ~EventModifiers.Alt;
            if (!m_Adjust && evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace))
            { CancelRecreate(); DeleteSelection(); evt.Use(); return; }
            var primaryDown = evt.type == EventType.MouseDown && evt.button == 0;
            var child = m_Session.cache.skeletonSelection.activeElement;
            var control = GUIUtility.GetControlID("UniskinSceneBone".GetHashCode(), FocusType.Passive);
            if (evt.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            using (new Handles.DrawingScope(Color.white, m_Session.SkeletonToWorld(m_Renderer, m_Source.rect.rect)))
            {
                m_View.defaultControlID = control;
                m_View.mode = m_Adjust ? SkeletonMode.Selection : recreate ? SkeletonMode.Disabled : m_Mode == 1 ? SkeletonMode.CreateBone : m_Mode == 2 ? SkeletonMode.SplitBone : m_Mode == 3 ? SkeletonMode.Selection : SkeletonMode.EditJoints;
                m_Controller.suppressBoneSelection = recreate || (m_Mode == 3 && child != null);
                m_Controller.OnGUI();
                if (m_Adjust) HandleAdjust();
                else if (recreate) HandleRecreate(evt, control);
                else if (primaryDown && m_Mode == 3 && child != null)
                {
                    var parent = m_Controller.hoveredBone;
                    if (SceneBoneSession.CanSetParent(child, parent)) { m_Session.SetParent(child, parent); m_Mode = 0; m_Error = null; }
                    else m_Error = "A bone cannot be parented to itself or one of its descendants.";
                    evt.Use();
                }
                else if (primaryDown && m_Mode != 1 && m_Controller.hoveredBone == null && evt.type != EventType.Used)
                { m_Session.cache.skeletonSelection.Clear(); evt.Use(); }
                // Keep native Scene picking from selecting the renderer during bone gestures.
                if (primaryDown && evt.type != EventType.Used) evt.Use();
            }
            if (evt.rawType == EventType.MouseUp || GUIUtility.hotControl == 0) m_Session.cache.EndUndoOperation();
            if (evt.type != EventType.Layout && evt.type != EventType.Repaint) { UpdateUI(); scene.Repaint(); }
        }

        void UpdateScenePose()
        {
            if (SkinningEditorInput.Update(null))
            {
                UpdateUI();
                SceneView.RepaintAll();
            }
            if (EditorApplication.timeSinceStartup < m_NextPoseRefresh || m_Saving || BlockReason() != null) return;
            m_NextPoseRefresh = EditorApplication.timeSinceStartup + 1.0 / 30.0;
            if (!m_Adjust && m_Session.RefreshScenePose(m_Renderer, m_Source.rect.rect)) SceneView.RepaintAll();
        }

        void ToggleAdjust()
        {
            if (m_Adjust) { Apply(); return; }
            if (AnimationMode.InAnimationMode() || BlockReason() != null) return;
            if (m_Session.dirty && !Apply()) return;
            CancelRecreate(); m_Controller.Reset(); m_Mode = 0;
            m_Session.RefreshScenePose(m_Renderer, m_Source.rect.rect);
            Undo.IncrementCurrentGroup();
            m_AdjustUndoGroup = Undo.GetCurrentGroup();
            m_AdjustRotations = m_Session.SourceFrames();
            m_AdjustPreview = new SceneBoneAdjustPreview(m_Session, m_Renderer, m_Source, m_AdjustRotations);
            m_Adjust = true;
            m_AdjustPulseStart = EditorApplication.timeSinceStartup;
            UpdateUI(); SceneView.RepaintAll();
        }

        void UpdateAdjustPulse()
        {
            if (m_AdjustPulse == null) return;
            var active = m_Adjust && rootVisualElement.Q<Button>("adjust").enabledInHierarchy;
            m_AdjustPulse.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            m_AdjustPulse.style.opacity = active ? .5f - .5f * Mathf.Cos((float)((EditorApplication.timeSinceStartup - m_AdjustPulseStart) * Math.PI)) : 0;
        }

        void HandleAdjust()
        {
            var bone = recreateTarget;
            if (bone == null) return;
            var toWorld = m_Session.SkeletonToWorld(m_Renderer, m_Source.rect.rect);
            var position = toWorld.MultiplyPoint3x4(bone.position);
            var right = toWorld.MultiplyVector(bone.right).normalized;
            var forward = Vector3.Cross(right, toWorld.MultiplyVector(bone.up)).normalized;
            using (new Handles.DrawingScope(Color.white, Matrix4x4.identity))
            {
                var radius = HandleUtility.GetHandleSize(position) * .65f;
                Handles.color = Color.red; Handles.DrawLine(position, position + right * radius);
                Handles.color = Color.green; Handles.DrawLine(position, position + toWorld.MultiplyVector(bone.up).normalized * radius);
                Handles.color = Color.white;
                EditorGUI.BeginChangeCheck();
                var worldRotation = Quaternion.LookRotation(forward, Vector3.Cross(forward, right));
                var rotation = Handles.Disc(worldRotation, position, forward, radius, false, 0);
                var moved = Handles.PositionHandle(position, worldRotation);
                if (EditorGUI.EndChangeCheck())
                {
                    var direction = toWorld.inverse.MultiplyVector(rotation * Vector3.right);
                    m_Session.AdjustPose(bone, toWorld.inverse.MultiplyPoint3x4(moved),
                        Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg));
                    RefreshAdjustPreview();
                }
            }
        }

        UndoPropertyModification[] OnAdjustInspectorModifications(UndoPropertyModification[] modifications)
        {
            if (!m_Adjust || m_Saving || m_AdjustPreview == null || m_AdjustPreview.updating) return modifications;
            try
            {
                if (m_AdjustPreview.ReadInspectorChanges(modifications.Where(item => item.currentValue != null && item.currentValue.target is Transform &&
                    (item.currentValue.propertyPath?.StartsWith("m_LocalPosition", StringComparison.Ordinal) == true ||
                    item.currentValue.propertyPath?.StartsWith("m_LocalRotation", StringComparison.Ordinal) == true))
                    .Select(item => item.currentValue.target as Transform)))
                {
                    UpdateUI(); SceneView.RepaintAll();
                }
            }
            catch (Exception exception) { m_Error = exception.Message; Debug.LogError("Adjust: " + exception.Message); }
            return modifications;
        }

        void RefreshAdjustPreview()
        {
            if (!m_Adjust || m_AdjustPreview == null) return;
            try { m_AdjustPreview.Update(); m_Error = null; }
            catch (Exception exception) { m_Error = exception.Message; }
        }

        void CancelAdjust()
        {
            if (!m_Adjust) return;
            m_AdjustPreview?.Restore(); m_AdjustPreview = null;
            m_Adjust = false;
            m_Session.cache.EndUndoOperation();
            Undo.RevertAllDownToGroup(m_AdjustUndoGroup);
            m_Session.RestoreAfterUndo();
            m_AdjustRotations = null; m_AdjustUndoGroup = -1;
        }

        void HandleRecreate(Event evt, int control)
        {
            if (evt.type == EventType.MouseDown && evt.button == 0 && GUIUtility.hotControl == 0)
            {
                m_Controller.Reset();
                m_RecreatingBone = recreateTarget;
                m_RecreateStart = m_View.GetMouseWorldPosition(m_RecreatingBone.forward, m_RecreatingBone.position);
                m_RecreateEnd = m_RecreateStart;
                m_RecreateControl = control;
                GUIUtility.hotControl = control;
                evt.Use();
            }
            else if (m_RecreatingBone != null && evt.button == 0 &&
                (evt.type == EventType.MouseDrag || evt.type == EventType.MouseUp))
            {
                m_RecreateEnd = m_View.GetMouseWorldPosition(m_RecreatingBone.forward, m_RecreateStart);
                if (evt.type == EventType.MouseUp)
                {
                    var bone = m_RecreatingBone;
                    CancelRecreate();
                    Apply(bone, m_RecreateStart, m_RecreateEnd);
                }
                evt.Use();
            }
            if (evt.type == EventType.Repaint && m_RecreatingBone != null)
            {
                var end = (m_RecreateEnd - m_RecreateStart).sqrMagnitude < .0001f ? m_RecreateStart + Vector3.right * .01f : m_RecreateEnd;
                var color = m_RecreatingBone.bindPoseColor;
                if (m_RecreatingBone.iconId == BoneIconUtility.Default)
                {
                    BoneDrawingUtility.DrawBone(m_RecreateStart, end, m_RecreatingBone.forward, color, color);
                    BoneDrawingUtility.DrawBoneNode(m_RecreateStart, m_RecreatingBone.forward, color);
                }
                else BoneIconUtility.Draw(m_RecreateStart, m_RecreatingBone.iconId, color, iconSize: m_RecreatingBone.iconSize);
            }
        }

        void CancelRecreate()
        {
            if (m_RecreateControl != 0 && GUIUtility.hotControl == m_RecreateControl) GUIUtility.hotControl = 0;
            m_RecreatingBone = null;
            m_RecreateControl = 0;
        }

        void DeleteSelection()
        {
            var bones = m_Session.cache.skeletonSelection.elements.Where(bone => bone != null && !bone.IsConstraintParent()).ToArray();
            if (bones.Length == 0) return;
            if (!ConfirmBoneRemoval(bones)) return;
            using (m_Session.cache.UndoScope("Delete Bones"))
            {
                ConstraintsTool.RemoveConstraintsForBones(bones);
                m_Session.skeleton.DestroyBones(bones);
                m_Session.cache.skeletonSelection.Clear();
                m_Session.Changed(true);
            }
            UpdateUI(); SceneView.RepaintAll();
        }

        bool ConfirmBoneRemoval(BoneCache[] bones) => SceneBoneDeletionGuard.Confirm(m_Session.cache, bones,
            message => EditorUtility.DisplayDialog("Delete Weighted Bones", message, "Delete Bones", "Cancel"));

        void CancelMode()
        {
            CancelRecreate();
            CancelAdjust();
            m_Controller?.Reset(); m_Mode = 0;
            m_Session.cache?.skeletonSelection.Clear();
            UpdateUI(); SceneView.RepaintAll();
        }

        void RestoreTool()
        {
            if (toolScope.Restore()) SceneView.RepaintAll();
        }

        internal bool RequestClose()
        {
            if (m_Saving) return false;
            if (m_Session == null || m_Session.cache == null) { Close(); return true; }
            rootVisualElement.focusController?.focusedElement?.Blur();
            if (m_Adjust || m_Session.dirty)
            {
                if (!Apply()) return false;
            }
            hasUnsavedChanges = false; Close(); return true;
        }

        bool Apply(BoneCache recreatedBone = null, Vector3 start = default, Vector3 end = default)
        {
            if (m_Saving) return false;
            m_Saving = true; rootVisualElement.SetEnabled(false);
            try
            {
                var blocked = BlockReason();
                if (blocked != null) throw new InvalidOperationException(blocked);
                m_Session.cache.EndUndoOperation();
                if (m_Adjust) m_AdjustPreview?.Restore();
                var source = new SceneMeshSource(m_Renderer.sprite);
                if (m_Adjust)
                {
                    m_Session.SaveAdjusted(source, m_Renderer, m_AdjustRotations, m_AdjustUndoGroup);
                    m_AdjustPreview = null;
                    m_Adjust = false; m_AdjustRotations = null; m_AdjustUndoGroup = -1;
                }
                else if (recreatedBone != null) m_Session.RecreateBoneAndSave(recreatedBone, start, end, m_Renderer, source);
                else m_Session.Save(source);
                m_Source = new SceneMeshSource(m_Renderer.sprite);
                m_Session.sourceFingerprint = SceneBoneSession.Fingerprint(m_Source);
                m_Error = null; return true;
            }
            catch (Exception ex) { m_Error = ex.Message; EditorUtility.DisplayDialog("Edit Bones", ex.Message, "OK"); return false; }
            finally { m_Saving = false; rootVisualElement.SetEnabled(true); RefreshAdjustPreview(); UpdateUI(); SceneView.RepaintAll(); }
        }

        public override void SaveChanges() { if (!Apply()) throw new InvalidOperationException(m_Error); base.SaveChanges(); }
        public override void DiscardChanges() { m_DiscardOnClose = true; base.DiscardChanges(); }

        void OnUndoRedo()
        {
            if (m_Saving || m_Session.cache == null) return;
            CancelRecreate();
            m_Session.RestoreAfterUndo(); m_Controller?.Reset(); RefreshAdjustPreview(); UpdateUI(); SceneView.RepaintAll();
        }

        void OnSelectionChanged()
        {
            if (m_Saving || m_CloseQueued || m_EntrySelection == null || Selection.objects.SequenceEqual(m_EntrySelection)) return;
            m_CloseQueued = true;
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                m_CloseQueued = false;
                if (!Selection.objects.SequenceEqual(m_EntrySelection) && !RequestClose()) Selection.objects = m_EntrySelection;
            };
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode) return;
            if (!RequestClose()) EditorApplication.isPlaying = false;
        }
    }
}
