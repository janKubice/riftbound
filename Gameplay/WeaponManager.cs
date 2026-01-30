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
    [SerializeField] private WeaponStats _currentRuntimeStats;
    public WeaponStats CurrentRuntimeStats => _currentRuntimeStats;
    private WeaponData _currentWeaponData; // Aktuální data (včetně logiky útoku)
    public WeaponData CurrentWeaponData => _currentWeaponData;
    private AnimatorOverrideController _animOverrideController;
    [SerializeField] private Animator _animator;
    private PlayerAudio _playerAudio;

    // Síťová proměnná, která říká, jaký prefab zbraně se má zobrazit (index do seznamu _weaponPrefabs)
    // -1 znamená beze zbraně (unarmed)
    public NetworkVariable<int> _currentWeaponIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> _isContinuousFiring = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private float _lastAttackTime = -999f;

    [SerializeField] private WeaponVisualsController _visuals;
    private PlayerAiming _aiming;

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

        if (IsServer)
        {
            // Pokud začínáme bez zbraně
            _currentWeaponIndex.Value = -1;
        }
        _aiming = GetComponent<PlayerAiming>();

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

    private void LateUpdate()
    {
        // 1. Ověříme, že držíme kontinuální zbraň (Laser)
        if (_currentWeaponData != null && _currentWeaponData.IsContinuous)
        {
            // 2. Ověříme síťovou proměnnou (zda se střílí)
            if (_isContinuousFiring.Value)
            {
                // 3. Vypočítáme, kam laser dopadá
                Vector3 endPos = CalculateLaserEndPoint();

                // 4. Řekneme vizuálu, ať se tam vykreslí
                _visuals.UpdateLaserVisual(true, endPos);
            }
            else
            {
                // Pokud se nestřílí, laser vypneme
                _visuals.UpdateLaserVisual(false, Vector3.zero);
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
    public void RequestAttackServerRpc()
    {
        if (_currentWeaponData != null && _currentWeaponData.AttackLogic != null)
        {
            // 1. Vytvoříme dočasnou kopii statistik pro tento výstřel
            WeaponStats attackStats = _currentRuntimeStats;

            // 2. Naplníme ji kombinovanými efekty (Zbraň + Global)
            attackStats.OnHitEffects = GetCombinedEffects();

            // 3. Předáme Logic
            _currentWeaponData.AttackLogic.ExecuteAttack(NetworkObject, this, GetFirePoint(), attackStats);
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

        Collider[] enviroHits = Physics.OverlapSphere(position, 2.0f); // Radius 2 metry

        foreach (var hit in enviroHits)
        {
            // Hledáme náš nový skript na stromech
            if (hit.TryGetComponent(out InteractiveFoliage foliage))
            {
                // Vypočítáme směr od hráče ke stromu
                Vector3 dir = (hit.transform.position - transform.position).normalized;
                foliage.OnHit(dir);
            }
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
        //Debug.Log("[WeaponManager] pokus o loop útok.");
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


    /// <summary>
    /// Helper metoda pro Shop. Zjistí data zbraně (Cenu, Ikonu) jen podle čísla v listu.
    /// </summary>
    public WeaponData GetWeaponDataByIndex(int index)
    {
        // 1. Kontrola rozsahu listu
        if (_weaponPrefabs == null)
        {
            Debug.LogError("[WeaponManager] CHYBA: List '_weaponPrefabs' není inicializován!");
            return null;
        }

        if (index < 0 || index >= _weaponPrefabs.Count)
        {
            Debug.LogError($"[WeaponManager] CHYBA: NPC chce zbraň ID {index}, ale v listu hráče je jen {_weaponPrefabs.Count} zbraní!");
            return null;
        }

        // 2. Kontrola prefabu
        GameObject prefab = _weaponPrefabs[index];
        if (prefab == null)
        {
            Debug.LogError($"[WeaponManager] CHYBA: Na indexu {index} je v listu 'None' (chybí prefab)!");
            return null;
        }

        // 3. Kontrola Holderu
        var holder = prefab.GetComponent<WeaponDataHolder>();
        if (holder == null)
        {
            Debug.LogError($"[WeaponManager] CHYBA: Prefab '{prefab.name}' nemá skript 'WeaponDataHolder'!");
            return null;
        }

        // 4. Kontrola Dat
        if (holder.Data == null)
        {
            Debug.LogError($"[WeaponManager] CHYBA: Prefab '{prefab.name}' má Holder, ale chybí v něm 'WeaponData' (ScriptableObject)!");
            return null;
        }

        return holder.Data;
    }

    public void SetContinuousFireState(bool isFiring)
    {
        if (!IsOwner) return;
        // Pošleme požadavek na server jen pokud se stav změnil
        if (_isContinuousFiring.Value != isFiring)
        {
            SetContinuousFireServerRpc(isFiring);
        }
    }

    [ServerRpc]
    public void SpawnLaserServerRpc(Vector3 startPoint, Vector3 endPoint)
    {
        // Server to rozešle všem klientům
        SpawnLaserClientRpc(startPoint, endPoint);
    }

    [ClientRpc]
    private void SpawnLaserClientRpc(Vector3 startPoint, Vector3 endPoint)
    {
        // 1. Získáme prefab pro vizuál (použijeme MuzzleFlashPrefab z WeaponData jako "Laser Prefab")
        if (_currentWeaponData != null && _currentWeaponData.MuzzleFlashPrefab != null)
        {
            // Instancujeme vizuál laseru
            GameObject laserInstance = Instantiate(_currentWeaponData.MuzzleFlashPrefab, Vector3.zero, Quaternion.identity);

            // 2. Nastavíme mu pozice
            if (laserInstance.TryGetComponent(out LaserBeamVFX laserScript))
            {
                laserScript.UpdateBeam(startPoint, endPoint);
            }
            else
            {
                // Fallback pro TrailRenderer (pokud trváš na Trailu, jen ho přesuneme a natáhneme)
                LineRenderer lr = laserInstance.GetComponent<LineRenderer>();
                if (lr != null)
                {
                    lr.SetPosition(0, startPoint);
                    lr.SetPosition(1, endPoint);
                }
            }
        }
    }

    [ServerRpc]
    private void SetContinuousFireServerRpc(bool isFiring)
    {
        _isContinuousFiring.Value = isFiring;
    }

    private Vector3 CalculateLaserEndPoint()
    {
        Transform startT = GetFirePoint();
        if (startT == null) return transform.position;

        Vector3 start = startT.position;
        Vector3 dir = startT.forward;

        // Zpřesnění směru podle kamery
        if (_aiming != null)
        {
            Vector3 target = _aiming.CurrentAimPoint;
            // Ošetření, aby dir nebyl zero vector
            Vector3 targetDir = (target - start).normalized;
            if (targetDir != Vector3.zero) dir = targetDir;
        }

        float range = _currentWeaponData.BaseStats.Range > 0 ? _currentWeaponData.BaseStats.Range : 50f;

        // OPRAVA: Použijeme RaycastAll a vyfiltrujeme sebe
        RaycastHit[] hits = Physics.RaycastAll(start, dir, range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            // Klíčová kontrola: Pokud je trefený objekt součástí mého hráče (stejný root), ignoruj ho
            if (hit.transform.root == transform.root) continue;

            return hit.point; // První cizí překážka
        }

        // Nic jsme netrefili -> laser do nekonečna
        return start + (dir * range);
    }

    [ServerRpc]
    public void SpawnChainLightningServerRpc(Vector3[] points)
    {
        SpawnChainLightningClientRpc(points);
    }

    [ClientRpc]
    private void SpawnChainLightningClientRpc(Vector3[] points)
    {
        // Použijeme MuzzleFlashPrefab jako prefab blesku
        if (_currentWeaponData != null && _currentWeaponData.MuzzleFlashPrefab != null)
        {
            GameObject lightningGO = Instantiate(_currentWeaponData.MuzzleFlashPrefab, Vector3.zero, Quaternion.identity);

            if (lightningGO.TryGetComponent(out ChainLightningVFX vfx))
            {
                vfx.DrawChain(points);
            }
            else
            {
                // Fallback pro obyčejný LineRenderer
                LineRenderer lr = lightningGO.GetComponent<LineRenderer>();
                if (lr != null)
                {
                    lr.positionCount = points.Length;
                    lr.SetPositions(points);
                    Destroy(lightningGO, 0.2f);
                }
            }
        }
    }

    /// <summary>
    /// Spojí lokální efekty zbraně a globální efekty hráče do jednoho seznamu.
    /// Volá se před každým útokem.
    /// </summary>
    public List<HitEffect> GetCombinedEffects()
    {
        List<HitEffect> combined = new List<HitEffect>();

        // 1. Přidat efekty zbraně
        if (_currentRuntimeStats.OnHitEffects != null)
        {
            combined.AddRange(_currentRuntimeStats.OnHitEffects);
        }

        // 2. Přidat globální efekty hráče
        var globalFX = GetComponent<PlayerGlobalEffects>();
        if (globalFX != null && globalFX.GlobalEffects != null)
        {
            combined.AddRange(globalFX.GlobalEffects);
        }

        return combined;
    }

    // --- RPC PRO OBCHOD (Server Authority) ---

    [ServerRpc]
    public void AddWeaponEffectServerRpc(int shopItemIndex, NetworkBehaviourReference shopRef)
    {
        // Získáme referenci na obchod, abychom vytáhli správný efekt
        if (shopRef.TryGet(out ShopInteractable shop))
        {
            ShopItemData item = shop.GetItemByIndex(shopItemIndex);
            if (item != null && !item.IsGlobalUpgrade)
            {
                if (_currentRuntimeStats.OnHitEffects == null)
                    _currentRuntimeStats.OnHitEffects = new List<HitEffect>();

                _currentRuntimeStats.OnHitEffects.Add(item.EffectPayload);
                Debug.Log($"[WeaponManager] Efekt {item.ItemName} přidán na zbraň.");
            }
        }
    }

    [ServerRpc]
    public void RemoveWeaponEffectServerRpc(int listIndex)
    {
        if (_currentRuntimeStats.OnHitEffects == null) return;
        if (listIndex >= 0 && listIndex < _currentRuntimeStats.OnHitEffects.Count)
        {
            _currentRuntimeStats.OnHitEffects.RemoveAt(listIndex);
        }
    }

    [ServerRpc]
    public void SwapWeaponEffectsServerRpc(int indexA, int indexB)
    {
        if (_currentRuntimeStats.OnHitEffects == null) return;
        int count = _currentRuntimeStats.OnHitEffects.Count;

        if (indexA >= 0 && indexA < count && indexB >= 0 && indexB < count)
        {
            // Prohození
            var temp = _currentRuntimeStats.OnHitEffects[indexA];
            _currentRuntimeStats.OnHitEffects[indexA] = _currentRuntimeStats.OnHitEffects[indexB];
            _currentRuntimeStats.OnHitEffects[indexB] = temp;
        }
    }

    public void AddRuntimeEffect(HitEffect effect)
    {
        if (!IsServer) return;
        if (_currentRuntimeStats.OnHitEffects == null) _currentRuntimeStats.OnHitEffects = new System.Collections.Generic.List<HitEffect>();

        _currentRuntimeStats.OnHitEffects.Add(effect);
    }

    // Prohodit efekty (pouze Server)
    public void SwapEffects(int indexA, int indexB)
    {
        if (!IsServer || _currentRuntimeStats.OnHitEffects == null) return;
        var list = _currentRuntimeStats.OnHitEffects;
        if (indexA >= 0 && indexA < list.Count && indexB >= 0 && indexB < list.Count)
        {
            var temp = list[indexA];
            list[indexA] = list[indexB];
            list[indexB] = temp;
        }
    }

    // Odstranit efekt (pouze Server)
    public void RemoveEffect(int index)
    {
        if (!IsServer || _currentRuntimeStats.OnHitEffects == null) return;
        if (index >= 0 && index < _currentRuntimeStats.OnHitEffects.Count)
        {
            _currentRuntimeStats.OnHitEffects.RemoveAt(index);
        }
    }
}