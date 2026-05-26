using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class DasherEnemyAI : EnemyBaseAI
{
    private enum DashState
    {
        Chasing,
        Telegraphing,
        Leaping,
        Recovering
    }

    [Header("Leap Trigger")]
    [SerializeField] private float _leapTriggerRange = 9.0f;
    [SerializeField] private float _minLeapRange = 2.0f;
    [SerializeField] private float _leapCooldown = 3.2f;

    [Header("Leap Motion")]
    [SerializeField] private float _leapDistance = 8.0f;

    [Tooltip("Jak dlouho trvá samotný skok.")]
    [SerializeField] private float _leapDuration = 0.45f;

    [Tooltip("Výška oblouku skoku.")]
    [SerializeField] private float _leapHeight = 2.8f;

    [Tooltip("Křivka výšky. X = progress skoku, Y = výška 0-1.")]
    [SerializeField] private AnimationCurve _heightCurve = new AnimationCurve(
        new Keyframe(0.0f, 0.0f),
        new Keyframe(0.5f, 1.0f),
        new Keyframe(1.0f, 0.0f)
    );

    [Tooltip("Na konci skoku se nepřítel dosnapuje na zem.")]
    [SerializeField] private bool _snapToGroundOnLanding = true;

    [SerializeField] private float _groundSnapRayHeight = 6.0f;
    [SerializeField] private float _groundSnapRayDistance = 12.0f;
    [SerializeField] private LayerMask _groundMask = ~0;

    [Header("Telegraph")]
    [SerializeField] private float _telegraphDuration = 0.8f;
    [SerializeField] private float _recoveryDuration = 0.55f;

    [Header("Prediction")]
    [Range(0f, 1.5f)]
    [SerializeField] private float _predictionFactor = 0.45f;

    [SerializeField] private float _maxPredictionDistance = 2.5f;

    [Header("Hit Detection")]
    [SerializeField] private LayerMask _playerHitMask = ~0;
    [SerializeField] private float _hitRadius = 0.85f;
    [SerializeField] private float _hitCheckHeight = 0.9f;
    [SerializeField] private bool _damageEachPlayerOnlyOncePerLeap = true;

    [Header("Landing Impact")]
    [SerializeField] private bool _damageOnLanding = true;
    [SerializeField] private float _landingDamageRadius = 1.8f;
    [SerializeField] private float _landingDamageMultiplier = 1.0f;

    [Header("Obstacle")]
    [SerializeField] private bool _clampEndPointByObstacle = true;
    [SerializeField] private LayerMask _obstacleMask = ~0;
    [SerializeField] private float _obstacleSphereRadius = 0.5f;

    [Header("Visuals")]
    [SerializeField] private LineRenderer _leapLine;
    [SerializeField] private Transform _groundWarning;
    [SerializeField] private Renderer _groundWarningRenderer;
    [SerializeField] private float _groundWarningYOffset = 0.04f;
    [SerializeField] private float _groundWarningWidth = 1.2f;

    [Header("VFX")]
    [SerializeField] private GameObject _chargeVFX;
    [SerializeField] private GameObject _leapTrailVFX;
    [SerializeField] private GameObject _landingImpactVFX;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _chargeSfx;
    [SerializeField] private AudioClip _leapSfx;
    [SerializeField] private AudioClip _landSfx;
    [SerializeField] private AudioClip _hitSfx;

    [Header("Debug")]
    [SerializeField] private bool _drawDebug = false;

    private DashState _state = DashState.Chasing;

    private float _lastLeapTime = -999f;

    private Vector3 _lockedDirection;
    private Vector3 _lockedStartPoint;
    private Vector3 _lockedEndPoint;

    private PlayerController _cachedTargetController;
    private Transform _lastTarget;

    private readonly HashSet<ulong> _damagedPlayersThisLeap = new HashSet<ulong>();
    private MaterialPropertyBlock _warningBlock;

    protected override void Awake()
    {
        base.Awake();

        _useFlowFieldMovement = true;
        _warningBlock = new MaterialPropertyBlock();

        if (_leapLine != null)
        {
            _leapLine.positionCount = 2;
            _leapLine.enabled = false;
            _leapLine.gameObject.SetActive(false);
            _leapLine.useWorldSpace = true;
        }

        if (_groundWarning != null)
            _groundWarning.gameObject.SetActive(false);

        if (_chargeVFX != null)
            _chargeVFX.SetActive(false);

        if (_leapTrailVFX != null)
            _leapTrailVFX.SetActive(false);
    }

    public override void BehaviorLogic()
    {
        if (TargetPlayer == null || _isSpawning.Value)
            return;

        CacheTargetController();

        if (_state != DashState.Chasing)
        {
            IsMovementPaused = true;
            return;
        }

        float sqrDistance = (TargetPlayer.position - MyTransform.position).sqrMagnitude;
        float triggerSqr = _leapTriggerRange * _leapTriggerRange;
        float minSqr = _minLeapRange * _minLeapRange;

        bool inLeapRange = sqrDistance <= triggerSqr && sqrDistance >= minSqr;
        bool cooldownReady = Time.time >= _lastLeapTime + _leapCooldown;

        if (inLeapRange && cooldownReady)
        {
            StartCoroutine(LeapRoutine());
            return;
        }

        IsMovementPaused = false;
    }

    private void CacheTargetController()
    {
        if (_lastTarget == TargetPlayer)
            return;

        _lastTarget = TargetPlayer;
        _cachedTargetController = null;

        if (TargetPlayer != null)
            TargetPlayer.TryGetComponent(out _cachedTargetController);
    }

    private IEnumerator LeapRoutine()
    {
        _state = DashState.Telegraphing;
        IsMovementPaused = true;
        _damagedPlayersThisLeap.Clear();

        _lockedStartPoint = MyTransform.position;

        Vector3 predictedTarget = GetPredictedTargetPosition();

        _lockedDirection = predictedTarget - _lockedStartPoint;
        _lockedDirection.y = 0f;

        if (_lockedDirection.sqrMagnitude < 0.001f)
            _lockedDirection = MyTransform.forward;

        _lockedDirection.Normalize();

        _lockedEndPoint = _lockedStartPoint + _lockedDirection * _leapDistance;
        _lockedEndPoint = ResolveEndPoint(_lockedStartPoint, _lockedEndPoint);

        PlayTelegraphClientRpc(_lockedStartPoint, _lockedEndPoint, _telegraphDuration);

        if (_audioSource != null && _chargeSfx != null)
            _audioSource.PlayOneShot(_chargeSfx);

        float telegraphTimer = 0f;

        while (telegraphTimer < _telegraphDuration)
        {
            telegraphTimer += Time.deltaTime;
            RotateToPoint(_lockedEndPoint);
            yield return null;
        }

        _state = DashState.Leaping;

        PlayLeapStartClientRpc();

        if (_audioSource != null && _leapSfx != null)
            _audioSource.PlayOneShot(_leapSfx);

        yield return LeapMotionRoutine();

        if (_snapToGroundOnLanding)
            SnapToGround();

        CheckLandingImpact();

        PlayLeapEndClientRpc(MyTransform.position);

        _state = DashState.Recovering;

        yield return new WaitForSeconds(_recoveryDuration);

        _lastLeapTime = Time.time;
        _state = DashState.Chasing;
        IsMovementPaused = false;
    }

    private IEnumerator LeapMotionRoutine()
    {
        float timer = 0f;

        Vector3 previousTargetPosition = MyTransform.position;

        while (timer < _leapDuration)
        {
            timer += Time.deltaTime;

            float t = Mathf.Clamp01(timer / Mathf.Max(0.01f, _leapDuration));

            Vector3 flatPosition = Vector3.Lerp(_lockedStartPoint, _lockedEndPoint, t);

            float height01 = _heightCurve != null
                ? _heightCurve.Evaluate(t)
                : Mathf.Sin(t * Mathf.PI);

            float y = Mathf.Lerp(_lockedStartPoint.y, _lockedEndPoint.y, t) + height01 * _leapHeight;

            Vector3 targetPosition = new Vector3(flatPosition.x, y, flatPosition.z);
            Vector3 delta = targetPosition - previousTargetPosition;

            MoveControllerRaw(delta);

            previousTargetPosition = targetPosition;

            RotateToPoint(_lockedEndPoint);
            CheckPlayerHits();

            yield return null;
        }
    }

    private void MoveControllerRaw(Vector3 delta)
    {
        if (_controller != null && _controller.enabled)
        {
            CollisionFlags flags = _controller.Move(delta);

            if ((flags & CollisionFlags.Sides) != 0 && _state == DashState.Leaping)
            {
                // Pokud narazí do stěny, skok přirozeně skončí blízko překážky.
                // Záměrně neděláme extra logiku, CharacterController už kolizi vyřeší.
            }
        }
        else
        {
            MyTransform.position += delta;
        }
    }

    private Vector3 GetPredictedTargetPosition()
    {
        if (TargetPlayer == null)
            return MyTransform.position + MyTransform.forward;

        Vector3 targetPos = TargetPlayer.position;

        if (_cachedTargetController != null)
        {
            Vector3 prediction = _cachedTargetController.Velocity * _predictionFactor;
            prediction = Vector3.ClampMagnitude(prediction, _maxPredictionDistance);
            targetPos += prediction;
        }

        return targetPos;
    }

    private Vector3 ResolveEndPoint(Vector3 start, Vector3 desiredEnd)
    {
        Vector3 direction = desiredEnd - start;
        direction.y = 0f;

        float distance = direction.magnitude;

        if (distance <= 0.001f)
            return desiredEnd;

        direction.Normalize();

        if (_clampEndPointByObstacle)
        {
            Vector3 origin = start + Vector3.up * 0.8f;

            if (Physics.SphereCast(
                    origin,
                    Mathf.Max(0.1f, _obstacleSphereRadius),
                    direction,
                    out RaycastHit hit,
                    distance,
                    _obstacleMask,
                    QueryTriggerInteraction.Ignore))
            {
                return hit.point - direction * 0.45f;
            }
        }

        return desiredEnd;
    }

    private void SnapToGround()
    {
        Vector3 rayOrigin = MyTransform.position + Vector3.up * _groundSnapRayHeight;

        if (Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                _groundSnapRayDistance,
                _groundMask,
                QueryTriggerInteraction.Ignore))
        {
            if (_controller != null && _controller.enabled)
            {
                Vector3 delta = hit.point - MyTransform.position;
                _controller.Move(delta);
            }
            else
            {
                MyTransform.position = hit.point;
            }
        }
    }

    private void CheckPlayerHits()
    {
        Vector3 hitCenter = MyTransform.position + Vector3.up * _hitCheckHeight;

        Collider[] hits = Physics.OverlapSphere(
            hitCenter,
            _hitRadius,
            _playerHitMask,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hits.Length; i++)
        {
            TryDamagePlayer(hits[i], _currentDamage);
        }
    }

    private void CheckLandingImpact()
    {
        if (!_damageOnLanding)
            return;

        Collider[] hits = Physics.OverlapSphere(
            MyTransform.position + Vector3.up * 0.4f,
            _landingDamageRadius,
            _playerHitMask,
            QueryTriggerInteraction.Ignore
        );

        int landingDamage = Mathf.RoundToInt(_currentDamage * _landingDamageMultiplier);

        for (int i = 0; i < hits.Length; i++)
        {
            TryDamagePlayer(hits[i], landingDamage);
        }
    }

    private void TryDamagePlayer(Collider hit, int damage)
    {
        if (hit == null || damage <= 0)
            return;

        PlayerAttributes player = hit.GetComponent<PlayerAttributes>();

        if (player == null)
            player = hit.GetComponentInParent<PlayerAttributes>();

        if (player == null)
            return;

        NetworkObject playerNetObj = player.GetComponent<NetworkObject>();

        if (playerNetObj == null)
            playerNetObj = player.GetComponentInParent<NetworkObject>();

        if (playerNetObj == null)
            return;

        if (_damageEachPlayerOnlyOncePerLeap)
        {
            if (_damagedPlayersThisLeap.Contains(playerNetObj.NetworkObjectId))
                return;

            _damagedPlayersThisLeap.Add(playerNetObj.NetworkObjectId);
        }

        player.TakeDamageServerRpc(damage, NetworkObject.OwnerClientId);

        if (_audioSource != null && _hitSfx != null)
            _audioSource.PlayOneShot(_hitSfx);
    }

    [ClientRpc]
    private void PlayTelegraphClientRpc(Vector3 start, Vector3 end, float duration)
    {
        if (_leapLine != null)
        {
            _leapLine.gameObject.SetActive(true);
            _leapLine.enabled = true;
            _leapLine.SetPosition(0, start + Vector3.up * 0.12f);
            _leapLine.SetPosition(1, end + Vector3.up * 0.12f);
        }

        if (_groundWarning != null)
        {
            Vector3 midpoint = (start + end) * 0.5f;
            Vector3 dir = end - start;
            dir.y = 0f;

            float length = Mathf.Max(0.1f, dir.magnitude);

            _groundWarning.gameObject.SetActive(true);
            _groundWarning.position = midpoint + Vector3.up * _groundWarningYOffset;

            if (dir.sqrMagnitude > 0.001f)
                _groundWarning.rotation = Quaternion.LookRotation(dir.normalized);

            _groundWarning.localScale = new Vector3(
                _groundWarningWidth,
                1f,
                length
            );

            if (_groundWarningRenderer != null)
            {
                _groundWarningRenderer.GetPropertyBlock(_warningBlock);

                Color c = new Color(1f, 0.25f, 0.08f, 0.55f);
                _warningBlock.SetColor("_BaseColor", c);
                _warningBlock.SetColor("_Color", c);

                _groundWarningRenderer.SetPropertyBlock(_warningBlock);
            }
        }

        if (_chargeVFX != null)
            _chargeVFX.SetActive(true);

        StartCoroutine(HideTelegraphAfterDelay(duration));
    }

    private IEnumerator HideTelegraphAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (_leapLine != null)
        {
            _leapLine.enabled = false;
            _leapLine.gameObject.SetActive(false);
        }

        if (_groundWarning != null)
            _groundWarning.gameObject.SetActive(false);

        if (_chargeVFX != null)
            _chargeVFX.SetActive(false);
    }

    [ClientRpc]
    private void PlayLeapStartClientRpc()
    {
        if (_leapTrailVFX != null)
            _leapTrailVFX.SetActive(true);

        if (_chargeVFX != null)
            _chargeVFX.SetActive(false);

        if (_leapLine != null)
        {
            _leapLine.enabled = false;
            _leapLine.gameObject.SetActive(false);
        }

        if (_groundWarning != null)
            _groundWarning.gameObject.SetActive(false);
    }

    [ClientRpc]
    private void PlayLeapEndClientRpc(Vector3 position)
    {
        if (_leapTrailVFX != null)
            _leapTrailVFX.SetActive(false);

        if (_landingImpactVFX != null)
        {
            GameObject instance = Instantiate(_landingImpactVFX, position, Quaternion.identity);
            Destroy(instance, 4f);
        }

        if (_audioSource != null && _landSfx != null)
            _audioSource.PlayOneShot(_landSfx);
    }

    private void OnDrawGizmosSelected()
    {
        if (!_drawDebug)
            return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _leapTriggerRange);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, _minLeapRange);

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position + Vector3.up * 0.75f, transform.forward * _leapDistance);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * _hitCheckHeight, _hitRadius);
    }
}