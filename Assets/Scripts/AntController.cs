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

    // Called on the Host/Server as soon as this networked object spawns
    public override void OnStartServer()
    {
        base.OnStartServer();
        lastPosition = transform.position;
        PickRandomTarget();
    }

    private void Update()
    {
        // Movement is server authoritative. NetworkTransform replicates the
        // resulting position/rotation down to the clients.
        if (!isServer) return;

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
