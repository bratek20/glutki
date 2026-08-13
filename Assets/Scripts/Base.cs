using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class Base : NetworkBehaviour
{
    [SerializeField] private GameObject[] unitPrefabs;
    [SerializeField] private int spawnCost = 1;
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.3f);

    [SyncVar] private int storedResources = 5;

    private SpriteRenderer spriteRenderer;
    private Collider2D selectionCollider;
    private Color normalColor;
    private int unitIndex;

    public int StoredResources => storedResources;
    public int SpawnCost => spawnCost;

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
            BaseSelectionManager.Select(this);
        }
    }

    public void SetHighlighted(bool highlighted)
    {
        if (spriteRenderer == null) return;
        spriteRenderer.color = highlighted ? highlightColor : normalColor;
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

    // Any client can request a spawn from this base - the server is the sole authority on whether it's affordable.
    [Command(requiresAuthority = false)]
    public void CmdRequestSpawn()
    {
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

        GameObject unit = Instantiate(unitPrefab, transform.position, Quaternion.identity);
        SlimeController controller = unit.GetComponent<SlimeController>();
        if (controller != null) controller.HomeBase = this;

        NetworkServer.Spawn(unit);
    }
}
