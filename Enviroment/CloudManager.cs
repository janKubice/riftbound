using UnityEngine;

[DisallowMultipleComponent]
public class CloudManager : MonoBehaviour
{
    private struct CloudData
    {
        public Transform Transform;
        public float Speed;
        public float ScaleFactor;
        public Renderer[] Renderers;
    }

    [Header("Prefabs & Count")]
    [SerializeField] private GameObject[] _cloudPrefabs;
    [Min(0)] [SerializeField] private int _cloudCount = 30;
    [SerializeField] private int _seed = 12345;

    [Header("Spawn Area")]
    [Tooltip("Lokální prostor cloud fieldu. Například 500 x 50 x 500.")]
    [SerializeField] private Vector3 _areaSize = new Vector3(500f, 50f, 500f);

    [Header("Follow Camera")]
    [Tooltip("Doporučeno pro velké mapy. Mraky drží pole kolem kamery, ale stále se pohybují uvnitř pole.")]
    [SerializeField] private bool _followMainCamera = true;

    [Tooltip("Pokud je vyplněno, má přednost před Camera.main.")]
    [SerializeField] private Transform _followTarget;

    [SerializeField] private bool _followOnlyXZ = true;

    [Header("Visuals & Scale")]
    [SerializeField] private Vector2 _scaleRange = new Vector2(5f, 15f);
    [SerializeField] private Vector2 _horizontalStretchRange = new Vector2(1.0f, 1.35f);

    [Tooltip("Zapnout pouze pokud mraky vypadají dobře z každého úhlu.")]
    [SerializeField] private bool _randomizeYRotation = false;

    [Header("Wind Settings")]
    [SerializeField] private bool _useGlobalWind = true;
    [SerializeField] private Vector3 _fallbackWindDirection = new Vector3(1f, 0f, 0.35f);
    [SerializeField] private float _baseSpeed = 5.0f;
    [SerializeField] private float _speedVariation = 2.0f;
    [SerializeField] private float _globalWindSpeedMultiplier = 1.0f;

    [Header("Day Night Visuals")]
    [SerializeField] private bool _useDayNightTint = true;

    [SerializeField] private Gradient _cloudTintByTime = DefaultCloudTintGradient();

    [SerializeField] private AnimationCurve _opacityByTime = new AnimationCurve(
        new Keyframe(0.00f, 0.45f),
        new Keyframe(0.25f, 0.65f),
        new Keyframe(0.50f, 0.85f),
        new Keyframe(0.75f, 0.70f),
        new Keyframe(1.00f, 0.45f)
    );

    [Range(0f, 1f)]
    [SerializeField] private float _fogColorInfluence = 0.25f;

    [Header("Location Influence")]
    [SerializeField] private bool _useAtmosphereLocationInfluence = true;

    [Range(0f, 1f)]
    [SerializeField] private float _locationTintInfluence = 0.25f;

    [Range(-1f, 1f)]
    [SerializeField] private float _locationOpacityAdd = 0.05f;

    [Header("Material Property Names")]
    [SerializeField] private bool _useMaterialPropertyBlocks = true;

    [Tooltip("Nastaví více běžných názvů barev, aby fungovaly různé shadery.")]
    [SerializeField] private bool _setCommonColorProperties = true;

    [Tooltip("Nastaví více běžných názvů opacity/alpha properties.")]
    [SerializeField] private bool _setCommonOpacityProperties = true;

    [Header("Debug")]
    [SerializeField] private bool _regenerateOnStart = true;
    [SerializeField] private bool _drawGizmo = true;

    private CloudData[] _clouds;
    private Transform _cloudRoot;
    private MaterialPropertyBlock _block;
    private Vector3 _lastFollowPosition;

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private static readonly int TintColorID = Shader.PropertyToID("_TintColor");
    private static readonly int CloudTintID = Shader.PropertyToID("_CloudTint");

    private static readonly int OpacityID = Shader.PropertyToID("_Opacity");
    private static readonly int AlphaID = Shader.PropertyToID("_Alpha");
    private static readonly int CloudOpacityID = Shader.PropertyToID("_CloudOpacity");

    private void Start()
    {
        if (_regenerateOnStart)
            RegenerateClouds();
        else if (_clouds == null || _clouds.Length == 0)
            CacheExistingClouds();
    }

    private void Update()
    {
        FollowTargetIfNeeded();

        if (_clouds == null || _clouds.Length == 0)
            return;

        float deltaTime = Time.deltaTime;

        Vector3 windDirectionWorld = GetWindDirectionWorld();
        Vector3 windDirectionLocal = transform.InverseTransformDirection(windDirectionWorld);
        windDirectionLocal.y = 0f;

        if (windDirectionLocal.sqrMagnitude < 0.0001f)
            windDirectionLocal = Vector3.right;

        windDirectionLocal.Normalize();

        float windStrength = GetWindStrength();
        Vector3 movementStep = windDirectionLocal * (_globalWindSpeedMultiplier * windStrength * deltaTime);

        Vector3 halfSize = _areaSize * 0.5f;

        for (int i = 0; i < _clouds.Length; i++)
        {
            Transform cloud = _clouds[i].Transform;

            if (cloud == null)
                continue;

            Vector3 localPosition = cloud.localPosition;
            localPosition += movementStep * _clouds[i].Speed;

            localPosition.x = Wrap(localPosition.x, -halfSize.x, halfSize.x);
            localPosition.z = Wrap(localPosition.z, -halfSize.z, halfSize.z);

            cloud.localPosition = localPosition;
        }

        ApplyVisuals();
    }

    [ContextMenu("Regenerate Clouds")]
    public void RegenerateClouds()
    {
        if (_cloudPrefabs == null || _cloudPrefabs.Length == 0)
        {
            Debug.LogWarning("CloudManager: Cloud prefabs nejsou nastavené.", this);
            return;
        }

        EnsureCloudRoot();
        ClearClouds();

        Random.State oldState = Random.state;
        Random.InitState(_seed);

        _clouds = new CloudData[_cloudCount];

        Vector3 halfSize = _areaSize * 0.5f;

        for (int i = 0; i < _cloudCount; i++)
        {
            GameObject prefab = _cloudPrefabs[Random.Range(0, _cloudPrefabs.Length)];

            if (prefab == null)
                continue;

            GameObject cloudObject = Instantiate(prefab, _cloudRoot);

            cloudObject.name = $"Cloud_{i:00}_{prefab.name}";

            Vector3 localPosition = new Vector3(
                Random.Range(-halfSize.x, halfSize.x),
                Random.Range(-halfSize.y, halfSize.y),
                Random.Range(-halfSize.z, halfSize.z)
            );

            cloudObject.transform.localPosition = localPosition;

            float scale = Random.Range(_scaleRange.x, _scaleRange.y);
            float stretchX = Random.Range(_horizontalStretchRange.x, _horizontalStretchRange.y);
            float stretchZ = Random.Range(_horizontalStretchRange.x, _horizontalStretchRange.y);

            cloudObject.transform.localScale = new Vector3(
                scale * stretchX,
                scale,
                scale * stretchZ
            );

            if (_randomizeYRotation)
            {
                cloudObject.transform.localRotation = Quaternion.Euler(
                    0f,
                    Random.Range(0f, 360f),
                    0f
                );
            }
            else
            {
                cloudObject.transform.localRotation = Quaternion.identity;
            }

            float scaleFactor = Mathf.InverseLerp(_scaleRange.x, _scaleRange.y, scale);
            float speed = _baseSpeed + Random.Range(-_speedVariation, _speedVariation);
            speed *= 1.0f + scaleFactor * 0.25f;
            speed = Mathf.Max(0.01f, speed);

            _clouds[i] = new CloudData
            {
                Transform = cloudObject.transform,
                Speed = speed,
                ScaleFactor = scaleFactor,
                Renderers = cloudObject.GetComponentsInChildren<Renderer>(true)
            };
        }

        Random.state = oldState;

        ApplyVisuals();
    }

    private void CacheExistingClouds()
    {
        EnsureCloudRoot();

        int childCount = _cloudRoot.childCount;
        _clouds = new CloudData[childCount];

        for (int i = 0; i < childCount; i++)
        {
            Transform child = _cloudRoot.GetChild(i);

            _clouds[i] = new CloudData
            {
                Transform = child,
                Speed = Mathf.Max(0.01f, _baseSpeed + Random.Range(-_speedVariation, _speedVariation)),
                ScaleFactor = 0.5f,
                Renderers = child.GetComponentsInChildren<Renderer>(true)
            };
        }
    }

    private void EnsureCloudRoot()
    {
        if (_cloudRoot != null)
            return;

        Transform existing = transform.Find("GeneratedClouds");

        if (existing != null)
        {
            _cloudRoot = existing;
            return;
        }

        GameObject root = new GameObject("GeneratedClouds");
        root.transform.SetParent(transform);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        _cloudRoot = root.transform;
    }

    private void ClearClouds()
    {
        if (_cloudRoot == null)
            return;

        for (int i = _cloudRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = _cloudRoot.GetChild(i);

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }

    private void FollowTargetIfNeeded()
    {
        if (!_followMainCamera && _followTarget == null)
            return;

        Transform target = _followTarget;

        if (target == null && Camera.main != null)
            target = Camera.main.transform;

        if (target == null)
            return;

        Vector3 targetPosition = target.position;
        Vector3 newPosition = transform.position;

        if (_followOnlyXZ)
        {
            newPosition.x = targetPosition.x;
            newPosition.z = targetPosition.z;
        }
        else
        {
            newPosition = targetPosition;
        }

        if ((newPosition - _lastFollowPosition).sqrMagnitude > 0.0001f)
        {
            transform.position = newPosition;
            _lastFollowPosition = newPosition;
        }
    }

    private Vector3 GetWindDirectionWorld()
    {
        Vector3 direction;

        if (_useGlobalWind && GlobalWindManager.Instance != null)
        {
            direction = GlobalWindManager.Instance.CurrentWindDirection;
        }
        else
        {
            direction = _fallbackWindDirection;
        }

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.right;

        return direction.normalized;
    }

    private float GetWindStrength()
    {
        if (_useGlobalWind && GlobalWindManager.Instance != null)
            return Mathf.Max(0.01f, GlobalWindManager.Instance.CurrentWindStrength);

        return 1f;
    }

    private void ApplyVisuals()
    {
        if (!_useDayNightTint && !_useAtmosphereLocationInfluence)
            return;

        if (_block == null)
            _block = new MaterialPropertyBlock();

        float timePercent = DayNightCycle.Instance != null
            ? DayNightCycle.Instance.TimePercent
            : 0.5f;

        Color tint = _cloudTintByTime.Evaluate(timePercent);
        float opacity = Mathf.Clamp01(_opacityByTime.Evaluate(timePercent));

        if (DayNightCycle.Instance != null)
        {
            Color fogColor = DayNightCycle.Instance.CurrentFogColor;
            tint = Color.Lerp(tint, fogColor, _fogColorInfluence);
        }

        if (_useAtmosphereLocationInfluence && AtmosphereManager.Instance != null)
        {
            LocationProfile profile = AtmosphereManager.Instance.CurrentProfile;
            float influence = AtmosphereManager.Instance.LocationInfluence;

            if (profile != null && influence > 0.001f)
            {
                tint = Color.Lerp(tint, profile.FogColor, _locationTintInfluence * influence);
                opacity = Mathf.Clamp01(opacity + _locationOpacityAdd * influence);
            }
        }

        for (int i = 0; i < _clouds.Length; i++)
        {
            Renderer[] renderers = _clouds[i].Renderers;

            if (renderers == null)
                continue;

            float cloudOpacity = opacity;

            // Větší mraky mohou být lehce výraznější.
            cloudOpacity *= Mathf.Lerp(0.85f, 1.1f, _clouds[i].ScaleFactor);
            cloudOpacity = Mathf.Clamp01(cloudOpacity);

            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];

                if (renderer == null)
                    continue;

                if (_useMaterialPropertyBlocks)
                {
                    renderer.GetPropertyBlock(_block);

                    if (_setCommonColorProperties)
                    {
                        _block.SetColor(BaseColorID, tint);
                        _block.SetColor(ColorID, tint);
                        _block.SetColor(TintColorID, tint);
                        _block.SetColor(CloudTintID, tint);
                    }

                    if (_setCommonOpacityProperties)
                    {
                        _block.SetFloat(OpacityID, cloudOpacity);
                        _block.SetFloat(AlphaID, cloudOpacity);
                        _block.SetFloat(CloudOpacityID, cloudOpacity);
                    }

                    renderer.SetPropertyBlock(_block);
                }
                else
                {
                    Material material = renderer.sharedMaterial;

                    if (material == null)
                        continue;

                    if (_setCommonColorProperties)
                    {
                        if (material.HasProperty(BaseColorID)) material.SetColor(BaseColorID, tint);
                        if (material.HasProperty(ColorID)) material.SetColor(ColorID, tint);
                        if (material.HasProperty(TintColorID)) material.SetColor(TintColorID, tint);
                        if (material.HasProperty(CloudTintID)) material.SetColor(CloudTintID, tint);
                    }

                    if (_setCommonOpacityProperties)
                    {
                        if (material.HasProperty(OpacityID)) material.SetFloat(OpacityID, cloudOpacity);
                        if (material.HasProperty(AlphaID)) material.SetFloat(AlphaID, cloudOpacity);
                        if (material.HasProperty(CloudOpacityID)) material.SetFloat(CloudOpacityID, cloudOpacity);
                    }
                }
            }
        }
    }

    private static float Wrap(float value, float min, float max)
    {
        float range = max - min;

        if (range <= 0.0001f)
            return value;

        while (value < min)
            value += range;

        while (value > max)
            value -= range;

        return value;
    }

    private void OnDrawGizmos()
    {
        if (!_drawGizmo)
            return;

        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, _areaSize);

        Vector3 wind = GetWindDirectionWorld();
        Gizmos.color = new Color(0.5f, 0.85f, 1f, 0.9f);
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.DrawRay(transform.position, wind * 30f);
    }

    private static Gradient DefaultCloudTintGradient()
    {
        Gradient gradient = new Gradient();

        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Hex("#2A2C44"), 0.00f),
                new GradientColorKey(Hex("#8797AF"), 0.25f),
                new GradientColorKey(Hex("#E2E6EA"), 0.50f),
                new GradientColorKey(Hex("#D88A76"), 0.72f),
                new GradientColorKey(Hex("#2A2C44"), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0.00f),
                new GradientAlphaKey(1f, 1.00f)
            }
        );

        return gradient;
    }

    private static Color Hex(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out Color color))
            return color;

        return Color.white;
    }
}