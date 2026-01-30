using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class LaserBeamVFX : MonoBehaviour
{
    private LineRenderer _lineRenderer;

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
    }

    public void UpdateBeam(Vector3 start, Vector3 end)
    {
        _lineRenderer.enabled = true;
        _lineRenderer.SetPosition(0, start);
        _lineRenderer.SetPosition(1, end);
    }

    public void StopBeam()
    {
        if (_lineRenderer != null) _lineRenderer.enabled = false;
    }
}