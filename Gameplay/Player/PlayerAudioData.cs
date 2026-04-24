using UnityEngine;

[System.Serializable]
public struct AudioClipsSettings
{
    public AudioClip Clip;
    [Range(0f, 1f)] public float Volume;

    public AudioClipsSettings(AudioClip clip, float volume = 1f)
    {
        Clip = clip;
        Volume = volume;
    }
}

// Tento objekt bude obsahovat všechny zvukové klipy pro hráče.
// Vytvořte ho v Unity kliknutím pravým tlačítkem -> Create -> Audio -> Player Audio Data
[CreateAssetMenu(fileName = "PlayerAudioData", menuName = "Audio/Player Audio Data")]
public class PlayerAudioData : ScriptableObject
{
    [Header("Pohyb")]
    public AudioClipsSettings Jump = new AudioClipsSettings(null, 1.0f);
    public AudioClipsSettings Land = new AudioClipsSettings(null, 1.0f);
    public AudioClipsSettings DodgeSwoosh = new AudioClipsSettings(null, 1.0f);
    public AudioClipsSettings Footstep = new AudioClipsSettings(null, 1.0f);

    [Header("Boj")]
    public AudioClipsSettings AttackSwing = new AudioClipsSettings(null, 1.0f);
    public AudioClipsSettings HitReceived = new AudioClipsSettings(null, 1.0f);
    public AudioClipsSettings HitDealt = new AudioClipsSettings(null, 1.0f);

    [Header("Stav")]
    public AudioClipsSettings OutOfStamina = new AudioClipsSettings(null, 1.0f);
    public AudioClipsSettings HealthCritical = new AudioClipsSettings(null, 1.0f);
    public AudioClipsSettings ManaCritical = new AudioClipsSettings(null, 1.0f);

    [Header("Interakce")]
    public AudioClipsSettings ItemPickup = new AudioClipsSettings(null, 1.0f);
}