# Mirror: networked objects spawn on client but never move

**Stack:** Unity + Mirror 96.0.1, KcpTransport, host on one machine + remote client on LAN
(`192.168.0.156:7777`), server-authoritative movement via `NetworkTransform`.

## Symptom

A prefab (`Ant_Worker`) spawned with `NetworkServer.Spawn()` appears on the remote client but sits
at `(0,0,0)` forever. On the host it moves correctly. No errors in either console.

## Root cause

`NetworkServer.SetClientReady` only spawns observers when the connection owns a player object —
`NetworkServer.cs:1271`:

```csharp
conn.isReady = true;
if (conn.identity != null)          // null: NetworkManager had no playerPrefab
    SpawnObserversForConnection(conn);
```

The project ran with `playerPrefab = null` and `autoCreatePlayer = false`, so `conn.identity` was
permanently `null` and `SpawnObserversForConnection` never ran. That function is the sole sender of
`ObjectSpawnStartedMessage`/`ObjectSpawnFinishedMessage`, so on the client
`NetworkClient.isSpawnFinished` (`NetworkClient.cs:110`) stayed `false` for the whole session.

That matters because of `NetworkClient.OnSpawn` (`NetworkClient.cs:1449`):

```csharp
if (isSpawnFinished) ApplySpawnPayload(identity, message);
else { /* defer via pendingSpawns until OnObjectSpawnFinished */ }
```

Every spawn took the deferred branch. The object was added to `NetworkClient.spawned` and positioned
from the SpawnMessage — hence `(0,0,0)` — but `InitializeIdentityFlags`, which does
`identity.isClient = true` (`NetworkClient.cs:1391`), never ran, and neither did `OnStartClient`.
The deferred payload waits on an `OnObjectSpawnFinished` that never arrives.

The failure then surfaces in `NetworkTransformReliable.OnDeserialize`
(`NetworkTransformReliable.cs:315`):

```csharp
if (isServer) OnClientToServerSync(position, rotation, scale);
else if (isClient) OnServerToClientSync(position, rotation, scale);
```

On the client both flags are `false`, so the transform bytes are read and silently discarded.
`clientSnapshots` stays empty, and `Update()`'s `else if (isClient) UpdateClient()` never runs
either.

**Why SyncVars still worked:** the generated `DeserializeSyncVars` doesn't check `isClient`, so a
test SyncVar replicated perfectly while the transform in the *same* `EntityStateMessage` was
dropped. This asymmetry is what isolated the bug.

**Why the ant was still an observer:** it was spawned *after* the client went ready, via
`RebuildObservers` -> `AddAllReadyServerConnectionsToObservers` (`NetworkServer.cs:1853`), which only
checks `conn.isReady`. So server-side state looked entirely healthy — `observers: 2`, dirty flags
set, bytes serialized every tick.

## Evidence chain

| Observation | Conclusion |
|---|---|
| Client: `ready: True`, `connected: True`, object in `spawned` | Connection healthy |
| Server: `observers: 2 [0,-96390570]`, `NetworkTransformReliable: dirty on 233/3161 frames` | Server correctly serializing and sending |
| Client: `[NT DESERIALIZE] read 7 bytes \| snapshots: 0` | Payload arrives, is parsed, then discarded |
| Client: `isServer:False isClient:False` | **The smoking gun** |
| SyncVar hook fires, transform doesn't, same message | Not transport, not observers, not masks |
| Server: `GameController \| observers: 0`, client `spawned: 1` not 2 | Scene objects never observed — same root cause |

## Fix

1. Added a minimal `Player.prefab` (GameObject + Transform + NetworkIdentity, nothing else).
2. NetworkManager: `playerPrefab` -> Player.prefab, `autoCreatePlayer` -> `true`.

Both are required. `NetworkManager.OnClientConnect` (`NetworkManager.cs:1375`) sends `Ready()`
*before* `AddPlayer()`. The `Ready` pass hits `SetClientReady` while `conn.identity` is still null
and skips observer spawning; it's `AddPlayerForConnection` that assigns `conn.identity`
(`NetworkServer.cs:1114`) and then calls `SetClientReady` a second time (`NetworkServer.cs:1127`),
which is the pass that actually works. Assigning a playerPrefab without `autoCreatePlayer` changes
nothing, since `AddPlayer()` would never be sent.

## Ruled out along the way

- **Unreliable channel / `NetworkTransformUnreliable`** — initially suspected because the spawn
  message (reliable) arrived while updates (unreliable RPC) didn't. Wrong: swapping to
  `NetworkTransformReliable` changed nothing, since the real gate was `isClient`. The swap was kept
  anyway as the better default.
- **Stale build / component-order mismatch** — client reported `components: 2`, matching the server.
- **Interest management / observers** — server dump confirmed the remote connection was an observer.
- **Dirty flags / sync masks** — confirmed dirty and serializing on the server.

## Secondary bugs found (fixed)

- `AntController.Start()` overwrote `targetPosition` set by `OnStartServer`, wasting the first
  movement frame (`OnStartServer` runs before `Start`). Init moved into `OnStartServer`.
- Client-side rotation code fought `NetworkTransform`: both wrote `transform.rotation` every frame
  with undefined ordering. Rotation is now computed server-side only and replicated.

## Still open

`PickRandomTarget` offsets from the ant's *current* position with no bounds, so it random-walks away
indefinitely (observed reaching `y = -18.9`). Cosmetic, unrelated to sync, not yet fixed.

## Diagnostic tooling left in the repo

- `Assets/Scripts/MirrorMemoryDumper.cs` — press **P** to dump state. On the host it reports
  observers and per-component dirty-frame counts; on the client it reports what actually arrived.
- `Assets/Scripts/DebugNetworkTransform.cs` — a `NetworkTransformReliable` subclass that logs its own
  serialize/deserialize traffic. Currently unused; point the ant prefab's NetworkTransform script
  reference at it to re-enable.
