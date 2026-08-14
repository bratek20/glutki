using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Local, per-peer popup shown once GameController's synced Result becomes non-None. Same
// always-active-root + CanvasGroup pattern as AttackOrderPopup, and for the same reason: this
// GameObject must stay active so Awake keeps running at scene load and registers the singleton -
// see AttackOrderPopup's comments for the full explanation.
public class GameResultPopup : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private Button confirmButton;

    private static GameResultPopup instance;

    private CanvasGroup canvasGroup;
    private GameObject blocker;

    public static void Show(GameResult result)
    {
        if (instance != null) instance.ShowInternal(result);
    }

    private void Awake()
    {
        instance = this;

        canvasGroup = panel.GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = panel.AddComponent<CanvasGroup>();

        Transform blockerTransform = transform.parent != null ? transform.parent.Find("GameResult_Blocker") : null;
        blocker = blockerTransform != null ? blockerTransform.gameObject : null;

        confirmButton.onClick.AddListener(OnConfirmClicked);

        SetVisible(false);
    }

    private void ShowInternal(GameResult result)
    {
        titleText.text = result == GameResult.PlayersWon
            ? "Victory! Every bot base has been destroyed."
            : "Defeat! Every Queen has fallen.";

        SetVisible(true);
    }

    private void SetVisible(bool visible)
    {
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
        if (blocker != null) blocker.SetActive(visible);
    }

    private void OnConfirmClicked()
    {
        GameUI.Disconnect();
    }
}
