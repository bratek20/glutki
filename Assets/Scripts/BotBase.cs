using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Purely a server-side wave spawner - no Base View, no ownership, no resources. Periodically
// spawns a wave of bot units that all march on one randomly-picked PlayerBase's Queen. Has HP so
// players can strike back (see AttackOrderPopup) - once it's destroyed, its already-spawned wave
// units are unaffected, they're independent NetworkIdentities with no back-reference to it.
public class BotBase : NetworkBehaviour
{
    [SerializeField] private GameObject[] enemyUnitPrefabs;
    [SerializeField] private float waveIntervalMin = 30f;
    [SerializeField] private float waveIntervalMax = 60f;
    [SerializeField] private int waveSizeMin = 3;
    [SerializeField] private int waveSizeMax = 6;
    [SerializeField] private int maxHealth = 30;

    [SyncVar] private int currentHealth;

    private Collider2D selectionCollider;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsAlive => currentHealth > 0;

    private void Awake()
    {
        currentHealth = maxHealth;
        selectionCollider = GetComponent<Collider2D>();
    }

    private void Update()
    {
        if (selectionCollider == null || Camera.main == null || Mouse.current == null) return;
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Vector2 worldPoint = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        if (selectionCollider.OverlapPoint(worldPoint))
        {
            AttackOrderPopup.Open(this);
        }
    }

    [Server]
    public void TakeDamage(int amount)
    {
        if (!IsAlive) return;

        currentHealth = Mathf.Max(0, currentHealth - amount);

        if (currentHealth == 0) Die();
    }

    [Server]
    private void Die()
    {
        CancelInvoke();
        NetworkServer.Destroy(gameObject);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        ScheduleNextWave();
    }

    [Server]
    private void ScheduleNextWave()
    {
        Invoke(nameof(SpawnWave), Random.Range(waveIntervalMin, waveIntervalMax));
    }

    [Server]
    private void SpawnWave()
    {
        PlayerBase target = PickRandomTarget();
        if (target != null)
        {
            int count = Random.Range(waveSizeMin, waveSizeMax + 1);
            for (int i = 0; i < count; i++)
            {
                SpawnWaveUnit(target);
            }
        }

        ScheduleNextWave();
    }

    [Server]
    private PlayerBase PickRandomTarget()
    {
        var bases = BaseSelectionManager.AllBases;
        return bases.Count > 0 ? bases[Random.Range(0, bases.Count)] : null;
    }

    [Server]
    private void SpawnWaveUnit(PlayerBase target)
    {
        if (enemyUnitPrefabs == null || enemyUnitPrefabs.Length == 0) return;

        GameObject prefab = enemyUnitPrefabs[Random.Range(0, enemyUnitPrefabs.Length)];
        GameObject unit = Instantiate(prefab, transform.position, Quaternion.identity);

        UnitController controller = unit.GetComponent<UnitController>();
        if (controller != null)
        {
            controller.Faction = Faction.Bot;
            controller.AttackTargetBase = target;
        }

        NetworkServer.Spawn(unit);
    }
}
