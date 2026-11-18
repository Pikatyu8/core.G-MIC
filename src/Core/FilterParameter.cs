using System;
using UnityEngine;

namespace CoreGMIC.Core
{
    public enum FilterParamKind
    {
        Float,
        Int,
        Bool,
        Choice
    }

    public class FilterParameter
    {
        public string Key { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public FilterParamKind Kind { get; }
        public float MinValue { get; }
        public float MaxValue { get; }
        public float DefaultValue { get; }
        public float Step { get; }
        public string[] Choices { get; }
        public Func<bool> IsVisible { get; }

        private float _value;
        private readonly Action<float> _onChanged;

        public float Value
        {
            get => _value;
            set
            {
                float normalized = Normalize(value);
                if (Mathf.Approximately(_value, normalized))
                {
                    return;
                }

                _value = normalized;
                _onChanged?.Invoke(_value);
            }
        }

        public int IntValue => Mathf.RoundToInt(_value);
        public bool BoolValue => _value >= 0.5f;

        public FilterParameter(
            string key,
            string displayName,
            string description,
            FilterParamKind kind,
            float minValue,
            float maxValue,
            float defaultValue,
            Action<float> onChanged = null,
            string[] choices = null,
            Func<bool> isVisible = null,
            float step = 0f)
        {
            Key = key;
            DisplayName = displayName;
            Description = description ?? string.Empty;
            Kind = kind;
            MinValue = minValue;
            MaxValue = maxValue;
            Step = step;
            DefaultValue = Normalize(defaultValue);
            Choices = choices;
            IsVisible = isVisible;
            _onChanged = onChanged;
            _value = DefaultValue;
        }

        public void Reset()
        {
            Value = DefaultValue;
            ForceApply();
        }

        public void ForceApply()
        {
            _onChanged?.Invoke(_value);
        }

        private float Normalize(float raw)
        {
            float clamped = Mathf.Clamp(raw, MinValue, MaxValue);
            switch (Kind)
            {
                case FilterParamKind.Int:
                case FilterParamKind.Choice:
                    return Mathf.Round(clamped);
                case FilterParamKind.Bool:
                    return clamped >= 0.5f ? 1f : 0f;
                default:
                    if (Step > 0f)
                    {
                        float stepped = MinValue + Mathf.Round((clamped - MinValue) / Step) * Step;
                        return Mathf.Clamp((float)Math.Round(stepped, 4), MinValue, MaxValue);
                    }
                    return clamped;
            }
        }

        public static FilterParameter Float(
            string key, string displayName, string description,
            float min, float max, float defaultValue,
            Action<float> onChanged = null, Func<bool> isVisible = null, float step = 0f)
        {
            return new FilterParameter(key, displayName, description, FilterParamKind.Float, min, max, defaultValue, onChanged, null, isVisible, step);
        }

        public static FilterParameter Int(
            string key, string displayName, string description,
            int min, int max, int defaultValue,
            Action<int> onChanged = null, Func<bool> isVisible = null)
        {
            return new FilterParameter(
                key, displayName, description, FilterParamKind.Int,
                min, max, defaultValue,
                onChanged != null ? v => onChanged(Mathf.RoundToInt(v)) : null,
                null, isVisible);
        }

        public static FilterParameter Bool(
            string key, string displayName, string description,
            bool defaultValue,
            Action<bool> onChanged = null, Func<bool> isVisible = null)
        {
            return new FilterParameter(
                key, displayName, description, FilterParamKind.Bool,
                0f, 1f, defaultValue ? 1f : 0f,
                onChanged != null ? v => onChanged(v >= 0.5f) : null,
                null, isVisible);
        }

        public static FilterParameter Choice(
            string key, string displayName, string description,
            string[] choices, int defaultIndex,
            Action<int> onChanged = null, Func<bool> isVisible = null)
        {
            int maxIdx = (choices != null && choices.Length > 0) ? choices.Length - 1 : 0;
            return new FilterParameter(
                key, displayName, description, FilterParamKind.Choice,
                0f, maxIdx, defaultIndex,
                onChanged != null ? v => onChanged(Mathf.RoundToInt(v)) : null,
                choices, isVisible);
        }
    }
}