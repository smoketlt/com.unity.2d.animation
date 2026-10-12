using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    internal sealed partial class SceneEditWeightsWindow
    {
        enum Gesture { None, Select, Piece, Paint }
        Gesture m_Gesture;
        Vector2 m_GestureStart, m_LastStamp;
        int[] m_SelectionBefore;
        int m_Control;
        bool m_Additive;
        bool m_EscapeHandledBeforeSceneGUI;
        Mesh m_OverlayMesh;
        Material m_OverlayMaterial;
        [SerializeField] bool m_PreviousForceRenderingOff, m_HidesRenderer;
        readonly Dictionary<string, (Transform transform, float length)> m_LiveBoneBindings = new Dictionary<string, (Transform, float)>();
        readonly Dictionary<string, (Transform transform, float length)> m_CharacterLiveBindings = new Dictionary<string, (Transform, float)>();
        bool m_CharacterBindingsLoaded;

        void RefreshLiveBoneBindings()
        {
            m_LiveBoneBindings.Clear();
            var instance = PrefabUtility.GetNearestPrefabInstanceRoot(m_Renderer.gameObject);
            var host = instance != null ? instance.transform : m_Renderer.transform.root;
            if (!m_CharacterBindingsLoaded)
            {
                m_CharacterBindingsLoaded = true;
                var characterProvider = m_Source.provider.GetDataProvider<ICharacterDataProvider>();
                var bones = characterProvider != null ? characterProvider.GetCharacterData().bones : null;
                if (bones != null)
                    for (var i = 0; i < bones.Length; ++i)
                    {
                        if (string.IsNullOrEmpty(bones[i].guid)) continue;
                        var transform = SceneBoneInspector.FindCharacterTransform(host, bones, i);
                        if (transform != null)
                            m_CharacterLiveBindings[bones[i].guid] = (transform, SceneBoneInspector.CharacterBoneLocalLength(m_Source.provider, bones[i].length));
                    }
            }
            foreach (var skin in host.GetComponentsInChildren<SpriteSkin>(true))
            {
                if (!skin.isActiveAndEnabled) continue;
                var sprite = skin.GetComponent<SpriteRenderer>()?.sprite;
                if (sprite == null || UniskinRigUtility.SourcePath(sprite) != m_Session.context.assetPath) continue;
                var bones = sprite.GetBones(); var transforms = skin.boneTransforms;
                if (transforms == null) continue;
                for (var i = 0; i < Mathf.Min(bones.Length, transforms.Length); ++i)
                    if (transforms[i] != null && !string.IsNullOrEmpty(bones[i].guid))
                        if (skin.gameObject == m_Renderer.gameObject || !m_LiveBoneBindings.ContainsKey(bones[i].guid))
                            m_LiveBoneBindings[bones[i].guid] = (transforms[i], bones[i].length);
            }
            foreach (var binding in m_CharacterLiveBindings)
                if (binding.Value.transform != null && !m_LiveBoneBindings.ContainsKey(binding.Key))
                    m_LiveBoneBindings[binding.Key] = binding.Value;
        }

        void WorldBoneSegment(BoneCache bone, out Vector3 start, out Vector3 end)
        {
            if (m_LiveBoneBindings.TryGetValue(bone.guid, out var binding) && binding.transform != null)
            {
                start = binding.transform.position;
                end = binding.transform.TransformPoint(Vector3.right * binding.length);
                return;
            }
            var matrix = m_Session.context.SkeletonToWorld(m_Renderer, m_Source.rect.rect);
            start = matrix.MultiplyPoint3x4(bone.position);
            end = matrix.MultiplyPoint3x4(bone.endPosition);
        }

        void Cancel()
        {
            ClearBoneDrop();
            EndGesture(); m_Bind = false;
            m_Session.vertices.EndSelection(false);
            m_Session.vertices.Clear();
            UpdateUI(); SceneView.RepaintAll();
        }

        void EndGesture()
        {
            if (m_Control != 0 && GUIUtility.hotControl == m_Control) GUIUtility.hotControl = 0;
            m_Gesture = Gesture.None;
            m_Session?.context?.cache?.EndUndoOperation();
        }

        Vector3[] WorldVertices()
        {
            var mesh = m_Session.mesh;
            // This host deforms bind vertices itself, independently of Sprite Editor previews.
            mesh.ClearVertexPositionOverride();
            var vertices = mesh.vertices;
            var result = new Vector3[mesh.vertexCount];
            var pixelToWorld = SceneMeshSession.PixelToWorld(m_Renderer, m_Source.rect.rect);
            var skin = m_Renderer.GetComponent<SpriteSkin>();
            var transforms = skin != null ? skin.boneTransforms : null;
            var bones = m_Renderer.sprite.GetBones();
            var poses = m_Renderer.sprite.GetBindPoses();
            var map = mesh.bones.Select(bone => Array.FindIndex(bones, item => item.guid == bone.guid)).ToArray();
            var skeletonToWorld = m_Session.context.SkeletonToWorld(m_Renderer, m_Source.rect.rect);
            var localToSkeleton = m_Session.cache.selectedSprite.GetLocalToWorldMatrixFromMode();
            for (var i = 0; i < result.Length; ++i)
            {
                var rest = pixelToWorld.MultiplyPoint3x4(vertices[i]);
                var local = m_Renderer.transform.InverseTransformPoint(rest);
                var sum = 0f; var posed = Vector3.zero;
                if (transforms != null && skin.isActiveAndEnabled)
                    foreach (var channel in mesh.vertexWeights[i])
                    {
                        if (!channel.enabled || channel.weight <= 0 || channel.boneIndex < 0 || channel.boneIndex >= map.Length) continue;
                        var index = map[channel.boneIndex];
                        var position = index >= 0 && index < transforms.Length && index < poses.Length && transforms[index] != null ?
                            transforms[index].localToWorldMatrix.MultiplyPoint3x4(poses[index].MultiplyPoint3x4(local)) :
                            (skeletonToWorld * m_Session.context.CapturedDeformation(mesh.bones[channel.boneIndex]) * localToSkeleton).MultiplyPoint3x4(vertices[i]);
                        posed += position * channel.weight;
                        sum += channel.weight;
                    }
                result[i] = sum > 0 ? posed / sum : rest;
            }
            return result;
        }

        void BeforeSceneGUI(SceneView scene)
        {
            m_EscapeHandledBeforeSceneGUI = false;
            var evt = Event.current;
            if (m_Saving || m_Session?.cache == null || evt == null ||
                evt.type != EventType.KeyDown || evt.keyCode != KeyCode.Escape) return;
            // Unimator's duringSceneGui handler clears Unity selection and consumes Escape.
            // Claim the key in the earlier phase, while it is still a KeyDown event.
            m_EscapeHandledBeforeSceneGUI = true;
            Cancel(); evt.Use();
        }

        void OnSceneGUI(SceneView scene)
        {
            if (m_Saving) return;
            // Scene View can consume Escape before duringSceneGui runs. rawType survives Use().
            // Handle it before preview/blocking/control setup so local unselection still works.
            var evt = Event.current;
            if (evt.rawType == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                // The same event reaches both phases; never unselect twice for one key press.
                if (!m_EscapeHandledBeforeSceneGUI && m_Session?.cache != null) Cancel();
                evt.Use(); return;
            }
            if (BlockReason() != null) { RestoreTool(); return; }
            RefreshLiveBoneBindings();
            var world = WorldVertices();
            if (!m_OwnsTool) { m_PreviousTool = UnityEditor.Tools.current; UnityEditor.Tools.current = Tool.None; m_OwnsTool = true; }
            if (!m_HidesRenderer) { m_PreviousForceRenderingOff = m_Renderer.forceRenderingOff; m_HidesRenderer = true; m_Renderer.forceRenderingOff = true; }
            m_ModePopup?.SetValueWithoutNotify(m_Direct ? "Direct" : m_RemoveMode || evt.alt ? "Remove" : "Add");
            m_Control = GUIUtility.GetControlID("UniskinSceneWeights".GetHashCode(), FocusType.Passive);
            if (evt.type == EventType.Layout) HandleUtility.AddDefaultControl(m_Control);
            var screen = world.Select(HandleUtility.WorldToGUIPoint).ToArray();
            if (evt.type == EventType.Repaint)
            {
                DrawPreview(world);
                DrawMeshEdges(screen);
                if (scene.drawGizmos) DrawBones();
                DrawPieces(screen);
                if (!m_Bind && !m_Direct && !evt.control && !evt.command) DrawBrush(evt.mousePosition);
                if (m_Gesture == Gesture.Select) DrawSelectionRect(m_GestureStart, evt.mousePosition);
            }
            if (m_Direct) EditorGUIUtility.AddCursorRect(new Rect(Vector2.zero, scene.position.size), MouseCursor.Arrow);
            if (evt.rawType == EventType.MouseUp && m_Gesture != Gesture.None)
            { EndGesture(); UpdateUI(); scene.Repaint(); evt.Use(); return; }
            if (evt.button != 0 && evt.type != EventType.MouseMove) return;
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                m_GestureStart = evt.mousePosition;
                m_Additive = evt.control || evt.command || evt.shift;
                var hitBone = scene.drawGizmos ? NearestBone(evt.mousePosition) : null;
                if ((evt.control || evt.command) && hitBone == null && !m_Bind)
                {
                    m_Gesture = Gesture.Select;
                    m_SelectionBefore = m_Session.vertices.elements.ToArray();
                    var vertex = NearestVertex(screen, evt.mousePosition);
                    if (vertex >= 0)
                        m_Session.vertices.elements = m_SelectionBefore.Contains(vertex) ? m_SelectionBefore.Except(new[] { vertex }).ToArray() : m_SelectionBefore.Concat(new[] { vertex }).ToArray();
                }
                else
                {
                    var selectable = screen;
                    if (m_Selected)
                    {
                        var selected = m_Session.selectedBones;
                        selectable = screen.Select((point, i) => m_Session.vertices.Contains(i) || selected.Any(b => m_Session.Values(i)[b] > 0)
                            ? point : new Vector2(float.PositiveInfinity, float.PositiveInfinity)).ToArray();
                    }
                    var vertex = m_Direct && !m_Bind && !evt.control && !evt.command ? NearestVertex(selectable, evt.mousePosition) : -1;
                    var bone = hitBone;
                    if (vertex >= 0)
                    {
                        if (!m_Session.vertices.Contains(vertex))
                            m_Session.vertices.elements = m_Additive ? m_Session.vertices.elements.Concat(new[] { vertex }).ToArray() : new[] { vertex };
                        m_Gesture = Gesture.Piece;
                    }
                    else if (bone != null)
                    {
                        if (m_Bind) Run(() => m_Session.Bind(bone));
                        else m_Session.SelectBone(bone, m_Additive, toggleSelected: false);
                    }
                    else if (m_Direct && !m_Bind)
                    {
                        if (evt.clickCount >= 2)
                        {
                            m_Session.vertices.EndSelection(false);
                            m_Session.vertices.Clear();
                        }
                        else
                        {
                            // Keep the selection on a single click; replace/add only after dragging.
                            m_Gesture = Gesture.Select;
                            m_SelectionBefore = m_Session.vertices.elements.ToArray();
                        }
                    }
                    else if (!m_Bind && !m_Direct && m_Session.selectedBones.Length > 0)
                    {
                        m_Gesture = Gesture.Paint; m_LastStamp = evt.mousePosition;
                        Stamp(screen, evt.mousePosition, BrushRadius());
                    }
                }
                if (m_Gesture != Gesture.None) GUIUtility.hotControl = m_Control;
                evt.Use(); UpdateUI(); scene.Repaint(); return;
            }
            if (evt.type == EventType.MouseDrag && m_Gesture != Gesture.None && GUIUtility.hotControl == m_Control)
            {
                if (m_Gesture == Gesture.Select && Vector2.Distance(m_GestureStart, evt.mousePosition) > 3)
                {
                    var rect = SelectionRect(m_GestureStart, evt.mousePosition);
                    var inside = Enumerable.Range(0, screen.Length).Where(i => rect.Contains(screen[i]));
                    m_Session.vertices.elements = m_Additive ? m_SelectionBefore.Union(inside).ToArray() : inside.ToArray();
                }
                else if (m_Gesture == Gesture.Piece && m_Session.selectedBones.Length > 0)
                {
                    m_Session.BeginWeightUndo("Drag Vertex Weights");
                    foreach (var vertex in m_Session.targets) m_Session.Adjust(vertex, m_Session.selectedBones, -evt.delta.y * .0025f * m_Strength / 100f);
                    m_Session.Changed();
                }
                else if (m_Gesture == Gesture.Paint)
                {
                    var radius = BrushRadius();
                    var distance = Vector2.Distance(m_LastStamp, evt.mousePosition);
                    var step = Mathf.Max(2, radius * .25f);
                    var count = Mathf.Min(64, Mathf.FloorToInt(distance / step));
                    var origin = m_LastStamp;
                    for (var i = 1; i <= count; ++i)
                    {
                        m_LastStamp = Vector2.Lerp(origin, evt.mousePosition, Mathf.Min(1, i * step / distance));
                        Stamp(screen, m_LastStamp, radius);
                    }
                }
                evt.Use(); UpdateUI(); scene.Repaint();
            }
            if (evt.type == EventType.MouseMove) scene.Repaint();
        }

        static int NearestVertex(Vector2[] vertices, Vector2 point)
        {
            var nearest = -1; var distance = 12f / EditorGUIUtility.pixelsPerPoint;
            for (var i = 0; i < vertices.Length; ++i)
            {
                var next = Vector2.Distance(vertices[i], point);
                if (next < distance) { nearest = i; distance = next; }
            }
            return nearest;
        }

        BoneCache NearestBone(Vector2 point)
        {
            BoneCache nearest = null; var distance = 10f / EditorGUIUtility.pixelsPerPoint;
            foreach (var bone in m_Session.context.skeleton.bones.Where(bone => !bone.IsConstraintParent()))
            {
                // Bind picks only new candidates; ordinary editing picks only this mesh's bones.
                if (m_Session.mesh.ContainsBone(bone) == m_Bind) continue;
                WorldBoneSegment(bone, out var start, out var end);
                var a = HandleUtility.WorldToGUIPoint(start);
                var b = HandleUtility.WorldToGUIPoint(end);
                var delta = b - a;
                var t = delta.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude) : 0;
                var next = Vector2.Distance(point, a + delta * t);
                if (next < distance) { distance = next; nearest = bone; }
            }
            return nearest;
        }

        float BrushRadius()
        {
            var matrix = SceneMeshSession.PixelToWorld(m_Renderer, m_Source.rect.rect);
            var center = m_Session.mesh.frame.size * .5f;
            return Mathf.Max(2, Vector2.Distance(HandleUtility.WorldToGUIPoint(matrix.MultiplyPoint3x4(center)),
                HandleUtility.WorldToGUIPoint(matrix.MultiplyPoint3x4(center + Vector2.right * m_Size))));
        }

        void Stamp(Vector2[] points, Vector2 center, float radius)
        {
            var selected = m_Session.selectedBones;
            if (selected.Length == 0) return;
            var inner = radius * (1 - m_Feather / 100f);
            foreach (var vertex in m_Session.targets)
            {
                var distance = Vector2.Distance(points[vertex], center);
                if (distance > radius) continue;
                var falloff = distance <= inner || radius <= inner ? 1 : (radius - distance) / (radius - inner);
                if (falloff <= 0 || m_Strength == 0) continue;
                m_Session.BeginWeightUndo("Paint Mesh Weights");
                m_Session.Adjust(vertex, selected, m_Strength / 100f * falloff * (m_RemoveMode || Event.current.alt ? -1 : 1));
            }
            m_Session.Changed();
        }

        void DrawBones()
        {
            using (new Handles.DrawingScope(Color.white, Matrix4x4.identity))
                foreach (var bone in m_Session.context.skeleton.bones.Where(bone => !bone.IsConstraintParent()))
                {
                    var bound = m_Session.mesh.ContainsBone(bone);
                    var color = bound ? WeightColor(bone) : new Color(.65f, .65f, .65f, m_Bind ? 1f : .25f);
                    if (m_Bind && bound) color = new Color(.3f, .3f, .3f, .4f);
                    var scale = bound && !m_Bind && m_Session.cache.skeletonSelection.Contains(bone) ? 1.15f : 1;
                    WorldBoneSegment(bone, out var start, out var end);
                    BoneDrawingUtility.DrawBone(start, end, Vector3.forward, color, scale);
                    BoneDrawingUtility.DrawBoneNode(start, Vector3.forward, color, scale);
                }
        }

        void DrawMeshEdges(Vector2[] positions)
        {
            Handles.BeginGUI();
            try
            {
                using (new Handles.DrawingScope(Color.white, Matrix4x4.identity))
                {
                    // Authored constraints only; automatic interior triangle edges stay hidden.
                    DrawEdges(m_Session.mesh.edges, new Color(1f, .65f, 0f));
                    // Draw last so boundary edges also present in the authored list remain cyan.
                    DrawEdges(m_Session.mesh.outlineEdges, Color.cyan);
                }
            }
            finally { Handles.EndGUI(); }

            void DrawEdges(int2[] edges, Color color)
            {
                Handles.color = color;
                foreach (var edge in edges)
                {
                    if (edge.x < 0 || edge.y < 0 || edge.x >= positions.Length || edge.y >= positions.Length) continue;
                    Handles.DrawAAPolyLine(2f, positions[edge.x], positions[edge.y]);
                }
            }
        }

        void DrawPieces(Vector2[] positions)
        {
            var selected = m_Session.selectedBones;
            var boneOrder = m_Session.orderedBoneIndices;
            var bones = m_Session.mesh.bones;
            Handles.BeginGUI();
            try
            {
                for (var vertex = 0; vertex < positions.Length; ++vertex)
                {
                    var values = m_Session.Values(vertex);
                    var active = m_Session.vertices.Contains(vertex);
                    if (m_Selected && !active && !selected.Any(i => values[i] > 0)) continue;
                    var radius = (m_Pieces ? (active ? 6f : 5f) : (active ? 3f : 2f)) / EditorGUIUtility.pixelsPerPoint;
                    var start = 0f;
                    if (!m_Pieces)
                    {
                        // Pieces changes only the visual; dots use the same selection and weight gestures.
                        Handles.color = Color.cyan;
                        Handles.DrawSolidDisc(positions[vertex], Vector3.forward, radius);
                    }
                    else
                    {
                        foreach (var bone in boneOrder)
                        {
                            if (values[bone] <= 0) continue;
                            var color = WeightColor(bones[bone]);
                            Handles.color = color;
                            Handles.DrawSolidArc(positions[vertex], Vector3.forward, Quaternion.Euler(0, 0, start) * Vector3.up, values[bone] * 360, radius);
                            start += values[bone] * 360;
                        }
                        if (start == 0) { Handles.color = Color.gray; Handles.DrawSolidDisc(positions[vertex], Vector3.forward, radius); }
                    }
                    if (active) { Handles.color = Color.white; Handles.DrawWireDisc(positions[vertex], Vector3.forward, radius + 1); }
                }
            }
            finally { Handles.EndGUI(); Handles.color = Color.white; }
        }

        void DrawPreview(Vector3[] vertices)
        {
            if (m_OverlayMesh == null) m_OverlayMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            if (m_OverlayMaterial == null)
            {
                var shader = ResourceLoader.Load<Shader>("SkinningModule/SceneWeightsPreview.shader");
                if (shader == null) return;
                m_OverlayMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            var mesh = m_Session.mesh; var bones = mesh.bones; var selected = m_Session.selectedBones;
            var colors = new Color[vertices.Length]; var uv = new Vector2[vertices.Length];
            var textureRect = m_Renderer.sprite.textureRect; var texture = m_Renderer.sprite.texture;
            for (var vertex = 0; vertex < vertices.Length; ++vertex)
            {
                var color = Color.black;
                foreach (var channel in mesh.vertexWeights[vertex])
                    if (channel.enabled && channel.boneIndex >= 0 && channel.boneIndex < bones.Length && (!m_Selected || selected.Contains(channel.boneIndex)))
                        color += WeightColor(bones[channel.boneIndex]) * channel.weight;
                color.a = m_Renderer.color.a; colors[vertex] = color;
                var pixel = textureRect.position + Vector2.Scale(mesh.vertices[vertex] / m_Source.rect.rect.size, textureRect.size);
                uv[vertex] = pixel / new Vector2(texture.width, texture.height);
            }
            m_OverlayMesh.Clear(); m_OverlayMesh.vertices = vertices; m_OverlayMesh.colors = colors; m_OverlayMesh.uv = uv; m_OverlayMesh.triangles = m_Session.DrawIndices();
            m_OverlayMaterial.SetTexture("_MainTex", texture);
            m_OverlayMaterial.SetFloat("_Overlay", m_Overlay ? 1 : 0);
            m_OverlayMaterial.SetColor("_Tint", m_Renderer.color);
            if (m_OverlayMaterial.SetPass(0)) Graphics.DrawMeshNow(m_OverlayMesh, Matrix4x4.identity);
        }

        void DrawBrush(Vector2 center)
        {
            var selected = m_Session.selectedBones; var color = selected.Length > 0 ? WeightColor(m_Session.mesh.bones[selected[0]]) : Color.gray;
            Handles.BeginGUI();
            Handles.color = color; Handles.DrawWireDisc(center, Vector3.forward, BrushRadius());
            Handles.color = Color.gray; Handles.DrawWireDisc(center, Vector3.forward, BrushRadius() * (1 - m_Feather / 100f));
            Handles.EndGUI(); Handles.color = Color.white;
        }
        static Rect SelectionRect(Vector2 a, Vector2 b) => Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        static void DrawSelectionRect(Vector2 a, Vector2 b)
        {
            Handles.BeginGUI(); GUI.Box(SelectionRect(a, b), GUIContent.none, "SelectionRect"); Handles.EndGUI();
        }
    }
}
