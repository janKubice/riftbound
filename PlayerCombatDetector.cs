using UnityEngine;
// using System.Collections; // Není potřeba, pokud nepoužíváš Coroutines, ale nechávám pro jistotu

public class PlayerCombatDetector : MonoBehaviour
{
    [Header("Detekce Nepřátel")]
    [Tooltip("Vzdálenost, pod kterou považujeme nepřítele za hrozbu.")]
    [SerializeField] private float _detectionRadius = 30.0f;

    [Tooltip("Na jaké vrstvě jsou nepřátelé? (Nutné nastavit!)")]
    [SerializeField] private LayerMask _enemyLayer;
    
    [Tooltip("Maximální počet nepřátel, které zkontrolujeme v jednom snímku (Optimalizace paměti).")]
    [SerializeField] private int _maxEnemiesToCheck = 20;

    [Header("Časování")]
    [Tooltip("Jak dlouho musí být nepřítel blízko (< 30m), aby začal boj.")]
    [SerializeField] private float _timeToEnterCombat = 5.0f;

    [Tooltip("Jak dlouho musí být všichni nepřátelé daleko (> 30m), aby boj skončil.")]
    [SerializeField] private float _timeToExitCombat = 10.0f;

    // Interní stavy
    private bool _isInCombat = false;
    private float _dangerTimer = 0f; 
    private float _safeTimer = 0f;   

    // Optimalizace: Předalokované pole pro OverlapSphere, abychom nezatěžovali GC každým framem
    private Collider[] _hitBuffer;

    private void Awake()
    {
        // Inicializace bufferu
        _hitBuffer = new Collider[_maxEnemiesToCheck];
    }

    private void Update()
    {
        // 1. Zjistíme počet koliderů v dosahu a naplníme buffer
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, _detectionRadius, _hitBuffer, _enemyLayer);

        bool realEnemyNearby = false;

        // 2. Projdeme nalezené objekty
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _hitBuffer[i];

            // Pokud má objekt skript DPSdummy, ignorujeme ho (continue skočí na další iteraci)
            // Používáme TryGetComponent pro bezpečnost a rychlost
            if (col.TryGetComponent<DPSDummy>(out _)) 
            {
                continue; 
            }
            
            // Alternativa: Pokud je DPSdummy na parent objektu a collider na child
            // if (col.GetComponentInParent<DPSdummy>() != null) continue;

            // Pokud jsme našli alespoň jednoho nepřítele, který NENÍ dummy:
            realEnemyNearby = true;
            break; // Nemusíme prohledávat zbytek, jeden stačí pro poplach
        }

        // 3. Logika změny stavu
        if (realEnemyNearby)
        {
            HandleEnemyNearby();
        }
        else
        {
            HandleNoEnemyNearby();
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
        {
            MusicManager.Instance.SetState(MusicManager.MusicState.Combat);
        }
        
        Debug.Log("[CombatDetector] Entering Combat Mode (Valid enemy nearby)");
    }

    private void ExitCombat()
    {
        _isInCombat = false;
        _safeTimer = 0f; 

        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.SetState(MusicManager.MusicState.Exploration);
        }

        Debug.Log("[CombatDetector] Exiting Combat Mode (Safe)");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1, 0, 0, 0.3f); 
        Gizmos.DrawWireSphere(transform.position, _detectionRadius);
    }
}