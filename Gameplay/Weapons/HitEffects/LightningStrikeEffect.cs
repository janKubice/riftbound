using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Effects/Lightning Strike")]
public class LightningStrikeEffect : HitEffect
{
    [Header("Proc Settings")]
    [Tooltip("Pravděpodobnost spuštění blesku (0.0 = 0%, 1.0 = 100%)")]
    [Range(0f, 1f)]
    public float ProcChance = 0.2f;

    [Header("Combat Stats")]
    public int BonusDamage = 50;

    [Header("Visuals")]
    [Tooltip("Prefab blesku (musí mít NetworkObject a skript LightningVisual)")]
    public GameObject LightningVisualPrefab;

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager, List<HitEffect> remainingPayload)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        // 1. VYHODNOCENÍ BLESKU
        if (Random.value <= ProcChance)
        {
            ApplyDamage(target, attacker.OwnerClientId);
            SpawnVisual(target.transform.position);
        }

        // 2. KASKÁDOVÁNÍ (Pokračování řetězce)
        // Předáme zbylé efekty dál na ten samý cíl
        if (remainingPayload != null && remainingPayload.Count > 0)
        {
            // Vezmeme další efekt na řadě
            HitEffect nextEffect = remainingPayload[0];

            // Vytvoříme zbytek fronty (posuneme index)
            List<HitEffect> nextPayload = new List<HitEffect>();
            for (int i = 1; i < remainingPayload.Count; i++)
            {
                nextPayload.Add(remainingPayload[i]);
            }

            // Okamžitě spustíme další efekt na tomto stejném cíli
            if (nextEffect != null)
            {
                nextEffect.OnHit(hitPosition, target, attacker, manager, nextPayload);
            }
        }
    }

    private void ApplyDamage(GameObject target, ulong attackerId)
    {
        if (target.TryGetComponent(out EnemyHealth enemy) || (enemy = target.GetComponentInParent<EnemyHealth>()))
        {
            enemy.TakeDamage(BonusDamage, attackerId);
        }
        else if (target.TryGetComponent(out PlayerAttributes player) && player.OwnerClientId != attackerId)
        {
            player.TakeDamageServerRpc(BonusDamage, attackerId);
        }
    }

    /// <summary>
    /// Spawne vizuální efekt blesku na dané pozici. Prefab musí mít skript LightningVisual, který se postará o animaci a zničení objektu po animaci.
    /// </summary>
    /// <param name="position"></param>
    private void SpawnVisual(Vector3 position)
    {
        if (LightningVisualPrefab == null) return;

        GameObject lightning = Instantiate(LightningVisualPrefab, position, Quaternion.identity);
        if (lightning.TryGetComponent(out NetworkObject netObj))
        {
            netObj.Spawn(true);
        }
    }

    public override string GetDescription()
    {
        return $"<color=#FFFF00><b>Lightning Strike:</b></color> <color=white>{ProcChance * 100:F0}%</color> chance " +
               $"to strike the target with holy lightning, dealing <color=#FF4444>{BonusDamage} flat damage</color>.";
    }
}