# Glutki — Design Overview

A small 2D multiplayer colony prototype in Unity, networked with Mirror (Host + one remote Client,
plus AI-controlled bot waves). Design inspiration: **Ant Colony: Wild Forest** — a shared world map
dotted with bases, each with its own zoomed-in colony interior. Each side owns one or more
**PlayerBase**s that produce autonomous **units** which gather resources and bring them home, while
periodic bot waves march out to try to kill each PlayerBase's Queen.

## Core concepts

- **PlayerBase** (`PlayerBase.cs`) — a NetworkBehaviour placed in the scene. Holds `storedResources`
  and a roster of unit prefabs it can spawn, plus a `queenPrefab`. Owned by either `Host` or
  `Client` (`BaseOwner` enum, assigned per-instance in the Inspector — not negotiated at runtime).
  Many bases can exist. `Base` the class name is intentionally free for a future common
  player/bot base if one ever turns out to be needed — right now `PlayerBase` and `BotBase` don't
  share code.
- **BotBase** (`BotBase.cs`) — a separate, ownerless NetworkBehaviour with no Base View. Purely a
  server-side timer that periodically spawns a wave of bot units, all assigned (via
  `UnitController.AttackTargetBase`) to march on one randomly-picked `PlayerBase`.
- **Ownership** — only the owning side can spend a base's resources / spawn from it
  (`PlayerBase.CmdRequestSpawn`, checked server-side via the command's sender connection vs
  `NetworkServer.localConnection`). Anyone can *select/inspect* any base (see its resource count,
  highlight it, enter its Base View) regardless of owner — inspection is unrestricted, only
  spawning is gated. Once a base's Queen dies (`PlayerBase.IsQueenAlive` goes false), spawning is
  gated off entirely and its gatherers permanently give up gathering, wandering aimlessly instead.
- **Selection** (`BaseSelectionManager.cs`) — a per-peer, **unsynced**, client-local concept: each
  peer tracks its own "currently selected base" and highlights it locally. Not networked by design
  — selection is a viewing concern, not game state.
- **World View / Base View** (`ViewManager.cs`) — another unsynced, per-peer concept: the camera is
  either showing the shared world map, or "inside" one specific base. Clicking a base (or the
  bottom-right toggle button) enters its Base View; the toggle button also leaves it. Every base's
  interior is a real place in the shared world, not a separate scene, so units physically occupy
  it and every peer sees the same interior content when looking there. Interiors are placed by
  `netId` rather than map position (`PlayerBase.InteriorCenter`) — each base gets its own far-apart slot
  in a dedicated row, so bases sitting close together on the map never bleed into each other's
  interior view.
- **Units** (`UnitController.cs`) — server-authoritative AI. Each unit remembers the `PlayerBase`
  that spawned it (`HomeBase`) and always returns gathered resources there specifically. Task state
  machine: `ExitingBase` (walking from spawn/entry point to the base's interior exit, then warps
  onto the world map) → `Wandering` → `SeekingResource` → `ReturningToBase` (world map, walking
  toward the base) → warps into the interior → `EnteringBase` (walking to the Queen/depot point,
  deposits) → back to `ExitingBase`. Bot wave units instead run `MarchingToBase` →
  `MarchingToQueen`, following the same interior-warp mechanic to reach their target's Queen. The
  **Queen** (`UnitType.Queen`) is a special stationary unit permanently parked at each base's
  interior center — it never runs the task state machine, only combat.
- **Combat** — every unit has HP/damage/attack-interval/attack-range stats and a `Faction`
  (`Host`/`Client`/`Bot`; assigned by whoever spawns the unit). `isAggressive` units actively hunt
  the nearest different-faction unit within `aggroRadius` and chase it; non-aggressive units never
  seek a fight, they only retaliate against whoever last hit them, and give up the moment that
  attacker steps outside `attackRange` (no chasing). Combat is checked before, and overrides,
  whatever task a unit was otherwise doing - it resumes that task afterward. Death
  (`NetworkServer.Destroy`) is immediate at 0 HP; a dying Queen calls back into its `PlayerBase` to
  flip `IsQueenAlive` off.
- **Resources** (`Resource.cs`) — pickups consumed by gatherer units.
- **UI** — `ResourceHud`, `SpawnUnitButton`, and `ViewToggleButton` all read/act on
  `BaseSelectionManager.SelectedBase` / `ViewManager`, never a global base reference.

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
