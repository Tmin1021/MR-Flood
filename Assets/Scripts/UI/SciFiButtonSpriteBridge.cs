using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class SciFiButtonSpriteBridge : MonoBehaviour
{
    [Header("SCIFI Button Reference")]
    public Button sciFiButton;
    public Image targetImage;

    [Header("Optional Manual Sprites")]
    public Sprite normalSprite;
    public Sprite highlightedSprite;
    public Sprite pressedSprite;
    public Sprite disabledSprite;

    [Header("Timing")]
    public float pressedTime = 0.12f;
    public float actionDelay = 0.03f;

    [Header("Action")]
    public UnityEvent onClickAction;

    private Coroutine routine;
    private bool isInteractable = true;

    private void Awake()
    {
        AutoFillFromUnityButton();
        SetNormal();
    }

    private void AutoFillFromUnityButton()
    {
        if (sciFiButton == null)
            return;

        if (targetImage == null)
            targetImage = sciFiButton.targetGraphic as Image;

        if (targetImage != null && normalSprite == null)
            normalSprite = targetImage.sprite;

        SpriteState state = sciFiButton.spriteState;

        if (highlightedSprite == null)
            highlightedSprite = state.highlightedSprite;

        if (pressedSprite == null)
            pressedSprite = state.pressedSprite;

        if (disabledSprite == null)
            disabledSprite = state.disabledSprite;
    }

    public void OnMRTKClick()
    {
        if (!isInteractable)
            return;

        if (routine != null)
            StopCoroutine(routine);

        routine = StartCoroutine(ClickRoutine());
    }

    public void OnMRTKFocusEnter()
    {
        if (!isInteractable)
            return;

        SetSprite(highlightedSprite != null ? highlightedSprite : normalSprite);
    }

    public void OnMRTKFocusExit()
    {
        if (!isInteractable)
            return;

        SetNormal();
    }

    public void SetInteractable(bool value)
    {
        isInteractable = value;

        if (sciFiButton != null)
            sciFiButton.interactable = value;

        Microsoft.MixedReality.Toolkit.UI.Interactable mrtkInteractable =
            GetComponentInParent<Microsoft.MixedReality.Toolkit.UI.Interactable>();
        if (mrtkInteractable != null)
            mrtkInteractable.IsEnabled = value;

        if (isInteractable)
            SetNormal();
        else
            SetSprite(disabledSprite);
    }

    private IEnumerator ClickRoutine()
    {
        SetSprite(pressedSprite != null ? pressedSprite : highlightedSprite);

        yield return new WaitForSeconds(pressedTime);

        SetNormal();

        yield return new WaitForSeconds(actionDelay);

        onClickAction?.Invoke();

        routine = null;
    }

    private void SetNormal()
    {
        SetSprite(normalSprite);
    }

    private void SetSprite(Sprite sprite)
    {
        if (targetImage == null || sprite == null)
            return;

        targetImage.sprite = sprite;
    }
}
