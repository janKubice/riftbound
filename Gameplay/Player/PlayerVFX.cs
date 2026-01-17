using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Collections; // Potřebujeme pro Dictionary

// Tento skript přidejte na prefab Hráče
public class PlayerVFX : NetworkBehaviour
{
    [Header("Invulnerability (Nesmrtelnost)")]
    [Tooltip("Všechny renderery postavy, u kterých změníme materiály.")]
    [SerializeField] private SkinnedMeshRenderer[] _playerRenderers;

    [Tooltip("Materiál, který se použije během nesmrtelnosti (nahradí VŠECHNY materiály na rendererech).")]
    [SerializeField] private Material _invulnerableMaterial;

    private Dictionary<SkinnedMeshRenderer, Material[]> _originalMaterials = new Dictionary<SkinnedMeshRenderer, Material[]>();


    [Header("Dodge (Úhyb)")]
    [Tooltip("Komponenta TrailRenderer (ideálně na pod-objektu hráče).")]
    [SerializeField] private TrailRenderer[] _dodgeTrails;

    [Header("Movement Particles (Částice)")]
    [Tooltip("Prefab částicového efektu, který se spawne při dopadu.")]
    [SerializeField] private GameObject _landingParticlesPrefab;
    [Tooltip("Částicový systém, který hraje při sprintu (musí být na hráči a nastaven na 'Looping').")]
    [SerializeField] private ParticleSystem _sprintParticles;

    [Header("Shop / Levitation FX")]
    [Tooltip("Efekt na těle hráče (Shine) - hýbe se s hráčem.")]
    [SerializeField] private GameObject _bodyLevitationVFX;

    [Tooltip("Efekt na zemi (Kruh/Pilíř) - zůstává přilepený k zemi.")]
    [SerializeField] private GameObject _groundLevitationVFX;

    [Tooltip("Vrstvy, které považujeme za zem (aby se kruh nechyta hráče).")]
    [SerializeField] private LayerMask _groundLayerMask = 1; // Defaultně "Default"
    // ----------------------------------

    [Header("Required Components (Komponenty)")]
    [Tooltip("Odkaz na PlayerAttributes pro čtení IsInvulnerable.")]
    [SerializeField] private PlayerAttributes _attributes;
    [Tooltip("Odkaz na Animator pro čtení stavu sprintu/země.")]
    [SerializeField] private Animator _animator;

    [Header("Damage Feedback")]
    [Tooltip("Materiál při poškození (červený flash).")]
    [SerializeField] private Material _damageMaterial;
    [SerializeField] private float _damageFlashDuration = 0.15f;
    private Coroutine _damageFlashRoutine;
    private bool _isLevitating = false;

    // Pomocný enum pro RPC
    public enum VFX_Type { DodgeTrail, LandingDust }

    private void Awake()
    {
        // Automatické nalezení, pokud není nastaveno
        if (_attributes == null) _attributes = GetComponent<PlayerAttributes>();
        if (_animator == null) _animator = GetComponent<Animator>();

        // Uložení původních materiálů
        _originalMaterials.Clear();
        if (_playerRenderers != null)
        {
            foreach (var renderer in _playerRenderers)
            {
                if (renderer != null)
                {
                    _originalMaterials[renderer] = (Material[])renderer.materials.Clone();
                }
            }
        }

        // Na začátku vše vypneme
        if (_dodgeTrails != null)
        {
            foreach (var trail in _dodgeTrails)
            {
                if (trail != null) trail.emitting = false;
            }
        }

        if (_sprintParticles != null) _sprintParticles.Stop();

        // Vypneme i levitaci při startu
        if (_bodyLevitationVFX != null) _bodyLevitationVFX.SetActive(false);
        if (_groundLevitationVFX != null) _groundLevitationVFX.SetActive(false);
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

    public override void OnNetworkSpawn()
    {
        try
        {
            if (_attributes != null)
            {
                _attributes.IsInvulnerable.OnValueChanged += OnInvulnerabilityChanged;
                OnInvulnerabilityChanged(false, _attributes.IsInvulnerable.Value);
                _attributes.CurrentHealth.OnValueChanged += OnHealthChanged;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CRITICAL SPAWN ERROR] Chyba v {name}: {e.Message}\n{e.StackTrace}");
        }

    }

    public override void OnNetworkDespawn()
    {
        if (_attributes != null)
        {
            _attributes.IsInvulnerable.OnValueChanged -= OnInvulnerabilityChanged;
            _attributes.CurrentHealth.OnValueChanged -= OnHealthChanged;
        }

        RestoreOriginalMaterials();
    }

    private void OnHealthChanged(int previousValue, int newValue)
    {
        if (newValue < previousValue && !_attributes.IsInvulnerable.Value)
        {
            TriggerDamageFlash();
        }
    }

    private void TriggerDamageFlash()
    {
        if (_damageMaterial == null) return;

        if (_damageFlashRoutine != null) StopCoroutine(_damageFlashRoutine);
        _damageFlashRoutine = StartCoroutine(DamageFlashRoutine());
    }

    private IEnumerator DamageFlashRoutine()
    {
        ApplyMaterialOverride(_damageMaterial);
        yield return new WaitForSeconds(_damageFlashDuration);

        if (!_attributes.IsInvulnerable.Value)
        {
            RestoreOriginalMaterials();
        }
        _damageFlashRoutine = null;
    }

    private void OnInvulnerabilityChanged(bool previousValue, bool newValue)
    {
        if (_playerRenderers == null || _playerRenderers.Length == 0 || _invulnerableMaterial == null) return;

        if (newValue)
        {
            foreach (var renderer in _playerRenderers)
            {
                if (renderer == null) continue;
                int materialCount = renderer.materials.Length;
                Material[] invulnerableMaterials = new Material[materialCount];
                for (int i = 0; i < materialCount; i++) invulnerableMaterials[i] = _invulnerableMaterial;
                renderer.materials = invulnerableMaterials;
            }
        }
        else
        {
            RestoreOriginalMaterials();
        }
    }

    private void ApplyMaterialOverride(Material mat)
    {
        foreach (var renderer in _playerRenderers)
        {
            if (renderer == null) continue;
            int count = renderer.materials.Length;
            Material[] newMats = new Material[count];
            for (int i = 0; i < count; i++) newMats[i] = mat;
            renderer.materials = newMats;
        }
    }

    private void RestoreOriginalMaterials()
    {
        foreach (var renderer in _playerRenderers)
        {
            if (renderer != null && _originalMaterials.TryGetValue(renderer, out Material[] originalMats))
            {
                renderer.materials = originalMats;
            }
        }
    }


    private void Update()
    {
        if (_animator == null || _sprintParticles == null) return;

        bool isSprinting = _animator.GetBool("IsSprinting");
        bool isGrounded = _animator.GetBool("IsGrounded");

        if (isSprinting && isGrounded)
        {
            if (!_sprintParticles.isEmitting) _sprintParticles.Play();
        }
        else
        {
            if (_sprintParticles.isEmitting) _sprintParticles.Stop();
        }

        UpdateLevitationVisuals();
    }

    private void UpdateLevitationVisuals()
    {
        // Pokud nelevitujeme nebo nemáme nastavený ground efekt, nic neděláme
        if (!_isLevitating || _groundLevitationVFX == null) return;

        // Vystřelíme paprsek z pozice hráče (trochu z výšky) směrem dolů
        Vector3 rayOrigin = transform.position + Vector3.up * 1.5f;
        
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 5f, _groundLayerMask))
        {
            // Našli jsme zem -> umístíme efekt na bod dopadu (hit.point)
            _groundLevitationVFX.transform.position = hit.point;
            
            // Volitelné: Zarovnání rotace (aby se kruh netočil s hráčem, ale zůstal "světový")
            // Nebo aby se točil podle hráče (Y osa), ale ležel na zemi.
            // Zde nastavuji, aby sledoval rotaci hráče kolem osy Y:
            _groundLevitationVFX.transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
        }
        else
        {
            // Pokud jsme nenašli zem (hráč je moc vysoko nebo nad dírou), 
            // dáme efekt prostě pod hráče o kus níž (fallback)
            _groundLevitationVFX.transform.position = transform.position + Vector3.down * 1.5f;
        }
    }

    // --- RPCs ---

    [ServerRpc]
    public void ToggleVFXServerRpc(VFX_Type type, bool state)
    {
        ToggleVFXClientRpc(type, state);
    }

    [ClientRpc]
    private void ToggleVFXClientRpc(VFX_Type type, bool state)
    {
        if (type == VFX_Type.DodgeTrail)
        {
            if (_dodgeTrails != null)
            {
                foreach (var trail in _dodgeTrails)
                {
                    if (trail != null) trail.emitting = state;
                }
            }
        }
    }

    [ServerRpc]
    public void SpawnVFXServerRpc(VFX_Type type)
    {
        SpawnVFXClientRpc(type);
    }

    [ClientRpc]
    private void SpawnVFXClientRpc(VFX_Type type)
    {
        if (type == VFX_Type.LandingDust)
        {
            if (_landingParticlesPrefab != null)
            {
                Instantiate(_landingParticlesPrefab, transform.position, Quaternion.identity);
            }
        }
    }

    // --- NOVÉ: LEVITATION RPC ---
    // Voláno z UpgradeShopUI.cs

    [ServerRpc]
    public void ToggleLevitationVFXServerRpc(bool active)
    {
        // Pošleme příkaz všem klientům
        ToggleLevitationVFXClientRpc(active);
    }

    [ClientRpc]
    private void ToggleLevitationVFXClientRpc(bool active)
    {
        _isLevitating = active;

        // 1. Tělový efekt (Shine) - jen zapneme/vypneme
        if (_bodyLevitationVFX != null)
        {
            _bodyLevitationVFX.SetActive(active);
        }

        // 2. Zemní efekt (Kruh) - zapneme/vypneme a resetujeme pozici
        if (_groundLevitationVFX != null)
        {
            _groundLevitationVFX.SetActive(active);
            if (active)
            {
                // Okamžitý update pozice při zapnutí, aby nebliknul na špatném místě
                UpdateLevitationVisuals();
            }
        }
    }
}