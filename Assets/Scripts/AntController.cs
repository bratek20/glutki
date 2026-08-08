using UnityEngine;
using Mirror;

public class AntController : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float stoppingDistance = 0.1f;
    [SerializeField] private float wanderRadius = 5f;

    private Vector3 targetPosition;
    private Vector3 lastPosition;
    private bool hasTarget;

    // DIAGNOSTIC ONLY - remove once sync works.
    // A SyncVar travels in the same EntityStateMessage as the NetworkTransform
    // data for this identity. If this hook fires on the client, delivery works
    // and the problem is NetworkTransform specific. If it never fires, nothing
    // is reaching this object at all.
    [SyncVar(hook = nameof(OnHeartbeatChanged))]
    private int heartbeat;

    private double nextHeartbeat;

    private void OnHeartbeatChanged(int oldValue, int newValue)
    {
        Debug.Log($"<color=lime>[SYNC RECEIVED]</color> heartbeat {newValue} | my pos: {transform.position}");
    }

    // Called on the Host/Server as soon as this networked object spawns
    public override void OnStartServer()
    {
        base.OnStartServer();
        lastPosition = transform.position;
        PickRandomTarget();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        Debug.Log($"<color=cyan>[CLIENT SPAWN]</color> Ant registered on Client. NetID: {netId} | Pos: {transform.position}");
    }

    private void Update()
    {
        // Movement is server authoritative. NetworkTransform replicates the
        // resulting position/rotation down to the clients.
        if (!isServer) return;

        // DIAGNOSTIC ONLY - tick a SyncVar once per second.
        if (NetworkTime.localTime >= nextHeartbeat)
        {
            nextHeartbeat = NetworkTime.localTime + 1.0;
            heartbeat++;
            Debug.Log($"<color=orange>[SYNC SENT]</color> heartbeat {heartbeat} | server pos: {transform.position}");
        }

        if (hasTarget)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPosition,
                moveSpeed * Time.deltaTime
            );

            // Reached target -> pick a new random spot to wander to
            if (Vector3.Distance(transform.position, targetPosition) <= stoppingDistance)
            {
                hasTarget = false;
                PickRandomTarget();
            }
        }

        // Rotate the 2D sprite towards the facing direction. Server only:
        // NetworkTransform syncs rotation, so doing this on the client too
        // would fight the interpolated rotation it applies every frame.
        Vector3 moveDir = transform.position - lastPosition;
        if (moveDir.sqrMagnitude > 0.0001f)
        {
            float angle = Mathf.Atan2(moveDir.y, moveDir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        lastPosition = transform.position;
    }

    // Set an explicit move target (Call from Host/Server scripts)
    [Server]
    public void SetTarget(Vector3 newTarget)
    {
        targetPosition = newTarget;
        targetPosition.z = 0f; // Force 2D plane
        hasTarget = true;
    }

    // Pick a random location within wanderRadius (Host/Server only)
    [Server]
    public void PickRandomTarget()
    {
        Vector2 randomOffset = Random.insideUnitCircle * wanderRadius;
        Vector3 newDestination = transform.position + new Vector3(randomOffset.x, randomOffset.y, 0f);
        SetTarget(newDestination);
    }
}
