using UnityEngine;

// Připni na Kameru
public class LayerCulling : MonoBehaviour
{
    void Start()
    {
        float[] distances = new float[32];
        // Vrstva 0 (Default) se renderuje nekonečně (0 = default)
        
        // Předpokládejme, že "SmallObjects" je vrstva č. 6
        distances[6] = 50f; // Objekty v této vrstvě zmizí po 50 metrech

        Camera.main.layerCullDistances = distances;
        Camera.main.layerCullSpherical = true; // Schová objekty i za zády plynuleji
    }
}
