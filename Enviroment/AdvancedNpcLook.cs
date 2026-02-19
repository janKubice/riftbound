using UnityEngine;

public class AdvancedNpcLook : MonoBehaviour
{
    [Header("Bones")]
    [SerializeField] private Transform _headBone;
    
    [Header("Settings")]
    [SerializeField] private float _lookRadius = 8f;
    [SerializeField] private float _headTurnSpeed = 5f;
    [SerializeField] private float _bodyTurnSpeed = 2f;
    [SerializeField] private Vector3 _lookOffset = new Vector3(0, 1.6f, 0); 
    
    [Header("Angles & Hysteresis")]
    [Tooltip("Kdy se tělo ZAČNE otáčet (např. 70 stupňů).")]
    [SerializeField] private float _startBodyTurnAngle = 70f;
    [Tooltip("Kdy se tělo PŘESTANE otáčet (např. 40 stupňů). Musí být menší než Start.")]
    [SerializeField] private float _stopBodyTurnAngle = 40f;

    [Header("Optimization")]
    [SerializeField] private float _searchInterval = 0.5f;
    [SerializeField] private LayerMask _targetLayer; 

    private Transform _target;
    private Quaternion _initialHeadRotation;
    
    private float _nextSearchTime;
    private readonly Collider[] _hitBuffer = new Collider[20]; 
    
    // Stavová proměnná pro Hysterezi
    private bool _isTurningBody = false;

    private void Start()
    {
        if (_headBone) _initialHeadRotation = _headBone.localRotation;
        _nextSearchTime = Time.time + Random.Range(0f, _searchInterval);
    }

    private void LateUpdate()
    {
        if (Time.time >= _nextSearchTime)
        {
            FindClosestPlayer();
            _nextSearchTime = Time.time + _searchInterval;
        }

        if (_target != null)
        {
            Vector3 targetLookPosition = _target.position + _lookOffset;
            Vector3 dirToTarget = targetLookPosition - transform.position;
            
            // --- 1. Stabilnější výpočet pro tělo (pouze Y osa) ---
            Vector3 flatDir = Vector3.ProjectOnPlane(dirToTarget, Vector3.up);

            if (flatDir != Vector3.zero)
            {
                float angleBody = Vector3.Angle(transform.forward, flatDir);

                // --- HYSTEREZE (Řešení kmitání) ---
                if (_isTurningBody)
                {
                    // Pokud už se točíme, zastavíme až když jsme dostatečně srovnaní (např. pod 40 stupňů)
                    if (angleBody < _stopBodyTurnAngle) _isTurningBody = false;
                }
                else
                {
                    // Pokud stojíme, začneme se točit až při velkém úhlu (např. nad 70 stupňů)
                    if (angleBody > _startBodyTurnAngle) _isTurningBody = true;
                }

                if (_isTurningBody)
                {
                    Quaternion targetBodyRot = Quaternion.LookRotation(flatDir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetBodyRot, Time.deltaTime * _bodyTurnSpeed);
                }
            }

            // --- 2. Vylepšená logika hlavy (Up Vector) ---
            if (_headBone != null)
            {
                Vector3 headDir = targetLookPosition - _headBone.position;
                if (headDir != Vector3.zero)
                {
                    // DŮLEŽITÉ: Jako druhý parametr dáváme transform.up.
                    // To zajistí, že hlava respektuje náklon těla a "nekroutí" se divně.
                    Quaternion lookRot = Quaternion.LookRotation(headDir, transform.up);
                    _headBone.rotation = Quaternion.Slerp(_headBone.rotation, lookRot, Time.deltaTime * _headTurnSpeed);
                }
            }
        }
        else
        {
            if (_headBone)
            {
                _headBone.localRotation = Quaternion.Slerp(_headBone.localRotation, _initialHeadRotation, Time.deltaTime * _headTurnSpeed);
            }
            // Když nemáme cíl, resetujeme stav otáčení těla
            _isTurningBody = false;
        }
    }

    private void FindClosestPlayer()
    {
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, _lookRadius, _hitBuffer, _targetLayer);
        
        float closestDist = float.MaxValue;
        Transform bestTarget = null;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _hitBuffer[i];
            if (hit.CompareTag("Player") && hit.transform != transform)
            {
                float d = Vector3.Distance(transform.position, hit.transform.position);
                if (d < closestDist)
                {
                    closestDist = d;
                    bestTarget = hit.transform;
                }
            }
        }
        
        _target = bestTarget;
    }
}