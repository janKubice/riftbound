using UnityEngine;

public static class CombatUtils
{
    /// <summary>
    /// Rozhodne, zda má projektil ignorovat kolizi s daným objektem.
    /// </summary>
    public static bool ShouldIgnore(GameObject other)
    {
        // 1. Ignorovat jiné projektily podle Tagu
        if (other.CompareTag("Projectile")) return true;

        // 2. Ignorovat hráče (vypnuté PvP)
        if (other.CompareTag("Player")) return true;

        // 3. Ignorovat jiné projektily podle komponenty
        if (other.TryGetComponent(out SmartProjectile _)) return true;

        // Pokud ani jedno neplatí, objekt neignorujeme
        return false;
    }
}