using Mirror;
using UnityEngine;

public class ColonyBase : NetworkBehaviour
{
    public static ColonyBase Instance { get; private set; }

    [SyncVar] private int storedResources = 5;

    public int StoredResources => storedResources;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    [Server]
    public void DepositResource(int resourceAmount)
    {
        storedResources += resourceAmount;
        Debug.Log($"Base now holds {storedResources} resource(s).");
    }

    [Server]
    public bool TrySpendResource(int amount)
    {
        if (storedResources < amount) return false;

        storedResources -= amount;
        return true;
    }
}
