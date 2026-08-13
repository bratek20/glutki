using UnityEngine;
using UnityEngine.InputSystem; // Added New Input System namespace
using Mirror;

public class GameController : NetworkBehaviour
{
    void Update()
    {
        if (!NetworkServer.active || !isServer) return;

        // New Input System check for Spacebar. This is a host-only debug shortcut, so it can
        // only ever spawn from a base the host owns - same rule CmdRequestSpawn enforces for clients.
        Base selectedBase = BaseSelectionManager.SelectedBase;
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame
            && selectedBase != null && selectedBase.Owner == BaseOwner.Host)
        {
            selectedBase.ServerTrySpawn();
        }
    }
}
