using UnityEngine;
using System.Collections;
using Unity.Netcode; // Předpokládám Netcode for GameObjects

public class VoidTrap : MonoBehaviour
{
    [Header("Death Settings")]
    [SerializeField] private float _suckDuration = 1.0f; // Jak dlouho trvá animace smrti
    [SerializeField] private float _suckForce = 5.0f;    // Jak rychle ho to táhne ke středu
    [SerializeField] private AnimationCurve _scaleCurve; // Křivka zmenšování (1 -> 0)

    private void OnTriggerEnter(Collider other)
    {
        // Zajímá nás jen lokální hráč (pro vizuální efekt)
        // Serverová autorita pro smrt se řeší v Health systému, ale efekt spouštíme lokálně
        if (other.TryGetComponent<NetworkObject>(out NetworkObject netObj))
        {
            if (!netObj.IsOwner) return; // Efekt řeší každý klient pro sebe

            // Zkontrolujeme, jestli je to hráč
            if (other.CompareTag("Player"))
            {
                StartCoroutine(SuckAndKillSequence(other.transform));
            }
        }
    }

    private IEnumerator SuckAndKillSequence(Transform playerTransform)
    {
        // 1. Získání referencí a vypnutí ovládání
        var controller = playerTransform.GetComponent<PlayerController>();
        var rb = playerTransform.GetComponent<Rigidbody>();
        var collider = playerTransform.GetComponent<Collider>();

        // Vypneme hráče, aby nemohl utéct
        if (controller != null) controller.enabled = false;
        if (rb != null) rb.isKinematic = true; // Aby nepadal fyzikou
        if (collider != null) collider.enabled = false; // Aby do ničeho nenarazil cestou

        // 2. Příprava proměnných
        Vector3 startPos = playerTransform.position;
        Vector3 targetPos = transform.position; // Střed Voidu
                                                // Můžeme cíl posunout kousek dolů, aby to vypadalo, že padá "do země"
        targetPos.y -= 0.5f;

        Vector3 startScale = playerTransform.localScale;
        float timer = 0f;

        // 3. Hlavní smyčka animace
        while (timer < _suckDuration)
        {
            timer += Time.deltaTime;
            float t = timer / _suckDuration; // Hodnota od 0 do 1

            // MATEMATIKA POHYBU:
            // Použijeme t * t * t (kubickou křivku).
            // To znamená: začátek je velmi pomalý, ale konec je extrémně rychlý "snap".
            float curveT = t * t * t;

            // A. Pohyb ke středu (Lerp s křivkou)
            playerTransform.position = Vector3.Lerp(startPos, targetPos, curveT);

            // B. Rotace (Spirála)
            // Čím blíže konci, tím rychleji se točí (720 stupňů za sekundu * t)
            playerTransform.Rotate(Vector3.up, 1000f * t * Time.deltaTime);

            // C. Spaghettification (Vizuální deformace)
            // X a Z se zmenšují k nule (hubne)
            // Y se natahuje (prodlužuje)
            float shrink = 1f - curveT;      // Jde k 0
            float stretch = 1f + (curveT * 3f); // Jde k 4 (natáhne se 4x)

            // Na úplném konci to scale musí jít do nuly všechno
            if (t > 0.9f) stretch *= (1f - t) * 10f; // Rychlé smrsknutí na konci

            playerTransform.localScale = new Vector3(
                startScale.x * shrink,
                startScale.y * stretch * shrink,
                startScale.z * shrink
            );

            yield return null;
        }

        // 4. Reset stavu (pro respawn/pooling)
        playerTransform.localScale = startScale;
        if (controller != null) controller.enabled = true;
        if (rb != null) rb.isKinematic = false;
        if (collider != null) collider.enabled = true;

        // 5. APLIKACE SMRTI
        // Podle tvých souborů používáš PlayerAttributes nebo EnemyHealth
        // Zde pro hráče:
        var playerAttributes = playerTransform.GetComponent<PlayerAttributes>();
        if (playerAttributes != null)
        {
            // Předpokládám metodu TakeDamage, dáme dmg větší než max health
            // Pokud PlayerAttributes nemá TakeDamage, použij to, co používáš pro smrt
            // Např: playerAttributes.Health.Value = 0; nebo podobně

            // Zde je obecný damage call (uprav podle toho, jak zabíjíš hráče):
            // playerAttributes.TakeDamage(99999f); 

            Debug.Log("VOID: Hráč byl pohlcen temnotou.");
        }

        // Pokud používáš Netcode a chceš ho despawnout/respawnout serverově:
        // NetworkObject no = playerTransform.GetComponent<NetworkObject>();
        // if (no != null && IsServer) no.Despawn();
    }
}