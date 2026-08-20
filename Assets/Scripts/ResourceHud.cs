using TMPro;
using UnityEngine;

public class ResourceHud : MonoBehaviour
{
    [SerializeField] private TMP_Text text;

    private void Update()
    {
        PlayerBase selectedBase = BaseSelectionManager.SelectedBase;
        int resources = selectedBase != null ? selectedBase.StoredResources : 0;

        // The Queen's food is shown alongside storage because it's the player's only read on why a
        // unit they ordered hasn't started growing yet - a Builder is still carrying it to her.
        int queenFood = selectedBase != null ? selectedBase.QueenFood : 0;
        int cost = selectedBase != null ? selectedBase.SpawnCost : 0;

        text.text = $"Resources: {resources}\nQueen fed: {queenFood}/{cost}";
    }
}
