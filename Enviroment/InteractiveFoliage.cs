using UnityEngine;
using System.Collections;

public class InteractiveFoliage : MonoBehaviour
{
    [Header("Reakce")]
    [SerializeField] private ParticleSystem _leafParticles; // Přetáhni sem particle efekt listí
    [SerializeField] private float _shakeAmount = 5.0f;     // Síla zatřesení
    [SerializeField] private float _shakeRecovery = 10.0f;  // Rychlost návratu

    private Quaternion _originalRot;
    private Vector3 _currentShakeVelocity;
    private Vector3 _shakeOffset; // Aktuální vychýlení

    private void Awake()
    {
        _originalRot = transform.localRotation;
    }

    private void Update()
    {
        // Procedurální návrat do původní polohy (pružina)
        if (_shakeOffset.sqrMagnitude > 0.001f)
        {
            // Lerp zpět k nule
            _shakeOffset = Vector3.Lerp(_shakeOffset, Vector3.zero, Time.deltaTime * _shakeRecovery);
            
            // Aplikace rotace
            transform.localRotation = _originalRot * Quaternion.Euler(_shakeOffset);
        }
    }

    // Tuto metodu zavoláme při zásahu
    public void OnHit(Vector3 hitDirection)
    {
        // 1. Spustit Particles (Listí)
        if (_leafParticles != null)
        {
            _leafParticles.Play();
        }

        // 2. Vypočítat směr výkyvu (strom se nakloní ve směru úderu)
        // Zjednodušeně: Úder zepředu (Z) způsobí rotaci kolem X.
        float intensity = Random.Range(0.8f, 1.2f);
        
        // Jednoduchý "impuls" do rotace
        _shakeOffset = new Vector3(hitDirection.z, 0, -hitDirection.x) * _shakeAmount * intensity;
    }
}