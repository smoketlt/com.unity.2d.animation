using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    internal sealed class SceneEditMeshWindow : SceneEditWindow
    {
        [SerializeField] SceneMeshSession m_Session;
        [SerializeField] SpriteRenderer m_Renderer;
        [SerializeField] bool m_Triangles = true;
        [SerializeField] bool m_Dim;
        [SerializeField] bool m_Isolate;
        [SerializeField] bool m_Deformed;
        [SerializeField] bool m_OnTop = true;
        [SerializeField] float m_Detail = 0.5f;
        [SerializeField] int m_Alpha = 10;
        [SerializeField] bool m_TraceActive;
        [SerializeField] string m_TraceBackup;
        [SerializeField] float m_TraceEntryDetail;
        [SerializeField] int m_TraceEntryAlpha;
        [SerializeField] int m_TraceTransactionUndoGroup = -1;
        [SerializeField] UnityEngine.Object[] m_SelectionObjects;
        bool m_SelectionRestoreQueued;
        SceneMeshSource m_Source;
        bool m_DiscardOnClose;
        bool m_SymmetrySourceMutated;
        SpriteMeshView m_View;
        SpriteMeshController m_Controller;
        SpriteRenderer m_PreviewRenderer;
        bool m_PreviousForceRenderingOff;
        readonly List<GameObject> m_HiddenObjects = new List<GameObject>();
        Mesh m_ImageMesh;
        Material m_ImageMaterial;
        MeshRenderer m_SortedImageRenderer;
        readonly Vector3[] m_ImageBoundsPoints = new Vector3[4];
        readonly Vector3[] m_ImageBoundsDash = new Vector3[2];
        string m_Error;
        bool m_BoxSelecting;
        Vector2 m_BoxStart;
        int[] m_BoxSelection;
        int m_Control;
        int m_PrimaryInputControl;
        readonly GUIWrapper m_InputGUI = new GUIWrapper();
        int m_AppliedUndoGroup = -1;
        int m_TraceUndoGroup = -1;
        bool m_TraceInProgress;
        bool m_MeshMutationInProgress;
        bool m_SaveInProgress;
        string m_LastImporterFingerprint;
        double m_NextPoseRepaint;
        string m_MeshClipboard;
        SpriteMeshData m_PastePreview;
        bool pasteActive => m_MeshClipboard != null;
        Vector2 m_PasteOffset;
        float m_PasteAngle;
        int m_PasteControl;
        bool m_PasteRotating;
        Vector2 m_PasteDragStart;
        Vector2 m_PasteEntryOffset;
        float m_PasteEntryAngle;
        Vector2 m_PastePivot;

        sealed class SceneGUIWrapper : GUIWrapper
        {
            public override float DistanceToCircle(Vector3 center, float radius)
            {
                return Mathf.Max(0f, Vector2.Distance(Event.current.mousePosition, HandleUtility.WorldToGUIPoint(center)) - radius);
            }
        }

        internal static bool IsEditingRenderer(SpriteRenderer renderer) => renderer != null &&
            Resources.FindObjectsOfTypeAll<SceneEditMeshWindow>().Any(window => window.m_Renderer == renderer);

        internal static bool IsEditingAsset(string path) => Resources.FindObjectsOfTypeAll<SceneEditMeshWindow>()
            .Any(window => window.m_Session != null && window.m_Session.assetPath == path);
        internal string editingAssetPath => m_Session != null ? m_Session.assetPath : null;

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
                foreach (var boneWindow in Resources.FindObjectsOfTypeAll<SceneEditBoneWindow>())
                    if (!boneWindow.RequestClose()) return;
                foreach (var weightWindow in Resources.FindObjectsOfTypeAll<SceneEditWeightsWindow>())
                    if (!weightWindow.RequestClose()) return;
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

        internal bool RequestClose()
        {
            if (m_SaveInProgress) return false;
            rootVisualElement.focusController?.focusedElement?.Blur();
            if (m_Source != null && m_Session.dirty)
            {
                if (!Apply()) return false;
            }
            hasUnsavedChanges = false;
            Close();
            return true;
        }

        void OnEnable()
        {
            if (!EnableWindowLifetime(m_Renderer != null || m_Session != null)) return;
            titleContent = new GUIContent("Edit Mesh", ResourceLoader.Load<Texture2D>("SkinningModule/SceneMeshIcons/CreateMesh.png"));
            minSize = new Vector2(340, 50) / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
            if (m_Session == null)
            {
                m_Session = CreateInstance<SceneMeshSession>();
                m_Session.hideFlags = HideFlags.HideAndDontSave;
            }
            if (m_TraceActive && string.IsNullOrEmpty(m_TraceBackup)) m_TraceActive = false;
            m_View = new SpriteMeshView(new SceneGUIWrapper()) { useSceneHandles = true };
            m_View.newGeometryClosureVertex = index => m_Session.IsNewHullClosureVertex(index);
            m_Controller = new SpriteMeshController
            {
                spriteMeshView = m_View, selection = m_Session.selection,
                cacheUndo = m_Session, triangulator = new Triangulator(),
                newGeometryCanceled = () => m_Session.CancelNew(),
                newGeometryCompleted = () => { m_Session.mode = 0; m_Session.newMeshBackup = null; },
                newGeometryCompletionRequested = () => { m_Session.CompleteNew(); m_View.mode = SpriteMeshViewMode.EditGeometry; }
            };
            Selection.selectionChanged += OnSelectionChanged;
            SceneView.beforeSceneGui += BeforeSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += PollAltInput;
            EditorApplication.update += UpdateScenePose;
            if (m_Renderer != null && m_Renderer.sprite != null)
                RefreshSource();
        }

        void OnDisable()
        {
            if (m_PasteControl != 0 && GUIUtility.hotControl == m_PasteControl) GUIUtility.hotControl = 0;
            m_PasteControl = 0;
            DisableWindowLifetime();
            EndTraceGesture();
            if (!isClosingForReload && !isRestoredWindow && !m_DiscardOnClose && !m_SaveInProgress && !EditorApplication.isPlaying && m_Source != null &&
                m_Renderer != null && m_Renderer.sprite != null && m_Session != null)
            {
                try
                {
                    if (m_Session.dirty)
                    {
                        if (m_Session.mode == 3) m_Session.FinishNew();
                        var source = new SceneMeshSource(m_Renderer.sprite);
                        if (source.Fingerprint() != m_Session.sourceMesh)
                            throw new InvalidOperationException("The source changed before the mesh window closed.");
                        Undo.RegisterCompleteObjectUndo(source.saveTarget, "Auto Save Sprite Mesh");
                        source.WriteMesh(m_Session.mesh);
                        m_Session.pendingCreation = false;
                        m_Session.savedMesh = JsonUtility.ToJson(m_Session.mesh);
                    }
                }
                catch (Exception exception) { Debug.LogError("Edit Mesh auto-save failed: " + exception.Message); }
            }
            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.beforeSceneGui -= BeforeSceneGUI;
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.update -= PollAltInput;
            EditorApplication.update -= UpdateScenePose;
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
            if (isRestoredWindow) return;
            rootVisualElement.Clear();
            var template = ResourceLoader.Load<VisualTreeAsset>("SkinningModule/SceneEditMeshWindow.uxml");
            if (template == null)
            {
                rootVisualElement.Add(new Label("Edit Mesh UI could not be loaded. Reimport the Animation package."));
                return;
            }
            template.CloneTree(rootVisualElement);
            rootVisualElement.Q(className: "uniskin-mesh-window").AddToClassList("uniskin-edit-mesh");
            var icon = rootVisualElement.Q<Image>("windowIcon");
            icon.image = titleContent.image;
            icon.style.width = icon.style.height = 16f / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
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
            Bind("copy", CopyMesh);
            Bind("paste", BeginMeshPaste);
            Bind("pasteCancel", CancelMeshPaste);
            Bind("pasteApply", ApplyMeshPaste);
            rootVisualElement.Q<Toggle>("pasteMirror").RegisterValueChangedCallback(_ => RefreshMeshPaste());
            rootVisualElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (!(evt.target is TextInputBaseField<string>) &&
                    HandleVertexClipboard(evt.keyCode, evt.ctrlKey || evt.commandKey, evt.shiftKey, evt.altKey))
                {
                    evt.StopImmediatePropagation();
                    return;
                }
                if (pasteActive && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
                {
                    ApplyMeshPaste();
                    evt.StopImmediatePropagation();
                    return;
                }
                if (evt.keyCode == KeyCode.Escape && HandleEscape())
                    evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            BindToggle("triangles", m_Triangles, value => m_Triangles = value);
            BindToggle("dim", m_Dim, value => m_Dim = value);
            BindToggle("isolate", m_Isolate, value => { m_Isolate = value; RestoreVisibility(); });
            BindToggle("deformed", m_Deformed, value => m_Deformed = value);
            BindToggle("onTop", m_OnTop, value =>
            {
                m_OnTop = value;
                if (m_SortedImageRenderer != null) m_SortedImageRenderer.enabled = !value;
            });
            BindToggle("symmetry", m_Session.symmetryActive, value => { m_Session.SetSymmetry(value); UpdateUI(); });
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
        bool CanUseMeshClipboard() => !m_SaveInProgress && !m_MeshMutationInProgress &&
            !m_TraceActive && m_Session != null && m_Session.mode != 3 &&
            GUIUtility.hotControl == 0 && BlockReason() == null;

        void CopyMesh()
        {
            if (!CanUseMeshClipboard() || pasteActive) return;
            try { EditorGUIUtility.systemCopyBuffer = m_Session.CopyMeshGeometry(); m_Error = null; }
            catch (Exception ex) { m_Error = ex.Message; }
            UpdateUI();
        }

        void BeginMeshPaste()
        {
            if (!CanUseMeshClipboard() || pasteActive) return;
            try
            {
                var clipboard = EditorGUIUtility.systemCopyBuffer;
                var preview = m_Session.PreviewMeshGeometry(clipboard, true, Vector2.zero, Vector2.one, 0);
                rootVisualElement.Q<Toggle>("pasteMirror").SetValueWithoutNotify(true);
                m_PasteOffset = Vector2.zero;

                m_PasteAngle = 0;
                m_MeshClipboard = clipboard;
                m_PastePreview = preview;
                m_Error = null;
            }
            catch (Exception ex) { m_Error = ex.Message; }
            UpdateUI();
            SceneView.RepaintAll();
        }

        void RefreshMeshPaste()
        {
            if (!pasteActive) return;
            try
            {
                m_PastePreview = m_Session.PreviewMeshGeometry(m_MeshClipboard,
                    rootVisualElement.Q<Toggle>("pasteMirror").value,
                    m_PasteOffset,
                    Vector2.one,
                    m_PasteAngle);
                m_Error = null;
            }
            catch (Exception ex) { m_Error = ex.Message; }
            UpdateUI();
            SceneView.RepaintAll();
        }

        void CancelMeshPaste()
        {
            if (m_PasteControl != 0 && GUIUtility.hotControl == m_PasteControl) GUIUtility.hotControl = 0;
            m_PasteControl = 0;
            m_MeshClipboard = null;
            m_PastePreview = null;
            m_Error = null;
            UpdateUI();
            SceneView.RepaintAll();
        }

        void ApplyMeshPaste()
        {
            if (!pasteActive || m_PastePreview == null || !CanUseMeshClipboard()) return;
            var preview = m_PastePreview;
            Mutate("Paste Sprite Mesh Geometry", () => m_Session.ApplyMeshGeometry(preview), false);
            if (m_Error == null) CancelMeshPaste();
        }

        internal static Vector2 RotatePastePoint(Vector2 point, float degrees)
        {
            float angle = degrees * Mathf.Deg2Rad;
            return new Vector2(point.x * Mathf.Cos(angle) - point.y * Mathf.Sin(angle),
                point.x * Mathf.Sin(angle) + point.y * Mathf.Cos(angle));
        }

        internal static float PasteRotation(Vector2 start, Vector2 current, Vector2 pivot) =>
            Vector2.SignedAngle(start - pivot, current - pivot);

        Vector2[] PasteCorners()
        {
            var center = m_Session.mesh.frame.size * 0.5f + m_PasteOffset;
            var local = m_PastePreview.vertices.Select(p => RotatePastePoint(p - center, -m_PasteAngle)).ToArray();
            var min = local.Aggregate(Vector2.Min);
            var max = local.Aggregate(Vector2.Max);
            return new[] { min, new Vector2(min.x, max.y), max, new Vector2(max.x, min.y) }
                .Select(p => center + RotatePastePoint(p, m_PasteAngle)).ToArray();
        }

        void HandleMeshPastePlacement(Matrix4x4 matrix)
        {
            if (m_PastePreview == null) return;
            var evt = Event.current;
            int control = GUIUtility.GetControlID("UniskinMeshPastePlacement".GetHashCode(), FocusType.Passive);
            var corners = PasteCorners();
            var screen = corners.Select(p => HandleUtility.WorldToGUIPoint(matrix.MultiplyPoint3x4(p))).ToArray();
            int corner = Array.FindIndex(screen, p => Vector2.Distance(p, evt.mousePosition) <= 12f);
            bool inside = false;
            for (int i = 0, j = 3; i < 4; j = i++)
                if ((screen[i].y > evt.mousePosition.y) != (screen[j].y > evt.mousePosition.y) &&
                    evt.mousePosition.x < (screen[j].x - screen[i].x) * (evt.mousePosition.y - screen[i].y) /
                    (screen[j].y - screen[i].y) + screen[i].x) inside = !inside;
            if (evt.type == EventType.Layout && !evt.alt)
                HandleUtility.AddControl(control, corner >= 0 || inside ? 0 : 1000);
            if (evt.type == EventType.Repaint)
            {
                using (new Handles.DrawingScope(new Color(0.3f, 0.7f, 1f), matrix))
                    Handles.DrawAAPolyLine(2f, corners.Concat(new[] { corners[0] }).Select(p => (Vector3)p).ToArray());
                Handles.BeginGUI();
                foreach (var point in screen)
                    EditorGUI.DrawRect(new Rect(point.x - 4, point.y - 4, 8, 8), new Color(0.3f, 0.7f, 1f));
                Handles.EndGUI();
            }
            if (evt.alt || evt.button != 0) return;
            var ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            var plane = new Plane(matrix.MultiplyVector(Vector3.forward).normalized, matrix.MultiplyPoint3x4(Vector3.zero));
            if (!plane.Raycast(ray, out var distance)) return;
            Vector2 pointInMesh = matrix.inverse.MultiplyPoint3x4(ray.GetPoint(distance));
            if (evt.type == EventType.MouseDown && GUIUtility.hotControl == 0 && (inside || corner >= 0))
            {
                m_PasteControl = control;
                GUIUtility.hotControl = control;
                m_PasteRotating = corner >= 0;
                m_PasteDragStart = pointInMesh;
                m_PasteEntryOffset = m_PasteOffset;
                m_PasteEntryAngle = m_PasteAngle;
                m_PastePivot = (corners[0] + corners[2]) * 0.5f;
                evt.Use();
            }
            else if (GUIUtility.hotControl == m_PasteControl && m_PasteControl != 0)
            {
                if (evt.type == EventType.MouseDrag)
                {
                    if (m_PasteRotating)
                    {
                        float delta = PasteRotation(m_PasteDragStart, pointInMesh, m_PastePivot);
                        m_PasteAngle = m_PasteEntryAngle + delta;
                        var entryCenter = m_Session.mesh.frame.size * 0.5f + m_PasteEntryOffset;
                        m_PasteOffset = m_PastePivot - m_Session.mesh.frame.size * 0.5f - RotatePastePoint(m_PastePivot - entryCenter, delta);
                    }
                    else m_PasteOffset = m_PasteEntryOffset + pointInMesh - m_PasteDragStart;
                    RefreshMeshPaste();
                    evt.Use();
                }
                else if (evt.type == EventType.MouseUp)
                {
                    GUIUtility.hotControl = 0;
                    m_PasteControl = 0;
                    evt.Use();
                }
            }
            if (corner >= 0 || inside)
                EditorGUIUtility.AddCursorRect(new Rect(evt.mousePosition - Vector2.one * 12, Vector2.one * 24),
                    corner >= 0 ? MouseCursor.RotateArrow : MouseCursor.MoveArrow);
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
            if (m_Renderer.sprite.GetSpriteID().ToString() != m_Session.spriteId || UniskinRigUtility.SourcePath(m_Renderer.sprite) != m_Session.assetPath)
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
            if (m_SaveInProgress) return;
            if (rootVisualElement.Q<VisualElement>("commands") == null) return;
            var blocked = BlockReason();
            rootVisualElement.Q<VisualElement>("mainControls").SetEnabled(blocked == null);
            foreach (var name in new[] { "detail", "alpha", "traceOK" })
                rootVisualElement.Q(name).SetEnabled(blocked == null);
            rootVisualElement.Q<VisualElement>("commands").tooltip = blocked;
            rootVisualElement.Q("traceSettings").tooltip = m_Error ?? blocked;
            rootVisualElement.Q("mainControls").EnableInClassList("mesh-hidden", m_TraceActive || pasteActive);
            rootVisualElement.Q("pasteSettings").EnableInClassList("mesh-hidden", !pasteActive);
            rootVisualElement.Q("pasteSettings").SetEnabled(blocked == null);
            rootVisualElement.Q<Button>("pasteApply").SetEnabled(blocked == null && m_PastePreview != null);
            foreach (var name in new[] { "copy", "paste" })
                rootVisualElement.Q(name).SetEnabled(CanUseMeshClipboard());
            rootVisualElement.Q("traceSettings").EnableInClassList("mesh-hidden", !m_TraceActive);
            rootVisualElement.Q<Label>("windowTitle").text = pasteActive ? "Edit Mesh: Paste" : m_TraceActive ? "Edit Mesh: Trace" : "Edit Mesh";
            var effectiveMode = EffectiveMode();
            var symmetry = rootVisualElement.Q<Toggle>("symmetry");
            symmetry.SetValueWithoutNotify(m_Session.symmetryActive);
            var symmetryTick = symmetry.Q<Label>(className: "mesh-tick");
            if (symmetryTick != null) symmetryTick.text = symmetry.value ? "\u2713" : string.Empty;
            symmetry.SetEnabled(blocked == null && !m_TraceActive);
            foreach (var pair in new[] { ("modify", 0), ("create", 1), ("delete", 2), ("newMesh", 3) })
                rootVisualElement.Q<Button>(pair.Item1).EnableInClassList("mesh-active", !m_TraceActive && effectiveMode == pair.Item2);
            rootVisualElement.Q<Button>("trace").EnableInClassList("mesh-active", m_TraceActive);
            hasUnsavedChanges = false; // RequestClose saves before closing; no Unity confirmation.
            rootVisualElement.Q<Label>("vertexCount").text = $"Vertices: {m_Session.mesh.vertexCount}    Triangles: {m_Session.mesh.indices.Length / 3}";
            var error = rootVisualElement.Q<Label>("meshClipboardError");
            error.text = m_Error;
            error.EnableInClassList("mesh-hidden", string.IsNullOrEmpty(m_Error));
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

        void UpdateScenePose()
        {
            if (!m_Deformed || m_TraceActive || m_SaveInProgress || m_MeshMutationInProgress ||
                m_Renderer == null || m_Renderer.sprite == null || EditorApplication.timeSinceStartup < m_NextPoseRepaint) return;
            m_NextPoseRepaint = EditorApplication.timeSinceStartup + 1.0 / 30.0;
            SceneView.RepaintAll();
        }

        SceneMeshDeformation Deformation() => m_Deformed && !m_TraceActive && m_Session.mode != 3 ?
            SceneMeshDeformation.Create(m_Renderer, m_Session.mesh) : null;

        void OnSelectionChanged()
        {
            if (m_SaveInProgress) return;
            if (m_Renderer == null || m_SelectionRestoreQueued || m_SelectionObjects == null ||
                Selection.objects.SequenceEqual(m_SelectionObjects)) return;
            // Scene picking (including other Scene GUI handlers) must not turn a mesh
            // authoring click into an exit request. Keep the entry renderer selected.
            m_SelectionRestoreQueued = true;
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                try
                {
                    if (m_SaveInProgress || m_Renderer == null) return;
                    var pinned = m_SelectionObjects.Where(item => item != null).ToArray();
                    if (pinned.Length == 0) pinned = new UnityEngine.Object[] { m_Renderer.gameObject };
                    if (!Selection.objects.SequenceEqual(pinned)) Selection.objects = pinned;
                }
                finally { m_SelectionRestoreQueued = false; }
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
            if (m_SaveInProgress) return;
            if (pasteActive) CancelMeshPaste();
            m_TraceUndoGroup = -1;
            m_TraceTransactionUndoGroup = -1;
            if (m_Session.hasBoneWeights) ExitTrace();
            if (m_Renderer == null || m_Renderer.sprite == null) return;
            // Importer Undo changes serialized data; reimport regenerates every Sprite instance.
            if (m_Source?.rig == null && m_AppliedUndoGroup >= 0 && new SceneMeshSource(m_Renderer.sprite).Fingerprint() != m_LastImporterFingerprint)
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
            m_DiscardOnClose = true;
            // Closing discards the in-memory session; no importer write needs rollback.
            base.DiscardChanges();
        }

        bool Apply()
        {
            if (m_SaveInProgress) return false;
            m_SaveInProgress = true;
            rootVisualElement.SetEnabled(false);
            try
            {
                if (BlockReason() != null) throw new InvalidOperationException(BlockReason());
                if (m_Session.mode == 3) m_Session.FinishNew();
                var source = new SceneMeshSource(m_Renderer.sprite);
                if (source.Fingerprint() != m_Session.sourceMesh)
                    throw new InvalidOperationException("The Sprite asset changed since editing began. Restore the source or undo the pending edits before closing, then reopen the mesh to load the current asset.");
                SceneMeshSource.Validate(m_Session.mesh);
                ExitTrace();
                Undo.IncrementCurrentGroup();
                m_AppliedUndoGroup = Undo.GetCurrentGroup();
                Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { source.saveTarget, m_Session }, "Apply Sprite Mesh");
                source.WriteMesh(m_Session.mesh);
                m_Session.pendingCreation = false;
                m_Session.savedMesh = JsonUtility.ToJson(m_Session.mesh);
                RefreshSource();
                if (m_Source == null) throw new InvalidOperationException(m_Error ?? "Could not reload the saved Sprite mesh.");
                m_Session.sourceMesh = m_LastImporterFingerprint;
                Undo.CollapseUndoOperations(m_AppliedUndoGroup);
                m_Error = null;
                SceneView.RepaintAll();
                return true;
            }
            catch (Exception ex) { ReportError(ex); return false; }
            finally
            {
                m_SaveInProgress = false;
                rootVisualElement.SetEnabled(true);
                UpdateUI();
            }
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
                if (m_Session.mode == 3) m_Session.FinishNew();
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
            ReleasePrimaryScenePress(m_InputGUI, m_PrimaryInputControl);
            m_PrimaryInputControl = 0;
            m_View?.CancelCreateEdgeDrag();
            RestoreVisibility();
            if (m_SortedImageRenderer != null) DestroyImmediate(m_SortedImageRenderer.gameObject);
            m_SortedImageRenderer = null;
            if (m_PreviewRenderer != null) m_PreviewRenderer.forceRenderingOff = m_PreviousForceRenderingOff;
            m_PreviewRenderer = null;
            toolScope.Restore();
            m_BoxSelecting = false;
            SceneView.RepaintAll();
        }

        void BeforeSceneGUI(SceneView scene)
        {
            if (m_SaveInProgress) return;
            if (BlockReason() != null) RestoreScene();
            else
            {
                toolScope.Hide();
                var evt = Event.current;
                if (!m_OnTop && evt.type == EventType.Repaint)
                {
                    if (m_PreviewRenderer == null)
                    {
                        m_PreviewRenderer = m_Renderer;
                        m_PreviousForceRenderingOff = m_Renderer.forceRenderingOff;
                        m_Renderer.forceRenderingOff = true;
                    }
                    DrawImage(SceneMeshSession.PixelToWorld(m_Renderer, m_Session.mesh.frame), pasteActive ? null : Deformation()?.mesh);
                }
                // Consume Escape before SceneView's native object deselection.
                // duringSceneGui runs too late to receive the original key event.
                if (HandleSceneEscape(evt)) return;
                // Native inspector handles run before duringSceneGui. Reserve the
                // press now, then release it to our mesh handles in OnSceneGUI.
                int control = GUIUtility.GetControlID("UniskinSceneMeshInput".GetHashCode(), FocusType.Passive);
                var temporaryAltMode = !m_TraceActive && evt.alt && (m_Session.mode == 0 || m_Session.mode == 1);
                if ((!evt.alt && UnityEditor.Tools.current != Tool.View) || temporaryAltMode)
                    if (CapturePrimaryScenePress(m_InputGUI, control)) m_PrimaryInputControl = control;
            }
        }

        internal bool HandleSceneEscape(Event evt)
        {
            if (evt.type != EventType.KeyDown || evt.keyCode != KeyCode.Escape || !HandleEscape()) return false;
            evt.Use();
            return true;
        }

        bool HandleEscape()
        {
            if (m_Session == null || m_SaveInProgress || m_TraceInProgress || m_MeshMutationInProgress) return false;
            if (pasteActive) CancelMeshPaste();
            else if (m_TraceActive) CancelTrace();
            else
            {
                ReleasePrimaryScenePress(m_InputGUI, m_PrimaryInputControl);
                m_PrimaryInputControl = 0;
                m_View?.CancelCreateEdgeDrag();
                if (m_BoxSelecting && GUIUtility.hotControl == m_Control) GUIUtility.hotControl = 0;
                m_BoxSelecting = false;
                m_BoxSelection = Array.Empty<int>();
                if (m_Session.mode == 3)
                {
                    m_Session.CancelNew();
                    m_Error = null;
                    UpdateUI();
                }
                else m_Session.selection.Clear();
                m_Session.EndUndoOperation();
                SceneView.RepaintAll();
            }
            return true;
        }

        internal static bool CapturePrimaryScenePress(IGUIWrapper gui, int control)
        {
            if (!gui.IsMouseDown(0) || !gui.IsControlHot(0)) return false;
            gui.SetControlHot(control);
            return true;
        }

        internal static void ReleasePrimaryScenePress(IGUIWrapper gui, int control)
        {
            if (control != 0 && gui.IsControlHot(control)) gui.SetControlHot(0);
        }

        void OnSceneGUI(SceneView scene)
        {
            ReleasePrimaryScenePress(m_InputGUI, m_PrimaryInputControl);
            m_PrimaryInputControl = 0;
            if (m_TraceInProgress || m_MeshMutationInProgress || m_SaveInProgress) return;
            if (BlockReason() != null) return;
            var evt = Event.current;
            if (pasteActive && evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
            {
                if (GUIUtility.hotControl == m_PasteControl) GUIUtility.hotControl = 0;
                ApplyMeshPaste();
                evt.Use();
                return;
            }
            if (pasteActive && evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                CancelMeshPaste();
                evt.Use();
                return;
            }
            if (!EditorGUIUtility.editingTextField && GUIUtility.hotControl == 0)
            {
                bool command = evt.type == EventType.ValidateCommand || evt.type == EventType.ExecuteCommand;
                bool copy = command && evt.commandName == "Copy";
                bool paste = command && evt.commandName == "Paste";
                if ((copy || paste) && !m_TraceActive && !pasteActive && m_Session.mode != 3)
                {
                    if (evt.type == EventType.ExecuteCommand)
                        HandleVertexClipboard(copy ? KeyCode.C : KeyCode.V, true, evt.shift, false);
                    evt.Use();
                    return;
                }
                if (evt.type == EventType.KeyDown && HandleVertexClipboard(evt.keyCode, evt.control || evt.command, evt.shift, evt.alt))
                {
                    evt.Use();
                    return;
                }
            }
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
            if (m_Renderer.sprite.GetSpriteID().ToString() != m_Session.spriteId || UniskinRigUtility.SourcePath(m_Renderer.sprite) != m_Session.assetPath)
            {
                RestoreScene();
                m_Error = "The renderer's Sprite changed. Select another object, then select this sprite again to reload.";
                return;
            }
            toolScope.Hide();
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
            if (pasteActive)
            {
                if (evt.type == EventType.Repaint)
                {
                    DrawImage(matrix, null);
                    DrawImageBounds(matrix, scene);
                    if (m_PastePreview != null)
                        using (new Handles.DrawingScope(new Color(0.3f, 0.9f, 1f), matrix))
                        {
                            if (m_Triangles) DrawTriangles(m_PastePreview);
                            foreach (var edge in m_PastePreview.edges.Concat(m_PastePreview.outlineEdges))
                                Handles.DrawLine(m_PastePreview.vertices[edge.x], m_PastePreview.vertices[edge.y]);
                            foreach (var point in m_PastePreview.vertices)
                                Handles.DrawSolidDisc(point, Vector3.forward, HandleUtility.GetHandleSize(point) * 0.025f);
                        }
                }
                HandleMeshPastePlacement(matrix);
                if (!evt.alt && evt.button == 0 && (evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag || evt.type == EventType.MouseUp)) evt.Use();
                return;
            }
            var deformation = evt.type == EventType.Repaint ? Deformation() : null;
            var displayedMesh = deformation?.mesh ?? m_Session.mesh;
            if (evt.type == EventType.Repaint)
            {
                DrawImage(matrix, deformation?.mesh);
                DrawImageBounds(matrix, scene);
            }
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
                if (evt.type == EventType.Repaint && m_Triangles) DrawTriangles(displayedMesh);
                if (evt.type == EventType.Repaint && !m_TraceActive && m_Session.symmetryActive)
                {
                    var size = m_Session.mesh.frame.size;
                    Handles.color = new Color(0.3f, 0.8f, 1f, 0.9f);
                    Handles.DrawDottedLine(new Vector3(size.x * 0.5f, 0f), new Vector3(size.x * 0.5f, size.y), 4f);
                    Handles.color = Color.white;
                }
                m_Controller.newGeometryVertexCreated = m_Session.symmetryHullActive ? point =>
                {
                    m_Session.AddSymmetryVertex(point);
                    if (m_Session.mode != 3) m_View.mode = SpriteMeshViewMode.EditGeometry;
                } : (Action<Vector2>)null;
                m_Controller.newGeometryVerticesRemoved = m_Session.symmetryHullActive ? m_Session.RemoveSymmetryVertices : (Action<int[]>)null;
                m_Controller.newGeometryVerticesMoved = m_Session.symmetryHullActive ? m_Session.MoveSymmetryVertices : (Action<int[], Vector2[]>)null;
                m_Controller.newGeometryPreview = m_Session.symmetryHullActive ? PreviewSymmetryHull : (Action)null;
                m_View.mode = m_TraceActive ? SpriteMeshViewMode.EditGeometry :
                    effectiveMode == 3 ? SpriteMeshViewMode.NewGeometry :
                    effectiveMode == 1 ? SpriteMeshViewMode.CreateVertex : SpriteMeshViewMode.EditGeometry;
                m_View.defaultControlID = m_Control;
                m_Controller.selection = m_Session.selection;
                m_Controller.disable = m_TraceActive || m_Session.mode == 2;
                EditorGUI.BeginChangeCheck();
                EditorGUI.BeginDisabledGroup(m_TraceActive);
                try
                {
                    if (!m_TraceActive) HandleDelete(effectiveMode);
                    // Delete operates on the staged source. Rebuild the transient pose
                    // before handing it to the shared controller for this event.
                    if (evt.type != EventType.Repaint) deformation = Deformation();
                    displayedMesh = deformation?.mesh ?? m_Session.mesh;
                    m_Controller.spriteMeshData = displayedMesh;
                    m_Controller.frame = deformation?.editingFrame ?? new Rect(Vector2.zero, m_Session.mesh.frame.size);
                    ConfigureSymmetryEditing(deformation);
                    // Controller edits also triangulate and may pump nested Scene GUI events.
                    // Keep partial vertices/indices private until the controller returns.
                    m_MeshMutationInProgress = true;
                    try
                    {
                        m_Controller.OnGUI();
                        if (evt.type != EventType.Repaint && evt.type != EventType.Layout && evt.type != EventType.MouseMove)
                            if (!m_SymmetrySourceMutated) deformation?.Commit(m_Session);
                    }
                    finally { m_MeshMutationInProgress = false; }
                    if (m_View.isCreateEdgeDragActive && evt.type == EventType.MouseDown && evt.button == 0) evt.Use();
                    if (!m_TraceActive) HandleBoxSelection(displayedMesh);
                }
                catch (Exception ex) { EditorJsonUtility.FromJsonOverwrite(backup, m_Session); m_Error = ex.Message; GUIUtility.hotControl = 0; }
                finally { EditorGUI.EndDisabledGroup(); EditorGUI.EndChangeCheck(); }
            }
            if (evt.rawType == EventType.MouseUp || GUIUtility.hotControl == 0) m_Session.EndUndoOperation();
            // AddDefaultControl is only a fallback: another Scene handle can win
            // nearestControl. Consume unhandled primary input after mesh tools run,
            // including clicks outside the source frame and clicks in New mode.
            if (evt.button == 0 &&
                (evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag || evt.type == EventType.MouseUp)) evt.Use();
            if (!m_TraceActive)
            {
                var cursor = effectiveMode == 2 ? MouseCursor.ArrowMinus :
                    effectiveMode == 1 || effectiveMode == 3 ? MouseCursor.ArrowPlus : MouseCursor.MoveArrow;
                // Register after mesh handles so the effective mode also wins over
                // slider cursors. Navigation exits above retain their native cursors.
                EditorGUIUtility.AddCursorRect(new Rect(Vector2.zero, scene.position.size), cursor);
            }
            hasUnsavedChanges = false;
            if (evt.type != EventType.Repaint && evt.type != EventType.Layout) { scene.Repaint(); UpdateUI(); }
        }

        Vector2 SourceGeometryPoint(Vector2 position, int edge, SceneMeshDeformation deformation) =>
            deformation == null ? position : deformation.SourcePosition(position,
                SceneMeshSession.SampleGeometryWeight(deformation.mesh, position, edge));

        void ConfigureSymmetryEditing(SceneMeshDeformation deformation)
        {
            m_SymmetrySourceMutated = false;
            bool active = !m_TraceActive && m_Session.symmetryActive && m_Session.mode != 3;
            m_Controller.geometryVertexCreated = active ? (position, edge) =>
            {
                m_SymmetrySourceMutated = true;
                m_Session.CreateSymmetricGeometry(SourceGeometryPoint(position, edge, deformation));
            } : (Action<Vector2, int>)null;
            m_Controller.geometryEdgeCreated = active ? (from, position, to, edge) =>
            {
                m_SymmetrySourceMutated = true;
                var source = to >= 0 ? m_Session.mesh.vertices[to] : SourceGeometryPoint(position, edge, deformation);
                m_Session.CreateSymmetricGeometry(source, from);
            } : (Action<int, Vector2, int, int>)null;
            m_Controller.geometryVerticesMoved = active ? (indices, positions) =>
            {
                m_SymmetrySourceMutated = true;
                var source = positions.Select((p, i) => deformation == null ? p :
                    deformation.SourcePosition(p, deformation.mesh.vertexWeights[indices[i]])).ToArray();
                m_Session.MoveSymmetricGeometry(indices, source);
            } : (Action<int[], Vector2[]>)null;
            m_Controller.geometrySymmetryPreview = active ? () => PreviewSymmetricGeometry(deformation) : (Action)null;
        }

        void PreviewSymmetricGeometry(SceneMeshDeformation deformation)
        {
            if (!m_View.IsActionActive(MeshEditorAction.CreateVertex) && !m_View.isCreateEdgeDragActive) return;
            if (m_View.hoveredVertex >= 0 && !m_View.isCreateEdgeDragActive) return;
            var source = SourceGeometryPoint(m_View.mouseWorldPosition, m_View.hoveredEdge, deformation);
            if (m_View.isCreateEdgeDragActive)
            {
                var preview = m_Controller.createEdgePreview;
                source = preview.vertex >= 0 ? m_Session.mesh.vertices[preview.vertex] :
                    SourceGeometryPoint(preview.position, preview.edge, deformation);
            }
            float center = m_Session.mesh.frame.width * 0.5f;
            if (Mathf.Abs(source.x - center) <= m_Session.mesh.frame.width * 0.005f) source.x = center;
            var mirrored = m_Session.MirrorHullPoint(source);
            if (mirrored.Equals(source)) return;
            var weight = SceneMeshSession.SampleGeometryWeight(m_Session.mesh, mirrored);
            var displayed = deformation?.DisplayPosition(mirrored, weight) ?? mirrored;
            if (m_View.isCreateEdgeDragActive && m_Session.selection.Count > 0)
            {
                int partner = m_Session.FindSymmetryVertex(m_Session.MirrorHullPoint(m_Session.mesh.vertices[m_Session.selection.activeElement]));
                if (partner >= 0)
                {
                    var start = deformation?.mesh.vertices[partner] ?? m_Session.mesh.vertices[partner];
                    m_View.BeginDrawEdges();
                    m_View.DrawEdge(start, displayed);
                    m_View.EndDrawEdges();
                }
            }
            m_View.DrawVertex(displayed);
        }

        bool HandleVertexClipboard(KeyCode key, bool action, bool shift, bool alt)
        {
            if (!action || alt || (key != KeyCode.C && key != KeyCode.V) || m_TraceActive || pasteActive ||
                GUIUtility.hotControl != 0 || m_SaveInProgress || m_MeshMutationInProgress ||
                m_Session == null || m_Session.mode == 3 || BlockReason() != null)
                return false;
            if (key == KeyCode.C)
            {
                var clipboard = m_Session.CopySelectedVertices();
                if (clipboard != null) EditorGUIUtility.systemCopyBuffer = clipboard;
            }
            else
            {
                m_MeshMutationInProgress = true;
                try
                {
                    if (m_Session.PasteVertices(EditorGUIUtility.systemCopyBuffer, shift)) m_Error = null;
                }
                catch (Exception ex) { m_Error = ex.Message; }
                finally { m_MeshMutationInProgress = false; }
            }
            SceneView.RepaintAll();
            UpdateUI();
            return true;
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
                    Mutate("Delete Sprite Mesh Edge", () => { RemoveGeometryEdge(edge); Triangulate(); }, false, keepMode: true);
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
                        RemoveGeometryEdge(new Unity.Mathematics.int2(selected[0], selected[1]));
                    else
                    {
                        var vertices = m_Session.symmetryActive ? m_Session.SymmetrySelection(selected) : selected;
                        if (m_Session.mesh.vertexCount - vertices.Length < 3)
                            throw new InvalidOperationException("Keep at least three vertices. Use New or Reset to replace the mesh.");
                        DataController().RemoveVertex(vertices);
                    }
                    Triangulate();
                }, false, keepMode: true);
            }
            evt.Use();
        }

        void RemoveGeometryEdge(Unity.Mathematics.int2 edge)
        {
            var controller = DataController();
            if (m_Session.symmetryActive)
            {
                int a = m_Session.FindSymmetryVertex(m_Session.MirrorHullPoint(m_Session.mesh.vertices[edge.x]));
                int b = m_Session.FindSymmetryVertex(m_Session.MirrorHullPoint(m_Session.mesh.vertices[edge.y]));
                if (a >= 0 && b >= 0) controller.RemoveEdge(new Unity.Mathematics.int2(a, b));
            }
            controller.RemoveEdge(edge);
        }

        void HandleBoxSelection(SpriteMeshData displayedMesh)
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
                for (var i = 0; i < displayedMesh.vertexCount; ++i)
                    if (rect.Contains(HandleUtility.WorldToGUIPoint(displayedMesh.vertices[i]))) m_Session.selection.Select(i, true);
                if (evt.type == EventType.MouseUp) { m_BoxSelecting = false; GUIUtility.hotControl = 0; }
                evt.Use();
            }
            if (evt.type == EventType.Repaint)
            {
                Handles.BeginGUI(); GUI.Box(rect, GUIContent.none, "SelectionRect"); Handles.EndGUI();
            }
        }

        void PreviewSymmetryHull()
        {
            if (m_View.hoveredVertex >= 0) return;
            var point = m_Session.SymmetryHullPoint(m_View.mouseWorldPosition);
            var mirrored = m_Session.MirrorHullPoint(point);
            m_View.BeginDrawEdges();
            if (m_Session.symmetryHullTip is Vector2 tip)
            {
                m_View.DrawEdge(tip, point);
                m_View.DrawEdge(m_Session.MirrorHullPoint(tip), mirrored);
            }
            m_View.EndDrawEdges();
            m_View.DrawVertex(point);
            if (!point.Equals(mirrored)) m_View.DrawVertex(mirrored);
        }

        void DrawTriangles(SpriteMeshData mesh)
        {
            var vertices = mesh.vertices;
            var indices = mesh.indices;
            Handles.color = new Color(0.7f, 0.7f, 0.7f, 0.6f);
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                var ia = indices[i];
                var ib = indices[i + 1];
                var ic = indices[i + 2];
                if (ia < 0 || ib < 0 || ic < 0 ||
                    ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length) continue;
                var a = vertices[ia];
                var b = vertices[ib];
                var c = vertices[ic];
                Handles.DrawDottedLine(a, b, 4f); Handles.DrawDottedLine(b, c, 4f); Handles.DrawDottedLine(c, a, 4f);
            }
        }

        void DrawImageBounds(Matrix4x4 matrix, SceneView scene)
        {
            var size = m_Session.mesh.frame.size;
            m_ImageBoundsPoints[0] = Vector3.zero;
            m_ImageBoundsPoints[1] = new Vector3(size.x, 0f);
            m_ImageBoundsPoints[2] = new Vector3(size.x, size.y);
            m_ImageBoundsPoints[3] = new Vector3(0f, size.y);
            using (new Handles.DrawingScope(Color.white, Matrix4x4.identity))
            {
                for (var i = 0; i < m_ImageBoundsPoints.Length; ++i)
                {
                    var point = HandleUtility.WorldToGUIPointWithDepth(matrix.MultiplyPoint3x4(m_ImageBoundsPoints[i]));
                    if (point.z <= 0f || float.IsNaN(point.x) || float.IsNaN(point.y) ||
                        float.IsInfinity(point.x) || float.IsInfinity(point.y)) return;
                    m_ImageBoundsPoints[i] = new Vector3(point.x, point.y, 0f);
                }
                var pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                var dashLength = 6f / pixelsPerPoint;
                var dashStep = 9f / pixelsPerPoint;
                var distance = 0f;
                var viewport = new Rect(Vector2.zero, scene.position.size);
                Handles.BeginGUI();
                try
                {
                    for (var i = 0; i < m_ImageBoundsPoints.Length; ++i)
                    {
                        var start = m_ImageBoundsPoints[i];
                        var end = m_ImageBoundsPoints[(i + 1) % m_ImageBoundsPoints.Length];
                        var length = Vector3.Distance(start, end);
                        if (length <= Mathf.Epsilon) continue;
                        var direction = (end - start) / length;
                        var visibleStart = 0f;
                        var visibleEnd = length;
                        // Clip before generating dashes so a zoomed-in, mostly offscreen
                        // image costs only as much drawing as the visible part of its frame.
                        if (ClipBoundsAxis(start.x, direction.x, viewport.xMin, viewport.xMax, ref visibleStart, ref visibleEnd) &&
                            ClipBoundsAxis(start.y, direction.y, viewport.yMin, viewport.yMax, ref visibleStart, ref visibleEnd))
                        {
                            var firstDash = Mathf.FloorToInt((distance + visibleStart) / dashStep);
                            var lastDash = Mathf.FloorToInt((distance + visibleEnd) / dashStep);
                            for (var dash = firstDash; dash <= lastDash; ++dash)
                            {
                                var from = Mathf.Max(visibleStart, dash * dashStep - distance);
                                var to = Mathf.Min(visibleEnd, dash * dashStep - distance + dashLength);
                                if (to <= from) continue;
                                Handles.color = (dash & 1) == 0 ? new Color(0.65f, 0.65f, 0.65f) : new Color(0.3f, 0.3f, 0.3f);
                                m_ImageBoundsDash[0] = start + direction * from;
                                m_ImageBoundsDash[1] = start + direction * to;
                                Handles.DrawAAPolyLine(1.5f / pixelsPerPoint, m_ImageBoundsDash);
                            }
                        }
                        // Preserve both dash phase and color when turning a corner.
                        distance = Mathf.Repeat(distance + length, dashStep * 2f);
                    }
                }
                finally { Handles.EndGUI(); }
            }
        }

        static bool ClipBoundsAxis(float start, float direction, float min, float max, ref float from, ref float to)
        {
            if (Mathf.Abs(direction) < 0.000001f) return start >= min && start <= max;
            var a = (min - start) / direction;
            var b = (max - start) / direction;
            from = Mathf.Max(from, Mathf.Min(a, b));
            to = Mathf.Min(to, Mathf.Max(a, b));
            return from <= to;
        }

        void DrawImage(Matrix4x4 matrix, SpriteMeshData deformedMesh)
        {
            var textureProvider = m_Source.textureProvider;
            if (textureProvider?.texture == null) return;
            if (m_ImageMaterial == null)
            {
                m_ImageMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
                m_ImageMaterial.SetInt("_Cull", (int)CullMode.Off);
            }
            if (m_ImageMesh == null) m_ImageMesh = new Mesh { name = "Uniskin Sprite Preview", hideFlags = HideFlags.HideAndDontSave };
            textureProvider.GetTextureActualWidthAndHeight(out var width, out var height);
            BuildImageMesh(m_ImageMesh, m_Session.mesh, deformedMesh, m_Source.bindingPose,
                m_Session.mode == 3 || m_TraceActive || pasteActive, width, height);
            var color = m_Renderer.color;
            if (m_Dim) { color.r *= 0.35f; color.g *= 0.35f; color.b *= 0.35f; }
            m_ImageMaterial.mainTexture = textureProvider.texture;
            m_ImageMaterial.color = color;
            if (!m_OnTop)
            {
                if (m_SortedImageRenderer == null)
                {
                    var preview = new GameObject("Uniskin Sorted Mesh Preview") { hideFlags = HideFlags.HideAndDontSave };
                    preview.transform.SetParent(m_Renderer.transform, false);
                    preview.AddComponent<MeshFilter>().sharedMesh = m_ImageMesh;
                    m_SortedImageRenderer = preview.AddComponent<MeshRenderer>();
                    m_SortedImageRenderer.sharedMaterial = m_ImageMaterial;
                    m_SortedImageRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    m_SortedImageRenderer.receiveShadows = false;
                    SceneVisibilityManager.instance.DisablePicking(preview, false);
                }
                var localMatrix = m_Renderer.transform.worldToLocalMatrix * matrix;
                m_ImageMesh.vertices = Array.ConvertAll(m_ImageMesh.vertices, localMatrix.MultiplyPoint3x4);
                m_ImageMesh.RecalculateBounds();
                m_SortedImageRenderer.gameObject.layer = m_Renderer.gameObject.layer;
                m_SortedImageRenderer.sortingLayerID = m_Renderer.sortingLayerID;
                m_SortedImageRenderer.sortingOrder = m_Renderer.sortingOrder;
                m_SortedImageRenderer.enabled = true;
                return;
            }
            if (m_SortedImageRenderer != null) m_SortedImageRenderer.enabled = false;
            m_ImageMaterial.SetPass(0);
            Graphics.DrawMeshNow(m_ImageMesh, matrix);
        }

        internal static void BuildImageMesh(Mesh imageMesh, SpriteMeshData sourceMesh, SpriteMeshData deformedMesh,
            SceneMeshBindingPose bindingPose, bool authoringHull, int width, int height)
        {
            var frame = sourceMesh.frame;
            var size = frame.size;
            Vector2 UV(float x, float y) => new Vector2((frame.x + x) / width, (frame.y + y) / height);
            imageMesh.Clear();
            var displayed = deformedMesh ?? sourceMesh;
            // New has no triangles until the hull closes. Keep a drawable source image
            // even when Update Bindings saved a custom texture mapping.
            if (!authoringHull && displayed.indices.Length >= 3 && (deformedMesh != null || bindingPose != null))
            {
                imageMesh.vertices = Array.ConvertAll(displayed.vertices, vertex => (Vector3)vertex);
                imageMesh.uv = sourceMesh.vertices.Select((vertex, i) =>
                {
                    var pixel = bindingPose?.TexturePosition(vertex, i) ?? vertex;
                    return UV(pixel.x, pixel.y);
                }).ToArray();
                imageMesh.triangles = displayed.indices;
            }
            else
            {
                var corners = new[] { Vector2.zero, new Vector2(size.x, 0), size, new Vector2(0, size.y) };
                imageMesh.vertices = Array.ConvertAll(corners, vertex => (Vector3)vertex);
                imageMesh.uv = corners.Select(vertex =>
                {
                    var pixel = bindingPose?.TexturePosition(vertex) ?? vertex;
                    return UV(pixel.x, pixel.y);
                }).ToArray();
                imageMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            }
            imageMesh.colors = Enumerable.Repeat(Color.white, imageMesh.vertexCount).ToArray();
        }
    }
}
