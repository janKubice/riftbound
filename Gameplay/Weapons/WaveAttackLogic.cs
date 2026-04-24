using UnityEngine;
using Unity.Netcode;

[CreateAssetMenu(fileName = "WaveAttack", menuName = "Attacks/Wave Logic")]
public class WaveAttackLogic : AttackLogic
{
    [Header("Wave Settings")]
    [Tooltip("Prefab vlny (musí mít SmartProjectile a NetworkObject).")]
    public GameObject WavePrefab;
    
    [Tooltip("Maximální úhel náhodné rotace po ose Z (např. 90 pro mírné náklony, 360 pro rotaci všemi směry).")]
    public float MaxZRotation = 180f;

    [Tooltip("Cena many za útok.")]
    public int ManaCost = 0;

    public override void ExecuteAttack(NetworkObject attacker, WeaponManager weaponManager, Transform firePoint, WeaponStats stats, int projectileCountBonus = 0)
    {
        // 1. Validace
        if (WavePrefab == null || firePoint == null) return;
        if (!NetworkManager.Singleton.IsServer) return; // Projektily řídí Server

        // 2. Kontrola Many (pokud systém many používáš)
        if (ManaCost > 0 && attacker.TryGetComponent(out PlayerAttributes attr))
        {
            if (attr.CurrentMana.Value < ManaCost) return;
            // attr.ConsumeManaServerRpc(ManaCost); // Odkomentuj, pokud máš tuto metodu
        }

        // 3. Zjištění směru letu
        Vector3 startPos = firePoint.position;
        Vector3 baseDir = firePoint.forward;

        if (attacker.TryGetComponent(out PlayerAiming aiming))
        {
            baseDir = (aiming.CurrentAimPoint - startPos).normalized;
        }

        // 4. Multishot a Rozptyl
        int count = stats.ProjectileCount + projectileCountBonus;
        float actualSpread = stats.Spread > 0 ? stats.Spread : 0f;
        float startAngle = -actualSpread / 2f;
        float angleStep = count > 1 ? actualSpread / (count - 1) : 0f;

        // 5. Spawn vln
        for (int i = 0; i < count; i++)
        {
            // Úhel pro multishot (např. pokud střílí 3 vlny naráz)
            float currentAngle = count > 1 ? startAngle + (angleStep * i) : 0f;
            Quaternion spreadRotation = Quaternion.Euler(0, currentAngle, 0);
            Vector3 finalDir = spreadRotation * baseDir;

            // Náhodná rotace kolem osy letu (Z-roll)
            float randomRoll = Random.Range(-MaxZRotation / 2f, MaxZRotation / 2f);
            
            // Směrová rotace k cíli + přidání náhodného náklonu vlny
            Quaternion lookRot = Quaternion.LookRotation(finalDir);
            Quaternion finalRot = lookRot * Quaternion.Euler(randomRoll, 0, 0);

            // Spawn
            GameObject waveInstance = Instantiate(WavePrefab, startPos, finalRot);
            
            if (waveInstance.TryGetComponent(out NetworkObject netObj))
            {
                netObj.Spawn(true);
            }

            // Inicializace chování (využije tvoji vylepšenou penetrační logiku!)
            if (waveInstance.TryGetComponent(out SmartProjectile smartProj))
            {
                smartProj.Initialize(attacker, finalDir, stats);
            }
        }
    }
}