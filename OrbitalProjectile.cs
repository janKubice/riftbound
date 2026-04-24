using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class OrbitalProjectile : SmartProjectile
{
    private Transform _orbitTarget;
    private Vector3 _orbitCenter;
    private float _orbitRadius;
    private float _orbitSpeed; 
    private float _currentAngle;

    public void InitializeOrbit(Transform target, Vector3 fallbackCenter, float radius, float speedDegrees, float startAngleDegrees)
    {
        _orbitTarget = target;
        _orbitCenter = fallbackCenter;
        _orbitRadius = radius;
        _orbitSpeed = speedDegrees;
        _currentAngle = startAngleDegrees;

        // Okamžitě vypneme lineární rychlost z base.Initialize, fyziku si řídíme sami
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
        }
    }

    // Unity zavolá tento FixedUpdate pro výpočet kruhové dráhy
    private void FixedUpdate()
    {
        if (!IsServer) return;

        // 1. Aktualizace středu rotace
        if (_orbitTarget != null)
        {
            // Pokud hostitel zemřel, cíl zrušíme a rotujeme kolem jeho poslední pozice (místa smrti)
            if (!_orbitTarget.gameObject.activeInHierarchy)
            {
                _orbitTarget = null;
            }
            else
            {
                // Střed držíme u hrudníku hostitele
                _orbitCenter = _orbitTarget.position + Vector3.up * 1f;
            }
        }

        // 2. Výpočet úhlu
        _currentAngle += _orbitSpeed * Time.fixedDeltaTime;
        float rad = _currentAngle * Mathf.Deg2Rad;

        // 3. Posun po kružnici
        Vector3 offset = new Vector3(Mathf.Cos(rad) * _orbitRadius, 0, Mathf.Sin(rad) * _orbitRadius);
        Vector3 newPos = _orbitCenter + offset;

        if (_rb != null)
        {
            Vector3 moveDir = (newPos - _rb.position).normalized;
            _rb.MovePosition(newPos);
            
            // Natáčení "špičky" projektilu po směru jízdy
            if (moveDir != Vector3.zero)
            {
                _rb.MoveRotation(Quaternion.LookRotation(moveDir));
            }
        }
    }
}