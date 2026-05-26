using UnityEngine;

// Tento skript je na PREFABU ZBRANĚ (který se instancuje do ruky)
// Slouží jen jako nosič dat pro WeaponManager
public class WeaponDataHolder : MonoBehaviour
{
    [Tooltip("Přetáhněte sem ScriptableObject s daty této zbraně")]
    public WeaponData Data;

    [Header("Grip Settings (Úchop)")]
    [Tooltip("Posun zbraně vůči ruce hráče")]
    public Vector3 PositionOffset = Vector3.zero;
    
    [Tooltip("Natočení zbraně v ruce hráče")]
    public Vector3 RotationOffset = Vector3.zero;

}