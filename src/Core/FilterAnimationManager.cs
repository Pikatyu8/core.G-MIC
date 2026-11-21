using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreGMIC.Core
{
    public static class FilterAnimationManager
    {
        public const float MinFrequency = 0.05f;
        public const float MaxFrequency = 10.0f;
        public const float DefaultFrequency = 0.5f;
        public const float DefaultFrequencyStep = 0.05f;

        private static readonly Dictionary<string, string> _selectedParamKeys = new Dictionary<string, string>();
        private static IFilter _lastAnimatedFilter;
        private static float _startTime;

        public static void Initialize(ModSettings settings)
        {
            _startTime = Time.realtimeSinceStartup;
            if (settings == null)
            {
                return;
            }

            settings.FilterAnimations ??= new Dictionary<string, FilterAnimationSettings>();

            foreach (IFilter filter in FilterManager.Filters)
            {
                if (!settings.FilterAnimations.TryGetValue(filter.Id, out FilterAnimationSettings filterAnim) || filterAnim == null)
                {
                    continue;
                }

                filterAnim.Parameters ??= new Dictionary<string, ParameterAnimationSettings>();
                foreach (FilterParameter param in GetNumericParameters(filter))
                {
                    if (filterAnim.Parameters.TryGetValue(param.Key, out ParameterAnimationSettings pAnim) && pAnim != null)
                    {
                        pAnim.Frequency = ClampFrequency(param, pAnim.Frequency);
                        pAnim.MinValue = Mathf.Clamp(pAnim.MinValue, param.MinValue, param.MaxValue);
                        pAnim.MaxValue = Mathf.Clamp(pAnim.MaxValue, param.MinValue, param.MaxValue);
                    }
                }
            }
        }

        public static void GetFrequencySpec(
            FilterParameter param,
            out float minFreq,
            out float maxFreq,
            out float step)
        {
            minFreq = param?.AnimMinFrequency ?? MinFrequency;
            maxFreq = param?.AnimMaxFrequency ?? MaxFrequency;
            if (maxFreq < minFreq)
            {
                maxFreq = minFreq;
            }

            step = param?.AnimFrequencyStep ?? DefaultFrequencyStep;
        }

        public static float ClampFrequency(FilterParameter param, float frequency)
        {
            GetFrequencySpec(param, out float minFreq, out float maxFreq, out float step);
            float clamped = Mathf.Clamp(frequency, minFreq, maxFreq);
            if (step > 0f)
            {
                float stepped = minFreq + Mathf.Round((clamped - minFreq) / step) * step;
                clamped = Mathf.Clamp((float)Math.Round(stepped, 4), minFreq, maxFreq);
            }

            return clamped;
        }

        public static bool HasNumericParameters(IFilter filter)
        {
            if (filter?.Parameters == null)
            {
                return false;
            }

            for (int i = 0; i < filter.Parameters.Count; i++)
            {
                FilterParamKind kind = filter.Parameters[i].Kind;
                if (kind == FilterParamKind.Float || kind == FilterParamKind.Int)
                {
                    return true;
                }
            }

            return false;
        }

        public static List<FilterParameter> GetNumericParameters(IFilter filter)
        {
            List<FilterParameter> result = new List<FilterParameter>();
            if (filter?.Parameters == null)
            {
                return result;
            }

            for (int i = 0; i < filter.Parameters.Count; i++)
            {
                FilterParameter p = filter.Parameters[i];
                if (p.Kind == FilterParamKind.Float || p.Kind == FilterParamKind.Int)
                {
                    result.Add(p);
                }
            }

            return result;
        }

        public static List<FilterParameter> GetVisibleNumericParameters(IFilter filter)
        {
            List<FilterParameter> result = new List<FilterParameter>();
            if (filter?.Parameters == null)
            {
                return result;
            }

            for (int i = 0; i < filter.Parameters.Count; i++)
            {
                FilterParameter p = filter.Parameters[i];
                if ((p.Kind == FilterParamKind.Float || p.Kind == FilterParamKind.Int) &&
                    (p.IsVisible == null || p.IsVisible()))
                {
                    result.Add(p);
                }
            }

            return result;
        }

        public static bool IsFilterAnimationEnabled(string filterId)
        {
            FilterAnimationSettings anim = GetFilterSettings(filterId, createIfMissing: false);
            return anim != null && anim.Enabled;
        }

        public static void SetFilterAnimationEnabled(IFilter filter, bool enabled)
        {
            if (filter == null)
            {
                return;
            }

            FilterAnimationSettings anim = GetFilterSettings(filter.Id, createIfMissing: true);
            if (anim != null)
            {
                anim.Enabled = enabled;
            }

            if (!enabled)
            {
                RestoreFilterBaseValues(filter);
            }
        }

        public static bool IsSynchronizeFreq(string filterId)
        {
            FilterAnimationSettings anim = GetFilterSettings(filterId, createIfMissing: false);
            return anim != null && anim.SynchronizeFreq;
        }

        public static void SetSynchronizeFreq(IFilter filter, bool enabled)
        {
            if (filter == null)
            {
                return;
            }

            FilterAnimationSettings anim = GetFilterSettings(filter.Id, createIfMissing: true);
            if (anim != null)
            {
                anim.SynchronizeFreq = enabled;
            }

            if (enabled)
            {
                SyncEnabledFrequencies(filter);
            }
        }

        public static int GetSelectedTargetIndex(IFilter filter)
        {
            if (filter == null)
            {
                return 0;
            }

            List<FilterParameter> visible = GetVisibleNumericParameters(filter);
            if (visible.Count == 0)
            {
                return 0;
            }

            if (_selectedParamKeys.TryGetValue(filter.Id, out string key))
            {
                int idx = visible.FindIndex(p => p.Key == key);
                if (idx >= 0)
                {
                    return idx;
                }
            }

            _selectedParamKeys[filter.Id] = visible[0].Key;
            return 0;
        }

        public static void SetSelectedTargetIndex(IFilter filter, int index)
        {
            if (filter == null)
            {
                return;
            }

            List<FilterParameter> visible = GetVisibleNumericParameters(filter);
            if (visible.Count == 0)
            {
                return;
            }

            int clamped = Mathf.Clamp(index, 0, visible.Count - 1);
            _selectedParamKeys[filter.Id] = visible[clamped].Key;
        }

        public static FilterParameter GetSelectedTargetParam(IFilter filter)
        {
            if (filter == null)
            {
                return null;
            }

            List<FilterParameter> visible = GetVisibleNumericParameters(filter);
            if (visible.Count == 0)
            {
                return null;
            }

            int idx = GetSelectedTargetIndex(filter);
            return visible[idx];
        }

        public static string[] GetTargetOptionLabels(IFilter filter)
        {
            if (filter == null)
            {
                return new[] { "None" };
            }

            List<FilterParameter> visible = GetVisibleNumericParameters(filter);
            if (visible.Count == 0)
            {
                return new[] { "None" };
            }

            string[] labels = new string[visible.Count];
            for (int i = 0; i < visible.Count; i++)
            {
                labels[i] = visible[i].DisplayName;
            }

            return labels;
        }

        public static ParameterAnimationSettings GetOrCreateParamSettings(string filterId, FilterParameter param)
        {
            if (string.IsNullOrEmpty(filterId) || param == null)
            {
                return null;
            }

            FilterAnimationSettings filterAnim = GetFilterSettings(filterId, createIfMissing: true);
            if (filterAnim == null)
            {
                return null;
            }

            filterAnim.Parameters ??= new Dictionary<string, ParameterAnimationSettings>();
            if (!filterAnim.Parameters.TryGetValue(param.Key, out ParameterAnimationSettings pAnim) || pAnim == null)
            {
                pAnim = new ParameterAnimationSettings
                {
                    Enabled = false,
                    Mode = ParameterAnimationMode.Oscillate,
                    Frequency = ClampFrequency(param, DefaultFrequency),
                    MinValue = param.MinValue,
                    MaxValue = param.MaxValue
                };
                filterAnim.Parameters[param.Key] = pAnim;
            }
            else
            {
                pAnim.Frequency = ClampFrequency(param, pAnim.Frequency);
            }

            return pAnim;
        }

        public static bool IsParamEnabled(string filterId, FilterParameter param)
        {
            if (string.IsNullOrEmpty(filterId) || param == null)
            {
                return false;
            }

            FilterAnimationSettings filterAnim = GetFilterSettings(filterId, createIfMissing: false);
            if (filterAnim?.Parameters == null)
            {
                return false;
            }

            return filterAnim.Parameters.TryGetValue(param.Key, out ParameterAnimationSettings pAnim) &&
                   pAnim != null &&
                   pAnim.Enabled;
        }

        public static void SetParamEnabled(IFilter filter, FilterParameter param, bool enabled)
        {
            if (filter == null || param == null)
            {
                return;
            }

            ParameterAnimationSettings pAnim = GetOrCreateParamSettings(filter.Id, param);
            if (pAnim != null)
            {
                pAnim.Enabled = enabled;
            }

            if (enabled && IsSynchronizeFreq(filter.Id))
            {
                SyncEnabledFrequencies(filter);
            }
            else if (!enabled)
            {
                param.RestoreBaseValue();
            }
        }

        public static ParameterAnimationMode GetParamMode(string filterId, FilterParameter param)
        {
            if (string.IsNullOrEmpty(filterId) || param == null)
            {
                return ParameterAnimationMode.Oscillate;
            }

            FilterAnimationSettings filterAnim = GetFilterSettings(filterId, createIfMissing: false);
            if (filterAnim?.Parameters != null &&
                filterAnim.Parameters.TryGetValue(param.Key, out ParameterAnimationSettings pAnim) &&
                pAnim != null)
            {
                return pAnim.Mode;
            }

            return ParameterAnimationMode.Oscillate;
        }

        public static void SetParamMode(string filterId, FilterParameter param, ParameterAnimationMode mode)
        {
            ParameterAnimationSettings pAnim = GetOrCreateParamSettings(filterId, param);
            if (pAnim != null)
            {
                pAnim.Mode = mode;
            }
        }

        public static void SetParamFrequency(IFilter filter, FilterParameter param, float frequency)
        {
            if (filter == null || param == null)
            {
                return;
            }

            float clamped = ClampFrequency(param, frequency);
            ParameterAnimationSettings pAnim = GetOrCreateParamSettings(filter.Id, param);
            if (pAnim != null)
            {
                pAnim.Frequency = clamped;
            }

            if (IsSynchronizeFreq(filter.Id))
            {
                SyncEnabledFrequencies(filter, clamped);
            }
        }

        public static void SetParamMin(string filterId, FilterParameter param, float minValue)
        {
            ParameterAnimationSettings pAnim = GetOrCreateParamSettings(filterId, param);
            if (pAnim != null)
            {
                float clamped = Mathf.Clamp(minValue, param.MinValue, param.MaxValue);
                pAnim.MinValue = (param.Kind == FilterParamKind.Int) ? Mathf.Round(clamped) : clamped;
            }
        }

        public static void SetParamMax(string filterId, FilterParameter param, float maxValue)
        {
            ParameterAnimationSettings pAnim = GetOrCreateParamSettings(filterId, param);
            if (pAnim != null)
            {
                float clamped = Mathf.Clamp(maxValue, param.MinValue, param.MaxValue);
                pAnim.MaxValue = (param.Kind == FilterParamKind.Int) ? Mathf.Round(clamped) : clamped;
            }
        }

        public static void ResetFilter(IFilter filter)
        {
            if (filter == null)
            {
                return;
            }

            FilterAnimationSettings anim = GetFilterSettings(filter.Id, createIfMissing: false);
            if (anim != null)
            {
                anim.Enabled = false;
                anim.SynchronizeFreq = false;
                anim.Parameters?.Clear();
            }

            _selectedParamKeys.Remove(filter.Id);
            RestoreFilterBaseValues(filter);
        }

        public static void UpdateActiveFilter(IFilter activeFilter)
        {
            if (_lastAnimatedFilter != null && _lastAnimatedFilter != activeFilter)
            {
                RestoreFilterBaseValues(_lastAnimatedFilter);
            }

            _lastAnimatedFilter = activeFilter;
            if (activeFilter == null || activeFilter.Parameters == null)
            {
                return;
            }

            FilterAnimationSettings filterAnim = GetFilterSettings(activeFilter.Id, createIfMissing: false);
            if (filterAnim == null || !filterAnim.Enabled || filterAnim.Parameters == null)
            {
                RestoreFilterBaseValues(activeFilter);
                return;
            }

            float elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - _startTime);
            float? syncedFreq = filterAnim.SynchronizeFreq ? GetFirstEnabledFrequency(activeFilter, filterAnim) : null;

            for (int i = 0; i < activeFilter.Parameters.Count; i++)
            {
                FilterParameter param = activeFilter.Parameters[i];
                if (param.Kind != FilterParamKind.Float && param.Kind != FilterParamKind.Int)
                {
                    continue;
                }

                if (param.IsVisible != null && !param.IsVisible())
                {
                    param.RestoreBaseValue();
                    continue;
                }

                if (!filterAnim.Parameters.TryGetValue(param.Key, out ParameterAnimationSettings pAnim) ||
                    pAnim == null ||
                    !pAnim.Enabled)
                {
                    param.RestoreBaseValue();
                    continue;
                }

                float freq = ClampFrequency(param, syncedFreq ?? pAnim.Frequency);
                float t;
                switch (pAnim.Mode)
                {
                    case ParameterAnimationMode.Loop:
                        t = Mathf.Repeat(elapsed * freq, 1f);
                        break;
                    case ParameterAnimationMode.ReverseLoop:
                        t = 1f - Mathf.Repeat(elapsed * freq, 1f);
                        break;
                    default:
                        t = 0.5f - 0.5f * Mathf.Cos(elapsed * freq * Mathf.PI * 2f);
                        break;
                }

                float animValue = Mathf.Lerp(pAnim.MinValue, pAnim.MaxValue, t);
                param.ApplyAnimatedValue(animValue);
            }
        }

        public static void RestoreActiveFilter()
        {
            if (_lastAnimatedFilter != null)
            {
                RestoreFilterBaseValues(_lastAnimatedFilter);
                _lastAnimatedFilter = null;
            }
        }

        private static void SyncEnabledFrequencies(IFilter filter, float? overrideFreq = null)
        {
            FilterAnimationSettings filterAnim = GetFilterSettings(filter.Id, createIfMissing: false);
            if (filterAnim?.Parameters == null)
            {
                return;
            }

            float? targetFreq = overrideFreq ?? GetFirstEnabledFrequency(filter, filterAnim);
            if (!targetFreq.HasValue)
            {
                return;
            }

            List<FilterParameter> visible = GetVisibleNumericParameters(filter);
            for (int i = 0; i < visible.Count; i++)
            {
                FilterParameter param = visible[i];
                if (filterAnim.Parameters.TryGetValue(param.Key, out ParameterAnimationSettings pAnim) &&
                    pAnim != null &&
                    pAnim.Enabled)
                {
                    pAnim.Frequency = ClampFrequency(param, targetFreq.Value);
                }
            }
        }

        private static float? GetFirstEnabledFrequency(IFilter filter, FilterAnimationSettings filterAnim)
        {
            if (filter?.Parameters == null || filterAnim?.Parameters == null)
            {
                return null;
            }

            for (int i = 0; i < filter.Parameters.Count; i++)
            {
                FilterParameter p = filter.Parameters[i];
                if (p.Kind != FilterParamKind.Float && p.Kind != FilterParamKind.Int)
                {
                    continue;
                }

                if (p.IsVisible != null && !p.IsVisible())
                {
                    continue;
                }

                if (filterAnim.Parameters.TryGetValue(p.Key, out ParameterAnimationSettings pAnim) &&
                    pAnim != null &&
                    pAnim.Enabled)
                {
                    return ClampFrequency(p, pAnim.Frequency);
                }
            }

            return null;
        }

        private static void RestoreFilterBaseValues(IFilter filter)
        {
            if (filter?.Parameters == null)
            {
                return;
            }

            for (int i = 0; i < filter.Parameters.Count; i++)
            {
                FilterParameter param = filter.Parameters[i];
                if (param.Kind == FilterParamKind.Float || param.Kind == FilterParamKind.Int)
                {
                    param.RestoreBaseValue();
                }
            }
        }

        private static FilterAnimationSettings GetFilterSettings(string filterId, bool createIfMissing)
        {
            if (string.IsNullOrEmpty(filterId) || CoreGMICMod.Settings == null)
            {
                return null;
            }

            CoreGMICMod.Settings.FilterAnimations ??= new Dictionary<string, FilterAnimationSettings>();
            if (!CoreGMICMod.Settings.FilterAnimations.TryGetValue(filterId, out FilterAnimationSettings anim) || anim == null)
            {
                if (!createIfMissing)
                {
                    return null;
                }

                anim = new FilterAnimationSettings();
                CoreGMICMod.Settings.FilterAnimations[filterId] = anim;
            }

            return anim;
        }
    }
}