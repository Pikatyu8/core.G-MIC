using System;
using System.Collections.Generic;
using Modding.Menu;
using Modding.Menu.Config;
using UnityEngine;
using UnityEngine.UI;

namespace SidePanelLib
{
    /// <summary>Optional tuning knobs. All values are in canvas units (1920x1080 reference).</summary>
    public sealed class SidePanelOptions
    {
        /// <summary>Prefix for the created GameObjects. Use your mod name to keep the hierarchy readable.</summary>
        public string Name { get; set; } = "SidePanel";

        /// <summary>Gap between the panel and the right edge of the screen.</summary>
        public float RightInset { get; set; } = 120f;

        /// <summary>Shift from the vertical center of the screen. Positive = up.</summary>
        public float VerticalOffset { get; set; } = 0f;

        public float ButtonHeight { get; set; } = 44f;

        /// <summary>Vertical gap between buttons.</summary>
        public float Spacing { get; set; } = 12f;

        public int FontSize { get; set; } = 26;

        /// <summary>Extra width added to the text width of each button.</summary>
        public float ButtonPadding { get; set; } = 24f;
    }

    public sealed class SidePanel
    {
        private const float PanelWidth = 560f;
        private const float InitialButtonWidth = 440f;   // overridden by the layout group; kept for parity
        private const float FallbackRightEdge = 960f;    // half of the 1920 reference width

        private readonly MenuScreen _screen;
        private readonly Action<MenuSelectable> _cancelAction;
        private readonly SidePanelOptions _options;
        private readonly RectTransform _rect;
        private readonly ContentArea _area;
        private readonly List<UnityEngine.UI.MenuButton> _buttons = new List<UnityEngine.UI.MenuButton>();
        private readonly List<Func<string>> _labels = new List<Func<string>>();

        /// <summary>The panel's root GameObject (e.g. to hide it with SetActive).</summary>
        public GameObject Root { get; }

        /// <summary>The buttons in top-to-bottom order.</summary>
        public IReadOnlyList<UnityEngine.UI.MenuButton> Buttons => _buttons;

        /// <summary>
        /// Creates the panel on <paramref name="screen"/>.
        /// </summary>
        /// <param name="screen">Any already-built MenuScreen.</param>
        /// <param name="cancelAction">
        /// What Escape / B does while one of the panel's buttons is selected.
        /// Normally "go back to the mod list".
        /// </param>
        public static SidePanel Attach(
            MenuScreen screen,
            Action<MenuSelectable> cancelAction,
            SidePanelOptions options = null)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            if (cancelAction == null) throw new ArgumentNullException(nameof(cancelAction));

            return new SidePanel(screen, cancelAction, options ?? new SidePanelOptions());
        }

        private SidePanel(MenuScreen screen, Action<MenuSelectable> cancelAction, SidePanelOptions options)
        {
            _screen = screen;
            _cancelAction = cancelAction;
            _options = options;

            Root = new GameObject(options.Name + "_Panel", typeof(RectTransform));
            Root.transform.SetParent(screen.transform, false);

            _rect = (RectTransform)Root.transform;
            _rect.anchorMin = new Vector2(0.5f, 0.5f);
            _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(1f, 0.5f);
            _rect.sizeDelta = new Vector2(PanelWidth, 0f);

            VerticalLayoutGroup vlg = Root.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleRight;
            vlg.spacing = options.Spacing;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;

            _area = new ContentArea(
                Root,
                new SingleContentLayout(new AnchoredPosition(
                    new Vector2(1f, 0.5f),
                    new Vector2(1f, 0.5f),
                    Vector2.zero)));

            Reposition();
        }

        /// <summary>Adds a button whose caption is a fixed string.</summary>
        public SidePanel AddButton(string label, Action onSubmit)
        {
            return AddButton(() => label, onSubmit);
        }

        /// <summary>
        /// Adds a button whose caption is re-evaluated on every Refresh() and after every click.
        /// </summary>
        public SidePanel AddButton(Func<string> label, Action onSubmit)
        {
            if (label == null) throw new ArgumentNullException(nameof(label));

            UnityEngine.UI.MenuButton button;
            _area.AddMenuButton(
                _options.Name + "_Button" + _buttons.Count,
                new MenuButtonConfig
                {
                    Label = label(),
                    CancelAction = _cancelAction,
                    SubmitAction = _ =>
                    {
                        onSubmit?.Invoke();
                        Refresh();
                    },
                    Style = MenuButtonStyle.VanillaStyle,
                    Proceed = false
                },
                out button);

            _buttons.Add(button);
            _labels.Add(label);

            StyleButton(button);
            ResizePanel();
            Reposition();
            return this;
        }

        /// <summary>
        /// Adds a "Label: On/Off" button that flips a boolean when pressed.
        /// </summary>
        public SidePanel AddToggle(
            string label,
            Func<bool> getValue,
            Action<bool> setValue,
            string onText = "On",
            string offText = "Off")
        {
            if (getValue == null) throw new ArgumentNullException(nameof(getValue));
            if (setValue == null) throw new ArgumentNullException(nameof(setValue));

            return AddButton(
                () => label + ": " + (getValue() ? onText : offText),
                () => setValue(!getValue()));
        }

        /// <summary>
        /// Re-reads every caption, refits button widths and re-centers the panel.
        /// Call it after changing state from outside the panel.
        /// </summary>
        public void Refresh()
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                UnityEngine.UI.MenuButton button = _buttons[i];
                if (button == null)
                {
                    continue; // screen was destroyed
                }

                Text text = button.GetComponentInChildren<Text>(true);
                if (text != null)
                {
                    text.text = _labels[i]();
                }

                FitWidth(button);
            }

            Reposition();
        }

        /// <summary>
        /// Aligns the panel to the right edge / vertical center of the root canvas.
        /// Called automatically; public in case you change resolution at runtime.
        /// </summary>
        public void Reposition()
        {
            float rightEdge = FallbackRightEdge;
            float centerY = 0f;

            RectTransform screenRt = _screen.GetComponent<RectTransform>();
            // includeInactive: menu screens are usually inactive while being built.
            Canvas[] canvases = _screen.GetComponentsInParent<Canvas>(true);

            if (screenRt != null && canvases.Length > 0)
            {
                RectTransform canvasRt = canvases[0].rootCanvas.GetComponent<RectTransform>();
                Vector3 world = canvasRt.TransformPoint(
                    new Vector3(canvasRt.rect.xMax, canvasRt.rect.center.y, 0f));
                Vector3 local = screenRt.InverseTransformPoint(world);

                // anchoredPosition is relative to the parent's rect center, not its pivot.
                Vector2 parentCenter = screenRt.rect.center;
                rightEdge = local.x - parentCenter.x;
                centerY = local.y - parentCenter.y;
            }

            _rect.anchoredPosition = new Vector2(
                rightEdge - _options.RightInset,
                centerY + _options.VerticalOffset);
        }

        private void ResizePanel()
        {
            int n = _buttons.Count;
            float height = n == 0 ? 0f : n * _options.ButtonHeight + (n - 1) * _options.Spacing;
            _rect.sizeDelta = new Vector2(PanelWidth, height);
        }

        private void StyleButton(UnityEngine.UI.MenuButton button)
        {
            if (button == null)
            {
                return;
            }

            ContentSizeFitter fitter = button.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                fitter.enabled = false;
            }

            HorizontalLayoutGroup hlg = button.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.childAlignment = TextAnchor.MiddleRight;
            }

            RectTransform rt = button.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.sizeDelta = new Vector2(InitialButtonWidth, _options.ButtonHeight);
            }

            LayoutElement layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = _options.ButtonHeight;
            layout.preferredHeight = _options.ButtonHeight;

            Text text = button.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                ContentSizeFitter textFitter = text.GetComponent<ContentSizeFitter>();
                if (textFitter != null)
                {
                    textFitter.enabled = false;
                }

                RectTransform textRt = text.GetComponent<RectTransform>();
                if (textRt != null)
                {
                    textRt.anchorMin = Vector2.zero;
                    textRt.anchorMax = Vector2.one;
                    textRt.offsetMin = Vector2.zero;
                    textRt.offsetMax = Vector2.zero;
                }

                text.fontSize = _options.FontSize;
                text.alignment = TextAnchor.MiddleCenter;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
            }

            FitWidth(button);
        }

        private void FitWidth(UnityEngine.UI.MenuButton button)
        {
            Text text = button.GetComponentInChildren<Text>(true);
            LayoutElement layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();

            if (text != null)
            {
                float width = text.preferredWidth + _options.ButtonPadding;
                layout.minWidth = width;
                layout.preferredWidth = width;
            }

            RectTransform rt = button.GetComponent<RectTransform>();
            if (rt != null)
            {
                LayoutRebuilder.MarkLayoutForRebuild(rt);
            }
        }
    }
}