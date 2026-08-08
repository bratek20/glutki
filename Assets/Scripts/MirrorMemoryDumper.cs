using UnityEngine;
using Mirror;
using UnityEngine.InputSystem;

public class MirrorMemoryDumper : MonoBehaviour
{
    void Update()
    {
        // Press 'P' on the client to dump Mirror's client state
        if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
        {
            Debug.Log($"=== MIRROR CLIENT STATE === ready: {NetworkClient.ready} | connected: {NetworkClient.isConnected} | NetworkTime.time: {NetworkTime.time:F2} | spawned: {NetworkClient.spawned.Count}");

            foreach (var kvp in NetworkClient.spawned)
            {
                NetworkIdentity identity = kvp.Value;

                // How many transform snapshots has this object actually received?
                // 0 = the server's NetworkTransform updates are NOT arriving.
                // >0 but position frozen = they arrive but interpolation isn't applying them.
                string ntInfo = "no NetworkTransform";
                if (identity.TryGetComponent(out NetworkTransformBase nt))
                    ntInfo = $"{nt.GetType().Name} snapshots: {nt.clientSnapshots.Count} | syncDir: {nt.syncDirection} | syncPos: {nt.syncPosition}";

                Debug.Log($"-> NetID: {kvp.Key} | {identity.gameObject.name} | Pos: {identity.transform.position} | isOwned: {identity.isOwned} | {ntInfo}");
            }
        }
    }
}
