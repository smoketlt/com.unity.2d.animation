using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    [CustomEditor(typeof(UniskinSpriteMask))]
    internal sealed class UniskinSpriteMaskEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            root.Add(new Button(() => Run(true)) { text = "Capture Placement", tooltip = "Capture the mask renderer's current placement on the Sprite surface." });
            root.Add(new Button(() => Run(false)) { text = "Fit Mask to Sprite", tooltip = "Fit the mask image to the complete Sprite image." });
            return root;
        }

        void Run(bool capture)
        {
            var mask = target as UniskinSpriteMask;
            if (mask == null) return;
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(mask, "Change Uniskin Mask Placement");
            Undo.RecordObject(mask.GetComponent<SpriteRenderer>(), "Change Uniskin Mask Placement");
            var legacy = mask.placementSource != null ? mask.placementSource.GetComponent<SpriteMask>() : null;
            if (legacy != null) Undo.RecordObject(legacy, "Replace Sprite Mask");
            try
            {
                if (capture) mask.CapturePlacement(); else mask.ResetPlacement();
                EditorUtility.SetDirty(mask);
                if (PrefabUtility.IsPartOfPrefabInstance(mask)) PrefabUtility.RecordPrefabInstancePropertyModifications(mask);
                serializedObject.Update();
            }
            catch (Exception ex) { EditorUtility.DisplayDialog("Uniskin Mask", ex.Message, "OK"); }
        }
    }
}
