using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    internal sealed class SceneEditMeshWindow : EditorWindow
    {
        [SerializeField] SceneMeshSession m_Session;
        [SerializeField] SpriteRenderer m_Renderer;
        [SerializeField] bool m_Triangles = true;
        [SerializeField] bool m_Dim;
        [SerializeField] bool m_Isolate;
        [SerializeField] float m_Detail = 0.5f;
        [SerializeField] int m_Alpha = 10;
        [SerializeField] bool m_TraceActive;
        [SerializeField] string m_TraceBackup;
        [SerializeField] float m_TraceEntryDetail;
        [SerializeField] int m_TraceEntryAlpha;
        [SerializeField] int m_TraceTransactionUndoGroup = -1;
        [SerializeField] UnityEngine.Object[] m_SelectionObjects;
        bool m_SelectionCloseQueued;
        SceneMeshSource m_Source;
        SpriteMeshView m_View;
        SpriteMeshController m_Controller;
        Tool m_PreviousTool;
        bool m_OwnsTool;
        SpriteRenderer m_PreviewRenderer;
        bool m_PreviousForceRenderingOff;
        readonly List<GameObject> m_HiddenObjects = new List<GameObject>();
        Mesh m_ImageMesh;
        Material m_ImageMaterial;
        string m_Error;
        bool m_BoxSelecting;
        Vector2 m_BoxStart;
        int[] m_BoxSelection;
        int m_Control;
        int m_AppliedUndoGroup = -1;
        int m_TraceUndoGroup = -1;
        bool m_TraceInProgress;
        bool m_MeshMutationInProgress;
        string m_LastImporterFingerprint;

        sealed class SceneGUIWrapper : GUIWrapper
        {
            public override float DistanceToCircle(Vector3 center, float radius)
            {
                return Mathf.Max(0f, Vector2.Distance(Event.current.mousePosition, HandleUtility.WorldToGUIPoint(center)) - radius);
            }
        }

        [MenuItem("Window/Fedorov/Uniskin/Edit Mesh")]
        internal static void Open()
        {
            var renderer = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<SpriteRenderer>() : null;
            OpenForRenderer(renderer, false);
        }

        internal static bool IsEditingRenderer(SpriteRenderer renderer) => renderer != null &&
            Resources.FindObjectsOfTypeAll<SceneEditMeshWindow>().Any(window => window.m_Renderer == renderer);

        internal static void OpenForRenderer(SpriteRenderer renderer, bool createMesh)
        {
            if (renderer == null || renderer.sprite == null)
            {
                EditorUtility.DisplayDialog("Edit Mesh", "Select a SpriteRenderer with an imported Sprite.", "OK");
                return;
            }
            SceneEditMeshWindow openingWindow = null;
            try
            {
                var source = new SceneMeshSource(renderer.sprite);
                if (!createMesh && !source.hasAuthoredMesh && !IsEditingRenderer(renderer))
                    throw new InvalidOperationException("Use Create Mesh in the SpriteRenderer Inspector first.");
                foreach (var existing in Resources.FindObjectsOfTypeAll<SceneEditMeshWindow>())
                {
                    if (existing.m_Renderer == renderer && !existing.docked)
                    {
                        existing.Focus();
                        return;
                    }
                    if (!existing.RequestClose()) return;
                }
                // Saving the previous session may have reimported this same asset.
                source = new SceneMeshSource(renderer.sprite);
                var window = CreateInstance<SceneEditMeshWindow>();
                openingWindow = window;
                window.m_Renderer = renderer;
                window.m_Source = source;
                window.m_SelectionObjects = Selection.objects;
                window.m_Session.Load(renderer.sprite, source);
                var blocked = window.BlockReason();
                if (blocked != null) throw new InvalidOperationException(blocked);
                if (createMesh && !source.hasAuthoredMesh) window.m_Session.CreateMesh();
                window.m_LastImporterFingerprint = source.Fingerprint();
                var size = new Vector2(340, 190) / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
                var origin = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.position.position + new Vector2(35, 55) : new Vector2(100, 100);
                window.position = new Rect(origin, size);
                window.ShowPopup();
                window.Focus();
                SceneView.RepaintAll();
            }
            catch (Exception ex)
            {
                if (openingWindow != null) DestroyImmediate(openingWindow);
                EditorUtility.DisplayDialog("Edit Mesh", ex.Message, "OK");
            }
        }

        bool RequestClose()
        {
            if (m_Source != null && m_Session.dirty)
            {
                var choice = EditorUtility.DisplayDialogComplex("Unsaved Sprite Mesh",
                    "Save mesh changes to the original Sprite asset before leaving Edit Mesh?", "Save", "Cancel", "Revert");
                if (choice == 1 || (choice == 0 && !Apply())) return false;
            }
            // Discard only staged data. The importer has not changed until Apply.
            hasUnsavedChanges = false;
            Close();
            return true;
        }

        void OnEnable()
        {
            titleContent = new GUIContent("Edit Mesh");
            minSize = new Vector2(340, 50) / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
            if (m_Session == null)
            {
                m_Session = CreateInstance<SceneMeshSession>();
                m_Session.hideFlags = HideFlags.HideAndDontSave;
            }
            if (m_TraceActive && string.IsNullOrEmpty(m_TraceBackup)) m_TraceActive = false;
            m_View = new SpriteMeshView(new SceneGUIWrapper()) { useSceneHandles = true };
            m_Controller = new SpriteMeshController
            {
                spriteMeshView = m_View, selection = m_Session.selection,
                cacheUndo = m_Session, triangulator = new Triangulator(),
                newGeometryCanceled = () => m_Session.CancelNew(),
                newGeometryCompleted = () => { m_Session.mode = 0; m_Session.newMeshBackup = null; }
            };
            Selection.selectionChanged += OnSelectionChanged;
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += PollAltInput;
            if (m_Renderer != null && m_Renderer.sprite != null)
                RefreshSource();
        }

        void OnDisable()
        {
            EndTraceGesture();
            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.update -= PollAltInput;
            RestoreScene();
            if (m_ImageMesh != null) DestroyImmediate(m_ImageMesh);
            if (m_ImageMaterial != null) DestroyImmediate(m_ImageMaterial);
        }

        void OnDestroy()
        {
            if (m_Session != null) DestroyImmediate(m_Session);
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var template = ResourceLoader.Load<VisualTreeAsset>("SkinningModule/SceneEditMeshWindow.uxml");
            if (template == null)
            {
                rootVisualElement.Add(new Label("Edit Mesh UI could not be loaded. Reimport the Animation package."));
                return;
            }
            template.CloneTree(rootVisualElement);
            SceneMeshWindowStyle.Configure(rootVisualElement, this, () => RequestClose());
            Bind("modify", () => SetMode(0));
            Bind("create", () => SetMode(1));
            Bind("delete", () => SetMode(2));
            Bind("newMesh", NewMesh);
            Bind("reset", () => { ExitTrace(); Mutate("Reset Sprite Mesh", () =>
            {
                m_Session.mesh.Clear();
                DataController().CreateQuad();
                Triangulate();
            }, true); });
            Bind("generate", () => { ExitTrace(); Mutate("Generate Sprite Mesh Vertices", Generate, true); });
            Bind("trace", () => { if (EnterTrace()) Trace(false); });
            Bind("traceOK", () => { ExitTrace(); SceneView.RepaintAll(); UpdateUI(); });
            Bind("traceCancel", CancelTrace);
            rootVisualElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (!m_TraceActive || evt.keyCode != KeyCode.Escape) return;
                CancelTrace();
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            BindToggle("triangles", m_Triangles, value => m_Triangles = value);
            BindToggle("dim", m_Dim, value => m_Dim = value);
            BindToggle("isolate", m_Isolate, value => { m_Isolate = value; RestoreVisibility(); });
            var detail = rootVisualElement.Q<Slider>("detail");
            detail.SetValueWithoutNotify(m_Detail);
            detail.RegisterValueChangedCallback(evt => { m_Detail = evt.newValue; Trace(true); });
            var alpha = rootVisualElement.Q<SliderInt>("alpha");
            alpha.SetValueWithoutNotify(m_Alpha);
            alpha.RegisterValueChangedCallback(evt => { m_Alpha = evt.newValue; Trace(true); });
            foreach (var slider in new VisualElement[] { detail, alpha })
            {
                slider.RegisterCallback<PointerDownEvent>(_ => BeginTraceGesture(), TrickleDown.TrickleDown);
                slider.RegisterCallback<PointerUpEvent>(_ => EndTraceGesture(), TrickleDown.TrickleDown);
                slider.RegisterCallback<FocusOutEvent>(_ => EndTraceGesture());
            }
            rootVisualElement.schedule.Execute(UpdateUI).Every(200);
            UpdateUI();
        }

        void Bind(string name, Action action)
        {
            var button = rootVisualElement.Q<Button>(name);
            button.clicked += action;
            if (button.parent[button.parent.childCount - 1] == button) button.AddToClassList("mesh-last-action");
            var iconName = name == "newMesh" ? "New" : char.ToUpperInvariant(name[0]) + name.Substring(1);
            var texture = ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/" + iconName + ".png");
            var label = new Label(button.text) { pickingMode = PickingMode.Ignore };
            button.text = string.Empty;
            if (texture != null) button.Add(new Image { image = texture, pickingMode = PickingMode.Ignore, name = "commandIcon" });
            button.Add(label);
        }
        void BindToggle(string name, bool value, Action<bool> action)
        {
            var toggle = rootVisualElement.Q<Toggle>(name);
            toggle.SetValueWithoutNotify(value);
            toggle.RegisterValueChangedCallback(evt => { action(evt.newValue); SceneView.RepaintAll(); });
        }

        string BlockReason()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Mesh editing is available in Edit Mode.";
            if (m_Renderer == null || m_Renderer.sprite == null || m_Source == null)
                return m_Error ?? "Select a GameObject with a SpriteRenderer in the Hierarchy.";
            if (m_Renderer.sprite.GetSpriteID().ToString() != m_Session.spriteId || AssetDatabase.GetAssetPath(m_Renderer.sprite) != m_Session.assetPath)
                return "The renderer's Sprite changed. Select another object, then select this sprite again to reload.";
            if (Mathf.Abs(m_Renderer.transform.lossyScale.x) < 0.000001f || Mathf.Abs(m_Renderer.transform.lossyScale.y) < 0.000001f)
                return "The sprite needs non-zero X and Y scale for mesh editing.";
            if (m_Renderer.drawMode != SpriteDrawMode.Simple) return "Set SpriteRenderer Draw Mode to Simple to edit its asset mesh.";
            if (SkinningModule.IsEditingAsset(m_Source.assetPath))
                return "Close Skinning Editor for this asset before editing here. Apply here, then reopen Skinning Editor to see the same mesh.";
            return null;
        }

        void UpdateUI()
        {
            if (rootVisualElement.Q<VisualElement>("commands") == null) return;
            var blocked = BlockReason();
            rootVisualElement.Q<VisualElement>("mainControls").SetEnabled(blocked == null);
            foreach (var name in new[] { "detail", "alpha", "traceOK" })
                rootVisualElement.Q(name).SetEnabled(blocked == null);
            rootVisualElement.Q<VisualElement>("commands").tooltip = blocked;
            rootVisualElement.Q("traceSettings").tooltip = m_Error ?? blocked;
            rootVisualElement.Q("mainControls").EnableInClassList("mesh-hidden", m_TraceActive);
            rootVisualElement.Q("traceSettings").EnableInClassList("mesh-hidden", !m_TraceActive);
            rootVisualElement.Q<Label>("windowTitle").text = m_TraceActive ? "Edit Mesh: Trace" : "Edit Mesh";
            var effectiveMode = EffectiveMode();
            foreach (var pair in new[] { ("modify", 0), ("create", 1), ("delete", 2), ("newMesh", 3) })
                rootVisualElement.Q<Button>(pair.Item1).EnableInClassList("mesh-active", !m_TraceActive && effectiveMode == pair.Item2);
            rootVisualElement.Q<Button>("trace").EnableInClassList("mesh-active", m_TraceActive);
            hasUnsavedChanges = m_Source != null && m_Session.dirty;
            saveChangesMessage = "Apply mesh changes to the original Sprite asset? All instances will use this mesh.";
            rootVisualElement.Q<Label>("vertexCount").text = $"Vertices: {m_Session.mesh.vertexCount}    Triangles: {m_Session.mesh.indices.Length / 3}";
            if (blocked != null) RestoreScene();
        }

        int EffectiveMode() => !m_TraceActive && SkinningEditorInput.altKeyDown &&
            (m_Session.mode == 0 || m_Session.mode == 1) ? 1 - m_Session.mode : m_Session.mode;

        void PollAltInput()
        {
            if (!SkinningEditorInput.Update(null)) return;
            UpdateUI();
            SceneView.RepaintAll();
        }

        void OnSelectionChanged()
        {
            if (m_Renderer == null || m_SelectionCloseQueued ||
                (m_SelectionObjects != null && Selection.objects.SequenceEqual(m_SelectionObjects))) return;
            m_SelectionCloseQueued = true;
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                m_SelectionCloseQueued = false;
                if (m_SelectionObjects != null && Selection.objects.SequenceEqual(m_SelectionObjects)) return;
                if (!RequestClose() && m_SelectionObjects != null) Selection.objects = m_SelectionObjects;
            };
        }

        void RefreshSource()
        {
            try
            {
                m_Source = new SceneMeshSource(m_Renderer.sprite);
                m_LastImporterFingerprint = m_Source.Fingerprint();
            }
            catch (Exception ex) { m_Source = null; m_Error = ex.Message; }
        }

        void OnUndoRedo()
        {
            m_TraceUndoGroup = -1;
            m_TraceTransactionUndoGroup = -1;
            if (m_Session.hasBoneWeights) ExitTrace();
            if (m_Renderer == null || m_Renderer.sprite == null) return;
            // Importer Undo changes serialized data; reimport regenerates every Sprite instance.
            if (m_AppliedUndoGroup >= 0 && new SceneMeshSource(m_Renderer.sprite).Fingerprint() != m_LastImporterFingerprint)
                AssetDatabase.ImportAsset(m_Session.assetPath, ImportAssetOptions.ForceUpdate);
            RefreshSource();
            m_Session.EndUndoOperation();
            m_BoxSelecting = false;
            SceneView.RepaintAll();
            UpdateUI();
        }

        public override void SaveChanges()
        {
            if (!Apply()) throw new InvalidOperationException(m_Error ?? "Complete the mesh before saving.");
            base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            // Closing discards the in-memory session; no importer write needs rollback.
            base.DiscardChanges();
        }

        bool Apply()
        {
            try
            {
                if (BlockReason() != null) throw new InvalidOperationException(BlockReason());
                if (m_Session.mode == 3) m_Session.CompleteNew();
                var source = new SceneMeshSource(m_Renderer.sprite);
                if (source.Fingerprint() != m_Session.sourceMesh)
                    throw new InvalidOperationException("The Sprite asset changed since editing began. Close this window and choose Revert, then reopen the mesh from the Inspector to load the current asset.");
                SceneMeshSource.Validate(m_Session.mesh);
                ExitTrace();
                Undo.IncrementCurrentGroup();
                m_AppliedUndoGroup = Undo.GetCurrentGroup();
                Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { source.importer, m_Session }, "Apply Sprite Mesh");
                source.WriteMesh(m_Session.mesh);
                m_Session.pendingCreation = false;
                m_Session.savedMesh = JsonUtility.ToJson(m_Session.mesh);
                RefreshSource();
                m_Session.sourceMesh = m_Source.Fingerprint();
                Undo.CollapseUndoOperations(m_AppliedUndoGroup);
                m_Error = null;
                SceneView.RepaintAll();
                UpdateUI();
                return true;
            }
            catch (Exception ex) { ReportError(ex); return false; }
        }

        void Revert()
        {
            if (m_Renderer == null || m_Renderer.sprite == null) return;
            try
            {
                RefreshSource();
                if (m_Source == null) return;
                m_Session.BeginUndoOperation("Revert Sprite Mesh");
                m_Session.Load(m_Renderer.sprite, m_Source);
                m_Session.EndUndoOperation();
                m_Error = null;
                if (!m_Source.hasAuthoredMesh) { hasUnsavedChanges = false; Close(); return; }
                SceneView.RepaintAll();
                UpdateUI();
            }
            catch (Exception ex) { ReportError(ex); }
        }

        void ReportError(Exception exception)
        {
            m_Error = exception.Message;
            UpdateUI();
            EditorUtility.DisplayDialog("Edit Mesh", m_Error, "OK");
        }

        void SetMode(int mode)
        {
            ExitTrace();
            if (m_Session.mode == 3)
            {
                Mutate("Complete Sprite Mesh Hull", () => { }, false);
                return;
            }
            m_Session.mode = mode;
            m_Session.selection.Clear();
            SceneView.RepaintAll();
            UpdateUI();
        }

        void NewMesh()
        {
            ExitTrace();
            if (m_Session.mode == 3) { SetMode(0); return; }
            if (!ConfirmWeightLoss()) return;
            m_Session.BeginNew();
            m_Error = null;
            SceneView.RepaintAll();
            UpdateUI();
        }

        bool ConfirmWeightLoss()
        {
            return !HasWeights() || EditorUtility.DisplayDialog("Replace weighted geometry?", "This operation removes this Sprite's mesh weights. Existing bones are preserved.", "Continue", "Cancel");
        }

        bool HasWeights() => m_Session.hasBoneWeights;

        bool EnterTrace()
        {
            if (m_TraceInProgress || BlockReason() != null) return false;
            if (m_TraceActive) return true;
            m_TraceInProgress = true;
            try
            {
                if (HasWeights() && !EditorUtility.DisplayDialog("Trace mesh weights",
                    "This Sprite has bone weights. Trace replaces its geometry and removes those weights. Cancel in Trace restores the original mesh and weights. Existing bones are preserved.",
                    "Enter Trace", "Cancel")) return false;
                m_View.CancelCreateEdgeDrag();
                if (m_BoxSelecting && GUIUtility.hotControl == m_Control) GUIUtility.hotControl = 0;
                m_BoxSelecting = false;
                m_Session.EndUndoOperation();
                m_TraceBackup = EditorJsonUtility.ToJson(m_Session);
                m_TraceEntryDetail = m_Detail;
                m_TraceEntryAlpha = m_Alpha;
                Undo.IncrementCurrentGroup();
                m_TraceTransactionUndoGroup = Undo.GetCurrentGroup();
                m_TraceActive = true;
                UpdateUI();
                return true;
            }
            finally { m_TraceInProgress = false; }
        }

        void ExitTrace()
        {
            EndTraceGesture();
            if (m_TraceTransactionUndoGroup >= 0)
            {
                Undo.CollapseUndoOperations(m_TraceTransactionUndoGroup);
                Undo.IncrementCurrentGroup();
                m_TraceTransactionUndoGroup = -1;
            }
            m_TraceActive = false;
            m_TraceBackup = null;
        }

        void CancelTrace()
        {
            if (!m_TraceActive || m_TraceInProgress) return;
            EndTraceGesture();
            if (!string.IsNullOrEmpty(m_TraceBackup))
            {
                m_Session.EndUndoOperation();
                m_Session.BeginUndoOperation("Cancel Sprite Mesh Trace");
                EditorJsonUtility.FromJsonOverwrite(m_TraceBackup, m_Session);
                m_Session.EndUndoOperation();
                m_Detail = m_TraceEntryDetail;
                m_Alpha = m_TraceEntryAlpha;
            }
            ExitTrace();
            rootVisualElement.Q<Slider>("detail").value = m_Detail;
            rootVisualElement.Q<SliderInt>("alpha").value = m_Alpha;
            m_Error = null;
            SceneView.RepaintAll();
            UpdateUI();
        }

        SpriteMeshDataController DataController() => new SpriteMeshDataController { spriteMeshData = m_Session.mesh };
        void Triangulate()
        {
            var controller = DataController();
            controller.Triangulate(new Triangulator());
            controller.SortTrianglesByDepth();
        }

        void Generate()
        {
            m_Session.GenerateInteriorVertices();
        }

        void BeginTraceGesture()
        {
            if (!m_TraceActive || m_TraceUndoGroup >= 0) return;
            Undo.IncrementCurrentGroup();
            m_TraceUndoGroup = Undo.GetCurrentGroup();
        }

        void EndTraceGesture()
        {
            if (m_TraceUndoGroup < 0) return;
            Undo.CollapseUndoOperations(m_TraceUndoGroup);
            m_TraceUndoGroup = -1;
        }

        void Trace(bool live)
        {
            if (!m_TraceActive || m_TraceInProgress || BlockReason() != null) return;
            m_TraceInProgress = true;
            try
            {
                Mutate("Trace Sprite Mesh", () =>
                {
                    if (m_Source.textureProvider?.texture == null)
                        throw new InvalidOperationException("The importer does not provide a texture for tracing.");
                    DataController().OutlineFromAlpha(new SceneMeshOutlineGenerator(), m_Source.textureProvider, m_Detail, (byte)m_Alpha);
                    if (m_Session.mesh.vertexCount < 3)
                        throw new InvalidOperationException("No contour found. Lower the alpha threshold.");
                    Triangulate();
                }, false, !live);
            }
            finally { m_TraceInProgress = false; }
        }

        void Mutate(string name, Action action, bool clearsWeights, bool showErrorDialog = true, bool keepMode = false)
        {
            if (m_MeshMutationInProgress || BlockReason() != null) return;
            if (clearsWeights && !ConfirmWeightLoss()) return;
            var backup = EditorJsonUtility.ToJson(m_Session);
            m_Session.BeginUndoOperation(name);
            m_MeshMutationInProgress = true;
            try
            {
                if (m_Session.mode == 3) m_Session.CompleteNew();
                action();
                SceneMeshSource.Validate(m_Session.mesh);
                m_Session.selection.Clear();
                if (!keepMode) m_Session.mode = 0;
                m_Error = null;
            }
            catch (Exception ex)
            {
                EditorJsonUtility.FromJsonOverwrite(backup, m_Session);
                // Live slider values can temporarily produce an empty contour.
                // Retain the last valid mesh and allow the gesture to continue.
                if (showErrorDialog) ReportError(ex);
                else m_Error = ex.Message;
            }
            finally { m_Session.EndUndoOperation(); m_MeshMutationInProgress = false; }
            SceneView.RepaintAll();
            UpdateUI();
        }

        void OnPlayModeChanged(PlayModeStateChange state)
        {
            RestoreScene();
            UpdateUI();
        }

        void RestoreVisibility()
        {
            foreach (var obj in m_HiddenObjects)
                if (obj != null) SceneVisibilityManager.instance.Show(obj, false);
            m_HiddenObjects.Clear();
        }

        void RestoreScene()
        {
            m_View?.CancelCreateEdgeDrag();
            RestoreVisibility();
            if (m_PreviewRenderer != null) m_PreviewRenderer.forceRenderingOff = m_PreviousForceRenderingOff;
            m_PreviewRenderer = null;
            if (m_OwnsTool && UnityEditor.Tools.current == Tool.None) UnityEditor.Tools.current = m_PreviousTool;
            m_OwnsTool = false;
            m_BoxSelecting = false;
            SceneView.RepaintAll();
        }

        void OnSceneGUI(SceneView scene)
        {
            if (m_TraceInProgress || m_MeshMutationInProgress) return;
            if (BlockReason() != null) return;
            var evt = Event.current;
            if (m_TraceActive && evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                CancelTrace();
                evt.Use();
                return;
            }
            if (SkinningEditorInput.Update(evt))
            {
                UpdateUI();
                scene.Repaint();
            }
            if (m_Renderer.sprite.GetSpriteID().ToString() != m_Session.spriteId || AssetDatabase.GetAssetPath(m_Renderer.sprite) != m_Session.assetPath)
            {
                RestoreScene();
                m_Error = "The renderer's Sprite changed. Select another object, then select this sprite again to reload.";
                return;
            }
            if (!m_OwnsTool)
            {
                m_PreviousTool = UnityEditor.Tools.current;
                UnityEditor.Tools.current = Tool.None;
                m_OwnsTool = true;
            }
            if (m_PreviewRenderer == null)
            {
                m_PreviewRenderer = m_Renderer;
                m_PreviousForceRenderingOff = m_Renderer.forceRenderingOff;
                m_Renderer.forceRenderingOff = true;
            }
            if (m_Isolate && m_HiddenObjects.Count == 0)
            {
                foreach (var renderer in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                    if (renderer != m_Renderer && !SceneVisibilityManager.instance.IsHidden(renderer.gameObject))
                    {
                        SceneVisibilityManager.instance.Hide(renderer.gameObject, false);
                        m_HiddenObjects.Add(renderer.gameObject);
                    }
            }
            var matrix = SceneMeshSession.PixelToWorld(m_Renderer, m_Session.mesh.frame);
            if (evt.type == EventType.Repaint) DrawImage(matrix);
            if (evt.button == 1 || evt.button == 2) return;
            var temporaryAltMode = !m_TraceActive && SkinningEditorInput.altKeyDown &&
                (m_Session.mode == 0 || m_Session.mode == 1);
            if (evt.alt && !temporaryAltMode) return;
            if (temporaryAltMode) evt.modifiers &= ~EventModifiers.Alt;
            if (m_TraceActive && evt.type != EventType.Layout && evt.type != EventType.Repaint)
            {
                bool primaryMouse = evt.button == 0 &&
                    (evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag || evt.type == EventType.MouseUp);
                bool deleteKey = evt.type == EventType.KeyDown &&
                    (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace);
                bool deleteCommand = (evt.type == EventType.ValidateCommand || evt.type == EventType.ExecuteCommand) &&
                    (evt.commandName == "Delete" || evt.commandName == "SoftDelete");
                if (primaryMouse || deleteKey || deleteCommand) evt.Use();
                return;
            }
            var effectiveMode = EffectiveMode();
            m_Control = GUIUtility.GetControlID("UniskinSceneMesh".GetHashCode(), FocusType.Passive);
            if (evt.type == EventType.Layout) HandleUtility.AddDefaultControl(m_Control);
            var backup = EditorJsonUtility.ToJson(m_Session);
            using (new Handles.DrawingScope(Color.white, matrix))
            {
                if (evt.type == EventType.Repaint && m_Triangles) DrawTriangles();
                m_View.mode = m_TraceActive ? SpriteMeshViewMode.EditGeometry :
                    effectiveMode == 3 ? SpriteMeshViewMode.NewGeometry :
                    effectiveMode == 1 ? SpriteMeshViewMode.CreateVertex : SpriteMeshViewMode.EditGeometry;
                m_View.defaultControlID = m_Control;
                m_Controller.spriteMeshData = m_Session.mesh;
                m_Controller.selection = m_Session.selection;
                m_Controller.disable = m_TraceActive || m_Session.mode == 2;
                m_Controller.frame = new Rect(Vector2.zero, m_Session.mesh.frame.size);
                EditorGUI.BeginChangeCheck();
                EditorGUI.BeginDisabledGroup(m_TraceActive);
                try
                {
                    if (!m_TraceActive && m_Session.mode == 3 && evt.type == EventType.MouseDown && evt.button == 0 && m_View.hoveredVertex == 0 && m_Session.mesh.vertexCount >= 3)
                    {
                        Mutate("Complete Sprite Mesh Hull", () => { }, false);
                        evt.Use();
                    }
                    if (!m_TraceActive) HandleDelete(effectiveMode);
                    m_Controller.OnGUI();
                    if (m_View.isCreateEdgeDragActive && evt.type == EventType.MouseDown && evt.button == 0) evt.Use();
                    if (!m_TraceActive) HandleBoxSelection();
                }
                catch (Exception ex) { EditorJsonUtility.FromJsonOverwrite(backup, m_Session); m_Error = ex.Message; GUIUtility.hotControl = 0; }
                finally { EditorGUI.EndDisabledGroup(); EditorGUI.EndChangeCheck(); }
            }
            if (evt.rawType == EventType.MouseUp || GUIUtility.hotControl == 0) m_Session.EndUndoOperation();
            if (temporaryAltMode && evt.button == 0 &&
                (evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag || evt.type == EventType.MouseUp)) evt.Use();
            hasUnsavedChanges = m_Session.dirty;
            if (evt.type != EventType.Repaint && evt.type != EventType.Layout) { scene.Repaint(); UpdateUI(); }
        }

        void HandleDelete(int effectiveMode)
        {
            var evt = Event.current;
            bool direct = evt.type == EventType.MouseDown && evt.button == 0 &&
                (effectiveMode == 2 || (effectiveMode == 0 && evt.clickCount == 2)) &&
                (m_View.hoveredVertex >= 0 || m_View.hoveredEdge >= 0);
            bool key = evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace);
            bool command = (evt.type == EventType.ExecuteCommand || evt.type == EventType.ValidateCommand) &&
                (evt.commandName == "Delete" || evt.commandName == "SoftDelete");
            if (!direct && !key && !command) return;
            if (evt.type == EventType.ValidateCommand) { evt.Use(); return; }
            // The shared controller owns New-mode deletion and boundary stitching.
            if (m_Session.mode == 3) return;
            if (direct)
            {
                if (m_View.hoveredVertex >= 0)
                {
                    m_Session.selection.Clear();
                    m_Session.selection.Select(m_View.hoveredVertex, true);
                }
                else
                {
                    var edge = m_Session.mesh.edges[m_View.hoveredEdge];
                    Mutate("Delete Sprite Mesh Edge", () => { DataController().RemoveEdge(edge); Triangulate(); }, false, keepMode: true);
                    evt.Use();
                    return;
                }
            }
            if (m_Session.selection.Count > 0)
            {
                var selected = m_Session.selection.elements;
                Mutate("Delete Sprite Mesh Selection", () =>
                {
                    if (!direct && selected.Length == 2 && m_Session.mesh.edges.ContainsAny(new Unity.Mathematics.int2(selected[0], selected[1])))
                        DataController().RemoveEdge(new Unity.Mathematics.int2(selected[0], selected[1]));
                    else
                    {
                        if (m_Session.mesh.vertexCount - selected.Length < 3)
                            throw new InvalidOperationException("Keep at least three vertices. Use New or Reset to replace the mesh.");
                        DataController().RemoveVertex(selected);
                    }
                    Triangulate();
                }, false, keepMode: true);
            }
            evt.Use();
        }

        void HandleBoxSelection()
        {
            var evt = Event.current;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && m_Session.mode != 3)
            {
                m_Session.selection.Clear(); evt.Use(); return;
            }
            if (evt.type == EventType.MouseDown && evt.button == 0 && HandleUtility.nearestControl == m_Control && m_Session.mode != 3)
            {
                m_BoxSelecting = true; m_BoxStart = evt.mousePosition;
                m_BoxSelection = EditorGUI.actionKey || evt.shift ? m_Session.selection.elements : Array.Empty<int>();
                GUIUtility.hotControl = m_Control; evt.Use();
            }
            if (!m_BoxSelecting || GUIUtility.hotControl != m_Control) return;
            var rect = Rect.MinMaxRect(Mathf.Min(m_BoxStart.x, evt.mousePosition.x), Mathf.Min(m_BoxStart.y, evt.mousePosition.y),
                Mathf.Max(m_BoxStart.x, evt.mousePosition.x), Mathf.Max(m_BoxStart.y, evt.mousePosition.y));
            if (evt.type == EventType.MouseDrag || evt.type == EventType.MouseUp)
            {
                m_Session.selection.elements = m_BoxSelection;
                for (var i = 0; i < m_Session.mesh.vertexCount; ++i)
                    if (rect.Contains(HandleUtility.WorldToGUIPoint(m_Session.mesh.vertices[i]))) m_Session.selection.Select(i, true);
                if (evt.type == EventType.MouseUp) { m_BoxSelecting = false; GUIUtility.hotControl = 0; }
                evt.Use();
            }
            if (evt.type == EventType.Repaint)
            {
                Handles.BeginGUI(); GUI.Box(rect, GUIContent.none, "SelectionRect"); Handles.EndGUI();
            }
        }

        void DrawTriangles()
        {
            var mesh = m_Session.mesh;
            Handles.color = new Color(0.7f, 0.7f, 0.7f, 0.6f);
            for (var i = 0; i + 2 < mesh.indices.Length; i += 3)
            {
                var a = mesh.vertices[mesh.indices[i]];
                var b = mesh.vertices[mesh.indices[i + 1]];
                var c = mesh.vertices[mesh.indices[i + 2]];
                Handles.DrawDottedLine(a, b, 4f); Handles.DrawDottedLine(b, c, 4f); Handles.DrawDottedLine(c, a, 4f);
            }
        }

        void DrawImage(Matrix4x4 matrix)
        {
            var textureProvider = m_Source.textureProvider;
            if (textureProvider?.texture == null) return;
            if (m_ImageMaterial == null)
            {
                m_ImageMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
                m_ImageMaterial.SetInt("_Cull", (int)CullMode.Off);
            }
            if (m_ImageMesh == null) m_ImageMesh = new Mesh { name = "Uniskin Sprite Preview", hideFlags = HideFlags.HideAndDontSave };
            var frame = m_Session.mesh.frame;
            var size = frame.size;
            textureProvider.GetTextureActualWidthAndHeight(out var width, out var height);
            Vector2 UV(float x, float y) => new Vector2((frame.x + x) / width, (frame.y + y) / height);
            m_ImageMesh.vertices = new[] { Vector3.zero, new Vector3(size.x, 0), new Vector3(size.x, size.y), new Vector3(0, size.y) };
            m_ImageMesh.uv = new[] { UV(0, 0), UV(size.x, 0), UV(size.x, size.y), UV(0, size.y) };
            m_ImageMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m_ImageMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            var color = m_Renderer.color;
            if (m_Dim) { color.r *= 0.35f; color.g *= 0.35f; color.b *= 0.35f; }
            m_ImageMaterial.mainTexture = textureProvider.texture;
            m_ImageMaterial.color = color;
            m_ImageMaterial.SetPass(0);
            Graphics.DrawMeshNow(m_ImageMesh, matrix);
        }
    }
}
