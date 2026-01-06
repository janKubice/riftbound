using UnityEngine;
using System.Collections.Generic;

public class WeaponVisualsController : MonoBehaviour
{
    private List<PooledVFX> _vfxPool = new List<PooledVFX>();
    
    private GameObject _currentVfxPrefab;
    private Transform _currentSpawnPoint;

    public void InitializeWeapon(WeaponData data, GameObject weaponInstance)
    {
        // 1. Vyčistit starý pool při změně zbraně (zničíme staré objekty)
        foreach (var item in _vfxPool)
        {
            if (item.Root != null) Destroy(item.Root);
        }
        _vfxPool.Clear();

        // 2. Nastavení referencí pro nový typ zbraně
        _currentVfxPrefab = data.IsRanged ? data.MuzzleFlashPrefab : data.MuzzleFlashPrefab; // Uprav dle potřeby (Swing vs Muzzle)
        
        if (data.IsRanged)
        {
            _currentSpawnPoint = weaponInstance.transform.Find("FirePoint");
        }
        else
        {
            _currentSpawnPoint = weaponInstance.transform.Find("SwingPoint");
            if (_currentSpawnPoint == null) _currentSpawnPoint = weaponInstance.transform;
        }
    }

    public void OnAttackVisual(float cooldown)
    {
        if (_currentVfxPrefab == null || _currentSpawnPoint == null) return;

        // 3. Pokus najít volný efekt v poolu
        PooledVFX vfxToUse = null;

        foreach (var item in _vfxPool)
        {
            if (!item.IsActive) // Pokud efekt zrovna nehraje/je vypnutý
            {
                vfxToUse = item;
                break;
            }
        }

        // 4. Pokud není žádný volný, vytvoříme nový (expandujeme pool)
        if (vfxToUse == null)
        {
            vfxToUse = CreateNewVFXInstance();
            _vfxPool.Add(vfxToUse);
        }

        // 5. Aktivace a přehrání
        vfxToUse.Root.SetActive(true);
        foreach (var ps in vfxToUse.Systems)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play();
        }
    }

    private PooledVFX CreateNewVFXInstance()
    {
        GameObject instance = Instantiate(_currentVfxPrefab, _currentSpawnPoint);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;

        PooledVFX pooled = new PooledVFX();
        pooled.Root = instance;
        // Načteme všechny systémy jednou při vzniku
        pooled.Systems = instance.GetComponentsInChildren<ParticleSystem>(true);

        // Vypneme playOnAwake pro jistotu
        foreach (var ps in pooled.Systems)
        {
            var main = ps.main;
            main.playOnAwake = false;
        }

        return pooled;
    }
}