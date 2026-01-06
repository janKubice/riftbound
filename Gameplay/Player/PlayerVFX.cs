using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Collections; // Potřebujeme pro Dictionary

// Tento skript přidejte na prefab Hráče
public class PlayerVFX : NetworkBehaviour
{
    [Header("Invulnerability (Nesmrtelnost)")]
    [Tooltip("Všechny renderery postavy, u kterých změníme materiály.")]
    [SerializeField] private SkinnedMeshRenderer[] _playerRenderers; // ZMĚNA: Pole rendererů

    // [SerializeField] private Material _normalMaterial; // ZMĚNA: Už není potřeba, materiály si uložíme

    [Tooltip("Materiál, který se použije během nesmrtelnosti (nahradí VŠECHNY materiály na rendererech).")]
    [SerializeField] private Material _invulnerableMaterial;

    // ZMĚNA: Slovník pro uložení původních materiálů pro každý renderer
    // Klíč (Key) = Renderer, Hodnota (Value) = Pole jeho původních materiálů
    private Dictionary<SkinnedMeshRenderer, Material[]> _originalMaterials = new Dictionary<SkinnedMeshRenderer, Material[]>();


    [Header("Dodge (Úhyb)")]
    [Tooltip("Komponenta TrailRenderer (ideálně na pod-objektu hráče).")]
    [SerializeField] private TrailRenderer[] _dodgeTrails;

    [Header("Movement Particles (Částice)")]
    [Tooltip("Prefab částicového efektu, který se spawne při dopadu.")]
    [SerializeField] private GameObject _landingParticlesPrefab;
    [Tooltip("Částicový systém, který hraje při sprintu (musí být na hráči a nastaven na 'Looping').")]
    [SerializeField] private ParticleSystem _sprintParticles;

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

    // Pomocný enum pro RPC
    public enum VFX_Type { DodgeTrail, LandingDust }

    private void Awake()
    {
        // Automatické nalezení, pokud není nastaveno
        if (_attributes == null) _attributes = GetComponent<PlayerAttributes>();
        if (_animator == null) _animator = GetComponent<Animator>();

        // --- ZMĚNA: Uložení původních materiálů ---
        _originalMaterials.Clear();
        if (_playerRenderers != null)
        {
            foreach (var renderer in _playerRenderers)
            {
                if (renderer != null)
                {
                    // Uložíme si kopii pole VŠECH materiálů, které renderer používá
                    _originalMaterials[renderer] = (Material[])renderer.materials.Clone();
                }
            }
        }
        // --- Konec Změny ---

        // Na začátku vše vypneme
        if (_dodgeTrails != null) // ZMĚNA
        {
            foreach (var trail in _dodgeTrails) // ZMĚNA
            {
                if (trail != null) trail.emitting = false; // ZMĚNA
            }
        }

        if (_sprintParticles != null) _sprintParticles.Stop();
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
            // Přihlásíme se k odběru změny NetworkVariable.
            // Toto běží na VŠECH klientech.
            if (_attributes != null)
            {
                _attributes.IsInvulnerable.OnValueChanged += OnInvulnerabilityChanged;
                // Zavoláme hned, abychom nastavili správný materiál při spawnu
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
        // Vždy se odhlásíme z odběru
        if (_attributes != null)
        {
            _attributes.IsInvulnerable.OnValueChanged -= OnInvulnerabilityChanged;
            _attributes.CurrentHealth.OnValueChanged -= OnHealthChanged;
        }

        // ZMĚNA: Při despawnu pro jistotu vrátíme původní materiály
        // (Pokud by despawn nastal během nesmrtelnosti, poolovaný objekt by zůstal průhledný)
        RestoreOriginalMaterials();
    }

    private void OnHealthChanged(int previousValue, int newValue)
    {
        // Pokud zdraví kleslo (poškození) A zároveň nejsme zrovna nesmrtelní (aby se nepřepínal materiál)
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
        // 1. Nastavit červený materiál
        ApplyMaterialOverride(_damageMaterial);

        // 2. Čekat
        yield return new WaitForSeconds(_damageFlashDuration);

        // 3. Vrátit zpět (pokud mezitím nenastala nesmrtelnost)
        if (!_attributes.IsInvulnerable.Value)
        {
            RestoreOriginalMaterials();
        }
        _damageFlashRoutine = null;
    }

    /// <summary>
    /// Callback pro změnu nesmrtelnosti. Běží na všech klientech.
    /// </summary>
    private void OnInvulnerabilityChanged(bool previousValue, bool newValue)
    {
        // ZMĚNA: Kontrolujeme pole rendererů
        if (_playerRenderers == null || _playerRenderers.Length == 0 || _invulnerableMaterial == null) return;

        if (newValue)
        {
            // Projdeme všechny renderery
            foreach (var renderer in _playerRenderers)
            {
                if (renderer == null) continue;

                // Vytvoříme nové pole materiálů o stejné velikosti, jako mělo to původní
                int materialCount = renderer.materials.Length;
                Material[] invulnerableMaterials = new Material[materialCount];

                // Všechny sloty vyplníme JEDNÍM nesmrtelným materiálem
                for (int i = 0; i < materialCount; i++)
                {
                    invulnerableMaterials[i] = _invulnerableMaterial;
                }

                // Přiřadíme nové pole. Používáme .materials (množné číslo)
                renderer.materials = invulnerableMaterials;
            }
        }
        else
        {
            // Vrátíme původní materiály
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

    /// <summary>
    /// ZMĚNA: Nová pomocná metoda pro vrácení původních materiálů
    /// </summary>
    private void RestoreOriginalMaterials()
    {
        foreach (var renderer in _playerRenderers)
        {
            // Vrátíme původní pole materiálů uložené ve slovníku
            if (renderer != null && _originalMaterials.TryGetValue(renderer, out Material[] originalMats))
            {
                renderer.materials = originalMats;
            }
        }
    }


    private void Update()
    {
        // Sprint částice jsou řízeny lokálně na základě synchronizovaného Animatoru
        // (NetworkAnimator se stará o synchronizaci parametrů "IsSprinting" a "IsGrounded")
        if (_animator == null || _sprintParticles == null) return;

        bool isSprinting = _animator.GetBool("IsSprinting");
        bool isGrounded = _animator.GetBool("IsGrounded");

        if (isSprinting && isGrounded)
        {
            if (!_sprintParticles.isEmitting)
            {
                _sprintParticles.Play(); // Spustíme looping efekt
            }
        }
        else
        {
            if (_sprintParticles.isEmitting)
            {
                _sprintParticles.Stop(); // Zastavíme looping efekt
            }
        }
    }

    // --- Síťová logika pro efekty (Dodge a Dopad) ---

    /// <summary>
    /// Volá lokální hráč (Owner), aby zapnul/vypnul efekt (např. Trail)
    /// </summary>
    [ServerRpc]
    public void ToggleVFXServerRpc(VFX_Type type, bool state)
    {
        // Server přikáže všem klientům
        ToggleVFXClientRpc(type, state);
    }

    [ClientRpc]
    private void ToggleVFXClientRpc(VFX_Type type, bool state)
    {
        if (type == VFX_Type.DodgeTrail)
        {
            if (_dodgeTrails != null) // ZMĚNA
            {
                // Projdeme všechny traily v poli a nastavíme 'emitting'
                foreach (var trail in _dodgeTrails) // ZMĚNA
                {
                    if (trail != null)
                    {
                        trail.emitting = state;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Volá lokální hráč (Owner), aby spustil jednorázový efekt (např. dopad)
    /// </summary>
    [ServerRpc]
    public void SpawnVFXServerRpc(VFX_Type type)
    {
        // Server přikáže všem klientům
        SpawnVFXClientRpc(type);
    }

    [ClientRpc]
    private void SpawnVFXClientRpc(VFX_Type type)
    {
        if (type == VFX_Type.LandingDust)
        {
            if (_landingParticlesPrefab != null)
            {
                // Spawne prefab na pozici hráče (na jeho nohou)
                // Tento prefab by měl mít skript, který ho po přehrání sám zničí
                Instantiate(_landingParticlesPrefab, transform.position, Quaternion.identity);
            }
        }
    }
}