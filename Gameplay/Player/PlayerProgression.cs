using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System;

public class PlayerProgression : NetworkBehaviour
{
    [Header("Resources")]
    // Všechny dostupné definice (musíš je sem přetáhnout v Inspectoru nebo načíst z Resources)
    [SerializeField] private List<StatUpgradeData> _availableUpgrades;

    // XP synchronizované pro klienta (aby viděl UI)
    public NetworkVariable<int> CurrentXP = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Uchováváme level každého upgradu. Klíč je index v listu _availableUpgrades.
    // Index 0 je MaxHealth, Index 1 je Speed... atd.
    private NetworkList<int> _upgradeLevels;

    // Cache pro rychlý přístup k vypočítaným hodnotám (pouze Server + Local Owner)
    private Dictionary<StatType, float> _cachedValues = new Dictionary<StatType, float>();

    // Eventy pro UI
    public event Action OnXPChanged;
    public event Action OnUpgradePurchased;

    private void Awake()
    {
        _upgradeLevels = new NetworkList<int>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Inicializace listu levelů nulami
            for (int i = 0; i < _availableUpgrades.Count; i++)
            {
                _upgradeLevels.Add(0);
            }
        }

        CurrentXP.OnValueChanged += (oldVal, newVal) => OnXPChanged?.Invoke();
        _upgradeLevels.OnListChanged += (changeEvent) =>
        {
            RecalculateStats();
            OnUpgradePurchased?.Invoke();
        };

        RecalculateStats(); // První kalkulace
    }

    // --- VEŘEJNÉ API (Volá se ze hry) ---

    public void AddXPServer(int amount)
    {
        if (!IsServer) return;
        CurrentXP.Value += amount;
    }

    // --- SYSTÉM NÁKUPU ---

    public void TryBuyUpgrade(int upgradeIndex)
    {
        if (IsOwner)
        {
            BuyUpgradeServerRpc(upgradeIndex);
        }
    }

    [ServerRpc]
    private void BuyUpgradeServerRpc(int index)
    {
        if (index < 0 || index >= _availableUpgrades.Count) return;

        StatUpgradeData data = _availableUpgrades[index];
        int currentLevel = _upgradeLevels[index];
        int cost = data.GetCost(currentLevel);

        if (CurrentXP.Value >= cost)
        {
            // Transakce
            CurrentXP.Value -= cost;
            _upgradeLevels[index] = currentLevel + 1; // Triggerne OnListChanged -> Recalculate

            // Okamžitá aplikace změn (pro HP, Staminu, Manu)
            ApplyInstantStatChanges(data.Type, data.ValuePerLevel);
        }
    }

    // --- KALKULACE A APLIKACE ---

    private void RecalculateStats()
    {
        _cachedValues.Clear();

        for (int i = 0; i < _availableUpgrades.Count; i++)
        {
            var data = _availableUpgrades[i];
            int level = (i < _upgradeLevels.Count) ? _upgradeLevels[i] : 0;
            float bonus = data.GetTotalBonus(level);

            if (_cachedValues.ContainsKey(data.Type))
                _cachedValues[data.Type] += bonus;
            else
                _cachedValues.Add(data.Type, bonus);
        }

        // Zde můžeme aplikovat pasivní změny (Size, Speed update...)
        ApplyContinuousStats();
    }

    // 1. Metoda pro trvalý stav (Vždy zajistí správné hodnoty)
    private void ApplyContinuousStats()
    {
        if (!IsServer) return;

        // --- VELIKOST ---
        float sizeBonus = GetStatBonus(StatType.CharacterSize);
        transform.localScale = Vector3.one * (1.0f + sizeBonus);

        // Musíme vypočítat celkové MaxHealth absolutně, ne přičítáním.
        // Předpokládáme, že PlayerAttributes má veřejnou "BaseMaxHealth" nebo ji známe (např. 100).
        var attr = GetComponent<PlayerAttributes>();
        if (attr != null)
        {
            float hpBonus = GetStatBonus(StatType.MaxHealth);
            float manaBonus = GetStatBonus(StatType.MaxMana);
            float staminaBonus = GetStatBonus(StatType.MaxStamina);

            // Nastavíme hodnotu "natvrdo" podle levelu
            // (Předpokládám, že _defaultMaxHealth v attr je přístupné nebo 100)
            int newMaxHP = 100 + (int)hpBonus;
            attr.MaxHealth.Value = newMaxHP;

            attr.MaxMana.Value = 50 + (int)manaBonus;
            attr.MaxStamina.Value = 100 + (int)staminaBonus;
        }
    }

    // 2. Metoda pro okamžitou reakci na nákup
    private void ApplyInstantStatChanges(StatType type, float amount)
    {
        var attr = GetComponent<PlayerAttributes>();
        if (attr == null) return;

        // Zde řešíme jen "vedlejší efekty" nákupu
        switch (type)
        {
            case StatType.MaxHealth:
                // Když si koupím víc života, chci tu novou část rovnou i doplnit do CurrentHealth
                attr.Heal((int)amount);
                break;

            case StatType.MaxStamina:
                // Doplníme staminu o tolik, kolik jsme právě získali navíc
                attr.CurrentStamina.Value += amount;
                break;

            case StatType.MaxMana:
                attr.CurrentMana.Value += amount;
                break;
        }
    }

    // --- GETTERY PRO OSTATNÍ SKRIPTY ---

    /// <summary>
    /// Vrací celkový bonus pro daný stat (např. +0.5 pro Speed).
    /// </summary>
    public float GetStatBonus(StatType type)
    {
        if (_cachedValues.TryGetValue(type, out float val))
            return val;
        return 0f;
    }

    /// <summary>
    /// Vrací multiplier (např. 1.0 + 0.5 = 1.5x damage).
    /// Vhodné pro Damage, Speed, atd.
    /// </summary>
    public float GetStatMultiplier(StatType type, float baseValue = 1.0f)
    {
        return baseValue + GetStatBonus(type);
    }

    // UI Helpers
    public int GetUpgradeLevel(int index) => (index < _upgradeLevels.Count) ? _upgradeLevels[index] : 0;
    public StatUpgradeData GetData(int index) => (index < _availableUpgrades.Count) ? _availableUpgrades[index] : null;
    public int GetUpgradesCount() => _availableUpgrades.Count;
}