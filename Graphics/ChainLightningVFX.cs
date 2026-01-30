using UnityEngine;
using System.Collections;

[RequireComponent(typeof(LineRenderer))]
public class ChainLightningVFX : MonoBehaviour
{
    [SerializeField] private float _duration = 0.2f; // Jak dlouho blesk svítí
    [SerializeField] private float _jitterAmount = 0.2f; // "Cukatání" blesku (volitelné)

    private LineRenderer _lineRenderer;

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        _lineRenderer.enabled = false;
    }

    public void DrawChain(Vector3[] positions)
    {
        if (positions == null || positions.Length < 2) return;

        _lineRenderer.positionCount = positions.Length;
        _lineRenderer.SetPositions(positions);
        _lineRenderer.enabled = true;

        StartCoroutine(FadeRoutine());
    }

    private IEnumerator FadeRoutine()
    {
        float timer = 0f;
        float startWidth = _lineRenderer.startWidth;

        while (timer < _duration)
        {
            timer += Time.deltaTime;
            // Ztenčování blesku
            float progress = timer / _duration;
            _lineRenderer.widthMultiplier = startWidth * (1f - progress);
            yield return null;
        }

        _lineRenderer.enabled = false;
        // Pokud je instancovaný (není v poolu), zničíme ho
        Destroy(gameObject); 
    }
}