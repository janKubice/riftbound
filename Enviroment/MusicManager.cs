using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))] 
public class MusicManager : PersistentSingleton<MusicManager>
{
    [Header("Data")]
    [SerializeField] private GlobalMusicData _globalData;

    [Header("Settings")]
    [SerializeField] private float _locationCrossfadeTime = 3.0f; // Pomalý přechod lokací
    [SerializeField] private float _combatFadeInTime = 0.5f;      // Rychlý nástup boje
    [SerializeField] private float _combatFadeOutTime = 2.0f;     // Pomalejší uklidnění
    [Range(0f, 1f)] [SerializeField] private float _masterVolume = 0.5f;

    // Interní stavy
    public enum MusicState { Menu, Exploration, Combat, Boss }
    private MusicState _currentState = MusicState.Menu;
    
    // Audio Sources pro Double Buffering
    private AudioSource _sourceA;
    private AudioSource _sourceB;
    private bool _isSourceAActive = true;

    // Aktuální kontext
    private LocationProfile _currentLocation;
    private bool _isNight = false; // Toto musíte napojit na váš TimeManager
    private Coroutine _fadeRoutine;

    protected override void Awake()
    {
        base.Awake();
        // Vytvoříme dva zdroje dynamicky, abychom nemuseli nic nastavovat v Inspectoru
        _sourceA = gameObject.AddComponent<AudioSource>();
        _sourceB = gameObject.AddComponent<AudioSource>();
        
        SetupSource(_sourceA);
        SetupSource(_sourceB);
    }

    private void SetupSource(AudioSource source)
    {
        source.loop = true;
        source.playOnAwake = false;
        source.volume = 0f;
        source.spatialBlend = 0f; // 2D zvuk
    }

    // --- Veřejné API ---

    public void SetState(MusicState newState)
    {
        if (_currentState == newState) return;
        _currentState = newState;
        RefreshMusic(newState == MusicState.Combat ? _combatFadeInTime : _combatFadeOutTime);
    }

    public void EnterLocation(LocationProfile profile)
    {
        _currentLocation = profile;
        if (_currentState == MusicState.Exploration)
        {
            RefreshMusic(_locationCrossfadeTime);
        }
    }

    public void SetDayNight(bool isNight)
    {
        if (_isNight != isNight)
        {
            _isNight = isNight;
            if (_currentState == MusicState.Exploration)
            {
                RefreshMusic(_locationCrossfadeTime);
            }
        }
    }

    public void PlayMenuMusic()
    {
        _currentState = MusicState.Menu;
        CrossfadeTo(_globalData.MenuTrack, 1.0f);
    }

    // --- Logika výběru hudby ---

    private void RefreshMusic(float fadeDuration)
    {
        AudioClip nextClip = null;

        switch (_currentState)
        {
            case MusicState.Menu:
                nextClip = _globalData.MenuTrack;
                break;

            case MusicState.Exploration:
                if (_currentLocation != null)
                {
                    List<AudioClip> playlist = _isNight ? _currentLocation.NightTracks : _currentLocation.DayTracks;
                    nextClip = GetRandomClip(playlist);
                }
                break;

            case MusicState.Combat:
                nextClip = GetRandomClip(_globalData.ActionTracks);
                break;

            case MusicState.Boss:
                nextClip = GetRandomClip(_globalData.BossTracks);
                break;
        }

        // Pokud je klip stejný jako ten, co hraje, neděláme nic (nepřerušujeme smyčku)
        AudioSource activeSource = _isSourceAActive ? _sourceA : _sourceB;
        if (activeSource.clip == nextClip && activeSource.isPlaying) return;

        CrossfadeTo(nextClip, fadeDuration);
    }

    private AudioClip GetRandomClip(List<AudioClip> clips)
    {
        if (clips == null || clips.Count == 0) return null;
        return clips[Random.Range(0, clips.Count)];
    }

    // --- Crossfade Logika ---

    private void CrossfadeTo(AudioClip newClip, float duration)
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(CrossfadeRoutine(newClip, duration));
    }

    private IEnumerator CrossfadeRoutine(AudioClip newClip, float duration)
    {
        AudioSource activeSource = _isSourceAActive ? _sourceA : _sourceB;
        AudioSource newSource = _isSourceAActive ? _sourceB : _sourceA;

        // 1. Příprava nového zdroje
        newSource.clip = newClip;
        if (newClip != null) newSource.Play();

        float timer = 0f;
        float startVolume = activeSource.volume;

        // 2. Prolínání
        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime; // Unscaled, aby hudba hrála i v pauze
            float t = timer / duration;

            // Aktivní zdroj jde do ticha
            activeSource.volume = Mathf.Lerp(startVolume, 0f, t);
            
            // Nový zdroj jde na MasterVolume (pokud existuje klip)
            if (newClip != null)
            {
                newSource.volume = Mathf.Lerp(0f, _masterVolume, t);
            }

            yield return null;
        }

        // 3. Dokončení
        activeSource.Stop();
        activeSource.volume = 0f;
        
        if (newClip != null)
        {
            newSource.volume = _masterVolume;
        }

        // Prohození rolí
        _isSourceAActive = !_isSourceAActive;
    }
}