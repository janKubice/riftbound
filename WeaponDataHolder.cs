using UnityEngine;

// Tento skript je na PREFABU ZBRANĚ (který se instancuje do ruky)
// Slouží jen jako nosič dat pro WeaponManager
public class WeaponDataHolder : MonoBehaviour
{
    [Tooltip("Přetáhněte sem ScriptableObject s daty této zbraně")]
    public WeaponData Data;
    
    // Starý kód (pokud existoval)
    // public WeaponAnimationData AnimationData; // TOTO SMAŽTE NEBO NAHRAĎTE
}