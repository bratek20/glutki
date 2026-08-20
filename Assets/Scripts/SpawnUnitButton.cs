using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SpawnUnitButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;

    private void Awake()
    {
        button.onClick.AddListener(OnClicked);
    }

    private void Update()
    {
        PlayerBase selectedBase = BaseSelectionManager.SelectedBase;
        bool canManage = selectedBase != null && selectedBase.IsOwnedByLocalPlayer;
        int cost = selectedBase != null ? selectedBase.SpawnCost : 1;
        int resources = selectedBase != null ? selectedBase.StoredResources : 0;

        button.interactable = canManage && resources >= cost;
        label.text = canManage ? $"Spawn Gatherer ({cost})" : "Not your base";
    }

    private void OnClicked()
    {
        BaseSelectionManager.SelectedBase?.CmdRequestSpawn();
    }
}
