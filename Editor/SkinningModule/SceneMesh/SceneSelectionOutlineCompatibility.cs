using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    // Native Sprite outlining uses SceneView's cached renderer list independently of
    // SetSelectedRenderState. Remove only our replacement contours from that list.
    // Reflection preserves the native int[] / EntityId[] types without narrowing IDs.
    internal static class SceneSelectionOutlineCompatibility
    {
        const BindingFlags k_Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo s_Parents = typeof(SceneView).GetField("s_CachedParentRenderersForOutlining", k_Static);
        static readonly FieldInfo s_Children = typeof(SceneView).GetField("s_CachedChildRenderersForOutlining", k_Static);
        static readonly FieldInfo s_ChildSet = typeof(SceneView).GetField("s_CachedChildRenderersForOutliningHashSet", k_Static);
        static readonly FieldInfo s_Dirty = typeof(SceneView).GetField("s_SelectionCacheDirty", k_Static);
        static readonly MethodInfo s_Filter = typeof(HandleUtility).GetMethod("FilterEntityIds", k_Static) ??
            typeof(HandleUtility).GetMethod("FilterInstanceIDs", k_Static);

        internal static Color color
        {
            get
            {
#if UNITY_6000_5_OR_NEWER
                var result = SceneView.selectedOutlineColor;
#else
                var preference = typeof(SceneView).GetField("kSceneViewSelectedOutline", k_Static)?.GetValue(null);
                var value = preference?.GetType().GetProperty("Color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(preference);
                var result = value is Color stored ? stored : new Color(1f, .4f, 0f);
#endif
                result.a = 1;
                return result;
            }
        }

        internal static bool Exclude(IEnumerable<SpriteRenderer> renderers)
        {
            if (s_Parents == null || s_Children == null || s_ChildSet == null || s_Dirty == null || s_Filter == null) return false;
            var excluded = new HashSet<object>();
            foreach (var renderer in renderers)
                if (renderer != null) excluded.Add(UnityObjectCompatibility.GetObjectId(renderer));
            if (excluded.Count == 0) return false;
            var args = new object[] { Selection.gameObjects, null, null, null };
            s_Filter.Invoke(null, args);
            s_Parents.SetValue(null, Remove((Array)args[1], excluded));
            s_Children.SetValue(null, Remove((Array)args[2], excluded));
            // Keep the complete membership set for native destroyed-child invalidation.
            s_ChildSet.SetValue(null, args[3]);
            s_Dirty.SetValue(null, false);
            return true;
        }

        internal static Array Remove(Array values, HashSet<object> excluded)
        {
            if (values == null) return null;
            var count = 0;
            foreach (var value in values) if (!excluded.Contains(value)) ++count;
            var result = Array.CreateInstance(values.GetType().GetElementType(), count);
            var index = 0;
            foreach (var value in values) if (!excluded.Contains(value)) result.SetValue(value, index++);
            return result;
        }

        internal static void Invalidate() => s_Dirty?.SetValue(null, true);
    }
}
