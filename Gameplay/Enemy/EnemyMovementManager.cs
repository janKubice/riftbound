using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;

public class EnemyMovementManager : MonoBehaviour
{
    public static EnemyMovementManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private float _separationRadius = 1.5f;
    [SerializeField] private float _separationForce = 2.0f;
    [Tooltip("Každý kolikátý snímek se počítá separace pro danou entitu")]
    [SerializeField] private int _separationUpdateRate = 10; 

    private List<EnemyBaseAI> _activeEnemies = new List<EnemyBaseAI>(3000);
    private static readonly Collider[] _neighborBuffer = new Collider[10];
    private int _frameCount = 0;

    private void Awake()
    {
        if (Instance != null) Destroy(gameObject);
        Instance = this;
    }

    public void RegisterEnemy(EnemyBaseAI enemy)
    {
        if (!_activeEnemies.Contains(enemy)) _activeEnemies.Add(enemy);
    }

    public void UnregisterEnemy(EnemyBaseAI enemy)
    {
        _activeEnemies.Remove(enemy);
    }

    private void Update()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        int count = _activeEnemies.Count;
        _frameCount++;

        for (int i = 0; i < count; i++)
        {
            var enemy = _activeEnemies[i];
            
            if (enemy == null) continue;

            // 1. Spuštění logiky nepřítele centrálně
            if (enemy.TargetPlayer != null && !enemy.IsMovementPaused)
            {
                enemy.BehaviorLogic();
            }

            if (enemy.TargetPlayer == null)
            {
                FindTargetFor(enemy);
                continue;
            }

            if (enemy.IsMovementPaused) continue;

            Vector3 currentPos = enemy.MyTransform.position;
            Vector3 targetPos = enemy.TargetPlayer.position + enemy._targetOffset;
            Vector3 direction = (targetPos - currentPos).normalized;

            // 2. Time-Slicing: Výpočet separace jen pro zlomek entit v aktuálním snímku
            if (i % _separationUpdateRate == _frameCount % _separationUpdateRate)
            {
                enemy.CachedSeparation = GetSeparationVector(enemy);
            }

            // 3. Aplikace pohybu s využitím cachované separace
            Vector3 finalVelocity = (direction + enemy.CachedSeparation).normalized * 3.5f; 
            enemy.ManualMove(finalVelocity);
        }
    }

    private Vector3 GetSeparationVector(EnemyBaseAI currentEnemy)
    {
        Vector3 separationVector = Vector3.zero;
        int hitCount = Physics.OverlapSphereNonAlloc(
            currentEnemy.MyTransform.position,
            _separationRadius,
            _neighborBuffer,
            LayerMask.GetMask("Enemy")
        );

        int count = 0;
        for (int i = 0; i < hitCount; i++)
        {
            var col = _neighborBuffer[i];
            if (col.gameObject == currentEnemy.gameObject) continue;

            Vector3 dir = col.transform.position - currentEnemy.MyTransform.position;
            float dist = dir.magnitude;

            if (dist <= 0.001f)
            {
                separationVector += Random.insideUnitSphere;
                count++;
                continue;
            }

            float strength = 1.0f - (dist / _separationRadius);
            separationVector -= (dir / dist) * strength; 
            count++;
        }

        if (count > 0)
        {
            separationVector = (separationVector / count) * _separationForce;
        }

        return separationVector;
    }

    private void FindTargetFor(EnemyBaseAI enemy)
    {
        if (NetworkManager.Singleton.ConnectedClients.Count > 0)
        {
            var client = NetworkManager.Singleton.ConnectedClientsList[0];
            if (client.PlayerObject != null)
            {
                enemy.TargetPlayer = client.PlayerObject.transform;
            }
        }
    }
}