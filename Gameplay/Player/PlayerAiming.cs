using UnityEngine;
using Unity.Netcode;
using UnityEngine.Animations.Rigging;

public class PlayerAiming : NetworkBehaviour
{
    [Header("Rigging")]
    [SerializeField] private Transform _aimTarget; // Objekt v Rigu, kam postava kouká
    [SerializeField] private Rig _aimRig; // Komponenta Rig na objektu AimRig
    [SerializeField] private float _aimSmoothSpeed = 15f;

    [Header("Idle Look Logic")]
    [SerializeField] private float _idleLookRadius = 10f;
    [SerializeField] private LayerMask _playerLayer;
    [SerializeField] private float _idleSwitchTargetInterval = 2.0f;

    [Header("Networking")]
    // Synchronizujeme pozici cíle, aby ostatní viděli, kam koukám
    private NetworkVariable<Vector3> _networkAimPosition = new NetworkVariable<Vector3>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private Camera _mainCamera;
    private PlayerController _controller;
    private Transform _currentTargetPlayer;
    private float _lastIdleCheckTime;
    private Vector3 _currentAimPos;

    public Vector3 CurrentAimPoint => _currentAimPos; // Veřejné pro střelbu

    public override void OnNetworkSpawn()
    {
        try
        {
            if (IsOwner)
            {
                _mainCamera = Camera.main; // Nebo vaše Cinemachine kamera
                _controller = GetComponent<PlayerController>();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CRITICAL SPAWN ERROR] Chyba v {name}: {e.Message}\n{e.StackTrace}");
        }

    }

    private void Update()
    {
        if (IsOwner)
        {
            HandleLocalAiming();
        }
        else
        {
            // Klienti jen interpolují cíl na základě dat ze sítě
            _currentAimPos = Vector3.Lerp(_currentAimPos, _networkAimPosition.Value, Time.deltaTime * _aimSmoothSpeed);
        }

        // Aplikace pozice na IK Target (fyzický vizuál)
        if (_aimTarget != null)
        {
            _aimTarget.position = _currentAimPos;
        }
    }

    public override void OnDestroy()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned && !NetworkManager.Singleton.IsServer)
        {
            if (!NetworkManager.Singleton.ShutdownInProgress)
            {
                Debug.LogError($"[Security Alert] Objekt {gameObject.name} byl smazán lokálně na klientovi! " +
                               $"To způsobí Invalid Destroy chybu. Prověřte volání v tomto skriptu.");
            }
        }
        base.OnDestroy();
    }

    private void HandleLocalAiming()
    {
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
        }

        // 2. Pokud ani teď nemáme kameru (např. při načítání scény), raději to přeskočíme, aby hra nespadla.
        if (_mainCamera == null)
        {
            return;
        }

        Vector3 targetPoint = Vector3.zero;
        bool isIdle = _controller.Velocity.magnitude < 0.1f; // Předpokládám public property Velocity v PlayerController

        // 1. Logika: Pokud se hýbu nebo mířím myší
        // Raycast ze středu obrazovky
        Ray ray = _mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));

        // Fixní vzdálenost pokud nic netrefíme (aby se postava nekroutila divně)
        Vector3 defaultAimPoint = ray.GetPoint(20f);

        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            targetPoint = hit.point;
        }
        else
        {
            targetPoint = defaultAimPoint;
        }

        // 2. Logika: Idle Look (Koukání na ostatní)
        if (isIdle)
        {
            HandleIdleLooking(ref targetPoint);
        }
        else
        {
            _currentTargetPlayer = null; // Reset pokud se pohnu
        }

        // 3. Update lokální proměnné a sítě
        _currentAimPos = Vector3.Lerp(_currentAimPos, targetPoint, Time.deltaTime * _aimSmoothSpeed);
        _networkAimPosition.Value = _currentAimPos;
    }

    private void HandleIdleLooking(ref Vector3 currentTarget)
    {
        if (Time.time > _lastIdleCheckTime + _idleSwitchTargetInterval)
        {
            _lastIdleCheckTime = Time.time;
            FindClosestPlayer();
        }

        if (_currentTargetPlayer != null)
        {
            // Pokud máme cíl, díváme se na jeho hlavu (odhadem +1.5y)
            Vector3 lookAtPos = _currentTargetPlayer.position + Vector3.up * 1.5f;

            // Smícháme pohled kamery s pohledem na hráče (váha 0.7 pro hráče)
            currentTarget = Vector3.Lerp(currentTarget, lookAtPos, 0.7f);
        }
    }

    private void FindClosestPlayer()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, _idleLookRadius, _playerLayer);
        float closestDist = float.MaxValue;
        Transform bestTarget = null;

        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue; // Ignorovat sebe

            float d = Vector3.Distance(transform.position, hit.transform.position);
            if (d < closestDist)
            {
                closestDist = d;
                bestTarget = hit.transform;
            }
        }
        _currentTargetPlayer = bestTarget;
    }

    // Pro debug v editoru
    private void OnDrawGizmos()
    {
        if (_aimTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(_aimTarget.position, 0.1f);
        }
    }
}