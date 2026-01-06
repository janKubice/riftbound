using UnityEngine;

public class CloudManager : MonoBehaviour
{
    [Header("Nastavení Mraků")]
    [SerializeField] private GameObject[] _cloudPrefabs;
    [SerializeField] private int _cloudCount = 20;

    [Header("Oblast (Spawn Area)")]
    [Tooltip("Velikost boxu, ve kterém mraky létají")]
    [SerializeField] private Vector3 _areaSize = new Vector3(200, 20, 200);

    [Header("Rychlost Větru")]
    [SerializeField] private float _minSpeed = 2.0f;
    [SerializeField] private float _maxSpeed = 8.0f;

    private void Start()
    {
        SpawnClouds();
    }

    private void SpawnClouds()
    {
        // Vypočítáme lokální hranice (od středu boxu doleva a doprava)
        float startX = -_areaSize.x / 2; // Levá strana (např. -100)
        float endX = _areaSize.x / 2;    // Pravá strana (např. +100)

        for (int i = 0; i < _cloudCount; i++)
        {
            GameObject prefab = _cloudPrefabs[Random.Range(0, _cloudPrefabs.Length)];

            // Instanciace
            GameObject cloud = Instantiate(prefab);
            
            // DŮLEŽITÉ: Nastavíme CloudManager jako rodiče
            cloud.transform.SetParent(this.transform);

            // Vygenerujeme náhodnou pozici v rámci boxu (LOKÁLNĚ)
            Vector3 randomLocalPos = new Vector3(
                Random.Range(startX, endX),          // Náhodně na ose X (aby nezačínaly všechny stejně)
                Random.Range(-_areaSize.y / 2, _areaSize.y / 2), // Výška
                Random.Range(-_areaSize.z / 2, _areaSize.z / 2)  // Hloubka
            );

            cloud.transform.localPosition = randomLocalPos;

            // Náhodná rotace a velikost
            cloud.transform.localRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
            float scale = Random.Range(0.8f, 1.5f);
            cloud.transform.localScale = Vector3.one * scale;

            // Inicializace pohybu
            CloudMovement movement = cloud.AddComponent<CloudMovement>();
            // Posíláme mu lokální souřadnice startu a konce
            movement.Initialize(Random.Range(_minSpeed, _maxSpeed), startX, endX);
        }
    }

    private void OnDrawGizmos()
    {
        // Nakreslíme box, abyste viděli, kde mraky budou
        Gizmos.color = new Color(0, 1, 1, 0.3f);
        // Matrix zajistí, že se Gizmo otáčí spolu s objektem
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, _areaSize);
    }
}