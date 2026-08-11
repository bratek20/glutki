using UnityEngine;
using UnityEngine.InputSystem; // Added New Input System namespace
using Mirror;

public class GameController : NetworkBehaviour
{
    [SerializeField] private GameObject[] slimePrefabs;
    [SerializeField] private int spawnCost = 1;
    private int slimeIndex = 0;

    public int SpawnCost => spawnCost;

    void Update()
    {
        if (!NetworkServer.active || !isServer) return;

        // New Input System check for Spacebar
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            TrySpawn();
        }
    }

    // Any client can request a spawn - the server is the sole authority on whether it's affordable.
    [Command(requiresAuthority = false)]
    public void CmdRequestSpawn()
    {
        TrySpawn();
    }

    [Server]
    private void TrySpawn()
    {
        if (ColonyBase.Instance == null || !ColonyBase.Instance.TrySpendResource(spawnCost)) return;

        SpawnAnt(ColonyBase.Instance.transform.position);
    }

    [Server]
    public void SpawnAnt(Vector3 position)
    {
        var slimePrefab = slimePrefabs[slimeIndex];
        slimeIndex = (slimeIndex + 1) % slimePrefabs.Length;
        GameObject slime = Instantiate(slimePrefab, position, Quaternion.identity);
        NetworkServer.Spawn(slime);
    }
}