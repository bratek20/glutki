using UnityEngine;
using Mirror;
using UnityEngine.InputSystem;

public class MirrorMemoryDumper : MonoBehaviour
{
    void Update()
    {
        // Press 'P' on Computer B to dump Mirror's client memory
        if (Keyboard.current.pKey.wasPressedThisFrame)
        {
            Debug.Log($"=== MIRROR CLIENT SPAWNED DICTIONARY (Total: {NetworkClient.spawned.Count}) ===");
            
            foreach (var kvp in NetworkClient.spawned)
            {
                uint netId = kvp.Key;
                NetworkIdentity identity = kvp.Value;
                Debug.Log($"-> Registered NetID: {netId} | Object Name: {identity.gameObject.name} | Pos: {identity.transform.position}");
            }
        }
    }
}