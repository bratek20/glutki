using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;

public class MainMenuUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_InputField ipInputField;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;

    [Header("Default Network Settings")]
    [SerializeField] private string defaultIP = "127.0.0.1"; // Pre-filled IP

    private void Start()
    {
        // Pre-fill the IP field if empty
        if (ipInputField != null && string.IsNullOrEmpty(ipInputField.text))
        {
            ipInputField.text = defaultIP;
        }

        // Attach button listeners
        if (hostButton != null)
            hostButton.onClick.AddListener(OnHostButtonClicked);

        if (clientButton != null)
            clientButton.onClick.AddListener(OnClientButtonClicked);
    }

    private void OnHostButtonClicked()
    {
        if (NetworkManager.singleton == null) return;

        // Starts local host (Server + Client)
        NetworkManager.singleton.StartHost();
    }

    private void OnClientButtonClicked()
    {
        if (NetworkManager.singleton == null) return;

        // Set target IP address
        string targetIP = string.IsNullOrWhiteSpace(ipInputField.text) ? defaultIP : ipInputField.text;
        NetworkManager.singleton.networkAddress = targetIP;

        // Connect as client
        NetworkManager.singleton.StartClient();
    }
}