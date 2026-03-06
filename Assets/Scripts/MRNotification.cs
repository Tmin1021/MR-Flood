using System.Collections;
using TMPro;
using UnityEngine;

public class MRNotification : MonoBehaviour
{
    public TMP_Text text;
    public float showSeconds = 2f;

    Coroutine routine;

    public void Show(string message)
    {
        if (text == null) return;
        text.text = message;
        text.gameObject.SetActive(true);

        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(HideLater());
    }

    IEnumerator HideLater()
    {
        yield return new WaitForSeconds(showSeconds);
        if (text != null) text.gameObject.SetActive(false);
        routine = null;
    }
}