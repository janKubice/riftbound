using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class WeaponManager : NetworkBehaviour
{
    [Header("Sockets")]
    [SerializeField] private Transform _rightHandSocket;
    [SerializeField] private Transform _firePoint;

    [Header("Data")]
    [Tooltip("Seznam VŠECH prefabů zbraní, které lze tímto manažerem vybavit. Pořadí je klíčové.")]
    [SerializeField] private List<GameObject> _weaponPrefabs;

    [Tooltip("Seznam prefabů zbraní, které se mají POLOŽIT NA ZEM (Pickup). Pořadí MUSÍ odpovídat seznamu _weaponPrefabs!")]
    [SerializeField] private List<GameObject> _pickupPrefabs;

    [Header("Výchozí Logika (Unarmed)")]
    [SerializeField] private WeaponAnimationData _unarmedAnimations;
    [SerializeField] private AttackLogic _unarmedAttackLogic; // Přetáhněte sem MeleeAttack SO

    // Aktuálně vybavený objekt
    private GameObject _currentWeaponInstance;
    private WeaponStats _currentRuntimeStats;
    private WeaponData _currentWeaponData; // Aktuální data (včetně logiky útoku)
    public WeaponData CurrentWeaponData => _currentWeaponData;
    private AnimatorOverrideController _animOverrideController;
    [SerializeField] private Animator _animator;
    private PlayerAudio _playerAudio;

    // Síťová proměnná, která říká, jaký prefab zbraně se má zobrazit (index do seznamu _weaponPrefabs)
    // -1 znamená beze zbraně (unarmed)
    private NetworkVariable<int> _currentWeaponIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private float _lastAttackTime = -999f;

    [SerializeField] private WeaponVisualsController _visuals;


    public override void OnNetworkSpawn()
    {
        try
        {
            _animOverrideController = new AnimatorOverrideController(_animator.runtimeAnimatorController);
            _animator.runtimeAnimatorController = _animOverrideController;
            _playerAudio = GetComponent<PlayerAudio>();
            _visuals = GetComponent<WeaponVisualsController>();

            _currentWeaponIndex.OnValueChanged += OnWeaponChanged;
            OnWeaponChanged(-1, _currentWeaponIndex.Value); // Vynutíme počáteční stav
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CRITICAL SPAWN ERROR] Chyba v {name}: {e.Message}\n{e.StackTrace}");
        }

    }

    public override void OnNetworkDespawn()
    {
        _currentWeaponIndex.OnValueChanged -= OnWeaponChanged;
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

    private void Update()
    {
        // Logiku zahození ovládá pouze lokální hráč
        if (!IsOwner) return;

        // Pokud máme klávesnici a stiskli jsme 'Q'
        if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame)
        {
            // Pokud nedržíme "unarmed" (-1)
            if (_currentWeaponIndex.Value != -1)
            {
                // Pošleme serveru žádost o zahození
                DropWeaponServerRpc();
            }
        }
    }

    // Tuto metodu může volat kdokoli (včetně jiných serverových skriptů)
    // Logika se provede POUZE na serveru.
    public void SetWeaponOnServer(int newWeaponIndex)
    {
        // 1. Ověření, že jsme na serveru (tohle už máte)
        if (!IsServer)
        {
            return;
        }

        // 2. Ověření indexu (validace)
        if (newWeaponIndex < -1 || newWeaponIndex >= _weaponPrefabs.Count)
        {
            Debug.LogWarning($"[WeaponManager] Neplatný index zbraně {newWeaponIndex}. Nastavuji 'Unarmed' (-1).");
            newWeaponIndex = -1; // Index, který skutečně nastavíme
        }

        // 3. Zkontrolujeme, zda vůbec děláme změnu
        int previousValue = _currentWeaponIndex.Value;
        if (previousValue == newWeaponIndex)
        {
            return; // Zbraň se nemění
        }

        // 4. Nastavíme novou hodnotu
        _currentWeaponIndex.Value = newWeaponIndex;

        // 5. OPRAVA: Manuální spuštění callbacku POUZE pro Hosta

        // if (IsHost) // <-- TOTO BYLO ŠPATNĚ

        // SPRÁVNÁ PODMÍNKA:
        // Musíme být Server (což víme) A ZÁROVEŇ vlastník tohoto objektu.
        if (IsOwner)
        {
            // Pokud IsServer == true (z kontroly nahoře) a IsOwner == true,
            // znamená to, že jsme Host a měníme SVŮJ VLASTNÍ stav.
            // Musíme zavolat callback ručně.
            OnWeaponChanged(previousValue, newWeaponIndex);
        }

        // Pokud IsServer == true a IsOwner == false:
        // Jsme Server, který mění stav pro vzdáleného Klienta.
        // Callback se NEVOLÁ ručně. Klient ho obdrží automaticky
        // přes NetworkVariable.OnValueChanged.
    }

    // Klient volá toto RPC (např. z inventáře)
    [ServerRpc]
    public void EquipWeaponServerRpc(int newWeaponIndex)
    {
        // RPC pouze předá volání serverové metodě
        SetWeaponOnServer(newWeaponIndex);
    }

    [ServerRpc]
    private void DropWeaponServerRpc()
    {
        // Tento kód běží POUZE na serveru

        int droppedIndex = _currentWeaponIndex.Value;

        // 1. Ověříme, že máme co zahodit a že máme odpovídající pickup prefab
        if (droppedIndex == -1 || droppedIndex >= _pickupPrefabs.Count || _pickupPrefabs[droppedIndex] == null)
        {
            // Nemáme co zahodit, nebo chybí prefab
            // Pro jistotu nastavíme unarmed, pokud by byl index neplatný
            _currentWeaponIndex.Value = -1;
            return;
        }

        // 2. Nastavíme hráče na "unarmed"
        _currentWeaponIndex.Value = -1;

        // 3. Najdeme pozici pro spawn (např. 1.5m před hráčem)
        // Používáme transform.position/forward serverové kopie hráče
        Vector3 spawnPos = transform.position + (transform.forward * 1.5f) + (Vector3.up * 0.5f);

        // 4. Získáme prefab "Pickup" objektu
        GameObject pickupPrefab = _pickupPrefabs[droppedIndex];

        // 5. Instancujeme a spawneme ho na síti
        GameObject pickupGO = Instantiate(pickupPrefab, spawnPos, transform.rotation);
        pickupGO.GetComponent<NetworkObject>().Spawn(true);
    }

    [ServerRpc]
    // Voláno, když hráč zmáčkne tlačítko (z PlayerController)
    public void RequestAttackServerRpc()
    {
        Debug.Log("[WeaponManager] pokus o útok.");
        if (_currentWeaponData != null && _currentWeaponData.AttackLogic != null)
        {
            // NOVÉ: Předáváme _currentRuntimeStats místo fixních hodnot
            _currentWeaponData.AttackLogic.ExecuteAttack(NetworkObject, this, GetFirePoint(), _currentRuntimeStats);
        }
    }

    // Tuto metodu volá AttackLogic na konci ExecuteAttack (MÍSTO TriggerAttackAnimationClientRpc)
    [ServerRpc]
    public void OnWeaponFiredServerRpc(float cooldown)
    {
        // Řekneme všem klientům: "Vystřelil jsem, cooldown byl X"
        OnWeaponFiredClientRpc(cooldown);
    }


    [ClientRpc]
    private void OnWeaponFiredClientRpc(float cooldown)
    {
        Debug.Log("[WeaponManager] Pokus o přehrání visuals.");
        // Předáme vizuálnímu kontroleru
        if (_visuals != null)
        {
            _visuals.OnAttackVisual(cooldown);
        }
    }

    public void SpawnMeleeImpact(Vector3 position)
    {
        // Pokud má aktuální zbraň definovaný efekt
        if (_currentWeaponData != null && _currentWeaponData.HitVFXPrefab != null)
        {
            // Řekneme všem klientům, ať zahrají efekt
            SpawnMeleeImpactClientRpc(position);
        }
    }

    [ClientRpc]
    private void SpawnMeleeImpactClientRpc(Vector3 position)
    {
        if (_currentWeaponData != null && _currentWeaponData.HitVFXPrefab != null)
        {
            Instantiate(_currentWeaponData.HitVFXPrefab, position, Quaternion.identity);
        }
    }

    /// <summary>
    /// Najde správný FirePoint.
    /// </summary>
    public Transform GetFirePoint()
    {
        // 1. Zkusíme najít FirePoint na aktuální zbrani (pro přesnou střelbu z hlavně)
        if (_currentWeaponInstance != null)
        {
            // Hledáme child objekt s názvem "FirePoint"
            Transform weaponFirePoint = _currentWeaponInstance.transform.Find("FirePoint");
            if (weaponFirePoint != null)
            {
                return weaponFirePoint;
            }
        }

        // 2. Pokud zbraň FirePoint nemá, vrátíme defaultní bod hráče
        return _firePoint;
    }

    // --- Vizuální logika (běží u všech) ---

    private void OnWeaponChanged(int oldIndex, int newIndex)
    {
        // 1. Bezpečný úklid staré zbraně
        if (_currentWeaponInstance != null)
        {
            // Zkontrolujeme, zda jde o síťový objekt a zda je právě aktivní v síti
            _currentWeaponInstance.NetDestroy();
            _currentWeaponInstance = null;
            _currentWeaponData = null;
        }

        // 2. Pokud je index -1 (Unarmed)
        if (newIndex == -1)
        {
            UpdateAnimations(_unarmedAnimations);
            return;
        }

        // 3. Validace indexu
        if (newIndex < 0 || newIndex >= _weaponPrefabs.Count)
        {
            Debug.LogError($"[WeaponManager] Neplatný index {newIndex}.");
            UpdateAnimations(_unarmedAnimations);
            return;
        }

        // 4. Instanciace nové zbraně
        GameObject weaponPrefab = _weaponPrefabs[newIndex];
        if (weaponPrefab != null)
        {
            _currentWeaponInstance = Instantiate(weaponPrefab, _rightHandSocket);
            _currentWeaponInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Získáme Data Holder z prefabu zbraně
            WeaponDataHolder holder = _currentWeaponInstance.GetComponent<WeaponDataHolder>();

            if (holder != null && holder.Data != null)
            {
                // A) Uložíme data
                _currentWeaponData = holder.Data;
                _currentRuntimeStats = _currentWeaponData.BaseStats;

                // C) Inicializujeme vizuální controller (IK, Trails...)
                if (_visuals != null)
                {
                    _visuals.InitializeWeapon(_currentWeaponData, _currentWeaponInstance);
                }
            }
            else
            {
                Debug.LogError($"[WeaponManager] Zbraň {_currentWeaponInstance.name} nemá WeaponDataHolder nebo Data!");
            }
        }
    }

    public void TryAttackLocalLoop()
    {
        Debug.Log("[WeaponManager] pokus o loop útok.");
        // Lokální kontrola cooldownu používá runtime staty
        float cd = _currentRuntimeStats.Cooldown > 0 ? _currentRuntimeStats.Cooldown : 0.1f;

        if (Time.time >= _lastAttackTime + cd)
        {
            _lastAttackTime = Time.time;
            RequestAttackServerRpc();
        }
    }

    // Metoda pro vylepšení (zavoláš ji, když hráč sebere powerup)
    public void UpgradeWeapon(float damageMult, float cooldownReduc)
    {
        // Toto by mělo běžet ideálně na serveru a synchronizovat se
        if (!IsServer) return;

        _currentRuntimeStats.Damage = Mathf.RoundToInt(_currentRuntimeStats.Damage * damageMult);
        _currentRuntimeStats.Cooldown *= cooldownReduc;
        // atd...
    }

    private void UpdateAnimations(WeaponAnimationData data)
    {
        // ... (vaše stávající logika pro UpdateAnimations)
        if (data == null)
        {
            Debug.LogWarning("Data animací nenalezena, používám unarmed.");
            data = _unarmedAnimations; // Záloha
        }

        _animOverrideController["Idle"] = data.Idle;
        _animOverrideController["Running_A"] = data.Walk;
        _animOverrideController["1H_Melee_Attack_Chop"] = data.Attack1;
    }
}