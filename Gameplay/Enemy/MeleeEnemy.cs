using UnityEngine;
using System.Collections;
using Unity.Netcode;

[RequireComponent(typeof(NetworkedAudioSource))]
public class MeleeEnemy : EnemyBaseAI
{
    [Header("Melee Stats")]
    [SerializeField] private float _attackRange = 2.0f;
    [SerializeField] private int _damage = 10;
    [SerializeField] private float _attackCooldown = 2.0f;

    [Header("Headbutt Animation")]
    [Tooltip("Jak dlouho trvá nápřah (trackuje hráče)")]
    [SerializeField] private float _windupTime = 0.6f;
    [Tooltip("Pauza před úderem (přestane se točit - čas na úhyb)")]
    [SerializeField] private float _lockTime = 0.1f;
    [Tooltip("Rychlost úderu")]
    [SerializeField] private float _strikeTime = 0.15f;

    [Space]
    [Tooltip("Záklon hlavy dozadu (záporné číslo)")]
    [SerializeField] private float _maxLeanBackAngle = -40f;
    [Tooltip("Předklon při úderu (kladné číslo)")]
    [SerializeField] private float _maxHeadbuttAngle = 55f;
    [SerializeField] private AnimationCurve _windupCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Visual Effects")]
    [Tooltip("Efekt nabíjení (připojí se k hlavě)")]
    [SerializeField] private GameObject _chargeVFXPrefab;
    [Tooltip("Efekt zásahu (krev/prach)")]
    [SerializeField] private GameObject _hitVFXPrefab;
    [Tooltip("Kost hlavy (pokud není, použije se root)")]
    [SerializeField] private Transform _headTransform;

    [Header("Audio")]
    [Tooltip("Index 2 = Attack Shout")]
    [SerializeField] private int _attackSoundIndex = 2;



    // Ukládáme původní rotaci modelu (fix pro modely s offsetem)
    private Quaternion _baseLocalRotation;
    private Transform _visualRoot;
    private NetworkedAudioSource _netAudio;

    private float _lastAttackTime;
    private bool _isAttacking = false;
    private float _attackRangeSqr;

    protected override void Awake()
    {
        base.Awake();
        _attackRangeSqr = _attackRange * _attackRange;
        _netAudio = GetComponent<NetworkedAudioSource>();

        // 1. Najdeme vizuální model
        if (_modelRenderer != null)
            _visualRoot = _modelRenderer.transform;
        else
            _visualRoot = transform.GetChild(0);

        // 2. Uložíme si, jak byl model otočený v Inspectoru (KLÍČOVÁ OPRAVA)
        _baseLocalRotation = _visualRoot.localRotation;
    }

    public override void BehaviorLogic()
    {
        if (_isAttacking) return;

        float distSqr = (MyTransform.position - TargetPlayer.position).sqrMagnitude;

        if (distSqr <= _attackRangeSqr)
        {
            if (Time.time >= _lastAttackTime + _attackCooldown)
            {
                StartCoroutine(HeadbuttAttackRoutine());
            }
            else
            {
                // Cooldown: Jen se točím na hráče
                IsMovementPaused = true;
                RotateTowardsTarget();
            }
        }
        else
        {
            IsMovementPaused = false; // Běžím k hráči
        }
    }

    // Vlastní metoda pro rotaci, abychom nespoléhali na base class
    private void RotateTowardsTarget()
    {
        if (TargetPlayer == null) return;

        Vector3 dir = (TargetPlayer.position - transform.position).normalized;
        dir.y = 0; // Nechceme se naklánět nahoru/dolů celým tělem

        if (dir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            // Rychlá rotace, ale ne instantní
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, 720f * Time.deltaTime);
        }
    }

    private IEnumerator HeadbuttAttackRoutine()
    {
        _isAttacking = true;
        IsMovementPaused = true;
        _lastAttackTime = Time.time;

        // --- FÁZE 1: WINDUP (Nápřah + Tracking) ---
        // Spustíme VFX nabíjení
        SpawnChargeVFXClientRpc();

        float timer = 0f;

        while (timer < _windupTime)
        {
            timer += Time.deltaTime;
            float progress = timer / _windupTime;
            float curveVal = _windupCurve.Evaluate(progress);

            // Výpočet úhlu záklonu
            float currentPitch = Mathf.Lerp(0, _maxLeanBackAngle, curveVal);

            // APLIKACE ROTACE: Nejdřív Pitch, pak Base Rotation
            // Tím zajistíme, že se nakloní "dopředu" z pohledu modelu, ať je otočený jakkoliv
            _visualRoot.localRotation = Quaternion.Euler(currentPitch, 0, 0) * _baseLocalRotation;

            // Důležité: Stále se točíme za hráčem (Tracking)
            RotateTowardsTarget();

            yield return null;
        }

        // --- FÁZE 2: LOCK (Zamknutí cíle) ---
        // Krátká pauza, kdy se nepřítel přestane točit.
        // Hráč má teď šanci uskočit do strany (Skill check).
        yield return new WaitForSeconds(_lockTime);

        // --- FÁZE 3: STRIKE (Úder) ---
        // Zvuk útoku ("Huuuuh!")
        if (_netAudio != null) _netAudio.PlayOneShotNetworked(_attackSoundIndex);

        timer = 0f;
        Quaternion preAttackRot = _visualRoot.localRotation;
        // Cílová rotace (hlava dopředu + base rotace)
        Quaternion strikeRot = Quaternion.Euler(_maxHeadbuttAngle, 0, 0) * _baseLocalRotation;

        while (timer < _strikeTime)
        {
            timer += Time.deltaTime;
            float progress = timer / _strikeTime;
            // Exponenciální zrychlení (prudký úder)
            progress = progress * progress;

            _visualRoot.localRotation = Quaternion.Lerp(preAttackRot, strikeRot, progress);

            // ZDE UŽ NENÍ RotateTowardsTarget() -> Útočí rovně tam, kam se díval naposled
            yield return null;
        }

        // --- FÁZE 4: DAMAGE & IMPACT ---
        CheckForHit();

        // --- FÁZE 5: RECOVERY (Návrat) ---
        timer = 0f;
        float recoveryTime = 0.5f;
        Quaternion currentRot = _visualRoot.localRotation;

        while (timer < recoveryTime)
        {
            timer += Time.deltaTime;
            // Pomalý návrat do základní rotace
            _visualRoot.localRotation = Quaternion.Lerp(currentRot, _baseLocalRotation, timer / recoveryTime);
            yield return null;
        }

        // Jistota na závěr
        _visualRoot.localRotation = _baseLocalRotation;

        _isAttacking = false;
        IsMovementPaused = false;
    }

    private void CheckForHit()
    {
        if (TargetPlayer == null) return;

        // Získání bodu úderu (před nepřítelem)
        Vector3 strikePoint = transform.position + transform.forward * 1.2f;

        // Kvadratická vzdálenost eliminuje náročnou operaci odmocniny (Vector3.Distance)
        float distSqr = (strikePoint - TargetPlayer.position).sqrMagnitude;

        // Porovnání s druhou mocninou poloměru zásahu (1.5 * 1.5 = 2.25)
        if (distSqr <= 2.25f)
        {
            if (TargetPlayer.TryGetComponent(out PlayerAttributes player))
            {
                player.TakeDamageServerRpc(_damage);

                // Lokální aplikace VFX bez RPC
                Vector3 hitPos = TargetPlayer.position + Vector3.up * 1.0f;
                SpawnHitVFXClientRpc(hitPos);
            }
        }
    }

    [ClientRpc]
    private void SpawnChargeVFXClientRpc()
    {
        if (_chargeVFXPrefab == null) return;
        Transform spawnPoint = _headTransform != null ? _headTransform : transform;

        GameObject vfx = Instantiate(_chargeVFXPrefab, spawnPoint.position, spawnPoint.rotation);
        vfx.transform.SetParent(spawnPoint); // Připnout k hlavě
        Destroy(vfx, _windupTime + 0.2f);
    }

    [ClientRpc]
    private void SpawnHitVFXClientRpc(Vector3 position)
    {
        if (_hitVFXPrefab == null) return;
        // Krev stříkne ve směru úderu
        Instantiate(_hitVFXPrefab, position, Quaternion.LookRotation(transform.forward));
    }
}