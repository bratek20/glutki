using System.Collections.Generic;
using UnityEngine;
using Mirror;

[RequireComponent(typeof(Animator))]
public class UnitController : NetworkBehaviour
{
    // Units currently spawned as seen by this peer (host/client). Used by the HUD to count
    // gatherers/attackers per base.
    private static readonly List<UnitController> activeUnits = new List<UnitController>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveUnits()
    {
        activeUnits.Clear();
    }

    public static int CountActive(PlayerBase homeBase, UnitType type)
    {
        int count = 0;
        foreach (UnitController unit in activeUnits)
        {
            if (unit.HomeBase == homeBase && unit.unitType == type) count++;
        }
        return count;
    }

    // Attackers belonging to homeBase that haven't already been sent to attack a BotBase.
    public static int CountAvailableAttackers(PlayerBase homeBase)
    {
        int count = 0;
        foreach (UnitController unit in activeUnits)
        {
            if (unit.HomeBase == homeBase && unit.unitType == UnitType.Attacker && unit.AttackTargetBotBase == null) count++;
        }
        return count;
    }

    // Orders up to count available Attackers belonging to homeBase to march on target.
    public static void SendAttackers(PlayerBase homeBase, BotBase target, int count)
    {
        int sent = 0;
        foreach (UnitController unit in activeUnits)
        {
            if (sent >= count) break;
            if (unit.HomeBase != homeBase || unit.unitType != UnitType.Attacker || unit.AttackTargetBotBase != null) continue;

            unit.OrderAttack(target);
            sent++;
        }
    }

    private enum UnitState
    {
        ExitingBase,
        Wandering,
        SeekingResource,
        Gathering,
        ReturningToBase,
        EnteringBase,
        MarchingToBase,
        MarchingToQueen,
        Guarding,
        MarchingToBotBase,
        AttackingBotBase,
        ReturningToGuard
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
    [Tooltip("How long a Gatherer stands at a resource (playing the attack animation) before it's actually consumed.")]
    [SerializeField] private float gatherDuration = 1.5f;
    [SerializeField] private Color carryingTintColor = new Color(1f, 0.85f, 0.35f);

    [Header("Attacker Guard")]
    [Tooltip("How far from the home base's interior center a freshly spawned Attacker parks itself while waiting for an attack order.")]
    [SerializeField] private float guardRadius = 1.5f;

    [Header("Combat")]
    [Tooltip("Aggressive units actively hunt down enemies within aggroRadius and chase them. Non-aggressive units only fight back if something attacks them, and won't chase.")]
    [SerializeField] private bool isAggressive;
    [SerializeField] private int maxHealth = 10;
    [SerializeField] private int attackDamage = 1;
    [SerializeField] private float attackInterval = 1f;
    [SerializeField] private float attackRange = 0.6f;
    [SerializeField] private float aggroRadius = 3f;
    [SerializeField] private float combatScanInterval = 0.2f;

    [Header("Debug")]
    [Tooltip("Trigger-only collider kept in sync with resourceDetectionRadius, purely so the detection range is visible as a gizmo in the Scene view.")]
    [SerializeField] private CircleCollider2D detectionRadiusGizmo;

    [field: SerializeField] public PlayerBase HomeBase { get; set; }

    // Set by BotBase when spawning a wave unit - marches on this base's Queen instead of wandering.
    [field: SerializeField] public PlayerBase AttackTargetBase { get; set; }

    // Set by OrderAttack when a player sends this Attacker out - marches on this BotBase instead
    // of staying put on guard.
    [field: SerializeField] public BotBase AttackTargetBotBase { get; set; }

    [field: SerializeField] public Faction Faction { get; set; }

    public bool IsAlive => currentHealth > 0;

    [Header("Debug (runtime, read-only - select this unit in Play mode to inspect)")]
    [SerializeField] private UnitState state = UnitState.ExitingBase;
    [SerializeField] private bool hasTarget;
    [SerializeField] private Vector3 targetPosition;
    [SerializeField] private UnitController combatTarget;

    private Resource targetResource;
    private float resourceScanTimer;
    private float gatherTimer;
    private int carriedAmount;

    private float combatScanTimer;
    private float attackTimer;

    // Replicates movement state from Server to all connected Clients
    [SyncVar] private bool isWalkingServer;

    // Replicates facing direction from Server to all connected Clients. The base sprite/animation
    // faces right, so this flips the renderer horizontally while moving left.
    [SyncVar] private bool facingLeftServer;

    // Replicates whether this gatherer is currently carrying a resource, for the carry tint
    [SyncVar(hook = nameof(OnCarryingChanged))] private bool isCarryingResource;

    [SyncVar] private int currentHealth;

    // Drives the "IsAttacking" animator bool on all clients.
    [SyncVar] private bool isAttackingServer;

    private void Awake()
    {
        SyncDetectionRadiusGizmo();
        currentHealth = maxHealth;
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
        activeUnits.Add(this);
        if (animator == null) animator = GetComponent<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        activeUnits.Remove(this);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // The Queen is a permanent fixture at the center of the base interior - it never moves,
        // it only fights back if something reaches her.
        if (unitType == UnitType.Queen) return;

        // Wave units spawned by a BotBase march straight for their assigned target base instead
        // of wandering out of a home base.
        if (AttackTargetBase != null)
        {
            state = UnitState.MarchingToBase;
            SetTarget(AttackTargetBase.transform.position);
            return;
        }

        // Player-spawned Attackers park themselves near their Queen and wait for an attack order
        // instead of wandering out onto the world map.
        if (unitType == UnitType.Attacker)
        {
            state = UnitState.Guarding;
            SetTarget(GuardPosition());
            return;
        }

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
            animator.SetBool("IsAttacking", isAttackingServer);
        }
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = facingLeftServer;
        }

        // 2. Server-only position and state evaluation
        if (!isServer) return;
        if (!IsAlive) return;

        if (isAggressive)
        {
            TryAcquireAggroTarget();
        }

        // Combat takes priority over whatever else this unit was doing.
        if (combatTarget != null)
        {
            UpdateCombat();
            return;
        }

        if (unitType == UnitType.Queen) return;

        UpdateTask();
    }

    [Server]
    private void UpdateTask()
    {
        if (state == UnitState.AttackingBotBase)
        {
            UpdateBotBaseAttack();
            return;
        }

        if (state == UnitState.Gathering)
        {
            UpdateGathering();
            return;
        }

        bool canGather = unitType == UnitType.Gatherer && HomeBase != null && HomeBase.IsQueenAlive;

        if (unitType == UnitType.Gatherer && !canGather)
        {
            // Home base lost its Queen - abandon gathering for good, just wander from here on.
            if (state != UnitState.ExitingBase && state != UnitState.Wandering)
            {
                targetResource = null;
                isCarryingResource = false;
                StartWandering();
            }
        }
        else if (canGather && (state == UnitState.Wandering || state == UnitState.SeekingResource))
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
            if (resource == null || !resource.IsAvailable) continue;

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
            case UnitState.MarchingToBase:
                OnArrivedAtTargetBase();
                break;
            case UnitState.MarchingToQueen:
                // The Queen is gone (dead, or never there) - nothing left to march on.
                StartWandering();
                break;
            case UnitState.MarchingToBotBase:
                OnArrivedAtBotBase();
                break;
            case UnitState.ReturningToGuard:
                OnReturnedToGuard();
                break;
            case UnitState.Guarding:
                // Reached its guard spot near the Queen - stay put indefinitely until OrderAttack.
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

        // An Attacker that was just ordered out marches on its target instead of wandering.
        if (AttackTargetBotBase != null)
        {
            state = UnitState.MarchingToBotBase;
            SetTarget(AttackTargetBotBase.transform.position);
            return;
        }

        StartWandering();
    }

    [Server]
    private void OnResourceReached()
    {
        // The resource may have been claimed by another gatherer while we were en route. Resource
        // is a scene-placed NetworkIdentity, so a consumed one is never actually destroyed - only
        // deactivated (see the gotcha on Resource.IsAvailable) - so targetResource itself never
        // goes null just because someone else got there first; IsAvailable is the real check.
        if (targetResource == null || !targetResource.IsAvailable)
        {
            targetResource = null;
            StartWandering();
            return;
        }

        // Stand and "attack" the resource for a bit before it's actually gathered.
        state = UnitState.Gathering;
        hasTarget = false;
        gatherTimer = gatherDuration;
        UpdateFacing(targetResource.transform.position);
    }

    [Server]
    private void UpdateGathering()
    {
        isWalkingServer = false;

        bool stillValid = HomeBase != null && HomeBase.IsQueenAlive && targetResource != null && targetResource.IsAvailable;
        if (!stillValid)
        {
            isAttackingServer = false;
            targetResource = null;
            StartWandering();
            return;
        }

        isAttackingServer = true;

        gatherTimer -= Time.deltaTime;
        if (gatherTimer > 0f) return;

        isAttackingServer = false;

        // Someone else may have grabbed it in the same instant our timer ran out.
        if (!targetResource.TryConsume(out int amount))
        {
            targetResource = null;
            StartWandering();
            return;
        }

        carriedAmount = amount;
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
        SetTarget(DepositPoint(entryPoint));
    }

    // Where a Gatherer walks to inside the base to deposit - the resource stock building once it's
    // been spawned, falling back to the Queen's spot if the base has no stock yet (or none assigned).
    private Vector3 DepositPoint(Vector3 fallback)
    {
        if (HomeBase == null) return fallback;
        return HomeBase.ResourceStock != null ? HomeBase.ResourceStock.transform.position : HomeBase.InteriorCenter;
    }

    [Server]
    private void OnEntryReached()
    {
        // Reached the resource stock (or the Queen's spot, if this base has no stock) - deposit,
        // then head back out.
        if (HomeBase != null && HomeBase.ResourceStock != null)
        {
            HomeBase.ResourceStock.Deposit(carriedAmount);
        }
        else if (HomeBase != null)
        {
            HomeBase.DepositResource(carriedAmount);
        }

        carriedAmount = 0;
        isCarryingResource = false;

        state = UnitState.ExitingBase;
        SetTarget(HomeBase != null ? HomeBase.InteriorExitPoint : transform.position);
    }

    [Server]
    private void OnArrivedAtTargetBase()
    {
        if (AttackTargetBase == null)
        {
            StartWandering();
            return;
        }

        // Warp into the target base's interior through the same opening its own units use, then
        // head for the Queen at the center.
        transform.position = AttackTargetBase.InteriorExitPoint;

        state = UnitState.MarchingToQueen;
        SetTarget(AttackTargetBase.InteriorCenter);
    }

    // True once the target is gone - either actually destroyed (a runtime-spawned BotBase) or, far
    // more commonly, deactivated: BotBase is a scene-placed NetworkIdentity, so NetworkServer.Destroy
    // never truly destroys it (Mirror only deactivates scene objects so they stay respawnable) - the
    // component reference stays valid, just with currentHealth stuck at 0. A plain null check alone
    // never trips for a scene object, so health has to be checked too.
    private static bool IsBotBaseGone(BotBase target)
    {
        return target == null || !target.IsAlive;
    }

    [Server]
    private void OnArrivedAtBotBase()
    {
        if (IsBotBaseGone(AttackTargetBotBase))
        {
            // Gone by someone else while we were still marching there - head back and resume
            // guarding the Queen instead of attacking nothing.
            Debug.Log($"{name}: target BotBase gone before arrival, returning to guard", this);
            AttackTargetBotBase = null;
            state = UnitState.ReturningToGuard;
            SetTarget(HomeBase != null ? HomeBase.transform.position : transform.position);
            return;
        }

        // Stand in place and start dealing damage - see UpdateBotBaseAttack.
        Debug.Log($"{name}: arrived at {AttackTargetBotBase.name}, attacking", this);
        state = UnitState.AttackingBotBase;
        hasTarget = false;
        UpdateFacing(AttackTargetBotBase.transform.position);
    }

    [Server]
    private void UpdateBotBaseAttack()
    {
        if (IsBotBaseGone(AttackTargetBotBase))
        {
            // The target is gone - head back and resume guarding the Queen, the same way this
            // Attacker did right after it was spawned.
            Debug.Log($"{name}: target BotBase destroyed, returning to guard (HomeBase={HomeBase})", this);
            AttackTargetBotBase = null;
            isAttackingServer = false;
            state = UnitState.ReturningToGuard;
            SetTarget(HomeBase != null ? HomeBase.transform.position : transform.position);
            return;
        }

        isWalkingServer = false;
        isAttackingServer = true;

        attackTimer -= Time.deltaTime;
        if (attackTimer <= 0f)
        {
            attackTimer = attackInterval;
            AttackTargetBotBase.TakeDamage(attackDamage);
        }
    }

    // Sends this Attacker out from wherever it currently is (normally guarding near its Queen) to
    // march on and attack the given BotBase.
    [Server]
    public void OrderAttack(BotBase target)
    {
        if (unitType != UnitType.Attacker || IsBotBaseGone(target)) return;

        Debug.Log($"{name}: ordered to attack {target.name}", this);
        AttackTargetBotBase = target;
        state = UnitState.ExitingBase;
        SetTarget(HomeBase != null ? HomeBase.InteriorExitPoint : transform.position);
    }

    [Server]
    private void OnReturnedToGuard()
    {
        // Warp from the world map into the home base interior, entering through the same opening
        // units exit from, then walk to a guard spot near the Queen - exactly like a freshly
        // spawned Attacker does in OnStartServer.
        Debug.Log($"{name}: back home, resuming guard (HomeBase={HomeBase})", this);
        transform.position = HomeBase != null ? HomeBase.InteriorExitPoint : transform.position;

        state = UnitState.Guarding;
        SetTarget(GuardPosition());
    }

    [Server]
    private Vector3 GuardPosition()
    {
        if (HomeBase == null) return transform.position;

        float angle = Random.Range(0f, Mathf.PI * 2f);
        Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        float distance = Random.Range(0f, guardRadius);

        return HomeBase.InteriorCenter + new Vector3(direction.x, direction.y, 0f) * distance;
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

        UpdateFacing(position);
    }

    [Server]
    private void UpdateFacing(Vector3 towards)
    {
        float deltaX = towards.x - transform.position.x;
        if (!Mathf.Approximately(deltaX, 0f))
        {
            facingLeftServer = deltaX < 0f;
        }
    }

    [Server]
    private void TryAcquireAggroTarget()
    {
        if (combatTarget != null && combatTarget.IsAlive) return;

        combatScanTimer -= Time.deltaTime;
        if (combatScanTimer > 0f) return;
        combatScanTimer = combatScanInterval;

        combatTarget = FindNearestEnemy(aggroRadius);
    }

    [Server]
    private UnitController FindNearestEnemy(float radius)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, radius);
        UnitController nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (Collider2D hit in hits)
        {
            UnitController other = hit.GetComponent<UnitController>();
            if (other == null || other == this || !other.IsAlive) continue;
            if (other.Faction == Faction) continue;

            float distance = Vector3.Distance(transform.position, other.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = other;
            }
        }

        return nearest;
    }

    [Server]
    private void UpdateCombat()
    {
        if (combatTarget == null || !combatTarget.IsAlive)
        {
            combatTarget = null;
            isAttackingServer = false;
            return;
        }

        float distance = Vector3.Distance(transform.position, combatTarget.transform.position);

        if (distance > attackRange)
        {
            // Non-aggressive units only defend themselves - they don't chase a target that backs off.
            if (!isAggressive)
            {
                combatTarget = null;
                isAttackingServer = false;
                return;
            }

            isAttackingServer = false;
            isWalkingServer = true;
            transform.position = Vector3.MoveTowards(transform.position, combatTarget.transform.position, moveSpeed * Time.deltaTime);
            UpdateFacing(combatTarget.transform.position);
        }
        else
        {
            isWalkingServer = false;
            isAttackingServer = true;
            UpdateFacing(combatTarget.transform.position);

            attackTimer -= Time.deltaTime;
            if (attackTimer <= 0f)
            {
                attackTimer = attackInterval;
                combatTarget.TakeDamage(attackDamage, this);
            }
        }
    }

    [Server]
    public void TakeDamage(int amount, UnitController attacker)
    {
        if (!IsAlive) return;

        currentHealth = Mathf.Max(0, currentHealth - amount);

        if (currentHealth == 0)
        {
            Die();
            return;
        }

        // Not already fighting something else - retaliate against whoever just hit us.
        if (combatTarget == null)
        {
            combatTarget = attacker;
        }
    }

    [Server]
    private void Die()
    {
        if (unitType == UnitType.Queen && HomeBase != null)
        {
            HomeBase.OnQueenKilled();
        }

        NetworkServer.Destroy(gameObject);
    }

    private void OnCarryingChanged(bool oldValue, bool newValue)
    {
        if (spriteRenderer == null) return;
        spriteRenderer.color = newValue ? carryingTintColor : Color.white;
    }
}
