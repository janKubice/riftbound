using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class MageEnemyAI : EnemyBaseAI
{
    [Header("Mage Spell")]
    [SerializeField] private EnemySpellDefinition _spell;
    [SerializeField] private Transform _firePoint;

    [Header("Positioning")]
    [SerializeField] private float _preferredRange = 11f;
    [SerializeField] private float _rangeTolerance = 1.5f;

    [Header("Targeting")]
    [SerializeField] private float _aimSmoothTime = 0.12f;
    [SerializeField] private float _groundRaycastHeight = 12f;
    [SerializeField] private float _groundRaycastDistance = 30f;
    [SerializeField] private LayerMask _groundMask = ~0;

    [Header("Telegraph Visuals")]
    [SerializeField] private GameObject _chargeUpVFX;
    [SerializeField] private LineRenderer _aimLine;
    [SerializeField] private Transform _groundTelegraphDecal;
    [SerializeField] private Renderer _groundTelegraphRenderer;
    [SerializeField] private float _groundTelegraphYOffset = 0.05f;
    [SerializeField] private Vector2 _telegraphScaleProgress = new Vector2(0.25f, 1.0f);

    [Header("Telegraph Stability")]
    [Tooltip("Když je zapnuto, telegraf se během začátku castu jemně přizpůsobuje cíli, ale potom se zamkne a už netančí za hráčem.")]
    [SerializeField] private bool _lockTelegraphBeforeRelease = true;

    [Tooltip("V jaké části castu se místo dopadu zamkne. 0.35 = po 35 % délky telegrafu.")]
    [SerializeField, Range(0.05f, 0.95f)] private float _telegraphLockProgress = 0.38f;

    [Tooltip("Maximální rychlost, jak rychle smí telegraf během zaměřování následovat hráče. Nižší hodnota = klidnější, čitelnější telegraf.")]
    [SerializeField] private float _telegraphMaxFollowSpeed = 5.5f;

    [Tooltip("Malé pohyby cíle pod tuto vzdálenost se ignorují, aby telegraf necukal kvůli mikropohybu hráče nebo raycastu po nerovné zemi.")]
    [SerializeField] private float _telegraphDeadZone = 0.2f;

    private float _lastCastTime = -999f;
    private bool _isCasting = false;

    private PlayerController _cachedTargetController;
    private Transform _lastTargetPlayer;

    private Vector3 _currentAimPoint;
    private Vector3 _aimVelocity;
    private Vector3 _serverLockedGroundPoint;
    private Vector3 _serverAimVelocity;
    private Vector3 _castScatterOffset;

    private bool _visualTelegraphLocked;
    private bool _serverTelegraphLocked;

    private MaterialPropertyBlock _telegraphBlock;

    protected override void Awake()
    {
        base.Awake();

        _useFlowFieldMovement = true;
        _telegraphBlock = new MaterialPropertyBlock();

        if (_aimLine != null)
        {
            _aimLine.positionCount = 2;
            _aimLine.enabled = false;
            _aimLine.gameObject.SetActive(false);
            _aimLine.useWorldSpace = true;
        }

        if (_groundTelegraphDecal != null)
            _groundTelegraphDecal.gameObject.SetActive(false);

        if (_chargeUpVFX != null)
            _chargeUpVFX.SetActive(false);
    }

    public override void BehaviorLogic()
    {
        if (TargetPlayer == null || _spell == null || _isSpawning.Value)
            return;

        CacheTargetController();

        if (_isCasting)
        {
            IsMovementPaused = true;
            RotateToPoint(_serverLockedGroundPoint);
            return;
        }

        float castRange = Mathf.Max(0.5f, _spell.CastRange > 0f ? _spell.CastRange : _preferredRange);
        float minComfortRange = Mathf.Max(0f, castRange - _rangeTolerance);
        float maxComfortRange = castRange + _rangeTolerance;

        float distance = Vector3.Distance(MyTransform.position, TargetPlayer.position);

        if (distance > maxComfortRange)
        {
            IsMovementPaused = false;
            return;
        }

        // Pokud je příliš blízko, klidně na chvíli zůstane stát a castí/otáčí se.
        IsMovementPaused = true;
        RotateToTarget();

        if (Time.time >= _lastCastTime + Mathf.Max(0.05f, _spell.Cooldown))
        {
            StartCoroutine(CastRoutine());
        }
    }

    private void CacheTargetController()
    {
        if (_lastTargetPlayer == TargetPlayer)
            return;

        _lastTargetPlayer = TargetPlayer;
        _cachedTargetController = null;

        if (TargetPlayer != null)
            TargetPlayer.TryGetComponent(out _cachedTargetController);
    }

    private IEnumerator CastRoutine()
    {
        _isCasting = true;
        IsMovementPaused = true;

        _castScatterOffset = GetScatterOffset();
        ResetTelegraphAimState();

        float castDuration = Mathf.Max(0.05f, _spell.TelegraphDuration);

        TriggerTelegraph(castDuration);

        float timer = 0f;

        while (timer < castDuration)
        {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / castDuration);

            Vector3 desiredGroundPoint = ResolveGroundPoint(GetPredictedTargetPoint());

            _serverLockedGroundPoint = UpdateStableTelegraphPoint(
                _serverLockedGroundPoint,
                desiredGroundPoint,
                ref _serverAimVelocity,
                progress,
                ref _serverTelegraphLocked
            );

            RotateToPoint(_serverLockedGroundPoint);

            yield return null;
        }

        if (IsServer && IsSpawned && _health.CurrentHealth.Value > 0)
        {
            ExecuteSpell(_serverLockedGroundPoint);
        }

        _lastCastTime = Time.time;
        _isCasting = false;
    }

    private Vector3 GetPredictedTargetPoint()
    {
        if (TargetPlayer == null)
            return MyTransform.position;

        Vector3 targetPos = TargetPlayer.position;

        if (_cachedTargetController != null)
        {
            targetPos += _cachedTargetController.Velocity * _spell.PredictionFactor;
        }

        targetPos += _castScatterOffset;
        return targetPos;
    }

    private Vector3 GetScatterOffset()
    {
        if (_spell == null || _spell.ScatterRadius <= 0f)
            return Vector3.zero;

        Vector2 circle = Random.insideUnitCircle * _spell.ScatterRadius;
        return new Vector3(circle.x, 0f, circle.y);
    }

    private Vector3 ResolveGroundPoint(Vector3 rawTarget)
    {
        Vector3 rayOrigin = rawTarget + Vector3.up * _groundRaycastHeight;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, _groundRaycastDistance, _groundMask))
        {
            return hit.point;
        }

        rawTarget.y = MyTransform.position.y;
        return rawTarget;
    }

    private void ExecuteSpell(Vector3 groundPoint)
    {
        if (_spell == null)
            return;

        if (_spell.CastReleaseVFX != null && _firePoint != null)
        {
            SpawnSimpleVfx(_spell.CastReleaseVFX, _firePoint.position);
        }

        if (_spell.SpawnProjectile && _spell.ProjectilePrefab != null && _firePoint != null)
        {
            SpawnProjectileTowards(groundPoint);
        }

        if (_spell.SpawnGroundZone && _spell.GroundZonePrefab != null)
        {
            SpawnGroundZone(groundPoint);

            if (_spell.GroundImpactVFX != null)
            {
                SpawnSimpleVfx(_spell.GroundImpactVFX, groundPoint);
            }
        }
    }

    private void SpawnProjectileTowards(Vector3 targetPoint)
    {
        Vector3 dir = (targetPoint - _firePoint.position).normalized;

        if (dir.sqrMagnitude < 0.0001f)
            dir = MyTransform.forward;

        _firePoint.rotation = Quaternion.LookRotation(dir);

        GameObject proj = Instantiate(_spell.ProjectilePrefab, _firePoint.position, _firePoint.rotation);

        if (proj.TryGetComponent(out NetworkObject netObj))
            netObj.Spawn(true);

        if (proj.TryGetComponent(out SmartProjectile smartProj))
        {
            _spell.ProjectileStats.Damage = _currentDamage;
            smartProj.Initialize(this.NetworkObject, dir, _spell.ProjectileStats);
        }
    }

    private void SpawnGroundZone(Vector3 groundPoint)
    {
        if (!IsServer || _spell == null || _spell.GroundZonePrefab == null)
            return;

        Vector3 direction = groundPoint - MyTransform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            direction = MyTransform.forward;

        direction.Normalize();

        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);

        GameObject zone = Instantiate(
            _spell.GroundZonePrefab,
            groundPoint + Vector3.up * 0.02f,
            rotation
        );

        if (!zone.TryGetComponent(out IEnemySpellZone spellZone))
        {
            Debug.LogError($"{nameof(MageEnemyAI)}: GroundZonePrefab nemá IEnemySpellZone komponentu.", zone);
            Destroy(zone);
            return;
        }

        spellZone.InitializeFromSpell(
            _spell,
            NetworkObjectId,
            direction
        );

        if (zone.TryGetComponent(out NetworkObject netObj))
        {
            if (!netObj.IsSpawned)
                netObj.Spawn(true);
        }
        else
        {
            Debug.LogError($"{nameof(MageEnemyAI)}: GroundZonePrefab nemá NetworkObject komponentu.", zone);
            Destroy(zone);
        }
    }

    private void SpawnSimpleVfx(GameObject prefab, Vector3 pos)
    {
        GameObject instance = Instantiate(prefab, pos, Quaternion.identity);

        if (instance.TryGetComponent(out NetworkObject netObj))
        {
            netObj.Spawn(true);
        }
        else
        {
            Destroy(instance, 5f);
        }
    }

    private void ResetTelegraphAimState()
    {
        Vector3 initialPoint = ResolveGroundPoint(GetPredictedTargetPoint());

        _currentAimPoint = initialPoint;
        _serverLockedGroundPoint = initialPoint;

        _aimVelocity = Vector3.zero;
        _serverAimVelocity = Vector3.zero;

        _visualTelegraphLocked = false;
        _serverTelegraphLocked = false;
    }

    private Vector3 UpdateStableTelegraphPoint(
        Vector3 currentPoint,
        Vector3 desiredGroundPoint,
        ref Vector3 velocity,
        float progress,
        ref bool isLocked
    )
    {
        if (_lockTelegraphBeforeRelease && isLocked)
            return currentPoint;

        if (_lockTelegraphBeforeRelease && progress >= Mathf.Clamp01(_telegraphLockProgress))
        {
            isLocked = true;
            velocity = Vector3.zero;
            return ResolveGroundPoint(currentPoint);
        }

        Vector3 flatDelta = desiredGroundPoint - currentPoint;
        flatDelta.y = 0f;

        if (_telegraphDeadZone > 0f && flatDelta.sqrMagnitude <= _telegraphDeadZone * _telegraphDeadZone)
            return currentPoint;

        float smoothTime = Mathf.Max(0.01f, _aimSmoothTime);
        float maxSpeed = Mathf.Max(0.1f, _telegraphMaxFollowSpeed);

        Vector3 smoothedPoint = Vector3.SmoothDamp(
            currentPoint,
            desiredGroundPoint,
            ref velocity,
            smoothTime,
            maxSpeed,
            Time.deltaTime
        );

        return ResolveGroundPoint(smoothedPoint);
    }

    #region Telegraph Visuals

    public override void StartTelegraphVisual()
    {
        if (TargetPlayer != null && _spell != null)
            ResetTelegraphAimState();

        if (_chargeUpVFX != null)
            _chargeUpVFX.SetActive(true);

        if (_aimLine != null)
        {
            _aimLine.enabled = true;
            _aimLine.gameObject.SetActive(true);
        }

        if (_groundTelegraphDecal != null)
            _groundTelegraphDecal.gameObject.SetActive(true);
    }

    public override void UpdateTelegraphVisual(float progress)
    {
        if (TargetPlayer == null || _spell == null)
            return;

        Vector3 rawAim = GetPredictedTargetPoint();
        Vector3 groundAim = ResolveGroundPoint(rawAim);

        _currentAimPoint = UpdateStableTelegraphPoint(
            _currentAimPoint,
            groundAim,
            ref _aimVelocity,
            Mathf.Clamp01(progress),
            ref _visualTelegraphLocked
        );

        if (_aimLine != null && _firePoint != null)
        {
            _aimLine.SetPosition(0, _firePoint.position);
            _aimLine.SetPosition(1, _currentAimPoint);

            Color c = _spell.SpellColor;
            Color start = new Color(c.r, c.g, c.b, 0.9f);
            Color end = new Color(c.r, c.g, c.b, 0.2f);

            _aimLine.startColor = start;
            _aimLine.endColor = end;
        }

        if (_groundTelegraphDecal != null)
        {
            float scaleProgress = Mathf.Lerp(
                _telegraphScaleProgress.x,
                _telegraphScaleProgress.y,
                progress
            );

            _groundTelegraphDecal.position = _currentAimPoint + Vector3.up * _groundTelegraphYOffset;

            Vector3 dir = _currentAimPoint - MyTransform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude > 0.001f)
            {
                _groundTelegraphDecal.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }

            if (_spell.TelegraphShape == EnemyTelegraphShape.Rectangle)
            {
                _groundTelegraphDecal.localScale = new Vector3(
                    _spell.TelegraphRectSize.x * scaleProgress,
                    1f,
                    _spell.TelegraphRectSize.y * scaleProgress
                );
            }
            else
            {
                float radius = Mathf.Max(0.25f, _spell.GetTelegraphRadius());
                float finalScale = radius * 2f * scaleProgress;

                _groundTelegraphDecal.localScale = new Vector3(
                    finalScale,
                    1f,
                    finalScale
                );
            }

            if (_groundTelegraphRenderer != null)
            {
                _groundTelegraphRenderer.GetPropertyBlock(_telegraphBlock);

                Color color = _spell.SpellColor;
                color.a = Mathf.Lerp(0.2f, 0.85f, progress);

                _telegraphBlock.SetColor("_BaseColor", color);
                _telegraphBlock.SetColor("_Color", color);

                _groundTelegraphRenderer.SetPropertyBlock(_telegraphBlock);
            }
        }
    }

    public override void StopTelegraphVisual()
    {
        if (_chargeUpVFX != null)
            _chargeUpVFX.SetActive(false);

        if (_aimLine != null)
        {
            _aimLine.enabled = false;
            _aimLine.gameObject.SetActive(false);
        }

        if (_groundTelegraphDecal != null)
            _groundTelegraphDecal.gameObject.SetActive(false);
    }

    #endregion
}