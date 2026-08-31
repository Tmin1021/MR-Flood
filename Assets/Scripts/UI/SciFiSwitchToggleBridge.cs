using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

public class SciFiSwitchToggleBridge : MonoBehaviour
{
    [Header("State")]
    public bool isOn = true;

    [Header("SCIFI Visual Objects")]
    public GameObject checkmarkObject;
    public Image backgroundImage;
    [SerializeField, Min(0.01f)] private float checkmarkTransitionDuration = 0.12f;
    [SerializeField, Range(0f, 1f)] private float offCheckmarkScale = 0.7f;

    [Header("Optional Text")]
    public Text legacyStateText;
    public TMP_Text tmpStateText;
    public string onText = "ON";
    public string offText = "OFF";

    [Header("Actions")]
    public UnityEvent onTurnOn;
    public UnityEvent onTurnOff;

    private Coroutine checkmarkTransition;
    private RectTransform checkmarkRect;
    private CanvasGroup checkmarkCanvasGroup;
    private Vector3 checkmarkBaseScale = Vector3.one;

    private void Start()
    {
        CacheCheckmarkVisual();
        RefreshVisual(false);
    }

    public void OnMRTKClick()
    {
        isOn = !isOn;
        RefreshVisual(true);

        if (isOn)
            onTurnOn?.Invoke();
        else
            onTurnOff?.Invoke();
    }

    public void SetState(bool value)
    {
        isOn = value;
        RefreshVisual(false);
    }

    private void RefreshVisual(bool animate)
    {
        CacheCheckmarkVisual();
        if (checkmarkObject != null)
        {
            if (checkmarkTransition != null)
                StopCoroutine(checkmarkTransition);

            if (!animate || !Application.isPlaying)
            {
                checkmarkObject.SetActive(isOn);
                SetCheckmarkVisual(isOn ? 1f : 0f);
            }
            else
            {
                checkmarkTransition = StartCoroutine(AnimateCheckmark(isOn));
            }
        }

        if (legacyStateText != null)
            legacyStateText.text = isOn ? onText : offText;

        if (tmpStateText != null)
            tmpStateText.text = isOn ? onText : offText;
    }

    private void CacheCheckmarkVisual()
    {
        if (checkmarkObject == null || checkmarkRect != null)
            return;

        checkmarkRect = checkmarkObject.GetComponent<RectTransform>();
        if (checkmarkRect != null)
            checkmarkBaseScale = checkmarkRect.localScale;

        checkmarkCanvasGroup = checkmarkObject.GetComponent<CanvasGroup>();
        if (checkmarkCanvasGroup == null)
            checkmarkCanvasGroup = checkmarkObject.AddComponent<CanvasGroup>();
    }

    private System.Collections.IEnumerator AnimateCheckmark(bool show)
    {
        if (show)
            checkmarkObject.SetActive(true);

        float start = checkmarkCanvasGroup != null ? checkmarkCanvasGroup.alpha : (show ? 0f : 1f);
        float end = show ? 1f : 0f;
        float elapsed = 0f;
        while (elapsed < checkmarkTransitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / checkmarkTransitionDuration);
            SetCheckmarkVisual(Mathf.Lerp(start, end, t));
            yield return null;
        }

        SetCheckmarkVisual(end);
        if (!show && checkmarkObject != null)
            checkmarkObject.SetActive(false);
        checkmarkTransition = null;
    }

    private void SetCheckmarkVisual(float visibility)
    {
        if (checkmarkCanvasGroup != null)
            checkmarkCanvasGroup.alpha = visibility;

        if (checkmarkRect != null)
            checkmarkRect.localScale = checkmarkBaseScale * Mathf.Lerp(offCheckmarkScale, 1f, visibility);
    }
}
