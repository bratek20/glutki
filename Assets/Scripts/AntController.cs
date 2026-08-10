using UnityEngine;
using Mirror;

[RequireComponent(typeof(Animator))]
public class AntController : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float stoppingDistance = 0.05f;
    [SerializeField] private Animator animator;

    private Vector3 targetPosition;
    private bool hasTarget;

    // Replicates movement state from Server to all connected Clients
    [SyncVar] private bool isWalkingServer;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (animator == null) animator = GetComponent<Animator>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        PickRandomTarget();
    }

    private void Update()
    {
        // 1. Visually update local Animator on Host & Clients
        if (animator != null)
        {
            animator.SetBool("IsWalking", isWalkingServer);
        }

        // 2. Server-only position and state evaluation
        if (!isServer) return;

        UpdateServerMovement();
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
            Invoke(nameof(PickRandomTarget), Random.Range(1f, 3f));
        }
        else
        {
            isWalkingServer = true;
        }
    }

    [Server]
    private void PickRandomTarget()
    {
        Vector2 circle = Random.insideUnitCircle * 5f;
        targetPosition = transform.position + new Vector3(circle.x, circle.y, 0f);
        hasTarget = true;
    }
}