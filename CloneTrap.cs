using UnityEngine;
using System.Diagnostics; // Pro StackTrace

public class CloneTrap : MonoBehaviour
{
    private void Awake()
    {
        // Okamžitě zkontrolujeme jméno. 
        // V GameLifecycleManageru se Instantiate a přejmenování děje v jednom bloku, 
        // ale Awake běží UVNITŘ Instantiate, takže jméno bude ještě "(Clone)".
        // Počkáme jeden frame, abychom dali šanci vašemu manažerovi ho přejmenovat.
    }

    private void Start()
    {
        UnityEngine.Debug.LogError($"[TRAP] Hráč vytvořen! Objekt '{gameObject.name}'!");

        // Zkusíme zjistit, kdo ho vytvořil, i když zpětně je to těžké.
        // Ale důležitější je, KDE je.
        UnityEngine.Debug.LogError($"[TRAP] Rodič: {(transform.parent != null ? transform.parent.name : "ROOT")}");
        UnityEngine.Debug.LogError($"[TRAP] Pozice: {transform.position}");

        // Pokud má NetworkObject, zjistíme detaily
        if (TryGetComponent<Unity.Netcode.NetworkObject>(out var netObj))
        {
            UnityEngine.Debug.LogError($"[TRAP] NetworkID: {netObj.NetworkObjectId}, IsSpawned: {netObj.IsSpawned}, IsSceneObject: {netObj.IsSceneObject}");
        }
    }
}