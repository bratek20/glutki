using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class PlayerBase : NetworkBehaviour
{
    [SerializeField] private GameObject[] unitPrefabs;
    [SerializeField] private GameObject queenPrefab;
    [SerializeField] private GameObject attackerPrefab;
    [SerializeField] private GameObject builderPrefab;
    [SerializeField] private GameObject childPrefab;
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.3f);
    [SerializeField] private BaseOwner owner = BaseOwner.Host;

    [Header("Queen Feeding")]
    [Tooltip("How much food the Queen must have been fed before she can start one birth. Ordering a unit is free and instant; the wait is a Builder shuttling this much from a magazine to her.")]
    [SerializeField] private int spawnCost = 5;

    [Header("Storage")]
    [Tooltip("Resources this base starts with, dealt out over its magazines in grid order - anything beyond what they can hold is dropped.")]
    [SerializeField] private int startingResources = 5;

    [Header("Starting Loadout")]
    [Tooltip("Units this base is given for free, fully grown (skipping the Child/growth pipeline), the moment the game starts. One Builder and one Gatherer by default - with no Builder the Queen could never be fed, so the base could never produce anything.")]
    [SerializeField] private GameObject[] startingUnitPrefabs;

    [Header("Interior layout")]
    [Tooltip("The interior, one letter per tile, top row first: O obstacle, F floor, Q queen (always two side by side), B barrack, R resource stock, G growth tile, E base entry. Every row must be the same width.")]
    [TextArea(6, 14)]
    [SerializeField] private string layout = BaseLayout.Default;
    [Tooltip("Size (world units) of one interior tile. The room's whole footprint is the layout's tile count times this, and tile prefabs (authored one world unit square) are scaled to it.")]
    [SerializeField] private float tileSize = 2f;
    [Tooltip("One prefab per tile type, each authored one world unit square. Anything left empty falls back to the floor prefab.")]
    [SerializeField] private BaseInterior.Prefabs tilePrefabs = new BaseInterior.Prefabs();
    [Tooltip("Distance between one base's interior slot and the next. Must comfortably exceed the interior's own width so no two interiors are ever visible at once, regardless of how close the bases are on the map.")]
    [SerializeField] private float interiorSlotSpacing = 500f;
    [Tooltip("How far below the map the whole row of interiors sits.")]
    [SerializeField] private float interiorRowY = -1000f;

    [Header("Unit Growth")]
    [Tooltip("How long the Queen plays her spawn animation before the Child she's producing actually appears.")]
    [SerializeField] private float spawnDuration = 1.5f;
    [Tooltip("Where a freshly produced Child appears, relative to the Queen. Line this up with the point the spawn animation 'drops' it. Pulled onto the nearest walkable tile if it lands in a wall.")]
    [SerializeField] private Vector2 childSpawnOffset = new Vector2(0.6f, -0.6f);
    [Tooltip("How long a Child stands idle on its growth tile before it's transformed into the unit it was ordered as.")]
    [SerializeField] private float childIdleTime = 3f;
    [Tooltip("How long that transformed unit then plays its growth animation (IsGrowing) on the tile before it comes to life. Set this to match the animation's length.")]
    [SerializeField] private float growthTime = 5f;

    [SyncVar] private UnitController queen;
    [SyncVar] private bool queenAlive = true;

    // Food a Builder has carried to the Queen and she hasn't spent on a birth yet. Synced because
    // the HUD shows it - it's the player's only read on why a queued order hasn't started.
    [SyncVar] private int queenFood;

    // Latched by OnBuilderKilled rather than derived from a live count, so it can only ever be set
    // as the result of a Builder actually dying - never by a count that reads 0 before the starting
    // loadout has spawned.
    [SyncVar] private bool buildersLost;

    // Barracks not already taken by an Attacker - or reserved by one that's been ordered and hasn't
    // been born yet. Synced purely so SpawnAttackerButton can grey itself out; the occupancy itself
    // is server state.
    [SyncVar] private int freeBarracks;

    // The live tile grid, one byte per tile, row-major from the bottom-left. Seeded from `layout`,
    // which is identical on every peer - but a player can build on a floor tile during play, so the
    // grid has to be synced rather than re-derived. Bytes rather than the enum so the wire format
    // can't shift under a reordered TileType.
    private readonly SyncList<byte> tileTypes = new SyncList<byte>();

    // What's actually sitting in each of this base's magazines, one entry per tile so an index can
    // never shift under a magazine that was built mid-game (everything that isn't one stays 0).
    //
    // Design decision: storage used to be a single pool on the base, drawn over the magazines in
    // grid order. Magazines each having their own limit is what forced this apart - a Gatherer has
    // to be told which magazine still has room, and the piles it walks up to have to be the ones
    // its load actually went into.
    private readonly SyncList<int> tileResources = new SyncList<int>();

    private SpriteRenderer spriteRenderer;
    private Collider2D selectionCollider;
    private Color normalColor;
    private int unitIndex;

    // Parsed from `layout` in Awake, on every peer. The grid's shape and its special tiles never
    // change during play (only floor tiles are ever built on), so this is worked out exactly once.
    private TileType[] layoutTiles;
    private int columns = 1;
    private int rows = 1;
    private readonly List<Vector2Int> growthTiles = new List<Vector2Int>();
    private readonly List<Vector2Int> barrackTiles = new List<Vector2Int>();
    private readonly List<Vector2Int> queenTiles = new List<Vector2Int>();
    private Vector2Int entryTile;
    private bool hasEntryTile;

    // Local view of the interior - the actual tile GameObjects. Null on a peer that never renders.
    private BaseInterior interior;

    // How much one magazine holds, measured off the magazine prefab in Awake (see Magazine) so the
    // limit and the piles that display it can't disagree. Every peer reads the same prefab.
    private int magazineCapacity;

    // Cached so asking for the nearest magazine doesn't allocate a delegate every trip.
    private Predicate<Vector2Int> magazineHasSpace;
    private Predicate<Vector2Int> magazineHasResources;

    // Server-only production state. A spawn order is only ever a prefab waiting in this queue until
    // a growth tile frees up; the Queen then plays her spawn animation for spawnDuration and a Child
    // is born, which walks to that tile and grows into the ordered unit there.
    private readonly Queue<SpawnOrder> spawnQueue = new Queue<SpawnOrder>();
    private UnitController[] growthSlots = new UnitController[0];
    private bool[] barrackTaken = new bool[0];
    private SpawnOrder spawningOrder;
    private bool isSpawning;
    private float spawnTimer;

    // A queued unit, plus the barrack that was set aside for it if it's an Attacker. Reserved at
    // order time rather than at birth, so the "is a barrack free?" the player sees is honest even
    // with several Attackers queued up. growthSlot is filled in later, when the birth actually
    // starts and a tile is picked.
    private struct SpawnOrder
    {
        public GameObject prefab;
        public int barrackSlot;
        public int growthSlot;
    }

    public int SpawnCost => spawnCost;
    public int QueenFood => queenFood;
    public BaseOwner Owner => owner;
    public UnitController Queen => queen;

    // Losing every Builder is as terminal as losing the Queen - nobody is left to carry food to
    // her, so the base can never produce again. GameController treats it as a loss for that reason.
    public bool HasLivingBuilder => !buildersLost;

    // How much more food the Queen still needs to work through every order currently queued - what
    // this base's Builders are working toward. Server-only: spawnQueue only exists there.
    public int FoodShortfall => Mathf.Max(0, spawnQueue.Count * spawnCost - queenFood);

    // Once the Queen dies, this base can no longer spawn units and its gatherers give up
    // gathering entirely - see UnitController's queen-alive checks.
    public bool IsQueenAlive => queenAlive;

    // Attackers belonging to this base that haven't already been sent to attack a BotBase - what
    // AttackOrderPopup's slider maxes out at.
    public int AvailableAttackers => UnitController.CountAvailableAttackers(this);

    // An Attacker needs somewhere to live, so ordering one is refused outright while every barrack
    // is spoken for.
    public int BarrackCount => barrackTiles.Count;
    public int FreeBarracks => freeBarracks;

    private Faction UnitFaction => owner == BaseOwner.Host ? Faction.Host : Faction.Client;

    // Placed by netId rather than map position - every base gets its own far-apart slot in a
    // dedicated interior row, regardless of how close together bases happen to be on the map.
    // netId is server-assigned and identical on every peer, so this stays consistent for everyone.
    public Vector3 InteriorCenter => new Vector3(netId * interiorSlotSpacing, interiorRowY, 0f);

    // The room is exactly its tile map, so its footprint follows the layout rather than being
    // dialled in separately. Used to clamp camera panning while inside this base.
    public Vector2 InteriorHalfSize => new Vector2(columns * tileSize * 0.5f, rows * tileSize * 0.5f);

    // Where units warp in and out of the interior - the gap the layout's E tile marks in the wall.
    public Vector3 InteriorExitPoint => hasEntryTile
        ? TileCenter(entryTile)
        : InteriorCenter + new Vector3(0f, InteriorHalfSize.y - tileSize * 0.5f, 0f);

    // The Queen straddles the seam between her two tiles, so this is their shared midpoint rather
    // than the interior's center - everything that used to aim at the middle of the room aims here.
    public Vector3 QueenPoint
    {
        get
        {
            if (queenTiles.Count == 0) return InteriorCenter;

            Vector3 sum = Vector3.zero;
            foreach (Vector2Int tile in queenTiles) sum += TileCenter(tile);
            return sum / queenTiles.Count;
        }
    }

    public float TileSize => tileSize;
    public int GridColumns => columns;
    public int GridRows => rows;
    public Vector3 GridOrigin => InteriorCenter - new Vector3(columns * tileSize * 0.5f, rows * tileSize * 0.5f, 0f);

    public bool InBounds(Vector2Int tile) => tile.x >= 0 && tile.x < columns && tile.y >= 0 && tile.y < rows;

    // Out-of-bounds reads as solid, so anything asking "can I walk here?" about a point outside the
    // room gets told no rather than an index error.
    public TileType TileAt(Vector2Int tile)
    {
        if (layoutTiles == null || !InBounds(tile)) return TileType.Obstacle;

        int index = tile.y * columns + tile.x;

        // Before the synced grid has arrived (or on a peer that never got one), the layout it was
        // seeded from is the same answer.
        if (tileTypes.Count == layoutTiles.Length) return (TileType)tileTypes[index];
        return layoutTiles[index];
    }

    public bool IsWalkable(Vector2Int tile) => TileAt(tile) != TileType.Obstacle;

    public Vector2Int WorldToTile(Vector3 worldPosition)
    {
        Vector3 local = worldPosition - GridOrigin;
        int x = Mathf.Clamp(Mathf.FloorToInt(local.x / tileSize), 0, columns - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(local.y / tileSize), 0, rows - 1);
        return new Vector2Int(x, y);
    }

    public Vector3 TileCenter(Vector2Int tile)
    {
        return GridOrigin + new Vector3((tile.x + 0.5f) * tileSize, (tile.y + 0.5f) * tileSize, 0f);
    }

    public bool ContainsInterior(Vector3 worldPosition)
    {
        Vector3 origin = GridOrigin;
        return worldPosition.x >= origin.x && worldPosition.x <= origin.x + columns * tileSize
            && worldPosition.y >= origin.y && worldPosition.y <= origin.y + rows * tileSize;
    }

    // The route a unit inside this interior should walk to reach `to`, as a list of waypoints ending
    // at the destination - see InteriorPath. Rooms can wall a magazine off behind an obstacle, so a
    // unit has to be routed around rather than aimed straight at where it's going.
    public void FindInteriorPath(Vector3 from, Vector3 to, List<Vector3> waypoints)
    {
        InteriorPath.Find(this, from, to, waypoints);
    }

    // Where a unit actually ends up after trying to move from -> to inside this interior. Obstacles
    // block, and a diagonal blocked on one axis slides along the other so a unit brushing a wall
    // keeps going instead of sticking to it. Routing is InteriorPath's job - this is the backstop
    // that keeps anything nudged off its route (a fight, a corner grazed) out of the walls.
    public Vector3 ResolveMovement(Vector3 from, Vector3 to)
    {
        // Nothing walks out of the room - the only way out is the warp at the entry tile.
        to = ClampToInterior(to);

        if (IsWalkable(WorldToTile(to))) return to;

        Vector3 alongX = new Vector3(to.x, from.y, to.z);
        if (IsWalkable(WorldToTile(alongX))) return alongX;

        Vector3 alongY = new Vector3(from.x, to.y, to.z);
        if (IsWalkable(WorldToTile(alongY))) return alongY;

        return from;
    }

    // Pulls a point onto somewhere a unit can actually stand - used for wander destinations and
    // spawn offsets, so nothing is ever sent walking into a wall it can't reach.
    public Vector3 NearestWalkablePoint(Vector3 position)
    {
        Vector3 clamped = ClampToInterior(position);
        Vector2Int tile = WorldToTile(clamped);
        if (IsWalkable(tile)) return clamped;

        for (int radius = 1; radius <= Mathf.Max(columns, rows); radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    // Only the ring itself - everything inside it was covered by a smaller radius.
                    if (Mathf.Abs(x) != radius && Mathf.Abs(y) != radius) continue;

                    Vector2Int candidate = tile + new Vector2Int(x, y);
                    if (InBounds(candidate) && IsWalkable(candidate)) return TileCenter(candidate);
                }
            }
        }

        return TileCenter(tile);
    }

    public Vector3 ClampToInterior(Vector3 position)
    {
        Vector3 origin = GridOrigin;
        return new Vector3(
            Mathf.Clamp(position.x, origin.x, origin.x + columns * tileSize),
            Mathf.Clamp(position.y, origin.y, origin.y + rows * tileSize),
            position.z);
    }

    // Growth tiles come straight out of the layout's G letters, in grid order.
    public int GrowthTileCount => growthTiles.Count;
    public float ChildIdleTime => childIdleTime;
    public float GrowthTime => growthTime;
    public Vector3 GrowthTileCenter(int slot) => slot >= 0 && slot < growthTiles.Count ? TileCenter(growthTiles[slot]) : QueenPoint;
    public Vector3 BarrackCenter(int slot) => slot >= 0 && slot < barrackTiles.Count ? TileCenter(barrackTiles[slot]) : QueenPoint;

    // How much this base can hold in total: exactly the magazines it has built, times what one of
    // them holds. There is nowhere else to put anything.
    public int StorageCapacity => MagazineCount * magazineCapacity;

    public int StoredResources
    {
        get
        {
            int total = 0;
            foreach (int amount in tileResources) total += amount;
            return total;
        }
    }

    public int MagazineCount
    {
        get
        {
            int count = 0;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    if (IsMagazine(new Vector2Int(x, y))) count++;
                }
            }
            return count;
        }
    }

    public bool IsMagazine(Vector2Int tile) => TileAt(tile) == TileType.Magazine;

    // 0 for anything that isn't a magazine, so a caller never has to check the tile type first.
    public int MagazineAmount(Vector2Int tile)
    {
        if (!IsMagazine(tile) || tileResources.Count != columns * rows) return 0;
        return tileResources[tile.y * columns + tile.x];
    }

    public int MagazineFreeSpace(Vector2Int tile) => IsMagazine(tile) ? Mathf.Max(0, magazineCapacity - MagazineAmount(tile)) : 0;

    // Whether there's room anywhere in the base. A Gatherer with a full load and nowhere to put it
    // waits outside rather than walking a pointless round trip.
    public bool HasStorageSpace
    {
        get
        {
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    if (MagazineFreeSpace(new Vector2Int(x, y)) > 0) return true;
                }
            }
            return false;
        }
    }

    // The magazine a unit standing at `from` should walk to - nearest by the route it would actually
    // walk, so one behind a wall doesn't look closer than it is. False when there's none worth going
    // to: every magazine full (depositing) or every magazine empty (loading up).
    public bool TryFindMagazineWithSpace(Vector3 from, out Vector2Int tile) => InteriorPath.TryFindNearest(this, from, magazineHasSpace, out tile);

    public bool TryFindMagazineWithResources(Vector3 from, out Vector2Int tile) => InteriorPath.TryFindNearest(this, from, magazineHasResources, out tile);

    public bool CanBuildMagazine => tilePrefabs != null && tilePrefabs.magazine != null;

    // The single source of truth for whether a tile can be built on, called client-side for the
    // build preview and server-side again as the real authorization check. A plain floor tile is
    // the only thing that's ever free - everything else already is something.
    public bool IsTileBuildable(Vector2Int tile) => TileAt(tile) == TileType.Floor;

    // Every peer loads the same scene data, so the Host/Client split of "am I the owner"
    // can be read straight off NetworkServer.active - true only for the host's own process.
    public bool IsOwnedByLocalPlayer => NetworkServer.active
        ? owner == BaseOwner.Host
        : owner == BaseOwner.Client;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        selectionCollider = GetComponent<Collider2D>();
        if (spriteRenderer != null) normalColor = spriteRenderer.color;

        magazineHasSpace = tile => MagazineFreeSpace(tile) > 0;
        magazineHasResources = tile => MagazineAmount(tile) > 0;

        // Derived from the prefab's piles rather than dialled in here, so a magazine can never claim
        // to hold more than it's able to show. A base with no magazine prefab still needs a number.
        magazineCapacity = Magazine.CapacityOf(tilePrefabs != null ? tilePrefabs.magazine : null);
        if (magazineCapacity <= 0) magazineCapacity = Magazine.DefaultCapacity;

        ParseLayout();
    }

    // Turns the authored letter grid into the tile array plus the handful of "where is X" lookups
    // the rest of the base needs. Runs on every peer, from prefab data, so all of them agree.
    private void ParseLayout()
    {
        if (!BaseLayout.TryParse(layout, out TileType[] parsed, out int parsedColumns, out int parsedRows, out string error))
        {
            Debug.LogError($"{name}: interior layout is invalid ({error}) - falling back to a single floor tile.", this);
            parsed = new[] { TileType.Floor };
            parsedColumns = 1;
            parsedRows = 1;
        }

        layoutTiles = parsed;
        columns = parsedColumns;
        rows = parsedRows;

        growthTiles.Clear();
        barrackTiles.Clear();
        queenTiles.Clear();
        hasEntryTile = false;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                Vector2Int tile = new Vector2Int(x, y);
                switch (layoutTiles[y * columns + x])
                {
                    case TileType.GrowthTile: growthTiles.Add(tile); break;
                    case TileType.Barrack: barrackTiles.Add(tile); break;
                    case TileType.Queen: queenTiles.Add(tile); break;
                    case TileType.Entry:
                        if (!hasEntryTile)
                        {
                            entryTile = tile;
                            hasEntryTile = true;
                        }
                        break;
                }
            }
        }
    }

    private void OnEnable()
    {
        BaseSelectionManager.Register(this);
    }

    private void OnDisable()
    {
        BaseSelectionManager.Unregister(this);
    }

    private void Update()
    {
        if (isServer) UpdateProduction();

        if (selectionCollider == null || Camera.main == null || Mouse.current == null) return;
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Vector2 worldPoint = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        if (selectionCollider.OverlapPoint(worldPoint))
        {
            ViewManager.EnterBaseView(this);
        }
    }

    public void SetHighlighted(bool highlighted)
    {
        if (spriteRenderer == null) return;
        spriteRenderer.color = highlighted ? highlightColor : normalColor;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Seed the synced grid from the authored layout. Runs before OnStartClient, so the host
        // builds its interior from a grid that's already filled in.
        tileTypes.Clear();
        foreach (TileType type in layoutTiles) tileTypes.Add((byte)type);

        tileResources.Clear();
        for (int i = 0; i < layoutTiles.Length; i++) tileResources.Add(0);
        FillMagazines(startingResources);

        growthSlots = new UnitController[growthTiles.Count];
        barrackTaken = new bool[barrackTiles.Count];
        freeBarracks = barrackTiles.Count;

        SpawnQueen();
        SpawnStartingLoadout();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        tileTypes.Callback += OnTileTypesChanged;
        tileResources.Callback += OnTileResourcesChanged;

        // Building the interior draws each magazine's piles as it goes, from a grid that's already
        // been synced - so there's nothing left to refresh here.
        interior = new BaseInterior(this, tilePrefabs);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        tileTypes.Callback -= OnTileTypesChanged;
        tileResources.Callback -= OnTileResourcesChanged;

        if (interior != null)
        {
            interior.Destroy();
            interior = null;
        }
    }

    // Fires on every peer, the server included - so a build swaps the tile object out everywhere
    // through exactly one code path.
    private void OnTileTypesChanged(SyncList<byte>.Operation op, int index, byte oldType, byte newType)
    {
        if (interior == null || op != SyncList<byte>.Operation.OP_SET) return;

        interior.BuildTile(new Vector2Int(index % columns, index / columns));
    }

    // Only ever one magazine's worth changes at a time, so only that tile's piles are redrawn.
    private void OnTileResourcesChanged(SyncList<int>.Operation op, int index, int oldAmount, int newAmount)
    {
        if (interior == null || op != SyncList<int>.Operation.OP_SET) return;

        interior.ShowMagazineFill(new Vector2Int(index % columns, index / columns));
    }

    // A base doesn't start from nothing: the layout already gives it its magazines, so all that's left
    // is its starting units. Runs after SpawnQueen because their spots are picked around her.
    [Server]
    private void SpawnStartingLoadout()
    {
        if (startingUnitPrefabs == null) return;

        for (int i = 0; i < startingUnitPrefabs.Length; i++)
        {
            if (startingUnitPrefabs[i] == null) continue;
            SpawnGrownUnit(startingUnitPrefabs[i], StartingUnitPosition(i));
        }
    }

    // Spawns a unit straight into its adult life, bypassing the Child -> growth tile pipeline. Only
    // the starting loadout uses this - everything produced during play has to be grown.
    [Server]
    private void SpawnGrownUnit(GameObject prefab, Vector3 position)
    {
        GameObject unitObject = Instantiate(prefab, position, Quaternion.identity);
        UnitController unit = unitObject.GetComponent<UnitController>();
        if (unit != null)
        {
            unit.HomeBase = this;
            unit.Faction = UnitFaction;

            // A starting Attacker still needs somewhere to live, same as an ordered one.
            if (unit.Type == UnitType.Attacker) unit.BarrackSlot = ClaimBarrack();
        }

        NetworkServer.Spawn(unitObject);
    }

    // Floor tiles nearest the Queen, so a starting unit never lands in a wall or on a growth tile.
    [Server]
    private Vector3 StartingUnitPosition(int index)
    {
        List<Vector2Int> floors = new List<Vector2Int>();
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                Vector2Int tile = new Vector2Int(x, y);
                if (TileAt(tile) == TileType.Floor) floors.Add(tile);
            }
        }

        if (floors.Count == 0) return QueenPoint;

        Vector3 queenPoint = QueenPoint;
        floors.Sort((a, b) => Vector3.Distance(TileCenter(a), queenPoint).CompareTo(Vector3.Distance(TileCenter(b), queenPoint)));

        return TileCenter(floors[index % floors.Count]);
    }

    [Server]
    private void SpawnQueen()
    {
        if (queenPrefab == null) return;

        GameObject queenObject = Instantiate(queenPrefab, QueenPoint, Quaternion.identity);
        UnitController controller = queenObject.GetComponent<UnitController>();
        if (controller != null)
        {
            controller.HomeBase = this;
            controller.Faction = UnitFaction;
        }

        NetworkServer.Spawn(queenObject);
        queen = controller;
    }

    // Same authorization rules as CmdRequestSpawn - see its comment.
    [Command(requiresAuthority = false)]
    public void CmdBuildTile(Vector2Int tile, TileType type, NetworkConnectionToClient sender = null)
    {
        bool senderIsHost = sender != null && sender == NetworkServer.localConnection;
        bool authorized = senderIsHost ? owner == BaseOwner.Host : owner == BaseOwner.Client;
        if (!authorized) return;

        ServerBuildTile(tile, type);
    }

    [Server]
    public void ServerBuildTile(Vector2Int tile, TileType type)
    {
        // A magazine is the only thing that can be put up during play. Growth tiles and barracks are
        // counted once at startup, so letting one appear later would leave the server's slot
        // bookkeeping out of step with the grid.
        if (type != TileType.Magazine) return;
        if (!IsTileBuildable(tile)) return;

        tileTypes[tile.y * columns + tile.x] = (byte)type;
    }

    // Called by our own Queen's UnitController when its HP reaches 0.
    [Server]
    public void OnQueenKilled()
    {
        queenAlive = false;

        // Nobody left to give birth - drop the backlog and abandon the spawn in progress. Children
        // already out on a growth tile are real units in the world, so they're left to finish.
        while (spawnQueue.Count > 0) ReleaseBarrack(spawnQueue.Dequeue().barrackSlot);
        CancelSpawnInProgress();
    }

    // Called by one of this base's Builders when it dies. Only ever flips the flag when the last one
    // is gone - and never back, because a base with no Builder can't produce the replacement.
    [Server]
    public void OnBuilderKilled()
    {
        if (buildersLost) return;
        if (UnitController.CountAlive(this, UnitType.Builder) > 0) return;

        buildersLost = true;
    }

    // Puts as much of `amount` into one magazine as it has room for and returns how much went in -
    // a Gatherer carrying more than the magazine can take leaves the rest on its back and walks to
    // another one. Two Gatherers arriving at the same magazine sort themselves out through exactly
    // this: whoever gets there second is told how little (or nothing) fit.
    [Server]
    public int DepositResourceAt(Vector2Int tile, int amount)
    {
        if (amount <= 0) return 0;

        int stored = Mathf.Min(amount, MagazineFreeSpace(tile));
        if (stored <= 0) return 0;

        tileResources[tile.y * columns + tile.x] += stored;
        return stored;
    }

    // Taken by a Builder loading up at one magazine. What it's carrying is out of storage from here
    // on, so a Builder that dies before reaching the Queen loses its load.
    [Server]
    public int WithdrawForFeedingAt(Vector2Int tile, int amount)
    {
        int taken = Mathf.Min(amount, MagazineAmount(tile));
        if (taken <= 0) return 0;

        tileResources[tile.y * columns + tile.x] -= taken;
        return taken;
    }

    // Deals resources out over the magazines in grid order, each filled to its limit before the next
    // gets anything. Only the starting stock goes in this way - everything after it arrives on a
    // Gatherer's back, one magazine at a time.
    [Server]
    private void FillMagazines(int amount)
    {
        for (int y = 0; y < rows && amount > 0; y++)
        {
            for (int x = 0; x < columns && amount > 0; x++)
            {
                amount -= DepositResourceAt(new Vector2Int(x, y), amount);
            }
        }
    }

    // Called by a Builder that just delivered a load to the Queen. Overshooting the current orders
    // is fine - the surplus banks toward whatever is ordered next.
    [Server]
    public void FeedQueen(int amount)
    {
        if (amount <= 0) return;

        queenFood += amount;
    }

    // Any client can call this, but the server only honors it for whichever side (Host or the
    // remote Client) actually owns this base - the sender can't spawn units for the other side.
    [Command(requiresAuthority = false)]
    public void CmdRequestSpawn(NetworkConnectionToClient sender = null)
    {
        bool senderIsHost = sender != null && sender == NetworkServer.localConnection;
        bool authorized = senderIsHost ? owner == BaseOwner.Host : owner == BaseOwner.Client;
        if (!authorized) return;

        ServerTrySpawn();
    }

    // Ordering is free and instant - the resource cost is paid later, in food, by whichever Builder
    // carries it to the Queen. An order the base can't currently afford simply waits in the queue.
    [Server]
    public void ServerTrySpawn()
    {
        if (!queenAlive) return;
        if (unitPrefabs == null || unitPrefabs.Length == 0) return;

        GameObject unitPrefab = unitPrefabs[unitIndex];
        unitIndex = (unitIndex + 1) % unitPrefabs.Length;

        spawnQueue.Enqueue(new SpawnOrder { prefab = unitPrefab, barrackSlot = -1 });
    }

    // Same authorization rules as CmdRequestSpawn - see its comment.
    [Command(requiresAuthority = false)]
    public void CmdRequestSpawnAttacker(NetworkConnectionToClient sender = null)
    {
        bool senderIsHost = sender != null && sender == NetworkServer.localConnection;
        bool authorized = senderIsHost ? owner == BaseOwner.Host : owner == BaseOwner.Client;
        if (!authorized) return;

        ServerTrySpawnAttacker();
    }

    [Server]
    public void ServerTrySpawnAttacker()
    {
        if (!queenAlive) return;
        if (attackerPrefab == null) return;

        // An Attacker lives in a barrack, so one is set aside the moment it's ordered - with every
        // barrack spoken for there's nowhere to put it and the order is refused outright.
        int slot = ClaimBarrack();
        if (slot < 0) return;

        spawnQueue.Enqueue(new SpawnOrder { prefab = attackerPrefab, barrackSlot = slot });
    }

    [Server]
    private int ClaimBarrack()
    {
        for (int slot = 0; slot < barrackTaken.Length; slot++)
        {
            if (barrackTaken[slot]) continue;

            barrackTaken[slot] = true;
            freeBarracks--;
            return slot;
        }

        return -1;
    }

    // Called when an Attacker dies, or when the order that reserved a barrack is abandoned.
    [Server]
    public void ReleaseBarrack(int slot)
    {
        if (slot < 0 || slot >= barrackTaken.Length || !barrackTaken[slot]) return;

        barrackTaken[slot] = false;
        freeBarracks++;
    }

    // Drives the one-Child-at-a-time production line: pull the next order off the queue once the
    // Queen has been fed enough and a growth tile is free, have her play her spawn animation for
    // spawnDuration, then put a Child into the world.
    [Server]
    private void UpdateProduction()
    {
        if (isSpawning)
        {
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f) FinishSpawn();
            return;
        }

        if (spawnQueue.Count == 0 || childPrefab == null) return;

        // The Queen can't give birth on an empty stomach - the order waits until a Builder has
        // carried her spawnCost worth of food.
        if (queenFood < spawnCost) return;

        int slot = FreeGrowthSlot();
        // Every growth tile is taken - the order simply stays queued until one frees up.
        if (slot < 0) return;

        queenFood -= spawnCost;
        spawningOrder = spawnQueue.Dequeue();
        spawningOrder.growthSlot = slot;
        isSpawning = true;
        spawnTimer = spawnDuration;
        if (queen != null) queen.SetSpawning(true);
    }

    [Server]
    private void FinishSpawn()
    {
        if (queen != null) queen.SetSpawning(false);

        SpawnOrder order = spawningOrder;
        isSpawning = false;
        spawningOrder = default;

        Vector3 birthPoint = NearestWalkablePoint(QueenPoint + (Vector3)childSpawnOffset);
        GameObject childObject = Instantiate(childPrefab, birthPoint, Quaternion.identity);
        UnitController child = childObject.GetComponent<UnitController>();
        if (child == null)
        {
            Destroy(childObject);
            ReleaseBarrack(order.barrackSlot);
            return;
        }

        child.HomeBase = this;
        child.Faction = UnitFaction;
        child.GrowsIntoPrefab = order.prefab;
        child.GrowthSlot = order.growthSlot;
        child.BarrackSlot = order.barrackSlot;

        NetworkServer.Spawn(childObject);
        growthSlots[order.growthSlot] = child;
    }

    [Server]
    private void CancelSpawnInProgress()
    {
        if (!isSpawning) return;

        isSpawning = false;
        if (spawningOrder.growthSlot >= 0) growthSlots[spawningOrder.growthSlot] = null;
        ReleaseBarrack(spawningOrder.barrackSlot);
        spawningOrder = default;
        if (queen != null) queen.SetSpawning(false);
    }

    // A slot is taken from the moment its Child is ordered up (so two births can't race for the
    // same tile) until that Child grows up or dies. Children are runtime-spawned, so a destroyed
    // one really does go null here - unlike the scene-placed Resource/BotBase objects.
    [Server]
    private int FreeGrowthSlot()
    {
        for (int slot = 0; slot < growthSlots.Length; slot++)
        {
            if (growthSlots[slot] == null) return slot;
        }
        return -1;
    }

    // Called by a Child once it's waited out childIdleTime: it's replaced, in place, by the unit it
    // was ordered as. That unit inherits the same growth tile and holds it while it plays its
    // growth animation there, handing it back itself once it's fully grown. The barrack the order
    // reserved (if any) travels with it too.
    [Server]
    public void CompleteGrowth(UnitController child)
    {
        int slot = child.GrowthSlot;
        UnitController grown = null;

        if (child.GrowsIntoPrefab != null)
        {
            GameObject unit = Instantiate(child.GrowsIntoPrefab, child.transform.position, Quaternion.identity);
            grown = unit.GetComponent<UnitController>();
            if (grown != null)
            {
                grown.HomeBase = this;
                grown.Faction = UnitFaction;
                grown.GrowthSlot = slot;
                grown.BarrackSlot = child.BarrackSlot;
            }

            NetworkServer.Spawn(unit);
        }

        // Handed over to the grown unit - the Child mustn't give it back when it's destroyed below.
        child.BarrackSlot = -1;

        if (slot >= 0 && slot < growthSlots.Length) growthSlots[slot] = grown;

        NetworkServer.Destroy(child.gameObject);
    }

    // Called when a Child dies before it finished growing - frees its tile for the next order.
    [Server]
    public void ReleaseGrowthSlot(UnitController child)
    {
        for (int slot = 0; slot < growthSlots.Length; slot++)
        {
            if (growthSlots[slot] == child) growthSlots[slot] = null;
        }
    }

    // Same authorization rules as CmdRequestSpawn - see its comment.
    [Command(requiresAuthority = false)]
    public void CmdRequestSpawnBuilder(NetworkConnectionToClient sender = null)
    {
        bool senderIsHost = sender != null && sender == NetworkServer.localConnection;
        bool authorized = senderIsHost ? owner == BaseOwner.Host : owner == BaseOwner.Client;
        if (!authorized) return;

        ServerTrySpawnBuilder();
    }

    // Builders have to be replaceable: losing the last one loses the base, so a player who's down to
    // one needs a way to order another before it dies. Ordering costs the same food as anything else.
    [Server]
    public void ServerTrySpawnBuilder()
    {
        if (!queenAlive) return;
        if (builderPrefab == null) return;

        spawnQueue.Enqueue(new SpawnOrder { prefab = builderPrefab, barrackSlot = -1 });
    }

    // Same authorization rules as CmdRequestSpawn - see its comment.
    [Command(requiresAuthority = false)]
    public void CmdOrderAttack(BotBase target, int count, NetworkConnectionToClient sender = null)
    {
        bool senderIsHost = sender != null && sender == NetworkServer.localConnection;
        bool authorized = senderIsHost ? owner == BaseOwner.Host : owner == BaseOwner.Client;
        if (!authorized || target == null) return;

        ServerOrderAttack(target, count);
    }

    [Server]
    public void ServerOrderAttack(BotBase target, int count)
    {
        if (target == null || count <= 0) return;

        UnitController.SendAttackers(this, target, count);
    }
}
