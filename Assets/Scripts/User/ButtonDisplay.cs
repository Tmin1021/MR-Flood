using UnityEngine;

public class ButtonDisplay : MonoBehaviour
{
    public Material onColor;
    public Material offColor;

    private Renderer _r;
    private bool _isOn;

    void Awake()
    {
        _r = GetComponent<Renderer>();
    }

    private void Start()
    {
        SetVisual(_isOn);
    }

    // Call this from the MRTK button OnClick event
    public void ToggleBackground()
    {
        _isOn = !_isOn;
        SetVisual(_isOn);
    }

    public void SetState(bool value)
    {
        _isOn = value;
        SetVisual(_isOn);
    }

    private void SetVisual(bool on)
    {
        if (!_r || !onColor || !offColor) return;
        _r.sharedMaterial = on ? onColor : offColor;
    }
}
