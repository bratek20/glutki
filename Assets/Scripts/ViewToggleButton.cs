using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Canvas))]
public class ViewToggleButton : MonoBehaviour
{
    [SerializeField] private Vector2 padding = new Vector2(16f, 16f);
    [SerializeField] private Vector2 buttonSize = new Vector2(160f, 48f);
    [SerializeField] private Color normalColor = new Color(0.2f, 0.4f, 0.7f);
    [SerializeField] private Color highlightedColor = new Color(0.28f, 0.5f, 0.8f);
    [SerializeField] private Color pressedColor = new Color(0.15f, 0.3f, 0.55f);
    [SerializeField] private Color disabledColor = new Color(0.4f, 0.4f, 0.4f);

    private Button button;
    private Text label;

    private void Awake()
    {
        BuildButton();
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

    private void BuildButton()
    {
        GameObject buttonObject = new GameObject("ViewToggleButton", typeof(RectTransform));
        buttonObject.transform.SetParent(transform, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-padding.x, padding.y);
        rect.sizeDelta = buttonSize;

        Image image = buttonObject.AddComponent<Image>();

        button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = normalColor;
        colors.highlightedColor = highlightedColor;
        colors.pressedColor = pressedColor;
        colors.selectedColor = normalColor;
        colors.disabledColor = disabledColor;
        button.colors = colors;
        button.onClick.AddListener(OnClicked);

        // Don't leave the button stuck in the keyboard/gamepad-navigation "selected" state after a click.
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        GameObject textObject = new GameObject("Label", typeof(RectTransform));
        textObject.transform.SetParent(buttonObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        label = textObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 18;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
    }
}
