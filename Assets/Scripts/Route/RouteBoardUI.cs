using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RouteBoardUI : MonoBehaviour
{
    [Header("Optional Targets")]
    public Text legacyText;
    public TMP_Text tmpText;

    [TextArea(3, 20)]
    public string emptyMessage = "Select a start building and a destination building.";

    private void Start()
    {
        Clear();
    }

    public void ShowRoute(Route route, CityBuilding startBuilding, CityBuilding destinationBuilding)
    {
        string startName = startBuilding != null
            ? (string.IsNullOrWhiteSpace(startBuilding.displayName) ? startBuilding.id : startBuilding.displayName)
            : "Unknown";

        string destinationName = destinationBuilding != null
            ? (string.IsNullOrWhiteSpace(destinationBuilding.displayName) ? destinationBuilding.id : destinationBuilding.displayName)
            : "Unknown";

        SetText(RouteInstructionBuilder.BuildBoardText(route, startName, destinationName));
    }

    public void ShowMessage(string message)
    {
        SetText(string.IsNullOrWhiteSpace(message) ? emptyMessage : message);
    }

    public void Clear()
    {
        SetText(emptyMessage);
    }

    private void SetText(string value)
    {
        if (legacyText != null)
            legacyText.text = value;

        if (tmpText != null)
            tmpText.text = value;
    }
}