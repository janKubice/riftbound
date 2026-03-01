using UnityEngine;

// Třída pro cacheování referencí, abychom nemuseli volat GetComponentsInChildren při každém výstřelu
public class PooledVFX
{
    public GameObject Root;
    public ParticleSystem[] Systems;
    public bool IsActive => Root != null && Root.activeSelf && IsPlaying();

    private bool IsPlaying()
    {
        if (Systems != null && Systems.Length > 0 && Systems[0] != null)
        {
            return Systems[0].isPlaying;
        }
        return false;
    }
}