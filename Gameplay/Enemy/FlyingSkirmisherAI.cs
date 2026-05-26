using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class FlyingSkirmisherAI : EnemyBaseAI
{
    private enum TelegraphVisualStyle
    {
        ChargeOnly,
        MuzzleCue,
        AimMarker,
        FullLine
    }

    [Header("Flight Visuals")]
    [SerializeField] private Transform _visualContainer;

    [Tooltip("Skutečná výška root objektu/collideru nad hráčem. VisualContainer už nemá nést hlavní výšku letu.")]
    [SerializeField] private float _flightHeight = 2.6f;

    [Tooltip("Pouze jemné vizuální dýchání modelu. Pro bullet hell nech nízko, aby firepoint nelítal nahoru a dolů.")]
    [SerializeField] private float _hoverAmplitude = 0.08f;
    [SerializeField] private float _hoverSpeed = 1.25f;

    [Header("Flying Hitbox")]
    [Tooltip("U létajícího enemy musí být hitbox nahoře u modelu/rootu, ne dole na zemi.")]
    [SerializeField] private bool _configureFlyingHitbox = true;
    [SerializeField] private float _hitboxHeight = 1.6f;
    [SerializeField] private float _hitboxRadius = 0.45f;
    [SerializeField] private Vector3 _hitboxCenter = Vector3.zero;

    [Header("Orbit Navigation")]
    [Tooltip("Horizontální vzdálenost od hráče. Tohle drží enemy bokem, ne přímo nad hráčem.")]
    [SerializeField] private float _orbitRadius = 8.0f;
    [SerializeField] private float _minimumHorizontalDistance = 5.5f;
    [SerializeField] private float _orbitSpeed = 24f;
    [SerializeField] private float _movementSpeed = 5.5f;
    [SerializeField] private float _movementSmoothTime = 0.18f;

    [Tooltip("Pro čitelnost bullet-hell útoku je lepší, když se enemy během zaměřování nehýbe.")]
    [SerializeField] private bool _freezeMovementDuringTelegraph = true;

    [SerializeField] private LayerMask _obstacleMask;

    [Header("Flying Movement Driver")]
    [Tooltip("Pro létajícího enemy je bezpečnější hýbat root transformem. CharacterController necháváme jako hitbox/collider, ale nevoláme přes něj Move(), protože při poolingu/spawnu umí skončit jako inactive controller.")]
    [SerializeField] private bool _useCharacterControllerMove = false;

    [Header("Custom Movement Tick")]
    [Tooltip("V tvém projektu ho už řídí EnemyMovementManager. Zapni jen v případě, že tento enemy není registrovaný v EnemyMovementManageru.")]
    [SerializeField] private bool _driveBehaviorFromUpdate = false;

    [Tooltip("0 = během telegraphu stojí. 0.15-0.35 = během zaměřování se ještě pomalu posouvá, takže nepůsobí zasekle.")]
    [Range(0f, 1f)]
    [SerializeField] private float _telegraphMovementMultiplier = 0.25f;

    [Header("Combat")]
    [Tooltip("Horizontální vzdálenost, ze které začne útočit.")]
    [SerializeField] private float _preferredAttackRange = 16f;
    [SerializeField] private float _attackCooldown = 3.0f;
    [SerializeField] private float _telegraphTime = 1.05f;
    [SerializeField] private float _targetAimHeight = 1.05f;

    [Tooltip("V bullet hellu má být predikce spíš jemná. Vysoká predikce působí, že čára míří mimo hráče.")]
    [Range(0f, 1.5f)]
    [SerializeField] private float _predictionFactor = 0.22f;

    [SerializeField] private float _maxPredictionLead = 1.25f;
    [SerializeField] private float _projectileSpawnForwardOffset = 0.35f;
    [SerializeField] private GameObject _projectilePrefab;
    [SerializeField] private Transform _firePoint;
    [SerializeField] private WeaponStats _projectileStats;

    [Header("Bullet Hell Telegraph")]
    [Tooltip("První část telegraphu míření ještě sleduje hráče. Potom se směr zamkne, aby byl útok férový a čitelný.")]
    [Range(0.05f, 0.9f)]
    [SerializeField] private float _aimTrackingPortion = 0.35f;

    [SerializeField] private bool _lockAimBeforeShot = true;
    [SerializeField] private float _aimSmoothTime = 0.10f;

    [Tooltip("Defaultně nepoužíváme dlouhou čáru přes půl mapy. Nejčistší varianta je krátký muzzle cue u enemy + charge VFX.")]
    [SerializeField] private TelegraphVisualStyle _telegraphVisualStyle = TelegraphVisualStyle.MuzzleCue;

    [Tooltip("Použije se jen pro FullLine. Pokud WeaponStats.Range > 0, vezme se Range. Pokud ne, použije se tato fallback délka.")]
    [SerializeField] private float _telegraphLineLength = 28f;

    [Tooltip("Krátká směrová stopa u firepointu. Tohle nepřekrývá hráče ani zem, takže i 10 ranged enemy vypadá čitelněji.")]
    [SerializeField] private float _muzzleCueLength = 2.75f;

    [SerializeField] private bool _clampTelegraphToObstacles = true;
    [SerializeField] private float _telegraphStartWidth = 0.025f;
    [SerializeField] private float _telegraphEndWidth = 0.10f;
    [SerializeField] private float _telegraphEndAlpha = 0.85f;
    [SerializeField] private float _groundLineLift = 0.04f;

    [Header("Telegraph References")]
    [SerializeField] private GameObject _chargeUpVFX;

    [Tooltip("Vzdušná čára z firepointu. Ukazuje přesný směr výstřelu.")]
    [SerializeField] private LineRenderer _trajectoryLine;

    [Tooltip("Volitelné. Druhá čára na zemi je pro bullet hell mnohem čitelnější než jen 3D čára ve vzduchu.")]
    [SerializeField] private LineRenderer _groundDangerLine;

    [Tooltip("Volitelné. Malý marker/ring na zemi v bodě, kam se zrovna míří.")]
    [SerializeField] private GameObject _aimLockMarker;

    [SerializeField] private LayerMask _groundMask = ~0;
    [SerializeField] private Color _trajectoryStartColor = new Color(1f, 0.85f, 0.15f, 0.45f);
    [SerializeField] private Color _trajectoryEndColor = new Color(1f, 0.05f, 0.0f, 1f);

    private float _currentOrbitAngle;
    private int _orbitDirection = 1;
    private float _attackTimer;
    private bool _isAttacking;

    private Vector3 _lockedShootDirection = Vector3.forward;
    private Vector3 _lockedAimPoint;
    private bool _hasLockedAim;

    private Vector3 _currentAimPosition;
    private Vector3 _aimVelocity;
    private Vector3 _moveVelocity;
    private Vector3 _visualBaseLocalPosition;
    private float _hoverPhase;

    private PlayerController _cachedTargetController;
    private Transform _lastTargetPlayer;
    private int _lastBehaviorFrame = -1;

    protected override void Awake()
    {
        base.Awake();

        // Tenhle enemy používá vlastní letecký pohyb, ne flow field po zemi.
        _useFlowFieldMovement = false;

        if (_visualContainer != null)
            _visualBaseLocalPosition = _visualContainer.localPosition;

        _hoverPhase = Random.Range(0f, 10f);

        ConfigureLineRenderer(_trajectoryLine, false);
        ConfigureLineRenderer(_groundDangerLine, false);
        ConfigureFlyingHitbox();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ConfigureFlyingHitbox();

        if (IsServer)
        {
            _currentOrbitAngle = Random.Range(0f, 360f);
            _orbitDirection = Random.value > 0.5f ? 1 : -1;
            _attackTimer = _attackCooldown * Random.Range(0.45f, 1.0f);
        }
    }

    public override void InitializeEnemy(EnemyTier tier, EnemyDefinition def, float finalScale, Vector3 pos)
    {
        // Spawn pozice už je ve vzduchu. Nepřítel tedy nevyjede ze země a až potom neskočí nahoru.
        Vector3 flyingSpawnPos = pos + Vector3.up * _flightHeight;
        base.InitializeEnemy(tier, def, finalScale, flyingSpawnPos);
        ConfigureFlyingHitbox();
    }

    protected override void Update()
    {
        base.Update();

        if (!CanRunFlyingBehavior())
            return;

        if (IsServer && _driveBehaviorFromUpdate)
            BehaviorLogic();

        UpdateVisualHoverAndLook();
    }

    public override void BehaviorLogic()
    {
        // EnemyMovementManager může mít v seznamu ještě pooled/despawned/inactive enemy.
        // CharacterController.Move spadne, pokud je GameObject inactiveInHierarchy, i když controller.enabled vrací true.
        if (!CanRunFlyingBehavior())
            return;

        // BehaviorLogic může volat buď EnemyMovementManager, nebo náš Update().
        // Tohle brání dvojitému pohybu ve stejném framu.
        if (_lastBehaviorFrame == Time.frameCount)
            return;
        _lastBehaviorFrame = Time.frameCount;

        if (TargetPlayer == null || _isSpawning.Value || !IsAlive)
            return;

        if (_statusReceiver != null && _statusReceiver.IsStunned)
            return;

        CacheTargetControllerIfNeeded();

        CalculateOrbitOffset();

        float moveMultiplier = 1f;
        if (_isAttacking)
            moveMultiplier = _freezeMovementDuringTelegraph ? _telegraphMovementMultiplier : Mathf.Max(_telegraphMovementMultiplier, 0.35f);

        // I během telegraphu dovolíme malý drift. Když chceš absolutně statický enemy, nastav Telegraph Movement Multiplier na 0.
        if (!_isAttacking || moveMultiplier > 0.001f)
            ApplyFlyingMovement(moveMultiplier);

        if (_isAttacking)
            return;

        if (GetFlatDistanceToTargetSqr() <= _preferredAttackRange * _preferredAttackRange)
        {
            _attackTimer -= Time.deltaTime;
            if (_attackTimer <= 0f)
            {
                _attackTimer = _attackCooldown;
                StartCoroutine(ShootRoutine());
            }
        }
    }

    private bool CanRunFlyingBehavior()
    {
        if (!Application.isPlaying)
            return false;

        if (!isActiveAndEnabled || gameObject == null || !gameObject.activeInHierarchy)
            return false;

        if (!IsSpawned)
            return false;

        if (MyTransform == null)
            MyTransform = transform;

        return true;
    }

    private bool CanUseCharacterController()
    {
        return _useCharacterControllerMove
            && _controller != null
            && _controller.gameObject != null
            && _controller.gameObject.activeInHierarchy
            && isActiveAndEnabled
            && gameObject.activeInHierarchy;
    }

    private void OnDisable()
    {
        _isAttacking = false;
        _moveVelocity = Vector3.zero;
        _aimVelocity = Vector3.zero;
        StopTelegraphVisual();
    }

    private void CacheTargetControllerIfNeeded()
    {
        if (_lastTargetPlayer == TargetPlayer)
            return;

        _lastTargetPlayer = TargetPlayer;
        _cachedTargetController = null;

        if (TargetPlayer != null)
            TargetPlayer.TryGetComponent(out _cachedTargetController);
    }

    private void CalculateOrbitOffset()
    {
        _currentOrbitAngle += _orbitSpeed * _orbitDirection * Time.deltaTime;

        Vector3 orbitDirection = Quaternion.Euler(0f, _currentOrbitAngle, 0f) * Vector3.forward;
        orbitDirection.y = 0f;
        orbitDirection.Normalize();

        float radius = Mathf.Max(_orbitRadius, _minimumHorizontalDistance);
        _targetOffset = orbitDirection * radius;

        Vector3 rayStart = TargetPlayer.position + Vector3.up * _flightHeight;
        if (_obstacleMask.value != 0 && Physics.Raycast(rayStart, orbitDirection, out _, radius, _obstacleMask, QueryTriggerInteraction.Ignore))
        {
            _orbitDirection *= -1;
            _currentOrbitAngle += 25f * _orbitDirection;
        }
    }

    private void ApplyFlyingMovement(float speedMultiplier = 1f)
    {
        if (!CanRunFlyingBehavior() || TargetPlayer == null || MyTransform == null)
            return;

        Vector3 targetPos = TargetPlayer.position + _targetOffset;
        targetPos.y = TargetPlayer.position.y + _flightHeight;

        float maxSpeed = Mathf.Max(0.01f, _movementSpeed * Mathf.Clamp01(speedMultiplier));

        Vector3 nextPos = Vector3.SmoothDamp(
            MyTransform.position,
            targetPos,
            ref _moveVelocity,
            _movementSmoothTime,
            maxSpeed,
            Time.deltaTime
        );

        Vector3 delta = nextPos - MyTransform.position;
        if (delta.sqrMagnitude < 0.000001f)
            return;

        // Defaultně tady CharacterController.Move vůbec nepoužíváme.
        // Flying enemy nepotřebuje ground controller movement a přesně tahle native Unity metoda hází
        // "CharacterController.Move called on inactive controller" při poolingu/spawnu/despawnu.
        if (CanUseCharacterController())
        {
            _controller.Move(delta);
            return;
        }

        MyTransform.position = nextPos;
    }

    private float GetFlatDistanceToTargetSqr()
    {
        Vector3 delta = MyTransform.position - TargetPlayer.position;
        delta.y = 0f;
        return delta.sqrMagnitude;
    }

    private IEnumerator ShootRoutine()
    {
        if (_projectilePrefab == null || _firePoint == null)
        {
            Debug.LogWarning($"{name}: FlyingSkirmisherAI nemá nastavený projectile prefab nebo fire point.");
            yield break;
        }

        _isAttacking = true;
        _moveVelocity = Vector3.zero;
        _aimVelocity = Vector3.zero;
        _hasLockedAim = false;

        _currentAimPosition = GetPredictedAimPoint();
        UpdateAimState(0f, forceTrack: true);

        StartTelegraphVisual();

        float timer = 0f;
        while (timer < _telegraphTime)
        {
            if (TargetPlayer == null || !IsAlive)
                break;

            timer += Time.deltaTime;
            UpdateTelegraphVisual(Mathf.Clamp01(timer / _telegraphTime));
            yield return null;
        }

        if (IsServer && TargetPlayer != null && IsAlive && IsSpawned)
        {
            UpdateAimState(1f, forceTrack: !_lockAimBeforeShot);

            if (!_hasLockedAim)
                LockAim(_currentAimPosition);

            SpawnProjectile();
        }

        StopTelegraphVisual();
        _isAttacking = false;
    }

    private Vector3 GetRawAimPoint()
    {
        if (TargetPlayer == null)
            return MyTransform.position + MyTransform.forward * 5f;

        return TargetPlayer.position + Vector3.up * _targetAimHeight;
    }

    private Vector3 GetPredictedAimPoint()
    {
        Vector3 aimPoint = GetRawAimPoint();

        if (_cachedTargetController == null || _firePoint == null)
            return aimPoint;

        Vector3 horizontalVelocity = _cachedTargetController.Velocity;
        horizontalVelocity.y = 0f;

        float distance = Vector3.Distance(_firePoint.position, aimPoint);
        float projectileSpeed = Mathf.Max(1f, _projectileStats.ProjectileSpeed);
        float flightTime = distance / projectileSpeed;

        Vector3 lead = horizontalVelocity * (flightTime * _predictionFactor);
        if (lead.magnitude > _maxPredictionLead)
            lead = lead.normalized * _maxPredictionLead;

        return aimPoint + lead;
    }

    private void UpdateAimState(float progress, bool forceTrack = false)
    {
        if (_firePoint == null)
            return;

        bool trackingPhase = progress < _aimTrackingPortion;
        bool shouldTrack = forceTrack || !_lockAimBeforeShot || trackingPhase;

        if (shouldTrack)
        {
            Vector3 desiredAimPoint = GetPredictedAimPoint();
            _currentAimPosition = Vector3.SmoothDamp(_currentAimPosition, desiredAimPoint, ref _aimVelocity, _aimSmoothTime);

            Vector3 toAim = _currentAimPosition - _firePoint.position;
            if (toAim.sqrMagnitude < 0.01f)
                toAim = GetFallbackShootDirection();

            _lockedShootDirection = toAim.normalized;
            _lockedAimPoint = _currentAimPosition;
            _hasLockedAim = false;
            return;
        }

        if (!_hasLockedAim)
            LockAim(_currentAimPosition);
    }

    private void LockAim(Vector3 aimPoint)
    {
        if (_firePoint == null)
            return;

        Vector3 toAim = aimPoint - _firePoint.position;
        if (toAim.sqrMagnitude < 0.01f)
            toAim = GetFallbackShootDirection();

        _lockedShootDirection = toAim.normalized;
        _lockedAimPoint = aimPoint;
        _hasLockedAim = true;
    }

    private Vector3 GetFallbackShootDirection()
    {
        if (_visualContainer != null && _visualContainer.forward.sqrMagnitude > 0.01f)
            return _visualContainer.forward.normalized;

        if (MyTransform.forward.sqrMagnitude > 0.01f)
            return MyTransform.forward.normalized;

        return Vector3.forward;
    }

    private float GetFullTelegraphLength()
    {
        // FullLine má odpovídat skutečnému range útoku, ne náhodné fixní délce.
        // Když WeaponStats.Range není nastavený, použijeme fallback z inspectoru.
        float range = _projectileStats.Range > 0f ? _projectileStats.Range : _telegraphLineLength;
        return Mathf.Max(2f, range);
    }

    private float GetMuzzleCueLength()
    {
        return Mathf.Max(0.25f, _muzzleCueLength);
    }

    private bool UsesTrajectoryLine()
    {
        return _telegraphVisualStyle == TelegraphVisualStyle.MuzzleCue
            || _telegraphVisualStyle == TelegraphVisualStyle.FullLine;
    }

    private bool UsesGroundDangerLine()
    {
        return _telegraphVisualStyle == TelegraphVisualStyle.FullLine;
    }

    private bool UsesAimMarker()
    {
        return _telegraphVisualStyle == TelegraphVisualStyle.AimMarker
            || _telegraphVisualStyle == TelegraphVisualStyle.FullLine;
    }

    private void SpawnProjectile()
    {
        if (_projectilePrefab == null || _firePoint == null)
            return;

        if (_lockedShootDirection.sqrMagnitude < 0.01f)
            _lockedShootDirection = GetFallbackShootDirection();

        Quaternion rotation = Quaternion.LookRotation(_lockedShootDirection, Vector3.up);
        Vector3 spawnPosition = _firePoint.position + _lockedShootDirection * _projectileSpawnForwardOffset;

        GameObject proj = Instantiate(_projectilePrefab, spawnPosition, rotation);

        // WeaponStats je u tebe value type/struct, proto ho nekontrolujeme proti null.
        // Pracujeme s runtime kopií, abychom neměnili serialized hodnotu přímo na enemy prefab instanci.
        WeaponStats runtimeStats = _projectileStats;
        runtimeStats.Damage = _currentDamage;

        if (proj.TryGetComponent(out SmartProjectile smartProj))
        {
            smartProj.Initialize(NetworkObject, _lockedShootDirection, runtimeStats);
        }
        else
        {
            Debug.LogWarning($"{name}: Projectile prefab nemá SmartProjectile komponentu.");
        }

        if (proj.TryGetComponent(out NetworkObject netObj))
        {
            if (!netObj.IsSpawned)
                netObj.Spawn(true);
        }
        else
        {
            Debug.LogWarning($"{name}: Projectile prefab nemá NetworkObject. V multiplayeru se nemusí zobrazit klientům.");
        }
    }

    private void UpdateVisualHoverAndLook()
    {
        if (_visualContainer == null || _isSpawning.Value || !IsAlive)
            return;

        float hoverY = Mathf.Sin((Time.time + _hoverPhase) * _hoverSpeed) * _hoverAmplitude;
        _visualContainer.localPosition = _visualBaseLocalPosition + Vector3.up * hoverY;

        if (TargetPlayer == null)
            return;

        Vector3 lookTarget = _isAttacking ? _lockedAimPoint : GetRawAimPoint();
        Vector3 lookDir = lookTarget - _visualContainer.position;

        if (lookDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
            _visualContainer.rotation = Quaternion.Slerp(_visualContainer.rotation, targetRot, Time.deltaTime * 8f);
        }
    }

    private void ConfigureFlyingHitbox()
    {
        if (!_configureFlyingHitbox || _controller == null)
            return;

        bool wasEnabled = _controller.enabled;
        if (wasEnabled)
            _controller.enabled = false;

        _controller.height = Mathf.Max(0.2f, _hitboxHeight);
        _controller.radius = Mathf.Max(0.05f, _hitboxRadius);
        _controller.center = _hitboxCenter;
        _controller.stepOffset = 1f;

        if (wasEnabled)
            _controller.enabled = true;
    }

    private void ConfigureLineRenderer(LineRenderer line, bool active)
    {
        if (line == null)
            return;

        line.positionCount = 2;
        line.useWorldSpace = true;
        line.enabled = active;
        line.gameObject.SetActive(active);
    }

    private Vector3 GetLineEnd(Vector3 start, Vector3 direction, float length)
    {
        Vector3 end = start + direction * length;

        if (_clampTelegraphToObstacles && _obstacleMask.value != 0 && Physics.Raycast(start, direction, out RaycastHit hit, length, _obstacleMask, QueryTriggerInteraction.Ignore))
            end = hit.point;

        return end;
    }

    private Vector3 ProjectPointToGround(Vector3 point)
    {
        Vector3 rayStart = point + Vector3.up * 3f;

        if (_groundMask.value != 0 && Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 40f, _groundMask, QueryTriggerInteraction.Ignore))
            return hit.point + Vector3.up * _groundLineLift;

        float fallbackY = TargetPlayer != null ? TargetPlayer.position.y + _groundLineLift : point.y;
        return new Vector3(point.x, fallbackY, point.z);
    }

    private void ApplyLineVisual(LineRenderer line, Vector3 start, Vector3 end, float progress, bool fadeEnd)
    {
        if (line == null)
            return;

        float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
        float width = Mathf.Lerp(_telegraphStartWidth, _telegraphEndWidth, easedProgress);

        Color color = Color.Lerp(_trajectoryStartColor, _trajectoryEndColor, easedProgress);
        color.a = Mathf.Lerp(_trajectoryStartColor.a, _telegraphEndAlpha, easedProgress);

        Color endColor = color;
        if (fadeEnd)
            endColor.a *= 0.35f;

        line.positionCount = 2;
        line.useWorldSpace = true;
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = endColor;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
    }

    #region Telegraph Visuals
    public override void StartTelegraphVisual()
    {
        _hasLockedAim = false;
        _currentAimPosition = GetPredictedAimPoint();
        _lockedAimPoint = _currentAimPosition;

        if (_firePoint != null)
        {
            Vector3 toAim = _currentAimPosition - _firePoint.position;
            _lockedShootDirection = toAim.sqrMagnitude > 0.01f ? toAim.normalized : GetFallbackShootDirection();
        }

        if (_chargeUpVFX != null)
            _chargeUpVFX.SetActive(true);

        ConfigureLineRenderer(_trajectoryLine, UsesTrajectoryLine());
        ConfigureLineRenderer(_groundDangerLine, UsesGroundDangerLine());

        if (_aimLockMarker != null)
            _aimLockMarker.SetActive(UsesAimMarker());
    }

    public override void UpdateTelegraphVisual(float progress)
    {
        if (_firePoint == null || TargetPlayer == null)
            return;

        UpdateAimState(progress);

        Vector3 lineStart = _firePoint.position;

        if (_telegraphVisualStyle == TelegraphVisualStyle.MuzzleCue)
        {
            Vector3 shortEnd = lineStart + _lockedShootDirection * GetMuzzleCueLength();
            ApplyLineVisual(_trajectoryLine, lineStart, shortEnd, progress, fadeEnd: true);
        }
        else if (_telegraphVisualStyle == TelegraphVisualStyle.FullLine)
        {
            float length = GetFullTelegraphLength();
            Vector3 lineEnd = GetLineEnd(lineStart, _lockedShootDirection, length);

            ApplyLineVisual(_trajectoryLine, lineStart, lineEnd, progress, fadeEnd: true);

            if (_groundDangerLine != null)
            {
                Vector3 groundStart = ProjectPointToGround(lineStart);
                Vector3 groundEnd = ProjectPointToGround(lineEnd);
                ApplyLineVisual(_groundDangerLine, groundStart, groundEnd, progress, fadeEnd: false);
            }
        }

        if (_aimLockMarker != null && UsesAimMarker())
        {
            Vector3 markerPoint = ProjectPointToGround(_lockedAimPoint);
            _aimLockMarker.transform.position = markerPoint;

            float markerScale = Mathf.Lerp(0.25f, 0.6f, Mathf.SmoothStep(0f, 1f, progress));
            _aimLockMarker.transform.localScale = Vector3.one * markerScale;
        }
    }

    public override void StopTelegraphVisual()
    {
        if (_chargeUpVFX != null)
            _chargeUpVFX.SetActive(false);

        ConfigureLineRenderer(_trajectoryLine, false);
        ConfigureLineRenderer(_groundDangerLine, false);

        if (_aimLockMarker != null)
            _aimLockMarker.SetActive(false);
    }
    #endregion
}
