using System;
using System.Linq;
using UnityEditor.UIElements;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    internal sealed class SceneEditBoneWindow : SceneEditWindow
    {
        [SerializeField] SceneBoneSession m_Session;
        [SerializeField] SpriteRenderer m_Renderer;
        [SerializeField] UnityEngine.Object[] m_EntrySelection;
        [SerializeField] int m_Mode;
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
            try
            {
                var source = new SceneMeshSource(renderer.sprite);
                // Ordinary Sprites retain their existing per-sprite skeleton entry.
                var characterProvider = source.provider.GetDataProvider<ICharacterDataProvider>();
                var character = characterProvider != null && characterProvider.GetCharacterData().bones != null;
                OpenForRenderer(renderer, boneGuid, skeletonOnly: character);
            }
            catch (Exception ex) { EditorUtility.DisplayDialog("Edit Bones", ex.Message, "OK"); }
        }

        internal static void OpenForRenderer(SpriteRenderer renderer, string boneGuid = null, bool skeletonOnly = false)
        {
            SceneEditBoneWindow opening = null;
            try
            {
                if (renderer == null || renderer.sprite == null || !renderer.gameObject.scene.IsValid())
                    throw new InvalidOperationException("Select a SpriteRenderer with an imported Sprite in the scene.");
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Bone editing is available in Edit Mode.");
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
                        if (boneGuid != null)
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
                if (boneGuid != null) window.SelectBone(boneGuid);
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
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += UpdateScenePose;
        }

        void SetupController()
        {
            if (m_Session.cache == null) return;
            if (m_Renderer != null && m_Source != null && !m_Session.scenePoseCaptured)
                m_Session.CaptureScenePose(m_Renderer, m_Source.rect.rect);
            m_View = new SkeletonView(new SceneBoneGUI());
            m_Controller = new SkeletonController
            {
                view = m_View, selection = m_Session.cache.skeletonSelection,
                skeleton = m_Session.skeleton, editBindPose = true
            };
        }

        void OnDisable()
        {
            DisableWindowLifetime();
            if (!isClosingForReload && !isRestoredWindow && !m_DiscardOnClose && !m_Saving && !EditorApplication.isPlaying && m_Source != null &&
                m_Renderer != null && m_Renderer.sprite != null && m_Session != null && m_Session.cache != null)
            {
                try { if (m_Session.dirty) m_Session.Save(new SceneMeshSource(m_Renderer.sprite)); }
                catch (Exception exception) { Debug.LogError("Edit Bones auto-save failed: " + exception.Message); }
            }
            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.beforeSceneGui -= BeforeSceneGUI;
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
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
            rootVisualElement.Q<FloatField>("boneLength").RegisterValueChangedCallback(evt => EditProperty("Change Bone Length", bone =>
            {
                if (float.IsFinite(evt.newValue)) bone.length = Mathf.Max(0, evt.newValue);
            }, false));
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
            button.clicked += () => { m_Controller?.Reset(); m_Mode = m_Mode == mode ? 0 : mode; UpdateUI(); SceneView.RepaintAll(); };
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
            if (m_Saving || rootVisualElement.Q("commands") == null) return;
            var blocked = BlockReason();
            rootVisualElement.Q("commands").SetEnabled(blocked == null);
            rootVisualElement.Q("commands").tooltip = blocked ?? m_Error;
            foreach (var pair in new[] { ("create", 1), ("split", 2), ("setParent", 3) })
                rootVisualElement.Q<Button>(pair.Item1).EnableInClassList("mesh-active", m_Mode == pair.Item2);
            var selected = m_Session.cache != null ? m_Session.cache.skeletonSelection.elements.Where(bone => bone != null).ToArray() : Array.Empty<BoneCache>();
            var color = rootVisualElement.Q<ColorField>("boneColor");
            var name = rootVisualElement.Q<TextField>("boneName");
            name.SetEnabled(selected.Length == 1 && !selected[0].IsConstraintParent());
            var length = rootVisualElement.Q<FloatField>("boneLength");
            length.SetEnabled(selected.Length == 1);
            color.SetEnabled(selected.Length > 0);
            // Poll viewport edits without replacing partially typed text.
            var focused = rootVisualElement.panel?.focusController.focusedElement as VisualElement;
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
            if (m_Saving || BlockReason() != null || m_Controller == null) return;
            if (Event.current.type == EventType.Layout) m_Session.RefreshScenePose(m_Renderer, m_Source.rect.rect);
            toolScope.Hide();
            var evt = Event.current;
            if (evt.alt || evt.button == 1 || evt.button == 2) return;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape) { CancelMode(); evt.Use(); return; }
            if (evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace))
            { DeleteSelection(); evt.Use(); return; }
            var primaryDown = evt.type == EventType.MouseDown && evt.button == 0;
            var child = m_Session.cache.skeletonSelection.activeElement;
            var control = GUIUtility.GetControlID("UniskinSceneBone".GetHashCode(), FocusType.Passive);
            if (evt.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            using (new Handles.DrawingScope(Color.white, m_Session.SkeletonToWorld(m_Renderer, m_Source.rect.rect)))
            {
                m_View.defaultControlID = control;
                m_View.mode = m_Mode == 1 ? SkeletonMode.CreateBone : m_Mode == 2 ? SkeletonMode.SplitBone : m_Mode == 3 ? SkeletonMode.Selection : SkeletonMode.EditJoints;
                m_Controller.suppressBoneSelection = m_Mode == 3 && child != null;
                m_Controller.OnGUI();
                if (primaryDown && m_Mode == 3 && child != null)
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
            if (EditorApplication.timeSinceStartup < m_NextPoseRefresh || m_Saving || BlockReason() != null) return;
            m_NextPoseRefresh = EditorApplication.timeSinceStartup + 1.0 / 30.0;
            if (m_Session.RefreshScenePose(m_Renderer, m_Source.rect.rect)) SceneView.RepaintAll();
        }

        void DeleteSelection()
        {
            var bones = m_Session.cache.skeletonSelection.elements.Where(bone => bone != null && !bone.IsConstraintParent()).ToArray();
            if (bones.Length == 0) return;
            using (m_Session.cache.UndoScope("Delete Bones"))
            {
                ConstraintsTool.RemoveConstraintsForBones(bones);
                m_Session.skeleton.DestroyBones(bones);
                m_Session.cache.skeletonSelection.Clear();
                m_Session.Changed(true);
            }
            UpdateUI(); SceneView.RepaintAll();
        }

        void CancelMode()
        {
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
            rootVisualElement.focusController?.focusedElement?.Blur();
            if (m_Session.dirty)
            {
                if (!Apply()) return false;
            }
            hasUnsavedChanges = false; Close(); return true;
        }

        bool Apply()
        {
            if (m_Saving) return false;
            m_Saving = true; rootVisualElement.SetEnabled(false);
            try
            {
                var blocked = BlockReason();
                if (blocked != null) throw new InvalidOperationException(blocked);
                m_Session.cache.EndUndoOperation();
                var source = new SceneMeshSource(m_Renderer.sprite);
                m_Session.Save(source);
                m_Source = new SceneMeshSource(m_Renderer.sprite);
                m_Session.sourceFingerprint = SceneBoneSession.Fingerprint(m_Source);
                m_Error = null; return true;
            }
            catch (Exception ex) { m_Error = ex.Message; EditorUtility.DisplayDialog("Edit Bones", ex.Message, "OK"); return false; }
            finally { m_Saving = false; rootVisualElement.SetEnabled(true); UpdateUI(); SceneView.RepaintAll(); }
        }

        public override void SaveChanges() { if (!Apply()) throw new InvalidOperationException(m_Error); base.SaveChanges(); }
        public override void DiscardChanges() { m_DiscardOnClose = true; base.DiscardChanges(); }

        void OnUndoRedo()
        {
            if (m_Saving || m_Session.cache == null) return;
            m_Session.RestoreAfterUndo(); m_Controller?.Reset(); UpdateUI(); SceneView.RepaintAll();
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
