using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Local, per-peer popup shown when the player clicks a BotBase. Lets them pick how many of their
// selected base's available Attackers to send, then fires PlayerBase.CmdOrderAttack. Never touches
// BaseSelectionManager/ViewManager - the player's own selected base stays selected throughout, since
// that's where the Attackers are pulled from.
public class AttackOrderPopup : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private Slider slider;
    [SerializeField] private TMP_Text countLabel;
    [SerializeField] private Button attackButton;
    [SerializeField] private Button cancelButton;

    private static AttackOrderPopup instance;

    private BotBase target;

    public static void Open(BotBase target)
    {
        if (instance != null) instance.Show(target);
    }

    private void Awake()
    {
        instance = this;

        attackButton.onClick.AddListener(OnAttackClicked);
        cancelButton.onClick.AddListener(Hide);
        slider.onValueChanged.AddListener(_ => RefreshCountLabel());
    }

    private void Update()
    {
        if (target == null || !panel.activeSelf) return;

        if (!target.IsAlive)
        {
            Hide();
            return;
        }

        RefreshAvailableAttackers();
        RefreshHpText();
    }

    private void Show(BotBase newTarget)
    {
        target = newTarget;

        titleText.text = "Attack Bot Base";
        slider.wholeNumbers = true;
        slider.minValue = 0;
        slider.value = 0;

        RefreshAvailableAttackers();
        RefreshHpText();
        RefreshCountLabel();

        panel.SetActive(true);
    }

    private void Hide()
    {
        target = null;
        panel.SetActive(false);
    }

    private void RefreshAvailableAttackers()
    {
        PlayerBase selectedBase = BaseSelectionManager.SelectedBase;
        int available = selectedBase != null && selectedBase.IsOwnedByLocalPlayer ? selectedBase.AvailableAttackers : 0;

        slider.maxValue = available;
        if (slider.value > available) slider.value = available;

        attackButton.interactable = available > 0;
        RefreshCountLabel();
    }

    private void RefreshHpText()
    {
        if (target == null) return;
        hpText.text = $"HP: {target.CurrentHealth} / {target.MaxHealth}";
    }

    private void RefreshCountLabel()
    {
        countLabel.text = $"Send {(int)slider.value} / {(int)slider.maxValue} attackers";
    }

    private void OnAttackClicked()
    {
        int count = (int)slider.value;
        if (count > 0)
        {
            BaseSelectionManager.SelectedBase?.CmdOrderAttack(target, count);
        }

        Hide();
    }
}
