using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    internal class AnimationPreviewPanel : VisualElement
    {
        readonly ObjectField m_ClipField;
        readonly Button m_FirstButton;
        readonly Button m_PreviousButton;
        readonly Button m_PlayButton;
        readonly Button m_StopButton;
        readonly Button m_NextButton;
        readonly Button m_LastButton;
        readonly SliderInt m_FrameSlider;
        readonly IntegerField m_FrameField;
        readonly Label m_FrameCountLabel;
        readonly Toggle m_LoopToggle;
        readonly Label m_StatusLabel;

        public event Action<AnimationClip> clipChanged = delegate { };
        public event Action firstFrameClicked = delegate { };
        public event Action previousFrameClicked = delegate { };
        public event Action playPauseClicked = delegate { };
        public event Action stopClicked = delegate { };
        public event Action nextFrameClicked = delegate { };
        public event Action lastFrameClicked = delegate { };
        public event Action<int> frameChanged = delegate { };
        public event Action<bool> loopChanged = delegate { };

        public AnimationPreviewPanel()
        {
            name = "AnimationPreviewPanel";
            styleSheets.Add(ResourceLoader.Load<StyleSheet>("SkinningModule/AnimationPreviewPanel.uss"));
            AddToClassList("animation-preview-panel");

            m_ClipField = new ObjectField("Animation")
            {
                name = "AnimationClipField",
                objectType = typeof(AnimationClip),
                allowSceneObjects = false
            };
            m_ClipField.AddToClassList("animation-preview-clip");

            m_FirstButton = CreateButton("|<", "First frame", () => firstFrameClicked());
            m_PreviousButton = CreateButton("<", "Previous frame", () => previousFrameClicked());
            m_PlayButton = CreateButton("Play", "Play or pause animation preview", () => playPauseClicked());
            m_PlayButton.AddToClassList("animation-preview-play");
            m_StopButton = CreateButton("Stop", "Stop and restore the pose from before animation preview", () => stopClicked());
            m_NextButton = CreateButton(">", "Next frame", () => nextFrameClicked());
            m_LastButton = CreateButton(">|", "Last frame", () => lastFrameClicked());

            m_FrameSlider = new SliderInt(0, 1)
            {
                name = "AnimationFrameSlider",
                showInputField = false
            };
            m_FrameSlider.AddToClassList("animation-preview-slider");

            m_FrameField = new IntegerField("Frame")
            {
                name = "AnimationFrameField",
                isDelayed = false
            };
            m_FrameField.AddToClassList("animation-preview-frame");

            m_FrameCountLabel = new Label("/ 0");
            m_FrameCountLabel.AddToClassList("animation-preview-frame-count");

            m_LoopToggle = new Toggle("Loop")
            {
                name = "AnimationLoopToggle",
                value = true
            };

            m_StatusLabel = new Label("Select an AnimationClip");
            m_StatusLabel.AddToClassList("animation-preview-status");

            Add(m_ClipField);
            Add(m_FirstButton);
            Add(m_PreviousButton);
            Add(m_PlayButton);
            Add(m_StopButton);
            Add(m_NextButton);
            Add(m_LastButton);
            Add(m_FrameSlider);
            Add(m_FrameField);
            Add(m_FrameCountLabel);
            Add(m_LoopToggle);
            Add(m_StatusLabel);

            m_ClipField.RegisterValueChangedCallback(evt => clipChanged(evt.newValue as AnimationClip));
            m_FrameSlider.RegisterValueChangedCallback(evt => frameChanged(evt.newValue));
            m_FrameField.RegisterValueChangedCallback(evt => frameChanged(evt.newValue));
            m_LoopToggle.RegisterValueChangedCallback(evt => loopChanged(evt.newValue));

            SetClipState(false, 0, 0, false, "Select an AnimationClip");
            RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());
            RegisterCallback<MouseUpEvent>(evt => evt.StopPropagation());
        }

        public bool loop
        {
            get => m_LoopToggle.value;
            set => m_LoopToggle.SetValueWithoutNotify(value);
        }

        public void SetClipState(bool hasClip, int frame, int lastFrame, bool isPlaying, string status)
        {
            int clampedLastFrame = Mathf.Max(0, lastFrame);
            int clampedFrame = Mathf.Clamp(frame, 0, clampedLastFrame);

            m_FirstButton.SetEnabled(hasClip);
            m_PreviousButton.SetEnabled(hasClip);
            m_PlayButton.SetEnabled(hasClip);
            m_StopButton.SetEnabled(hasClip);
            m_NextButton.SetEnabled(hasClip);
            m_LastButton.SetEnabled(hasClip);
            m_FrameSlider.SetEnabled(hasClip);
            m_FrameField.SetEnabled(hasClip);
            m_LoopToggle.SetEnabled(hasClip);

            m_FrameSlider.lowValue = 0;
            m_FrameSlider.highValue = Mathf.Max(1, clampedLastFrame);
            m_FrameSlider.SetValueWithoutNotify(clampedFrame);
            m_FrameField.SetValueWithoutNotify(clampedFrame);
            m_FrameCountLabel.text = $"/ {clampedLastFrame}";
            m_PlayButton.text = isPlaying ? "Pause" : "Play";
            m_StatusLabel.text = status ?? string.Empty;
        }

        static Button CreateButton(string text, string tooltip, Action clicked)
        {
            Button button = new Button(clicked)
            {
                text = text,
                tooltip = tooltip
            };
            button.AddToClassList("animation-preview-button");
            return button;
        }
    }
}
