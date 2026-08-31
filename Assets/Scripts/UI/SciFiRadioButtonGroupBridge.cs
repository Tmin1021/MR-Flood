using System;
using Microsoft.MixedReality.Toolkit.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class SciFiRadioButtonGroupBridge : MonoBehaviour
{
    [Serializable]
    public class IntEvent : UnityEvent<int> { }

    [Serializable]
    public class RadioOption
    {
        public string label;

        [Header("Input")]
        public Interactable mrtkInteractable;
        public Toggle uiToggle;

        [Header("Optional Image Visual")]
        public Image targetImage;
        public Sprite selectedSprite;
        public Sprite unselectedSprite;
        public bool useImageColors;
        public Color selectedImageColor = Color.white;
        public Color unselectedImageColor = Color.white;

        [Header("Optional Text Visual")]
        public Text legacyLabelText;
        public TMP_Text tmpLabelText;
        public bool useTextColors;
        public Color selectedTextColor = Color.white;
        public Color unselectedTextColor = Color.white;

        [Header("Action")]
        public UnityEvent onSelected;
    }

    [Header("Options")]
    [SerializeField] private RadioOption[] options;
    [SerializeField] private int selectedIndex;

    [Header("Unity Toggle")]
    [SerializeField] private bool autoRegisterUnityToggles = true;
    [SerializeField] private bool syncUnityToggleState = true;

    [Header("MRTK")]
    [SerializeField] private bool autoRegisterMrtkClicks = true;
    [SerializeField] private bool syncMrtkToggleState = true;

    [Header("Actions")]
    [SerializeField] private bool invokeOnStart;
    [SerializeField] private bool invokeWhenReselecting;
    public IntEvent onSelectionChanged;

    private UnityAction[] registeredClickActions;
    private UnityAction<bool>[] registeredToggleActions;
    private bool isRefreshingVisuals;

    public int SelectedIndex => selectedIndex;

    private void OnEnable()
    {
        RegisterMrtkClicks();
        RegisterUnityToggles();

        SetSelectedIndexInternal(selectedIndex, invokeOnStart, true);
    }

    private void OnDisable()
    {
        UnregisterMrtkClicks();
        UnregisterUnityToggles();
    }

    public void OnMRTKClick(int optionIndex)
    {
        Select(optionIndex);
    }

    public void Select(int optionIndex)
    {
        SetSelectedIndexInternal(optionIndex, true, false);
    }

    public void SetSelectedIndex(int optionIndex)
    {
        SetSelectedIndexInternal(optionIndex, false, false);
    }

    public void SelectOption0() => Select(0);
    public void SelectOption1() => Select(1);
    public void SelectOption2() => Select(2);
    public void SelectOption3() => Select(3);
    public void SelectOption4() => Select(4);
    public void SelectOption5() => Select(5);
    public void SelectOption6() => Select(6);
    public void SelectOption7() => Select(7);

    public void SetInteractable(bool value)
    {
        if (options == null)
            return;

        for (int i = 0; i < options.Length; i++)
        {
            RadioOption option = options[i];

            if (option == null)
                continue;

            if (option.mrtkInteractable != null)
                option.mrtkInteractable.IsEnabled = value;

            if (option.uiToggle != null)
                option.uiToggle.interactable = value;
        }
    }

    private void RegisterMrtkClicks()
    {
        UnregisterMrtkClicks();

        if (!autoRegisterMrtkClicks || options == null)
            return;

        registeredClickActions = new UnityAction[options.Length];

        for (int i = 0; i < options.Length; i++)
        {
            RadioOption option = options[i];

            if (option == null || option.mrtkInteractable == null)
                continue;

            int capturedIndex = i;

            registeredClickActions[i] = () => Select(capturedIndex);
            option.mrtkInteractable.OnClick.AddListener(registeredClickActions[i]);

            option.mrtkInteractable.CanDeselect = false;
        }
    }

    private void UnregisterMrtkClicks()
    {
        if (registeredClickActions == null || options == null)
            return;

        for (int i = 0; i < registeredClickActions.Length && i < options.Length; i++)
        {
            if (registeredClickActions[i] == null || options[i]?.mrtkInteractable == null)
                continue;

            options[i].mrtkInteractable.OnClick.RemoveListener(registeredClickActions[i]);
        }

        registeredClickActions = null;
    }

    private void RegisterUnityToggles()
    {
        UnregisterUnityToggles();

        if (!autoRegisterUnityToggles || options == null)
            return;

        registeredToggleActions = new UnityAction<bool>[options.Length];

        for (int i = 0; i < options.Length; i++)
        {
            RadioOption option = options[i];

            if (option == null || option.uiToggle == null)
                continue;

            int capturedIndex = i;

            registeredToggleActions[i] = isOn => OnUnityToggleChanged(capturedIndex, isOn);
            option.uiToggle.onValueChanged.AddListener(registeredToggleActions[i]);
        }
    }

    private void UnregisterUnityToggles()
    {
        if (registeredToggleActions == null || options == null)
            return;

        for (int i = 0; i < registeredToggleActions.Length && i < options.Length; i++)
        {
            if (registeredToggleActions[i] == null || options[i]?.uiToggle == null)
                continue;

            options[i].uiToggle.onValueChanged.RemoveListener(registeredToggleActions[i]);
        }

        registeredToggleActions = null;
    }

    private void OnUnityToggleChanged(int optionIndex, bool isOn)
    {
        if (isRefreshingVisuals)
            return;

        if (!IsValidIndex(optionIndex))
            return;

        if (isOn)
        {
            Select(optionIndex);
            return;
        }

        // Radio button behavior:
        // The selected option should not be allowed to turn itself off.
        if (optionIndex == selectedIndex)
        {
            RadioOption option = options[optionIndex];

            if (option != null && option.uiToggle != null)
                option.uiToggle.SetIsOnWithoutNotify(true);
        }
    }

    private void SetSelectedIndexInternal(int optionIndex, bool notify, bool force)
    {
        if (!IsValidIndex(optionIndex))
        {
            Debug.LogWarning($"{nameof(SciFiRadioButtonGroupBridge)} on {name}: option index {optionIndex} is out of range.");
            return;
        }

        if (!force && selectedIndex == optionIndex)
        {
            RefreshVisuals();

            if (notify && invokeWhenReselecting)
                NotifySelection();

            return;
        }

        selectedIndex = optionIndex;
        RefreshVisuals();

        if (notify)
            NotifySelection();
    }

    private bool IsValidIndex(int optionIndex)
    {
        return options != null && optionIndex >= 0 && optionIndex < options.Length;
    }

    private void RefreshVisuals()
    {
        if (options == null)
            return;

        isRefreshingVisuals = true;

        try
        {
            for (int i = 0; i < options.Length; i++)
                RefreshOption(options[i], i == selectedIndex);
        }
        finally
        {
            isRefreshingVisuals = false;
        }
    }

    private void RefreshOption(RadioOption option, bool selected)
    {
        if (option == null)
            return;

        // Let Unity Toggle control the checkmark through its own Graphic field.
        // Do not manually SetActive the checkmark object.
        if (syncUnityToggleState && option.uiToggle != null)
            option.uiToggle.SetIsOnWithoutNotify(selected);

        if (option.targetImage != null)
        {
            Sprite sprite = selected ? option.selectedSprite : option.unselectedSprite;

            if (sprite != null)
                option.targetImage.sprite = sprite;

            if (option.useImageColors)
                option.targetImage.color = selected ? option.selectedImageColor : option.unselectedImageColor;
        }

        if (option.useTextColors)
        {
            Color color = selected ? option.selectedTextColor : option.unselectedTextColor;

            if (option.legacyLabelText != null)
                option.legacyLabelText.color = color;

            if (option.tmpLabelText != null)
                option.tmpLabelText.color = color;
        }

        if (syncMrtkToggleState &&
            option.mrtkInteractable != null &&
            option.mrtkInteractable.NumOfDimensions == 2)
        {
            option.mrtkInteractable.IsToggled = selected;
        }
    }

    private void NotifySelection()
    {
        onSelectionChanged?.Invoke(selectedIndex);
        options[selectedIndex]?.onSelected?.Invoke();
    }
}