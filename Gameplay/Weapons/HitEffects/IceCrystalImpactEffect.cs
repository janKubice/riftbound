using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

[CreateAssetMenu(menuName = "Effects/Ice Crystal Impact")]
public class IceCrystalImpactEffect : HitEffect
{
    [Header("Visual")]
    [Tooltip("Prefab ice crystal efektu. Ideálně NetworkObject, aby ho viděli všichni klienti.")]
    public GameObject IceCrystalPrefab;

    [Tooltip("Y offset od detekovaného bodu země. Pokud má prefab pivot přesně v bodě dopadu, nech 0.")]
    public float VisualGroundOffset = 0f;

    [Tooltip("Za jak dlouho po spawnu efekt skutečně zasáhne cíle.")]
    public float ImpactDelay = 0.5f;

    [Tooltip("Za jak dlouho se visual prefab odstraní. Dej podle délky particle animace.")]
    public float DestroyVisualAfter = 4f;

    [Header("Ground Detection")]
    [Tooltip("Vrstvy, které se považují za zem.")]
    public LayerMask GroundLayers;

    [Tooltip("Jak vysoko nad hitem začne raycast dolů hledat zem.")]
    public float GroundRayStartHeight = 25f;

    [Tooltip("Jak daleko dolů se hledá zem.")]
    public float GroundRayDistance = 80f;

    [Tooltip("Pokud true, prefab se natočí podle normály země.")]
    public bool AlignToGroundNormal = false;

    [Header("Impact")]
    [Tooltip("Poloměr zásahu kolem místa dopadu.")]
    public float ImpactRadius = 4f;

    [Tooltip("Vrstvy, které může krystal zasáhnout. Typicky Enemy, případně Player/Destructible.")]
    public LayerMask TargetLayers;

    [Tooltip("Jestli se mají počítat i trigger collidery.")]
    public QueryTriggerInteraction TriggerInteraction = QueryTriggerInteraction.Collide;

    [Header("Damage")]
    [Tooltip("Pokud true, přičte damage z CurrentRuntimeStats zbraně.")]
    public bool UseWeaponStatsDamage = true;

    [Tooltip("Fixní bonus damage navíc.")]
    public int BaseDamage = 0;

    [Tooltip("Násobič finálního damage. 1 = normální damage, 0.5 = poloviční.")]
    public float DamageMultiplier = 1f;

    [Header("Status Effect")]
    [Tooltip("Volitelný status efekt, např. Freeze/Slow/Chill.")]
    public StatusEffectData StatusToApply;

    [Header("Payload")]
    [Tooltip("Pokud true, všechny zbývající on-hit efekty se aplikují na každý zasažený cíl.")]
    public bool ExecuteRemainingPayloadOnEachTarget = true;

    private static readonly Collider[] _hitBuffer = new Collider[128];

    public override void OnHit(
        Vector3 hitPosition,
        GameObject target,
        NetworkObject attacker,
        WeaponManager manager,
        List<HitEffect> remainingPayload)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        if (IceCrystalPrefab == null)
        {
            Debug.LogWarning("[IceCrystalImpactEffect] Missing IceCrystalPrefab.");
            return;
        }

        if (attacker == null)
        {
            Debug.LogWarning("[IceCrystalImpactEffect] Missing attacker.");
            return;
        }

        if (manager == null)
        {
            Debug.LogWarning("[IceCrystalImpactEffect] Missing WeaponManager. Cannot run delayed impact coroutine.");
            return;
        }

        Vector3 searchPosition = target != null ? target.transform.position : hitPosition;

        Vector3 groundPoint = ResolveGroundPoint(searchPosition, out Vector3 groundNormal);
        Quaternion rotation = AlignToGroundNormal
            ? Quaternion.FromToRotation(Vector3.up, groundNormal)
            : Quaternion.identity;

        Vector3 visualPosition = groundPoint + Vector3.up * VisualGroundOffset;

        GameObject visual = Instantiate(IceCrystalPrefab, visualPosition, rotation);

        if (visual.TryGetComponent(out NetworkObject visualNetObj))
        {
            visualNetObj.Spawn(true);
        }
        else
        {
            Debug.LogWarning("[IceCrystalImpactEffect] IceCrystalPrefab has no NetworkObject. Clients may not see the visual.");
        }

        List<HitEffect> payloadSnapshot = remainingPayload != null
            ? new List<HitEffect>(remainingPayload)
            : null;

        manager.StartCoroutine(DelayedImpact(
            groundPoint,
            attacker,
            manager,
            payloadSnapshot
        ));

        if (DestroyVisualAfter > 0f)
        {
            manager.StartCoroutine(DestroyVisualLater(visual, DestroyVisualAfter));
        }
    }

    private IEnumerator DelayedImpact(
        Vector3 impactPoint,
        NetworkObject attacker,
        WeaponManager manager,
        List<HitEffect> payloadSnapshot)
    {
        if (ImpactDelay > 0f)
            yield return new WaitForSeconds(ImpactDelay);

        int finalDamage = CalculateDamage(manager);

        int hitCount = Physics.OverlapSphereNonAlloc(
            impactPoint,
            ImpactRadius,
            _hitBuffer,
            TargetLayers,
            TriggerInteraction
        );

        HashSet<GameObject> alreadyHit = new HashSet<GameObject>();

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _hitBuffer[i];
            if (col == null)
                continue;

            GameObject actualTarget = null;
            Vector3 targetHitPosition = col.bounds.center;
            bool validHit = false;

            if (col.TryGetComponent(out EnemyHealth enemy) || (enemy = col.GetComponentInParent<EnemyHealth>()))
            {
                actualTarget = enemy.gameObject;

                if (!alreadyHit.Add(actualTarget))
                    continue;

                enemy.TakeDamage(finalDamage, attacker.OwnerClientId);

                if (StatusToApply != null && StatusToApply.Type != StatusEffectType.None)
                {
                    enemy.ApplyStatusEffect(StatusToApply);
                }

                validHit = true;
            }
            else if (col.TryGetComponent(out PlayerAttributes player) || (player = col.GetComponentInParent<PlayerAttributes>()))
            {
                actualTarget = player.gameObject;

                if (!alreadyHit.Add(actualTarget))
                    continue;

                if (player.NetworkObjectId != attacker.NetworkObjectId)
                {
                    player.TakeDamageServerRpc(finalDamage, attacker.OwnerClientId);
                    validHit = true;
                }
            }
            else if (col.TryGetComponent(out DestructibleProp prop) || (prop = col.GetComponentInParent<DestructibleProp>()))
            {
                actualTarget = prop.gameObject;

                if (!alreadyHit.Add(actualTarget))
                    continue;

                prop.TakeHit();
                validHit = true;
            }

            if (validHit && ExecuteRemainingPayloadOnEachTarget)
            {
                ExecutePayloadOnTarget(
                    targetHitPosition,
                    actualTarget,
                    attacker,
                    manager,
                    payloadSnapshot
                );
            }
        }

        System.Array.Clear(_hitBuffer, 0, hitCount);
    }

    private int CalculateDamage(WeaponManager manager)
    {
        int damage = BaseDamage;

        if (UseWeaponStatsDamage && manager != null)
        {
            damage += manager.CurrentRuntimeStats.Damage;
        }

        damage = Mathf.RoundToInt(damage * DamageMultiplier);
        return Mathf.Max(0, damage);
    }

    private Vector3 ResolveGroundPoint(Vector3 searchPosition, out Vector3 groundNormal)
    {
        Vector3 rayStart = searchPosition + Vector3.up * GroundRayStartHeight;

        if (Physics.Raycast(
            rayStart,
            Vector3.down,
            out RaycastHit hit,
            GroundRayDistance,
            GroundLayers,
            QueryTriggerInteraction.Ignore))
        {
            groundNormal = hit.normal;
            return hit.point;
        }

        groundNormal = Vector3.up;

        Debug.LogWarning(
            "[IceCrystalImpactEffect] Ground was not found. Falling back to original hit position. " +
            "Check GroundLayers / GroundRayStartHeight / GroundRayDistance."
        );

        return searchPosition;
    }

    private void ExecutePayloadOnTarget(
        Vector3 hitPosition,
        GameObject target,
        NetworkObject attacker,
        WeaponManager manager,
        List<HitEffect> payloadSnapshot)
    {
        if (payloadSnapshot == null || payloadSnapshot.Count == 0)
            return;

        HitEffect nextEffect = payloadSnapshot[0];
        if (nextEffect == null)
            return;

        List<HitEffect> nextPayload = new List<HitEffect>(payloadSnapshot.Count - 1);

        for (int i = 1; i < payloadSnapshot.Count; i++)
        {
            nextPayload.Add(payloadSnapshot[i]);
        }

        nextEffect.OnHit(hitPosition, target, attacker, manager, nextPayload);
    }

    private IEnumerator DestroyVisualLater(GameObject visual, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (visual == null)
            yield break;

        if (visual.TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
        {
            netObj.Despawn(true);
        }
        else
        {
            Destroy(visual);
        }
    }

    public override string GetDescription()
    {
        string damageText = UseWeaponStatsDamage
            ? $"Weapon Damage + {BaseDamage}"
            : BaseDamage.ToString();

        string statusText = StatusToApply != null && StatusToApply.Type != StatusEffectType.None
            ? $" Applies <color=#66CCFF>{StatusToApply.Type}</color>."
            : "";

        return $"<color=#88DDFF><b>Ice Crystal:</b></color> Summons a falling crystal that impacts after " +
               $"<color=white>{ImpactDelay:F1}s</color>, dealing <color=#FF4444>{damageText}</color> " +
               $"in a <color=white>{ImpactRadius:F1}m</color> area.{statusText}";
    }
}