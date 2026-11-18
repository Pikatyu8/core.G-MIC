using System;
using System.Collections.Generic;
using CoreGMIC.Core;
using Modding.Menu;
using Modding.Menu.Config;
using Satchel.BetterMenus;
using UnityEngine;
using UnityEngine.UI;
using MenuButton = Satchel.BetterMenus.MenuButton;

namespace CoreGMIC.UI
{
    public static class ModMenu
    {
        private const string ResetButtonId = "reset_active_filter";
        private const string HeavyWarningPanelId = "heavy_warning_panel";
        private const string ExperimentalWarningPanelId = "experimental_warning_panel";
        private const string HeavyWarningOptionLabel = "<color=#FF3B30>[!] GPU HEAVY -></color>";
        private const string HeavyWarningBannerText = "<color=#FF3B30>GPU HEAVY SHADERS PAST THIS POINT</color>";
        private const string ExperimentalWarningOptionLabel = "<color=#FF9500>[!] EXPERIMENTAL -></color>";
        private const string ExperimentalWarningBannerText = "<color=#FF9500>EXPERIMENTAL SHADERS PAST THIS POINT</color>";

        private static Menu _menuRef;
        private static UnityEngine.UI.MenuButton _disablePlayerButton;
        private static UnityEngine.UI.MenuButton _disableTiktikButton;
        private static UnityEngine.UI.MenuButton _noFastNoiseButton;
        private static UnityEngine.UI.MenuButton _enableExperimentalButton;
        private static bool _isUpdatingUi;
        private static readonly List<IFilter> _filterSlots = new List<IFilter>();
        private static int _selectedOptionIndex;
        private static int _dummyWarningIndex = -1;
        private static int _dummyExperimentalIndex = -1;

        public static MenuScreen GetMenuScreen(MenuScreen modListMenu)
        {
            _isUpdatingUi = true;
            try
            {
                _filterSlots.Clear();
                List<string> filterNames = new List<string>();

                // Slot 0: No active filter.
                _filterSlots.Add(null);
                filterNames.Add("None");

                // Light / LUT / Standard filters first.
                foreach (IFilter filter in FilterManager.Filters)
                {
                    if (filter.Category == FilterCategory.Heavy || filter.Category == FilterCategory.Experimental)
                    {
                        continue;
                    }

                    _filterSlots.Add(filter);
                    string availability = filter.IsAvailable ? "" : " [N/A]";
                    filterNames.Add($"{filter.DisplayName}{availability}");
                }

                // Dummy warning entry before GPU-heavy filters.
                _dummyWarningIndex = -1;
                bool hasHeavyFilters = false;
                foreach (IFilter filter in FilterManager.Filters)
                {
                    if (filter.Category == FilterCategory.Heavy)
                    {
                        hasHeavyFilters = true;
                        break;
                    }
                }

                if (hasHeavyFilters)
                {
                    _dummyWarningIndex = _filterSlots.Count;
                    _filterSlots.Add(null);
                    filterNames.Add(HeavyWarningOptionLabel);

                    foreach (IFilter filter in FilterManager.Filters)
                    {
                        if (filter.Category != FilterCategory.Heavy)
                        {
                            continue;
                        }

                        _filterSlots.Add(filter);
                        string availability = filter.IsAvailable ? "" : " [N/A]";
                        filterNames.Add($"{filter.DisplayName}{availability}");
                    }
                }

                // Experimental filters (only shown when enabled via corner toggle).
                _dummyExperimentalIndex = -1;
                if (FilterManager.EnableExperimentalShaders)
                {
                    bool hasExperimentalFilters = false;
                    foreach (IFilter filter in FilterManager.Filters)
                    {
                        if (filter.Category == FilterCategory.Experimental)
                        {
                            hasExperimentalFilters = true;
                            break;
                        }
                    }

                    if (hasExperimentalFilters)
                    {
                        _dummyExperimentalIndex = _filterSlots.Count;
                        _filterSlots.Add(null);
                        filterNames.Add(ExperimentalWarningOptionLabel);

                        foreach (IFilter filter in FilterManager.Filters)
                        {
                            if (filter.Category != FilterCategory.Experimental)
                            {
                                continue;
                            }

                            _filterSlots.Add(filter);
                            string availability = filter.IsAvailable ? "" : " [N/A]";
                            filterNames.Add($"{filter.DisplayName}{availability}");
                        }
                    }
                }

                SyncSelectedOptionIndex();

                List<Element> elements = new List<Element>
                {
                    new HorizontalOption(
                        "Enable Post-Processing",
                        "Master toggle for all G'MIC visual filters.",
                        new[] { "Off", "On" },
                        index =>
                        {
                            bool enabled = index == 1;
                            FilterManager.IsEnabled = enabled;
                            CoreGMICMod.Settings.Enabled = enabled;
                        },
                        () => FilterManager.IsEnabled ? 1 : 0,
                        Id: "master_enable"
                    ),
                    new HorizontalOption(
                        "Active Filter",
                        "Select which G'MIC filter to apply to the game world.",
                        filterNames.ToArray(),
                        index =>
                        {
                            _selectedOptionIndex = index;
                            IFilter chosen = (index >= 0 && index < _filterSlots.Count) ? _filterSlots[index] : null;
                            FilterManager.SetActiveById(chosen?.Id ?? "none");
                            if (!_isUpdatingUi)
                            {
                                RefreshParameterVisibility();
                            }
                        },
                        () =>
                        {
                            SyncSelectedOptionIndexIfExternal();
                            return _selectedOptionIndex;
                        },
                        Id: "active_filter"
                    ),
                    new TextPanel(
                        HeavyWarningBannerText,
                        width: 1000f,
                        fontSize: 30,
                        Id: HeavyWarningPanelId
                    )
                    {
                        isVisible = IsDummyWarningSelected()
                    },
                    new TextPanel(
                        ExperimentalWarningBannerText,
                        width: 1000f,
                        fontSize: 30,
                        Id: ExperimentalWarningPanelId
                    )
                    {
                        isVisible = IsDummyExperimentalSelected()
                    },
                    new MenuButton(
                        "Reset Filter Defaults",
                        "Restore default parameters for the currently active filter.",
                        _ =>
                        {
                            IFilter active = FilterManager.ActiveFilter;
                            if (active == null)
                            {
                                return;
                            }

                            FilterManager.ResetFilterParameters(active);
                            RefreshFromManager();
                        },
                        proceed: false,
                        Id: ResetButtonId
                    )
                    {
                        isVisible = HasVisibleParameters(FilterManager.ActiveFilter)
                    }
                };

                foreach (IFilter filter in FilterManager.Filters)
                {
                    if (filter.Parameters == null)
                    {
                        continue;
                    }

                    foreach (FilterParameter param in filter.Parameters)
                    {
                        Element elem = CreateParameterElement(filter.Id, param);
                        if (elem != null)
                        {
                            elem.isVisible = ShouldShowParam(filter, param);
                            elements.Add(elem);
                        }
                    }
                }

                _menuRef = new Menu("core.G'MIC", elements.ToArray());
                MenuScreen screen = _menuRef.GetMenuScreen(modListMenu);
                AddCornerExclusionButtons(screen);
                return screen;
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        public static void RefreshFromManager()
        {
            if (_menuRef == null)
            {
                return;
            }

            _isUpdatingUi = true;
            try
            {
                SyncSelectedOptionIndex();
                ApplyParameterVisibilityFlags();
                UpdateCornerExclusionLabels();
                _menuRef.Update();
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        private static void AddCornerExclusionButtons(MenuScreen screen)
        {
            if (screen == null || screen.title == null)
            {
                return;
            }

            GameObject screenGo = screen.gameObject;
            RectTransform screenRt = screenGo.GetComponent<RectTransform>();
            Canvas canvas = screen.GetComponentInParent<Canvas>();

            float rightEdgeX = 920f;
            float centerY = 0f;
            if (canvas != null && screenRt != null)
            {
                RectTransform canvasRt = canvas.GetComponent<RectTransform>();
                Vector3 worldRightCenter = canvasRt.TransformPoint(new Vector3(canvasRt.rect.xMax, canvasRt.rect.center.y, 0f));
                Vector3 localRightCenter = screenRt.InverseTransformPoint(worldRightCenter);
                rightEdgeX = localRightCenter.x;
                centerY = localRightCenter.y;
            }

            GameObject panelGo = new GameObject("CoreGMIC_CornerPanel");
            panelGo.transform.SetParent(screenGo.transform, false);

            RectTransform panelRt = panelGo.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(1f, 0.5f);
            panelRt.sizeDelta = new Vector2(560f, 240f);
            panelRt.anchoredPosition = new Vector2(rightEdgeX - 120f, centerY);

            VerticalLayoutGroup vlg = panelGo.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleRight;
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;

            ContentArea cornerArea = new ContentArea(
                panelGo,
                new SingleContentLayout(new AnchoredPosition(
                    new Vector2(1f, 0.5f),
                    new Vector2(1f, 0.5f),
                    Vector2.zero)));

            cornerArea.AddMenuButton(
                "DisablePlayerButton",
                new MenuButtonConfig
                {
                    Label = GetDisablePlayerLabel(),
                    CancelAction = _ => _menuRef?.CancelAction?.Invoke(),
                    SubmitAction = _ =>
                    {
                        EntityExclusionManager.DisablePlayer = !EntityExclusionManager.DisablePlayer;
                        UpdateCornerExclusionLabels();
                    },
                    Style = MenuButtonStyle.VanillaStyle,
                    Proceed = false
                },
                out _disablePlayerButton);

            ConfigureCornerButton(_disablePlayerButton);

            cornerArea.AddMenuButton(
                "DisableTiktikButton",
                new MenuButtonConfig
                {
                    Label = GetDisableTiktikLabel(),
                    CancelAction = _ => _menuRef?.CancelAction?.Invoke(),
                    SubmitAction = _ =>
                    {
                        EntityExclusionManager.DisableTiktik = !EntityExclusionManager.DisableTiktik;
                        UpdateCornerExclusionLabels();
                    },
                    Style = MenuButtonStyle.VanillaStyle,
                    Proceed = false
                },
                out _disableTiktikButton);

            ConfigureCornerButton(_disableTiktikButton);

            cornerArea.AddMenuButton(
                "NoFastNoiseButton",
                new MenuButtonConfig
                {
                    Label = GetNoFastNoiseLabel(),
                    CancelAction = _ => _menuRef?.CancelAction?.Invoke(),
                    SubmitAction = _ =>
                    {
                        if (CoreGMICMod.Settings != null)
                        {
                            CoreGMICMod.Settings.NoFastNoise = !CoreGMICMod.Settings.NoFastNoise;
                        }
                        UpdateCornerExclusionLabels();
                    },
                    Style = MenuButtonStyle.VanillaStyle,
                    Proceed = false
                },
                out _noFastNoiseButton);

            ConfigureCornerButton(_noFastNoiseButton);

            cornerArea.AddMenuButton(
                "EnableExperimentalButton",
                new MenuButtonConfig
                {
                    Label = GetEnableExperimentalLabel(),
                    CancelAction = _ => _menuRef?.CancelAction?.Invoke(),
                    SubmitAction = _ =>
                    {
                        FilterManager.EnableExperimentalShaders = !FilterManager.EnableExperimentalShaders;
                        RefreshFromManager();
                    },
                    Style = MenuButtonStyle.VanillaStyle,
                    Proceed = false
                },
                out _enableExperimentalButton);

            ConfigureCornerButton(_enableExperimentalButton);
        }

        private static void ConfigureCornerButton(UnityEngine.UI.MenuButton button)
        {
            if (button == null)
            {
                return;
            }

            ContentSizeFitter btnFitter = button.GetComponent<ContentSizeFitter>();
            if (btnFitter != null)
            {
                btnFitter.enabled = false;
            }

            HorizontalLayoutGroup hlg = button.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.childAlignment = TextAnchor.MiddleRight;
            }

            RectTransform btnRt = button.GetComponent<RectTransform>();
            if (btnRt != null)
            {
                btnRt.sizeDelta = new Vector2(440f, 44f);
            }

            LayoutElement layoutElem = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            layoutElem.minHeight = 44f;
            layoutElem.preferredHeight = 44f;

            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                ContentSizeFitter lblFitter = label.GetComponent<ContentSizeFitter>();
                if (lblFitter != null)
                {
                    lblFitter.enabled = false;
                }

                RectTransform lblRt = label.GetComponent<RectTransform>();
                if (lblRt != null)
                {
                    lblRt.anchorMin = Vector2.zero;
                    lblRt.anchorMax = Vector2.one;
                    lblRt.offsetMin = Vector2.zero;
                    lblRt.offsetMax = Vector2.zero;
                }

                label.fontSize = 26;
                label.alignment = TextAnchor.MiddleCenter;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
            }

            UpdateButtonPreferredWidth(button);
        }

        private static void UpdateButtonPreferredWidth(UnityEngine.UI.MenuButton button)
        {
            if (button == null)
            {
                return;
            }

            Text label = button.GetComponentInChildren<Text>(true);
            LayoutElement layoutElem = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            if (label != null)
            {
                float targetWidth = label.preferredWidth + 24f;
                layoutElem.minWidth = targetWidth;
                layoutElem.preferredWidth = targetWidth;
            }

            RectTransform rt = button.GetComponent<RectTransform>();
            if (rt != null)
            {
                LayoutRebuilder.MarkLayoutForRebuild(rt);
            }
        }

        private static void UpdateCornerExclusionLabels()
        {
            if (_disablePlayerButton != null)
            {
                Text label = _disablePlayerButton.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    label.text = GetDisablePlayerLabel();
                }
                UpdateButtonPreferredWidth(_disablePlayerButton);
            }

            if (_disableTiktikButton != null)
            {
                Text label = _disableTiktikButton.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    label.text = GetDisableTiktikLabel();
                }
                UpdateButtonPreferredWidth(_disableTiktikButton);
            }

            if (_noFastNoiseButton != null)
            {
                Text label = _noFastNoiseButton.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    label.text = GetNoFastNoiseLabel();
                }
                UpdateButtonPreferredWidth(_noFastNoiseButton);
            }

            if (_enableExperimentalButton != null)
            {
                Text label = _enableExperimentalButton.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    label.text = GetEnableExperimentalLabel();
                }
                UpdateButtonPreferredWidth(_enableExperimentalButton);
            }
        }

        private static string GetDisablePlayerLabel()
        {
            return EntityExclusionManager.DisablePlayer ? "Disable Player: On" : "Disable Player: Off";
        }

        private static string GetDisableTiktikLabel()
        {
            return EntityExclusionManager.DisableTiktik ? "Disable Tiktik: On" : "Disable Tiktik: Off";
        }

        private static string GetNoFastNoiseLabel()
        {
            return (CoreGMICMod.Settings != null && CoreGMICMod.Settings.NoFastNoise)
                ? "No FastNoise: On"
                : "No FastNoise: Off";
        }

        private static string GetEnableExperimentalLabel()
        {
            return FilterManager.EnableExperimentalShaders ? "Experimental Shaders: On" : "Experimental Shaders: Off";
        }

        private static bool IsDummyWarningSelected()
        {
            return _dummyWarningIndex >= 0 && _selectedOptionIndex == _dummyWarningIndex;
        }

        private static bool IsDummyExperimentalSelected()
        {
            return _dummyExperimentalIndex >= 0 && _selectedOptionIndex == _dummyExperimentalIndex;
        }

        private static void SyncSelectedOptionIndex()
        {
            IFilter active = FilterManager.ActiveFilter;
            if (active == null)
            {
                _selectedOptionIndex = 0;
                return;
            }

            int idx = _filterSlots.FindIndex(f => f == active);
            _selectedOptionIndex = idx >= 0 ? idx : 0;
        }

        private static void SyncSelectedOptionIndexIfExternal()
        {
            if (_selectedOptionIndex < 0 || _selectedOptionIndex >= _filterSlots.Count ||
                _filterSlots[_selectedOptionIndex] != FilterManager.ActiveFilter)
            {
                SyncSelectedOptionIndex();
            }
        }

        private static Element CreateParameterElement(string filterId, FilterParameter templateParam)
        {
            string fId = filterId;
            string pKey = templateParam.Key;
            string elemId = GetParamElementId(fId, pKey);

            switch (templateParam.Kind)
            {
                case FilterParamKind.Choice:
                    return new HorizontalOption(
                        templateParam.DisplayName,
                        templateParam.Description,
                        templateParam.Choices ?? new[] { "Default" },
                        index =>
                        {
                            FilterParameter param = FilterManager.GetFilterParameter(fId, pKey);
                            if (param == null)
                            {
                                return;
                            }

                            param.Value = index;
                            FilterManager.SaveFilterParameters(FilterManager.GetFilterById(fId));
                            if (!_isUpdatingUi)
                            {
                                RefreshParameterVisibility();
                            }
                        },
                        () => FilterManager.GetFilterParameter(fId, pKey)?.IntValue ?? templateParam.IntValue,
                        Id: elemId
                    );

                case FilterParamKind.Bool:
                    return Blueprints.HorizontalBoolOption(
                        templateParam.DisplayName,
                        templateParam.Description,
                        val =>
                        {
                            FilterParameter param = FilterManager.GetFilterParameter(fId, pKey);
                            if (param == null)
                            {
                                return;
                            }

                            param.Value = val ? 1f : 0f;
                            FilterManager.SaveFilterParameters(FilterManager.GetFilterById(fId));
                            if (!_isUpdatingUi)
                            {
                                RefreshParameterVisibility();
                            }
                        },
                        () => FilterManager.GetFilterParameter(fId, pKey)?.BoolValue ?? templateParam.BoolValue,
                        "On",
                        "Off",
                        Id: elemId
                    );

                case FilterParamKind.Int:
                    return new FilterSlider(
                        templateParam.DisplayName,
                        val =>
                        {
                            FilterParameter param = FilterManager.GetFilterParameter(fId, pKey);
                            if (param == null)
                            {
                                return;
                            }

                            param.Value = val;
                            FilterManager.SaveFilterParameters(FilterManager.GetFilterById(fId));
                        },
                        () => FilterManager.GetFilterParameter(fId, pKey)?.Value ?? templateParam.Value,
                        templateParam.MinValue,
                        templateParam.MaxValue,
                        wholeNumbers: true,
                        id: elemId
                    );

                case FilterParamKind.Float:
                default:
                    return new FilterSlider(
                        templateParam.DisplayName,
                        val =>
                        {
                            FilterParameter param = FilterManager.GetFilterParameter(fId, pKey);
                            if (param == null)
                            {
                                return;
                            }

                            param.Value = val;
                            FilterManager.SaveFilterParameters(FilterManager.GetFilterById(fId));
                        },
                        () => FilterManager.GetFilterParameter(fId, pKey)?.Value ?? templateParam.Value,
                        templateParam.MinValue,
                        templateParam.MaxValue,
                        wholeNumbers: false,
                        id: elemId
                    );
            }
        }

        private static void RefreshParameterVisibility()
        {
            if (_menuRef == null)
            {
                return;
            }

            ApplyParameterVisibilityFlags();
            _menuRef.Reflow();
        }

        private static void ApplyParameterVisibilityFlags()
        {
            if (_menuRef == null)
            {
                return;
            }

            Element warningPanel = _menuRef.Find(HeavyWarningPanelId);
            if (warningPanel != null)
            {
                warningPanel.isVisible = IsDummyWarningSelected();
            }

            Element experimentalPanel = _menuRef.Find(ExperimentalWarningPanelId);
            if (experimentalPanel != null)
            {
                experimentalPanel.isVisible = IsDummyExperimentalSelected();
            }

            Element resetBtn = _menuRef.Find(ResetButtonId);
            if (resetBtn != null)
            {
                resetBtn.isVisible = HasVisibleParameters(FilterManager.ActiveFilter);
            }

            foreach (IFilter filter in FilterManager.Filters)
            {
                if (filter.Parameters == null)
                {
                    continue;
                }

                foreach (FilterParameter param in filter.Parameters)
                {
                    Element elem = _menuRef.Find(GetParamElementId(filter.Id, param.Key));
                    if (elem != null)
                    {
                        elem.isVisible = ShouldShowParam(filter, param);
                    }
                }
            }
        }

        private static bool HasVisibleParameters(IFilter filter)
        {
            return filter != null && filter.Parameters != null && filter.Parameters.Count > 0;
        }

        private static bool ShouldShowParam(IFilter filter, FilterParameter param)
        {
            if (FilterManager.ActiveFilter != filter)
            {
                return false;
            }

            return param.IsVisible == null || param.IsVisible();
        }

        private static string GetParamElementId(string filterId, string paramKey)
        {
            return $"param:{filterId}:{paramKey}";
        }

        private sealed class FilterSlider : CustomSlider
        {
            public FilterSlider(
                string name,
                Action<float> storeValue,
                Func<float> loadValue,
                float minValue,
                float maxValue,
                bool wholeNumbers = false,
                string id = "__UseName")
                : base(name, storeValue, loadValue, minValue, maxValue, wholeNumbers, id)
            {
            }

            public override void Update()
            {
                if (valueLabel != null)
                {
                    valueLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
                    valueLabel.verticalOverflow = VerticalWrapMode.Overflow;
                }

                base.Update();
            }
        }
    }
}