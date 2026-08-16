using Mirror;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Resource : NetworkBehaviour
{
    [SerializeField] private int amount = 1;

    // Resource is a scene-placed NetworkIdentity (nested in Map.prefab, same as BotBase) - Mirror
    // never actually destroys those, NetworkServer.Destroy just deactivates them so they stay
    // respawnable. A reference to a "consumed" Resource therefore never becomes null; code that
    // needs to know whether one is still up for grabs must check IsAvailable, not == null.
    [SyncVar] private bool consumed;

    public int Amount => amount;
    public bool IsAvailable => !consumed;

    // Consumes this resource for the caller, returning the amount it held. Returns false (granting
    // nothing) if it was already consumed - guards against two gatherers both arriving in the same
    // tick and double-counting the same resource's amount.
    [Server]
    public bool TryConsume(out int consumedAmount)
    {
        if (consumed)
        {
            consumedAmount = 0;
            return false;
        }

        consumed = true;
        consumedAmount = amount;
        NetworkServer.Destroy(gameObject);
        return true;
    }
}
