using UnityEngine;

public class CloudManager : MonoBehaviour
{
    private struct CloudData
    {
        public Transform Transform;
        public float Speed;
    }

    [Header("Prefabs & Count")]
    [SerializeField] private GameObject[] _cloudPrefabs;
    [SerializeField] private int _cloudCount = 30;

    [Header("Spawn Area")]
    [Tooltip("Doporučuji výrazně zvětšit, např. 500x50x500")]
    [SerializeField] private Vector3 _areaSize = new Vector3(500, 50, 500);

    [Header("Visuals & Scale")]
    [Tooltip("Rozsah náhodného zvětšení. Pokud jsou mraky malé, zvedni tyto hodnoty.")]
    [SerializeField] private Vector2 _scaleRange = new Vector2(5f, 15f);
    [Tooltip("Zapnout pouze pokud mraky vypadají dobře z každého úhlu nasvícení.")]
    [SerializeField] private bool _randomizeYRotation = false; 

    [Header("Wind Settings")]
    [SerializeField] private float _baseSpeed = 5.0f;
    [SerializeField] private float _speedVariation = 2.0f;
    [SerializeField] private Vector3 _windDirection = new Vector3(1, 0, 0); 

    private CloudData[] _clouds;
    private float _leftBound;
    private float _rightBound;

    private void Start()
    {
        InitializeClouds();
    }

    private void InitializeClouds()
    {
        if (_cloudPrefabs == null || _cloudPrefabs.Length == 0) return;

        _clouds = new CloudData[_cloudCount];
        _windDirection = _windDirection.normalized;
        _leftBound = -_areaSize.x / 2;
        _rightBound = _areaSize.x / 2;

        for (int i = 0; i < _cloudCount; i++)
        {
            GameObject prefab = _cloudPrefabs[Random.Range(0, _cloudPrefabs.Length)];
            
            // Instanciace rovnou pod rodiče
            GameObject cloudObj = Instantiate(prefab, transform);

            // Generování lokální pozice
            Vector3 localPos = new Vector3(
                Random.Range(_leftBound, _rightBound),
                Random.Range(-_areaSize.y / 2, _areaSize.y / 2),
                Random.Range(-_areaSize.z / 2, _areaSize.z / 2)
            );
            cloudObj.transform.localPosition = localPos;

            // Měřítko
            float scale = Random.Range(_scaleRange.x, _scaleRange.y);
            
            // Low-poly mraky často vypadají lépe, když jsou mírně roztažené do šířky
            cloudObj.transform.localScale = new Vector3(scale * Random.Range(1f, 1.3f), scale, scale * Random.Range(1f, 1.3f));

            if (_randomizeYRotation)
            {
                cloudObj.transform.localRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
            }

            // Paralaxa: Větší (a hypoteticky bližší) mraky se hýbou mírně odlišně
            float scaleFactor = (scale - _scaleRange.x) / (_scaleRange.y - _scaleRange.x);
            float finalSpeed = (_baseSpeed + Random.Range(-_speedVariation, _speedVariation)) * (1.0f + (scaleFactor * 0.3f));
            
            _clouds[i] = new CloudData
            {
                Transform = cloudObj.transform,
                Speed = finalSpeed
            };
        }
    }

    private void Update()
    {
        // Všechny mraky posouváme v jednom cyklu. Odpadá režie na volání desítek Update() metod.
        float dt = Time.deltaTime;
        Vector3 movementStep = _windDirection * dt;

        for (int i = 0; i < _clouds.Length; i++)
        {
            Transform t = _clouds[i].Transform;
            Vector3 pos = t.localPosition;
            
            pos += movementStep * _clouds[i].Speed;

            // Endless looping (Wrap-around na ose X)
            if (_windDirection.x > 0 && pos.x > _rightBound) 
                pos.x = _leftBound;
            else if (_windDirection.x < 0 && pos.x < _leftBound) 
                pos.x = _rightBound;

            t.localPosition = pos;
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0, 1, 1, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, _areaSize);
    }
}