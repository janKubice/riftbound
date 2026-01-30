using UnityEngine;

[CreateAssetMenu(menuName = "Shop/Shop Item")]
public class ShopItemData : ScriptableObject
{
    [Header("Info")]
    public string ItemName;
    [TextArea] public string Description;
    public Sprite Icon;
    
    [Header("Cena")]
    public int GoldCost;
    
    [Header("Efekt")]
    [Tooltip("Samotný efekt, který se přidá (např. FireDamage, SpawnLightning)")]
    public HitEffect EffectPayload;

    [Header("Typ")]
    [Tooltip("True = Přidá se na hráče (funguje na vše). False = Přidá se na aktuální zbraň.")]
    public bool IsGlobalUpgrade;
}