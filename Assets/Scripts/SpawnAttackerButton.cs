using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SpawnAttackerButton : MonoBehaviour
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

        if (!canManage)
        {
            button.interactable = false;
            label.text = "Not your base";
            return;
        }

        // Unlike the other two, this one can be refused up front: an Attacker lives in a barrack,
        // and there's nowhere to put one while every barrack is taken or already reserved by a
        // queued order. Ordering itself is still free - the food cost is paid on the Queen's side.
        bool hasBarrack = selectedBase.FreeBarracks > 0;
        button.interactable = selectedBase.IsQueenAlive && hasBarrack;
        label.text = hasBarrack
            ? $"Spawn Attacker ({cost} food, {selectedBase.FreeBarracks} barracks)"
            : "No free barrack";
    }

    private void OnClicked()
    {
        BaseSelectionManager.SelectedBase?.CmdRequestSpawnAttacker();
    }
}
