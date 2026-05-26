using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
public class SettingsManager : PersistentSingleton<SettingsManager>
{
    [Header("Audio Mixer")]
    [SerializeField] private AudioMixer mainAudioMixer;

    [Tooltip("Exposed parameter name on the Master mixer group.")]
    [SerializeField] private string masterVolumeParameter = "Master";

    [Tooltip("Exposed parameter name on the SFX mixer group.")]
    [SerializeField] private string sfxVolumeParameter = "SFX";

    [Tooltip("Exposed parameter name on the Music mixer group.")]
    [SerializeField] private string musicVolumeParameter = "Music";

    [SerializeField] private bool logMissingMixerParameters = true;

    public SettingsData CurrentSettings { get; private set; } = new SettingsData();
    public bool HasUnsavedChanges => _isDirty;

    public event Action<SettingsData> SettingsChanged;
    public event Action OnSettingsApplied;

    private readonly HashSet<string> _missingMixerParametersAlreadyLogged = new HashSet<string>();

    private string _savePath;
    private bool _isDirty;

    private string SavePath
    {
        get
        {
            if (string.IsNullOrEmpty(_savePath))
                _savePath = Path.Combine(Application.persistentDataPath, "settings.json");

            return _savePath;
        }
    }

    protected override void Awake()
    {
        base.Awake();

        _savePath = Path.Combine(Application.persistentDataPath, "settings.json");
        LoadSettings();
    }

    public SettingsData GetSettingsCopy()
    {
        EnsureSettings();
        return CurrentSettings.Clone();
    }

    public void UpdateSettings(SettingsData newSettings, bool saveImmediately = false)
    {
        if (newSettings == null)
        {
            Debug.LogWarning("[SettingsManager] UpdateSettings received null settings.");
            return;
        }

        CurrentSettings = newSettings.Clone();
        CurrentSettings.MigrateIfNeeded();
        CurrentSettings.Validate();

        MarkDirty();
        ApplyAllSettings();

        if (saveImmediately)
            SaveSettingsIfNeeded();
    }

    public void ResetToDefaults(bool saveImmediately = true)
    {
        CurrentSettings = new SettingsData();
        CurrentSettings.MigrateIfNeeded();
        CurrentSettings.Validate();

        MarkDirty();
        ApplyAllSettings();

        if (saveImmediately)
            SaveSettingsIfNeeded();
    }

    public void SetResolution(int width, int height)
    {
        EnsureSettings();

        if (width <= 0 || height <= 0)
            return;

        if (CurrentSettings.ResolutionWidth == width && CurrentSettings.ResolutionHeight == height)
            return;

        CurrentSettings.ResolutionWidth = width;
        CurrentSettings.ResolutionHeight = height;

        MarkDirty();
        ApplyGraphicsSettings();
        NotifySettingsChanged();
    }

    public void SetFullScreenMode(FullScreenMode mode)
    {
        EnsureSettings();

        int modeValue = (int)mode;
        if (CurrentSettings.FullScreenMode == modeValue)
            return;

        CurrentSettings.FullScreenMode = modeValue;
        CurrentSettings.Fullscreen = mode != FullScreenMode.Windowed;

        MarkDirty();
        ApplyGraphicsSettings();
        NotifySettingsChanged();
    }

    public void SetVSync(int value)
    {
        EnsureSettings();

        value = Mathf.Clamp(value, 0, 2);
        if (CurrentSettings.VSync == value)
            return;

        CurrentSettings.VSync = value;

        MarkDirty();
        ApplyGraphicsSettings();
        NotifySettingsChanged();
    }

    public void SetShadowQuality(int value)
    {
        EnsureSettings();

        value = Mathf.Clamp(value, 0, 2);
        if (CurrentSettings.ShadowQuality == value)
            return;

        CurrentSettings.ShadowQuality = value;

        MarkDirty();
        ApplyGraphicsSettings();
        NotifySettingsChanged();
    }

    public void SetMasterVolume(float value)
    {
        EnsureSettings();
        SetVolume(ref CurrentSettings.MasterVolume, value);
    }

    public void SetSfxVolume(float value)
    {
        EnsureSettings();
        SetVolume(ref CurrentSettings.SfxVolume, value);
    }

    public void SetMusicVolume(float value)
    {
        EnsureSettings();
        SetVolume(ref CurrentSettings.MusicVolume, value);
    }

    public void SetCameraShakeEnabled(bool value)
    {
        EnsureSettings();

        if (CurrentSettings.EnableCameraShake == value)
            return;

        CurrentSettings.EnableCameraShake = value;

        MarkDirty();
        NotifySettingsChanged();
    }

    public void SetDamageNumbersEnabled(bool value)
    {
        EnsureSettings();

        if (CurrentSettings.ShowDamageNumbers == value)
            return;

        CurrentSettings.ShowDamageNumbers = value;

        MarkDirty();
        NotifySettingsChanged();
    }

    public void SetMouseSensitivity(float value)
    {
        EnsureSettings();

        value = Mathf.Clamp(value, 0.01f, 2f);
        if (Mathf.Approximately(CurrentSettings.MouseSensitivity, value))
            return;

        CurrentSettings.MouseSensitivity = value;

        MarkDirty();
        NotifySettingsChanged();
    }

    public void ApplyAllSettings()
    {
        EnsureSettings();
        CurrentSettings.MigrateIfNeeded();
        CurrentSettings.Validate();

        ApplyGraphicsSettings();
        ApplyAudioSettings();
        NotifySettingsChanged();
    }

    public void SaveSettingsIfNeeded()
    {
        if (!_isDirty)
            return;

        SaveSettings();
    }

    public void SaveSettings()
    {
        EnsureSettings();
        CurrentSettings.MigrateIfNeeded();
        CurrentSettings.Validate();

        try
        {
            string directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string json = JsonUtility.ToJson(CurrentSettings, true);
            string tempPath = SavePath + ".tmp";

            File.WriteAllText(tempPath, json);

            if (File.Exists(SavePath))
                File.Delete(SavePath);

            File.Move(tempPath, SavePath);
            _isDirty = false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SettingsManager] Failed to save settings: {e.Message}");
        }
    }

    private void LoadSettings()
    {
        SettingsData loadedSettings = null;

        if (File.Exists(SavePath))
        {
            try
            {
                string json = File.ReadAllText(SavePath);

                if (!string.IsNullOrWhiteSpace(json))
                    loadedSettings = JsonUtility.FromJson<SettingsData>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SettingsManager] Failed to load settings. Defaults will be used. Error: {e.Message}");
                TryBackupBrokenSettingsFile();
            }
        }

        CurrentSettings = loadedSettings ?? new SettingsData();
        CurrentSettings.MigrateIfNeeded();
        CurrentSettings.Validate();

        _isDirty = false;
        ApplyAllSettings();
    }

    private void ApplyGraphicsSettings()
    {
        EnsureSettings();
        CurrentSettings.Validate();

        QualitySettings.vSyncCount = CurrentSettings.VSync;

        FullScreenMode mode = (FullScreenMode)CurrentSettings.FullScreenMode;
        Screen.SetResolution(CurrentSettings.ResolutionWidth, CurrentSettings.ResolutionHeight, mode);

        // Explicitní volání 'UnityEngine.ShadowQuality' řeší chybu CS0104.
        // Funguje i v URP pro kompletní zapnutí/vypnutí stínů (0 = Disable).
        QualitySettings.shadows = (UnityEngine.ShadowQuality)CurrentSettings.ShadowQuality;
    }

    private void ApplyAudioSettings()
    {
        if (mainAudioMixer == null)
            return;

        SetMixerVolume(masterVolumeParameter, CurrentSettings.MasterVolume);
        SetMixerVolume(sfxVolumeParameter, CurrentSettings.SfxVolume);
        SetMixerVolume(musicVolumeParameter, CurrentSettings.MusicVolume);
    }

    private void SetMixerVolume(string parameterName, float linearValue)
    {
        if (mainAudioMixer == null || string.IsNullOrWhiteSpace(parameterName))
            return;

        linearValue = Mathf.Clamp01(linearValue);
        float dbValue = linearValue > 0.0001f ? Mathf.Log10(linearValue) * 20f : -80f;

        bool success = mainAudioMixer.SetFloat(parameterName, dbValue);

        if (!success && logMissingMixerParameters && _missingMixerParametersAlreadyLogged.Add(parameterName))
        {
            Debug.LogWarning($"[SettingsManager] AudioMixer parameter '{parameterName}' was not found. " +
                             "Check that the mixer volume is exposed and the parameter name matches exactly.");
        }
    }

    private void SetVolume(ref float target, float value)
    {
        value = Mathf.Clamp01(value);

        if (Mathf.Approximately(target, value))
            return;

        target = value;

        MarkDirty();
        ApplyAudioSettings();
        NotifySettingsChanged();
    }

    private void NotifySettingsChanged()
    {
        OnSettingsApplied?.Invoke();
        SettingsChanged?.Invoke(CurrentSettings);
    }

    private void MarkDirty()
    {
        _isDirty = true;
    }

    private void EnsureSettings()
    {
        if (CurrentSettings == null)
            CurrentSettings = new SettingsData();
    }

    private void TryBackupBrokenSettingsFile()
    {
        try
        {
            if (!File.Exists(SavePath))
                return;

            string backupPath = SavePath + ".broken." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.Copy(SavePath, backupPath, true);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SettingsManager] Failed to backup broken settings file: {e.Message}");
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            SaveSettingsIfNeeded();
    }

    private void OnApplicationQuit()
    {
        SaveSettingsIfNeeded();
    }
}
