using UnityEngine;
using Unity.Netcode;

public static class NetSafe
{
    public static void Destroy(GameObject obj)
    {
        if (obj == null) return;
        
        if (obj.name.Contains("Player") || obj.name.Contains("Clone"))
        {
            UnityEngine.Debug.LogError($"[NETSAFE TRACE] Někdo chce zničit {obj.name}!");
            UnityEngine.Debug.LogError($"[NETSAFE TRACE] Volající: {System.Environment.StackTrace}");
        }

        // Zkusíme najít NetworkObject
        if (obj.TryGetComponent<NetworkObject>(out var netObj))
        {
            // JSME NA SERVERU?
            if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
            {
                if (netObj.IsSpawned)
                {
                    netObj.Despawn();
                }
                else
                {
                    // Na serveru můžeme nespaunuté objekty mazat
                    Object.Destroy(obj);
                }
                return;
            }

            // JSME NA KLIENTOVI?
            // Tady je ta změna: Na klientovi NESMÍME smazat NetworkObject,
            // i když IsSpawned == false. Protože on se možná právě rodí!

            // Místo smazání ho jen vypneme. Unity/Netcode se postará o zbytek.
            obj.SetActive(false);

            // Volitelně: Můžeme zkusit naplánovat smazání, pokud je to opravdu "odpad",
            // ale bezpečnější je nechat ho jen vypnutý.
            return;
        }

        // Objekt nemá NetworkObject, je to bezpečné smazat (částice, UI...)
        Object.Destroy(obj);
    }
}

public static class GameObjectExtensions
{
    /// <summary>
    /// Extension metoda pro bezpečné odstranění objektu v prostředí Unity Netcode.
    /// </summary>
    public static void NetDestroy(this GameObject obj)
    {
        NetSafe.Destroy(obj);
    }
}