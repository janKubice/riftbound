using System.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public abstract class EnemySpellBehaviour : NetworkBehaviour
{
    protected ulong SourceClientId { get; private set; } = ulong.MaxValue;

    protected void InitializeSpellBase(ulong sourceClientId)
    {
        SourceClientId = sourceClientId;
    }

    protected PlayerAttributes FindPlayer(Collider other)
    {
        if (other == null)
            return null;

        return other.GetComponentInParent<PlayerAttributes>();
    }

    protected NetworkObject FindNetworkObject(Collider other)
    {
        if (other == null)
            return null;

        return other.GetComponentInParent<NetworkObject>();
    }

    protected StatusEffectReceiver FindStatusReceiver(Component target)
    {
        if (target == null)
            return null;

        return target.GetComponentInParent<StatusEffectReceiver>();
    }

    protected void DamagePlayerFromServer(PlayerAttributes player, int damage)
    {
        if (!IsServer)
            return;

        if (player == null || damage <= 0)
            return;

        // Důležité:
        // nepředáváme SourceClientId jako attackerId,
        // protože enemy spell typicky patří serveru a host player by se jinak mohl ignorovat.
        player.TakeDamageServerRpc(damage);
    }

    protected void ApplyStatusFromServer(Component target, StatusEffectData statusEffect)
    {
        if (!IsServer)
            return;

        if (target == null || statusEffect == null)
            return;

        StatusEffectReceiver receiver = FindStatusReceiver(target);

        if (receiver == null)
            return;

        receiver.ApplyStatusEffect(statusEffect);
    }

    protected IEnumerator ServerDespawnAfter(float lifetime)
    {
        yield return new WaitForSeconds(Mathf.Max(0.05f, lifetime));

        if (!IsServer)
            yield break;

        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
        else
            Destroy(gameObject);
    }
}