using UnityEngine;
using System.Linq;

// Statická helper třída pro vyhledávání efektů
public static class GameEffectDatabase
{
    private static StatusEffectData[] _cachedEffects;

    public static StatusEffectData GetEffectByName(string name)
    {
        if (_cachedEffects == null)
        {
            _cachedEffects = Resources.LoadAll<StatusEffectData>("StatusEffects");
        }
        return _cachedEffects.FirstOrDefault(e => e.EffectName == name);
    }
}