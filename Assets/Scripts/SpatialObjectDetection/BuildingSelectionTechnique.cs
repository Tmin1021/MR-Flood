using System;
using UnityEngine;

public enum BuildingSelectionTechnique
{
    Direct,
    AssistedLens
}

public enum BuildingSelectionSource
{
    NormalCity,
    Lens,
    DirectPhysical
}

public readonly struct BuildingSelectionChangedEvent
{
    public CityBuilding Building { get; }
    public BuildingSelectionSource Source { get; }
    public int SelectionCount { get; }
    public bool IsCorrection { get; }

    public BuildingSelectionChangedEvent(
        CityBuilding building,
        BuildingSelectionSource source,
        int selectionCount,
        bool isCorrection)
    {
        Building = building;
        Source = source;
        SelectionCount = Mathf.Clamp(selectionCount, 0, 2);
        IsCorrection = isCorrection;
    }
}

[Serializable]
public sealed class BuildingSelectionTrialRecord
{
    public string trialId;
    public BuildingSelectionTechnique technique;
    public double trialStartTime;
    public double firstBuildingSelectionTime = -1d;
    public double secondBuildingSelectionTime = -1d;
    public double confirmationTime = -1d;
    public string firstBuildingId;
    public string secondBuildingId;
    public BuildingSelectionSource firstSelectionSource;
    public BuildingSelectionSource secondSelectionSource;
    public int selectionCorrections;
    public float magnificationFactor;
    public Vector3 latestPhysicalFocusWorld;
    public Vector3 latestLensFocusLocal;
    public Vector3[] directPhysicalCandidatePositions = Array.Empty<Vector3>();
}
