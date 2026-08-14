using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class PlayerBase : NetworkBehaviour
{
    [SerializeField] private GameObject[] unitPrefabs;
    [SerializeField] private GameObject queenPrefab;
    [SerializeField] private GameObject attackerPrefab;
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

    [SyncVar] private int storedResources = 5;
    [SyncVar] private UnitController queen;
    [SyncVar] private bool queenAlive = true;

    private SpriteRenderer spriteRenderer;
    private Collider2D selectionCollider;
    private Color normalColor;
    private int unitIndex;

    public int StoredResources => storedResources;
    public int SpawnCost => spawnCost;
    public BaseOwner Owner => owner;
    public UnitController Queen => queen;

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

    // Called by our own Queen's UnitController when its HP reaches 0.
    [Server]
    public void OnQueenKilled()
    {
        queenAlive = false;
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
        if (!TrySpendResource(spawnCost)) return;

        SpawnUnit();
    }

    [Server]
    private void SpawnUnit()
    {
        if (unitPrefabs == null || unitPrefabs.Length == 0) return;

        GameObject unitPrefab = unitPrefabs[unitIndex];
        unitIndex = (unitIndex + 1) % unitPrefabs.Length;

        // Units spawn inside the base interior and have to walk out to the exit before they
        // appear on the world map - see UnitController's ExitingBase state.
        GameObject unit = Instantiate(unitPrefab, InteriorCenter, Quaternion.identity);
        UnitController controller = unit.GetComponent<UnitController>();
        if (controller != null)
        {
            controller.HomeBase = this;
            controller.Faction = UnitFaction;
        }

        NetworkServer.Spawn(unit);
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

        SpawnAttacker();
    }

    [Server]
    private void SpawnAttacker()
    {
        GameObject unit = Instantiate(attackerPrefab, InteriorCenter, Quaternion.identity);
        UnitController controller = unit.GetComponent<UnitController>();
        if (controller != null)
        {
            controller.HomeBase = this;
            controller.Faction = UnitFaction;
        }

        NetworkServer.Spawn(unit);
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
