using UnityEngine;
using Microsoft.MixedReality.Toolkit.UI;

public class WaterLevelController : MonoBehaviour
{
    [Header("References")]
    public PinchSlider slider;
    public FloodManager floodManager;
    public FloodSource[] floodSources;
    public PointSelectManager pointSelectManager;

    [Header("Scenario Control")]
    public float minIntensityMultiplier = 0.5f;
    public float maxIntensityMultiplier = 3f;

    public float minRadiusMultiplier = 0.75f;
    public float maxRadiusMultiplier = 2.5f;

    [Header("Optional Legacy Visual")]
    public Transform waterPlane;
    public bool stillMoveLegacyWaterPlane = false;
    public float minLegacyLevel = 0f;
    public float maxLegacyLevel = 0.2f;

    private float baseLocalY;
    private float[] baseIntensities;
    private float[] baseRadii;

    private void Start()
    {
        if (slider == null) return;

        if (waterPlane != null)
            baseLocalY = waterPlane.localPosition.y;

        CacheFloodSourceBaseValues();

        slider.OnValueUpdated.AddListener(OnSliderUpdated);
        Apply(slider.SliderValue);
    }

    private void OnDestroy()
    {
        if (slider != null)
            slider.OnValueUpdated.RemoveListener(OnSliderUpdated);
    }

    private void CacheFloodSourceBaseValues()
    {
        if (floodSources == null) return;

        baseIntensities = new float[floodSources.Length];
        baseRadii = new float[floodSources.Length];

        for (int i = 0; i < floodSources.Length; i++)
        {
            if (floodSources[i] == null) continue;
            baseIntensities[i] = floodSources[i].intensity;
            baseRadii[i] = floodSources[i].radius;
        }
    }

    private void OnSliderUpdated(SliderEventData data)
    {
        Apply(data.NewValue);

        if (floodManager != null)
            floodManager.UpdateFloodState();

        pointSelectManager?.ResetSelection();
    }

    private void Apply(float t)
    {
        float intensityMultiplier = Mathf.Lerp(minIntensityMultiplier, maxIntensityMultiplier, t);
        float radiusMultiplier = Mathf.Lerp(minRadiusMultiplier, maxRadiusMultiplier, t);

        if (floodSources != null && baseIntensities != null && baseRadii != null)
        {
            for (int i = 0; i < floodSources.Length; i++)
            {
                if (floodSources[i] == null) continue;

                floodSources[i].intensity = baseIntensities[i] * intensityMultiplier;
                floodSources[i].radius = baseRadii[i] * radiusMultiplier;
            }
        }

        if (stillMoveLegacyWaterPlane && waterPlane != null)
        {
            float level = Mathf.Lerp(minLegacyLevel, maxLegacyLevel, t);
            Vector3 lp = waterPlane.localPosition;
            lp.y = baseLocalY + level;
            waterPlane.localPosition = lp;
        }
    }
}