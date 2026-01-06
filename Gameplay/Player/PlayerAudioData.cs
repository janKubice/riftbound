using UnityEngine;

// Tento objekt bude obsahovat všechny zvukové klipy pro hráče.
// Vytvořte ho v Unity kliknutím pravým tlačítkem -> Create -> Audio -> Player Audio Data
[CreateAssetMenu(fileName = "PlayerAudioData", menuName = "Audio/Player Audio Data")]
public class PlayerAudioData : ScriptableObject
{
    [Header("Pohyb")]
    public AudioClip Jump;
    public AudioClip Land;
    public AudioClip DodgeSwoosh;
    public AudioClip Footstep; // Pro budoucí použití

    [Header("Boj")]
    public AudioClip AttackSwing;
    public AudioClip HitReceived; // Dostal jsem zásah
    public AudioClip HitDealt;    // Dal jsem zásah

    [Header("Stav")]
    public AudioClip OutOfStamina;
    public AudioClip HealthCritical; // Pro budoucí použití

    [Header("Interakce")]
    public AudioClip ItemPickup;
}