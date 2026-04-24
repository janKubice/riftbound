using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

// Datová struktura pro jeden možný drop z truhly
[System.Serializable]
public class LootItem
{
    [Tooltip("Název pro orientaci v editoru (např. 'Epický Meč')")]
    public string ItemName;
    
    [Tooltip("Prefab zbraně (WeaponPickup), který reálně vypadne")]
    public GameObject Prefab;
    
    [Tooltip("Váha pravděpodobnosti (např. 100 pro běžné, 10 pro vzácné, 1 pro epické)")]
    public float Weight = 10f;

    [Header("Efekty Rarity")]
    [Tooltip("GameObject s efektem výbuchu/konfet pro tento konkrétní předmět")]
    public GameObject BurstEffectPrefab;
    [Tooltip("Zvuk otevření truhly pro tento konkrétní předmět")]
    public AudioClip OpenSound;
}

public class LootboxInteractable : NetworkBehaviour, IInteractable // Využívá existující rozhraní
{
    [Header("Reference k Modelu")]
    [SerializeField] private Transform _lidTransform; 
    [SerializeField] private Transform _chestBody;    
    [SerializeField] private Transform _itemSpawnPoint; 

    [Header("Systém Odměn (Loot Pool)")]
    [SerializeField] private List<LootItem> _lootPool = new List<LootItem>();
    [SerializeField] private float _popOutForce = 7f; 

    [Header("Společné Efekty (Před otevřením)")]
    [Tooltip("GameObject efektu, který hraje během třesení (např. sání magie)")]
    [SerializeField] private GameObject _buildUpEffectPrefab; 
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _rumbleClip;

    [Header("Nastavení Animace")]
    [SerializeField] private float _shakeDuration = 2.0f;
    [SerializeField] private float _shakeIntensity = 0.05f;
    [SerializeField] private Vector3 _openRotation = new Vector3(-110f, 0, 0);

    [Header("Interakce (HUD)")]
    [SerializeField] private string _interactionPrompt = "E - Přečíst cedulku";

    [Header("Audio (Volitelné)")]
    [SerializeField] private NetworkedAudioSource _networkedAudio;
    [SerializeField] private int _readSoundIndex = 0;

    private bool _isOpened = false;

    // --- IInteractable ---
    public string InteractionPrompt => _isOpened ? "" : _interactionPrompt;

    public void Interact(NetworkObject interactor)
    {
        if (_isOpened) return;
        if (_networkedAudio != null)
        {
            _networkedAudio.PlayOneShotNetworked(_readSoundIndex);
        }
        TriggerLootboxServerRpc();
    }

    // --- Vážená Pravděpodobnost (Weighted Random) ---
    private int GetRandomLootIndex()
    {
        if (_lootPool == null || _lootPool.Count == 0) return -1;

        // 1. Sečteme všechny váhy dohromady (i když to bude např. 1+2+1+1+5 = 10)
        float totalWeight = 0;
        foreach (var item in _lootPool)
        {
            totalWeight += item.Weight;
        }

        // 2. Vybereme náhodné číslo od 0 do celkové váhy
        float randomValue = Random.Range(0, totalWeight);
        float currentSum = 0;

        // 3. Najdeme, do kterého "dílu koláče" číslo padlo
        for (int i = 0; i < _lootPool.Count; i++)
        {
            currentSum += _lootPool[i].Weight;
            if (randomValue <= currentSum)
            {
                return i; // Vracíme index vybraného předmětu
            }
        }
        return 0; // Pojistka
    }

    // --- Síťová Logika ---
    [ServerRpc(RequireOwnership = false)]
    private void TriggerLootboxServerRpc()
    {
        if (_isOpened) return;
        _isOpened = true; 

        // Server vylosuje odměnu
        int selectedLootIndex = GetRandomLootIndex();
        
        if (selectedLootIndex == -1) 
        {
            Debug.LogError("Truhla nemá nastavený žádný Loot!");
            return;
        }

        // Pošleme všem klientům, JAKÝ index se losoval, aby věděli, jaké efekty zahrát
        PlayChestShowClientRpc(selectedLootIndex);

        // Server paralelně odpočítává čas a pak spawne věc
        StartCoroutine(ServerSpawnItemRoutine(selectedLootIndex));
    }

    [ClientRpc]
    private void PlayChestShowClientRpc(int lootIndex)
    {
        StartCoroutine(ProceduralAnimationRoutine(lootIndex));
    }

    // --- Procedurální Animace (Běží u všech hráčů) ---
    private IEnumerator ProceduralAnimationRoutine(int lootIndex)
    {
        LootItem selectedLoot = _lootPool[lootIndex];

        // 1. FÁZE: Budování napětí
        if (_audioSource && _rumbleClip) _audioSource.PlayOneShot(_rumbleClip);
        
        GameObject buildUpInstance = null;
        if (_buildUpEffectPrefab)
        {
            // Spawneme efekt třesení na pozici těla truhly
            buildUpInstance = Instantiate(_buildUpEffectPrefab, _chestBody.position, Quaternion.identity, _chestBody);
        }

        Vector3 originalBodyPos = _chestBody.localPosition;
        float elapsed = 0f;

        while (elapsed < _shakeDuration)
        {
            elapsed += Time.deltaTime;
            float currentIntensity = Mathf.Lerp(_shakeIntensity * 0.2f, _shakeIntensity, elapsed / _shakeDuration);
            _chestBody.localPosition = originalBodyPos + Random.insideUnitSphere * currentIntensity;
            yield return null;
        }
        
        _chestBody.localPosition = originalBodyPos;

        // 2. FÁZE: Vyvrcholení (Otevření a specifické efekty)
        
        // Zničíme budovací efekt
        if (buildUpInstance) Destroy(buildUpInstance);

        // Přehrajeme zvuk podle rarity/vybraného předmětu
        if (_audioSource && selectedLoot.OpenSound) 
        {
            _audioSource.PlayOneShot(selectedLoot.OpenSound);
        }

        // Spawneme finální výbuch podle rarity/vybraného předmětu jako GameObject
        if (selectedLoot.BurstEffectPrefab)
        {
            Instantiate(selectedLoot.BurstEffectPrefab, _itemSpawnPoint.position, Quaternion.identity);
        }

        // Animace víka - RELATIVNÍ ROTACE
        elapsed = 0f;
        float openDuration = 0.5f; // Mírně zpomaleno (z 0.3) pro lepší efekt

        // 1. Uložíme si počáteční rotaci ve stupních
        Vector3 startEuler = _lidTransform.localEulerAngles;
        
        // 2. Vypočítáme cílovou rotaci PŘIČTENÍM našeho úhlu.
        // POZOR: Pokud se ti víko točí po jiné ose než X, změň to zde!
        // Např. pro Z: startEuler + new Vector3(0, 0, _openAngle);
        Vector3 targetEuler = startEuler + Quaternion.Euler(_openRotation).eulerAngles;

        while (elapsed < openDuration) 
        {
            elapsed += Time.deltaTime;
            // Křivka pro plynulé zpomalení na konci
            float t = Mathf.Sin((elapsed / openDuration) * Mathf.PI * 0.5f); 
            
            // Lerpujeme přímo jednotlivé stupně (Eulerovy úhly)
            _lidTransform.localEulerAngles = Vector3.Lerp(startEuler, targetEuler, t);
            
            yield return null;
        }
    }

    // --- Spawn a Vymrštění Zbraně (Běží jen na Serveru) ---
    private IEnumerator ServerSpawnItemRoutine(int lootIndex)
    {
        yield return new WaitForSeconds(_shakeDuration);

        LootItem selectedLoot = _lootPool[lootIndex];

        // 1. Spawn zbraně
        GameObject loot = Instantiate(selectedLoot.Prefab, _itemSpawnPoint.position, Quaternion.identity);
        NetworkObject netObj = loot.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
        }

        // 2. Fyzikální vymrštění
        Rigidbody rb = loot.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 forceDirection = (Vector3.up + transform.forward * 0.5f).normalized;
            rb.AddForce(forceDirection * _popOutForce, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 10f, ForceMode.Impulse);
        }
    }
}