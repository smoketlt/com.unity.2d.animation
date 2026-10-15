using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D.Animation;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    [CustomEditor(typeof(UniskinMeshMask)), CanEditMultipleObjects]
    internal sealed class UniskinMeshMaskEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var alphaRow = Row("Alpha Cutoff");
            var alpha = new Slider(0, 1) { showInputField = true };
            Compact(alpha);
            alpha.BindProperty(serializedObject.FindProperty("m_AlphaCutoff"));
            alphaRow.Add(alpha);
            root.Add(alphaRow);
            var layers = Row("Sorting Layers");
            layers.Add(Compact(Layer("m_BackSortingLayerID", "")));
            layers.Add(Compact(Layer("m_FrontSortingLayerID", "")));
            root.Add(layers);
            root.Add(CreateOrderRange());
            return root;
        }

        VisualElement CreateOrderRange()
        {
            var root = new VisualElement();
            var back = serializedObject.FindProperty("m_BackSortingOrder");
            var front = serializedObject.FindProperty("m_FrontSortingOrder");
            var backLayer = serializedObject.FindProperty("m_BackSortingLayerID");
            var frontLayer = serializedObject.FindProperty("m_FrontSortingLayerID");
            var backField = new IntegerField { isDelayed = true, tooltip = "Back Order in Layer" };
            var frontField = new IntegerField { isDelayed = true, tooltip = "Front Order in Layer" };
            var orders = Row("Mask Order Range");
            orders.Add(Compact(backField));
            orders.Add(Compact(frontField));
            root.Add(orders);
            void Refresh()
            {
                serializedObject.Update();
                backField.SetValueWithoutNotify(back.intValue);
                frontField.SetValueWithoutNotify(front.intValue);
                backField.showMixedValue = back.hasMultipleDifferentValues;
                frontField.showMixedValue = front.hasMultipleDifferentValues;
            }
            void SetOrder(bool isBack, int value)
            {
                value = Mathf.Clamp(value, short.MinValue, short.MaxValue);
                foreach (var item in targets)
                {
                    var data = new SerializedObject(item);
                    var b = data.FindProperty("m_BackSortingOrder");
                    var f = data.FindProperty("m_FrontSortingOrder");
                    if (data.FindProperty("m_BackSortingLayerID").intValue == data.FindProperty("m_FrontSortingLayerID").intValue)
                    {
                        if (isBack) f.intValue = Mathf.Max(f.intValue, value);
                        else b.intValue = Mathf.Min(b.intValue, value);
                    }
                    (isBack ? b : f).intValue = value;
                    data.ApplyModifiedProperties();
                }
                Refresh();
            }
            backField.RegisterValueChangedCallback(evt => SetOrder(true, evt.newValue));
            frontField.RegisterValueChangedCallback(evt => SetOrder(false, evt.newValue));
            foreach (var property in new[] { back, front, backLayer, frontLayer })
                root.TrackPropertyValue(property, _ => Refresh());
            Refresh();
            return root;
        }

        static VisualElement Row(string label)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.Add(new Label(label) { style = { width = 120, flexShrink = 0 } });
            return row;
        }

        static BaseField<T> Compact<T>(BaseField<T> field)
        {
            field.RemoveFromClassList(BaseField<T>.alignedFieldUssClassName);
            field.style.flexGrow = 1;
            field.style.flexBasis = 0;
            field.style.minWidth = 0;
            field.labelElement.style.minWidth = 0;
            field.labelElement.style.display = DisplayStyle.None;
            return field;
        }

        PopupField<string> Layer(string propertyName, string label)
        {
            var property = serializedObject.FindProperty(propertyName);
            var names = new List<string>(); var ids = new List<int>();
            foreach (var layer in SortingLayer.layers) { names.Add(layer.name); ids.Add(layer.id); }
            var index = ids.IndexOf(property.intValue);
            if (index < 0) { names.Add("Missing layer (" + property.intValue + ")"); ids.Add(property.intValue); index = ids.Count - 1; }
            var field = new PopupField<string>(label, names, index);
            field.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            field.showMixedValue = property.hasMultipleDifferentValues;
            field.RegisterValueChangedCallback(evt =>
            {
                serializedObject.Update(); property.intValue = ids[field.index]; serializedObject.ApplyModifiedProperties();
            });
            field.TrackPropertyValue(property, p =>
            {
                field.showMixedValue = p.hasMultipleDifferentValues;
                var current = ids.IndexOf(p.intValue);
                if (current >= 0) field.SetValueWithoutNotify(names[current]);
            });
            return field;
        }
    }
}
