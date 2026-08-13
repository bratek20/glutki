using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class Base : NetworkBehaviour
{
    [SerializeField] private GameObject[] unitPrefabs;
    [SerializeField] private GameObject queenPrefab;
    [SerializeField] private int spawnCost = 1;
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.3f);
    [SerializeField] private BaseOwner owner = BaseOwner.Host;

    [Header("Base View interior")]
    [Tooltip("Where this base's interior (Queen, exit point, wandering-in units) sits in world space, relative to the base itself. Offset far away so interiors never overlap the map or each other.")]
    [SerializeField] private Vector3 interiorOffset = new Vector3(0f, -100f, 0f);
    [Tooltip("Half-size of the interior room, roughly 2x a screen's worth of view. Also used to clamp camera panning while inside this base.")]
    [SerializeField] private Vector2 interiorHalfSize = new Vector2(18f, 10f);

    [SyncVar] private int storedResources = 5;

    private SpriteRenderer spriteRenderer;
    private Collider2D selectionCollider;
    private Color normalColor;
    private int unitIndex;

    public int StoredResources => storedResources;
    public int SpawnCost => spawnCost;
    public BaseOwner Owner => owner;

    public Vector3 InteriorCenter => transform.position + interiorOffset;
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

        GameObject queen = Instantiate(queenPrefab, InteriorCenter, Quaternion.identity);
        UnitController controller = queen.GetComponent<UnitController>();
        if (controller != null) controller.HomeBase = this;

        NetworkServer.Spawn(queen);
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
        if (controller != null) controller.HomeBase = this;

        NetworkServer.Spawn(unit);
    }
}
