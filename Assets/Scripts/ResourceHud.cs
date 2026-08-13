using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Canvas))]
public class ResourceHud : MonoBehaviour
{
    [SerializeField] private int fontSize = 24;
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Vector2 padding = new Vector2(16f, 16f);

    private Text hudText;

    private void Awake()
    {
        hudText = BuildHudText();
    }

    private void Update()
    {
        Base selectedBase = BaseSelectionManager.SelectedBase;
        int resources = selectedBase != null ? selectedBase.StoredResources : 0;
        hudText.text = $"Resources: {resources}\nUnits: {UnitController.ActiveUnitCount}";
    }

    private Text BuildHudText()
    {
        GameObject textObject = new GameObject("ResourceHudText", typeof(RectTransform));
        textObject.transform.SetParent(transform, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(padding.x, -padding.y);
        rect.sizeDelta = new Vector2(300f, 80f);

        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.color = textColor;
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        return text;
    }
}
