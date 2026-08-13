using UnityEngine;
using UnityEngine.InputSystem; // Added New Input System namespace
using Mirror;

public class GameController : NetworkBehaviour
{
    void Update()
    {
        if (!NetworkServer.active || !isServer) return;

        // New Input System check for Spacebar
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            BaseSelectionManager.SelectedBase?.ServerTrySpawn();
        }
    }
}
