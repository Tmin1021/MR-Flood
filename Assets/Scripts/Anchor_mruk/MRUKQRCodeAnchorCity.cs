// using Meta.XR.MRUtilityKit;
// using UnityEngine;

// public class MRUKQRCodeCityAnchor : MonoBehaviour
// {
//     [Header("QR Filter")]
//     public string requiredPayload = "FLOOD_CITY_ANCHOR_01";
//     public bool lockAfterFirstValidQR = true;

//     [Header("City Alignment")]
//     public Transform cityRoot;
//     public Transform cityLocalQrPose;

//     [Tooltip("Extra offset from the detected QR pose, in world meters.")]
//     public Vector3 worldPositionOffset;

//     [Tooltip("Extra rotation applied to the QR pose before aligning the city.")]
//     public Vector3 qrRotationOffsetEuler;

//     [Tooltip("Use this if the QR is lying flat and you only want horizontal map rotation.")]
//     public bool yawOnly = false;

//     [Header("Pose Stabilization")]
//     [Min(1)]
//     public int stableFramesRequired = 8;

//     [Min(0f)]
//     public float positionStabilityThreshold = 0.015f;

//     [Min(0f)]
//     public float rotationStabilityThresholdDegrees = 2f;

//     [Tooltip("Move the city while the QR pose is settling, then lock once stable.")]
//     public bool previewAlignmentWhileStabilizing = true;

//     [Header("Refresh Existing Project Systems")]
//     public CityBootstrapper cityBootstrapper;
//     public SimpleGraphManager graphManager;
//     public FloodManager floodManager;
//     public PointSelectManager pointSelectManager;

//     [Tooltip("Optional: disable route/flood interaction scripts at startup, then enable them after QR anchoring.")]
//     public MonoBehaviour[] enableAfterAnchor;

//     private bool anchored;
//     private MRUKTrackable activeQrTrackable;
//     private string activePayload;
//     private bool hasPoseSample;
//     private Vector3 lastObservedQrPosition;
//     private Quaternion lastObservedQrRotation = Quaternion.identity;
//     private int consecutiveStableFrames;
//     private bool warnedAboutManualPlacementOverride;

//     private void Awake()
//     {
//         ResolveMissingReferences();
//     }

//     public void OnTrackableAdded(MRUKTrackable trackable)
//     {
//         if (IsDisabledByManualPlacementFlow())
//             return;

//         if (anchored && lockAfterFirstValidQR)
//             return;

//         if (!TrySetActiveTrackable(trackable))
//             return;

//         ResetPoseStabilization();
//     }

//     public void OnTrackableRemoved(MRUKTrackable trackable)
//     {
//         if (IsDisabledByManualPlacementFlow())
//             return;

//         if (activeQrTrackable != trackable)
//             return;

//         activeQrTrackable = null;
//         activePayload = null;
//         ResetPoseStabilization();

//         if (!lockAfterFirstValidQR)
//             anchored = false;
//     }

//     private void LateUpdate()
//     {
//         if (IsDisabledByManualPlacementFlow())
//             return;

//         if (activeQrTrackable == null)
//             return;

//         if (anchored)
//         {
//             if (!lockAfterFirstValidQR)
//                 ApplyAnchor(activeQrTrackable.transform);
//             return;
//         }

//         bool poseLocked = UpdatePoseStabilization(activeQrTrackable.transform);

//         if (previewAlignmentWhileStabilizing || poseLocked)
//             ApplyAnchor(activeQrTrackable.transform);

//         if (!poseLocked)
//             return;

//         ApplyAnchor(activeQrTrackable.transform);
//         anchored = true;
//         RefreshProjectAfterAnchor();

//         Debug.Log($"City anchored to QR: {activePayload}");

//         if (lockAfterFirstValidQR)
//         {
//             activeQrTrackable = null;
//             activePayload = null;
//         }
//     }

//     private void ApplyAnchor(Transform qrTransform)
//     {
//         ResolveMissingReferences();

//         if (cityRoot == null || cityLocalQrPose == null || qrTransform == null)
//         {
//             Debug.LogWarning("QR anchor setup is missing cityRoot, cityLocalQrPose, or QR transform.");
//             return;
//         }

//         Quaternion qrRotation = GetAdjustedQrRotation(qrTransform);
//         Vector3 qrPosition = GetAdjustedQrPosition(qrTransform);
//         Quaternion cityRootRotation =
//             qrRotation * Quaternion.Inverse(cityLocalQrPose.localRotation);

//         Vector3 scaledLocalAnchorPosition =
//             Vector3.Scale(cityLocalQrPose.localPosition, cityRoot.localScale);

//         Vector3 cityRootPosition =
//             qrPosition
//             - cityRootRotation * scaledLocalAnchorPosition;

//         cityRoot.SetPositionAndRotation(cityRootPosition, cityRootRotation);
//     }

//     private void RefreshProjectAfterAnchor()
//     {
//         ResolveMissingReferences();

//         if (cityBootstrapper != null)
//             cityBootstrapper.BuildCity();

//         if (graphManager != null)
//             graphManager.BuildGraphFromNeighbors();

//         if (floodManager != null)
//             floodManager.UpdateFloodState();

//         if (pointSelectManager != null)
//             pointSelectManager.RefreshAfterCityRebuild();

//         if (enableAfterAnchor != null)
//         {
//             foreach (var behaviour in enableAfterAnchor)
//             {
//                 if (behaviour != null)
//                     behaviour.enabled = true;
//             }
//         }
//     }

//     private bool TrySetActiveTrackable(MRUKTrackable trackable)
//     {
//         if (trackable == null)
//             return false;

//         if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
//             return false;

//         string payload = trackable.MarkerPayloadString;

//         if (!string.IsNullOrWhiteSpace(requiredPayload) && payload != requiredPayload)
//         {
//             Debug.Log($"Ignored QR payload: {payload}");
//             return false;
//         }

//         activeQrTrackable = trackable;
//         activePayload = payload;
//         return true;
//     }

//     private void ResetPoseStabilization()
//     {
//         hasPoseSample = false;
//         consecutiveStableFrames = 0;
//         lastObservedQrPosition = Vector3.zero;
//         lastObservedQrRotation = Quaternion.identity;
//     }

//     private bool UpdatePoseStabilization(Transform qrTransform)
//     {
//         Vector3 qrPosition = GetAdjustedQrPosition(qrTransform);
//         Quaternion qrRotation = GetAdjustedQrRotation(qrTransform);

//         if (!hasPoseSample)
//         {
//             hasPoseSample = true;
//             consecutiveStableFrames = 1;
//             lastObservedQrPosition = qrPosition;
//             lastObservedQrRotation = qrRotation;
//             return stableFramesRequired <= 1;
//         }

//         float positionDelta = Vector3.Distance(lastObservedQrPosition, qrPosition);
//         float rotationDelta = Quaternion.Angle(lastObservedQrRotation, qrRotation);
//         bool stableThisFrame =
//             positionDelta <= positionStabilityThreshold &&
//             rotationDelta <= rotationStabilityThresholdDegrees;

//         consecutiveStableFrames = stableThisFrame ? consecutiveStableFrames + 1 : 1;
//         lastObservedQrPosition = qrPosition;
//         lastObservedQrRotation = qrRotation;

//         return consecutiveStableFrames >= stableFramesRequired;
//     }

//     private Vector3 GetAdjustedQrPosition(Transform qrTransform)
//     {
//         return qrTransform.position + worldPositionOffset;
//     }

//     private Quaternion GetAdjustedQrRotation(Transform qrTransform)
//     {
//         Quaternion qrRotation =
//             qrTransform.rotation * Quaternion.Euler(qrRotationOffsetEuler);

//         if (yawOnly)
//             qrRotation = Quaternion.Euler(0f, qrRotation.eulerAngles.y, 0f);

//         return qrRotation;
//     }

//     private void ResolveMissingReferences()
//     {
//         cityBootstrapper ??= FindFirstObjectByType<CityBootstrapper>(FindObjectsInactive.Include);
//         graphManager ??= FindFirstObjectByType<SimpleGraphManager>(FindObjectsInactive.Include);
//         floodManager ??= FindFirstObjectByType<FloodManager>(FindObjectsInactive.Include);
//         pointSelectManager ??= FindFirstObjectByType<PointSelectManager>(FindObjectsInactive.Include);

//         if (cityRoot == null && pointSelectManager != null)
//             cityRoot = pointSelectManager.cityRoot;

//         if (cityRoot == null && cityBootstrapper != null && cityBootstrapper.buildingsRoot != null)
//             cityRoot = cityBootstrapper.buildingsRoot.root;

//         if (cityLocalQrPose == null && cityRoot != null)
//         {
//             cityLocalQrPose = cityRoot.Find("CityAnchorPoint");

//             if (cityLocalQrPose == null)
//                 cityLocalQrPose = cityRoot.Find("QR_VirtualReferencePoint");
//         }
//     }

//     private bool IsDisabledByManualPlacementFlow()
//     {
//         if (!CityAnchorManager.IsManualPlacementFlowActive)
//             return false;

//         if (!warnedAboutManualPlacementOverride)
//         {
//             warnedAboutManualPlacementOverride = true;
//             Debug.Log("MRUKQRCodeCityAnchor: ignored because manual plane placement flow is active.");
//         }

//         return true;
//     }
// }
