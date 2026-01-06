using UnityEngine;

[ExecuteAlways] // Funguje i v editoru
public class ParticleFreezer : MonoBehaviour
{
    [Range(0f, 1f)] public float playbackProgress = 0.5f; // 0% až 100%
    public bool autoSimulateInEditor = true;

    private ParticleSystem ps;

    void OnEnable()
    {
        ps = GetComponent<ParticleSystem>();
    }

    void Update()
    {
        if (ps == null) return;

        // Pokud jsme v editoru nebo je hra pauznutá (timeScale 0)
        if (!Application.isPlaying || Time.timeScale == 0)
        {
            // Vypočítá čas na základě délky trvání (Duration) a procenta
            float targetTime = ps.main.duration * playbackProgress;

            // Manuálně vyrenderuje particle system v daném čase
            // true = i pro dceřiné systémy (děti)
            // true = restartovat a simulovat od nuly (aby to bylo přesné)
            ps.Simulate(targetTime, true, true); 
            ps.Pause();
        }
    }
}