using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    internal sealed partial class SceneEditWeightsWindow : SceneEditWindow
    {
        [SerializeField] SceneWeightSession m_Session;
        [SerializeField] SpriteRenderer m_Renderer;
        bool m_DiscardOnClose;
        [SerializeField] UnityEngine.Object[] m_EntrySelection;
        [SerializeField] bool m_RemoveMode, m_Direct, m_Bind, m_Overlay, m_Selected, m_Preview;
        [SerializeField] bool m_Pieces = true;
        [SerializeField] float m_Strength = 100, m_Size = 80, m_Feather = 60;
        [SerializeField] bool m_SettingsLoaded;
        Rect m_PreferredRect;
        string m_LastPreferences;
        [Serializable] sealed class WindowPreferences
        {
            public Rect rect;
            public float strength = 100, size = 80, feather = 60;
            public bool direct, remove, pieces = true, overlay, selected, preview;
        }
        static string PreferencesKey => "Unity.2D.Animation.SceneEditWeights." + Application.dataPath;

        void LoadWindowPreferences()
        {
            try
            {
                var json = EditorPrefs.GetString(PreferencesKey, string.Empty);
                if (string.IsNullOrEmpty(json)) return;
                var settings = JsonUtility.FromJson<WindowPreferences>(json);
                if (settings == null) return;
                if (float.IsFinite(settings.strength)) m_Strength = Mathf.Clamp(settings.strength, 0, 100);
                if (float.IsFinite(settings.size)) m_Size = Mathf.Clamp(settings.size, 1, 500);
                if (float.IsFinite(settings.feather)) m_Feather = Mathf.Clamp(settings.feather, 0, 100);
                m_Direct = settings.direct; m_RemoveMode = settings.remove;
                m_Pieces = settings.pieces; m_Overlay = settings.overlay; m_Selected = settings.selected;
                m_Preview = settings.preview;
                var rect = settings.rect;
                if (float.IsFinite(rect.x) && float.IsFinite(rect.y) && float.IsFinite(rect.width) && float.IsFinite(rect.height) && rect.width > 0 && rect.height > 0)
                    m_PreferredRect = rect;
            }
            catch (ArgumentException) { }
        }

        void SaveWindowPreferences()
        {
            if (!m_SettingsLoaded || position.width < minSize.x || position.height < minSize.y) return;
            var json = JsonUtility.ToJson(new WindowPreferences
            {
                rect = position, strength = m_Strength, size = m_Size, feather = m_Feather,
                direct = m_Direct, remove = m_RemoveMode, pieces = m_Pieces, overlay = m_Overlay, selected = m_Selected, preview = m_Preview
            });
            if (json == m_LastPreferences) return;
            EditorPrefs.SetString(PreferencesKey, json); m_LastPreferences = json;
        }
        SceneMeshSource m_Source;
        bool m_Saving, m_SelectionRestoreQueued;
        string m_Error, m_RowKey;
        PopupField<string> m_ModePopup;
        VisualElement m_BindPulse;
        Button m_BindButton;
        bool m_BindPulseActive;
        double m_BindPulseStart;
        BoneCache m_DragBone;
        Button m_DragBoneName;
        int m_DragBonePointer;
        float m_DragBoneStartY;
        int m_DropBoneIndex = -1;
        readonly List<(BoneCache bone, VisualElement row, Button swatch, Slider slider)> m_Rows = new List<(BoneCache, VisualElement, Button, Slider)>();

        internal static bool IsEditingRenderer(SpriteRenderer renderer) => renderer != null && Resources.FindObjectsOfTypeAll<SceneEditWeightsWindow>().Any(window => window.m_Renderer == renderer);
        internal static bool IsEditingAsset(string path) => Resources.FindObjectsOfTypeAll<SceneEditWeightsWindow>().Any(window => window.m_Session?.context?.assetPath == path);
        internal string editingAssetPath => m_Session?.context?.assetPath;
        internal static bool HasActiveSceneHost => Resources.FindObjectsOfTypeAll<SceneEditWeightsWindow>().Any(window => !window.m_Saving && window.BlockReason() == null);

        internal static void OpenForRenderer(SpriteRenderer renderer)
        {
            SceneEditWeightsWindow opening = null;
            try
            {
                if (renderer == null || renderer.sprite == null || !renderer.gameObject.scene.IsValid())
                    throw new InvalidOperationException("Select a scene SpriteRenderer with an authored mesh.");
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Weight editing is available in Edit Mode.");
                if (SkinningModule.IsEditingAsset(UniskinRigUtility.SourcePath(renderer.sprite)))
                    throw new InvalidOperationException("Close Skinning Editor for this asset before opening Edit Weights.");
                foreach (var window in Resources.FindObjectsOfTypeAll<SceneEditMeshWindow>()) if (!window.RequestClose()) return;
                foreach (var window in Resources.FindObjectsOfTypeAll<SceneEditBoneWindow>()) if (!window.RequestClose()) return;
                foreach (var window in Resources.FindObjectsOfTypeAll<SceneEditWeightsWindow>())
                {
                    if (window.m_Renderer == renderer) { window.Focus(); return; }
                    if (!window.RequestClose()) return;
                }
                var source = new SceneMeshSource(renderer.sprite);
                opening = CreateInstance<SceneEditWeightsWindow>();
                opening.m_Renderer = renderer;
                opening.m_EntrySelection = Selection.objects;
                opening.m_Source = source;
                opening.m_Session.Load(renderer.sprite, source);
                opening.m_Session.context.CaptureScenePose(renderer, source.rect.rect);
                var blocked = opening.BlockReason();
                if (blocked != null) throw new InvalidOperationException(blocked);
                var origin = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.position.position + new Vector2(35, 55) : new Vector2(100, 100);
                opening.position = new Rect(origin, new Vector2(370, 444) / Mathf.Max(1, EditorGUIUtility.pixelsPerPoint));
                if (opening.m_PreferredRect.width > 0)
                {
                    var rect = opening.m_PreferredRect;
                    rect.width = Mathf.Max(opening.minSize.x, rect.width); rect.height = Mathf.Max(opening.minSize.y, rect.height);
                    opening.position = rect;
                }
                opening.ShowPopup(); opening.Focus(); SceneView.RepaintAll();
            }
            catch (Exception ex)
            {
                if (opening != null) DestroyImmediate(opening);
                EditorUtility.DisplayDialog("Edit Weights", ex.Message, "OK");
            }
        }

        void OnEnable()
        {
            if (!EnableWindowLifetime(m_Renderer != null || m_Session != null)) return;
            titleContent = new GUIContent("Edit Weights");
            minSize = new Vector2(370, 354) / Mathf.Max(1, EditorGUIUtility.pixelsPerPoint);
            if (!m_SettingsLoaded) { LoadWindowPreferences(); m_SettingsLoaded = true; }
            if (m_Session == null) { m_Session = CreateInstance<SceneWeightSession>(); m_Session.hideFlags = HideFlags.HideAndDontSave; }
            m_Session.Attach();
            if (m_Renderer != null && m_Renderer.sprite != null)
                try { m_Source = new SceneMeshSource(m_Renderer.sprite); } catch (Exception ex) { m_Error = ex.Message; }
            Selection.selectionChanged += OnSelectionChanged;
            SceneView.beforeSceneGui += BeforeSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        void OnDisable()
        {
            DisableWindowLifetime();
            EndGesture();
            if (!isClosingForReload && !isRestoredWindow && !m_DiscardOnClose && !m_Saving && !EditorApplication.isPlaying && m_Source != null &&
                m_Renderer != null && m_Renderer.sprite != null && m_Session?.context?.cache != null)
            {
                try { if (m_Session.dirty) m_Session.Save(new SceneMeshSource(m_Renderer.sprite)); }
                catch (Exception exception) { Debug.LogError("Edit Weights auto-save failed: " + exception.Message); }
            }
            SaveWindowPreferences();
            ClearBoneDrop();
            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.beforeSceneGui -= BeforeSceneGUI;
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EndGesture(); RestoreTool();
            if (m_OverlayMesh != null) DestroyImmediate(m_OverlayMesh);
            if (m_OverlayMaterial != null) DestroyImmediate(m_OverlayMaterial);
        }
        void OnDestroy() { if (m_Session != null) DestroyImmediate(m_Session); }

        public void CreateGUI()
        {
            if (isRestoredWindow) return;
            rootVisualElement.Clear(); m_Rows.Clear(); m_RowKey = null;
            ResourceLoader.Load<VisualTreeAsset>("SkinningModule/SceneEditWeightsWindow.uxml").CloneTree(rootVisualElement);
            SceneMeshWindowStyle.Configure(rootVisualElement, this, () => RequestClose(), resizeHeight: true);
            var mode = new PopupField<string>("Mode", new List<string> { "Direct", "Add", "Remove" }, m_Direct ? 0 : m_RemoveMode ? 2 : 1);
            m_ModePopup = mode;
            mode.name = "weightMode"; rootVisualElement.Q("mode").Add(mode);
            mode.RegisterValueChangedCallback(evt =>
            {
                EndGesture(); m_Direct = evt.newValue == "Direct"; m_RemoveMode = evt.newValue == "Remove";
                rootVisualElement.Q<Slider>("size").SetEnabled(!m_Direct);
                rootVisualElement.Q<Slider>("feather").SetEnabled(!m_Direct);
                SaveWindowPreferences();
                SceneView.RepaintAll();
            });
            Slider("strength", m_Strength, value => m_Strength = value);
            Slider("size", m_Size, value => m_Size = value);
            Slider("feather", m_Feather, value => m_Feather = value);
            rootVisualElement.Q<Slider>("size").SetEnabled(!m_Direct);
            rootVisualElement.Q<Slider>("feather").SetEnabled(!m_Direct);
            Option("pieces", m_Pieces, value => m_Pieces = value);
            Option("overlay", m_Overlay, value => m_Overlay = value);
            Option("selected", m_Selected, value => m_Selected = value);
            Option("preview", m_Preview, value => m_Preview = value);
            Command("smooth", "Smooth", () => Run(m_Session.Smooth));
            Command("auto", "Auto", () => Run(m_Session.Auto));
            Command("prune", "Prune", ShowPrune);
            Command("weld", "Weld", ShowWeld);
            rootVisualElement.Q<Button>("weld").tooltip = "Copy weights into this mesh from another character mesh. Select the source and All or Overlapping. Requires two common bound bones; preserves locked weights.";
            Command("bind", "Bind", () => { EndGesture(); m_Bind = !m_Bind; UpdateUI(); SceneView.RepaintAll(); });
            m_BindButton = rootVisualElement.Q<Button>("bind");
            m_BindPulse = new VisualElement { name = "bindPulse", pickingMode = PickingMode.Ignore };
            m_BindButton.Insert(0, m_BindPulse);
            m_BindPulseActive = false;
            rootVisualElement.schedule.Execute(UpdateBindPulse).Every(16);
            Command("swap", "Swap", () => Run(m_Session.Swap));
            Command("remove", "Delete", () => Run(m_Session.Remove));
            Command("updateBindings", "UpdateBind", () => Run(UpdateBindings));
            var titleIcon = rootVisualElement.Q<Image>("weightsIcon");
            titleIcon.image = ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/Weights.png");
            rootVisualElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                Cancel(); evt.imguiEvent?.Use(); evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            rootVisualElement.schedule.Execute(UpdateUI).Every(200);
            rootVisualElement.schedule.Execute(SaveWindowPreferences).Every(250);
            rootVisualElement.Q<Button>("swap").RegisterCallback<GeometryChangedEvent>(_ => AlignBoneSliders());
            rootVisualElement.Q<ScrollView>("bones").contentContainer.RegisterCallback<GeometryChangedEvent>(_ => AlignBoneSliders());
            UpdateUI();
        }

        static void SizeWeightSlider(Slider slider)
        {
            var height = 20f / Mathf.Max(1, EditorGUIUtility.pixelsPerPoint);
            // Unity's horizontal-slider selectors can override the track height in USS.
            // Pin the actual containers and numeric field to the same physical-pixel height.
            slider.style.height = height;
            foreach (var className in new[] { "unity-base-field__input", "unity-base-slider__drag-container", "unity-base-slider__tracker", "unity-base-slider__text-field" })
            {
                var element = slider.Q(className: className);
                if (element == null) continue;
                element.style.height = element.style.minHeight = element.style.maxHeight = height;
                element.style.marginTop = element.style.marginBottom = 0;
                element.style.paddingTop = element.style.paddingBottom = 0;
            }
            var container = slider.Q(className: "unity-base-slider__drag-container");
            container.style.alignSelf = Align.Stretch;
            container.style.marginLeft = container.style.marginRight = 0;
            var tracker = slider.Q(className: "unity-base-slider__tracker");
            tracker.style.top = tracker.style.bottom = tracker.style.left = tracker.style.right = 0;
        }

        void AlignBoneSliders()
        {
            var swap = rootVisualElement.Q<Button>("swap");
            if (swap == null || !float.IsFinite(swap.worldBound.x)) return;
            var scale = Mathf.Max(1, EditorGUIUtility.pixelsPerPoint);
            foreach (var item in m_Rows)
            {
                // Row inset + swatch + name gap precede the name; its remaining width
                // puts the slider's left edge exactly at the Swap button's left edge.
                var width = Mathf.Max(20 / scale, swap.worldBound.x - item.row.worldBound.x - 28 / scale);
                var name = item.row.Q<Button>(className: "weight-bone-name");
                if (Mathf.Abs(name.resolvedStyle.width - width) > .1f) name.style.width = width;
            }
        }

        void Slider(string name, float initial, Action<float> change)
        {
            var slider = rootVisualElement.Q<Slider>(name);
            SizeWeightSlider(slider);
            slider.SetValueWithoutNotify(initial);
            var lastValue = initial;
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("mesh-slider-fill");
            slider.Q(className: "unity-base-slider__tracker").Add(fill);
            void Refresh() => fill.style.width = Length.Percent((slider.value - slider.lowValue) * 100f / (slider.highValue - slider.lowValue));
            slider.RegisterValueChangedCallback(evt =>
            {
                if (!float.IsFinite(evt.newValue)) { slider.SetValueWithoutNotify(lastValue); return; }
                lastValue = Mathf.Clamp(evt.newValue, slider.lowValue, slider.highValue);
                slider.SetValueWithoutNotify(lastValue);
                change(lastValue); Refresh(); SaveWindowPreferences(); SceneView.RepaintAll();
            });
            Refresh();
        }

        void Option(string name, bool initial, Action<bool> change)
        {
            var toggle = rootVisualElement.Q<Toggle>(name); toggle.SetValueWithoutNotify(initial);
            toggle.RegisterValueChangedCallback(evt => { change(evt.newValue); SaveWindowPreferences(); SceneView.RepaintAll(); });
        }

        void Command(string name, string icon, Action action)
        {
            var button = rootVisualElement.Q<Button>(name);
            var label = new Label(button.text) { pickingMode = PickingMode.Ignore }; button.text = string.Empty;
            button.Add(new Image { name = "commandIcon", image = ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/" + icon + ".png"), pickingMode = PickingMode.Ignore });
            button.Add(label); button.clicked += action;
        }

        void Run(Action operation)
        {
            if (BlockReason() != null || m_Saving) return;
            EndGesture();
            try { operation(); m_Error = null; }
            catch (Exception ex) { m_Error = ex.Message; EditorUtility.DisplayDialog("Edit Weights", ex.Message, "OK"); }
            UpdateUI(); SceneView.RepaintAll();
        }

        void ShowWeld()
        {
            if (BlockReason() != null || m_Saving) return;
            EndGesture();
            var menu = new GenericMenu();
            var sources = m_Session.weldSources;
            for (var i = 0; i < sources.Length; ++i)
            {
                var source = sources[i];
                // Numbered names keep duplicate layer names distinct and slashes out of menu paths.
                var label = (i + 1) + ". " + source.name.Replace('/', '\u2215');
                menu.AddItem(new GUIContent(label + "/Overlapping"), false, () => Run(() => m_Session.Weld(source, true)));
                menu.AddItem(new GUIContent(label + "/All"), false, () => Run(() => m_Session.Weld(source, false)));
            }
            if (sources.Length == 0)
                menu.AddDisabledItem(new GUIContent("Requires another character mesh with two common bound bones"));
            menu.ShowAsContext();
        }

        void ShowPrune()
        {
            if (BlockReason() != null) return;
            PruneWeightsWindow.ShowWindow((max, threshold) => this != null && m_Session != null ? m_Session.CountPruned(max, threshold) : 0,
                (max, threshold) => { if (this != null && m_Session != null) Run(() => m_Session.Prune(max, threshold)); }, sceneStyle: true);
        }

        void RebuildRows()
        {
            var bones = m_Session.orderedBoneIndices.Select(i => m_Session.mesh.bones[i]).ToArray();
            var key = string.Join("|", bones.Select(bone => bone.guid + ":" + bone.name));
            if (key == m_RowKey) return;
            m_RowKey = key; m_Rows.Clear();
            var list = rootVisualElement.Q<ScrollView>("bones"); list.Clear();
            foreach (var bone in bones)
            {
                var row = new VisualElement(); row.AddToClassList("weight-bone-row");
                var swatch = new Button(() => { m_Session.ToggleLock(bone); UpdateUI(); }) { tooltip = "Lock/unlock this bone's weights" };
                var scale = Mathf.Max(1, EditorGUIUtility.pixelsPerPoint);
                row.style.paddingLeft = row.style.paddingRight = 4 / scale;
                swatch.style.width = swatch.style.height = swatch.style.minWidth = swatch.style.minHeight = 20 / scale;
                swatch.AddToClassList("weight-swatch"); row.Add(swatch);
                swatch.Add(new Image { name = "weightLock", image = WeightInspector.LockTexture, pickingMode = PickingMode.Ignore });
                var name = new Button { text = bone.name, tooltip = "Select bone. Ctrl/Shift adds to selection. Drag to change this mesh's draw order; lower rows draw later." }; name.AddToClassList("weight-bone-name");
                name.style.marginLeft = 4 / scale;
                name.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    m_Session.SelectBone(bone, evt.ctrlKey || evt.commandKey || evt.shiftKey); UpdateUI(); SceneView.RepaintAll();
                    m_DragBone = bone; m_DragBoneStartY = evt.position.y; m_DropBoneIndex = -1;
                    m_DragBoneName = name; m_DragBonePointer = evt.pointerId;
                    name.CapturePointer(evt.pointerId);
                    evt.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
                name.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (m_DragBone != bone || !name.HasPointerCapture(evt.pointerId) || (evt.pressedButtons & 1) == 0 ||
                        Mathf.Abs(evt.position.y - m_DragBoneStartY) < 4) return;
                    m_DropBoneIndex = m_Rows.FindIndex(item => evt.position.y < item.row.worldBound.center.y);
                    if (m_DropBoneIndex < 0) m_DropBoneIndex = m_Rows.Count;
                    for (var i = 0; i < m_Rows.Count; ++i)
                    {
                        m_Rows[i].row.EnableInClassList("weight-drop-before", i == m_DropBoneIndex);
                        m_Rows[i].row.EnableInClassList("weight-drop-after", m_DropBoneIndex == m_Rows.Count && i == m_Rows.Count - 1);
                    }
                    var viewport = list.contentViewport.worldBound;
                    var scroll = evt.position.y < viewport.yMin + 18 ? -8 : evt.position.y > viewport.yMax - 18 ? 8 : 0;
                    if (scroll != 0) list.scrollOffset = new Vector2(list.scrollOffset.x, Mathf.Clamp(list.scrollOffset.y + scroll, 0, list.verticalScroller.highValue));
                    evt.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
                name.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (evt.button != 0 || m_DragBone != bone) return;
                    var drop = m_DropBoneIndex; ClearBoneDrop();
                    if (drop >= 0) Run(() => m_Session.MoveBone(bone, drop));
                    evt.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
                name.RegisterCallback<PointerCaptureOutEvent>(_ => ClearBoneDrop());
                row.Add(name);
                var slider = new Slider(0, 100) { showInputField = true };
                SizeWeightSlider(slider);
                slider.AddToClassList("weight-row-slider");
                var fill = new VisualElement { pickingMode = PickingMode.Ignore }; fill.AddToClassList("mesh-slider-fill");
                fill.style.backgroundColor = WeightColor(bone); slider.Q(className: "unity-base-slider__tracker").Add(fill);
                slider.RegisterValueChangedCallback(evt =>
                {
                    var index = Array.IndexOf(m_Session.mesh.bones, bone);
                    if (index >= 0) Run(() => m_Session.SetRow(index, evt.newValue / 100));
                });
                row.Add(slider); list.Add(row); m_Rows.Add((bone, row, swatch, slider));
            }
        }

        void ClearBoneDrop()
        {
            m_DragBone = null; m_DropBoneIndex = -1;
            var name = m_DragBoneName; m_DragBoneName = null;
            if (name != null && name.HasPointerCapture(m_DragBonePointer)) name.ReleasePointer(m_DragBonePointer);
            foreach (var item in m_Rows)
            {
                item.row.RemoveFromClassList("weight-drop-before");
                item.row.RemoveFromClassList("weight-drop-after");
            }
        }

        void UpdateUI()
        {
            if (m_Saving || rootVisualElement.Q("commands") == null) return;
            var blocked = BlockReason();
            rootVisualElement.Q("commands").SetEnabled(blocked == null);
            UpdateBindPulse();
            rootVisualElement.Q<Label>("status").text = blocked ?? m_Error ?? (m_Bind ? "Click bones to bind. Esc exits Bind." : m_Direct ? "Ctrl+click/drag selects vertices. Drag a vertex to edit weights." : "Ctrl+click/drag selects vertices. Alt temporarily removes weights in Add.");
            if (blocked != null) { RestoreTool(); return; }
            RebuildRows();
            AlignBoneSliders();
            var selected = m_Session.selectedBones; var locked = m_Session.lockedBones;
            var controller = new SpriteMeshDataController { spriteMeshData = m_Session.mesh };
            for (var i = 0; i < m_Rows.Count; ++i)
            {
                var row = m_Rows[i];
                var boneIndex = Array.IndexOf(m_Session.mesh.bones, row.bone);
                row.row.EnableInClassList("weight-bone-selected", selected.Contains(boneIndex));
                row.swatch.style.backgroundColor = WeightColor(row.bone);
                row.swatch.Q<Image>("weightLock").style.display = locked.Contains(boneIndex) ? DisplayStyle.Flex : DisplayStyle.None;
                controller.GetMultiEditBoneWeightData(m_Session.vertices, boneIndex, out var weight, out var mixed);
                var focused = rootVisualElement.panel?.focusController.focusedElement as VisualElement;
                if (focused == null || !row.slider.Contains(focused)) row.slider.SetValueWithoutNotify(weight * 100);
                row.slider.showMixedValue = mixed;
                row.slider.SetEnabled(!locked.Contains(boneIndex) && !m_Bind);
                row.slider.Q(className: "mesh-slider-fill").style.width = Length.Percent(weight * 100);
            }
            rootVisualElement.Q<Button>("bind").EnableInClassList("mesh-active", m_Bind);
            rootVisualElement.Q<Button>("swap").SetEnabled(selected.Length == 2 && !selected.Any(i => locked.Contains(i)));
            rootVisualElement.Q<Button>("remove").SetEnabled(selected.Length > 0);
            rootVisualElement.Q<Button>("auto").SetEnabled(m_Session.mesh.boneCount > 0);
            rootVisualElement.Q<Button>("smooth").SetEnabled(m_Session.mesh.boneCount > 0);
            rootVisualElement.Q<Button>("updateBindings").SetEnabled(m_Session.mesh.boneCount > 0 && !m_Bind);
            hasUnsavedChanges = false; // Closing saves directly instead of prompting.
        }

        static Color WeightColor(BoneCache bone) => BoneColorUtility.GetWeightMapColor(bone.ToSpriteSheetIfNeeded());

        void UpdateBindPulse()
        {
            if (m_BindPulse == null || m_BindButton == null) return;
            var active = m_Bind && m_BindButton.enabledInHierarchy;
            if (active && !m_BindPulseActive) m_BindPulseStart = EditorApplication.timeSinceStartup;
            m_BindPulseActive = active;
            m_BindPulse.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            // A two-second cosine cycle starts at zero and fades smoothly through 1 back to 0.
            m_BindPulse.style.opacity = active
                ? .5f - .5f * Mathf.Cos((float)((EditorApplication.timeSinceStartup - m_BindPulseStart) * Math.PI))
                : 0;
        }

        string BlockReason()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Weight editing is available in Edit Mode.";
            if (m_Renderer == null || m_Renderer.sprite == null || m_Session?.context?.cache == null) return "The entry SpriteRenderer is unavailable.";
            if (m_Session.cache.selectedSprite == null) return "The staged Sprite is unavailable. Reopen Edit Weights.";
            var mesh = m_Session.mesh;
            if (mesh.vertices.Length != mesh.vertexCount || mesh.vertexWeights.Length != mesh.vertexCount)
                return "The staged mesh is incomplete. Reopen Edit Weights.";
            if (m_Source == null) return m_Error ?? "Reopen Edit Weights to load the importer.";
            if (UniskinRigUtility.SourcePath(m_Renderer.sprite) != m_Session.context.assetPath || m_Renderer.sprite.GetSpriteID().ToString() != m_Session.context.spriteId)
                return "The renderer's Sprite changed. Reopen Edit Weights.";
            if (m_Renderer.drawMode != SpriteDrawMode.Simple) return "Set SpriteRenderer Draw Mode to Simple.";
            if (Mathf.Abs(m_Renderer.transform.lossyScale.x * m_Renderer.transform.lossyScale.y) < .000001f) return "The renderer needs non-zero X/Y scale.";
            if (SkinningModule.IsEditingAsset(m_Source.assetPath)) return "Close Skinning Editor for this asset before continuing.";
            return null;
        }

        internal bool RequestClose()
        {
            if (m_Saving) return false;
            rootVisualElement.focusController?.focusedElement?.Blur();
            EndGesture();
            if (m_Session?.context?.cache != null && m_Session.dirty)
            {
                if (!Apply()) return false;
            }
            hasUnsavedChanges = false; Close(); return true;
        }

        bool Apply()
        {
            if (m_Saving) return false;
            EndGesture(); m_Saving = true; rootVisualElement.SetEnabled(false);
            try
            {
                var blocked = BlockReason(); if (blocked != null) throw new InvalidOperationException(blocked);
                m_Session.Save(new SceneMeshSource(m_Renderer.sprite));
                m_Source = new SceneMeshSource(m_Renderer.sprite); m_Error = null; return true;
            }
            catch (Exception ex) { m_Error = ex.Message; EditorUtility.DisplayDialog("Edit Weights", ex.Message, "OK"); return false; }
            finally { m_Saving = false; rootVisualElement.SetEnabled(true); UpdateUI(); SceneView.RepaintAll(); }
        }

        public override void SaveChanges() { if (!Apply()) throw new InvalidOperationException(m_Error); base.SaveChanges(); }
        public override void DiscardChanges() { m_DiscardOnClose = true; base.DiscardChanges(); }
        void OnUndoRedo() { EndGesture(); ClearBoneDrop(); m_Session.InvalidateDrawOrder(); m_Session.context?.RestoreAfterUndo(); m_RowKey = null; UpdateUI(); SceneView.RepaintAll(); }
        void RestoreTool()
        {
            if (m_HidesRenderer)
            {
                if (m_Renderer != null) m_Renderer.forceRenderingOff = m_PreviousForceRenderingOff;
                m_HidesRenderer = false;
            }
            if (toolScope.Restore()) SceneView.RepaintAll();
        }
        void OnSelectionChanged()
        {
            if (m_Saving || m_SelectionRestoreQueued || m_Renderer == null || m_EntrySelection == null || Selection.objects.SequenceEqual(m_EntrySelection)) return;
            m_SelectionRestoreQueued = true;
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                try
                {
                    if (m_Saving || m_Renderer == null) return;
                    var pinned = m_EntrySelection.Where(item => item != null).ToArray();
                    if (pinned.Length == 0) pinned = new UnityEngine.Object[] { m_Renderer.gameObject };
                    if (!Selection.objects.SequenceEqual(pinned)) Selection.objects = pinned;
                }
                finally { m_SelectionRestoreQueued = false; }
            };
        }
        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode && !RequestClose()) EditorApplication.isPlaying = false;
        }
    }
}
