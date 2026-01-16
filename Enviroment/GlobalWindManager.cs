using UnityEngine;

[ExecuteAlways]
public class GlobalWindManager : MonoBehaviour
{
    [Header("Nastavení Větru")]
    public float WindSpeed = 1.0f;
    public float WindStrength = 0.5f;
    public Vector3 WindDirection = new Vector3(1, 0, 1);

    private void Update()
    {
        // Vypočítáme vektor větru, který se mění v čase (aby to nebylo statické)
        float time = Application.isPlaying ? Time.time : 0f;
        
        // Simusoida pro poryvy větru
        float gust = Mathf.Sin(time * WindSpeed) * 0.2f + 1.0f; 
        
        Vector4 windParams = new Vector4(
            WindDirection.x, 
            WindDirection.y, 
            WindDirection.z, 
            WindStrength * gust
        );

        // Nastavíme globální proměnnou, kterou čtou všechny shadery stromů/trávy
        Shader.SetGlobalVector("_GlobalWind", windParams);
    }
}