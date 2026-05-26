using UnityEngine;

[System.Serializable]
public class SettingsData
{
    public const int CurrentVersion = 2;

    public int Version = CurrentVersion;

    [Header("Graphics")]
    public int ResolutionWidth = 0;
    public int ResolutionHeight = 0;

    // Stored as int because JsonUtility handles enums, but int is easier to migrate safely.
    // FullScreenMode values: 0 ExclusiveFullScreen, 1 FullScreenWindow, 2 MaximizedWindow, 3 Windowed.
    public int FullScreenMode = (int)UnityEngine.FullScreenMode.FullScreenWindow;

    // Kept for migration from your old settings file. New code uses FullScreenMode instead.
    public bool Fullscreen = true;

    public int VSync = 1;          // 0 Off, 1 Every V Blank, 2 Every Second V Blank
    public int ShadowQuality = 2;  // 0 Disable, 1 HardOnly, 2 All

    [Header("Audio")]
    [Range(0f, 1f)] public float MasterVolume = 1f;
    [Range(0f, 1f)] public float SfxVolume = 1f;
    [Range(0f, 1f)] public float MusicVolume = 1f;

    [Header("Gameplay")]
    public bool EnableCameraShake = true;
    public bool ShowDamageNumbers = true;

    [Range(0.05f, 10f)] public float MouseSensitivity = 1f;

    public SettingsData Clone()
    {
        return new SettingsData
        {
            Version = Version,
            ResolutionWidth = ResolutionWidth,
            ResolutionHeight = ResolutionHeight,
            FullScreenMode = FullScreenMode,
            Fullscreen = Fullscreen,
            VSync = VSync,
            ShadowQuality = ShadowQuality,
            MasterVolume = MasterVolume,
            SfxVolume = SfxVolume,
            MusicVolume = MusicVolume,
            EnableCameraShake = EnableCameraShake,
            ShowDamageNumbers = ShowDamageNumbers,
            MouseSensitivity = MouseSensitivity
        };
    }

    public void MigrateIfNeeded()
    {
        if (Version < 2)
        {
            FullScreenMode = Fullscreen
                ? (int)UnityEngine.FullScreenMode.FullScreenWindow
                : (int)UnityEngine.FullScreenMode.Windowed;

            if (ResolutionWidth <= 0 || ResolutionHeight <= 0)
            {
                ResolutionWidth = Screen.currentResolution.width;
                ResolutionHeight = Screen.currentResolution.height;
            }
        }

        Version = CurrentVersion;
    }

    public void Validate()
    {
        if (ResolutionWidth <= 0 || ResolutionHeight <= 0)
        {
            ResolutionWidth = Screen.currentResolution.width;
            ResolutionHeight = Screen.currentResolution.height;
        }

        if (!IsValidFullScreenMode(FullScreenMode))
            FullScreenMode = (int)UnityEngine.FullScreenMode.FullScreenWindow;

        Fullscreen = ((UnityEngine.FullScreenMode)FullScreenMode) != UnityEngine.FullScreenMode.Windowed;

        VSync = Mathf.Clamp(VSync, 0, 2);
        ShadowQuality = Mathf.Clamp(ShadowQuality, 0, 2);

        MasterVolume = Mathf.Clamp01(MasterVolume);
        SfxVolume = Mathf.Clamp01(SfxVolume);
        MusicVolume = Mathf.Clamp01(MusicVolume);

        MouseSensitivity = Mathf.Clamp(MouseSensitivity, 0.01f, 2f);
    }

    private static bool IsValidFullScreenMode(int value)
    {
        return value == (int)UnityEngine.FullScreenMode.ExclusiveFullScreen
            || value == (int)UnityEngine.FullScreenMode.FullScreenWindow
            || value == (int)UnityEngine.FullScreenMode.MaximizedWindow
            || value == (int)UnityEngine.FullScreenMode.Windowed;
    }
}
