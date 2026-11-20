using System;
using System.Collections.Generic;
using System.Globalization;
using CoreGMIC.Core;
using Modding.Menu;
using Satchel.BetterMenus;
using SidePanelLib;
using UnityEngine;
using UnityEngine.UI;
using MenuButton = Satchel.BetterMenus.MenuButton;

namespace CoreGMIC.UI
{
    public static class ModMenu
    {
        private const string ResetButtonId = "reset_active_filter";

        private static readonly FilterCategoryMeta[] CategoryMetas =
        {
            new FilterCategoryMeta(FilterCategory.Lut),
            new FilterCategoryMeta(FilterCategory.Standard),
            new FilterCategoryMeta(
                FilterCategory.Heavy,
                bannerPanelId: "heavy_warning_panel",
                optionLabel: "<color=#FF3B30>[!] GPU HEAVY -></color>",
                bannerText: "<color=#FF3B30>GPU HEAVY SHADERS PAST THIS POINT</color>"),
            new FilterCategoryMeta(
                FilterCategory.Experimental,
                bannerPanelId: "experimental_warning_panel",
                optionLabel: "<color=#FF9500>[!] EXPERIMENTAL -></color>",
                bannerText: "<color=#FF9500>EXPERIMENTAL SHADERS PAST THIS POINT</color>",
                isCategoryVisible: () => FilterManager.EnableExperimentalShaders)
        };

        private static Menu _menuRef;
        private static SidePanel _sidePanel;
        private static SidePanel _bottomRightPanel;
        private static bool _isUpdatingUi;
        private static readonly List<IFilter> _filterSlots = new List<IFilter>();
        private static readonly List<CornerButtonSpec> _cornerButtonSpecs = new List<CornerButtonSpec>();
        private static int _selectedOptionIndex;

        public static MenuScreen GetMenuScreen(MenuScreen modListMenu)
        {
            _isUpdatingUi = true;
            try
            {
                List<string> filterNames = RebuildFilterSlots();
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
                    )
                };

                foreach (FilterCategoryMeta meta in CategoryMetas)
                {
                    if (!meta.HasBanner)
                    {
                        continue;
                    }

                    elements.Add(new TextPanel(
                        meta.BannerText,
                        width: 1000f,
                        fontSize: 30,
                        Id: meta.BannerPanelId)
                    {
                        isVisible = IsBannerSelected(meta)
                    });
                }

                elements.Add(new MenuButton(
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
                    Id: ResetButtonId)
                {
                    isVisible = HasVisibleParameters(FilterManager.ActiveFilter)
                });

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
                _menuRef.OnBuilt += (_, _) =>
                {
                    RealignMenuTexts();
                    AdjustRowSpacingByDescription();
                };
                _menuRef.OnUpdate += (_, _) => RealignMenuTexts();
                _menuRef.OnReflow += (_, _) => AdjustRowSpacingByDescription();
                MenuScreen screen = _menuRef.GetMenuScreen(modListMenu);

                _sidePanel = SidePanel.Attach(
                    screen,
                    _ => _menuRef?.CancelAction?.Invoke(),
                    new SidePanelOptions
                    {
                        Name = "CoreGMIC",
                        Anchor = SidePanelAnchor.MiddleRight,
                        RightInset = 24f,
                        HideCursors = true
                    });
                PopulateSidePanel();

                _bottomRightPanel = SidePanel.Attach(
                        screen,
                        _ => _menuRef?.CancelAction?.Invoke(),
                        new SidePanelOptions
                        {
                            Name = "CoreGMIC_Corner",
                            Anchor = SidePanelAnchor.BottomRight,
                            RightInset = 24f,
                            BottomInset = 20f,
                            Spacing = 8f,
                            StackUpwards = true,
                            HideCursors = true
                        })
                    .AddButton(
                        "Reload Exclusions",
                        () =>
                        {
                            EntityExclusionManager.ReloadJsonRules(applyJsonEnabledState: true);
                            PopulateSidePanel();
                            RefreshFromManager();
                        },
                        id: "reload_exclusions")
                    .AddButton(
                        "Open Exclusion Settings",
                        () => JsonExclusionConfig.OpenInDefaultEditor(),
                        id: "open_exclusions");

                foreach (CornerButtonSpec spec in _cornerButtonSpecs)
                {
                    _bottomRightPanel.AddButton(spec.Label, spec.OnSubmit, spec.Id, spec.IsVisible);
                }

                RealignMenuTexts();
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
                FilterCategoryMeta preservedBanner = GetSelectedBannerMeta();

                List<string> filterNames = RebuildFilterSlots();
                if (_menuRef.Find("active_filter") is HorizontalOption activeFilterOption)
                {
                    activeFilterOption.Values = filterNames.ToArray();
                }

                SyncSelectedOptionIndex(preservedBanner);
                ApplyParameterVisibilityFlags();
                _sidePanel?.Refresh();
                _bottomRightPanel?.Refresh();
                _menuRef.Update();
                RealignMenuTexts();
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        public static void RegisterCornerButton(
            string id,
            Func<string> label,
            Action onSubmit,
            Func<bool> isVisible = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            if (label == null) throw new ArgumentNullException(nameof(label));

            int existingIdx = _cornerButtonSpecs.FindIndex(s => s.Id == id);
            CornerButtonSpec spec = new CornerButtonSpec(id, label, onSubmit, isVisible);
            if (existingIdx >= 0)
            {
                _cornerButtonSpecs[existingIdx] = spec;
            }
            else
            {
                _cornerButtonSpecs.Add(spec);
            }

            _bottomRightPanel?.AddButton(label, onSubmit, id, isVisible);
        }

        public static bool UnregisterCornerButton(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            int removed = _cornerButtonSpecs.RemoveAll(s => s.Id == id);
            bool removedFromPanel = _bottomRightPanel != null && _bottomRightPanel.RemoveButton(id);
            return removed > 0 || removedFromPanel;
        }

        private static void RealignMenuTexts()
        {
            if (_menuRef?.menuScreen == null)
            {
                return;
            }

            foreach (FixVerticalAlign align in _menuRef.menuScreen.GetComponentsInChildren<FixVerticalAlign>(true))
            {
                if (align == null)
                {
                    continue;
                }

                if (align.labelFixType == FixVerticalAlign.LabelFixType.KeyMap)
                {
                    align.AlignTextKeymap();
                }
                else
                {
                    align.AlignText();
                }
            }
        }

        private static void AdjustRowSpacingByDescription()
        {
            if (_menuRef?.MenuOrder == null || _menuRef.MenuOrder.Count == 0)
            {
                return;
            }

            float cumulativeOffsetY = 0f;

            foreach (GameObjectRow row in _menuRef.MenuOrder)
            {
                if (row?.Row == null)
                {
                    continue;
                }

                bool hasActive = false;
                float rowExtraHeight = 0f;

                foreach (GameObject go in row.Row)
                {
                    if (go == null || !go.activeSelf)
                    {
                        continue;
                    }

                    hasActive = true;

                    RectTransform rt = go.GetComponent<RectTransform>();
                    if (rt != null && cumulativeOffsetY > 0.01f)
                    {
                        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y - cumulativeOffsetY);
                    }

                    float extra = GetDescriptionExtraHeight(go);
                    if (extra > rowExtraHeight)
                    {
                        rowExtraHeight = extra;
                    }
                }

                if (hasActive)
                {
                    cumulativeOffsetY += rowExtraHeight;
                }
            }
        }

        private static float GetDescriptionExtraHeight(GameObject go)
        {
            Transform descTf = go.transform.Find("Description");
            if (descTf == null || !descTf.gameObject.activeSelf)
            {
                return 0f;
            }

            Text descText = descTf.GetComponent<Text>();
            if (descText == null || string.IsNullOrEmpty(descText.text))
            {
                return 0f;
            }

            RectTransform descRt = descText.rectTransform;
            if (descRt.rect.width <= 1f)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(descRt);
            }

            float width = descRt.rect.width > 1f ? descRt.rect.width : descRt.sizeDelta.x;
            if (width <= 1f)
            {
                return 0f;
            }

            float ppu = descText.pixelsPerUnit > 0f ? descText.pixelsPerUnit : 1f;
            TextGenerationSettings settings = descText.GetGenerationSettings(new Vector2(width, 0f));

            float actualHeight = descText.cachedTextGeneratorForLayout.GetPreferredHeight(descText.text, settings) / ppu;
            float singleLineHeight = descText.cachedTextGeneratorForLayout.GetPreferredHeight("A", settings) / ppu;

            float extra = actualHeight - singleLineHeight;
            return extra > 2f ? (extra + 8f) : 0f;
        }

        private static void PopulateSidePanel()
        {
            if (_sidePanel == null)
            {
                return;
            }

            _sidePanel.Clear();

            foreach (ExclusionRule rule in EntityExclusionManager.Rules)
            {
                ExclusionRule capturedRule = rule;
                _sidePanel.AddToggle(
                    capturedRule.DisplayName,
                    () => capturedRule.Enabled,
                    v => EntityExclusionManager.SetEnabled(capturedRule.Id, v));
            }

            _sidePanel
                .AddToggle("No FastNoise", () => CoreGMICMod.Settings?.NoFastNoise ?? false, v =>
                {
                    if (CoreGMICMod.Settings != null)
                    {
                        CoreGMICMod.Settings.NoFastNoise = v;
                    }
                })
                .AddToggle("Experimental Shaders", () => FilterManager.EnableExperimentalShaders, v =>
                {
                    FilterManager.EnableExperimentalShaders = v;
                    RefreshFromManager();
                });
        }

        private static List<string> RebuildFilterSlots()
        {
            _filterSlots.Clear();
            List<string> filterNames = new List<string>();

            // Slot 0: No active filter.
            _filterSlots.Add(null);
            filterNames.Add("None");

            foreach (FilterCategoryMeta meta in CategoryMetas)
            {
                meta.DummySlotIndex = -1;
                if (meta.IsCategoryVisible != null && !meta.IsCategoryVisible())
                {
                    continue;
                }

                List<IFilter> categoryFilters = new List<IFilter>();
                foreach (IFilter filter in FilterManager.Filters)
                {
                    if (filter.Category == meta.Category)
                    {
                        categoryFilters.Add(filter);
                    }
                }

                if (categoryFilters.Count == 0)
                {
                    continue;
                }

                if (meta.HasBanner)
                {
                    meta.DummySlotIndex = _filterSlots.Count;
                    _filterSlots.Add(null);
                    filterNames.Add(meta.OptionLabel);
                }

                foreach (IFilter filter in categoryFilters)
                {
                    _filterSlots.Add(filter);
                    string availability = filter.IsAvailable ? "" : " [N/A]";
                    filterNames.Add($"{filter.DisplayName}{availability}");
                }
            }

            return filterNames;
        }

        private static FilterCategoryMeta GetSelectedBannerMeta()
        {
            foreach (FilterCategoryMeta meta in CategoryMetas)
            {
                if (IsBannerSelected(meta))
                {
                    return meta;
                }
            }

            return null;
        }

        private static bool IsBannerSelected(FilterCategoryMeta meta)
        {
            return meta != null && meta.HasBanner && meta.DummySlotIndex >= 0 && _selectedOptionIndex == meta.DummySlotIndex;
        }

        private static void SyncSelectedOptionIndex(FilterCategoryMeta preservedBanner = null)
        {
            IFilter active = FilterManager.ActiveFilter;
            if (active == null)
            {
                if (preservedBanner != null && preservedBanner.DummySlotIndex >= 0)
                {
                    _selectedOptionIndex = preservedBanner.DummySlotIndex;
                }
                else
                {
                    _selectedOptionIndex = 0;
                }
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
                case FilterParamKind.Button:
                    return new MenuButton(
                        templateParam.DisplayName,
                        templateParam.Description,
                        _ =>
                        {
                            FilterParameter param = FilterManager.GetFilterParameter(fId, pKey);
                            param?.InvokeButton();
                        },
                        proceed: false,
                        Id: elemId
                    );

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
                        step: 0f,
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
                        step: templateParam.Step,
                        id: elemId
                    );
            }
        }

        private static void RefreshParameterVisibility()
        {
            RefreshFromManager();
        }

        private static void ApplyParameterVisibilityFlags()
        {
            if (_menuRef == null)
            {
                return;
            }

            foreach (FilterCategoryMeta meta in CategoryMetas)
            {
                if (!meta.HasBanner)
                {
                    continue;
                }

                Element bannerPanel = _menuRef.Find(meta.BannerPanelId);
                if (bannerPanel != null)
                {
                    bannerPanel.isVisible = IsBannerSelected(meta);
                }
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

        private sealed class CornerButtonSpec
        {
            public string Id { get; }
            public Func<string> Label { get; }
            public Action OnSubmit { get; }
            public Func<bool> IsVisible { get; }

            public CornerButtonSpec(string id, Func<string> label, Action onSubmit, Func<bool> isVisible)
            {
                Id = id;
                Label = label;
                OnSubmit = onSubmit;
                IsVisible = isVisible;
            }
        }

        private sealed class FilterCategoryMeta
        {
            public FilterCategory Category { get; }
            public string BannerPanelId { get; }
            public string OptionLabel { get; }
            public string BannerText { get; }
            public Func<bool> IsCategoryVisible { get; }
            public int DummySlotIndex { get; set; } = -1;

            public FilterCategoryMeta(
                FilterCategory category,
                string bannerPanelId = null,
                string optionLabel = null,
                string bannerText = null,
                Func<bool> isCategoryVisible = null)
            {
                Category = category;
                BannerPanelId = bannerPanelId;
                OptionLabel = optionLabel;
                BannerText = bannerText;
                IsCategoryVisible = isCategoryVisible;
            }

            public bool HasBanner => !string.IsNullOrEmpty(BannerPanelId);
        }

        private sealed class FilterSlider : CustomSlider
        {
            private readonly float _step;
            private bool _isSnapping;
            private Text _displayLabel;

            public FilterSlider(
                string name,
                Action<float> storeValue,
                Func<float> loadValue,
                float minValue,
                float maxValue,
                bool wholeNumbers = false,
                float step = 0f,
                string id = "__UseName")
                : base(name, storeValue, loadValue, minValue, maxValue, wholeNumbers, id)
            {
                _step = step;
            }

            public override GameObjectRow Create(ContentArea c, Menu Instance, bool AddToList = true)
            {
                GameObjectRow row = base.Create(c, Instance, AddToList);
                if (_step > 0f && !wholeNumbers && slider != null)
                {
                    slider.onValueChanged.AddListener(OnSliderChangedSnap);
                }

                SetupIsolatedDisplayLabel();
                return row;
            }

            public override void Update()
            {
                if (valueLabel != null)
                {
                    valueLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
                    valueLabel.verticalOverflow = VerticalWrapMode.Overflow;
                }

                base.Update();
                SyncDisplayLabel();
            }

            protected override void UpdateValueLabel()
            {
                if (valueLabel == null)
                {
                    return;
                }

                if (wholeNumbers)
                {
                    valueLabel.text = $"{Mathf.RoundToInt(value)}";
                }
                else if (_step > 0f)
                {
                    valueLabel.text = FormatWithStep(value, _step);
                }
                else
                {
                    base.UpdateValueLabel();
                }

                FixVerticalAlign align = valueLabel.GetComponent<FixVerticalAlign>();
                if (align != null)
                {
                    align.AlignText();
                }
            }

            private void SetupIsolatedDisplayLabel()
            {
                Text origLabel = label; // Cache CustomSlider._label onto the hidden stub.
                if (origLabel == null || _displayLabel != null)
                {
                    return;
                }

                GameObject displayGo = UnityEngine.Object.Instantiate(origLabel.gameObject, origLabel.transform.parent);
                displayGo.name = "Label";
                displayGo.transform.SetSiblingIndex(origLabel.transform.GetSiblingIndex());
                _displayLabel = displayGo.GetComponent<Text>();

                origLabel.gameObject.name = "Label_CustomSliderStub";
                origLabel.enabled = false;
                LayoutElement stubLayout = origLabel.gameObject.GetComponent<LayoutElement>() ?? origLabel.gameObject.AddComponent<LayoutElement>();
                stubLayout.ignoreLayout = true;

                FixVerticalAlign stubAlign = origLabel.GetComponent<FixVerticalAlign>();
                if (stubAlign != null)
                {
                    UnityEngine.Object.DestroyImmediate(stubAlign);
                }

                SyncDisplayLabel();
            }

            private void SyncDisplayLabel()
            {
                if (_displayLabel == null)
                {
                    return;
                }

                _displayLabel.text = Name;
                FixVerticalAlign align = _displayLabel.GetComponent<FixVerticalAlign>();
                if (align != null)
                {
                    align.AlignText();
                }
            }

            private void OnSliderChangedSnap(float rawValue)
            {
                if (_isSnapping || _step <= 0f || wholeNumbers || slider == null)
                {
                    return;
                }

                float stepped = minValue + Mathf.Round((rawValue - minValue) / _step) * _step;
                float snapped = Mathf.Clamp((float)Math.Round(stepped, 4), minValue, maxValue);
                if (!Mathf.Approximately(slider.value, snapped))
                {
                    _isSnapping = true;
                    slider.value = snapped;
                    _isSnapping = false;
                }
            }

            private static string FormatWithStep(float val, float step)
            {
                int decimals = 1;
                if (step > 0f && step < 1f)
                {
                    float scaled = step;
                    decimals = 0;
                    while (decimals < 4 && Mathf.Abs(scaled - Mathf.Round(scaled)) > 0.001f)
                    {
                        scaled *= 10f;
                        decimals++;
                    }

                    decimals = Mathf.Max(1, decimals);
                }

                return val.ToString("F" + decimals, CultureInfo.InvariantCulture);
            }
        }
    }
}