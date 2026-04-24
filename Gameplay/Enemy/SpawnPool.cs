using System.Collections.Generic;

[System.Serializable]
public class SpawnPool
{
    public string PoolName = "Default Wave";
    public List<EnemySpawnWeight> Enemies;
    
    private float _totalWeight = -1f;

    public float GetTotalWeight()
    {
        if (_totalWeight < 0)
        {
            _totalWeight = 0;
            foreach (var e in Enemies) _totalWeight += e.Weight;
        }
        return _totalWeight;
    }
}