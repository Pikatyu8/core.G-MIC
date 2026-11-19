using System;
using System.Collections.Generic;
using Modding.Menu;
using Modding.Menu.Config;
using UnityEngine;
using UnityEngine.UI;

namespace SidePanelLib
{
    public enum SidePanelAnchor
    {
        MiddleRight,
        BottomRight
    }

    public sealed class SidePanelItem
    {
        public string Id { get; }
        public UnityEngine.UI.MenuButton Button { get; }
        public Func<string> Label { get; set; }
        public Action OnSubmit { get; set; }
        public Func<bool> IsVisible { get; set; }

        internal SidePanelItem(
            string id,
            UnityEngine.UI.MenuButton button,
            Func<string> label,
            Action onSubmit,
            Func<bool> isVisible)
        {
            Id = id;
            Button = button;
            Label = label;
            OnSubmit = onSubmit;
            IsVisible = isVisible;
        }
    }

    /// <summary>Optional tuning knobs. All values are in canvas units (1920x1080 reference).</summary>
    public sealed class SidePanelOptions
    {
        /// <summary>Prefix for the created GameObjects. Use your mod name to keep the hierarchy readable.</summary>
        public string Name { get; set; } = "SidePanel";

        /// <summary>Screen edge anchor for the panel.</summary>
        public SidePanelAnchor Anchor { get; set; } = SidePanelAnchor.MiddleRight;

        /// <summary>Gap between the panel and the right edge of the screen.</summary>
        public float RightInset { get; set; } = 120f;

        /// <summary>Gap between the panel and the bottom edge when Anchor == BottomRight.</summary>
        public float BottomInset { get; set; } = 36f;

        /// <summary>Shift from the anchor vertical baseline. Positive = up.</summary>
        public float VerticalOffset { get; set; } = 0f;

        /// <summary>When true, newly added buttons are placed above existing buttons.</summary>
        public bool StackUpwards { get; set; } = false;

        /// <summary>When true, selection arrow cursors are removed from buttons in this panel.</summary>
        public bool HideCursors { get; set; } = false;

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
        private const float FallbackBottomEdge = -540f;  // half of the 1080 reference height

        private readonly MenuScreen _screen;
        private readonly Action<MenuSelectable> _cancelAction;
        private readonly SidePanelOptions _options;
        private readonly RectTransform _rect;
        private readonly ContentArea _area;
        private readonly List<SidePanelItem> _items = new List<SidePanelItem>();
        private readonly Dictionary<string, SidePanelItem> _itemsById = new Dictionary<string, SidePanelItem>();
        private readonly List<UnityEngine.UI.MenuButton> _buttons = new List<UnityEngine.UI.MenuButton>();
        private int _buttonSerial;

        /// <summary>The panel's root GameObject (e.g. to hide it with SetActive).</summary>
        public GameObject Root { get; }

        /// <summary>The buttons in top-to-bottom visual order.</summary>
        public IReadOnlyList<UnityEngine.UI.MenuButton> Buttons => _buttons;

        /// <summary>The items in top-to-bottom visual order.</summary>
        public IReadOnlyList<SidePanelItem> Items => _items;

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

            bool isBottomRight = options.Anchor == SidePanelAnchor.BottomRight;

            Root = new GameObject(options.Name + "_Panel", typeof(RectTransform));
            Root.transform.SetParent(screen.transform, false);

            _rect = (RectTransform)Root.transform;
            _rect.anchorMin = new Vector2(0.5f, 0.5f);
            _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = isBottomRight ? new Vector2(1f, 0f) : new Vector2(1f, 0.5f);
            _rect.sizeDelta = new Vector2(PanelWidth, 0f);

            VerticalLayoutGroup vlg = Root.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = isBottomRight ? TextAnchor.LowerRight : TextAnchor.MiddleRight;
            vlg.spacing = options.Spacing;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;

            Vector2 areaAnchor = isBottomRight ? new Vector2(1f, 0f) : new Vector2(1f, 0.5f);
            _area = new ContentArea(
                Root,
                new SingleContentLayout(new AnchoredPosition(areaAnchor, areaAnchor, Vector2.zero)));

            Root.AddComponent<PanelHook>().Owner = this;
            Reposition();
        }

        /// <summary>Adds a button whose caption is a fixed string.</summary>
        public SidePanel AddButton(
            string label,
            Action onSubmit,
            string id = null,
            Func<bool> isVisible = null)
        {
            return AddButton(() => label, onSubmit, id, isVisible);
        }

        /// <summary>
        /// Adds or updates a button whose caption is re-evaluated on every Refresh() and after every click.
        /// </summary>
        public SidePanel AddButton(
            Func<string> label,
            Action onSubmit,
            string id = null,
            Func<bool> isVisible = null)
        {
            if (label == null) throw new ArgumentNullException(nameof(label));

            if (!string.IsNullOrEmpty(id) && _itemsById.TryGetValue(id, out SidePanelItem existing))
            {
                existing.Label = label;
                existing.OnSubmit = onSubmit;
                existing.IsVisible = isVisible;
                Refresh();
                return this;
            }

            string itemId = string.IsNullOrEmpty(id) ? $"{_options.Name}_Button{_buttonSerial++}" : id;

            SidePanelItem item = null;
            UnityEngine.UI.MenuButton button;
            _area.AddMenuButton(
                itemId,
                new MenuButtonConfig
                {
                    Label = label(),
                    CancelAction = _cancelAction,
                    SubmitAction = _ =>
                    {
                        item?.OnSubmit?.Invoke();
                        Refresh();
                    },
                    Style = MenuButtonStyle.VanillaStyle,
                    Proceed = false
                },
                out button);

            item = new SidePanelItem(itemId, button, label, onSubmit, isVisible);
            _itemsById[itemId] = item;

            if (_options.StackUpwards)
            {
                button.transform.SetAsFirstSibling();
                _items.Insert(0, item);
                _buttons.Insert(0, button);
            }
            else
            {
                _items.Add(item);
                _buttons.Add(button);
            }

            StyleButton(button);
            Refresh();
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
            string offText = "Off",
            string id = null,
            Func<bool> isVisible = null)
        {
            if (getValue == null) throw new ArgumentNullException(nameof(getValue));
            if (setValue == null) throw new ArgumentNullException(nameof(setValue));

            return AddButton(
                () => label + ": " + (getValue() ? onText : offText),
                () => setValue(!getValue()),
                id,
                isVisible);
        }

        public SidePanelItem FindItem(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            _itemsById.TryGetValue(id, out SidePanelItem item);
            return item;
        }

        public bool RemoveButton(string id)
        {
            if (string.IsNullOrEmpty(id) || !_itemsById.TryGetValue(id, out SidePanelItem item))
            {
                return false;
            }

            _itemsById.Remove(id);
            _items.Remove(item);
            _buttons.Remove(item.Button);

            if (item.Button != null)
            {
                UnityEngine.Object.Destroy(item.Button.gameObject);
            }

            ResizePanel();
            Reposition();
            return true;
        }

        public void SetVisible(string id, Func<bool> isVisible)
        {
            SidePanelItem item = FindItem(id);
            if (item == null)
            {
                return;
            }

            item.IsVisible = isVisible;
            Refresh();
        }

        public void SetVisible(string id, bool visible)
        {
            SetVisible(id, () => visible);
        }

        /// <summary>
        /// Removes and destroys all buttons currently inside this panel.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i] != null)
                {
                    UnityEngine.Object.Destroy(_buttons[i].gameObject);
                }
            }

            _items.Clear();
            _itemsById.Clear();
            _buttons.Clear();
            ResizePanel();
            Reposition();
        }

        /// <summary>
        /// Re-evaluates visibility and captions, refits button widths and re-aligns the panel.
        /// Call it after changing state from outside the panel.
        /// </summary>
        public void Refresh()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                SidePanelItem item = _items[i];
                UnityEngine.UI.MenuButton button = item.Button;
                if (button == null)
                {
                    continue; // screen was destroyed
                }

                bool visible = item.IsVisible == null || item.IsVisible();
                if (button.gameObject.activeSelf != visible)
                {
                    button.gameObject.SetActive(visible);
                }

                if (!visible)
                {
                    continue;
                }

                Text text = button.GetComponentInChildren<Text>(true);
                if (text != null)
                {
                    text.text = item.Label();
                    FixVerticalAlign align = text.GetComponent<FixVerticalAlign>();
                    if (align != null)
                    {
                        align.AlignText();
                    }
                }

                FitWidth(button);
            }

            ResizePanel();
            Reposition();
        }

        /// <summary>
        /// Aligns the panel to the configured edge of the root canvas.
        /// Called automatically; public in case you change resolution at runtime.
        /// </summary>
        public void Reposition()
        {
            bool isBottomRight = _options.Anchor == SidePanelAnchor.BottomRight;
            float rightEdge = FallbackRightEdge;
            float verticalBaseline = isBottomRight ? FallbackBottomEdge : 0f;

            RectTransform screenRt = _screen.GetComponent<RectTransform>();
            // includeInactive: menu screens are usually inactive while being built.
            Canvas[] canvases = _screen.GetComponentsInParent<Canvas>(true);

            if (screenRt != null && canvases.Length > 0)
            {
                RectTransform canvasRt = canvases[0].rootCanvas.GetComponent<RectTransform>();
                float refY = isBottomRight ? canvasRt.rect.yMin : canvasRt.rect.center.y;
                Vector3 world = canvasRt.TransformPoint(new Vector3(canvasRt.rect.xMax, refY, 0f));
                Vector3 local = screenRt.InverseTransformPoint(world);

                // anchoredPosition is relative to the parent's rect center, not its pivot.
                Vector2 parentCenter = screenRt.rect.center;
                rightEdge = local.x - parentCenter.x;
                verticalBaseline = local.y - parentCenter.y;
            }

            float posY = isBottomRight
                ? (verticalBaseline + _options.BottomInset + _options.VerticalOffset)
                : (verticalBaseline + _options.VerticalOffset);

            _rect.anchoredPosition = new Vector2(rightEdge - _options.RightInset, posY);
        }

        private void ResizePanel()
        {
            int visibleCount = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                UnityEngine.UI.MenuButton btn = _items[i].Button;
                if (btn != null && btn.gameObject.activeSelf)
                {
                    visibleCount++;
                }
            }

            float height = visibleCount == 0
                ? 0f
                : visibleCount * _options.ButtonHeight + (visibleCount - 1) * _options.Spacing;
            _rect.sizeDelta = new Vector2(PanelWidth, height);
            LayoutRebuilder.MarkLayoutForRebuild(_rect);
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
            ConfigureCursors(button);
        }

        private void ConfigureCursors(UnityEngine.UI.MenuButton button)
        {
            if (button == null)
            {
                return;
            }

            Transform cursorL = button.transform.Find("Label/CursorLeft");
            Transform cursorR = button.transform.Find("Label/CursorRight");

            if (_options.HideCursors)
            {
                if (cursorL != null)
                {
                    UnityEngine.Object.Destroy(cursorL.gameObject);
                }

                if (cursorR != null)
                {
                    UnityEngine.Object.Destroy(cursorR.gameObject);
                }

                return;
            }

            if (button.gameObject.activeInHierarchy)
            {
                // Modding API adds Animator before Image on CursorLeft/CursorRight.
                // Toggling active state forces Animator to rebind to the Image component.
                if (cursorL != null)
                {
                    cursorL.gameObject.SetActive(false);
                    cursorL.gameObject.SetActive(true);
                }

                if (cursorR != null)
                {
                    cursorR.gameObject.SetActive(false);
                    cursorR.gameObject.SetActive(true);
                }
            }
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

        private sealed class PanelHook : MonoBehaviour
        {
            public SidePanel Owner;

            private void OnEnable()
            {
                Owner?.Reposition();
            }

            private void OnRectTransformDimensionsChange()
            {
                Owner?.Reposition();
            }
        }
    }
}