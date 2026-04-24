using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class FlyingSkirmisherAI : EnemyBaseAI
{
    [Header("Flight Visuals")]
    [SerializeField] private Transform _visualContainer;
    [SerializeField] private float _flightHeight = 4.0f;
    [SerializeField] private float _hoverAmplitude = 0.6f;
    [SerializeField] private float _hoverSpeed = 2.5f;

    [Header("Orbit Navigation")]
    [Tooltip("Vzdálenost, ve které bude kroužit.")]
    [SerializeField] private float _orbitRadius = 12f; 
    [SerializeField] private float _orbitSpeed = 45f;
    [SerializeField] private float _movementSpeed = 6f;
    [SerializeField] private LayerMask _obstacleMask;

    [Header("Combat & Prediction")]
    [Tooltip("Vzdálenost, ze které začne útočit.")]
    [SerializeField] private float _preferredAttackRange = 18f;
    [SerializeField] private float _attackCooldown = 3.0f;
    [SerializeField] private float _telegraphTime = 1.2f;
    [Range(0f, 1.5f)][SerializeField] private float _predictionFactor = 0.9f;
    [SerializeField] private GameObject _projectilePrefab;
    [SerializeField] private Transform _firePoint;
    [SerializeField] private WeaponStats _projectileStats;

    [Header("Telegraph Visuals")]
    [SerializeField] private float _aimSmoothTime = 0.15f;
    [SerializeField] private GameObject _chargeUpVFX;
    [SerializeField] private LineRenderer _trajectoryLine;
    [SerializeField] private Color _trajectoryStartColor = new Color(1f, 0.8f, 0f, 0.8f);
    [SerializeField] private Color _trajectoryEndColor = new Color(1f, 0f, 0f, 1f);

    private float _currentOrbitAngle;
    private int _orbitDirection = 1;
    private float _attackTimer;
    private bool _isAttacking = false;
    private Vector3 _lockedShootDirection;

    // Cache pro vyhlazování a predikci
    private Vector3 _currentAimPosition;
    private Vector3 _aimVelocity;
    private PlayerController _cachedTargetController;
    private Transform _lastTargetPlayer;

    protected override void Awake()
    {
        base.Awake();
        if (_trajectoryLine != null)
        {
            _trajectoryLine.positionCount = 2;
            _trajectoryLine.enabled = false;
            _trajectoryLine.gameObject.SetActive(false);
            _trajectoryLine.useWorldSpace = true; // Kritické pro pohyb s modelem
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            _currentOrbitAngle = Random.Range(0f, 360f);
            _orbitDirection = Random.value > 0.5f ? 1 : -1;
            _attackTimer = _attackCooldown;
        }
    }

    protected override void Update()
    {
        base.Update();

        if (_visualContainer != null && !_isSpawning.Value && _health.CurrentHealth.Value > 0)
        {
            // Hover efekt - vertikální pohyb modelu
            float hoverY = _flightHeight + Mathf.Sin(Time.time * _hoverSpeed) * _hoverAmplitude;
            _visualContainer.localPosition = new Vector3(0, hoverY, 0);
            
            // Otáčení modelu ke směru útoku nebo k hráči
            if (TargetPlayer != null)
            {
                Vector3 lookTarget = _isAttacking ? _currentAimPosition : TargetPlayer.position + Vector3.up;
                Vector3 lookDir = lookTarget - _visualContainer.position;
                if (lookDir.sqrMagnitude > 0.1f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(lookDir);
                    _visualContainer.rotation = Quaternion.Slerp(_visualContainer.rotation, targetRot, Time.deltaTime * 6f);
                }
            }
        }
    }

    public override void BehaviorLogic()
    {
        if (TargetPlayer == null || _isSpawning.Value) return;

        if (_lastTargetPlayer != TargetPlayer)
        {
            _lastTargetPlayer = TargetPlayer;
            TargetPlayer.TryGetComponent(out _cachedTargetController);
        }

        CalculateOrbitOffset();
        ApplyMovement();

        if (_isAttacking) return;

        float sqrDistance = (transform.position - TargetPlayer.position).sqrMagnitude;
        if (sqrDistance <= _preferredAttackRange * _preferredAttackRange)
        {
            _attackTimer -= Time.deltaTime;
            if (_attackTimer <= 0f)
            {
                _attackTimer = _attackCooldown;
                StartCoroutine(ShootRoutine());
            }
        }
    }

    private void CalculateOrbitOffset()
    {
        _currentOrbitAngle += _orbitSpeed * _orbitDirection * Time.deltaTime;
        Quaternion rotation = Quaternion.Euler(0, _currentOrbitAngle, 0);
        Vector3 orbitDirection = rotation * Vector3.forward;
        _targetOffset = orbitDirection * _orbitRadius;

        // Detekce překážek na orbitě
        Vector3 rayStart = TargetPlayer.position + Vector3.up;
        if (Physics.Raycast(rayStart, orbitDirection, out RaycastHit hit, _orbitRadius, _obstacleMask))
        {
            _orbitDirection *= -1;
            _currentOrbitAngle += 10f * _orbitDirection; 
        }
    }

    private void ApplyMovement()
    {
        Vector3 targetPos = TargetPlayer.position + _targetOffset;
        targetPos.y = transform.position.y; // Root držíme v jedné rovině
        transform.position = Vector3.MoveTowards(transform.position, targetPos, _movementSpeed * Time.deltaTime);
    }

    private IEnumerator ShootRoutine()
    {
        _isAttacking = true;
        
        // Inicializace míření na aktuální pozici hráče
        _currentAimPosition = TargetPlayer.position + Vector3.up;
        _aimVelocity = Vector3.zero;

        StartTelegraphVisual();

        float timer = 0f;
        while (timer < _telegraphTime)
        {
            timer += Time.deltaTime;
            UpdateTelegraphVisual(timer / _telegraphTime);
            yield return null;
        }

        if (IsServer && TargetPlayer != null && _health.CurrentHealth.Value > 0 && IsSpawned)
        {
            SpawnProjectile();
        }

        StopTelegraphVisual();
        _isAttacking = false;
    }

    private void SpawnProjectile()
    {
        if (_projectilePrefab == null || _firePoint == null) return;

        _firePoint.rotation = Quaternion.LookRotation(_lockedShootDirection);
        GameObject proj = Instantiate(_projectilePrefab, _firePoint.position, _firePoint.rotation);
        
        if (proj.TryGetComponent(out NetworkObject netObj)) netObj.Spawn(true);
        if (proj.TryGetComponent(out SmartProjectile smartProj))
        {
            _projectileStats.Damage = _currentDamage;
            smartProj.Initialize(this.NetworkObject, _lockedShootDirection, _projectileStats);
        }
    }

    #region Telegraph Visuals
    public override void StartTelegraphVisual()
    {
        if (_chargeUpVFX) _chargeUpVFX.SetActive(true);
        if (_trajectoryLine)
        {
            _trajectoryLine.gameObject.SetActive(true);
            _trajectoryLine.enabled = true;
        }
    }

    public override void UpdateTelegraphVisual(float progress)
    {
        if (_trajectoryLine == null || _firePoint == null || TargetPlayer == null) return;

        // 1. Výpočet ideálního bodu dopadu s predikcí
        Vector3 rawTargetPos = TargetPlayer.position + Vector3.up; 
        if (_cachedTargetController != null)
        {
            float dist = Vector3.Distance(_firePoint.position, rawTargetPos);
            float flightTime = dist / Mathf.Max(1f, _projectileStats.ProjectileSpeed);
            rawTargetPos += _cachedTargetController.Velocity * (flightTime * _predictionFactor);
        }

        // 2. Vyhlazení pohybu cíle (Aim Inertia)
        _currentAimPosition = Vector3.SmoothDamp(_currentAimPosition, rawTargetPos, ref _aimVelocity, _aimSmoothTime);

        // 3. Výpočet směru a kolize
        _lockedShootDirection = (_currentAimPosition - _firePoint.position).normalized;
        float range = _projectileStats.Range > 0 ? _projectileStats.Range : 40f;
        Vector3 endPos = _firePoint.position + (_lockedShootDirection * range);

        if (Physics.Raycast(_firePoint.position, _lockedShootDirection, out RaycastHit hit, range, _obstacleMask))
        {
            endPos = hit.point;
        }

        // 4. Aktualizace LineRendereru (start je vždy u pohyblivého modelu)
        _trajectoryLine.SetPosition(0, _firePoint.position);
        _trajectoryLine.SetPosition(1, endPos);

        // Barva
        Color currentColor = Color.Lerp(_trajectoryStartColor, _trajectoryEndColor, progress);
        _trajectoryLine.startColor = currentColor;
        _trajectoryLine.endColor = new Color(currentColor.r, currentColor.g, currentColor.b, 0f);
    }

    public override void StopTelegraphVisual()
    {
        if (_chargeUpVFX) _chargeUpVFX.SetActive(false);
        if (_trajectoryLine)
        {
            _trajectoryLine.enabled = false;
            _trajectoryLine.gameObject.SetActive(false);
        }
    }
    #endregion
}