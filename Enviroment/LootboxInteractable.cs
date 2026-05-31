using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class LootItem
{
    public string ItemName;
    public GameObject Prefab;
    public float Weight = 10f;

    [Header("Efekty Rarity")]
    public GameObject BurstEffectPrefab;
    public AudioClip OpenSound;
}

public class LootboxInteractable : NetworkBehaviour, IInteractable 
{
    [Header("Reference k Modelu")]
    [SerializeField] private Transform _lidTransform; 
    [SerializeField] private Transform _chestBody;    
    [SerializeField] private Transform _itemSpawnPoint; 

    [Header("Systém Odměn (Chování truhly)")]
    [Tooltip("Vyhodí zbraň/předmět do světa")]
    [SerializeField] private bool _spawnPhysicalItem = true;
    [Tooltip("Spustí UI výběr upgradu pro hráče, který truhlu otevřel")]
    [SerializeField] private bool _offerUpgradeChoice = true;
    [SerializeField] private string _upgradeRewardContext = "Lootbox Reward";

    [SerializeField] private List<LootItem> _lootPool = new List<LootItem>();
    [SerializeField] private float _popOutForce = 7f; 

    [Header("Společné Efekty (Před otevřením)")]
    [SerializeField] private GameObject _buildUpEffectPrefab; 
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _rumbleClip;
    [Tooltip("Zvuk při otevření, pokud truhla nedává fyzický loot (např. jen upgrade)")]
    [SerializeField] private AudioClip _fallbackOpenSound;

    [Header("Nastavení Animace")]
    [SerializeField] private float _shakeDuration = 2.0f;
    [SerializeField] private float _shakeIntensity = 0.05f;
    [SerializeField] private Vector3 _openRotation = new Vector3(-110f, 0, 0);

    [Header("Interakce (HUD)")]
    [SerializeField] private string _interactionPrompt = "E - Open Lootbox";

    [Header("Audio (Volitelné)")]
    [SerializeField] private NetworkedAudioSource _networkedAudio;
    [SerializeField] private int _readSoundIndex = 0;

    private bool _isOpened = false;

    public string InteractionPrompt => _isOpened ? "" : _interactionPrompt;

    public void Interact(NetworkObject interactor)
    {
        if (_isOpened) return;
        
        if (_networkedAudio != null)
        {
            _networkedAudio.PlayOneShotNetworked(_readSoundIndex);
        }
        
        // Předáváme referenci na hráče, který akci vyvolal
        TriggerLootboxServerRpc(new NetworkObjectReference(interactor));
    }

    private int GetRandomLootIndex()
    {
        if (_lootPool == null || _lootPool.Count == 0) return -1;

        float totalWeight = 0;
        foreach (var item in _lootPool)
        {
            totalWeight += item.Weight;
        }

        float randomValue = Random.Range(0, totalWeight);
        float currentSum = 0;

        for (int i = 0; i < _lootPool.Count; i++)
        {
            currentSum += _lootPool[i].Weight;
            if (randomValue <= currentSum)
            {
                return i;
            }
        }
        return 0; 
    }

    [ServerRpc(RequireOwnership = false)]
    private void TriggerLootboxServerRpc(NetworkObjectReference interactorRef)
    {
        if (_isOpened) return;
        _isOpened = true; 

        int selectedLootIndex = -1;
        
        if (_spawnPhysicalItem)
        {
            selectedLootIndex = GetRandomLootIndex();
            if (selectedLootIndex == -1) 
            {
                Debug.LogWarning("[Lootbox] Má zapnutý fyzický drop, ale chybí LootPool!");
            }
        }

        PlayChestShowClientRpc(selectedLootIndex);

        StartCoroutine(ServerRewardRoutine(selectedLootIndex, interactorRef));
    }

    [ClientRpc]
    private void PlayChestShowClientRpc(int lootIndex)
    {
        StartCoroutine(ProceduralAnimationRoutine(lootIndex));
    }

    private IEnumerator ProceduralAnimationRoutine(int lootIndex)
    {
        // 1. FÁZE: Budování napětí
        if (_audioSource && _rumbleClip) _audioSource.PlayOneShot(_rumbleClip);
        
        GameObject buildUpInstance = null;
        if (_buildUpEffectPrefab)
        {
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

        // 2. FÁZE: Vyvrcholení (Otevření a efekty)
        if (buildUpInstance) Destroy(buildUpInstance);

        // Bezpečnostní kontrola pro případ, že truhla nedává fyzický loot (lootIndex == -1)
        if (lootIndex >= 0 && lootIndex < _lootPool.Count)
        {
            LootItem selectedLoot = _lootPool[lootIndex];
            
            if (_audioSource && selectedLoot.OpenSound) 
                _audioSource.PlayOneShot(selectedLoot.OpenSound);

            if (selectedLoot.BurstEffectPrefab)
                Instantiate(selectedLoot.BurstEffectPrefab, _itemSpawnPoint.position, Quaternion.identity);
        }
        else
        {
            // Fallback efekty, pokud truhla dává jen UI upgrade
            if (_audioSource && _fallbackOpenSound)
                _audioSource.PlayOneShot(_fallbackOpenSound);
        }

        // Animace víka
        elapsed = 0f;
        float openDuration = 0.5f; 
        Vector3 startEuler = _lidTransform.localEulerAngles;
        Vector3 targetEuler = startEuler + Quaternion.Euler(_openRotation).eulerAngles;

        while (elapsed < openDuration) 
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Sin((elapsed / openDuration) * Mathf.PI * 0.5f); 
            _lidTransform.localEulerAngles = Vector3.Lerp(startEuler, targetEuler, t);
            yield return null;
        }
    }

    // --- Zpracování Odměn (Běží jen na Serveru) ---
    private IEnumerator ServerRewardRoutine(int lootIndex, NetworkObjectReference interactorRef)
    {
        yield return new WaitForSeconds(_shakeDuration);

        // 1. Fyzický drop zbraně
        if (_spawnPhysicalItem && lootIndex >= 0 && lootIndex < _lootPool.Count)
        {
            LootItem selectedLoot = _lootPool[lootIndex];
            GameObject loot = Instantiate(selectedLoot.Prefab, _itemSpawnPoint.position, Quaternion.identity);
            
            if (loot.TryGetComponent(out NetworkObject netObj))
            {
                netObj.Spawn();
            }

            if (loot.TryGetComponent(out Rigidbody rb))
            {
                Vector3 forceDirection = (Vector3.up + transform.forward * 0.5f).normalized;
                rb.AddForce(forceDirection * _popOutForce, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * 10f, ForceMode.Impulse);
            }
        }

        // 2. Volba upgradu přes RewardChoiceManager
        if (_offerUpgradeChoice)
        {
            // Rozbalení reference na interagujícího hráče
            if (interactorRef.TryGet(out NetworkObject interactor))
            {
                if (interactor.TryGetComponent(out RewardChoiceManager rewardChoiceManager))
                {
                    rewardChoiceManager.OfferRewardChoicesServer(_upgradeRewardContext);
                }
                else
                {
                    Debug.LogWarning($"[Lootbox] Hráč {interactor.name} nemá komponentu RewardChoiceManager.");
                }
            }
            else
            {
                Debug.LogWarning("[Lootbox] Nepodařilo se dohledat NetworkObject interagujícího hráče.");
            }
        }
    }
}