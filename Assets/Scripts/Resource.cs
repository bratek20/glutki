using Mirror;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Resource : NetworkBehaviour
{
    [SerializeField] private int amount = 1;

    public int Amount => amount;

    [Server]
    public void Consume()
    {
        NetworkServer.Destroy(gameObject);
    }
}
