using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))] 
public class MusicManager : PersistentSingleton<MusicManager>
{
    [Header("Data")]
    [SerializeField] private GlobalMusicData _globalData;

    [Header("Settings")]
    [SerializeField] private float _locationCrossfadeTime = 3.0f;
    [SerializeField] private float _combatFadeInTime = 0.5f;
    [SerializeField] private float _combatFadeOutTime = 2.0f;
    [Range(0f, 1f)] [SerializeField] private float _masterVolume = 0.5f;

    public enum MusicState { Menu, Exploration, Combat, Boss }
    private MusicState _currentState = MusicState.Menu;
    
    private AudioSource _sourceA;
    private AudioSource _sourceB;
    private bool _isSourceAActive = true;

    private LocationProfile _currentLocation;
    private bool _isNight = false;
    private Coroutine _fadeRoutine;

    protected override void Awake()
    {
        base.Awake();
        _sourceA = gameObject.AddComponent<AudioSource>();
        _sourceB = gameObject.AddComponent<AudioSource>();
        
        SetupSource(_sourceA);
        SetupSource(_sourceB);

        // Preload bojových a boss tracků hned při startu (nebo na začátku hry)
        PreloadAudioClips(_globalData.ActionTracks);
        PreloadAudioClips(_globalData.BossTracks);
    }

    private void SetupSource(AudioSource source)
    {
        source.loop = true;
        source.playOnAwake = false;
        source.volume = 0f;
        source.spatialBlend = 0f;
    }

    // Explicitní asynchronní načtení audia do RAM, zabrání lagu při prvním Play()
    private void PreloadAudioClips(List<AudioClip> clips)
    {
        if (clips == null) return;
        
        foreach (var clip in clips)
        {
            if (clip != null && clip.loadState != AudioDataLoadState.Loaded)
            {
                clip.LoadAudioData(); // Načítá se na pozadí, neblokuje vlákno
            }
        }
    }

    public void SetState(MusicState newState)
    {
        if (_currentState == newState) return;
        _currentState = newState;
        RefreshMusic(newState == MusicState.Combat ? _combatFadeInTime : _combatFadeOutTime);
    }

    public void EnterLocation(LocationProfile profile)
    {
        _currentLocation = profile;

        if (_currentLocation != null)
        {
            // Můžeme preloadnout i lokace, když do nich hráč vstoupí
            PreloadAudioClips(_currentLocation.DayTracks);
            PreloadAudioClips(_currentLocation.NightTracks);
        }

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

        AudioSource activeSource = _isSourceAActive ? _sourceA : _sourceB;
        if (activeSource.clip == nextClip && activeSource.isPlaying) return;

        CrossfadeTo(nextClip, fadeDuration);
    }

    private AudioClip GetRandomClip(List<AudioClip> clips)
    {
        if (clips == null || clips.Count == 0) return null;
        return clips[Random.Range(0, clips.Count)];
    }

    private void CrossfadeTo(AudioClip newClip, float duration)
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(CrossfadeRoutine(newClip, duration));
    }

    private IEnumerator CrossfadeRoutine(AudioClip newClip, float duration)
    {
        AudioSource activeSource = _isSourceAActive ? _sourceA : _sourceB;
        AudioSource newSource = _isSourceAActive ? _sourceB : _sourceA;

        newSource.clip = newClip;
        if (newClip != null) newSource.Play();

        float timer = 0f;
        float startVolume = activeSource.volume;

        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            float t = timer / duration;

            activeSource.volume = Mathf.Lerp(startVolume, 0f, t);
            
            if (newClip != null)
            {
                newSource.volume = Mathf.Lerp(0f, _masterVolume, t);
            }

            yield return null;
        }

        activeSource.Stop();
        activeSource.volume = 0f;
        
        if (newClip != null)
        {
            newSource.volume = _masterVolume;
        }

        _isSourceAActive = !_isSourceAActive;
    }
}