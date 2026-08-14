using TMPro;
using UnityEngine;

public class ResourceHud : MonoBehaviour
{
    [SerializeField] private TMP_Text text;

    private void Update()
    {
        PlayerBase selectedBase = BaseSelectionManager.SelectedBase;
        int resources = selectedBase != null ? selectedBase.StoredResources : 0;
        text.text = $"Resources: {resources}";
    }
}
