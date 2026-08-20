using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SpawnBuilderButton : MonoBehaviour
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

        // Same as SpawnUnitButton - ordering is free, the food cost is paid on the Queen's side.
        button.interactable = canManage && selectedBase.IsQueenAlive;
        label.text = canManage ? $"Spawn Builder ({cost} food)" : "Not your base";
    }

    private void OnClicked()
    {
        BaseSelectionManager.SelectedBase?.CmdRequestSpawnBuilder();
    }
}
