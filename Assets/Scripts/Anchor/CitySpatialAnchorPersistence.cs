using System;
using System.Threading.Tasks;
using Microsoft.MixedReality.OpenXR;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[DisallowMultipleComponent]
public sealed class CitySpatialAnchorPersistence : MonoBehaviour
{
    [SerializeField] private float initializationTimeoutSeconds = 15f;
    [SerializeField] private float localizationTimeoutSeconds = 15f;

    public bool IsSupportedRuntime =>
        Application.platform == RuntimePlatform.WSAPlayerX86 ||
        Application.platform == RuntimePlatform.WSAPlayerX64 ||
        Application.platform == RuntimePlatform.WSAPlayerARM;

    public bool HasActiveAnchor => activeAnchor != null;
    public Pose ActiveAnchorPose => activeAnchor != null
        ? new Pose(activeAnchor.transform.position, activeAnchor.transform.rotation)
        : Pose.identity;

    private ARAnchorManager anchorManager;
    private XRAnchorStore anchorStore;
    private ARAnchor activeAnchor;

    public async Task<bool> TryRestoreAsync(string anchorName)
    {
        if (!IsSupportedRuntime || string.IsNullOrWhiteSpace(anchorName))
            return false;

        try
        {
            if (!await EnsureAnchorStoreReadyAsync())
                return false;

            if (!ContainsPersistedAnchor(anchorName))
            {
                Debug.LogWarning(
                    $"CitySpatialAnchorPersistence: persisted anchor '{anchorName}' was not found.");
                return false;
            }

            TrackableId trackableId = anchorStore.LoadAnchor(anchorName);
            if (trackableId == TrackableId.invalidId)
            {
                Debug.LogWarning(
                    $"CitySpatialAnchorPersistence: loading anchor '{anchorName}' returned an invalid id.");
                return false;
            }

            float deadline = Time.realtimeSinceStartup + localizationTimeoutSeconds;
            int locatedFrames = 0;
            while (Time.realtimeSinceStartup < deadline)
            {
                ARAnchor candidate = anchorManager.GetAnchor(trackableId);
                if (candidate != null &&
                    !candidate.pending &&
                    candidate.trackingState != TrackingState.None)
                {
                    locatedFrames++;
                    if (locatedFrames >= 3)
                    {
                        activeAnchor = candidate;
                        Debug.Log(
                            $"CitySpatialAnchorPersistence: anchor '{anchorName}' localized at " +
                            $"{candidate.transform.position}.");
                        return true;
                    }
                }
                else
                {
                    locatedFrames = 0;
                }

                await Task.Yield();
            }

            Debug.LogWarning(
                $"CitySpatialAnchorPersistence: timed out localizing anchor '{anchorName}'.");
            ARAnchor unresolvedAnchor = anchorManager.GetAnchor(trackableId);
            if (unresolvedAnchor != null)
                Destroy(unresolvedAnchor.gameObject);
            return false;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"CitySpatialAnchorPersistence: restore failed for '{anchorName}': {exception.Message}");
            return false;
        }
    }

    public async Task<bool> TryPersistAsync(string anchorName, Pose pose)
    {
        if (!IsSupportedRuntime || string.IsNullOrWhiteSpace(anchorName))
            return false;

        try
        {
            if (!await EnsureAnchorStoreReadyAsync())
                return false;

            if (ContainsPersistedAnchor(anchorName))
                anchorStore.UnpersistAnchor(anchorName);

            ReleaseActiveAnchor();

            GameObject anchorObject = new GameObject("City Persistent Spatial Anchor");
            anchorObject.SetActive(false);
            anchorObject.transform.SetPositionAndRotation(pose.position, pose.rotation);
            ARAnchor candidate = anchorObject.AddComponent<ARAnchor>();
            anchorObject.SetActive(true);

            float deadline = Time.realtimeSinceStartup + localizationTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (candidate != null &&
                    !candidate.pending &&
                    candidate.trackableId != TrackableId.invalidId &&
                    candidate.trackingState != TrackingState.None)
                {
                    if (!anchorStore.TryPersistAnchor(candidate.trackableId, anchorName))
                    {
                        Debug.LogWarning(
                            $"CitySpatialAnchorPersistence: device store rejected anchor '{anchorName}'.");
                        Destroy(anchorObject);
                        return false;
                    }

                    activeAnchor = candidate;
                    Debug.Log(
                        $"CitySpatialAnchorPersistence: anchor '{anchorName}' persisted successfully.");
                    return true;
                }

                await Task.Yield();
            }

            if (anchorObject != null)
                Destroy(anchorObject);

            Debug.LogWarning(
                $"CitySpatialAnchorPersistence: timed out creating anchor '{anchorName}'.");
            return false;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"CitySpatialAnchorPersistence: persist failed for '{anchorName}': {exception.Message}");
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string anchorName)
    {
        ReleaseActiveAnchor();

        if (!IsSupportedRuntime || string.IsNullOrWhiteSpace(anchorName))
            return true;

        try
        {
            if (!await EnsureAnchorStoreReadyAsync())
                return false;

            if (ContainsPersistedAnchor(anchorName))
                anchorStore.UnpersistAnchor(anchorName);

            Debug.Log(
                $"CitySpatialAnchorPersistence: persisted anchor '{anchorName}' deleted.");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"CitySpatialAnchorPersistence: could not delete anchor '{anchorName}': {exception.Message}");
            return false;
        }
    }

    public void ReleaseActiveAnchor()
    {
        if (activeAnchor == null)
            return;

        Destroy(activeAnchor.gameObject);
        activeAnchor = null;
    }

    private async Task<bool> EnsureAnchorStoreReadyAsync()
    {
        if (anchorStore != null)
            return true;

        EnsureAnchorManager();
        if (anchorManager == null)
            return false;

        float deadline = Time.realtimeSinceStartup + initializationTimeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (anchorManager.subsystem != null && anchorManager.subsystem.running)
            {
                anchorStore = await XRAnchorStore.LoadAnchorStoreAsync(anchorManager.subsystem);
                if (anchorStore != null)
                    return true;
            }

            await Task.Yield();
        }

        Debug.LogWarning(
            "CitySpatialAnchorPersistence: OpenXR anchor subsystem/store did not become ready. " +
            "Verify the Microsoft HoloLens OpenXR feature group is enabled.");
        return false;
    }

    private void EnsureAnchorManager()
    {
        if (anchorManager != null)
            return;

        anchorManager = FindFirstObjectByType<ARAnchorManager>(FindObjectsInactive.Include);
        if (anchorManager != null)
        {
            anchorManager.enabled = true;
            return;
        }

        GameObject runtimeRoot = new GameObject("City Spatial Anchor Runtime");
        runtimeRoot.SetActive(false);

        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.transform.parent != null)
            runtimeRoot.transform.SetParent(mainCamera.transform.parent, false);

        XROrigin xrOrigin = runtimeRoot.AddComponent<XROrigin>();
        xrOrigin.Origin = runtimeRoot;
        xrOrigin.CameraFloorOffsetObject = runtimeRoot;
        xrOrigin.Camera = mainCamera;

        anchorManager = runtimeRoot.AddComponent<ARAnchorManager>();
        runtimeRoot.SetActive(true);

        // ARAnchorManager needs XROrigin for world/session pose conversion. MRTK already
        // owns the camera playspace, so disable XROrigin's Start/update lifecycle to prevent
        // it from recentering or otherwise competing with MRTK for tracking-origin control.
        xrOrigin.enabled = false;
    }

    private bool ContainsPersistedAnchor(string anchorName)
    {
        if (anchorStore == null)
            return false;

        for (int i = 0; i < anchorStore.PersistedAnchorNames.Count; i++)
        {
            if (string.Equals(
                    anchorStore.PersistedAnchorNames[i],
                    anchorName,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
