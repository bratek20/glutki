using System;
using Mirror;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Resource : NetworkBehaviour
{
    [Serializable]
    public struct FillStage
    {
        [Tooltip("Shown while the remaining fill is at or below this fraction of totalAmount (and above the next lower stage's).")]
        [Range(0f, 1f)] public float maxPercentage;
        public Sprite sprite;
    }

    // How much a gatherer takes per trip lives on the gatherer (UnitController.gatherAmount), not
    // here - a resource only knows how much it still holds.
    [Tooltip("How much this resource holds in total when full.")]
    [SerializeField] private int totalAmount = 100;

    [Tooltip("Sprite per depletion stage. The stage with the smallest maxPercentage that still covers the current fill wins, so 1 is the full-looking sprite. A stage of 0 is never shown - that's when the resource is gone.")]
    [SerializeField] private FillStage[] fillStages;

    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("Debug")]
    [Tooltip("Editor-only preview: drag this to see which fillStages sprite a given fill fraction resolves to. Only applies outside Play mode - at runtime the real remaining amount drives the sprite.")]
    [Range(0f, 1f)][SerializeField] private float previewPercentage = 1f;

    // Resource is a scene-placed NetworkIdentity (nested in Map.prefab, same as BotBase) - Mirror
    // never actually destroys those, NetworkServer.Destroy just deactivates them so they stay
    // respawnable. A reference to a depleted Resource therefore never becomes null; code that
    // needs to know whether one is still up for grabs must check IsAvailable, not == null.
    [SyncVar(hook = nameof(OnRemainingChanged))] private int remainingAmount;

    public int RemainingAmount => remainingAmount;
    public bool IsAvailable => remainingAmount > 0;

    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        remainingAmount = totalAmount;
    }

    // Editor-only: previewPercentage stands in for the runtime remaining amount so the depletion
    // stages can be eyeballed without entering Play mode. Deliberately doesn't touch the
    // remainingAmount SyncVar - writing one outside a spawned NetworkIdentity isn't valid.
    private void OnValidate()
    {
        if (Application.isPlaying) return;
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        ApplyStageSprite(previewPercentage);
    }

    public override void OnStartServer()
    {
        remainingAmount = totalAmount;
        ApplyStageSprite();
    }

    public override void OnStartClient()
    {
        ApplyStageSprite();
    }

    // Takes up to requested from what's left, returning how much was actually granted. Returns
    // false (granting nothing) if the resource is already empty - guards against two gatherers both
    // finishing in the same tick and over-drawing what the resource still held.
    [Server]
    public bool TryGather(int requested, out int gatheredAmount)
    {
        if (remainingAmount <= 0 || requested <= 0)
        {
            gatheredAmount = 0;
            return false;
        }

        gatheredAmount = Mathf.Min(requested, remainingAmount);
        remainingAmount -= gatheredAmount;

        if (remainingAmount <= 0) NetworkServer.Destroy(gameObject);
        else ApplyStageSprite();

        return true;
    }

    private void OnRemainingChanged(int oldValue, int newValue)
    {
        ApplyStageSprite();
    }

    private void ApplyStageSprite()
    {
        if (remainingAmount <= 0) return;
        ApplyStageSprite(totalAmount > 0 ? (float)remainingAmount / totalAmount : 0f);
    }

    // Picks the tightest stage that still covers the given fill: of every stage whose
    // maxPercentage is at or above it, the one with the smallest maxPercentage.
    private void ApplyStageSprite(float percentage)
    {
        if (spriteRenderer == null || fillStages == null || fillStages.Length == 0) return;

        Sprite best = null;
        float bestMax = float.MaxValue;
        Sprite fallback = null;
        float fallbackMax = float.MinValue;

        foreach (FillStage stage in fillStages)
        {
            if (stage.sprite == null) continue;

            if (stage.maxPercentage >= percentage && stage.maxPercentage < bestMax)
            {
                best = stage.sprite;
                bestMax = stage.maxPercentage;
            }

            if (stage.maxPercentage > fallbackMax)
            {
                fallback = stage.sprite;
                fallbackMax = stage.maxPercentage;
            }
        }

        Sprite chosen = best != null ? best : fallback;
        if (chosen != null) spriteRenderer.sprite = chosen;
    }
}
