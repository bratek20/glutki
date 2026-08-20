# Glutki — Design Overview

A small 2D multiplayer colony prototype in Unity, networked with Mirror (Host + one remote Client,
plus AI-controlled bot waves). Design inspiration: **Ant Colony: Wild Forest** — a shared world map
dotted with bases, each with its own zoomed-in colony interior. Each side owns one or more
**PlayerBase**s that produce autonomous **units** which gather resources and bring them home, while
periodic bot waves march out to try to kill each PlayerBase's Queen.

## Core concepts

- **PlayerBase** (`PlayerBase.cs`) — a NetworkBehaviour placed in the scene. Holds `storedResources`
  and a roster of unit prefabs it can spawn, plus a `queenPrefab`, an `attackerPrefab`, a
  `resourceStockPrefab`, and a `childPrefab`. Owned by either `Host` or `Client` (`BaseOwner` enum, assigned per-instance
  in the Inspector — not negotiated at runtime). Many bases can exist. `Base` the class name is
  intentionally free for a future common player/bot base if one ever turns out to be needed — right
  now `PlayerBase` and `BotBase` don't share code.
- **BotBase** (`BotBase.cs`) — a separate, ownerless NetworkBehaviour with no Base View. Purely a
  server-side timer that periodically spawns a wave of bot units, all assigned (via
  `UnitController.AttackTargetBase`) to march on one randomly-picked *still-Queen-alive*
  `PlayerBase` (`PickRandomTarget` filters out any base whose Queen has already fallen, so wave
  units stop getting wasted marching into an empty interior once one player's out). Has its own HP
  (`maxHealth`/`TakeDamage`) independent of any unit's Queen-style HP — reaching 0 calls
  `NetworkServer.Destroy`, but every wave unit it already spawned is an independent
  `NetworkIdentity` with no back-reference to it, so they're unaffected and keep acting normally.
  Clicking it (same `Collider2D`-overlap pattern as `PlayerBase`) opens `AttackOrderPopup` instead
  of entering a Base View — it never touches `BaseSelectionManager`/`ViewManager`, since the
  player's own selected base must stay selected as the source of Attackers to send.
  **Gotcha:** `BotBase` (like `PlayerBase`) is a *scene-placed* `NetworkIdentity` (nested in
  `Map.prefab`, not `Instantiate`d at runtime) — Mirror never actually destroys those,
  `NetworkServer.Destroy` just deactivates them so they stay respawnable. A reference to a "dead"
  `BotBase` therefore never becomes null; code that needs to know whether one is gone must check
  `!botBase.IsAlive` (see `UnitController.IsBotBaseGone`), not `== null`.
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
- **Unit production** — spawning is never instant. A spawn request (gatherer or Attacker) spends
  the resources immediately but only *enqueues* a prefab on its `PlayerBase`; the base then runs a
  strictly one-at-a-time production line, server-side. When a growth tile is free it starts a
  birth: the Queen's `IsSpawning` animator bool goes true for a configurable `spawnDuration` (a
  plain timer, so the value can just be dialled in to match whatever the birth animation looks
  like), after which a **Child** unit appears at a configurable offset from the Queen — line that
  offset up with the point the animation "produces" it. Growth tiles are a run of ordinary
  build-grid tiles offset from the Queen's tile (`growthTileOffset`, then `growthTileCount` tiles
  running right — one tile to her right, two of them, by default), each holding exactly one unit
  in production; while they're all taken the Queen can't start a birth and orders just sit in
  the queue. A slot is reserved from the moment a birth *starts*, so two births can never race for
  the same tile, and stays held all the way through both phases below — only the fully grown unit
  hands it back (or a death at any point does). Nothing can be built on a growth
  tile (`IsTileBuildable` excludes them). A Queen's death clears the backlog and cancels the birth
  in progress, but Children already on a tile are real units and are left to finish.
- **Units** (`UnitController.cs`) — server-authoritative AI. Each unit remembers the `PlayerBase`
  that spawned it (`HomeBase`) and always returns gathered resources there specifically. Task state
  machine: `ExitingBase` (walking from spawn/entry point to the base's interior exit, then warps
  onto the world map) → `Wandering` → `SeekingResource` → `Gathering` (stands at the resource for
  `gatherDuration`, playing the attack animation, before actually drawing from it via
  `Resource.TryGather`) → `ReturningToBase` (world map, walking toward the base) → warps into the
  interior → `EnteringBase` (walking to the resource stock building, or the Queen's spot if the base
  has none, and depositing there) → back to `ExitingBase`. Bot wave units instead run `MarchingToBase` →
  `MarchingToQueen`, following the same interior-warp mechanic to reach their target's Queen. The
  **Queen** (`UnitType.Queen`) is a special stationary unit permanently parked at each base's
  interior center — it never runs the task state machine, only combat. Growing up takes two timed
  phases on the growth tile, split so each one can be matched to its own animation: a
  `UnitType.Child` runs `WalkingToGrowthTile` → `WaitingToGrow` (idle for the base's
  `childIdleTime`), then the base swaps it out in place — the Child is destroyed and the ordered
  prefab spawned on the spot, inheriting the same tile — and that unit runs `Growing` (the
  `IsGrowing` animator bool, for `growthTime`) before `BeginNormalLife` sends it off to gather or
  guard. So the growth animation plays on the real unit, not the Child. Player-owned
  `UnitType.Attacker` units instead spawn straight into `Guarding` (idle near their home base's
  Queen, at a random spot within `guardRadius`) and stay there until `PlayerBase.CmdOrderAttack`
  (fired by `AttackOrderPopup`) calls `UnitController.OrderAttack` on some of them — from there they
  run `ExitingBase` → `MarchingToBotBase` → `AttackingBotBase` (stand and repeatedly damage the
  target `BotBase` until it's destroyed or they are, then wander).
- **Combat** — every unit has HP/damage/attack-interval/attack-range stats and a `Faction`
  (`Host`/`Client`/`Bot`; assigned by whoever spawns the unit). `isAggressive` units actively hunt
  the nearest different-faction unit within `aggroRadius` and chase it; non-aggressive units never
  seek a fight, they only retaliate against whoever last hit them, and give up the moment that
  attacker steps outside `attackRange` (no chasing). Combat is checked before, and overrides,
  whatever task a unit was otherwise doing - it resumes that task afterward. Death
  (`NetworkServer.Destroy`) is immediate at 0 HP; a dying Queen calls back into its `PlayerBase` to
  flip `IsQueenAlive` off.
- **Animation** — all animator state is server-driven: a `[SyncVar]` per animator bool
  (`IsWalking`/`IsAttacking`/`IsSpawning`/`IsGrowing`), pushed to the local `Animator` every frame
  on every peer. Each unit type has its own Animator Controller declaring only the parameters it
  actually animates, so `UnitController` filters pushes against the controller's real parameter
  list — Unity logs a warning *per frame* otherwise.
- **Resources** (`Resource.cs`) — depletable deposits, not one-shot pickups: each holds a
  `totalAmount` and is mined down over many trips. A gatherer takes its own configurable
  `gatherAmount` per trip via `TryGather`, which grants whatever is actually left if that's less
  than asked and returns false once the deposit is empty — so two gatherers finishing in the same
  tick can never over-draw it. A deposit's look tracks how much it still holds: `fillStages` maps
  fill fractions to sprites (the tightest stage covering the current fraction wins, so `1` is the
  full sprite), driven off the remaining-amount `[SyncVar]` so every peer sees the same stage.
  A stage of `0` is never shown — that's the point where the resource disappears. **Gotcha:** same
  scene-placed-`NetworkIdentity` situation as `BotBase` — `Resource` instances are nested in
  `Map.prefab`, so `NetworkServer.Destroy` only deactivates a depleted one, it's never truly
  destroyed. Code that needs to know whether a resource is still up for grabs must check
  `Resource.IsAvailable`, not `== null` — a held reference to a depleted `Resource` never becomes
  null.
- **ResourceStock** (`ResourceStock.cs`) — a player-built "building" in a base's interior; a base
  can have many, built up over time. Purely a deposit point in the world plus a thin forward to
  `PlayerBase.DepositResource` — the resource count itself still lives on `PlayerBase`. Registers
  itself into a static `allStocks` list on `OnStartClient`/`OnStopClient` (same pattern as
  `UnitController.activeUnits`) so *any* peer, not just the server, can enumerate a base's stocks -
  used both for `PlayerBase.IsTileBuildable`'s occupied-tile check and for picking the nearest
  stock to deposit at (`PlayerBase.NearestResourceStock`). `HomeBase` is a `[SyncVar]`, not a bare
  serialized field - it has to reach every peer, not just the server that sets it, since clients
  read it directly (e.g. for the occupied-tile check). If a base has no stock built yet (or no
  `resourceStockPrefab` assigned), gatherers fall back to depositing at the Queen's spot instead.
- **Build grid / Build mode** — each base's interior is tiled by a simple N x M grid of same-size
  tiles (`PlayerBase.tileSize`/`GridColumns`/`GridRows`/`GridOrigin`, fully covering the interior
  room, centered on `InteriorCenter`; `WorldToTile`/`TileCenter` convert between a world position
  and a tile), used to place buildings on. `NewBuildButton` drives client-local "build mode" for
  the viewed base: while active it draws the grid and a green/red hover-tile highlight straight to
  the screen via `GL` calls, and a left-click on a buildable tile calls
  `PlayerBase.CmdBuildResourceStock`; right-click, Escape, or clicking the button again cancel
  instead. `PlayerBase.IsTileBuildable` is the single source of truth for whether a tile can be
  built on (in bounds, not the Queen's tile, not already occupied by one of this base's own
  stocks) — called client-side for the preview and server-side (again) as the actual authorization
  check, so they can never disagree. **Gotcha:** the project renders via URP (see
  `ProjectSettings/QualitySettings.asset`'s `customRenderPipeline`), which never invokes the
  legacy `OnRenderObject` callback — GL-based overlays like this one have to hook
  `RenderPipelineManager.endCameraRendering` instead and set up `GL.LoadProjectionMatrix`/
  `GL.modelview` by hand, since URP doesn't do that implicitly the way the built-in pipeline does.
- **UI** — `ResourceHud`, `UnitsHud`, `SpawnUnitButton`, `SpawnAttackerButton`, `ViewToggleButton`,
  `NewBuildButton`, and `AttackOrderPopup` all read/act on `BaseSelectionManager.SelectedBase` /
  `ViewManager`, never a global base reference. Each is a plain `MonoBehaviour` added to its
  corresponding pre-built GameObject in `GameScene` (`Resource_Hud`, `Units_Hud`, `SpawnUnit_Button`,
  `SpawnAttacker_Button`, `ViewToggle_Button`, `NewBuild_Button`, `AttackOrder_Popup`) with its
  `TMP_Text`/`Button`/`Slider` references wired in the Inspector — UI is laid out by hand in the
  scene, never built at runtime. `UnitsHud` counts gatherers/attackers via
  `UnitController.CountActive`, scoped to the selected base's `HomeBase` (bot-wave units have no
  `HomeBase`, so they never count toward any base). `AttackOrderPopup` is opened by `BotBase` on
  click and lets the player choose how many of `BaseSelectionManager.SelectedBase`'s available
  Attackers (`PlayerBase.AvailableAttackers`) to send via a slider, then confirms with
  `PlayerBase.CmdOrderAttack`.
- **Game end** (`GameController.cs`) — server-only, checked once a second via `InvokeRepeating`:
  players win once every `BotBase` is dead (`IsAlive` false, not `== null` — see the `BotBase`
  gotcha above), bots win once every `PlayerBase.IsQueenAlive` is false. The outcome is a
  `[SyncVar] GameResult` with a hook, so it reaches every peer the same instant the server decides
  it and pops up `GameResultPopup` there too — same local, unsynced popup pattern as
  `AttackOrderPopup`. Its Confirm button calls `GameUI.Disconnect()` (shared with the Leave
  button), which relies on Mirror auto-loading `offlineScene` (`MainMenu`) once disconnected to
  return the player there — no explicit scene-load code needed.

## Networking model

- Mirror, client-server. Gameplay logic is server-authoritative, guarded by `[Server]` / `isServer`.
- State that must reach every peer uses `[SyncVar]` (e.g. `storedResources`). State that's purely
  local to a peer (selection) intentionally stays a plain static/local field, never a SyncVar.
- Client → server requests go through `[Command(requiresAuthority = false)]` since bases aren't
  player-owned NetworkIdentities; authorization is checked manually inside the command instead of
  relying on Mirror's built-in ownership.
- The `NetworkManager` normally only exists as a `dontDestroyOnLoad` scene object in `MainMenu`
  (created before `MainMenuUI`'s Host/Client buttons ever run); `GameScene` itself has none. To let
  `GameScene` be opened and played directly (skipping `MainMenu`) — e.g. for faster iteration in the
  Editor — `NetworkBootstrap.cs` sits in `GameScene` and auto-starts a Host, from a prefab
  (`Assets/Prefabs/NetworkManager.prefab`, kept config-identical to `MainMenu`'s instance) if
  `NetworkManager.singleton` is still null by the time `GameScene` loads. Going through `MainMenu`
  normally is unaffected — the bootstrap only ever acts when nothing has created a NetworkManager
  yet.

## Sprite sorting

- Draw order is engine-level, not script-driven. Sorting layers (back to front:
  `Background` → `Ground` → `Entities` → `Overlay`) give a coarse absolute order for things that
  must never fight each other, and within a layer the URP **2D Renderer**'s Transparency Sort Mode
  is `Custom Axis (0,1,0)`, so sprites sort by world Y — whatever stands lower on screen draws in
  front. Nothing per-frame, and it applies to every sprite automatically.
- Everything that shares the world and should overlap by position — units, Queens, bases,
  resources, resource stocks — lives together on `Entities` **on purpose**: a unit walking below a
  base overlaps it, walking above it goes behind. Splitting buildings into their own layer would
  make one of those two cases always wrong.
- **Sorting order stays 0 on `Entities`.** It's compared before the sort axis, so any nonzero value
  pins that sprite in front of or behind everything and defeats the Y sorting.
- Y sorting uses each renderer's transform position, so **pivots are the sort point**. Unit
  spritesheets are bottom-center pivoted, which makes it read as "whose feet are lower".
  `Base_Spritesheet` is center-pivoted, so a base sorts from its middle rather than its base.
- The setup is applied by the `Claude -> Setup Sprite Sorting` menu action
  (`Assets/Editor/ClaudeRenderingTools.cs`) — it creates the layers, flips the sort mode, and
  assigns every prefab/scene sprite renderer. Re-runnable and idempotent; run it after adding art.
  Sorting layer IDs in it are fixed constants, not Unity's random ones, precisely so a re-run can't
  orphan assignments a previous run made.
- A unit with child sprites (health bar, marker) needs a `SortingGroup` on its root, or each child
  Y-sorts independently and can slide behind other units.

## Input & platform notes

- The project uses the **new Input System exclusively** (`activeInputHandler: 1`). Legacy
  `UnityEngine.Input` and `OnMouseDown`-style messages don't work here — use `Mouse.current` /
  `Keyboard.current` and manual `Collider2D` overlap/raycast checks instead.
- All gameplay scripts live flat in `Assets/Scripts/` (no subfolders) — keep following that
  convention unless a real reorganization is asked for.

## UI element creation

- Claude never hand-edits `GameScene.unity` (or any other scene file) to add, move, or restyle a
  UI element. All UI is added by the **user**, in the Editor, by clicking a `Claude -> ...` menu
  item that Claude writes under `Assets/Editor/` (see `ClaudeUiTools.cs` for the existing pair of
  actions). This is a deliberate split: Claude owns the gameplay/UI *scripts*, the user owns what
  actually lands in the scene.
- A menu action must fully build **and** wire its GameObject in one click — find the scene's
  `UI Canvas`, construct the hierarchy as its child, add the driving `MonoBehaviour`, and assign
  every one of its serialized `Button`/`TMP_Text`/`Slider` references via `SerializedObject` (not
  reflection hacks) — the user should never have to manually drag anything in the Inspector
  afterward.
- Reuse the scene's existing visual building blocks instead of inventing new ones: the built-in
  `UI/Skin/UISprite.psd` sprite (sliced) for buttons/handles/fills, `UI/Skin/Background.psd` for
  panels, `TextMeshProUGUI` children literally named `"Text (TMP)"` (stretch-anchored, centered,
  auto-sizing), and Unity's stock `Button`/`Slider` component defaults (color tint transition,
  standard color block) rather than custom-styling every element.
- Guard against being re-run on an already-built element (check for a same-named child first,
  dialog + select the existing one instead of duplicating), and call
  `Undo.RegisterCreatedObjectUndo` + `EditorSceneManager.MarkSceneDirty` so the action behaves like
  any other Editor edit — undoable, and prompts the scene to be saved.

## Maintaining this document

- Update this file whenever a feature changes the game's shape (new system, changed ownership
  rules, changed networking model, etc.) — as part of doing the work, not as an afterthought.
- Keep it **short and high-level**. It describes *what exists and why*, not *how* — implementation
  detail belongs in the code itself.
- Don't duplicate information: if something is obvious from reading the code (exact field names,
  method signatures, algorithms), it doesn't belong here. Only capture what the code alone can't
  tell a reader — intent, constraints, and decisions.
