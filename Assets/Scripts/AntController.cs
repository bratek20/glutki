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
        PickRandomTarget();
    }

    private void Start()
    {
        lastPosition = transform.position;
        targetPosition = transform.position;
    }

    private void Update()
    {
        // 1. HOST LOGIC: Move physical object on the server
        if (isServer && hasTarget)
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

        // 2. HOST & CLIENT LOGIC: Rotate 2D sprite towards facing direction
        // (Calculated frame-by-frame on both Host and Client using actual position change)
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