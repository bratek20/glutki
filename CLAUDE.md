# Glutki — Design Overview

A small 2D multiplayer colony prototype in Unity, networked with Mirror (Host + one remote Client).
Each side owns a **Base** that produces autonomous **units** (slimes) which gather resources and
bring them home.

## Core concepts

- **Base** (`Base.cs`) — a NetworkBehaviour placed in the scene. Holds `storedResources` and a
  roster of unit prefabs it can spawn. Owned by either `Host` or `Client` (`BaseOwner` enum,
  assigned per-instance in the Inspector — not negotiated at runtime). Many bases can exist.
- **Ownership** — only the owning side can spend a base's resources / spawn from it
  (`Base.CmdRequestSpawn`, checked server-side via the command's sender connection vs
  `NetworkServer.localConnection`). Anyone can *select/inspect* any base (see its resource count,
  highlight it) regardless of owner — inspection is unrestricted, only spawning is gated.
- **Selection** (`BaseSelectionManager.cs`) — a per-peer, **unsynced**, client-local concept: each
  peer tracks its own "currently selected base" and highlights it locally. Not networked by design
  — selection is a viewing concern, not game state.
- **Units** (`SlimeController.cs`) — server-authoritative AI (`Wandering` /`SeekingResource` /
  `ReturningToBase`). Each unit remembers the `Base` that spawned it (`HomeBase`) and returns
  gathered resources there specifically, not to "a" base.
- **Resources** (`Resource.cs`) — pickups consumed by gatherer units.
- **UI** — `ResourceHud` and `SpawnSlimeButton` both read/act on `BaseSelectionManager.SelectedBase`,
  never a global base reference.

## Networking model

- Mirror, client-server. Gameplay logic is server-authoritative, guarded by `[Server]` / `isServer`.
- State that must reach every peer uses `[SyncVar]` (e.g. `storedResources`). State that's purely
  local to a peer (selection) intentionally stays a plain static/local field, never a SyncVar.
- Client → server requests go through `[Command(requiresAuthority = false)]` since bases aren't
  player-owned NetworkIdentities; authorization is checked manually inside the command instead of
  relying on Mirror's built-in ownership.

## Input & platform notes

- The project uses the **new Input System exclusively** (`activeInputHandler: 1`). Legacy
  `UnityEngine.Input` and `OnMouseDown`-style messages don't work here — use `Mouse.current` /
  `Keyboard.current` and manual `Collider2D` overlap/raycast checks instead.
- All gameplay scripts live flat in `Assets/Scripts/` (no subfolders) — keep following that
  convention unless a real reorganization is asked for.

## Maintaining this document

- Update this file whenever a feature changes the game's shape (new system, changed ownership
  rules, changed networking model, etc.) — as part of doing the work, not as an afterthought.
- Keep it **short and high-level**. It describes *what exists and why*, not *how* — implementation
  detail belongs in the code itself.
- Don't duplicate information: if something is obvious from reading the code (exact field names,
  method signatures, algorithms), it doesn't belong here. Only capture what the code alone can't
  tell a reader — intent, constraints, and decisions.
