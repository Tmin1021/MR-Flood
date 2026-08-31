using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class LoadingBarAction : MonoBehaviour
{
    [SerializeField] private Slider loadingSlider;
    [Header("Action")]
    public UnityEvent onClickAction;
    private bool hasInvoked = false;

    void Start()
    {
        loadingSlider= GetComponent<Slider>();
    }

    void Update()
    {
        DoActions();
    }

    private void DoActions()
    {
        if(loadingSlider == null) return;
        if(hasInvoked == true) return;
        
        if(loadingSlider.value >= loadingSlider.maxValue)
        {
            hasInvoked = true;
            onClickAction?.Invoke();
        }
    }
}
