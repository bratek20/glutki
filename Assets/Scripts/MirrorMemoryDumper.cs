using UnityEngine;
using Mirror;
using UnityEngine.InputSystem;
using System.Collections.Generic;

// Diagnostic. Press 'P' on EITHER side to dump state.
// On the host it reports the server's view (observers + dirty flags),
// on the client it reports what actually arrived.
public class MirrorMemoryDumper : MonoBehaviour
{
    // How many frames each NetworkBehaviour reported itself dirty.
    // IsDirty() is cleared every time Mirror serializes the component, so we
    // have to sample it every frame rather than read it once on keypress.
    readonly Dictionary<NetworkBehaviour, int> dirtyFrames = new Dictionary<NetworkBehaviour, int>();
    int sampledFrames;

    void Update()
    {
        if (NetworkServer.active) SampleServerDirtyFlags();

        if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
        {
            if (NetworkServer.active) DumpServer();
            else DumpClient();
        }
    }

    void SampleServerDirtyFlags()
    {
        sampledFrames++;
        foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
        {
            if (identity == null || identity.NetworkBehaviours == null) continue;
            foreach (NetworkBehaviour nb in identity.NetworkBehaviours)
            {
                if (nb == null) continue;
                if (!nb.IsDirty()) continue;
                dirtyFrames.TryGetValue(nb, out int count);
                dirtyFrames[nb] = count + 1;
            }
        }
    }

    void DumpServer()
    {
        Debug.Log($"=== SERVER STATE === connections: {NetworkServer.connections.Count} | spawned: {NetworkServer.spawned.Count} | sampled over {sampledFrames} frames");

        foreach (var kvp in NetworkServer.spawned)
        {
            NetworkIdentity identity = kvp.Value;
            if (identity == null) continue;

            // observers.Count is the key number. The host's own local connection
            // counts as one, so with a remote client connected this must be >= 2.
            // If it is 1, the remote connection is not in this object's observer
            // list and Broadcast() will never send it anything.
            Debug.Log($"-> NetID: {kvp.Key} | {identity.gameObject.name} | Pos: {identity.transform.position} | observers: {identity.observers.Count} [{string.Join(",", identity.observers.Keys)}]");

            foreach (NetworkBehaviour nb in identity.NetworkBehaviours ?? new NetworkBehaviour[0])
            {
                if (nb == null) continue;
                dirtyFrames.TryGetValue(nb, out int count);
                Debug.Log($"     {nb.GetType().Name}: dirty on {count}/{sampledFrames} frames | syncDir: {nb.syncDirection} | syncMode: {nb.syncMode} | syncInterval: {nb.syncInterval} | enabled: {nb.enabled}");
            }
        }

        dirtyFrames.Clear();
        sampledFrames = 0;
    }

    void DumpClient()
    {
        Debug.Log($"=== CLIENT STATE === ready: {NetworkClient.ready} | connected: {NetworkClient.isConnected} | NetworkTime.time: {NetworkTime.time:F2} | spawned: {NetworkClient.spawned.Count}");

        foreach (var kvp in NetworkClient.spawned)
        {
            NetworkIdentity identity = kvp.Value;
            if (identity == null) continue;

            int nbCount = identity.NetworkBehaviours != null ? identity.NetworkBehaviours.Length : -1;
            string ntInfo = "no NetworkTransform";
            if (identity.TryGetComponent(out NetworkTransformBase nt))
                ntInfo = $"{nt.GetType().Name} snapshots: {nt.clientSnapshots.Count}";

            Debug.Log($"-> NetID: {kvp.Key} | {identity.gameObject.name} | Pos: {identity.transform.position} | isOwned: {identity.isOwned} | components: {nbCount} | {ntInfo}");
        }
    }
}
