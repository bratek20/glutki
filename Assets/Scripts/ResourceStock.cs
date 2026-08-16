using Mirror;
using UnityEngine;

// First "building" placed inside a base's interior. Purely a deposit point in the world -
// gatherers walk here (instead of to the Queen) to hand over what they're carrying. The actual
// resource count still lives on PlayerBase; this just forwards to it.
public class ResourceStock : NetworkBehaviour
{
    [field: SerializeField] public PlayerBase HomeBase { get; set; }

    [Server]
    public void Deposit(int amount)
    {
        if (HomeBase != null) HomeBase.DepositResource(amount);
    }
}
