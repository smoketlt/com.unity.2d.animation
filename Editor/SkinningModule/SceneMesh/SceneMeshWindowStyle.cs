using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    // Unimator's physical-pixel design tokens, independent of its assemblies.
    internal static class SceneMeshWindowStyle
    {
        internal static void Configure(VisualElement root, EditorWindow window, Action close, bool resizeHeight = false)
        {
            var scale = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
            float Px(float pixels) => pixels / scale;
            var header = root.Q("windowHeader");
            var closeButton = root.Q<Button>("closeWindow");
            closeButton.clicked += close;
            bool dragging = false;
            Vector2 startMouse = default, startWindow = default;
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.target == closeButton || closeButton.Contains(evt.target as VisualElement)) return;
                startWindow = window.position.position;
                startMouse = startWindow + (Vector2)evt.position;
                dragging = true;
                header.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            header.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging || !header.HasPointerCapture(evt.pointerId)) return;
                // Moving a native popup also causes synthetic panel/layout moves.
                // Those are not cursor samples and may contain a zero position.
                if ((evt.pressedButtons & 1) == 0 || evt.imguiEvent == null ||
                    evt.imguiEvent.rawType != EventType.MouseDrag) return;
                var rect = window.position;
                rect.position = startWindow + rect.position + (Vector2)evt.position - startMouse;
                if (rect != window.position) window.position = rect;
                evt.StopPropagation();
            });
            header.RegisterCallback<PointerUpEvent>(evt => { dragging = false; header.ReleasePointer(evt.pointerId); });
            header.RegisterCallback<PointerCaptureOutEvent>(_ => dragging = false);
            root.Query<Toggle>(className: "mesh-option-toggle").ForEach(toggle =>
            {
                var checkmark = toggle.Q(className: "unity-toggle__checkmark");
                if (checkmark == null) return;
                checkmark.style.backgroundImage = StyleKeyword.None;
                var tick = new Label("\u2713") { pickingMode = PickingMode.Ignore };
                tick.AddToClassList("mesh-tick");
                checkmark.Add(tick);
                void RefreshTick() => tick.text = toggle.value ? "\u2713" : string.Empty;
                toggle.RegisterValueChangedCallback(_ => RefreshTick());
                root.schedule.Execute(RefreshTick);
            });
            var content = root.Q(className: "mesh-block");
            void FitHeight()
            {
                if (window == null || !float.IsFinite(content.layout.height) || content.layout.height <= 0) return;
                var rect = window.position;
                var height = Mathf.Ceil(content.layout.height + Px(31 + 8 + 2));
                if (Mathf.Abs(rect.height - height) < 0.5f) return;
                rect.height = height;
                window.position = rect;
            }
            if (!resizeHeight)
            {
                content.RegisterCallback<GeometryChangedEvent>(_ => root.schedule.Execute(FitHeight));
                root.schedule.Execute(FitHeight);
            }
            else
            {
                var grip = new VisualElement { name = "heightResizeGrip" };
                root.Q(className: "uniskin-mesh-window").Add(grip);
                bool resizing = false;
                float startHeight = 0, startY = 0;
                grip.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    resizing = true; startHeight = window.position.height;
                    startY = window.position.y + evt.position.y;
                    grip.CapturePointer(evt.pointerId); evt.StopPropagation();
                });
                grip.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (!resizing || !grip.HasPointerCapture(evt.pointerId) || (evt.pressedButtons & 1) == 0 ||
                        evt.imguiEvent == null || evt.imguiEvent.rawType != EventType.MouseDrag) return;
                    var rect = window.position;
                    rect.height = Mathf.Max(window.minSize.y, startHeight + rect.y + evt.position.y - startY);
                    window.position = rect; evt.StopPropagation();
                });
                grip.RegisterCallback<PointerUpEvent>(evt => { resizing = false; grip.ReleasePointer(evt.pointerId); });
                grip.RegisterCallback<PointerCaptureOutEvent>(_ => resizing = false);
            }
            var detail = root.Q<Slider>("detail");
            var alpha = root.Q<SliderInt>("alpha");
            if (detail != null && alpha != null)
            {
                var detailFill = Fill(detail);
                var alphaFill = Fill(alpha);
                detail.RegisterValueChangedCallback(evt => detailFill.style.width = Length.Percent(evt.newValue * 100));
                alpha.RegisterValueChangedCallback(evt => alphaFill.style.width = Length.Percent(evt.newValue * 100f / 254));
                root.schedule.Execute(() =>
                {
                    detailFill.style.width = Length.Percent(detail.value * 100);
                    alphaFill.style.width = Length.Percent(alpha.value * 100f / 254);
                });
            }
            if (scale == 1f) return;
            var panel = root.Q(className: "uniskin-mesh-window");
            panel.style.fontSize = Px(11);
            panel.style.borderTopWidth = panel.style.borderBottomWidth = panel.style.borderLeftWidth = panel.style.borderRightWidth = Px(1);
            header.style.height = Px(31);
            var title = root.Q("windowTitle");
            title.style.fontSize = Px(10);
            title.style.paddingLeft = Px(4);
            title.style.paddingTop = Px(10);
            title.style.paddingBottom = Px(11);
            root.Query<Button>().ForEach(button =>
            {
                button.style.height = Px(button.name == "updateBindings" ? 30 : 24);
                if (button.name == "updateBindings") button.style.minHeight = Px(30);
                button.style.borderBottomWidth = Px(1);
                button.style.paddingLeft = button.style.paddingRight = Px(4);
            });
            closeButton.style.width = closeButton.style.minWidth = closeButton.style.height = Px(19);
            closeButton.style.marginTop = closeButton.style.marginBottom = Px(6);
            closeButton.style.marginRight = Px(5);
            closeButton.style.fontSize = Px(14);
            closeButton.style.paddingLeft = closeButton.style.paddingRight = 0;
            root.Query(className: "mesh-row").ForEach(row => row.style.marginBottom = Px(4));
            root.Query(className: "mesh-row").ForEach(row =>
            {
                for (var i = 0; i < row.childCount - 1; ++i)
                    if (row[i] is Button) row[i].style.marginRight = Px(4);
            });
            var body = root.Q(className: "mesh-body");
            body.style.paddingLeft = body.style.paddingRight = body.style.paddingTop = body.style.paddingBottom = Px(4);
            var block = root.Q(className: "mesh-block");
            block.style.paddingTop = Px(6);
            block.style.paddingLeft = Px(4);
            block.style.paddingRight = block.style.paddingBottom = Px(3);
            var options = root.Q(className: "mesh-options");
            if (options != null) options.style.marginTop = Px(4);
            var count = root.Q("vertexCount");
            if (count != null) count.style.marginTop = Px(4);
            var boneProperties = root.Q(className: "bone-properties");
            if (boneProperties != null)
                foreach (var field in boneProperties.Children())
                {
                    if (!field.ClassListContains("unity-base-field")) continue;
                    field.style.height = Px(20);
                    field.style.marginBottom = Px(4);
                    var label = field.Q(className: "unity-base-field__label");
                    if (label != null) label.style.minWidth = Px(52);
                }
            root.Query(className: "mesh-tick").ForEach(tick => tick.style.fontSize = Px(11));
            var boneIcon = root.Q<Button>("boneIcon");
            if (boneIcon != null)
            {
                boneIcon.style.width = Px(24);
                boneIcon.style.paddingLeft = boneIcon.style.paddingRight = 0;
                var image = root.Q("boneIconImage");
                image.style.width = image.style.height = Px(16);
                var row = root.Q(className: "bone-color-row");
                row.style.marginBottom = Px(4);
                var color = root.Q("boneColor");
                color.style.height = Px(20);
                color.style.marginRight = Px(4);
                color.Q(className: "unity-base-field__label").style.minWidth = Px(52);
                var sizeRow = root.Q(className: "bone-length-size-row");
                sizeRow.style.marginBottom = Px(4);
                foreach (var fieldName in new[] { "boneLength", "boneIconSize" })
                {
                    var field = root.Q(fieldName);
                    field.style.height = Px(20);
                    field.Q(className: "unity-base-field__label").style.minWidth = Px(fieldName == "boneLength" ? 52 : 30);
                }
                root.Q("boneIconSize").style.marginLeft = Px(8);
            }
            options?.Query(className: "unity-toggle__checkmark").ForEach(check => check.style.width = check.style.height = Px(14));
            options?.Query(className: "unity-toggle__text").ForEach(label => label.style.marginLeft = Px(4));
            var weightStep = root.Q<FloatField>("weightStep");
            if (weightStep != null)
            {
                weightStep.style.width = Px(90); weightStep.style.height = Px(20);
                weightStep.Q(className: "unity-base-field__label").style.marginRight = Px(4);
                foreach (var name in new[] { "weightDecrease", "weightIncrease" })
                {
                    var button = root.Q<Button>(name);
                    button.style.width = button.style.minWidth = Px(24); button.style.height = Px(20);
                    button.style.marginLeft = button.style.marginRight = Px(4);
                }
            }
            foreach (var slider in new VisualElement[] { detail, alpha })
            {
                if (slider == null) continue;
                slider.style.height = Px(20);
                slider.style.marginBottom = Px(4);
                slider.Q(className: "unity-base-field__label").style.minWidth = Px(100);
                slider.Q(className: "unity-base-slider__tracker").style.height = Px(14);
                var field = slider.Q(className: "unity-base-slider__text-field");
                if (field != null) { field.style.width = Px(45); field.style.marginLeft = Px(3); }
            }
            root.schedule.Execute(() => root.Query("commandIcon").ForEach(icon =>
            {
                icon.style.width = icon.style.height = Px(16);
                icon.style.marginRight = Px(4);
            }));
        }

        static VisualElement Fill(VisualElement slider)
        {
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("mesh-slider-fill");
            slider.Q(className: "unity-base-slider__tracker").Add(fill);
            return fill;
        }
    }
}
