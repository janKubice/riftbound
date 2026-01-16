using UnityEngine;

[ExecuteAlways] // Funguje i v editoru bez Play mode!
public class SkyboxController : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private Material _skyboxMaterial;
    [SerializeField] private float _dayCycleSpeed = 1.0f; // Rychlost otáčení slunce
    [SerializeField] private bool _autoRotate = true;

    private void Update()
    {
        if (_skyboxMaterial == null) return;

        // 1. Automatické otáčení slunce (Den a Noc)
        if (Application.isPlaying && _autoRotate)
        {
            transform.Rotate(Vector3.right, _dayCycleSpeed * Time.deltaTime);
        }

        // 2. Posílání směru světla do Shaderu
        // Shader potřebuje vědět, kde je slunce, aby tam namaloval to kolečko
        // -transform.forward je směr, odkud světlo svítí
        Shader.SetGlobalVector("_SunDirection", -transform.forward);
        
        // Pokud chceš měnit barvy podle denní doby, tady bys dělal:
        // if (transform.rotation.x > ...) _skyboxMaterial.SetColor("_TopColor", nightColor);
    }
}