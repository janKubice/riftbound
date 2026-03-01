using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class GravityProjectile : SmartProjectile
{
    [Header("Gravity Settings")]
    [Tooltip("Síla vyhození nahoru (dělá oblouk)")]
    [SerializeField] private float _arcHeight = 5f;
    
    [Tooltip("Reference na 3D model, který se bude točit (přetáhni z hierarchy)")]
    [SerializeField] private Transform _visualTransform; 
    
    [Tooltip("Rychlost rotace modelu")]
    [SerializeField] private float _rotationSpeed = 360f;

    // Přepíšeme Initialize pro úpravu fyziky při spawnu
    public override void Initialize(NetworkObject attacker, Vector3 direction, WeaponStats stats, List<HitEffect> payload = null)
    {
        // 1. Provedeme základní nastavení (ID útočníka, staty, payload)
        base.Initialize(attacker, direction, stats, payload);

        // 2. Přepíšeme chování Rigidbody
        if (_rb != null)
        {
            _rb.useGravity = true; // Base logika ji vypíná, my ji zapneme
            
            // Vypočítáme oblouk
            Vector3 throwForce = (direction.normalized * stats.ProjectileSpeed) + (Vector3.up * _arcHeight);
            _rb.linearVelocity = throwForce; // Rychlost zohledňuje směr i výšku
        }
    }

    private void Update()
    {
        // Vizuální rotace nezávislá na fyzice (jen efekt pro hráče)
        if (_visualTransform != null)
        {
            _visualTransform.Rotate(Vector3.right * _rotationSpeed * Time.deltaTime);
        }
    }
}