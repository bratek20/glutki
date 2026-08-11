using UnityEngine;
using UnityEngine.InputSystem; // Added New Input System namespace
using Mirror;

public class GameController : NetworkBehaviour
{
    [SerializeField] private GameObject[] slimePrefabs;
    private int slimeIndex = 0;

    void Update()
    {
        if (!NetworkServer.active || !isServer) return;

        // New Input System check for Spacebar
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            SpawnAnt(Vector3.zero);
        }
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