using TMPro;
using UnityEngine;

public class UnitsHud : MonoBehaviour
{
    [SerializeField] private TMP_Text text;

    private void Update()
    {
        PlayerBase selectedBase = BaseSelectionManager.SelectedBase;
        int gatherers = selectedBase != null ? UnitController.CountActive(selectedBase, UnitType.Gatherer) : 0;
        int builders = selectedBase != null ? UnitController.CountActive(selectedBase, UnitType.Builder) : 0;
        int attackers = selectedBase != null ? UnitController.CountActive(selectedBase, UnitType.Attacker) : 0;
        text.text = $"Units:\n* Gatherers: {gatherers}\n* Builders: {builders}\n* Attackers: {attackers}";
    }
}
