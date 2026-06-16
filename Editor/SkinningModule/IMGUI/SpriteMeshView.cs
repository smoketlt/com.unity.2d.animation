using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal class SpriteMeshView : ISpriteMeshView
    {
        readonly int m_VertexHashCode = "Vertex".GetHashCode();
        readonly int m_EdgeHashCode = "Edge".GetHashCode();
        const string kDeleteCommandName = "Delete";
        const string kSoftDeleteCommandName = "SoftDelete";
        static readonly Color kEdgeColor = Color.cyan;
        static readonly Color kEdgeHoveredColor = Color.yellow;
        static readonly Color kEdgeSelectedColor = Color.yellow;
        const float kEdgeWidth = 2f;
        const float kVertexHitRadius = 16f;
        const float kNewGeometryFrameHitRadius = 16f;
        const float kWeightVertexRadius = 16f;
        const float kMinWeightSlice = 0.001f;
        const int kWeightVertexTextureSize = 32;
        const int kMaxWeightVertexTextureCacheSize = 512;

        private class Styles
        {
            public readonly GUIStyle pointNormalStyle;
            public readonly GUIStyle pointHoveredStyle;
            public readonly GUIStyle pointSelectedStyle;

            public Styles()
            {
                Texture2D pointNormal = ResourceLoader.Load<Texture2D>("SkinningModule/dotCyan.png");
                Texture2D pointHovered = ResourceLoader.Load<Texture2D>("SkinningModule/dotYellow.png");
                Texture2D pointSelected = ResourceLoader.Load<Texture2D>("SkinningModule/dotYellow.png");

                pointNormalStyle = new GUIStyle();
                pointNormalStyle.normal.background = pointNormal;
                pointNormalStyle.fixedWidth = 16f;
                pointNormalStyle.fixedHeight = 16f;

                pointHoveredStyle = new GUIStyle();
                pointHoveredStyle.normal.background = pointHovered;
                pointHoveredStyle.fixedWidth = 20f;
                pointHoveredStyle.fixedHeight = 20f;

                pointSelectedStyle = new GUIStyle();
                pointSelectedStyle.normal.background = pointSelected;
                pointSelectedStyle.fixedWidth = 20f;
                pointSelectedStyle.fixedHeight = 20f;
            }
        }

        private Styles m_Styles;
        private Styles styles
        {
            get
            {
                if (m_Styles == null)
                    m_Styles = new Styles();

                return m_Styles;
            }
        }

        int m_HoveredEdge = -1;
        int m_HoveredEdgeControlID = -1;
        int m_MoveEdgeControlID = -1;
        int m_HoveredVertex = -1;
        int m_PrevHoveredVertex = -1;
        int m_HoveredVertexControlID = -1;
        int m_MoveVertexControlID = -1;
        Color m_TempColor;
        SliderData m_HotSliderData = SliderData.zero;
        readonly List<BoneWeightData> m_WeightVertexSlices = new List<BoneWeightData>(4);
        readonly Dictionary<int, Texture2D> m_WeightVertexTextureCache = new Dictionary<int, Texture2D>();
        MeshEditorAction m_PreviousActiveAction = MeshEditorAction.None;
        private Vector2 m_MouseWorldPosition;
        private float m_NearestVertexDistance;
        private float m_NearestEdgeDistance;
        private int m_NearestVertex = -1;
        private int m_NearestEdge = -1;
        private bool m_CreateEdgeDragActive;


        public SpriteMeshViewMode mode { get; set; }
        public ISelection<int> selection { get; set; }
        public int defaultControlID { get; set; }
        public bool drawVertexWeights { get; set; }
        public BoneCache[] vertexWeightBones { get; set; }
        public Rect frame { get; set; }
        private IGUIWrapper guiWrapper { get; set; }

        public Vector2 mouseWorldPosition
        {
            get { return m_MouseWorldPosition; }
        }

        public int hoveredVertex
        {
            get { return m_HoveredVertex; }
        }

        public int hoveredEdge
        {
            get { return m_HoveredEdge; }
        }

        public int closestEdge
        {
            get { return m_NearestEdge; }
        }

        public SpriteMeshView(IGUIWrapper gw)
        {
            guiWrapper = gw;
        }

        public void BeginLayout()
        {
            int vertexControlID = guiWrapper.GetControlID(m_VertexHashCode, FocusType.Passive);
            int edgeControlID = guiWrapper.GetControlID(m_EdgeHashCode, FocusType.Passive);

            if (guiWrapper.eventType == EventType.Layout || guiWrapper.eventType == EventType.MouseMove)
            {
                m_NearestVertexDistance = float.MaxValue;
                m_NearestEdgeDistance = float.MaxValue;
                m_NearestVertex = -1;
                m_NearestEdge = -1;
                m_MouseWorldPosition = guiWrapper.GUIToWorld(guiWrapper.mousePosition);
                m_HoveredVertexControlID = vertexControlID;
                m_HoveredEdgeControlID = edgeControlID;
                m_PrevHoveredVertex = m_HoveredVertex;
                m_HoveredVertex = -1;
                m_HoveredEdge = -1;

                if (guiWrapper.IsControlHot(0))
                {
                    m_MoveVertexControlID = -1;
                    m_MoveEdgeControlID = -1;
                }
            }
        }

        public void EndLayout()
        {
            guiWrapper.LayoutControl(m_HoveredEdgeControlID, m_NearestEdgeDistance);
            guiWrapper.LayoutControl(m_HoveredVertexControlID, m_NearestVertexDistance);

            if (guiWrapper.IsControlNearest(m_HoveredVertexControlID))
                m_HoveredVertex = m_NearestVertex;

            if (guiWrapper.IsControlNearest(m_HoveredEdgeControlID))
                m_HoveredEdge = m_NearestEdge;

            if (guiWrapper.eventType == EventType.Layout || guiWrapper.eventType == EventType.MouseMove)
                if (m_PrevHoveredVertex != m_HoveredVertex)
                    guiWrapper.Repaint();
        }

        public void LayoutVertex(Vector2 position, int index)
        {
            if (guiWrapper.eventType == EventType.Layout)
            {
                float distance = guiWrapper.DistanceToCircle(position, kVertexHitRadius);

                if (distance <= m_NearestVertexDistance)
                {
                    m_NearestVertexDistance = distance;
                    m_NearestVertex = index;
                }
            }
        }

        public void LayoutEdge(Vector2 startPosition, Vector2 endPosition, int index)
        {
            if (guiWrapper.eventType == EventType.Layout)
            {
                float distance = guiWrapper.DistanceToSegment(startPosition, endPosition);

                if (distance < m_NearestEdgeDistance)
                {
                    m_NearestEdgeDistance = distance;
                    m_NearestEdge = index;
                }
            }
        }

        public bool DoCreateVertex()
        {
            if (mode == SpriteMeshViewMode.CreateVertex && IsActionActive(MeshEditorAction.CreateVertex))
                ConsumeMouseMoveEvents();

            if (IsActionTriggered(MeshEditorAction.CreateVertex))
            {
                guiWrapper.SetGuiChanged(true);
                guiWrapper.UseCurrentEvent();

                return true;
            }

            return false;
        }

        public bool DoCreateNewGeometryVertex()
        {
            if (mode != SpriteMeshViewMode.NewGeometry)
                return false;

            if (!IsMouseInsideFrameForNewGeometry() ||
                hoveredVertex != -1 ||
                !guiWrapper.IsMouseDown(0) ||
                guiWrapper.clickCount > 1)
                return false;

            guiWrapper.SetGuiChanged(true);
            guiWrapper.UseCurrentEvent();
            return true;
        }

        public bool DoCancelNewGeometry()
        {
            if (mode != SpriteMeshViewMode.NewGeometry)
                return false;

            if (!guiWrapper.IsKeyDown(KeyCode.Escape))
                return false;

            guiWrapper.UseCurrentEvent();
            return true;
        }

        public bool DoCompleteNewGeometry()
        {
            if (mode != SpriteMeshViewMode.NewGeometry)
                return false;

            if (hoveredVertex != 0 ||
                !guiWrapper.IsMouseDown(0) ||
                guiWrapper.clickCount > 1)
                return false;

            guiWrapper.SetGuiChanged(true);
            guiWrapper.UseCurrentEvent();
            return true;
        }

        public bool DoDeleteNewGeometryVertex()
        {
            if (mode != SpriteMeshViewMode.NewGeometry)
                return false;

            if (hoveredVertex == -1 ||
                !guiWrapper.IsMouseDown(0) ||
                guiWrapper.clickCount < 2)
                return false;

            guiWrapper.SetGuiChanged(true);
            guiWrapper.UseCurrentEvent();
            return true;
        }

        public bool DoRemoveNewGeometryVertices()
        {
            if (mode != SpriteMeshViewMode.NewGeometry)
                return false;

            if ((guiWrapper.eventType != EventType.ValidateCommand && guiWrapper.eventType != EventType.ExecuteCommand) ||
                (guiWrapper.commandName != kSoftDeleteCommandName && guiWrapper.commandName != kDeleteCommandName))
                return false;

            if (guiWrapper.eventType == EventType.ExecuteCommand)
            {
                guiWrapper.SetGuiChanged(true);
                guiWrapper.UseCurrentEvent();
                return true;
            }

            guiWrapper.UseCurrentEvent();
            return false;
        }

        public bool DoSelectVertex(out bool additive)
        {
            additive = false;

            if (IsActionTriggered(MeshEditorAction.SelectVertex))
            {
                additive = guiWrapper.isActionKeyDown;
                guiWrapper.Repaint();
                return true;
            }

            return false;
        }

        public bool DoMoveVertex(out Vector2 delta)
        {
            delta = Vector2.zero;

            if (IsActionTriggered(MeshEditorAction.MoveVertex))
            {
                m_MoveVertexControlID = m_HoveredVertexControlID;
                m_HotSliderData.position = mouseWorldPosition;
            }

            Vector3 newPosition;
            if (guiWrapper.DoSlider(m_MoveVertexControlID, m_HotSliderData, out newPosition))
            {
                delta = newPosition - m_HotSliderData.position;
                m_HotSliderData.position = newPosition;
                return true;
            }

            return false;
        }

        public bool DoMoveEdge(out Vector2 delta)
        {
            delta = Vector2.zero;

            if (IsActionTriggered(MeshEditorAction.MoveEdge))
            {
                m_MoveEdgeControlID = m_HoveredEdgeControlID;
                m_HotSliderData.position = mouseWorldPosition;
            }

            Vector3 newPosition;
            if (guiWrapper.DoSlider(m_MoveEdgeControlID, m_HotSliderData, out newPosition))
            {
                delta = newPosition - m_HotSliderData.position;
                m_HotSliderData.position = newPosition;
                return true;
            }

            return false;
        }

        public bool DoCreateEdge()
        {
            if (IsActionActive(MeshEditorAction.CreateEdge))
                ConsumeMouseMoveEvents();

            if (IsActionTriggered(MeshEditorAction.CreateEdge))
            {
                m_CreateEdgeDragActive = false;
                guiWrapper.SetGuiChanged(true);
                guiWrapper.UseCurrentEvent();
                return true;
            }

            if (mode == SpriteMeshViewMode.CreateVertex && guiWrapper.IsMouseUp(0))
                m_CreateEdgeDragActive = false;

            return false;
        }

        public bool DoSplitEdge()
        {
            if (IsActionActive(MeshEditorAction.SplitEdge))
                ConsumeMouseMoveEvents();

            if (IsActionTriggered(MeshEditorAction.SplitEdge))
            {
                guiWrapper.UseCurrentEvent();
                guiWrapper.SetGuiChanged(true);
                return true;
            }

            return false;
        }

        public bool DoSelectEdge(out bool additive)
        {
            additive = false;

            if (IsActionTriggered(MeshEditorAction.SelectEdge))
            {
                additive = guiWrapper.isActionKeyDown;
                guiWrapper.Repaint();
                return true;
            }

            return false;
        }

        public bool DoRemove()
        {
            if (IsActionTriggered(MeshEditorAction.Remove))
            {
                guiWrapper.UseCurrentEvent();
                guiWrapper.SetGuiChanged(true);
                return true;
            }

            return false;
        }

        public void DrawVertex(Vector2 position)
        {
            DrawingUtility.DrawGUIStyleCap(0, position, Quaternion.identity, 1f, styles.pointNormalStyle);
        }

        public void DrawVertex(Vector2 position, EditableBoneWeight weight)
        {
            if (drawVertexWeights)
            {
                DrawWeightedVertex(position, weight, 0.5f);
                return;
            }

            DrawVertex(position);
        }

        public void DrawVertexHovered(Vector2 position)
        {
            DrawingUtility.DrawGUIStyleCap(0, position, Quaternion.identity, 1f, styles.pointHoveredStyle);
        }

        public void DrawVertexHovered(Vector2 position, EditableBoneWeight weight)
        {
            if (drawVertexWeights)
            {
                DrawWeightedVertex(position, weight, 0.5f);
                return;
            }

            DrawVertexHovered(position);
        }

        public void DrawVertexSelected(Vector2 position)
        {
            DrawingUtility.DrawGUIStyleCap(0, position, Quaternion.identity, 1f, styles.pointSelectedStyle);
        }

        public void DrawVertexSelected(Vector2 position, EditableBoneWeight weight)
        {
            if (drawVertexWeights)
            {
                DrawWeightedVertex(position, weight, 1f);
                return;
            }

            DrawVertexSelected(position);
        }

        public void BeginDrawEdges()
        {
            if (guiWrapper.eventType != EventType.Repaint)
                return;

            DrawingUtility.BeginSolidLines();
            m_TempColor = Handles.color;
        }

        public void EndDrawEdges()
        {
            if (guiWrapper.eventType != EventType.Repaint)
                return;

            DrawingUtility.EndLines();
            Handles.color = m_TempColor;
        }

        public void DrawEdge(Vector2 startPosition, Vector2 endPosition)
        {
            DrawEdge(startPosition, endPosition, kEdgeColor);
        }

        public void DrawEdgeHovered(Vector2 startPosition, Vector2 endPosition)
        {
            DrawEdge(startPosition, endPosition, kEdgeHoveredColor);
        }

        public void DrawEdgeSelected(Vector2 startPosition, Vector2 endPosition)
        {
            DrawEdge(startPosition, endPosition, kEdgeSelectedColor);
        }

        public bool IsActionActive(MeshEditorAction action)
        {
            if (!guiWrapper.IsControlHot(0))
                return false;

            bool canCreateEdge = CanCreateEdge();
            bool canSplitEdge = CanSplitEdge();

            if (action == MeshEditorAction.None)
                return guiWrapper.IsControlNearest(defaultControlID);

            if (action == MeshEditorAction.CreateVertex)
            {
                if (!frame.Contains(mouseWorldPosition))
                    return false;

                if (mode == SpriteMeshViewMode.EditGeometry)
                    return guiWrapper.IsControlNearest(defaultControlID) && guiWrapper.clickCount == 2;

                if (mode == SpriteMeshViewMode.CreateVertex)
                    return hoveredVertex == -1;
            }

            if (action == MeshEditorAction.MoveVertex)
            {
                if (mode == SpriteMeshViewMode.CreateVertex)
                    return false;

                return guiWrapper.IsControlNearest(m_HoveredVertexControlID);
            }

            if (action == MeshEditorAction.CreateEdge)
                return canCreateEdge;

            if (action == MeshEditorAction.SplitEdge)
                return canSplitEdge;

            if (action == MeshEditorAction.MoveEdge)
            {
                if (mode == SpriteMeshViewMode.NewGeometry)
                    return false;

                return guiWrapper.IsControlNearest(m_HoveredEdgeControlID);
            }

            if (action == MeshEditorAction.SelectVertex)
                return guiWrapper.IsControlNearest(m_HoveredVertexControlID);

            if (action == MeshEditorAction.SelectEdge)
                return mode == SpriteMeshViewMode.EditGeometry &&
                    guiWrapper.IsControlNearest(m_HoveredEdgeControlID) &&
                    !canCreateEdge && !canSplitEdge;

            if (action == MeshEditorAction.Remove)
            {
                if (mode == SpriteMeshViewMode.NewGeometry)
                    return false;

                return true;
            }

            return false;
        }

        public bool IsActionHot(MeshEditorAction action)
        {
            if (action == MeshEditorAction.None)
                return guiWrapper.IsControlHot(0);

            if (action == MeshEditorAction.MoveVertex)
                return guiWrapper.IsControlHot(m_HoveredVertexControlID);

            if (action == MeshEditorAction.MoveEdge)
                return guiWrapper.IsControlHot(m_HoveredEdgeControlID);

            return false;
        }

        public bool IsActionTriggered(MeshEditorAction action)
        {
            if (!IsActionActive(action))
                return false;

            if (action == MeshEditorAction.CreateVertex)
            {
                if (mode == SpriteMeshViewMode.EditGeometry)
                    return guiWrapper.IsMouseDown(0) && guiWrapper.clickCount == 2;
            }

            if (action == MeshEditorAction.Remove)
            {
                if ((guiWrapper.eventType == EventType.ValidateCommand || guiWrapper.eventType == EventType.ExecuteCommand)
                    && (guiWrapper.commandName == kSoftDeleteCommandName || guiWrapper.commandName == kDeleteCommandName))
                {
                    if (guiWrapper.eventType == EventType.ExecuteCommand)
                        return true;

                    guiWrapper.UseCurrentEvent();
                }

                return false;
            }

            if (action == MeshEditorAction.CreateEdge && mode == SpriteMeshViewMode.CreateVertex)
                return guiWrapper.IsMouseUp(0);

            if (action != MeshEditorAction.None)
                return guiWrapper.IsMouseDown(0);

            return false;
        }

        public Vector2 WorldToScreen(Vector2 position)
        {
            return HandleUtility.WorldToGUIPoint(position);
        }

        private void ConsumeMouseMoveEvents()
        {
            if (guiWrapper.eventType == EventType.MouseMove || (guiWrapper.eventType == EventType.MouseDrag && guiWrapper.mouseButton == 0))
                guiWrapper.UseCurrentEvent();
        }

        private bool CanCreateEdge()
        {
            if (!frame.Contains(mouseWorldPosition) || !(guiWrapper.IsControlNearest(defaultControlID) || guiWrapper.IsControlNearest(m_HoveredVertexControlID) || guiWrapper.IsControlNearest(m_HoveredEdgeControlID)))
                return false;

            if (mode == SpriteMeshViewMode.EditGeometry)
            {
                m_CreateEdgeDragActive = false;
                return false;
            }

            if (mode == SpriteMeshViewMode.NewGeometry)
            {
                m_CreateEdgeDragActive = false;
                return false;
            }

            if (mode == SpriteMeshViewMode.CreateVertex)
            {
                if (guiWrapper.IsMouseDown(0) && guiWrapper.IsControlNearest(m_HoveredVertexControlID) && hoveredVertex != -1)
                    m_CreateEdgeDragActive = true;

                return m_CreateEdgeDragActive && selection.Count == 1 && !selection.Contains(hoveredVertex);
            }

            if (mode == SpriteMeshViewMode.CreateEdge)
                return selection.Count == 1 && !selection.Contains(hoveredVertex);

            return false;
        }

        private bool IsMouseInsideFrameForNewGeometry()
        {
            if (frame.Contains(mouseWorldPosition))
                return true;

            Vector2 nearestFramePoint = new Vector2(
                Mathf.Clamp(mouseWorldPosition.x, frame.xMin, frame.xMax),
                Mathf.Clamp(mouseWorldPosition.y, frame.yMin, frame.yMax));

            return guiWrapper.DistanceToCircle(nearestFramePoint, kNewGeometryFrameHitRadius) <= 0f;
        }

        private bool CanSplitEdge()
        {
            if (!frame.Contains(mouseWorldPosition) || !(guiWrapper.IsControlNearest(defaultControlID) || guiWrapper.IsControlNearest(m_HoveredEdgeControlID)))
                return false;

            if (mode == SpriteMeshViewMode.EditGeometry)
                return false;

            if (mode == SpriteMeshViewMode.NewGeometry)
                return false;

            if (mode == SpriteMeshViewMode.SplitEdge)
                return m_NearestEdge != -1 && hoveredVertex == -1;

            return false;
        }

        private void DrawEdge(Vector2 startPosition, Vector2 endPosition, Color color)
        {
            if (guiWrapper.eventType != EventType.Repaint)
                return;

            Handles.color = color;
            float width = kEdgeWidth / Handles.matrix.m00;

            DrawingUtility.DrawSolidLine(width, startPosition, endPosition);
        }

        private void DrawWeightedVertex(Vector2 position, EditableBoneWeight weight, float opacity)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            BuildWeightVertexSlices(weight);

            Handles.BeginGUI();

            Vector2 guiPosition = HandleUtility.WorldToGUIPoint(position);
            Rect rect = new Rect(
                guiPosition.x - kWeightVertexRadius,
                guiPosition.y - kWeightVertexRadius,
                kWeightVertexRadius * 2f,
                kWeightVertexRadius * 2f);

            Color guiColor = GUI.color;
            GUI.color = new Color(guiColor.r, guiColor.g, guiColor.b, guiColor.a * opacity);
            GUI.DrawTexture(rect, GetWeightVertexTexture(), ScaleMode.StretchToFill, true);
            GUI.color = guiColor;

            Handles.EndGUI();
        }

        private void BuildWeightVertexSlices(EditableBoneWeight weight)
        {
            m_WeightVertexSlices.Clear();

            if (weight == null || vertexWeightBones == null)
                return;

            foreach (BoneWeightChannel channel in weight)
            {
                if (!channel.enabled ||
                    channel.weight <= kMinWeightSlice ||
                    channel.boneIndex < 0 ||
                    channel.boneIndex >= vertexWeightBones.Length)
                    continue;

                int existingIndex = m_WeightVertexSlices.FindIndex(slice => slice.boneIndex == channel.boneIndex);
                if (existingIndex >= 0)
                {
                    BoneWeightData existingSlice = m_WeightVertexSlices[existingIndex];
                    existingSlice.weight += channel.weight;
                    m_WeightVertexSlices[existingIndex] = existingSlice;
                }
                else
                {
                    m_WeightVertexSlices.Add(new BoneWeightData
                    {
                        boneIndex = channel.boneIndex,
                        weight = channel.weight
                    });
                }
            }

            m_WeightVertexSlices.Sort(CompareWeightVertexSlices);
        }

        private int CompareWeightVertexSlices(BoneWeightData left, BoneWeightData right)
        {
            string leftName = vertexWeightBones[left.boneIndex] != null ? vertexWeightBones[left.boneIndex].name : string.Empty;
            string rightName = vertexWeightBones[right.boneIndex] != null ? vertexWeightBones[right.boneIndex].name : string.Empty;
            int result = string.Compare(leftName, rightName, StringComparison.Ordinal);

            if (result == 0)
                result = left.boneIndex.CompareTo(right.boneIndex);

            return result;
        }

        private Texture2D GetWeightVertexTexture()
        {
            int hash = GetWeightVertexTextureHash();
            if (m_WeightVertexTextureCache.TryGetValue(hash, out Texture2D texture) && texture != null)
                return texture;

            if (m_WeightVertexTextureCache.Count > kMaxWeightVertexTextureCacheSize)
                ClearWeightVertexTextureCache();

            texture = CreateWeightVertexTexture();
            m_WeightVertexTextureCache[hash] = texture;
            return texture;
        }

        private int GetWeightVertexTextureHash()
        {
            unchecked
            {
                int hash = 17;
                float totalWeight = GetWeightVertexTotalWeight();
                if (totalWeight <= 0f)
                    return hash;

                float displayTotalWeight = Mathf.Max(1f, totalWeight);
                for (int i = 0; i < m_WeightVertexSlices.Count; ++i)
                {
                    BoneWeightData slice = m_WeightVertexSlices[i];
                    Color32 color = vertexWeightBones[slice.boneIndex].bindPoseColor;
                    int normalizedWeight = Mathf.RoundToInt(slice.weight / displayTotalWeight * 1000f);

                    hash = hash * 31 + slice.boneIndex;
                    hash = hash * 31 + normalizedWeight;
                    hash = hash * 31 + color.r;
                    hash = hash * 31 + color.g;
                    hash = hash * 31 + color.b;
                }

                return hash;
            }
        }

        private Texture2D CreateWeightVertexTexture()
        {
            Texture2D texture = new Texture2D(kWeightVertexTextureSize, kWeightVertexTextureSize, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[kWeightVertexTextureSize * kWeightVertexTextureSize];
            float totalWeight = GetWeightVertexTotalWeight();
            float displayTotalWeight = Mathf.Max(1f, totalWeight);
            float center = (kWeightVertexTextureSize - 1) * 0.5f;
            float radius = kWeightVertexTextureSize * 0.5f - 0.5f;

            for (int y = 0; y < kWeightVertexTextureSize; ++y)
            {
                for (int x = 0; x < kWeightVertexTextureSize; ++x)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    Color color = Color.clear;
                    if (distance <= radius)
                    {
                        color = totalWeight > 0f ? GetWeightVertexPixelColor(dx, dy, displayTotalWeight) : Color.black;
                        color.a = Mathf.Clamp01(radius - distance + 1f);
                    }

                    pixels[y * kWeightVertexTextureSize + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private float GetWeightVertexTotalWeight()
        {
            float totalWeight = 0f;
            for (int i = 0; i < m_WeightVertexSlices.Count; ++i)
                totalWeight += m_WeightVertexSlices[i].weight;

            return totalWeight;
        }

        private Color GetWeightVertexPixelColor(float dx, float dy, float displayTotalWeight)
        {
            float angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
            if (angle < 0f)
                angle += 360f;

            float currentAngle = 0f;
            for (int i = 0; i < m_WeightVertexSlices.Count; ++i)
            {
                BoneWeightData slice = m_WeightVertexSlices[i];
                currentAngle += 360f * slice.weight / displayTotalWeight;

                if (angle <= currentAngle)
                {
                    Color color = vertexWeightBones[slice.boneIndex].bindPoseColor;
                    color.a = 1f;
                    return color;
                }
            }

            return Color.black;
        }

        private void ClearWeightVertexTextureCache()
        {
            foreach (Texture2D texture in m_WeightVertexTextureCache.Values)
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }

            m_WeightVertexTextureCache.Clear();
        }

        public void DoRepaint()
        {
            if (guiWrapper.eventType != EventType.Layout)
                return;

            MeshEditorAction action = MeshEditorAction.None;

            if (IsActionActive(MeshEditorAction.CreateVertex))
                action = MeshEditorAction.CreateVertex;
            else if (IsActionActive(MeshEditorAction.CreateEdge))
                action = MeshEditorAction.CreateEdge;
            else if (IsActionActive(MeshEditorAction.SplitEdge))
                action = MeshEditorAction.SplitEdge;

            if (m_PreviousActiveAction != action)
            {
                m_PreviousActiveAction = action;
                guiWrapper.Repaint();
            }
        }

        public bool CanRepaint()
        {
            return guiWrapper.eventType == EventType.Repaint;
        }

        public bool CanLayout()
        {
            return guiWrapper.eventType == EventType.Layout;
        }
    }
}
