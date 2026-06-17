using System;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
    internal class WeightInspector
    {
        const float kBoneLabelWidth = 100f;
        const float kSliderWidth = 82f;
        const float kValueFieldWidth = 34f;
        const float kRowHeight = 18f;
        const float kRowSpacing = 3f;
        const float kLockButtonSize = 16f;
        const float kLockButtonSpacing = 5f;
        const int kMaxVisibleBoneRows = 5;

        private static Texture2D s_RectangleTexture;
        private static Texture2D s_LockTexture;
        private static string s_RectanglePng = "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAR0lEQVQ4EWOUEJFgAIL/IIIMwMgC1ATW/Pz1c5L0S4pKgtT/ZwKRpGpG1gM2ACRALhg1gIFhNAwGTRhAMwZJqRmmh5HS7AwAK6QNp5pwEUIAAAAASUVORK5CYII=";
        private static string s_LockPng = "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAtUlEQVQ4EcVSQQrCMBBcpQ9IQKG91xfU/7+gvkDvLSjkCbojJmwmMT2JC6E7u7PDpFmRf8euYWCk3o3wG34TGPtDf7UD62M9Ka6KWJ445yY9Tw7U0MvICvZUyGx77wWHIuNYgWh7pgEL58/Vkkhnu8hDCKlUy4fjkPpIrIOsAQAyDzCpKbDcF+YXuClQsCuFQoAtWxfcgx4v0qR/ufUKogt11rlLNMMCqKcniiT6bm8jDfwWvgCpnzMxaWosGQAAAABJRU5ErkJggg==";

        private SpriteMeshDataController m_SpriteMeshDataController = new SpriteMeshDataController();
        private GUIContent[] m_BoneNameContents;
        private Color[] m_BoneColors;
        private int[] m_SelectedBoneIndices = new int[0];
        private int[] m_LockedBoneIndices = new int[0];
        private GUIStyle m_BoneLabelStyle;
        private Vector2 m_ScrollPosition;

        public BaseSpriteMeshData spriteMeshData
        {
            get { return m_SpriteMeshDataController.spriteMeshData; }
            set
            {
                if (spriteMeshData != value)
                    m_SpriteMeshDataController.spriteMeshData = value;
            }
        }

        public GUIContent[] boneNames
        {
            get { return m_BoneNameContents; }
            set { m_BoneNameContents = value; }
        }

        public Color[] boneColors
        {
            get { return m_BoneColors; }
            set { m_BoneColors = value; }
        }

        public int[] selectedBoneIndices
        {
            get { return m_SelectedBoneIndices; }
            set { m_SelectedBoneIndices = value ?? new int[0]; }
        }

        public int[] lockedBoneIndices
        {
            get { return m_LockedBoneIndices; }
            set { m_LockedBoneIndices = value ?? new int[0]; }
        }

        public ICacheUndo cacheUndo { get; set; }
        public ISelection<int> selection { get; set; }
        public bool weightsEditable { get; set; }
        public Action<int, bool> boneButtonClicked = (boneIndex, additive) => {};
        public Action<int> lockButtonClicked = (boneIndex) => {};

        public int controlID
        {
            get { return 0; }
        }

        private bool m_UndoRegistered = false;

        protected ISpriteEditor spriteEditor { get; private set; }

        public void OnInspectorGUI()
        {
            ChannelsGUI();
        }

        private void ChannelsGUI()
        {
            if (GUIUtility.hotControl == 0)
                m_UndoRegistered = false;

            if (m_BoneNameContents == null)
                return;

            bool useScroll = m_BoneNameContents.Length > kMaxVisibleBoneRows;
            if (useScroll)
                m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition, false, false, GUILayout.Height(GetVisibleBoneRowsHeight(kMaxVisibleBoneRows)));

            for (int boneIndex = 0; boneIndex < m_BoneNameContents.Length; ++boneIndex)
            {
                float weight = 0f;
                bool isWeightMixed = false;

                if (spriteMeshData != null)
                    m_SpriteMeshDataController.GetMultiEditBoneWeightData(selection, boneIndex, out weight, out isWeightMixed);

                float newWeight = weight;
                bool isLocked = IsBoneLocked(boneIndex);

                EditorGUI.BeginChangeCheck();

                WeightChannelDrawer(boneIndex, ref newWeight, isWeightMixed, isLocked);

                if (EditorGUI.EndChangeCheck())
                {
                    RegisterUndo();
                    SetMultiEditBoneWeightDataRespectingLocks(boneIndex, weight, newWeight);
                }

                if (boneIndex < m_BoneNameContents.Length - 1)
                    GUILayout.Space(kRowSpacing);
            }

            if (useScroll)
                EditorGUILayout.EndScrollView();
        }

        private static float GetVisibleBoneRowsHeight(int rowCount)
        {
            return rowCount * kRowHeight + Mathf.Max(0, rowCount - 1) * kRowSpacing;
        }

        private void WeightChannelDrawer(int boneIndex, ref float weight, bool isWeightMixed, bool isLocked)
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(kRowHeight));

            DrawBoneLabel(boneIndex);
            GUILayout.Space(8f);

            using (new EditorGUI.DisabledScope(!weightsEditable || isLocked))
            {
                EditorGUI.showMixedValue = isWeightMixed;
                weight = GUILayout.HorizontalSlider(weight, 0f, 1f, GUILayout.Width(kSliderWidth));
                GUILayout.Space(6f);

                EditorGUIUtility.fieldWidth = kValueFieldWidth;
                weight = EditorGUILayout.FloatField(weight, GUILayout.Width(kValueFieldWidth));

                weight = Mathf.Clamp01(weight);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUI.showMixedValue = false;
            EditorGUIUtility.labelWidth = -1;
            EditorGUIUtility.fieldWidth = -1;
        }

        private void DrawBoneLabel(int boneIndex)
        {
            Rect rect = GUILayoutUtility.GetRect(kBoneLabelWidth, kRowHeight, GUILayout.Width(kBoneLabelWidth), GUILayout.Height(kRowHeight));
            Color color = GetBoneColor(boneIndex);
            Event evt = Event.current;
            Rect lockRect = new Rect(rect.x, rect.y + 1f, kLockButtonSize, kLockButtonSize);
            Rect labelRect = new Rect(lockRect.xMax + kLockButtonSpacing, rect.y, rect.width - kLockButtonSize - kLockButtonSpacing, rect.height);
            bool isLockHovered = lockRect.Contains(evt.mousePosition);
            bool isHovered = labelRect.Contains(evt.mousePosition);
            bool isSelected = IsBoneSelected(boneIndex);
            bool isLocked = IsBoneLocked(boneIndex);

            if (isLockHovered && evt.type == EventType.MouseDown && evt.button == 0)
            {
                lockButtonClicked(boneIndex);
                evt.Use();
            }
            else if (isHovered && evt.type == EventType.MouseDown && evt.button == 0)
            {
                boneButtonClicked(boneIndex, evt.shift || evt.control || evt.command);
                evt.Use();
            }

            DrawLockButton(lockRect, color, isLocked, isLockHovered);
            DrawBoneNameButton(labelRect, m_BoneNameContents[boneIndex], isHovered, isSelected);
        }

        private bool IsBoneSelected(int boneIndex)
        {
            for (int i = 0; i < m_SelectedBoneIndices.Length; ++i)
                if (m_SelectedBoneIndices[i] == boneIndex)
                    return true;

            return false;
        }

        private bool IsBoneLocked(int boneIndex)
        {
            for (int i = 0; i < m_LockedBoneIndices.Length; ++i)
                if (m_LockedBoneIndices[i] == boneIndex)
                    return true;

            return false;
        }

        private void DrawLockButton(Rect rect, Color color, bool isLocked, bool isHovered)
        {
            color = isHovered ? Color.Lerp(color, Color.white, 0.25f) : color;
            color.a = 1f;
            Texture2D rectangleTexture = RectangleTexture;
            if (rectangleTexture != null)
            {
                Color previousColor = GUI.color;
                GUI.color = color;
                GUI.DrawTexture(rect, rectangleTexture, ScaleMode.StretchToFill, true);
                GUI.color = previousColor;
            }
            else
            {
                EditorGUI.DrawRect(rect, color);
            }

            Texture2D lockTexture = LockTexture;
            if (isLocked && lockTexture != null)
                GUI.DrawTexture(rect, lockTexture, ScaleMode.StretchToFill, true);
        }

        private void DrawBoneNameButton(Rect rect, GUIContent content, bool isHovered, bool isSelected)
        {
            if (isSelected)
                EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f, 1f));
            else if (isHovered)
                EditorGUI.DrawRect(rect, new Color(0.25f, 0.25f, 0.25f, 1f));

            GUI.Label(rect, content, GetBoneLabelStyle());
        }

        private Color GetBoneColor(int boneIndex)
        {
            if (m_BoneColors == null || boneIndex < 0 || boneIndex >= m_BoneColors.Length)
                return new Color(0.35f, 0.35f, 0.35f, 1f);

            Color color = m_BoneColors[boneIndex];
            color.a = 1f;
            return color;
        }

        private static Texture2D RectangleTexture
        {
            get { return s_RectangleTexture ?? (s_RectangleTexture = MakeTexture(ref s_RectanglePng)); }
        }

        private static Texture2D LockTexture
        {
            get { return s_LockTexture ?? (s_LockTexture = MakeTexture(ref s_LockPng)); }
        }

        private static Texture2D MakeTexture(ref string base64)
        {
            if (string.IsNullOrEmpty(base64))
                return null;

            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                if (texture.LoadImage(bytes, false))
                {
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.filterMode = FilterMode.Bilinear;
                    texture.hideFlags = HideFlags.HideAndDontSave;
                    return texture;
                }

                UnityEngine.Object.DestroyImmediate(texture);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"WeightInspector: failed to load embedded texture: {exception.Message}");
            }

            return null;
        }

        private GUIStyle GetBoneLabelStyle()
        {
            if (m_BoneLabelStyle == null)
            {
                m_BoneLabelStyle = new GUIStyle(EditorStyles.label);
                m_BoneLabelStyle.alignment = TextAnchor.MiddleLeft;
                m_BoneLabelStyle.padding = new RectOffset(4, 4, 0, 0);
                m_BoneLabelStyle.normal.textColor = EditorStyles.label.normal.textColor;
            }

            return m_BoneLabelStyle;
        }

        private void SetMultiEditBoneWeightDataRespectingLocks(int boneIndex, float oldWeight, float newWeight)
        {
            if (IsBoneLocked(boneIndex) || Mathf.Abs(oldWeight - newWeight) <= Mathf.Epsilon)
                return;

            int[] indices = selection.elements;
            for (int i = 0; i < indices.Length; ++i)
            {
                if (indices[i] < 0 || indices[i] >= spriteMeshData.vertexWeights.Length)
                    continue;

                SetBoneWeightRespectingLocks(spriteMeshData.vertexWeights[indices[i]], boneIndex, newWeight, spriteMeshData.boneCount);
            }
        }

        private void SetBoneWeightRespectingLocks(EditableBoneWeight editableBoneWeight, int boneIndex, float newWeight, int boneCount)
        {
            float[] weights = GetWeightArray(editableBoneWeight, boneCount);
            float lockedWeight = SumLockedWeights(weights);
            float availableWeight = Mathf.Max(0f, 1f - lockedWeight);
            weights[boneIndex] = Mathf.Clamp(newWeight, 0f, availableWeight);

            float remainingWeight = Mathf.Max(0f, 1f - lockedWeight - weights[boneIndex]);
            float otherWeightSum = 0f;
            int otherBoneCount = 0;

            for (int i = 0; i < boneCount; ++i)
            {
                if (i == boneIndex || IsBoneLocked(i))
                    continue;

                otherWeightSum += weights[i];
                ++otherBoneCount;
            }

            for (int i = 0; i < boneCount; ++i)
            {
                if (i == boneIndex || IsBoneLocked(i))
                    continue;

                if (otherBoneCount == 0)
                    weights[i] = 0f;
                else if (otherWeightSum > 0f)
                    weights[i] = remainingWeight * weights[i] / otherWeightSum;
                else
                    weights[i] = remainingWeight / otherBoneCount;
            }

            SetFromWeightArray(editableBoneWeight, weights);
        }

        private static float[] GetWeightArray(EditableBoneWeight editableBoneWeight, int boneCount)
        {
            float[] weights = new float[boneCount];
            for (int i = 0; i < editableBoneWeight.Count; ++i)
            {
                BoneWeightChannel channel = editableBoneWeight[i];
                if (channel.enabled && channel.boneIndex >= 0 && channel.boneIndex < boneCount)
                    weights[channel.boneIndex] += channel.weight;
            }

            return weights;
        }

        private float SumLockedWeights(float[] weights)
        {
            float sum = 0f;
            for (int i = 0; i < weights.Length; ++i)
                if (IsBoneLocked(i))
                    sum += weights[i];

            return Mathf.Clamp01(sum);
        }

        private static void SetFromWeightArray(EditableBoneWeight editableBoneWeight, float[] weights)
        {
            editableBoneWeight.Clear();
            for (int i = 0; i < weights.Length; ++i)
                if (weights[i] > 0f)
                    editableBoneWeight.AddChannel(i, weights[i], true);

            editableBoneWeight.UnifyChannelsWithSameBoneIndex();
            editableBoneWeight.Clamp(4);
            editableBoneWeight.Normalize();
            editableBoneWeight.FilterChannels(0f);
        }

        private void RegisterUndo()
        {
            if (m_UndoRegistered)
                return;

            Debug.Assert(cacheUndo != null);

            cacheUndo.BeginUndoOperation(TextContent.editWeights);

            m_UndoRegistered = true;
        }
    }
}
