using UnityEngine;

// One physical pile of resources sitting on a ResourceStock tile. A pile holds either one or two
// resources and has a sprite for each; holding none means it isn't there at all, so the object
// simply switches itself off. Four piles on a tile is where a stock's capacity of eight comes from.
//
// Placed by hand in the ResourceStock prefab - this only decides whether a pile is visible and
// which of its two sprites it shows.
public class StoredResource : MonoBehaviour
{
    public const int MaxAmount = 2;

    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("Shown while this pile holds a single resource.")]
    [SerializeField] private Sprite singleSprite;
    [Tooltip("Shown while this pile holds two resources - its full look.")]
    [SerializeField] private Sprite doubleSprite;

    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void SetAmount(int amount)
    {
        amount = Mathf.Clamp(amount, 0, MaxAmount);

        if (amount == 0)
        {
            gameObject.SetActive(false);
            return;
        }

        // Activating first matters: a pile that has never been shown hasn't run Awake yet, so
        // spriteRenderer is only resolved once the object comes on.
        gameObject.SetActive(true);
        if (spriteRenderer == null) return;

        Sprite sprite = amount == 1 ? singleSprite : doubleSprite;
        if (sprite != null) spriteRenderer.sprite = sprite;
    }
}
