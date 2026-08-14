using Mirror;
using UnityEngine;

// Lets GameScene be played directly, bypassing MainMenu (where the Host/Client buttons normally
// create the NetworkManager - see MainMenuUI.cs). If GameScene loads and no NetworkManager exists
// yet, spin one up from a prefab (identical config to MainMenu's) and start hosting automatically.
public class NetworkBootstrap : MonoBehaviour
{
    [SerializeField] private NetworkManager networkManagerPrefab;

    private void Start()
    {
        if (NetworkManager.singleton != null) return;
        if (networkManagerPrefab == null) return;

        NetworkManager manager = Instantiate(networkManagerPrefab);
        manager.StartHost();
    }
}
