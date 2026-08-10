using UnityEngine;
using Mirror;

// DIAGNOSTIC ONLY - a NetworkTransformReliable that logs its own sync traffic.
// Swap back to plain NetworkTransformReliable once the bug is found.
public class DebugNetworkTransform : NetworkTransformReliable
{
    // logs are throttled so we don't drown the console at 60 ticks/sec
    double nextSerializeLog;
    double nextDeserializeLog;

    public override void OnSerialize(NetworkWriter writer, bool initialState)
    {
        int before = writer.Position;
        base.OnSerialize(writer, initialState);

        if (initialState || NetworkTime.localTime >= nextSerializeLog)
        {
            nextSerializeLog = NetworkTime.localTime + 1.0;
            Debug.Log($"<color=orange>[NT SERIALIZE]</color> initial:{initialState} | wrote {writer.Position - before} bytes | server pos: {transform.position}");
        }
    }

    public override void OnDeserialize(NetworkReader reader, bool initialState)
    {
        int before = reader.Position;
        base.OnDeserialize(reader, initialState);

        if (initialState || NetworkTime.localTime >= nextDeserializeLog)
        {
            nextDeserializeLog = NetworkTime.localTime + 1.0;
            Debug.Log($"<color=lime>[NT DESERIALIZE]</color> initial:{initialState} | read {reader.Position - before} bytes | snapshots: {clientSnapshots.Count} | my pos: {transform.position} | isServer:{isServer} isClient:{isClient}");
        }
    }

    // called on the client for every server->client transform update
    protected override void OnServerToClientSync(Vector3? position, Quaternion? rotation, Vector3? scale)
    {
        base.OnServerToClientSync(position, rotation, scale);
        Debug.Log($"<color=cyan>[NT S2C]</color> received pos: {position} | snapshots now: {clientSnapshots.Count} | NetworkTime.time: {NetworkTime.time:F2}");
    }
}
