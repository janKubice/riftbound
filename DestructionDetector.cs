using UnityEngine;
using Unity.Netcode;

public class DestructionDetector : MonoBehaviour
{
    private void OnDisable()
    {
        // Zajímá nás jen situace na klientovi u síťového objektu
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
        {
            // Vypíšeme, kdo sakra vypnul/ničí tento objekt
            Debug.LogError($"[DETEKTIV] Objekt {gameObject.name} byl deaktivován/zničen!");
            Debug.LogError($"[DETEKTIV] STACK TRACE:\n{System.Environment.StackTrace}");
        }
    }
}