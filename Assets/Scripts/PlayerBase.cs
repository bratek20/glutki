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
    [SerializeField] private GameObject resourceStockPrefab;
    [SerializeField] private GameObject childPrefab;
    [SerializeField] private int spawnCost = 1;
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.3f);
    [SerializeField] private BaseOwner owner = BaseOwner.Host;

    [Header("Base View interior")]
    [Tooltip("Half-size of the interior room, roughly 2x a screen's worth of view. Also used to clamp camera panning while inside this base.")]
    [SerializeField] private Vector2 interiorHalfSize = new Vector2(18f, 10f);
    [Tooltip("Distance between one base's interior slot and the next. Must comfortably exceed interiorHalfSize.x * 2 so no two interiors are ever visible at once, regardless of how close the bases are on the map.")]
    [SerializeField] private float interiorSlotSpacing = 500f;
    [Tooltip("How far below the map the whole row of interiors sits.")]
    [SerializeField] private float interiorRowY = -1000f;

    [Header("Build Grid")]
    [Tooltip("Size (world units) of one buildable tile inside this base's interior.")]
    [SerializeField] private float tileSize = 1f;

    [Header("Unit Growth")]
    [Tooltip("How long the Queen plays her spawn animation before the Child she's producing actually appears.")]
    [SerializeField] private float spawnDuration = 1.5f;
    [Tooltip("Where a freshly produced Child appears, relative to the Queen. Line this up with the point the spawn animation 'drops' it.")]
    [SerializeField] private Vector2 childSpawnOffset = new Vector2(0.6f, -0.6f);
    [Tooltip("How many grid tiles directly to the right of the Queen's tile are growth tiles. Each holds one growing Child at a time; while they're all taken the Queen can't start another spawn and orders just queue up.")]
    [SerializeField] private int growthTileCount = 2;
    [Tooltip("How long a Child sits on its growth tile before it becomes the unit it was ordered as.")]
    [SerializeField] private float growthTime = 5f;

    [SyncVar] private int storedResources = 5;
    [SyncVar] private UnitController queen;
    [SyncVar] private bool queenAlive = true;

    private SpriteRenderer spriteRenderer;
    private Collider2D selectionCollider;
    private Color normalColor;
    private int unitIndex;

    // Server-only production state. A spawn order is only ever a prefab waiting in this queue until
    // a growth tile frees up; the Queen then plays her spawn animation for spawnDuration and a Child
    // is born, which walks to that tile and grows into the ordered unit there.
    private readonly Queue<GameObject> spawnQueue = new Queue<GameObject>();
    private UnitController[] growthSlots = new UnitController[0];
    private GameObject spawningPrefab;
    private int spawningSlot = -1;
    private float spawnTimer;

    public int StoredResources => storedResources;
    public int SpawnCost => spawnCost;
    public BaseOwner Owner => owner;
    public UnitController Queen => queen;
    public bool HasResourceStockPrefab => resourceStockPrefab != null;

    // Closest of this base's built ResourceStocks to position, or null if it has none built yet.
    public ResourceStock NearestResourceStock(Vector3 position) => ResourceStock.Nearest(this, position);

    // Once the Queen dies, this base can no longer spawn units and its gatherers give up
    // gathering entirely - see UnitController's queen-alive checks.
    public bool IsQueenAlive => queenAlive;

    // Attackers belonging to this base that haven't already been sent to attack a BotBase - what
    // AttackOrderPopup's slider maxes out at.
    public int AvailableAttackers => UnitController.CountAvailableAttackers(this);

    private Faction UnitFaction => owner == BaseOwner.Host ? Faction.Host : Faction.Client;

    // Placed by netId rather than map position - every base gets its own far-apart slot in a
    // dedicated interior row, regardless of how close together bases happen to be on the map.
    // netId is server-assigned and identical on every peer, so this stays consistent for everyone.
    public Vector3 InteriorCenter => new Vector3(netId * interiorSlotSpacing, interiorRowY, 0f);
    public Vector2 InteriorHalfSize => interiorHalfSize;
    public Vector3 InteriorExitPoint => InteriorCenter + new Vector3(0f, interiorHalfSize.y, 0f);

    // The base's interior is tiled by a simple N x M grid of buildable tiles, all the same size,
    // fully covering the interior room and centered on InteriorCenter.
    public float TileSize => tileSize;
    public int GridColumns => Mathf.Max(1, Mathf.FloorToInt((interiorHalfSize.x * 2f) / tileSize));
    public int GridRows => Mathf.Max(1, Mathf.FloorToInt((interiorHalfSize.y * 2f) / tileSize));
    public Vector3 GridOrigin => InteriorCenter - new Vector3(GridColumns * tileSize * 0.5f, GridRows * tileSize * 0.5f, 0f);

    public Vector2Int WorldToTile(Vector3 worldPosition)
    {
        Vector3 local = worldPosition - GridOrigin;
        int x = Mathf.Clamp(Mathf.FloorToInt(local.x / tileSize), 0, GridColumns - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(local.y / tileSize), 0, GridRows - 1);
        return new Vector2Int(x, y);
    }

    public Vector3 TileCenter(Vector2Int tile)
    {
        return GridOrigin + new Vector3((tile.x + 0.5f) * tileSize, (tile.y + 0.5f) * tileSize, 0f);
    }

    // Growth tiles are just the run of ordinary grid tiles immediately to the right of the Queen's
    // own tile - nothing is built on them, they're the spots Children stand on while growing.
    public int GrowthTileCount => Mathf.Max(0, growthTileCount);
    public float GrowthTime => growthTime;
    public Vector2Int GrowthTile(int slot) => WorldToTile(InteriorCenter) + new Vector2Int(slot + 1, 0);
    public Vector3 GrowthTileCenter(int slot) => TileCenter(GrowthTile(slot));

    public bool IsGrowthTile(Vector2Int tile)
    {
        for (int slot = 0; slot < GrowthTileCount; slot++)
        {
            if (GrowthTile(slot) == tile) return true;
        }
        return false;
    }

    // Safe to call on any peer (build-mode preview) as well as the server (authoritative check
    // before actually building) - both need exactly the same rule.
    public bool IsTileBuildable(Vector2Int tile)
    {
        if (!HasResourceStockPrefab) return false;
        if (tile.x < 0 || tile.x >= GridColumns || tile.y < 0 || tile.y >= GridRows) return false;

        // Don't let a build sit right on top of the Queen parked at InteriorCenter.
        if (queen != null && Vector3.Distance(TileCenter(tile), queen.transform.position) < tileSize * 0.5f) return false;

        // Growth tiles are reserved for Children - a building there would block unit production.
        if (IsGrowthTile(tile)) return false;

        if (ResourceStock.AnyOccupiesTile(this, tile)) return false;

        return true;
    }

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
        growthSlots = new UnitController[GrowthTileCount];
        SpawnQueen();
    }

    [Server]
    private void SpawnQueen()
    {
        if (queenPrefab == null) return;

        GameObject queenObject = Instantiate(queenPrefab, InteriorCenter, Quaternion.identity);
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
    public void CmdBuildResourceStock(Vector2Int tile, NetworkConnectionToClient sender = null)
    {
        bool senderIsHost = sender != null && sender == NetworkServer.localConnection;
        bool authorized = senderIsHost ? owner == BaseOwner.Host : owner == BaseOwner.Client;
        if (!authorized) return;

        ServerBuildResourceStock(tile);
    }

    [Server]
    public void ServerBuildResourceStock(Vector2Int tile)
    {
        if (!IsTileBuildable(tile)) return;

        GameObject stockObject = Instantiate(resourceStockPrefab, TileCenter(tile), Quaternion.identity);
        ResourceStock stock = stockObject.GetComponent<ResourceStock>();
        if (stock != null) stock.HomeBase = this;

        NetworkServer.Spawn(stockObject);
    }

    // Called by our own Queen's UnitController when its HP reaches 0.
    [Server]
    public void OnQueenKilled()
    {
        queenAlive = false;

        // Nobody left to give birth - drop the backlog and abandon the spawn in progress. Children
        // already out on a growth tile are real units in the world, so they're left to finish.
        spawnQueue.Clear();
        CancelSpawnInProgress();
    }

    [Server]
    public void DepositResource(int resourceAmount)
    {
        storedResources += resourceAmount;
        Debug.Log($"Base now holds {storedResources} resource(s).");
    }

    [Server]
    public bool TrySpendResource(int amount)
    {
        if (storedResources < amount) return false;

        storedResources -= amount;
        return true;
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

    [Server]
    public void ServerTrySpawn()
    {
        if (!queenAlive) return;
        if (unitPrefabs == null || unitPrefabs.Length == 0) return;
        if (!TrySpendResource(spawnCost)) return;

        GameObject unitPrefab = unitPrefabs[unitIndex];
        unitIndex = (unitIndex + 1) % unitPrefabs.Length;

        spawnQueue.Enqueue(unitPrefab);
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
        if (!TrySpendResource(spawnCost)) return;

        spawnQueue.Enqueue(attackerPrefab);
    }

    // Drives the one-Child-at-a-time production line: pull the next order off the queue as soon as
    // a growth tile is free, have the Queen play her spawn animation for spawnDuration, then put a
    // Child into the world.
    [Server]
    private void UpdateProduction()
    {
        if (spawningPrefab != null)
        {
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f) FinishSpawn();
            return;
        }

        if (spawnQueue.Count == 0 || childPrefab == null) return;

        int slot = FreeGrowthSlot();
        // Every growth tile is taken - the order simply stays queued until one frees up.
        if (slot < 0) return;

        spawningSlot = slot;
        spawningPrefab = spawnQueue.Dequeue();
        spawnTimer = spawnDuration;
        if (queen != null) queen.SetSpawning(true);
    }

    [Server]
    private void FinishSpawn()
    {
        if (queen != null) queen.SetSpawning(false);

        GameObject grownPrefab = spawningPrefab;
        int slot = spawningSlot;
        spawningPrefab = null;
        spawningSlot = -1;

        GameObject childObject = Instantiate(childPrefab, InteriorCenter + (Vector3)childSpawnOffset, Quaternion.identity);
        UnitController child = childObject.GetComponent<UnitController>();
        if (child == null)
        {
            Destroy(childObject);
            return;
        }

        child.HomeBase = this;
        child.Faction = UnitFaction;
        child.GrowsIntoPrefab = grownPrefab;
        child.GrowthSlot = slot;

        NetworkServer.Spawn(childObject);
        growthSlots[slot] = child;
    }

    [Server]
    private void CancelSpawnInProgress()
    {
        if (spawningPrefab == null) return;

        spawningPrefab = null;
        if (spawningSlot >= 0) growthSlots[spawningSlot] = null;
        spawningSlot = -1;
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

    // Called by a Child once its growth timer runs out: it's replaced, in place, by the unit it was
    // ordered as, which then starts its own normal life (walking out to gather, or standing guard).
    [Server]
    public void CompleteGrowth(UnitController child)
    {
        ReleaseGrowthSlot(child);

        if (child.GrowsIntoPrefab != null)
        {
            GameObject unit = Instantiate(child.GrowsIntoPrefab, child.transform.position, Quaternion.identity);
            UnitController controller = unit.GetComponent<UnitController>();
            if (controller != null)
            {
                controller.HomeBase = this;
                controller.Faction = UnitFaction;
            }

            NetworkServer.Spawn(unit);
        }

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
