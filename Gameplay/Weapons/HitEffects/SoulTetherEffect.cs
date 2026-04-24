using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Effects/Soul Tether (Chain Link)")]
public class SoulTetherEffect : HitEffect
{
    [Header("Tether Settings")]
    [Tooltip("Kolik dalších nepřátel se má svázat s původním cílem?")]
    public int MaxLinkedTargets = 4;

    [Tooltip("Jak daleko může vlákno dosáhnout?")]
    public float LinkRadius = 15f;

    [Tooltip("Vrstvy nepřátel pro vyhledávání")]
    public LayerMask EnemyLayer;

    [Header("Visuals (Optional)")]
    [Tooltip("Prefab vlákna (např. LineRenderer), který spojí cíle")]
    public GameObject TetherVisualPrefab;

    private static readonly Collider[] _hitBuffer = new Collider[30];

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager, List<HitEffect> remainingPayload)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        // KASKÁDOVÁNÍ: Pokud už nemáme žádné další efekty, nemá smysl cíle svazovat
        if (remainingPayload == null || remainingPayload.Count == 0) return;

        // 1. Spustíme zbylé efekty na PŮVODNÍM cíli
        ExecutePayloadOnTarget(target, hitPosition, attacker, manager, remainingPayload);

        // 2. Najdeme další cíle pro svázání
        int hitCount = Physics.OverlapSphereNonAlloc(hitPosition, LinkRadius, _hitBuffer, EnemyLayer);
        int linksCreated = 0;

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _hitBuffer[i];
            GameObject potentialTarget = col.gameObject;

            // Ignorujeme původní cíl (už ho máme vyřešený nahoře)
            if (potentialTarget == target || col.transform.root == target.transform.root) continue;

            // Zkontrolujeme, zda má cíl EnemyHealth a žije
            if (col.TryGetComponent(out EnemyHealth enemy) || (enemy = col.GetComponentInParent<EnemyHealth>()))
            {
                if (enemy.CurrentHealth.Value <= 0) continue;

                // Spustíme zbylé efekty i na TOMTO novém cíli
                ExecutePayloadOnTarget(enemy.gameObject, col.bounds.center, attacker, manager, remainingPayload);

                // Spawn vizuálního vlákna mezi cíli
                SpawnTetherVisual(hitPosition, col.bounds.center);

                linksCreated++;
                if (linksCreated >= MaxLinkedTargets) break;
            }
        }

        System.Array.Clear(_hitBuffer, 0, hitCount);
    }

    private void ExecutePayloadOnTarget(GameObject target, Vector3 pos, NetworkObject attacker, WeaponManager manager, List<HitEffect> payloadToExecute)
    {
        // Vezmeme PRVNÍ efekt z batohu
        HitEffect nextEffect = payloadToExecute[0];

        // Vytvoříme zbytek fronty
        List<HitEffect> nextPayload = new List<HitEffect>();
        for (int i = 1; i < payloadToExecute.Count; i++)
        {
            nextPayload.Add(payloadToExecute[i]);
        }

        // Odpálíme
        if (nextEffect != null)
        {
            nextEffect.OnHit(pos, target, attacker, manager, nextPayload);
        }
    }

    private void SpawnTetherVisual(Vector3 startPos, Vector3 endPos)
    {
        if (TetherVisualPrefab == null) return;

        // Lokální spawn vizuálu u všech klientů (přes ClientRpc, pokud ho přidáš do pomocného skriptu, nebo rovnou NetworkObject)
        GameObject tether = Instantiate(TetherVisualPrefab, startPos, Quaternion.identity);

        // Pokud máš na prefabu LineRenderer, nastavíme mu body
        if (tether.TryGetComponent(out LineRenderer line))
        {
            line.SetPosition(0, startPos);
            line.SetPosition(1, endPos);
        }

        if (tether.TryGetComponent(out NetworkObject netObj))
        {
            netObj.Spawn(true);
        }

        // Zničení vizuálu po chvíli
        Destroy(tether, 1.5f);
    }

    public override string GetDescription()
    {
        return $"<color=#9370DB><b>Soul Tether:</b></color> Links the target with up to <color=white>{MaxLinkedTargets}</color> " +
               $"nearby enemies within <color=white>{LinkRadius}m</color>, " +
               $"applying all remaining hit effects to <color=white>all</color> linked targets.";
    }
}