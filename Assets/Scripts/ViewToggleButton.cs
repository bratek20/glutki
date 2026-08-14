using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ViewToggleButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;

    private void Awake()
    {
        button.onClick.AddListener(OnClicked);
    }

    private void Update()
    {
        bool inBaseView = ViewManager.CurrentView == ViewMode.Base;
        button.interactable = inBaseView || BaseSelectionManager.SelectedBase != null;
        label.text = inBaseView ? "World View" : "Enter Base";
    }

    private void OnClicked()
    {
        if (ViewManager.CurrentView == ViewMode.Base)
        {
            ViewManager.EnterWorldView();
        }
        else
        {
            ViewManager.EnterBaseView(BaseSelectionManager.SelectedBase);
        }
    }
}
