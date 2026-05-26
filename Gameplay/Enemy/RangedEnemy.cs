using UnityEngine;
using System.Collections;
using Unity.Netcode;

/// <summary>
/// Nepřítel pro boj na dálku s podporou lobbed (obloukových) střel, 
/// predikcí pohybu hráče a plynulým telegrafem.
/// </summary>
public class RangedEnemy : EnemyBaseAI
{
    [Header("Ranged Stats")]
    [SerializeField] private float _stopDistance = 10f;
    [SerializeField] private float _attackCooldown = 3.0f;
    [SerializeField] private float _telegraphTime = 1.0f;

    [Header("Projectile")]
    [SerializeField] private GameObject _projectilePrefab;
    [SerializeField] private Transform _firePoint;
    [SerializeField] private WeaponStats _projectileStats;

    [Header("Aiming & Prediction")]
    [Tooltip("Šance, že tento nepřítel bude používat predikci pohybu (např. 0.75 = 75%)")]
    [Range(0f, 1f)][SerializeField] private float _predictionChance = 0.75f;
    [Tooltip("Maximální síla predikce, pokud se nepřítel rozhodne predikovat.")]
    [Range(0f, 1.5f)][SerializeField] private float _maxPredictionFactor = 1.0f;
    [SerializeField] private float _scatterRadius = 2.0f;
    [SerializeField] private bool _isLobbed = true;
    [SerializeField] private float _arcHeight = 5.0f;
    [Tooltip("Plynulost sledování cíle telegrafem. Vyšší hodnota = pomalejší/plynulejší pohyb.")]
    [SerializeField] private float _aimSmoothTime = 0.1f;

    [Header("Attack Timing")]
    [SerializeField] private float _attackCooldownJitter = 0.5f;

    private float _actualAttackCooldown;

    [Header("Telegraph Visuals")]
    [SerializeField] private GameObject _chargeUpVFX;
    [SerializeField] private LineRenderer _trajectoryLine;
    [SerializeField] private LayerMask _obstacleMask = ~0;
    [SerializeField] private Color _trajectoryStartColor = new Color(1f, 1f, 0f, 0.8f);
    [SerializeField] private Color _trajectoryEndColor = new Color(1f, 0f, 0f, 1f);
    [SerializeField] private int _lineResolution = 30;
    [SerializeField] private float _maxPredictionTime = 3f;

    [Header("Impact Marker")]
    [SerializeField] private GameObject _impactMarkerPrefab;
    [SerializeField] private LayerMask _groundMask;
    [SerializeField] private float _groundProbeUp = 20f;
    [SerializeField] private float _groundProbeDown = 80f;
    [SerializeField] private float _impactMarkerGroundOffset = 0.04f;

    private GameObject _impactMarkerInstance;
    private Vector3 _lastImpactPoint;
    private Vector3 _lastImpactNormal = Vector3.up;
    private bool _hasLastImpactPoint;

    private float _lastAttackTime;
    private bool _isAttacking = false;
    private float _attackRangeSqr;

    // Cache pro výpočty
    private float _actualPredictionFactor; // Unikátní hodnota pro tuto instanci
    private Vector3 _currentScatterOffset;
    private Vector3 _lastCalculatedVelocity;
    private Vector3 _currentAimPosition;
    private Vector3 _aimVelocity;
    private Vector3[] _trajectoryPoints;

    protected override void Awake()
    {
        base.Awake();
        _attackRangeSqr = _stopDistance * _stopDistance;

        InitializePrediction();

        _actualAttackCooldown = GetRandomizedAttackCooldown();
        _lastAttackTime = Time.time - Random.Range(0f, _actualAttackCooldown);

        _trajectoryPoints = new Vector3[_lineResolution];

        if (_trajectoryLine != null)
        {
            _trajectoryLine.enabled = false;
            _trajectoryLine.gameObject.SetActive(false);
        }
    }

    private void InitializePrediction()
    {
        // Rozhodnutí, zda tento konkrétní nepřítel bude predikovat
        if (UnityEngine.Random.value <= _predictionChance)
        {
            // Náhodná míra predikce (např. od lehké nepřesnosti po maximální predikci)
            // Lze upravit spodní hranici dle potřeby (zde 0.2f)
            _actualPredictionFactor = UnityEngine.Random.Range(0.2f, _maxPredictionFactor);
        }
        else
        {
            // Žádná predikce (míří tam, kde hráč právě stojí)
            _actualPredictionFactor = 0f;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        StopTelegraphVisual();
        if (_chargeUpVFX) _chargeUpVFX.SetActive(false);
        _isAttacking = false;
        IsMovementPaused = false;
    }

    public override void BehaviorLogic()
    {
        if (_isAttacking || TargetPlayer == null) return;

        float distSqr = (MyTransform.position - TargetPlayer.position).sqrMagnitude;

        if (distSqr <= _attackRangeSqr)
        {
            if (Time.time >= _lastAttackTime + _actualAttackCooldown)
            {
                StartCoroutine(ShootRoutine());
            }
            else
            {
                IsMovementPaused = true;
                RotateToTarget();
            }
        }
        else
        {
            IsMovementPaused = false;
        }
    }

    private IEnumerator ShootRoutine()
    {
        _isAttacking = true;
        IsMovementPaused = true;
        _lastAttackTime = Time.time;
        _actualAttackCooldown = GetRandomizedAttackCooldown();

        // Resetování plynulého zaměřování na aktuální pozici hráče
        _currentAimPosition = TargetPlayer.position;
        _aimVelocity = Vector3.zero;

        // Fixace rozptylu pro celou dobu trvání telegrafu (aby se křivka netřásla)
        Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * _scatterRadius;
        _currentScatterOffset = new Vector3(randomCircle.x, 0, randomCircle.y);

        TriggerTelegraph(_telegraphTime);
        if (_chargeUpVFX) _chargeUpVFX.SetActive(true);

        float timer = 0f;
        while (timer < _telegraphTime)
        {
            timer += Time.deltaTime;
            RotateToTarget();
            yield return null;
        }

        if (_chargeUpVFX) _chargeUpVFX.SetActive(false);

        // Samotný výstřel na straně serveru
        if (IsServer && IsSpawned && _projectilePrefab != null && _firePoint != null)
        {
            GameObject proj = Instantiate(_projectilePrefab, _firePoint.position, _firePoint.rotation);
            var netObj = proj.GetComponent<NetworkObject>();
            netObj.Spawn(true);

            if (proj.TryGetComponent(out SmartProjectile smartProj))
            {
                _projectileStats.Damage = _currentDamage;
                smartProj.Initialize(this.NetworkObject, _firePoint.forward, _projectileStats);

                // Předání přesné vypočítané rychlosti z telegrafu do projektilu
                if (proj.TryGetComponent(out FlaskProjectile flask))
                {
                    flask.ApplyCalculatedVelocity(_lastCalculatedVelocity);

                    if (_hasLastImpactPoint)
                    {
                        flask.ShowImpactMarker(_lastImpactPoint, _lastImpactNormal);
                    }
                }
            }
        }

        _isAttacking = false;
        IsMovementPaused = false;
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

        // 1. Výpočet ideální dopadové pozice (raw)
        Vector3 rawTargetPos = TargetPlayer.position;

        // Odhad času letu pro predikci
        float initialEstTime = _isLobbed
            ? EstimateFlightTime(_firePoint.position, rawTargetPos, _arcHeight)
            : Vector3.Distance(_firePoint.position, rawTargetPos) / _projectileStats.ProjectileSpeed;

        // 2. Predikce pohybu hráče (používá lokální _actualPredictionFactor)
        if (_actualPredictionFactor > 0f && TargetPlayer.TryGetComponent(out PlayerController pc))
        {
            rawTargetPos += pc.Velocity * (initialEstTime * _actualPredictionFactor);
        }

        // Aplikace zafixovaného rozptylu (zároveň obstarává oněch 25%, které netrefí úplně přesně)
        rawTargetPos += _currentScatterOffset;

        // 3. VYHLAZENÍ CÍLE (Aim Inertia) - eliminuje teleportaci čáry
        _currentAimPosition = Vector3.SmoothDamp(
            _currentAimPosition,
            rawTargetPos,
            ref _aimVelocity,
            _aimSmoothTime
        );

        Vector3 finalTarget = _currentAimPosition;

        // Pro lobbed bombu chceme mířit na skutečný bod na zemi,
        // ne na pivot hráče nebo střed collideru.
        if (_isLobbed)
        {
            if (TryResolveGroundPoint(finalTarget, out Vector3 groundPoint, out Vector3 groundNormal))
            {
                finalTarget = groundPoint;

                _lastImpactPoint = groundPoint;
                _lastImpactNormal = groundNormal;
                _hasLastImpactPoint = true;

                ShowOrMoveImpactMarker(groundPoint, groundNormal);
            }
        }
        else
        {
            _hasLastImpactPoint = false;
            HideImpactMarker();
        }

        // 4. Výpočet počáteční rychlosti střely
        if (_isLobbed)
        {
            _lastCalculatedVelocity = CalculateArcVelocity(_firePoint.position, finalTarget, _arcHeight);
        }
        else
        {
            Vector3 dir = (finalTarget - _firePoint.position).normalized;
            _lastCalculatedVelocity = dir * _projectileStats.ProjectileSpeed;
        }

        // 5. Vykreslení trajektorie
        float renderFlightTime = _isLobbed
            ? EstimateFlightTime(_firePoint.position, finalTarget, _arcHeight)
            : Vector3.Distance(_firePoint.position, finalTarget) / _projectileStats.ProjectileSpeed;

        renderFlightTime = Mathf.Clamp(renderFlightTime, 0.1f, _maxPredictionTime);

        int pointCount = 0;
        Vector3 currentStepPos = _firePoint.position;
        _trajectoryPoints[pointCount++] = currentStepPos;

        float timeStep = renderFlightTime / (_lineResolution - 1);

        for (int i = 1; i < _lineResolution; i++)
        {
            float t = i * timeStep;
            Vector3 gravityEffect = _isLobbed ? (Physics.gravity * 0.5f * t * t) : Vector3.zero;
            Vector3 nextStepPos = _firePoint.position + (_lastCalculatedVelocity * t) + gravityEffect;

            if (Physics.Linecast(currentStepPos, nextStepPos, out RaycastHit hit, _obstacleMask))
            {
                _trajectoryPoints[pointCount++] = hit.point;
                break;
            }

            _trajectoryPoints[pointCount++] = nextStepPos;
            currentStepPos = nextStepPos;
        }

        _trajectoryLine.positionCount = pointCount;
        _trajectoryLine.SetPositions(_trajectoryPoints);

        // Vizuální feedback (barva se mění s blížícím se výstřelem)
        Color currentColor = Color.Lerp(_trajectoryStartColor, _trajectoryEndColor, progress);
        _trajectoryLine.startColor = currentColor;
        _trajectoryLine.endColor = new Color(currentColor.r, currentColor.g, currentColor.b, 0f);
    }

    public override void StopTelegraphVisual()
    {
        if (_chargeUpVFX) _chargeUpVFX.SetActive(false);
        if (_trajectoryLine)
        {
            _trajectoryLine.positionCount = 0;
            _trajectoryLine.enabled = false;
            _trajectoryLine.gameObject.SetActive(false);
        }

        HideImpactMarker();
    }

    // --- MATEMATICKÉ POMŮCKY ---
    private float EstimateFlightTime(Vector3 start, Vector3 target, float arcHeight)
    {
        float gravity = Mathf.Abs(Physics.gravity.y);
        float displacementY = target.y - start.y;
        float height = Mathf.Max(arcHeight, displacementY + 0.1f);

        float timeUp = Mathf.Sqrt(2f * height / gravity);
        float timeDown = Mathf.Sqrt(2f * (height - displacementY) / gravity);
        return timeUp + timeDown;
    }

    private Vector3 CalculateArcVelocity(Vector3 start, Vector3 target, float arcHeight)
    {
        float gravity = Mathf.Abs(Physics.gravity.y);
        float displacementY = target.y - start.y;
        float height = Mathf.Max(arcHeight, displacementY + 0.1f);

        float totalTime = EstimateFlightTime(start, target, arcHeight);
        if (totalTime <= 0) return Vector3.up;

        Vector3 displacementXZ = new Vector3(target.x - start.x, 0, target.z - start.z);
        Vector3 velocityY = Vector3.up * Mathf.Sqrt(2f * gravity * height);
        Vector3 velocityXZ = displacementXZ / totalTime;

        return velocityXZ + velocityY;
    }

    private bool TryResolveGroundPoint(Vector3 desiredPosition, out Vector3 point, out Vector3 normal)
    {
        Vector3 origin = desiredPosition + Vector3.up * _groundProbeUp;
        float distance = _groundProbeUp + _groundProbeDown;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance, _groundMask, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            normal = hit.normal;
            return true;
        }

        point = desiredPosition;
        normal = Vector3.up;
        return false;
    }

    private void ShowOrMoveImpactMarker(Vector3 point, Vector3 normal)
    {
        if (_impactMarkerPrefab == null)
            return;

        if (_impactMarkerInstance == null)
        {
            _impactMarkerInstance = Instantiate(_impactMarkerPrefab);
        }

        _impactMarkerInstance.SetActive(true);

        Vector3 markerPosition = point + normal * _impactMarkerGroundOffset;
        Quaternion markerRotation = Quaternion.FromToRotation(Vector3.up, normal);

        _impactMarkerInstance.transform.SetPositionAndRotation(markerPosition, markerRotation);
    }

    private void HideImpactMarker()
    {
        if (_impactMarkerInstance != null)
        {
            _impactMarkerInstance.SetActive(false);
        }
    }
    #endregion

    private float GetRandomizedAttackCooldown()
    {
        float minCooldown = Mathf.Max(0.1f, _attackCooldown - _attackCooldownJitter);
        float maxCooldown = _attackCooldown + _attackCooldownJitter;

        return Random.Range(minCooldown, maxCooldown);
    }
}