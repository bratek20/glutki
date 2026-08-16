using System.Collections.Generic;
using Mirror;
using UnityEngine;

// A player-built "building" inside a base's interior - gatherers walk here (not to the Queen) to
// deposit resources. Purely a deposit point in the world; the actual resource count still lives on
// PlayerBase. A base can have many of these built over time.
public class ResourceStock : NetworkBehaviour
{
    // Every stock visible to this peer, keyed by nothing in particular - same static-registry
    // pattern as UnitController.activeUnits, so any peer (not just the server) can enumerate a
    // base's stocks, e.g. for the build-mode preview's occupied-tile check.
    private static readonly List<ResourceStock> allStocks = new List<ResourceStock>();

    // A plain SerializeField wouldn't reach remote clients - this has to be a SyncVar so every
    // peer (not just the server that sets it) knows which base a given stock belongs to.
    [SyncVar] private PlayerBase homeBase;
    public PlayerBase HomeBase { get => homeBase; set => homeBase = value; }

    public override void OnStartClient()
    {
        base.OnStartClient();
        allStocks.Add(this);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        allStocks.Remove(this);
    }

    [Server]
    public void Deposit(int amount)
    {
        if (homeBase != null) homeBase.DepositResource(amount);
    }

    // True if one of homeBase's stocks already sits on tile - keeps two builds from landing on the
    // same tile.
    public static bool AnyOccupiesTile(PlayerBase homeBase, Vector2Int tile)
    {
        foreach (ResourceStock stock in allStocks)
        {
            if (stock.homeBase == homeBase && homeBase.WorldToTile(stock.transform.position) == tile) return true;
        }
        return false;
    }

    // Closest stock belonging to homeBase to position, or null if it has none built yet.
    public static ResourceStock Nearest(PlayerBase homeBase, Vector3 position)
    {
        ResourceStock nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (ResourceStock stock in allStocks)
        {
            if (stock.homeBase != homeBase) continue;

            float distance = Vector3.Distance(stock.transform.position, position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = stock;
            }
        }

        return nearest;
    }
}
