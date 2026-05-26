using System.Collections.Generic;

[System.Serializable]
public class SpawnPool
{
    public string PoolName = "Default Pool";
    public List<EnemySpawnWeight> Enemies = new List<EnemySpawnWeight>();

    private float _totalWeight = -1f;

    public float GetTotalWeight(bool forceRecalculate = false)
    {
        if (forceRecalculate || _totalWeight < 0f)
        {
            _totalWeight = 0f;

            if (Enemies == null)
                return _totalWeight;

            for (int i = 0; i < Enemies.Count; i++)
            {
                EnemySpawnWeight entry = Enemies[i];

                if (entry == null)
                    continue;

                if (entry.EnemyDef == null)
                    continue;

                if (entry.Weight <= 0f)
                    continue;

                _totalWeight += entry.Weight;
            }
        }

        return _totalWeight;
    }

    public void InvalidateWeightCache()
    {
        _totalWeight = -1f;
    }

    public bool IsUsable()
    {
        return Enemies != null && Enemies.Count > 0 && GetTotalWeight(true) > 0f;
    }
}