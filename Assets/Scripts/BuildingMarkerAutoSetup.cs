using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

// Editor
public class BuildingMarkerAutoSetup : MonoBehaviour
{
    [Header("Root that contains building objects")]
    public Transform buildingsRoot;

    [ContextMenu("Add Or Update Building Markers")]
    public void AddOrUpdateBuildingMarkers()
    {
        Transform root = buildingsRoot != null ? buildingsRoot : transform;

        int updatedCount = 0;
        int namedCount = 0;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform building = root.GetChild(i);
            if (building == null) continue;

            if (building.GetComponentInChildren<Renderer>(true) == null)
                continue;

            BuildingMarker marker = building.GetComponent<BuildingMarker>();
            if (marker == null)
                marker = building.gameObject.AddComponent<BuildingMarker>();

            marker.buildingId = building.name;

            string osmName = GetOSMName(building.gameObject);
            marker.displayName = !string.IsNullOrWhiteSpace(osmName)
                ? osmName.Trim()
                : building.name;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(marker);
            UnityEditor.EditorUtility.SetDirty(building.gameObject);
#endif

            updatedCount++;
            if (!string.IsNullOrWhiteSpace(osmName))
                namedCount++;
        }

        Debug.Log($"BuildingMarkerAutoSetup: updated {updatedCount} markers, found {namedCount} OSM names.");
    }

    private string GetOSMName(GameObject go)
    {
        Component meta = FindComponentByTypeName(go, "RealWorldTerrainOSMMeta");
        if (meta == null) return null;

        string value;

        value = TryCallGetter(meta, "GetValue", "name");
        if (!string.IsNullOrWhiteSpace(value)) return value;

        value = TryCallGetter(meta, "GetInfo", "name");
        if (!string.IsNullOrWhiteSpace(value)) return value;

        value = TryCallGetter(meta, "GetTagValue", "name");
        if (!string.IsNullOrWhiteSpace(value)) return value;

        value = TryReadFromDictionary(meta, "name");
        if (!string.IsNullOrWhiteSpace(value)) return value;

        value = TryReadFromCollections(meta, "name");
        if (!string.IsNullOrWhiteSpace(value)) return value;

        return null;
    }

    private Component FindComponentByTypeName(GameObject go, string typeName)
    {
        Component[] components = go.GetComponents<Component>();

        foreach (Component c in components)
        {
            if (c == null) continue;

            Type t = c.GetType();
            string fullName = t.FullName ?? "";

            if (t.Name == typeName || fullName.EndsWith("." + typeName, StringComparison.Ordinal))
                return c;
        }

        return null;
    }

    private string TryReadFromDictionary(Component component, string wantedKey)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = component.GetType();

        foreach (FieldInfo field in type.GetFields(flags))
        {
            if (!typeof(IDictionary).IsAssignableFrom(field.FieldType))
                continue;

            IDictionary dict = field.GetValue(component) as IDictionary;
            string result = ReadDictionary(dict, wantedKey);
            if (!string.IsNullOrWhiteSpace(result))
                return result;
        }

        foreach (PropertyInfo prop in type.GetProperties(flags))
        {
            if (!prop.CanRead) continue;
            if (!typeof(IDictionary).IsAssignableFrom(prop.PropertyType))
                continue;

            IDictionary dict = SafeGetPropertyValue(component, prop) as IDictionary;
            string result = ReadDictionary(dict, wantedKey);
            if (!string.IsNullOrWhiteSpace(result))
                return result;
        }

        return null;
    }

    private string ReadDictionary(IDictionary dict, string wantedKey)
    {
        if (dict == null) return null;

        foreach (DictionaryEntry entry in dict)
        {
            string key = entry.Key?.ToString();
            if (string.Equals(key, wantedKey, StringComparison.OrdinalIgnoreCase))
                return entry.Value?.ToString();
        }

        return null;
    }

    private string TryReadFromCollections(Component component, string wantedKey)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = component.GetType();

        foreach (FieldInfo field in type.GetFields(flags))
        {
            if (field.FieldType == typeof(string)) continue;

            string result = TryReadKeyValueEnumerable(field.GetValue(component) as IEnumerable, wantedKey);
            if (!string.IsNullOrWhiteSpace(result))
                return result;
        }

        foreach (PropertyInfo prop in type.GetProperties(flags))
        {
            if (!prop.CanRead) continue;
            if (prop.PropertyType == typeof(string)) continue;

            object value = SafeGetPropertyValue(component, prop);
            string result = TryReadKeyValueEnumerable(value as IEnumerable, wantedKey);
            if (!string.IsNullOrWhiteSpace(result))
                return result;
        }

        return null;
    }

    private string TryReadKeyValueEnumerable(IEnumerable enumerable, string wantedKey)
    {
        if (enumerable == null) return null;

        foreach (object item in enumerable)
        {
            if (item == null) continue;
            if (item is string) continue;

            string key = ReadMember(item, "key", "name", "tag", "id");
            if (!string.Equals(key, wantedKey, StringComparison.OrdinalIgnoreCase))
                continue;

            string value = ReadMember(item, "value", "info", "content", "text");
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private string ReadMember(object obj, params string[] memberNames)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = obj.GetType();

        foreach (string memberName in memberNames)
        {
            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
            {
                object value = field.GetValue(obj);
                if (value != null) return value.ToString();
            }

            PropertyInfo prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanRead)
            {
                object value = prop.GetValue(obj, null);
                if (value != null) return value.ToString();
            }
        }

        return null;
    }

    private object SafeGetPropertyValue(object obj, PropertyInfo prop)
    {
        try
        {
            return prop.GetValue(obj, null);
        }
        catch
        {
            return null;
        }
    }

    private string TryCallGetter(Component component, string methodName, string key)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo method = component.GetType().GetMethod(methodName, flags, null, new[] { typeof(string) }, null);

        if (method == null) return null;

        try
        {
            object result = method.Invoke(component, new object[] { key });
            return result?.ToString();
        }
        catch
        {
            return null;
        }
    }
}