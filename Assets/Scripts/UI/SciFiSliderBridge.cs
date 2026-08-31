using System;
using Microsoft.MixedReality.Toolkit.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class SciFiSliderBridge : MonoBehaviour
{
    [Serializable]
    public class FloatEvent : UnityEvent<float> { }

    [Header("MRTK Slider")]
    public PinchSlider pinchSlider;

    [Header("Optional SCIFI Visual Mirror")]
    public Slider sciFiSlider;
    public Image fillImage;
    public Text legacyValueText;
    public TMP_Text tmpValueText;

    [Header("Value Mapping")]
    public float minValue = 0f;
    public float maxValue = 1f;
    public string valueFormat = "0.##";
    public bool showPercent;

    [Header("Startup")]
    public bool setInitialNormalizedValue;
    [Range(0f, 1f)] public float initialNormalizedValue = 0.5f;
    public bool invokeOnStart;

    [Header("Actions")]
    public FloatEvent onValueChanged;
    public UnityEvent onInteractionStarted;
    public UnityEvent onInteractionEnded;

    private bool suppressCallbacks;

    private void OnEnable()
    {
        AutoFillReferences();

        if (pinchSlider != null)
        {
            pinchSlider.OnValueUpdated.AddListener(OnPinchSliderUpdated);
            pinchSlider.OnInteractionStarted.AddListener(OnPinchInteractionStarted);
            pinchSlider.OnInteractionEnded.AddListener(OnPinchInteractionEnded);
        }

        if (sciFiSlider != null)
            sciFiSlider.onValueChanged.AddListener(OnSciFiSliderUpdated);

        if (setInitialNormalizedValue)
            SetNormalizedValue(initialNormalizedValue, false, true);
        else
            RefreshVisuals(GetCurrentNormalizedValue());
    }

    private void Start()
    {
        if (invokeOnStart)
            NotifyValueChanged(GetCurrentNormalizedValue());
    }

    private void OnDisable()
    {
        if (pinchSlider != null)
        {
            pinchSlider.OnValueUpdated.RemoveListener(OnPinchSliderUpdated);
            pinchSlider.OnInteractionStarted.RemoveListener(OnPinchInteractionStarted);
            pinchSlider.OnInteractionEnded.RemoveListener(OnPinchInteractionEnded);
        }

        if (sciFiSlider != null)
            sciFiSlider.onValueChanged.RemoveListener(OnSciFiSliderUpdated);
    }

    public void SetNormalizedValue(float normalizedValue)
    {
        SetNormalizedValue(normalizedValue, true, true);
    }

    public void SetMappedValue(float mappedValue)
    {
        float normalizedValue = Mathf.Approximately(minValue, maxValue)
            ? 0f
            : Mathf.InverseLerp(minValue, maxValue, mappedValue);

        SetNormalizedValue(normalizedValue, true, true);
    }

    public void SetInteractable(bool value)
    {
        if (pinchSlider != null)
            pinchSlider.enabled = value;

        if (sciFiSlider != null)
            sciFiSlider.interactable = value;
    }

    private void AutoFillReferences()
    {
        if (pinchSlider == null)
            pinchSlider = GetComponent<PinchSlider>();

        if (sciFiSlider == null)
            sciFiSlider = GetComponent<Slider>();

        if (fillImage == null && sciFiSlider != null && sciFiSlider.fillRect != null)
            fillImage = sciFiSlider.fillRect.GetComponent<Image>();
    }

    private void OnPinchSliderUpdated(SliderEventData data)
    {
        if (suppressCallbacks)
            return;

        SetNormalizedValue(data.NewValue, true, false);
    }

    private void OnSciFiSliderUpdated(float value)
    {
        if (suppressCallbacks)
            return;

        float normalizedValue = Mathf.Approximately(sciFiSlider.minValue, sciFiSlider.maxValue)
            ? 0f
            : Mathf.InverseLerp(sciFiSlider.minValue, sciFiSlider.maxValue, value);

        SetNormalizedValue(normalizedValue, true, true);
    }

    private void OnPinchInteractionStarted(SliderEventData data)
    {
        onInteractionStarted?.Invoke();
    }

    private void OnPinchInteractionEnded(SliderEventData data)
    {
        onInteractionEnded?.Invoke();
    }

    private void SetNormalizedValue(float normalizedValue, bool notify, bool updatePinchSlider)
    {
        normalizedValue = Mathf.Clamp01(normalizedValue);

        if (updatePinchSlider && pinchSlider != null && !Mathf.Approximately(pinchSlider.SliderValue, normalizedValue))
        {
            suppressCallbacks = true;
            pinchSlider.SliderValue = normalizedValue;
            suppressCallbacks = false;
        }

        RefreshVisuals(normalizedValue);

        if (notify)
            NotifyValueChanged(normalizedValue);
    }

    private float GetCurrentNormalizedValue()
    {
        if (pinchSlider != null)
            return Mathf.Clamp01(pinchSlider.SliderValue);

        if (sciFiSlider != null)
        {
            return Mathf.Approximately(sciFiSlider.minValue, sciFiSlider.maxValue)
                ? 0f
                : Mathf.InverseLerp(sciFiSlider.minValue, sciFiSlider.maxValue, sciFiSlider.value);
        }

        return Mathf.Clamp01(initialNormalizedValue);
    }

    private void RefreshVisuals(float normalizedValue)
    {
        suppressCallbacks = true;

        if (sciFiSlider != null)
        {
            float sliderValue = Mathf.Lerp(sciFiSlider.minValue, sciFiSlider.maxValue, normalizedValue);
            sciFiSlider.SetValueWithoutNotify(sliderValue);
        }

        suppressCallbacks = false;

        if (fillImage != null && fillImage.type == Image.Type.Filled)
            fillImage.fillAmount = normalizedValue;

        string text = GetDisplayText(normalizedValue);

        if (legacyValueText != null)
            legacyValueText.text = text;

        if (tmpValueText != null)
            tmpValueText.text = text;
    }

    private void NotifyValueChanged(float normalizedValue)
    {
        onValueChanged?.Invoke(Mathf.Lerp(minValue, maxValue, normalizedValue));
    }

    private string GetDisplayText(float normalizedValue)
    {
        if (showPercent)
            return Mathf.RoundToInt(normalizedValue * 100f) + "%";

        float mappedValue = Mathf.Lerp(minValue, maxValue, normalizedValue);
        return mappedValue.ToString(valueFormat);
    }
}
