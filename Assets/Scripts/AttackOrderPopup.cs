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
    private CanvasGroup canvasGroup;
    private GameObject blocker;
    private bool isOpen;

    public static void Open(BotBase target)
    {
        if (instance != null) instance.Show(target);
    }

    private void Awake()
    {
        instance = this;

        // panel must stay active (never SetActive(false)) so Awake keeps firing on scene load -
        // visibility is driven by a CanvasGroup instead. Grabbed rather than serialized so this
        // works even on a popup built before this fix, with no rewiring needed.
        canvasGroup = panel.GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = panel.AddComponent<CanvasGroup>();

        // The dimmer sibling the Editor menu action creates alongside the popup - looked up by
        // name rather than a serialized field for the same already-built-popup reason.
        Transform blockerTransform = transform.parent != null ? transform.parent.Find("AttackOrder_Blocker") : null;
        blocker = blockerTransform != null ? blockerTransform.gameObject : null;

        attackButton.onClick.AddListener(OnAttackClicked);
        cancelButton.onClick.AddListener(Hide);
        slider.onValueChanged.AddListener(_ => RefreshCountLabel());

        SetVisible(false);
    }

    private void Update()
    {
        if (target == null || !isOpen) return;

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

        SetVisible(true);
    }

    private void Hide()
    {
        target = null;
        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        isOpen = visible;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
        if (blocker != null) blocker.SetActive(visible);
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
