using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class CharacterEquipment : NetworkBehaviour
{
    [Header("Odkazy na kostru")]
    [SerializeField] private Transform _skeletonRoot;
    [SerializeField] private SkinnedMeshRenderer _baseBodyRenderer;

    [Header("Základní části těla (pro skrývání)")]
    [SerializeField] private GameObject _baseHead;
    [SerializeField] private GameObject _baseChest;
    // ... další (ruce, nohy)

    [Header("Databáze prefabů")]
    [Tooltip("Seznam VŠECH prefabů pro hlavu. Pořadí je klíčové.")]
    [SerializeField] private List<GameObject> _headPrefabs;
    [Tooltip("Seznam VŠECH prefabů pro tělo. Pořadí je klíčové.")]
    [SerializeField] private List<GameObject> _chestPrefabs;
    // ... další seznamy pro další sloty

    [Header("Sloty pro Vybavení")]
    private GameObject _equippedHeadItem;
    private GameObject _equippedChestItem;
    // ... další sloty ...

    // Změněno z <ulong> na <int>
    // -1 = nic (prázdný slot)
    private NetworkVariable<int> _headItemIndex = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> _chestItemIndex = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // --- Network Spawn / Despawn ---

    public override void OnNetworkSpawn()
    {
        try
        {
            _headItemIndex.OnValueChanged += OnHeadItemChanged;
            _chestItemIndex.OnValueChanged += OnChestItemChanged;

            // Aplikujeme aktuální stav při spawnu
            OnHeadItemChanged(-1, _headItemIndex.Value);
            OnChestItemChanged(-1, _chestItemIndex.Value);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CRITICAL SPAWN ERROR] Chyba v {name}: {e.Message}\n{e.StackTrace}");
        }

    }

    public override void OnNetworkDespawn()
    {
        _headItemIndex.OnValueChanged -= OnHeadItemChanged;
        _chestItemIndex.OnValueChanged -= OnChestItemChanged;
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

    // --- RPC ---

    [ServerRpc]
    // Změněno z ulong itemPrefabHash na int itemIndex
    public void EquipItemServerRpc(int itemIndex, EquipmentSlot slot)
    {
        // Server by zde měl ověřit, zda hráč item vlastní
        // A zda je index validní pro daný slot
        switch (slot)
        {
            case EquipmentSlot.Head:
                // Ověření indexu
                if (itemIndex < -1 || itemIndex >= _headPrefabs.Count)
                {
                    _headItemIndex.Value = -1; // Neplatný -> odzbrojit
                }
                else
                {
                    _headItemIndex.Value = itemIndex;
                }
                break;

            case EquipmentSlot.Chest:
                if (itemIndex < -1 || itemIndex >= _chestPrefabs.Count)
                {
                    _chestItemIndex.Value = -1;
                }
                else
                {
                    _chestItemIndex.Value = itemIndex;
                }
                break;
        }
    }

    // --- NetworkVariable Callbacks ---

    // Změněno z ulong na int
    private void OnHeadItemChanged(int oldIndex, int newIndex)
    {
        // Vyměníme model a předáme správný seznam prefabů
        _equippedHeadItem = EquipItemInternal(newIndex, _equippedHeadItem, _baseHead, _headPrefabs);
    }

    private void OnChestItemChanged(int oldIndex, int newIndex)
    {
        // Vyměníme model a předáme správný seznam prefabů
        _equippedChestItem = EquipItemInternal(newIndex, _equippedChestItem, _baseChest, _chestPrefabs);
    }

    // --- Interní logika (Opraveno pro Indexy) ---

    /// <summary>
    /// Interní logika pro výměnu modelu vybavení.
    /// </summary>
    /// <param name="newItemIndex">Index nového prefabu (-1 = nic)</param>
    /// <param name="oldItemInstance">Instance starého GO (pro zničení)</param>
    /// <param name="baseBodyPartToHide">Část těla, která se má skrýt (např. hlava)</param>
    /// <param name="prefabList">Seznam prefabů relevantní pro tento slot</param>
    private GameObject EquipItemInternal(int newItemIndex, GameObject oldItemInstance, GameObject baseBodyPartToHide, List<GameObject> prefabList)
    {
        // 1. Zničíme starý objekt
        if (oldItemInstance != null)
        {
            oldItemInstance.NetDestroy();
        }

        // 2. Pokud je index -1 (prázdný slot), ukážeme zpět základní část těla
        if (newItemIndex == -1)
        {
            if (baseBodyPartToHide != null) baseBodyPartToHide.SetActive(true);
            return null;
        }

        // 3. Ověříme platnost indexu
        if (newItemIndex < 0 || newItemIndex >= prefabList.Count)
        {
            Debug.LogError($"[Equipment] Neplatný index {newItemIndex} pro daný slot. Zobrazuji prázdný slot.");
            if (baseBodyPartToHide != null) baseBodyPartToHide.SetActive(true);
            return null;
        }

        // 4. Najdeme prefab pomocí indexu
        GameObject itemPrefab = prefabList[newItemIndex];

        if (itemPrefab == null)
        {
            Debug.LogError($"[Equipment] Prefab na indexu {newItemIndex} je null!");
            if (baseBodyPartToHide != null) baseBodyPartToHide.SetActive(true);
            return null;
        }

        // 5. Instancujeme nový prefab
        GameObject itemInstance = Instantiate(itemPrefab, transform);
        SkinnedMeshRenderer itemRenderer = itemInstance.GetComponentInChildren<SkinnedMeshRenderer>();

        if (itemRenderer == null)
        {
            Debug.LogError($"[Equipment] Prefab {itemPrefab.name} neobsahuje SkinnedMeshRenderer!");
            itemInstance.NetDestroy();
            if (baseBodyPartToHide != null) baseBodyPartToHide.SetActive(true);
            return null;
        }

        // 6. Přemapujeme kosti
        AttachMesh(itemRenderer, _skeletonRoot);

        // 7. Skryjeme základní část těla
        if (baseBodyPartToHide != null) baseBodyPartToHide.SetActive(false);

        return itemInstance;
    }

    /// <summary>
    /// Tato "magická" funkce vezme renderer vybavení a napojí
    /// ho na hlavní kostru postavy.
    /// </summary>
    private void AttachMesh(SkinnedMeshRenderer equipmentRenderer, Transform skeletonRoot)
    {
        // Najdeme všechny kosti v naší hlavní kostře
        var skeletonBones = skeletonRoot.GetComponentsInChildren<Transform>();

        // Vytvoříme nové pole kostí pro náš kus vybavení
        Transform[] newBones = new Transform[equipmentRenderer.bones.Length];

        // Pro každou kost v rendereru vybavení...
        for (int i = 0; i < equipmentRenderer.bones.Length; i++)
        {
            string boneName = equipmentRenderer.bones[i].name;
            bool found = false;

            // ...najdeme kost se stejným jménem v naší hlavní kostře
            foreach (Transform skeletonBone in skeletonBones)
            {
                if (skeletonBone.name == boneName)
                {
                    newBones[i] = skeletonBone;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                Debug.LogWarning($"[Equipment] Kost '{boneName}' (z {equipmentRenderer.name}) nebyla nalezena na hlavní kostře!");
            }
        }

        // 6. Přiřadíme nové kosti a kořenovou kost
        equipmentRenderer.bones = newBones;

        if (_baseBodyRenderer != null)
        {
            equipmentRenderer.rootBone = _baseBodyRenderer.rootBone;
        }
        else
        {
            Debug.LogError("[Equipment] _baseBodyRenderer není nastaven! Nelze nastavit rootBone.");
        }
    }
}
