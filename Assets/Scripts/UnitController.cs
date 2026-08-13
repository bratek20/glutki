using UnityEngine;
using Mirror;

[RequireComponent(typeof(Animator))]
public class UnitController : NetworkBehaviour
{
    // Number of units currently spawned as seen by this peer (host/client). Used by the HUD.
    public static int ActiveUnitCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveUnitCount()
    {
        ActiveUnitCount = 0;
    }

    private enum UnitState
    {
        ExitingBase,
        Wandering,
        SeekingResource,
        ReturningToBase,
        EnteringBase
    }

    [SerializeField] private UnitType unitType = UnitType.Gatherer;
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float stoppingDistance = 0.05f;
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("Wandering")]
    [SerializeField] private float wanderMinDistance = 1.5f;
    [SerializeField] private float wanderMaxDistance = 5f;

    [Header("Gathering (Gatherer only)")]
    [SerializeField] private float resourceDetectionRadius = 3f;
    [SerializeField] private float resourceScanInterval = 0.2f;
    [SerializeField] private Color carryingTintColor = new Color(1f, 0.85f, 0.35f);

    [Header("Debug")]
    [Tooltip("Trigger-only collider kept in sync with resourceDetectionRadius, purely so the detection range is visible as a gizmo in the Scene view.")]
    [SerializeField] private CircleCollider2D detectionRadiusGizmo;

    public Base HomeBase { get; set; }

    private Vector3 targetPosition;
    private bool hasTarget;
    private UnitState state = UnitState.ExitingBase;
    private Resource targetResource;
    private float resourceScanTimer;
    private int carriedAmount;

    // Replicates movement state from Server to all connected Clients
    [SyncVar] private bool isWalkingServer;

    // Replicates facing direction from Server to all connected Clients. The base sprite/animation
    // faces right, so this flips the renderer horizontally while moving left.
    [SyncVar] private bool facingLeftServer;

    // Replicates whether this gatherer is currently carrying a resource, for the carry tint
    [SyncVar(hook = nameof(OnCarryingChanged))] private bool isCarryingResource;

    private void Awake()
    {
        SyncDetectionRadiusGizmo();
    }

    private void OnValidate()
    {
        SyncDetectionRadiusGizmo();
    }

    private void SyncDetectionRadiusGizmo()
    {
        if (detectionRadiusGizmo == null) return;
        detectionRadiusGizmo.radius = resourceDetectionRadius;
        detectionRadiusGizmo.enabled = unitType == UnitType.Gatherer;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ActiveUnitCount++;
        if (animator == null) animator = GetComponent<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        ActiveUnitCount--;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // The Queen is a permanent fixture at the center of the base interior - it never moves.
        if (unitType == UnitType.Queen) return;

        // Every other unit is spawned inside its home base's interior and has to walk out to the
        // exit before it can be seen on the world map.
        state = UnitState.ExitingBase;
        SetTarget(HomeBase != null ? HomeBase.InteriorExitPoint : transform.position);
    }

    private void Update()
    {
        // 1. Visually update local Animator/SpriteRenderer on Host & Clients
        if (animator != null)
        {
            animator.SetBool("IsWalking", isWalkingServer);
        }
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = facingLeftServer;
        }

        // 2. Server-only position and state evaluation
        if (!isServer) return;
        if (unitType == UnitType.Queen) return;

        if (unitType == UnitType.Gatherer && (state == UnitState.Wandering || state == UnitState.SeekingResource))
        {
            ScanForResource();
        }

        UpdateServerMovement();
    }

    [Server]
    private void ScanForResource()
    {
        // Already chasing a still-valid resource - no need to rescan yet.
        if (state == UnitState.SeekingResource && targetResource != null) return;

        resourceScanTimer -= Time.deltaTime;
        if (resourceScanTimer > 0f) return;
        resourceScanTimer = resourceScanInterval;

        Resource nearest = FindNearestResource();
        if (nearest == null) return;

        targetResource = nearest;
        SetTarget(nearest.transform.position);
        state = UnitState.SeekingResource;
    }

    [Server]
    private Resource FindNearestResource()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, resourceDetectionRadius);
        Resource nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (Collider2D hit in hits)
        {
            Resource resource = hit.GetComponent<Resource>();
            if (resource == null) continue;

            float distance = Vector3.Distance(transform.position, resource.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = resource;
            }
        }

        return nearest;
    }

    [Server]
    private void UpdateServerMovement()
    {
        if (!hasTarget) return;

        transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPosition) <= stoppingDistance)
        {
            isWalkingServer = false;
            hasTarget = false;
            OnTargetReached();
        }
        else
        {
            isWalkingServer = true;
        }
    }

    [Server]
    private void OnTargetReached()
    {
        switch (state)
        {
            case UnitState.ExitingBase:
                OnExitReached();
                break;
            case UnitState.SeekingResource:
                OnResourceReached();
                break;
            case UnitState.ReturningToBase:
                OnBaseReached();
                break;
            case UnitState.EnteringBase:
                OnEntryReached();
                break;
            default:
                Invoke(nameof(StartWandering), Random.Range(1f, 3f));
                break;
        }
    }

    [Server]
    private void OnExitReached()
    {
        // Warp from the base interior out onto the world map, at the base's actual position.
        transform.position = HomeBase != null ? HomeBase.transform.position : transform.position;
        StartWandering();
    }

    [Server]
    private void OnResourceReached()
    {
        // The resource may have been consumed by another gatherer while we were en route.
        if (targetResource == null)
        {
            StartWandering();
            return;
        }

        carriedAmount = targetResource.Amount;
        targetResource.Consume();
        targetResource = null;
        isCarryingResource = true;

        state = UnitState.ReturningToBase;
        SetTarget(HomeBase != null ? HomeBase.transform.position : transform.position);
    }

    [Server]
    private void OnBaseReached()
    {
        // Warp from the world map into the base interior, entering through the same opening units exit from.
        Vector3 entryPoint = HomeBase != null ? HomeBase.InteriorExitPoint : transform.position;
        transform.position = entryPoint;

        state = UnitState.EnteringBase;
        SetTarget(HomeBase != null ? HomeBase.InteriorCenter : entryPoint);
    }

    [Server]
    private void OnEntryReached()
    {
        // Reached the queen/depot point inside the base - deposit, then head back out.
        if (HomeBase != null)
        {
            HomeBase.DepositResource(carriedAmount);
        }

        carriedAmount = 0;
        isCarryingResource = false;

        state = UnitState.ExitingBase;
        SetTarget(HomeBase != null ? HomeBase.InteriorExitPoint : transform.position);
    }

    [Server]
    private void StartWandering()
    {
        state = UnitState.Wandering;

        float angle = Random.Range(0f, Mathf.PI * 2f);
        Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        float distance = Random.Range(wanderMinDistance, wanderMaxDistance);

        SetTarget(transform.position + new Vector3(direction.x, direction.y, 0f) * distance);
    }

    [Server]
    private void SetTarget(Vector3 position)
    {
        targetPosition = position;
        hasTarget = true;

        float deltaX = targetPosition.x - transform.position.x;
        if (!Mathf.Approximately(deltaX, 0f))
        {
            facingLeftServer = deltaX < 0f;
        }
    }

    private void OnCarryingChanged(bool oldValue, bool newValue)
    {
        if (spriteRenderer == null) return;
        spriteRenderer.color = newValue ? carryingTintColor : Color.white;
    }
}
