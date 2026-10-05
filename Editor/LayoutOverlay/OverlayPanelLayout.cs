using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Layout
{
    internal class OverlayPanelLayout
    {
        [Serializable]
        class SavedLayout
        {
            public Rect rect;
        }

        readonly VisualElement m_Panel;
        readonly Vector2? m_MinimumSize;
        readonly StyleLength m_DefaultWidth;
        readonly StyleLength m_DefaultHeight;
        VisualElement m_Parent;
        string m_LayoutKey;
        Rect m_PreferredRect;
        Vector2 m_ResolvedMinimumSize;
        bool m_Customized;
        bool m_Initialized;

        internal string preferenceKey => "Unity.2D.Animation.SkinningPanel." + Application.dataPath + "." + m_LayoutKey;
        internal VisualElement panel => m_Panel;

        public OverlayPanelLayout(VisualElement panel, string layoutKey, Vector2? minimumSize)
        {
            m_Panel = panel;
            m_MinimumSize = minimumSize;
            m_DefaultWidth = panel.style.width;
            m_DefaultHeight = panel.style.height;
            m_LayoutKey = layoutKey;
            Load();
            panel.RegisterCallback<AttachToPanelEvent>(OnAttach);
            panel.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            panel.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        public void SetLayoutKey(string layoutKey)
        {
            if (m_LayoutKey == layoutKey)
                return;

            Save();
            m_LayoutKey = layoutKey;
            m_Panel.style.position = Position.Relative;
            m_Panel.style.left = StyleKeyword.Auto;
            m_Panel.style.top = StyleKeyword.Auto;
            m_Panel.style.right = StyleKeyword.Auto;
            m_Panel.style.bottom = StyleKeyword.Auto;
            m_Panel.style.width = m_DefaultWidth;
            m_Panel.style.height = m_DefaultHeight;
            Load();
            Restore();
            m_Panel.schedule.Execute(Restore);
        }

        void Load()
        {
            m_Customized = false;
            string json = EditorPrefs.GetString(preferenceKey, "");
            if (string.IsNullOrEmpty(json))
                return;
            try
            {
                var saved = JsonUtility.FromJson<SavedLayout>(json);
                if (saved != null && IsValidRect(saved.rect))
                {
                    m_PreferredRect = saved.rect;
                    m_Customized = true;
                }
            }
            catch (ArgumentException) { }
        }

        public void Save()
        {
            if (m_Customized && IsValidRect(m_PreferredRect))
                EditorPrefs.SetString(preferenceKey, JsonUtility.ToJson(new SavedLayout { rect = m_PreferredRect }));
        }

        void OnAttach(AttachToPanelEvent evt)
        {
            m_Parent = m_Panel.parent;
            if (m_Parent != null)
                m_Parent.RegisterCallback<GeometryChangedEvent>(OnParentGeometryChanged);
            m_Panel.schedule.Execute(Restore);
        }

        void OnDetach(DetachFromPanelEvent evt)
        {
            Save();
            if (m_Parent != null)
                m_Parent.UnregisterCallback<GeometryChangedEvent>(OnParentGeometryChanged);
            m_Parent = null;
        }

        void OnGeometryChanged(GeometryChangedEvent evt) => Restore();
        void OnParentGeometryChanged(GeometryChangedEvent evt) => Restore();

        void Restore()
        {
            if (m_Panel.parent == null || !IsValidRect(m_Panel.parent.layout))
                return;

            if (!m_Initialized)
            {
                if (m_Panel.resolvedStyle.display == DisplayStyle.None || !IsValidRect(m_Panel.layout))
                    return;
                m_ResolvedMinimumSize = m_MinimumSize ?? m_Panel.layout.size;
                m_Initialized = true;
            }

            if (m_Customized)
                Apply(ClampRect(m_PreferredRect, m_Panel.parent.layout.size, m_ResolvedMinimumSize));
        }

        public Rect BeginInteraction()
        {
            Restore();
            Rect rect = m_Panel.layout;
            m_PreferredRect = rect;
            m_Customized = true;
            Apply(rect);
            m_Panel.BringToFront();
            return rect;
        }

        public void UpdateInteraction(Rect rect, bool resize = false)
        {
            if (m_Panel.parent == null)
                return;
            if (resize)
            {
                Vector2 remaining = m_Panel.parent.layout.size - rect.position;
                rect.width = Mathf.Min(rect.width, remaining.x);
                rect.height = Mathf.Min(rect.height, remaining.y);
            }
            m_PreferredRect = ClampRect(rect, m_Panel.parent.layout.size, m_ResolvedMinimumSize);
            Apply(m_PreferredRect);
        }

        void Apply(Rect rect)
        {
            m_Panel.style.position = Position.Absolute;
            m_Panel.style.left = rect.x;
            m_Panel.style.top = rect.y;
            m_Panel.style.right = StyleKeyword.Auto;
            m_Panel.style.bottom = StyleKeyword.Auto;
            m_Panel.style.width = rect.width;
            m_Panel.style.height = rect.height;
        }

        internal static Rect ClampRect(Rect rect, Vector2 availableSize, Vector2 minimumSize)
        {
            rect.width = Mathf.Clamp(rect.width, Mathf.Min(minimumSize.x, availableSize.x), availableSize.x);
            rect.height = Mathf.Clamp(rect.height, Mathf.Min(minimumSize.y, availableSize.y), availableSize.y);
            rect.x = Mathf.Clamp(rect.x, 0, availableSize.x - rect.width);
            rect.y = Mathf.Clamp(rect.y, 0, availableSize.y - rect.height);
            return rect;
        }

        internal static bool IsValidRect(Rect rect)
        {
            return IsFinite(rect.x) && IsFinite(rect.y) && IsFinite(rect.width) && IsFinite(rect.height) &&
                rect.width > 0 && rect.height > 0;
        }

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
