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


    // --- PRIVÁTNÍ PROMĚNNÉ ---
    private Quaternion _baseLocalRotation;
    private Quaternion _facingRotation; // Čisté natočení k hráči (osa Y)
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

        // 1. Získáme správný vizuální kořen bez ohledu na strukturu objektů
        if (_modelRenderer != null)
        {
            _visualRoot = _modelRenderer.transform;
        }
        else
        {
            // Pokud není renderer přiřazen, zkusíme najít prvního potomka. 
            // Pokud nemá potomky, fallback na samotný root.
            _visualRoot = transform.childCount > 0 ? transform.GetChild(0) : transform;
        }

        // 2. Uložíme výchozí rotace pro správné matematické operace
        _baseLocalRotation = _visualRoot.localRotation;
        _facingRotation = transform.rotation;
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

    // Vypočítá plynulou rotaci za hráčem (pouze v ose Y)
    private void RotateTowardsTarget()
    {
        if (TargetPlayer == null) return;

        Vector3 dir = (TargetPlayer.position - transform.position).normalized;
        dir.y = 0; // Zabráníme naklánění celého těla do země/do vzduchu

        if (dir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            // Rotaci zatím ukládáme jen do pomocné proměnné
            _facingRotation = Quaternion.RotateTowards(_facingRotation, targetRot, 720f * Time.deltaTime);
        }

        // Pokud neútočíme, rovnou otáčíme celým objektem
        if (!_isAttacking)
        {
            transform.rotation = _facingRotation;
        }
    }

    // Centrální metoda pro čistou aplikaci rotace (Otáčení za hráčem + Animace náklonu)
    private void ApplyAttackRotation(float currentPitch)
    {
        if (_visualRoot == transform)
        {
            // Model je přímo na hlavním objektu. Musíme obě rotace sečíst do jedné globální rotace.
            transform.rotation = _facingRotation * Quaternion.Euler(currentPitch, 0, 0);
        }
        else
        {
            // Model je Child (ideální struktura).
            // Hlavní tělo se otáčí za hráčem, hlava/model se naklání lokálně.
            transform.rotation = _facingRotation;
            _visualRoot.localRotation = Quaternion.Euler(currentPitch, 0, 0) * _baseLocalRotation;
        }
    }

    private IEnumerator HeadbuttAttackRoutine()
    {
        _isAttacking = true;
        IsMovementPaused = true;
        _lastAttackTime = Time.time;

        SpawnChargeVFXClientRpc();
        float timer = 0f;

        // --- FÁZE 1: WINDUP (Nápřah + Tracking) ---
        while (timer < _windupTime)
        {
            timer += Time.deltaTime;
            float progress = timer / _windupTime;
            float curveVal = _windupCurve.Evaluate(progress);
            float currentPitch = Mathf.Lerp(0, _maxLeanBackAngle, curveVal);

            // Spočítáme rotaci k hráči a rovnou ji smícháme s náklonem
            RotateTowardsTarget();
            ApplyAttackRotation(currentPitch);
            
            yield return null;
        }

        // --- FÁZE 2: LOCK (Zamknutí cíle) ---
        // Už nevoláme RotateTowardsTarget, cíl má čas uhnout
        yield return new WaitForSeconds(_lockTime);

        // --- FÁZE 3: STRIKE (Úder) ---
        if (_netAudio != null) _netAudio.PlayOneShotNetworked(_attackSoundIndex);

        timer = 0f;
        float startPitch = _maxLeanBackAngle;

        while (timer < _strikeTime)
        {
            timer += Time.deltaTime;
            float progress = timer / _strikeTime;
            progress = progress * progress; // Exponenciální zrychlení úderu

            float currentPitch = Mathf.Lerp(startPitch, _maxHeadbuttAngle, progress);
            ApplyAttackRotation(currentPitch);
            
            yield return null;
        }

        // --- FÁZE 4: DAMAGE & IMPACT ---
        CheckForHit();

        // --- FÁZE 5: RECOVERY (Návrat) ---
        timer = 0f;
        float recoveryTime = 0.5f;

        while (timer < recoveryTime)
        {
            timer += Time.deltaTime;
            float currentPitch = Mathf.Lerp(_maxHeadbuttAngle, 0f, timer / recoveryTime);
            ApplyAttackRotation(currentPitch);
            
            yield return null;
        }

        // --- ČISTÝ RESET NA KONCI ÚTOKU ---
        if (_visualRoot != transform)
        {
            _visualRoot.localRotation = _baseLocalRotation;
        }
        transform.rotation = _facingRotation;
        
        _isAttacking = false;
        IsMovementPaused = false;
    }

    private void CheckForHit()
    {
        if (TargetPlayer == null) return;

        // 1. Získáme směr dopředu z čisté rotace k hráči (ignorujeme náklon těla/hlavy do země)
        Vector3 forwardDir = _facingRotation * Vector3.forward;
        
        // Získání bodu úderu
        Vector3 strikePoint = transform.position + forwardDir * 1.2f;

        // 2. Zarovnáme pozici hráče do stejné výšky jako strikePoint. 
        // Tím měříme vzdálenost čistě ve 2D (osy X a Z), což eliminuje chybování na schodech/kopečcích.
        Vector3 flatPlayerPosition = new Vector3(TargetPlayer.position.x, strikePoint.y, TargetPlayer.position.z);

        // Kvadratická vzdálenost
        float distSqr = (strikePoint - flatPlayerPosition).sqrMagnitude;

        if (distSqr <= 2.25f)
        {
            // 3. Hledáme atributy v nadřazené struktuře (řeší problém, kdy TargetPlayer je Child objekt hráče)
            PlayerAttributes player = TargetPlayer.GetComponentInParent<PlayerAttributes>();
            
            if (player != null)
            {
                // Třída PlayerAttributes má [ServerRpc(RequireOwnership = false)],
                // takže toto volání ze serverové AI proběhne korektně.
                player.TakeDamageServerRpc(_damage, 1); // 1 jako ID nepřítele

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