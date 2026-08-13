using UnityEngine;
using UnityEngine.UI;
using Mirror;

public class GameUI : MonoBehaviour
{
    [SerializeField] private Button disconnectButton;

    private void Start()
    {
        if (disconnectButton != null)
        {
            disconnectButton.onClick.AddListener(OnDisconnectButtonClicked);
        }
    }

    private void OnDisconnectButtonClicked()
    {
        if (NetworkManager.singleton == null) return;

        // Check if we are hosting or just a client
        if (NetworkServer.active && NetworkClient.isConnected)
        {
            NetworkManager.singleton.StopHost();
        }
        else if (NetworkClient.isConnected)
        {
            NetworkManager.singleton.StopClient();
        }
    }
}