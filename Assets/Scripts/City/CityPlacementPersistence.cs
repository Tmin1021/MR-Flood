using System;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class CityPlacementSaveData
{
    public int version = CityPlacementPersistence.CurrentVersion;
    public bool hasConfirmedPlacement;
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
    public Vector3 localScale = Vector3.one;
    public bool useOsmTerrain = true;
    public bool usesSpatialAnchor;
    public string spatialAnchorName;
}

/// <summary>
/// Stores the confirmed city configuration. The device-local spatial anchor is persisted
/// separately by CitySpatialAnchorPersistence and referenced by name from this JSON data.
/// </summary>
public sealed class CityPlacementPersistence
{
    public const int CurrentVersion = 3;
    private const string SaveFileName = "city-placement.json";

    public string SaveFilePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    public bool HasSavedPlacement()
    {
        try
        {
            return File.Exists(SaveFilePath);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"CityPlacementPersistence: could not check '{SaveFilePath}': {exception.Message}");
            return false;
        }
    }

    public bool TryLoad(out CityPlacementSaveData data)
    {
        data = null;
        string path = SaveFilePath;

        try
        {
            if (!File.Exists(path))
            {
                Debug.Log($"CityPlacementPersistence: no saved placement found at '{path}'.");
                return false;
            }

            string json = File.ReadAllText(path);
            CityPlacementSaveData loaded = JsonUtility.FromJson<CityPlacementSaveData>(json);

            // Version 2 did not record a map source. Its default terrain was OSM.
            if (loaded != null && loaded.version == 2)
            {
                loaded.version = CurrentVersion;
                loaded.useOsmTerrain = true;
            }

            if (!TryValidate(loaded, out string validationError))
            {
                Debug.LogWarning(
                    $"CityPlacementPersistence: invalid save file rejected at '{path}': {validationError}");
                DeleteInvalidFile(path);
                return false;
            }

            loaded.rotation = Normalize(loaded.rotation);
            data = loaded;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"CityPlacementPersistence: restore failed for '{path}': {exception.Message}");
            DeleteInvalidFile(path);
            return false;
        }
    }

    public bool Save(CityPlacementSaveData data)
    {
        string path = SaveFilePath;
        string temporaryPath = path + ".tmp";
        string backupPath = path + ".bak";

        if (!TryValidate(data, out string validationError))
        {
            Debug.LogWarning(
                $"CityPlacementPersistence: save failed for '{path}': {validationError}");
            return false;
        }

        try
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            data.rotation = Normalize(data.rotation);
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, true));

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporaryPath, path, backupPath);
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                }
                catch (Exception exception)
                    when (exception is PlatformNotSupportedException || exception is IOException)
                {
                    ReplaceWithCopyFallback(temporaryPath, path);
                }
            }
            else
            {
                File.Move(temporaryPath, path);
            }

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"CityPlacementPersistence: save failed for '{path}': {exception.Message}");
            return false;
        }
        finally
        {
            TryDeleteSilently(temporaryPath);
            TryDeleteSilently(backupPath);
        }
    }

    public bool Delete()
    {
        string path = SaveFilePath;

        try
        {
            bool existed = File.Exists(path);
            if (existed)
                File.Delete(path);

            TryDeleteSilently(path + ".tmp");
            TryDeleteSilently(path + ".bak");

            Debug.Log(existed
                ? $"CityPlacementPersistence: saved placement deleted at '{path}'."
                : $"CityPlacementPersistence: no saved placement existed at '{path}'.");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"CityPlacementPersistence: could not delete '{path}': {exception.Message}");
            return false;
        }
    }

    private static bool TryValidate(CityPlacementSaveData data, out string error)
    {
        if (data == null)
        {
            error = "JSON did not contain placement data.";
            return false;
        }

        if (data.version != CurrentVersion)
        {
            error = $"unsupported version {data.version}; expected {CurrentVersion}.";
            return false;
        }

        if (!data.hasConfirmedPlacement)
        {
            error = "placement confirmation flag is false.";
            return false;
        }

        if (!IsFinite(data.position))
        {
            error = "position contains a non-finite value.";
            return false;
        }

        if (!IsFinite(data.rotation))
        {
            error = "rotation contains a non-finite value.";
            return false;
        }

        float rotationMagnitudeSquared =
            data.rotation.x * data.rotation.x +
            data.rotation.y * data.rotation.y +
            data.rotation.z * data.rotation.z +
            data.rotation.w * data.rotation.w;
        if (rotationMagnitudeSquared < 0.000001f)
        {
            error = "rotation quaternion is near zero.";
            return false;
        }

        if (!IsFinite(data.localScale) ||
            data.localScale.x <= 0f || data.localScale.y <= 0f || data.localScale.z <= 0f)
        {
            error = "scale must contain finite, positive values.";
            return false;
        }

        if (data.usesSpatialAnchor && string.IsNullOrWhiteSpace(data.spatialAnchorName))
        {
            error = "spatial-anchor persistence is enabled but its name is missing.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(Quaternion value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static Quaternion Normalize(Quaternion value)
    {
        float inverseMagnitude = 1f / Mathf.Sqrt(
            value.x * value.x + value.y * value.y +
            value.z * value.z + value.w * value.w);
        return new Quaternion(
            value.x * inverseMagnitude,
            value.y * inverseMagnitude,
            value.z * inverseMagnitude,
            value.w * inverseMagnitude);
    }

    private static void ReplaceWithCopyFallback(string temporaryPath, string path)
    {
        File.Copy(temporaryPath, path, true);
        File.Delete(temporaryPath);
    }

    private static void DeleteInvalidFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.LogWarning(
                    $"CityPlacementPersistence: rejected placement file deleted at '{path}'.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"CityPlacementPersistence: rejected file could not be deleted: {exception.Message}");
        }
    }

    private static void TryDeleteSilently(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup failure is non-fatal; the main operation already logged its result.
        }
    }
}
