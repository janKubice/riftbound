using UnityEngine;

[ExecuteAlways]
public class GlobalWindManager : MonoBehaviour
{
    public static GlobalWindManager Instance { get; private set; }

    [Header("Wind Settings")]
    [Min(0f)] public float WindSpeed = 1.0f;
    [Min(0f)] public float WindStrength = 0.5f;
    public Vector3 WindDirection = new Vector3(1f, 0f, 1f);

    [Header("Gusts")]
    [Range(0f, 2f)] public float GustAmount = 0.2f;
    [Min(0f)] public float GustFrequency = 1.0f;

    [Header("Debug")]
    [SerializeField] private bool _drawGizmo = true;
    [SerializeField] private float _gizmoLength = 4f;

    public Vector3 CurrentWindDirection { get; private set; } = Vector3.forward;
    public float CurrentWindStrength { get; private set; }

    private static readonly int GlobalWindID = Shader.PropertyToID("_GlobalWind");
    private static readonly int GlobalWindTimeID = Shader.PropertyToID("_GlobalWindTime");

    private void OnEnable()
    {
        Instance = this;
        UpdateWind();
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        UpdateWind();
    }

    private void UpdateWind()
    {
        Vector3 direction = WindDirection;

        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.forward;

        direction.y = 0f;
        direction.Normalize();

        float time = Application.isPlaying ? Time.time : (float)UnityEditorSafeTime();

        float gust = 1.0f + Mathf.Sin(time * GustFrequency) * GustAmount;
        float finalStrength = Mathf.Max(0f, WindStrength * gust);

        CurrentWindDirection = direction;
        CurrentWindStrength = finalStrength;

        Vector4 windParams = new Vector4(
            CurrentWindDirection.x,
            CurrentWindDirection.y,
            CurrentWindDirection.z,
            CurrentWindStrength
        );

        Shader.SetGlobalVector(GlobalWindID, windParams);
        Shader.SetGlobalFloat(GlobalWindTimeID, time * WindSpeed);
    }

    private double UnityEditorSafeTime()
    {
#if UNITY_EDITOR
        return UnityEditor.EditorApplication.timeSinceStartup;
#else
        return Time.time;
#endif
    }

    private void OnDrawGizmos()
    {
        if (!_drawGizmo)
            return;

        Vector3 direction = WindDirection;

        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.forward;

        direction.y = 0f;
        direction.Normalize();

        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.85f);
        Gizmos.DrawRay(transform.position, direction * _gizmoLength);
        Gizmos.DrawWireSphere(transform.position + direction * _gizmoLength, 0.25f);
    }
}