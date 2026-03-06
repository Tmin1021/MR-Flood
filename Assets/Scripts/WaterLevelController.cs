using UnityEngine;
using Microsoft.MixedReality.Toolkit.UI;

public class WaterLevelController : MonoBehaviour
{
    [Header("References")]
    public PinchSlider slider;
    public Transform waterPlane;  
    public Transform city;         

    [Header("Flood level")]
    public float minLevel = 0f;  
    public float maxLevel = 200f;  

    private float baseLocalY;

    void Start()
    {
        if (!slider || !waterPlane || !city) return;

        baseLocalY = waterPlane.localPosition.y;

        slider.OnValueUpdated.AddListener(OnSliderUpdated);

        Apply(slider.SliderValue);
    }

    void OnDestroy()
    {
        if (slider != null) slider.OnValueUpdated.RemoveListener(OnSliderUpdated);
    }

    private void OnSliderUpdated(SliderEventData data) => Apply(data.NewValue);

    private void Apply(float t)
    {
        float level = Mathf.Lerp(minLevel, maxLevel, t);

        Vector3 lp = waterPlane.localPosition;
        lp.y = baseLocalY + level;
        waterPlane.localPosition = lp;
    }
}
