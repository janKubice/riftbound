using UnityEngine;

public class EnemySpawnPoint : MonoBehaviour
{
    [Tooltip("Obtížnost této oblasti (1 = Start, 100 = Hardcore zóna).")]
    public float ZoneDifficulty = 1.0f;
    
    [Tooltip("Radius v jakém se mobové spawnují kolem tohoto bodu.")]
    public float SpawnRadius = 5.0f;

    void OnEnable() { Register(); }
    void Start() { Register(); } 

    void OnDisable() 
    { 
        if(DirectorSpawner.Instance) 
            DirectorSpawner.Instance.UnregisterSpawnPoint(this); 
    }

    private void Register()
    {
        if (DirectorSpawner.Instance) 
            DirectorSpawner.Instance.RegisterSpawnPoint(this);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, SpawnRadius);
        Gizmos.DrawIcon(transform.position, "SpawnPoint_Icon", true);
    }
}