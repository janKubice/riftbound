[System.Serializable]
public struct StatusEffectData
{
    public StatusEffectType Type;
    public float Duration;
    public float Potency; // Např. síla zpomalení (0.5 = 50%) nebo DMG za sekundu u Burn
    public float Chance;  // Šance na aplikaci (0-1)
}
