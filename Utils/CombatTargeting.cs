using UnityEngine;
using Unity.Netcode;

public enum CombatTeam
{
    Unknown,
    Player,
    Enemy
}

public static class CombatTargeting
{
    public static CombatTeam GetTeam(NetworkObject obj)
    {
        if (obj == null) return CombatTeam.Unknown;
        return GetTeam(obj.gameObject);
    }

    public static CombatTeam GetTeam(Component component)
    {
        if (component == null) return CombatTeam.Unknown;
        return GetTeam(component.gameObject);
    }

    public static CombatTeam GetTeam(GameObject go)
    {
        if (go == null) return CombatTeam.Unknown;

        Transform root = go.transform.root;

        if (root.CompareTag("Player") || go.CompareTag("Player"))
            return CombatTeam.Player;

        if (root.CompareTag("Enemy") || go.CompareTag("Enemy"))
            return CombatTeam.Enemy;

        if (go.GetComponentInParent<PlayerAttributes>() != null)
            return CombatTeam.Player;

        if (go.GetComponentInParent<EnemyHealth>() != null ||
            go.GetComponentInParent<EnemyBaseAI>() != null)
            return CombatTeam.Enemy;

        return CombatTeam.Unknown;
    }

    public static ulong GetDamageCreditClientId(NetworkObject attacker)
    {
        if (attacker == null)
            return ulong.MaxValue;

        CombatTeam team = GetTeam(attacker);

        // Pouze hráčský útok má dostat reálné OwnerClientId.
        // Enemy útok posíláme jako ulong.MaxValue, aby host player nebyl chráněn self-damage kontrolou.
        return team == CombatTeam.Player
            ? attacker.OwnerClientId
            : ulong.MaxValue;
    }

    public static bool IsSelf(NetworkObject attacker, Component target)
    {
        if (attacker == null || target == null)
            return false;

        NetworkObject targetNetObj = target.GetComponentInParent<NetworkObject>();

        return targetNetObj != null &&
               targetNetObj.NetworkObjectId == attacker.NetworkObjectId;
    }

    public static bool IsFriendly(NetworkObject attacker, Component target)
    {
        if (attacker == null || target == null)
            return false;

        CombatTeam attackerTeam = GetTeam(attacker);
        CombatTeam targetTeam = GetTeam(target);

        if (attackerTeam == CombatTeam.Unknown || targetTeam == CombatTeam.Unknown)
            return false;

        return attackerTeam == targetTeam;
    }

    public static bool CanDamage(NetworkObject attacker, Component target)
    {
        if (attacker == null || target == null)
            return false;

        if (IsSelf(attacker, target))
            return false;

        CombatTeam attackerTeam = GetTeam(attacker);
        CombatTeam targetTeam = GetTeam(target);

        return attackerTeam switch
        {
            CombatTeam.Player => targetTeam == CombatTeam.Enemy,
            CombatTeam.Enemy => targetTeam == CombatTeam.Player,
            _ => false
        };
    }

    public static bool TryDamage(Collider targetCollider, NetworkObject attacker, int damage, out GameObject damagedObject)
    {
        damagedObject = null;

        if (targetCollider == null || attacker == null)
            return false;

        if (!CanDamage(attacker, targetCollider))
            return false;

        ulong damageCreditId = GetDamageCreditClientId(attacker);

        EnemyHealth enemy = targetCollider.GetComponentInParent<EnemyHealth>();
        if (enemy != null)
        {
            enemy.TakeDamage(damage, damageCreditId);
            damagedObject = enemy.gameObject;
            return true;
        }

        PlayerAttributes player = targetCollider.GetComponentInParent<PlayerAttributes>();
        if (player != null)
        {
            player.TakeDamageServerRpc(damage, damageCreditId);
            damagedObject = player.gameObject;
            return true;
        }

        return false;
    }

    public static bool TryDamage(GameObject target, NetworkObject attacker, int damage, out GameObject damagedObject)
    {
        damagedObject = null;

        if (target == null || attacker == null)
            return false;

        if (!CanDamage(attacker, target.transform))
            return false;

        ulong damageCreditId = GetDamageCreditClientId(attacker);

        EnemyHealth enemy = target.GetComponentInParent<EnemyHealth>();
        if (enemy != null)
        {
            enemy.TakeDamage(damage, damageCreditId);
            damagedObject = enemy.gameObject;
            return true;
        }

        PlayerAttributes player = target.GetComponentInParent<PlayerAttributes>();
        if (player != null)
        {
            player.TakeDamageServerRpc(damage, damageCreditId);
            damagedObject = player.gameObject;
            return true;
        }

        return false;
    }
}