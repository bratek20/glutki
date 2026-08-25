using TMPro;
using UnityEngine;

public class ResourceHud : MonoBehaviour
{
    [SerializeField] private TMP_Text text;

    private void Update()
    {
        PlayerBase selectedBase = BaseSelectionManager.SelectedBase;
        int resources = selectedBase != null ? selectedBase.StoredResources : 0;

        // Storage is shown against its limit because the limit is a thing the player controls: it's
        // the sum of their magazines, and Gatherers stop delivering once it's reached.
        int capacity = selectedBase != null ? selectedBase.StorageCapacity : 0;

        // The Queen's food is shown alongside storage because it's the player's only read on why a
        // unit they ordered hasn't started growing yet - a Builder is still carrying it to her.
        int queenFood = selectedBase != null ? selectedBase.QueenFood : 0;
        int cost = selectedBase != null ? selectedBase.SpawnCost : 0;

        text.text = $"Resources: {resources} / {capacity}\nQueen fed: {queenFood}/{cost}";
    }
}
