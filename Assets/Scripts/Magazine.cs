using UnityEngine;

// The Magazine tile (M in a layout): the physical place a Gatherer deposits at and a Builder loads
// up from, plus the piles that show how full it is.
//
// Holds no count of its own - what's actually in it is synced state on PlayerBase, which hands each
// of its magazines the amount it should be showing. This only turns that number into piles.
public class Magazine : MonoBehaviour
{
    // How many piles ClaudeBaseTileTools builds a magazine with, and therefore the capacity a base
    // assumes when it has no magazine prefab to measure.
    public const int DefaultPiles = 4;

    [Tooltip("The piles on this tile, filled in order. Left empty, every StoredResource under this object is used, in hierarchy order.")]
    [SerializeField] private StoredResource[] piles;

    public static int DefaultCapacity => DefaultPiles * StoredResource.MaxAmount;

    // Measured off the prefab rather than configured on the base, so a magazine's limit can't
    // disagree with how much it's able to show: four piles of two resources is a limit of eight.
    // Deliberately not the instance Capacity - reading that on a prefab asset would resolve (and so
    // dirty) its serialized pile list.
    public static int CapacityOf(GameObject prefab)
    {
        if (prefab == null) return 0;
        return prefab.GetComponentsInChildren<StoredResource>(true).Length * StoredResource.MaxAmount;
    }

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
