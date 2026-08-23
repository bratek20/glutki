using UnityEngine;

// The ResourceStock tile: the physical place a Gatherer deposits at and a Builder loads up from,
// plus the piles that show how full it looks.
//
// Deliberately holds no resource count of its own. The count is still one pool on PlayerBase (see
// CLAUDE.md) - the base just hands each of its stocks the slice of that pool it should be showing,
// so N stocks can never disagree with the number in the HUD.
public class ResourceStock : MonoBehaviour
{
    [Tooltip("The piles on this tile, filled in order. Left empty, every StoredResource under this object is used, in hierarchy order.")]
    [SerializeField] private StoredResource[] piles;

    // Derived rather than configured, so adding or removing a pile in the prefab can't disagree
    // with how much the tile claims to hold. Four piles of two = the capacity of eight.
    public int Capacity => Piles.Length * StoredResource.MaxAmount;

    private StoredResource[] Piles
    {
        get
        {
            if (piles == null || piles.Length == 0) piles = GetComponentsInChildren<StoredResource>(true);
            return piles;
        }
    }

    // Shows amount resources, spread over the piles in order - each one filled to the brim before
    // the next one shows anything.
    public void SetFill(int amount)
    {
        StoredResource[] resourcePiles = Piles;

        for (int i = 0; i < resourcePiles.Length; i++)
        {
            if (resourcePiles[i] == null) continue;
            resourcePiles[i].SetAmount(Mathf.Clamp(amount - i * StoredResource.MaxAmount, 0, StoredResource.MaxAmount));
        }
    }
}
