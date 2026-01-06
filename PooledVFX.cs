using UnityEngine;

// Třída pro cacheování referencí, abychom nemuseli volat GetComponentsInChildren při každém výstřelu
public class PooledVFX
{
    public GameObject Root;
    public ParticleSystem[] Systems;
    public bool IsActive => Root.activeSelf && IsPlaying();

    private bool IsPlaying()
    {
        // Stačí zkontrolovat první systém, nebo kořenový
        if (Systems != null && Systems.Length > 0)
        {
            return Systems[0].isPlaying;
        }
        return false;
    }
}