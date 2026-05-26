using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class WorldEventSpawnPoint : MonoBehaviour
{
    public static readonly List<WorldEventSpawnPoint> All = new List<WorldEventSpawnPoint>(256);

    [Header("Spawn Point")]
    public WorldPOICategory AllowedCategories = WorldPOICategory.Any;

    [Tooltip("Náhodný offset kolem bodu.")]
    [Min(0f)]
    public float Radius = 2.5f;

    [Tooltip("Nižší = méně pravděpodobné, vyšší = častější.")]
    [Min(0f)]
    public float Weight = 1f;

    [Header("Runtime")]
    [SerializeField] private bool _isOccupied;

    public bool IsOccupied
    {
        get => _isOccupied;
        set => _isOccupied = value;
    }

    private void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    public bool Allows(WorldPOICategory category)
    {
        if (category == WorldPOICategory.None)
            return true;

        return (AllowedCategories & category) != 0 || AllowedCategories == WorldPOICategory.Any;
    }

    public Vector3 GetRandomPosition()
    {
        Vector2 offset = Random.insideUnitCircle * Radius;
        return transform.position + new Vector3(offset.x, 0f, offset.y);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = _isOccupied ? Color.red : Color.cyan;
        Gizmos.DrawWireSphere(transform.position, Radius);
    }
#endif
}