# Glutki — Design Overview

A small 2D multiplayer colony prototype in Unity, networked with Mirror (Host + one remote Client,
plus AI-controlled bot waves). Design inspiration: **Ant Colony: Wild Forest** — a shared world map
dotted with bases, each with its own zoomed-in colony interior. Each side owns one or more
**PlayerBase**s that produce autonomous **units** which gather resources and bring them home, while
periodic bot waves march out to try to kill each PlayerBase's Queen.

## Core concepts

- **PlayerBase** (`PlayerBase.cs`) — a NetworkBehaviour placed in the scene. Holds `storedResources`
  and a roster of unit prefabs it can spawn, plus a `queenPrefab`, an `attackerPrefab` and a
  `childPrefab`, and the authored **interior layout** (see below). Owned by either `Host` or
  `Client` (`BaseOwner` enum, assigned per-instance
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
- **Interior tile map** — a base interior *is* a grid of tiles, one `TileType` each: `Floor`,
  `Obstacle`, `Queen`, `Barrack`, `ResourceStock`, `GrowthTile`, `Entry`. It's authored as a letter
  grid on the `PlayerBase` prefab (`layout`, parsed by `BaseLayout`) — `F O Q B R G E`, top row
  first, every row the same width — and the room's whole footprint follows from it (`tileSize` x the
  layout's dimensions), rather than being dialled in separately. The default is a 7x6 walled room
  with the entry in the top wall, two barracks, two growth tiles and a row of five stocks.
  Each type has its own prefab (`BaseInterior.Prefabs`, floor as the fallback), authored **one world
  unit square** and scaled to the base's `tileSize`.
  **Design decision:** the tile *objects* are built locally on every peer (`BaseInterior`), not
  networked — a few dozen `NetworkIdentity`s per base to say the same thing twice would be absurd
  when every peer can derive them. What *is* synced is the grid itself, a `SyncList<byte>` of tile
  types on `PlayerBase`, because a player can change it during play by building. Everything else
  about the interior (where the Queen stands, which tiles are growth tiles or barracks, where the
  entry is) is parsed once from the layout in `Awake` and never changes.
  The **Queen** always covers exactly two side-by-side `Q` tiles and parks on the seam between them
  (`PlayerBase.QueenPoint`) — that, not `InteriorCenter`, is what units aim at. The **entry** tile is
  the gap in the wall units warp in and out through (`InteriorExitPoint`).
- **Obstacles** — units can't walk through an `Obstacle` tile. Enforced in
  `PlayerBase.ResolveMovement`, which every unit's movement step goes through while it's standing in
  some base's interior: it clamps the step into the room and slides along a wall rather than through
  it. Deliberately **not** pathfinding — obstacles are only ever the outer walls today, so nothing
  can be boxed in behind one. Interior wander destinations are pulled onto a walkable tile
  (`NearestWalkablePoint`) so nothing is ever sent toward a target it can't reach. Adding obstacles
  *inside* a room is what would force real pathfinding, and that's out of scope.
- **Queen feeding** — resources don't buy units directly; the Queen has to be *fed* them. Placing an
  order (gatherer, Attacker, or Builder) is free and instant — it only enqueues a prefab. The Queen
  can't start a birth until she's been fed `spawnCost` worth of food, which only ever arrives on a
  **Builder**'s back. So the real cost of a unit is a Builder round trip, and a base with no Builder
  can't produce at all. Surplus food banks toward whatever is ordered next; over-delivery is
  harmless. The player's read on all this is `ResourceHud`, which shows storage *and* `queenFood`.
  **Design decision:** a `ResourceStock` is the physical place a Builder walks to, not a container —
  the resource count itself stays a single pool on `PlayerBase`. One authoritative counter beats
  reconciling N per-stock balances, and it keeps the no-stock-built deposit fallback meaningful.
  A Builder that dies mid-trip loses what it was carrying, which follows from that same choice.
- **Unit production** — spawning is never instant. A spawn request only *enqueues* a prefab on its
  `PlayerBase`; the base then runs a strictly one-at-a-time production line, server-side. When the
  Queen has enough food and a growth tile is free it starts a birth: the Queen's `IsSpawning` animator bool goes true for a configurable `spawnDuration` (a
  plain timer, so the value can just be dialled in to match whatever the birth animation looks
  like), after which a **Child** unit appears at a configurable offset from the Queen — line that
  offset up with the point the animation "produces" it. Growth tiles are the layout's `G` tiles,
  each holding exactly one unit in production; while they're all taken the Queen can't start a birth
  and orders just sit in the queue. A slot is reserved from the moment a birth *starts*, so two
  births can never race for the same tile, and stays held all the way through both phases below —
  only the fully grown unit hands it back (or a death at any point does).
  Ordering an **Attacker** additionally reserves a `Barrack` tile up front (see **Units**), and is
  refused outright when none is free — the one order that can be turned down at request time.
  A Queen's death clears the backlog (releasing the barracks it reserved) and cancels the birth
  in progress, but Children already on a tile are real units and are left to finish.
- **Units** (`UnitController.cs`) — server-authoritative AI. Each unit remembers the `PlayerBase`
  that spawned it (`HomeBase`) and always returns gathered resources there specifically. Task state
  machine: `ExitingBase` (walking from spawn/entry point to the base's interior exit, then warps
  onto the world map) → `Wandering` → `SeekingResource` → `Gathering` (stands at the resource for
  `gatherDuration`, playing the attack animation, before actually drawing from it via
  `Resource.TryGather`) → `ReturningToBase` (world map, walking toward the base) → warps into the
  interior → `EnteringBase` (walking to the nearest `ResourceStock` tile, or the Queen's spot if the
  base has none, and depositing there) → back to `ExitingBase`. Bot wave units instead run `MarchingToBase` →
  `MarchingToQueen`, following the same interior-warp mechanic to reach their target's Queen. The
  **Queen** (`UnitType.Queen`) is a special stationary unit permanently parked on her two tiles at
  each base (`QueenPoint`) — she never runs the task state machine, only combat. Growing up takes two timed
  phases on the growth tile, split so each one can be matched to its own animation: a
  `UnitType.Child` runs `WalkingToGrowthTile` → `WaitingToGrow` (idle for the base's
  `childIdleTime`), then the base swaps it out in place — the Child is destroyed and the ordered
  prefab spawned on the spot, inheriting the same tile — and that unit runs `Growing` (the
  `IsGrowing` animator bool, for `growthTime`) before `BeginNormalLife` sends it off to work. So the
  growth animation plays on the real unit, not the Child. A base is also handed a
  **starting loadout** the instant it spawns — its `startingUnitPrefabs` (one Builder, one Gatherer),
  spawned fully grown on the floor tiles nearest the Queen, the only path that bypasses the Child
  pipeline. Without it a new base would be deadlocked: no Builder to feed the Queen, so no way to
  produce one. (Its stocks it already has — they're in the layout.)
  `UnitType.Builder` units never leave the interior. They shuttle resources from the nearest
  `ResourceStock` tile to the Queen (`WalkingToStock` → `LoadingFood` → `CarryingFoodToQueen`) whenever
  `PlayerBase.FoodShortfall` says an order is waiting on food, and otherwise just wander inside —
  interior wandering is penned into the room and off the walls. Losing
  every Builder ends a base exactly like losing its Queen (see **Game end**): the flag is *latched*
  when the last one dies rather than derived from a live count, so it can't trip on a count read
  before the starting loadout exists, and it never unlatches. Player-owned
  `UnitType.Attacker` units **live in barracks**: each one owns exactly one `Barrack` tile
  (`BarrackSlot`), walks there when it's grown and sits in it (`InBarrack`) — they don't patrol.
  The barrack is claimed when the Attacker is *ordered*, not when it's born, so the free-barrack
  count the player sees is honest with several queued; it stays held while the Attacker is away on a
  raid, and is only released when it dies (a Child carries the reservation too, so one killed before
  it's grown doesn't hold a barrack forever). `PlayerBase.CmdOrderAttack`
  (fired by `AttackOrderPopup`) calls `UnitController.OrderAttack` on some of them — from there they
  run `ExitingBase` → `MarchingToBotBase` → `AttackingBotBase` (stand and repeatedly damage the
  target `BotBase` until it's destroyed or they are) → `ReturningToBarrack`.
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
- **ResourceStock** (`ResourceStock.cs`) — the `R` tile: where a Gatherer deposits and a Builder
  loads up. A base starts with whatever its layout gives it and can build more. It holds **no count
  of its own** — the resource count is still one pool on `PlayerBase` (see **Queen feeding**), and
  the deposit/withdraw calls go straight there. What the component does is *show* how full the base
  is: a tile carries four `StoredResource` piles, each holding one or two resources (a sprite for
  each amount, hidden entirely at zero), so a stock reads up to 8. `PlayerBase` spreads its pool
  over its stocks in grid order, each filled to capacity before the next shows anything — one pool,
  a deterministic way to draw it, so every peer sees the same thing and no per-stock balance can
  ever drift from the HUD. The piles are placed by hand in the prefab; `ResourceStock` finds them
  under itself and derives its capacity from how many there are.
- **Build mode** — `NewBuildButton` drives client-local "build mode" for the viewed base: while
  active it draws the interior grid and a green/red hover-tile highlight straight to the screen via
  `GL` calls, and a left-click on a buildable tile calls `PlayerBase.CmdBuildTile`; right-click,
  Escape, or clicking the button again cancel instead. `PlayerBase.IsTileBuildable` is the single
  source of truth for whether a tile can be built on — a plain `Floor` tile and nothing else —
  called client-side for the preview and server-side (again) as the actual authorization check, so
  they can never disagree. Only a `ResourceStock` can go up during play: growth tiles and barracks
  are counted once at startup, so letting one appear later would put the server's slot bookkeeping
  out of step with the grid. **Gotcha:** the project renders via URP (see
  `ProjectSettings/QualitySettings.asset`'s `customRenderPipeline`), which never invokes the
  legacy `OnRenderObject` callback — GL-based overlays like this one have to hook
  `RenderPipelineManager.endCameraRendering` instead and set up `GL.LoadProjectionMatrix`/
  `GL.modelview` by hand, since URP doesn't do that implicitly the way the built-in pipeline does.
- **UI** — `ResourceHud`, `UnitsHud`, `SpawnUnitButton`, `SpawnAttackerButton`, `SpawnBuilderButton`,
  `ViewToggleButton`, `NewBuildButton`, and `AttackOrderPopup` all read/act on
  `BaseSelectionManager.SelectedBase` /
  `ViewManager`, never a global base reference. Each is a plain `MonoBehaviour` added to its
  corresponding pre-built GameObject in `GameScene` (`Resource_Hud`, `Units_Hud`, `SpawnUnit_Button`,
  `SpawnAttacker_Button`, `SpawnBuilder_Button`, `ViewToggle_Button`, `NewBuild_Button`,
  `AttackOrder_Popup`) with its
  `TMP_Text`/`Button`/`Slider` references wired in the Inspector — UI is laid out by hand in the
  scene, never built at runtime. `UnitsHud` counts gatherers/builders/attackers via
  `UnitController.CountActive`, scoped to the selected base's `HomeBase` (bot-wave units have no
  `HomeBase`, so they never count toward any base) — `CountAlive` is the variant that also skips
  units already at 0 HP, which is what a unit reporting its own death has to use, since it's still
  in the registry at that point. The three spawn buttons gate on the Queen being *alive*, not on
  stored resources: ordering is free, the cost lands later as food. `SpawnAttackerButton` has the
  one extra gate — `PlayerBase.FreeBarracks` (a `[SyncVar]`, so the button can read server-side
  occupancy) must be above zero. `AttackOrderPopup` is opened by `BotBase` on
  click and lets the player choose how many of `BaseSelectionManager.SelectedBase`'s available
  Attackers (`PlayerBase.AvailableAttackers`) to send via a slider, then confirms with
  `PlayerBase.CmdOrderAttack`.
- **Game end** (`GameController.cs`) — server-only, checked once a second via `InvokeRepeating`:
  players win once every `BotBase` is dead (`IsAlive` false, not `== null` — see the `BotBase`
  gotcha above), bots win once every `PlayerBase` is out. A base is out when it loses *either* its
  Queen (`IsQueenAlive`) *or* its last Builder (`HasLivingBuilder`) — with nobody left to carry food
  to her, a Queen can never produce again, so the two are the same defeat. The outcome is a
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
  resources, resource stocks, interior obstacles and barracks — lives together on `Entities`
  **on purpose**: a unit walking below a base overlaps it, walking above it goes behind. Splitting
  buildings into their own layer would make one of those two cases always wrong.
- Interior tiles that are literally the ground (`Tile_Floor`, `Tile_Queen`, `Tile_GrowthTile`,
  `Tile_Entry`) go on `Ground` instead — units always walk on top of them, so there's nothing to
  resolve by position.
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
  item that Claude writes under `Assets/Editor/` (see `ClaudeUiTools.cs`). This is a deliberate
  split: Claude owns the gameplay/UI *scripts*, the user owns what actually lands in the scene.
- The same rule covers **generated prefabs**: `Claude -> Setup Base Tiles`
  (`ClaudeBaseTileTools.cs`) is what creates the placeholder tile sprite and the per-`TileType`
  prefabs, converts `ResourceStock.prefab` into a plain (non-networked) tile with its
  `StoredResource` piles, and wires all of it into `PlayerBase.prefab`. It only ever fills in what's
  missing — a prefab or reference the user has already chosen is never overwritten.
- The same split applies to **prefab and project-settings wiring** a new feature needs before it
  will run: it goes into a menu action too, not hand-edited asset YAML. `ClaudeGameplayTools.cs`
  (`Claude -> Setup Builder Loadout`) and `ClaudeRenderingTools.cs` are the examples. These must be
  idempotent and must not clobber a value the user has deliberately set.
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
