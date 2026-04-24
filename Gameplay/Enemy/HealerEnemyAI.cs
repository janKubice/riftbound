using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class HealerEnemyAI : EnemyBaseAI
{
    [Header("Healer Settings")]
    [SerializeField] private float _auraRadius = 5f;
    [SerializeField] private float _tickRate = 1.5f;
    [SerializeField] private float _seekRadius = 15f;
    [SerializeField] private StatusEffectData _healAuraEffect;
    [SerializeField] private LayerMask _enemyLayer;

    private static readonly Collider[] _alliesInRange = new Collider[30];
    private EnemyHealth _targetAlly;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            // Trvalé vyřazení z globálního Flow Field trasování
            IsMovementPaused = true;
            StartCoroutine(AuraTickRoutine());
            StartCoroutine(TargetSelectionRoutine());
        }
    }

    private IEnumerator AuraTickRoutine()
    {
        yield return new WaitForSeconds(Random.Range(0f, _tickRate));
        var wait = new WaitForSeconds(_tickRate);
        while (true)
        {
            yield return wait;
            if (_isSpawning.Value || _health.CurrentHealth.Value <= 0) continue;
            ApplyAura();
        }
    }

    private void ApplyAura()
    {
        int hits = Physics.OverlapSphereNonAlloc(transform.position, _auraRadius, _alliesInRange, _enemyLayer);
        for (int i = 0; i < hits; i++)
        {
            Collider col = _alliesInRange[i];
            if (col.TryGetComponent(out StatusEffectReceiver receiver))
            {
                // receiver.ApplyEffect(_healAuraEffect);
            }
        }
    }

    private IEnumerator TargetSelectionRoutine()
    {
        // Posun startu rozloží zátěž napříč mnoha framy
        yield return new WaitForSeconds(Random.Range(0f, 2.0f));
        var wait = new WaitForSeconds(1.5f);
        while (true)
        {
            yield return wait;
            if (_isSpawning.Value) continue;
            FindMostInjuredAlly();
        }
    }

    private void FindMostInjuredAlly()
    {
        int hits = Physics.OverlapSphereNonAlloc(transform.position, _seekRadius, _alliesInRange, _enemyLayer);

        float lowestHealthPct = 1f;
        _targetAlly = null;

        for (int i = 0; i < hits; i++)
        {
            Collider col = _alliesInRange[i];
            if (col.gameObject == gameObject) continue;

            if (col.TryGetComponent(out EnemyHealth allyHealth) && allyHealth.IsInjured)
            {
                float healthPct = (float)allyHealth.CurrentHealth.Value / allyHealth.MaxHealth;
                if (healthPct < lowestHealthPct)
                {
                    lowestHealthPct = healthPct;
                    _targetAlly = allyHealth;
                }
            }
        }
    }

    // Explicitní přepsání pohybu. Kód spouští rodičovský Update() v EnemyBaseAI.
    public override void BehaviorLogic()
    {
        if (TargetPlayer == null) return;

        Vector3 targetPosition;

        if (_targetAlly != null && _targetAlly.CurrentHealth.Value > 0)
        {
            targetPosition = _targetAlly.transform.position;
        }
        else
        {
            // Útěk od hráče (udržování bezpečné vzdálenosti 10 jednotek)
            Vector3 directionAwayFromPlayer = (transform.position - TargetPlayer.position).normalized;
            targetPosition = TargetPlayer.position + (directionAwayFromPlayer * 10f);
        }

        Vector3 moveDirection = targetPosition - transform.position;
        moveDirection.y = 0;

        if (moveDirection.sqrMagnitude > 0.5f)
        {
            ManualMove(moveDirection.normalized * CurrentSpeed);
        }
    }
}