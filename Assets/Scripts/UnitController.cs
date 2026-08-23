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

    // Same as CountActive but skips units already at 0 HP. A dying unit is still in the registry when
    // it reports its own death (Mirror only removes it once the object is actually destroyed), so a
    // caller asking "is there anyone left?" from inside Die() would otherwise count the corpse.
    public static int CountAlive(PlayerBase homeBase, UnitType type)
    {
        int count = 0;
        foreach (UnitController unit in activeUnits)
        {
            if (unit.HomeBase == homeBase && unit.unitType == type && unit.IsAlive) count++;
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
        InBarrack,
        MarchingToBotBase,
        AttackingBotBase,
        ReturningToBarrack,
        WalkingToGrowthTile,
        WaitingToGrow,
        Growing,
        WalkingToStock,
        LoadingFood,
        CarryingFoodToQueen
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
    [Tooltip("How much a Gatherer takes out of a resource per trip. A resource holding less than this gives up whatever it has left.")]
    [SerializeField] private int gatherAmount = 10;
    [SerializeField] private Color carryingTintColor = new Color(1f, 0.85f, 0.35f);

    [Header("Feeding (Builder only)")]
    [Tooltip("How much the Builder carries from a ResourceStock to the Queen per trip. Bigger loads mean fewer, slower round trips.")]
    [SerializeField] private int feedCarryAmount = 5;
    [Tooltip("How long the Builder stands at the stock loading up (playing the attack animation) before the resources actually leave storage.")]
    [SerializeField] private float feedLoadDuration = 1f;
    [Tooltip("How often an idle Builder re-checks whether the Queen needs feeding again.")]
    [SerializeField] private float feedScanInterval = 0.5f;

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

    public UnitType Type => unitType;

    // Set by PlayerBase right before it spawns a Child: which unit prefab this Child eventually
    // grows into, and which of the base's growth tiles it walks to and occupies until it does.
    // Server-only - no peer other than the server ever needs to know.
    public GameObject GrowsIntoPrefab { get; set; }
    public int GrowthSlot { get; set; } = -1;

    // Which of the home base's barracks this Attacker lives in. Reserved by PlayerBase when the
    // Attacker is ordered and carried down the Child -> grown unit chain, so no two ever share one.
    // Server-only, like GrowthSlot.
    public int BarrackSlot { get; set; } = -1;

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

    private float feedTimer;
    private float feedScanTimer;

    private float combatScanTimer;
    private float attackTimer;
    private float growthTimer;

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

    // Drives the Queen's "IsSpawning" birth animation while her base produces a Child, and the
    // "IsGrowing" animation a freshly transformed unit plays on its growth tile.
    [SyncVar] private bool isSpawningServer;
    [SyncVar] private bool isGrowingServer;

    private HashSet<string> animatorParameters;

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

        // A Child is born next to the Queen and walks straight to the growth tile reserved for it,
        // where it waits out childIdleTime before the base transforms it.
        if (unitType == UnitType.Child)
        {
            state = UnitState.WalkingToGrowthTile;
            SetTarget(HomeBase != null && GrowthSlot >= 0 ? HomeBase.GrowthTileCenter(GrowthSlot) : transform.position);
            return;
        }

        // A slot already assigned means this unit was just transformed out of a Child and is
        // standing on its growth tile - it plays the growth animation there before coming to life.
        if (GrowthSlot >= 0)
        {
            state = UnitState.Growing;
            hasTarget = false;
            isGrowingServer = true;
            growthTimer = HomeBase != null ? HomeBase.GrowthTime : 0f;
            return;
        }

        BeginNormalLife();
    }

    // What a unit does once it's fully alive - either straight after spawning, or after it's
    // finished growing on its tile.
    [Server]
    private void BeginNormalLife()
    {
        // An Attacker lives in the barrack its order reserved - it walks there and stays put until
        // it's sent out, rather than milling about the interior.
        if (unitType == UnitType.Attacker)
        {
            state = UnitState.InBarrack;
            SetTarget(BarrackPosition());
            return;
        }

        // A Builder's whole life happens inside the base - it never walks out to the exit.
        if (unitType == UnitType.Builder)
        {
            if (!TryStartFeedingTrip()) StartWandering();
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
        SetAnimatorBool("IsWalking", isWalkingServer);
        SetAnimatorBool("IsAttacking", isAttackingServer);
        SetAnimatorBool("IsSpawning", isSpawningServer);
        SetAnimatorBool("IsGrowing", isGrowingServer);

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

        if (state == UnitState.LoadingFood)
        {
            UpdateLoadingFood();
            return;
        }

        if (state == UnitState.WaitingToGrow)
        {
            UpdateWaitingToGrow();
            return;
        }

        if (state == UnitState.Growing)
        {
            UpdateGrowing();
            return;
        }

        // Re-aimed every frame rather than latched at birth, so the base's growth tile layout can
        // be tuned in the Inspector during Play mode and Children retarget immediately.
        if (state == UnitState.WalkingToGrowthTile && HomeBase != null && GrowthSlot >= 0)
        {
            SetTarget(HomeBase.GrowthTileCenter(GrowthSlot));
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

        // An idle Builder keeps an eye on the Queen - the moment an order is placed (or a Gatherer
        // tops the stock back up) it stops pottering about and starts carrying.
        if (unitType == UnitType.Builder && state == UnitState.Wandering)
        {
            feedScanTimer -= Time.deltaTime;
            if (feedScanTimer <= 0f)
            {
                feedScanTimer = feedScanInterval;
                TryStartFeedingTrip();
            }
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

    // Whichever base's interior this unit is currently standing in, if any - its own while it's at
    // home, or the one it's marching on as a wave unit. Null out on the world map, where nothing
    // blocks movement.
    private PlayerBase CurrentInteriorBase()
    {
        if (HomeBase != null && HomeBase.ContainsInterior(transform.position)) return HomeBase;
        if (AttackTargetBase != null && AttackTargetBase.ContainsInterior(transform.position)) return AttackTargetBase;
        return null;
    }

    [Server]
    private void UpdateServerMovement()
    {
        if (!hasTarget) return;

        Vector3 from = transform.position;
        Vector3 to = Vector3.MoveTowards(from, targetPosition, moveSpeed * Time.deltaTime);

        // Inside a base, obstacle tiles are solid - the interior decides where the step actually
        // lands, sliding along a wall rather than passing through it.
        PlayerBase interiorBase = CurrentInteriorBase();
        transform.position = interiorBase != null ? interiorBase.ResolveMovement(from, to) : to;

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
            case UnitState.ReturningToBarrack:
                OnReturnedToBarrack();
                break;
            case UnitState.WalkingToGrowthTile:
                OnGrowthTileReached();
                break;
            case UnitState.WalkingToStock:
                OnStockReached();
                break;
            case UnitState.CarryingFoodToQueen:
                OnQueenReachedWithFood();
                break;
            case UnitState.InBarrack:
                // Home - stay put indefinitely until OrderAttack sends it out again.
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

        // Someone else may have drained the last of it in the same instant our timer ran out.
        if (!targetResource.TryGather(gatherAmount, out int amount))
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
        SetTarget(HomeBase != null ? HomeBase.DepositPoint(entryPoint) : entryPoint);
    }

    [Server]
    private void OnEntryReached()
    {
        // Reached whichever stock tile (or the Queen's spot, if this base has none) it was heading
        // for. The count itself is one pool on the base - a stock is only the place to drop it.
        if (HomeBase != null) HomeBase.DepositResource(carriedAmount);

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
        SetTarget(AttackTargetBase.QueenPoint);
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
            // Gone by someone else while we were still marching there - head home to the barrack
            // instead of attacking nothing.
            Debug.Log($"{name}: target BotBase gone before arrival, returning to barrack", this);
            AttackTargetBotBase = null;
            state = UnitState.ReturningToBarrack;
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
            // The target is gone - head back to the barrack, the same way this Attacker did right
            // after it was spawned.
            Debug.Log($"{name}: target BotBase destroyed, returning to barrack (HomeBase={HomeBase})", this);
            AttackTargetBotBase = null;
            isAttackingServer = false;
            state = UnitState.ReturningToBarrack;
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

    // Sends this Attacker out from wherever it currently is (normally sitting in its barrack) to
    // march on and attack the given BotBase. Its barrack stays reserved while it's away - it has
    // somewhere to come back to.
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
    private void OnReturnedToBarrack()
    {
        // Warp from the world map into the home base interior, entering through the same opening
        // units exit from, then walk back to its own barrack - exactly like a freshly grown
        // Attacker does in BeginNormalLife.
        Debug.Log($"{name}: back home, returning to barrack (HomeBase={HomeBase})", this);
        transform.position = HomeBase != null ? HomeBase.InteriorExitPoint : transform.position;

        state = UnitState.InBarrack;
        SetTarget(BarrackPosition());
    }

    // Starts a stock -> Queen round trip if there's one worth making. Returns false when there's
    // nothing to do, leaving the caller to decide what to do with an idle Builder - which is why
    // this doesn't fall back to wandering itself: the idle re-check calls it every feedScanInterval
    // and must not restart a wander that's already in progress.
    [Server]
    private bool TryStartFeedingTrip()
    {
        if (HomeBase == null) return false;

        // A dead Queen can't be fed, and nothing is worth carrying if she's already got enough
        // banked for every order that's been placed.
        if (!HomeBase.IsQueenAlive) return false;
        if (HomeBase.FoodShortfall <= 0) return false;
        if (HomeBase.StoredResources <= 0) return false;

        // A base whose layout gives it no stock tile has nowhere to pick up from - its Builder
        // simply can't work.
        if (!HomeBase.HasResourceStock) return false;

        state = UnitState.WalkingToStock;
        SetTarget(HomeBase.DepositPoint(transform.position));
        return true;
    }

    [Server]
    private void OnStockReached()
    {
        if (HomeBase == null)
        {
            StartWandering();
            return;
        }

        state = UnitState.LoadingFood;
        hasTarget = false;
        feedTimer = feedLoadDuration;
    }

    [Server]
    private void UpdateLoadingFood()
    {
        isWalkingServer = false;
        isAttackingServer = true;

        feedTimer -= Time.deltaTime;
        if (feedTimer > 0f) return;

        isAttackingServer = false;

        // Resources only actually leave storage once the load animation has played out, so another
        // Builder may have emptied the pool in the meantime.
        carriedAmount = HomeBase != null ? HomeBase.WithdrawForFeeding(feedCarryAmount) : 0;
        if (carriedAmount <= 0)
        {
            if (!TryStartFeedingTrip()) StartWandering();
            return;
        }

        isCarryingResource = true;
        state = UnitState.CarryingFoodToQueen;
        SetTarget(QueenPosition());
    }

    [Server]
    private void OnQueenReachedWithFood()
    {
        if (HomeBase != null) HomeBase.FeedQueen(carriedAmount);

        carriedAmount = 0;
        isCarryingResource = false;

        if (!TryStartFeedingTrip()) StartWandering();
    }

    private Vector3 QueenPosition()
    {
        if (HomeBase == null) return transform.position;
        return HomeBase.Queen != null ? HomeBase.Queen.transform.position : HomeBase.QueenPoint;
    }

    [Server]
    private void OnGrowthTileReached()
    {
        state = UnitState.WaitingToGrow;
        hasTarget = false;
        isWalkingServer = false;
        growthTimer = HomeBase != null ? HomeBase.ChildIdleTime : 0f;
    }

    // Phase one: the Child just sits idle on its tile for childIdleTime.
    [Server]
    private void UpdateWaitingToGrow()
    {
        isWalkingServer = false;
        StayOnGrowthTile();

        growthTimer -= Time.deltaTime;
        if (growthTimer > 0f) return;

        // Waited long enough - the base swaps this Child out for the unit it was ordered as, in
        // place and still holding the same tile, which then plays its growth animation there.
        if (HomeBase != null) HomeBase.CompleteGrowth(this);
    }

    // Phase two: the transformed unit plays its growth animation on the tile before coming to life.
    [Server]
    private void UpdateGrowing()
    {
        isWalkingServer = false;
        StayOnGrowthTile();

        growthTimer -= Time.deltaTime;
        if (growthTimer > 0f) return;

        isGrowingServer = false;

        // Fully grown - hand the tile back so the next queued order can use it, and start living.
        if (HomeBase != null) HomeBase.ReleaseGrowthSlot(this);
        GrowthSlot = -1;

        BeginNormalLife();
    }

    // Keeps a unit glued to its growth tile through both phases, recomputed rather than latched so
    // the base's growth tile layout can be tuned in the Inspector mid-game.
    [Server]
    private void StayOnGrowthTile()
    {
        if (HomeBase == null || GrowthSlot < 0) return;

        transform.position = Vector3.MoveTowards(transform.position, HomeBase.GrowthTileCenter(GrowthSlot), moveSpeed * Time.deltaTime);
    }

    // The middle of this Attacker's own barrack tile. Falls back to the Queen's spot for an
    // Attacker that somehow has no barrack (a base whose layout has none, say) - it still needs
    // somewhere to stand.
    [Server]
    private Vector3 BarrackPosition()
    {
        if (HomeBase == null) return transform.position;
        return BarrackSlot >= 0 ? HomeBase.BarrackCenter(BarrackSlot) : HomeBase.QueenPoint;
    }

    [Server]
    private void StartWandering()
    {
        state = UnitState.Wandering;

        float angle = Random.Range(0f, Mathf.PI * 2f);
        Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        float distance = Random.Range(wanderMinDistance, wanderMaxDistance);

        Vector3 destination = transform.position + new Vector3(direction.x, direction.y, 0f) * distance;

        // Wandering inside a base is penned into the room and off the walls, so a unit is never
        // sent toward a tile it can't reach. Out on the world map nothing is in the way.
        PlayerBase interiorBase = CurrentInteriorBase();
        if (interiorBase != null) destination = interiorBase.NearestWalkablePoint(destination);

        SetTarget(destination);
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

    // Called by this unit's PlayerBase while it's producing a Child, so the Queen plays her birth
    // animation for exactly as long as the base's spawnDuration says.
    [Server]
    public void SetSpawning(bool spawning)
    {
        isSpawningServer = spawning;
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

        // Losing the last Builder ends the base as surely as losing the Queen does - see
        // PlayerBase.HasLivingBuilder.
        if (unitType == UnitType.Builder && HomeBase != null)
        {
            HomeBase.OnBuilderKilled();
        }

        // Killed while still on a growth tile (either as a Child waiting, or mid growth animation)
        // - hand the tile back so the next queued order can use it.
        if (GrowthSlot >= 0 && HomeBase != null)
        {
            HomeBase.ReleaseGrowthSlot(this);
        }

        // Its barrack is free again - whether it died in it, on the way to it, or out on a raid.
        // A Child carries the reservation too, so an Attacker that dies before it's even grown
        // doesn't leave its barrack held forever.
        if (BarrackSlot >= 0 && HomeBase != null)
        {
            HomeBase.ReleaseBarrack(BarrackSlot);
            BarrackSlot = -1;
        }

        NetworkServer.Destroy(gameObject);
    }

    // Every unit type has its own Animator Controller declaring only the parameters it actually
    // animates (a Queen has no attack, a Gatherer no birth), so pushing all of them blindly would
    // log a warning every frame for the ones a given controller doesn't declare.
    private void SetAnimatorBool(string parameterName, bool value)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        if (animatorParameters == null)
        {
            animatorParameters = new HashSet<string>();
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                animatorParameters.Add(parameter.name);
            }
        }

        if (animatorParameters.Contains(parameterName)) animator.SetBool(parameterName, value);
    }

    private void OnCarryingChanged(bool oldValue, bool newValue)
    {
        if (spriteRenderer == null) return;
        spriteRenderer.color = newValue ? carryingTintColor : Color.white;
    }
}
