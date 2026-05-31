using UnityEngine;

public class PlayerCombatDetector : MonoBehaviour
{
    [Header("Detekce Nepřátel")]
    [SerializeField] private float _detectionRadius = 30.0f;
    [SerializeField] private LayerMask _enemyLayer;
    [SerializeField] private int _maxEnemiesToCheck = 20;

    [Header("Optimalizace")]
    [Tooltip("Jak často se ověřuje okolí (v sekundách). 0.25 = 4x za sekundu.")]
    [SerializeField] private float _checkInterval = 0.25f;

    [Header("Časování")]
    [SerializeField] private float _timeToEnterCombat = 5.0f;
    [SerializeField] private float _timeToExitCombat = 10.0f;

    private bool _isInCombat = false;
    private bool _realEnemyNearby = false;
    private float _dangerTimer = 0f;
    private float _safeTimer = 0f;
    private float _nextCheckTime = 0f;

    private Collider[] _hitBuffer;

    private void Awake()
    {
        _hitBuffer = new Collider[_maxEnemiesToCheck];
    }

    private void Update()
    {
        if (Time.time >= _nextCheckTime)
        {
            _nextCheckTime = Time.time + _checkInterval;
            EvaluateSurroundings();
        }

        if (_realEnemyNearby)
        {
            HandleEnemyNearby();
        }
        else
        {
            HandleNoEnemyNearby();
        }
    }

    private void EvaluateSurroundings()
    {
        _realEnemyNearby = false;

        if (EnemyMovementManager.Instance == null)
            return;

        // Vypočítáme umocněný rádius předem (optimalizace - nepočítá se v cyklu)
        float detectionRadiusSqr = _detectionRadius * _detectionRadius;
        Vector3 myPosition = transform.position;

        var enemies = EnemyMovementManager.Instance.MovingEnemies;
        int count = enemies.Count;

        for (int i = 0; i < count; i++)
        {
            EnemyBaseAI enemy = enemies[i];

            // Rychlá pojistka
            if (enemy == null || !enemy.IsAlive)
                continue;

            if (enemy.isDummy)
                continue;

            // Výpočet umocněné vzdálenosti
            float sqrDistance = (enemy.MyTransform.position - myPosition).sqrMagnitude;

            if (sqrDistance <= detectionRadiusSqr)
            {
                _realEnemyNearby = true;
                break; // Jeden nepřítel stačí k triggeru boje, cyklus končí
            }
        }
    }

    private void HandleEnemyNearby()
    {
        _safeTimer = 0f;

        if (_isInCombat) return;

        _dangerTimer += Time.deltaTime;

        if (_dangerTimer >= _timeToEnterCombat)
        {
            EnterCombat();
        }
    }

    private void HandleNoEnemyNearby()
    {
        _dangerTimer = 0f;

        if (!_isInCombat) return;

        _safeTimer += Time.deltaTime;

        if (_safeTimer >= _timeToExitCombat)
        {
            ExitCombat();
        }
    }

    private void EnterCombat()
    {
        _isInCombat = true;
        _dangerTimer = 0f;

        if (MusicManager.Instance != null)
            MusicManager.Instance.SetState(MusicManager.MusicState.Combat);

        Debug.Log("[CombatDetector] Entering Combat Mode");
    }

    private void ExitCombat()
    {
        _isInCombat = false;
        _safeTimer = 0f;

        if (MusicManager.Instance != null)
            MusicManager.Instance.SetState(MusicManager.MusicState.Exploration);

        Debug.Log("[CombatDetector] Exiting Combat Mode");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1, 0, 0, 0.3f);
        Gizmos.DrawWireSphere(transform.position, _detectionRadius);
    }
}