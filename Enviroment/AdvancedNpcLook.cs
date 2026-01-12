using UnityEngine;

public class AdvancedNpcLook : MonoBehaviour
{
    [Header("Bones")]
    [Tooltip("Transform hlavy nebo krku.")]
    [SerializeField] private Transform _headBone;
    
    [Header("Settings")]
    [SerializeField] private float _lookRadius = 8f;
    [SerializeField] private float _headTurnSpeed = 5f;
    [SerializeField] private float _bodyTurnSpeed = 2f;
    
    [Tooltip("Maximální úhel hlavy. Pokud je cíl dál, začne se točit tělo.")]
    [SerializeField] private float _maxHeadAngle = 70f;

    private Transform _target;
    private Quaternion _initialHeadRotation;

    private void Start()
    {
        if (_headBone) _initialHeadRotation = _headBone.localRotation;
    }

    private void Update()
    {
        // 1. Najít cíl (optimalizace: nehledat každý frame, ale pro jednoduchost zde OK)
        FindClosestPlayer();

        if (_target != null)
        {
            Vector3 directionToTarget = _target.position - transform.position;
            directionToTarget.y = 0; // Ignorujeme výšku pro rotaci těla
            
            // --- Logika Těla ---
            // Změříme úhel mezi "kam koukám tělem" a "kde je cíl"
            float angleBody = Vector3.Angle(transform.forward, directionToTarget);

            // Pokud je úhel velký, otáčíme celým tělem
            if (angleBody > _maxHeadAngle)
            {
                Quaternion targetBodyRot = Quaternion.LookRotation(directionToTarget);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetBodyRot, Time.deltaTime * _bodyTurnSpeed);
            }

            // --- Logika Hlavy ---
            if (_headBone != null)
            {
                // Vypočítáme směr přímo na hlavu cíle (včetně výšky)
                Vector3 headDir = _target.position - _headBone.position;
                Quaternion lookRot = Quaternion.LookRotation(headDir);
                
                // Převedeme do lokálního prostoru rodiče hlavy, abychom mohli limitovat úhly
                // (Tohle je zjednodušená verze, která funguje pro většinu humanoidů)
                _headBone.rotation = Quaternion.Slerp(_headBone.rotation, lookRot, Time.deltaTime * _headTurnSpeed);
                
                // Clampování (pokud chceme být extra precizní, ale logika těla výše to řeší přirozeněji)
            }
        }
        else
        {
            // Reset hlavy, když nikdo není nablízku
            if (_headBone)
            {
                _headBone.localRotation = Quaternion.Slerp(_headBone.localRotation, _initialHeadRotation, Time.deltaTime * _headTurnSpeed);
            }
        }
    }

    private void FindClosestPlayer()
    {
        // Rychlá detekce bez alokace paměti (pomocí Physics.OverlapSphereNonAlloc by to bylo lepší, ale toto stačí)
        Collider[] hits = Physics.OverlapSphere(transform.position, _lookRadius, LayerMask.GetMask("Player", "Default")); // Nastav LayerMask dle potřeby
        
        float closestDist = float.MaxValue;
        _target = null;

        foreach (var hit in hits)
        {
            if (hit.CompareTag("Player"))
            {
                float d = Vector3.Distance(transform.position, hit.transform.position);
                if (d < closestDist)
                {
                    closestDist = d;
                    _target = hit.transform;
                    // Zkusíme najít "Head" bone hráče pro lepší oční kontakt, jinak bereme pivot
                    Transform head = hit.transform.Find("Armature/Hips/Spine/Head"); // Příklad cesty
                    if (head) _target = head;
                }
            }
        }
    }
}