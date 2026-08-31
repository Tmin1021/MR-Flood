using System;
using System.Reflection;
using UnityEngine;

public class TrackedMarkerCityAnchorAdapter : MonoBehaviour
{
    public CityModelAnchor cityAnchor;

    [Header("QR Filter")]
    public string requiredPayload = "FLOOD_CITY_ANCHOR_01";

    [Tooltip("Keep true if the marker pose needs a few frames to become stable.")]
    public bool updateUntilLocked = true;

    private bool warnedAboutMissingPayload;
    private bool warnedAboutManualPlacementOverride;

    private void Awake()
    {
        ResolveCityAnchor();
    }

    private void OnEnable()
    {
        TryAnchor();
    }

    private void Update()
    {
        if (updateUntilLocked)
            TryAnchor();
    }

    private void TryAnchor()
    {
        if (CityAnchorManager.IsManualPlacementFlowActive)
        {
            if (!warnedAboutManualPlacementOverride)
            {
                Debug.Log("TrackedMarkerCityAnchorAdapter: ignored because manual plane placement flow is active.");
                warnedAboutManualPlacementOverride = true;
            }

            return;
        }

        ResolveCityAnchor();

        if (cityAnchor == null)
            return;

        if (!HasMatchingPayload())
            return;

        cityAnchor.AnchorToTransform(transform);
    }

    private void ResolveCityAnchor()
    {
        if (cityAnchor == null)
            cityAnchor = FindFirstObjectByType<CityModelAnchor>(FindObjectsInactive.Include);
    }

    private bool HasMatchingPayload()
    {
        if (string.IsNullOrWhiteSpace(requiredPayload))
            return true;

        string payload = TryReadMarkerPayload();

        if (string.IsNullOrWhiteSpace(payload))
        {
            if (!warnedAboutMissingPayload)
            {
                Debug.LogWarning(
                    $"TrackedMarkerCityAnchorAdapter on '{name}' could not read a QR payload yet. " +
                    "The city will not anchor until the marker component exposes one.");
                warnedAboutMissingPayload = true;
            }

            return false;
        }

        warnedAboutMissingPayload = false;
        return string.Equals(payload.Trim(), requiredPayload, StringComparison.Ordinal);
    }

    private string TryReadMarkerPayload()
    {
        Component[] components = GetComponents<Component>();

        foreach (Component component in components)
        {
            if (component == null || component == this || component is Transform)
                continue;

            string value = TryReadMember(
                component,
                "MarkerPayloadString",
                "Payload",
                "DecodedString",
                "DecodedValue",
                "Value",
                "Text",
                "Content");

            if (!string.IsNullOrWhiteSpace(value))
                return value;

            value = TryInvokeStringMethod(
                component,
                "GetPayload",
                "GetDecodedString",
                "GetDecodedValue",
                "GetValue");

            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string TryReadMember(Component component, params string[] memberNames)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = component.GetType();

        foreach (string memberName in memberNames)
        {
            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null && property.CanRead && property.PropertyType == typeof(string))
            {
                object value = property.GetValue(component, null);
                if (value is string text && !string.IsNullOrWhiteSpace(text))
                    return text;
            }

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null && field.FieldType == typeof(string))
            {
                object value = field.GetValue(component);
                if (value is string text && !string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }

        return null;
    }

    private static string TryInvokeStringMethod(Component component, params string[] methodNames)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = component.GetType();

        foreach (string methodName in methodNames)
        {
            MethodInfo method = type.GetMethod(methodName, flags, null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(string))
                continue;

            object value = method.Invoke(component, null);
            if (value is string text && !string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }
}
