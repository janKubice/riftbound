using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SettingsMenuUI : MonoBehaviour
{
    [Header("Graphics")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private TMP_Dropdown fullscreenModeDropdown;
    [SerializeField] private TMP_Dropdown vSyncDropdown;
    [SerializeField] private TMP_Dropdown shadowQualityDropdown;

    [Header("Audio")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;

    [Header("Gameplay")]
    [SerializeField] private Toggle cameraShakeToggle;
    [SerializeField] private Toggle damageNumbersToggle;
    [SerializeField] private Slider mouseSensitivitySlider;

    [Header("Optional Buttons")]
    [SerializeField] private Button applyButton;
    [SerializeField] private Button resetToDefaultsButton;

    private readonly List<ResolutionOption> _resolutionOptions = new List<ResolutionOption>();

    private readonly List<FullScreenMode> _fullscreenModes = new List<FullScreenMode>
    {
        FullScreenMode.FullScreenWindow,
        FullScreenMode.ExclusiveFullScreen,
        FullScreenMode.Windowed
    };

    private bool _isBound;
    private bool _isRefreshing;

    private struct ResolutionOption
    {
        public int Width;
        public int Height;
        public int RefreshRate;

        public ResolutionOption(int width, int height, int refreshRate)
        {
            Width = width;
            Height = height;
            RefreshRate = refreshRate;
        }

        public override string ToString()
        {
            if (RefreshRate > 0)
                return $"{Width} x {Height} @ {RefreshRate}Hz";

            return $"{Width} x {Height}";
        }
    }

    private void OnEnable()
    {
        BuildAllDropdownOptions();
        ConfigureSliders();
        Bind();

        if (SettingsManager.Instance != null)
        {
            RefreshUI(SettingsManager.Instance.CurrentSettings);
            SettingsManager.Instance.SettingsChanged += RefreshUI;
        }
        else
        {
            Debug.LogWarning("[SettingsMenuUI] SettingsManager.Instance is null. Settings UI cannot be initialized.");
        }
    }

    private void OnDisable()
    {
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.SettingsChanged -= RefreshUI;
            SettingsManager.Instance.SaveSettingsIfNeeded();
        }

        Unbind();
    }

    // ----------------------------------------------------------------------
    // Dropdown population
    // ----------------------------------------------------------------------

    private void BuildAllDropdownOptions()
    {
        BuildResolutionDropdownOptions();
        BuildFullscreenDropdownOptions();
        BuildVSyncDropdownOptions();
        BuildShadowQualityDropdownOptions();
    }

    private void BuildResolutionDropdownOptions()
    {
        if (resolutionDropdown == null)
            return;

        _resolutionOptions.Clear();

        List<string> labels = new List<string>();
        HashSet<string> usedResolutions = new HashSet<string>();

        Resolution[] resolutions = Screen.resolutions;

        for (int i = 0; i < resolutions.Length; i++)
        {
            Resolution resolution = resolutions[i];

            int width = resolution.width;
            int height = resolution.height;
            int refreshRate = GetRefreshRate(resolution);

            string key = $"{width}x{height}@{refreshRate}";

            if (!usedResolutions.Add(key))
                continue;

            ResolutionOption option = new ResolutionOption(width, height, refreshRate);
            _resolutionOptions.Add(option);
            labels.Add(option.ToString());
        }

        // Fallback, kdyby Screen.resolutions nic nevrátilo.
        if (_resolutionOptions.Count == 0)
        {
            Resolution current = Screen.currentResolution;

            int width = current.width;
            int height = current.height;
            int refreshRate = GetRefreshRate(current);

            ResolutionOption option = new ResolutionOption(width, height, refreshRate);
            _resolutionOptions.Add(option);
            labels.Add(option.ToString());
        }

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(labels);
        resolutionDropdown.RefreshShownValue();
    }

    private void BuildFullscreenDropdownOptions()
    {
        if (fullscreenModeDropdown == null)
            return;

        fullscreenModeDropdown.ClearOptions();
        fullscreenModeDropdown.AddOptions(new List<string>
        {
            "Borderless Fullscreen",
            "Exclusive Fullscreen",
            "Windowed"
        });

        fullscreenModeDropdown.RefreshShownValue();
    }

    private void BuildVSyncDropdownOptions()
    {
        if (vSyncDropdown == null)
            return;

        vSyncDropdown.ClearOptions();
        vSyncDropdown.AddOptions(new List<string>
        {
            "Off",
            "Every V Blank",
            "Every Second V Blank"
        });

        vSyncDropdown.RefreshShownValue();
    }

    private void BuildShadowQualityDropdownOptions()
    {
        if (shadowQualityDropdown == null)
            return;

        shadowQualityDropdown.ClearOptions();
        shadowQualityDropdown.AddOptions(new List<string>
        {
            "Off",
            "Hard Shadows",
            "Soft Shadows"
        });

        shadowQualityDropdown.RefreshShownValue();
    }

    // ----------------------------------------------------------------------
    // Binding
    // ----------------------------------------------------------------------

    private void Bind()
    {
        if (_isBound)
            return;

        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.AddListener(OnResolutionDropdownChanged);

        if (fullscreenModeDropdown != null)
            fullscreenModeDropdown.onValueChanged.AddListener(OnFullscreenModeDropdownChanged);

        if (vSyncDropdown != null)
            vSyncDropdown.onValueChanged.AddListener(OnVSyncDropdownChanged);

        if (shadowQualityDropdown != null)
            shadowQualityDropdown.onValueChanged.AddListener(OnShadowQualityDropdownChanged);

        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);

        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(OnSfxVolumeChanged);

        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);

        if (cameraShakeToggle != null)
            cameraShakeToggle.onValueChanged.AddListener(OnCameraShakeChanged);

        if (damageNumbersToggle != null)
            damageNumbersToggle.onValueChanged.AddListener(OnDamageNumbersChanged);

        if (mouseSensitivitySlider != null)
            mouseSensitivitySlider.onValueChanged.AddListener(OnMouseSensitivityChanged);

        if (applyButton != null)
            applyButton.onClick.AddListener(OnApplyButtonClicked);

        if (resetToDefaultsButton != null)
            resetToDefaultsButton.onClick.AddListener(OnResetToDefaultsClicked);

        _isBound = true;
    }

    private void Unbind()
    {
        if (!_isBound)
            return;

        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.RemoveListener(OnResolutionDropdownChanged);

        if (fullscreenModeDropdown != null)
            fullscreenModeDropdown.onValueChanged.RemoveListener(OnFullscreenModeDropdownChanged);

        if (vSyncDropdown != null)
            vSyncDropdown.onValueChanged.RemoveListener(OnVSyncDropdownChanged);

        if (shadowQualityDropdown != null)
            shadowQualityDropdown.onValueChanged.RemoveListener(OnShadowQualityDropdownChanged);

        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);

        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.RemoveListener(OnSfxVolumeChanged);

        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.RemoveListener(OnMusicVolumeChanged);

        if (cameraShakeToggle != null)
            cameraShakeToggle.onValueChanged.RemoveListener(OnCameraShakeChanged);

        if (damageNumbersToggle != null)
            damageNumbersToggle.onValueChanged.RemoveListener(OnDamageNumbersChanged);

        if (mouseSensitivitySlider != null)
            mouseSensitivitySlider.onValueChanged.RemoveListener(OnMouseSensitivityChanged);

        if (applyButton != null)
            applyButton.onClick.RemoveListener(OnApplyButtonClicked);

        if (resetToDefaultsButton != null)
            resetToDefaultsButton.onClick.RemoveListener(OnResetToDefaultsClicked);

        _isBound = false;
    }

    // ----------------------------------------------------------------------
    // UI refresh
    // ----------------------------------------------------------------------

    private void RefreshUI(SettingsData settings)
    {
        if (settings == null)
            return;

        _isRefreshing = true;

        if (resolutionDropdown != null)
        {
            int resolutionIndex = FindResolutionIndex(settings.ResolutionWidth, settings.ResolutionHeight);
            resolutionDropdown.SetValueWithoutNotify(resolutionIndex);
            resolutionDropdown.RefreshShownValue();
        }

        if (fullscreenModeDropdown != null)
        {
            int fullscreenIndex = FindFullscreenModeIndex((FullScreenMode)settings.FullScreenMode);
            fullscreenModeDropdown.SetValueWithoutNotify(fullscreenIndex);
            fullscreenModeDropdown.RefreshShownValue();
        }

        if (vSyncDropdown != null)
        {
            int vSyncValue = Mathf.Clamp(settings.VSync, 0, 2);
            vSyncDropdown.SetValueWithoutNotify(vSyncValue);
            vSyncDropdown.RefreshShownValue();
        }

        if (shadowQualityDropdown != null)
        {
            int shadowValue = Mathf.Clamp(settings.ShadowQuality, 0, 2);
            shadowQualityDropdown.SetValueWithoutNotify(shadowValue);
            shadowQualityDropdown.RefreshShownValue();
        }

        if (masterVolumeSlider != null)
            masterVolumeSlider.SetValueWithoutNotify(settings.MasterVolume);

        if (sfxVolumeSlider != null)
            sfxVolumeSlider.SetValueWithoutNotify(settings.SfxVolume);

        if (musicVolumeSlider != null)
            musicVolumeSlider.SetValueWithoutNotify(settings.MusicVolume);

        if (cameraShakeToggle != null)
            cameraShakeToggle.SetIsOnWithoutNotify(settings.EnableCameraShake);

        if (damageNumbersToggle != null)
            damageNumbersToggle.SetIsOnWithoutNotify(settings.ShowDamageNumbers);

        if (mouseSensitivitySlider != null)
            mouseSensitivitySlider.SetValueWithoutNotify(settings.MouseSensitivity);

        _isRefreshing = false;
    }

    // ----------------------------------------------------------------------
    // Dropdown callbacks
    // ----------------------------------------------------------------------

    private void OnResolutionDropdownChanged(int index)
    {
        if (_isRefreshing || SettingsManager.Instance == null)
            return;

        if (index < 0 || index >= _resolutionOptions.Count)
            return;

        ResolutionOption option = _resolutionOptions[index];
        SettingsManager.Instance.SetResolution(option.Width, option.Height);
    }

    private void OnFullscreenModeDropdownChanged(int index)
    {
        if (_isRefreshing || SettingsManager.Instance == null)
            return;

        if (index < 0 || index >= _fullscreenModes.Count)
            return;

        SettingsManager.Instance.SetFullScreenMode(_fullscreenModes[index]);
    }

    private void OnVSyncDropdownChanged(int index)
    {
        if (_isRefreshing || SettingsManager.Instance == null)
            return;

        SettingsManager.Instance.SetVSync(index);
    }

    private void OnShadowQualityDropdownChanged(int index)
    {
        if (_isRefreshing || SettingsManager.Instance == null)
            return;

        SettingsManager.Instance.SetShadowQuality(index);
    }

    // ----------------------------------------------------------------------
    // Slider callbacks
    // ----------------------------------------------------------------------

    private void OnMasterVolumeChanged(float value)
    {
        if (_isRefreshing || SettingsManager.Instance == null)
            return;

        SettingsManager.Instance.SetMasterVolume(value);
    }

    private void OnSfxVolumeChanged(float value)
    {
        if (_isRefreshing || SettingsManager.Instance == null)
            return;

        SettingsManager.Instance.SetSfxVolume(value);
    }

    private void OnMusicVolumeChanged(float value)
    {
        if (_isRefreshing || SettingsManager.Instance == null)
            return;

        SettingsManager.Instance.SetMusicVolume(value);
    }

    private void OnMouseSensitivityChanged(float value)
    {
        if (_isRefreshing || SettingsManager.Instance == null)
            return;

        SettingsManager.Instance.SetMouseSensitivity(value);
    }

    // ----------------------------------------------------------------------
    // Toggle callbacks
    // ----------------------------------------------------------------------

    private void OnCameraShakeChanged(bool value)
    {
        if (_isRefreshing || SettingsManager.Instance == null)
            return;

        SettingsManager.Instance.SetCameraShakeEnabled(value);
    }

    private void OnDamageNumbersChanged(bool value)
    {
        if (_isRefreshing || SettingsManager.Instance == null)
            return;

        SettingsManager.Instance.SetDamageNumbersEnabled(value);
    }

    // ----------------------------------------------------------------------
    // Button callbacks
    // ----------------------------------------------------------------------

    private void OnApplyButtonClicked()
    {
        if (SettingsManager.Instance == null)
            return;

        SettingsManager.Instance.SaveSettingsIfNeeded();
    }

    private void OnResetToDefaultsClicked()
    {
        if (SettingsManager.Instance == null)
            return;

        SettingsManager.Instance.ResetToDefaults(false);
        BuildAllDropdownOptions();
        RefreshUI(SettingsManager.Instance.CurrentSettings);
    }

    // ----------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------

    private void ConfigureSliders()
    {
        ConfigureVolumeSlider(masterVolumeSlider);
        ConfigureVolumeSlider(sfxVolumeSlider);
        ConfigureVolumeSlider(musicVolumeSlider);
        ConfigureMouseSensitivitySlider(mouseSensitivitySlider);
    }

    private static void ConfigureVolumeSlider(Slider slider)
    {
        if (slider == null)
            return;

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
    }

    private static void ConfigureMouseSensitivitySlider(Slider slider)
    {
        if (slider == null)
            return;

        slider.minValue = 0.01f;
        slider.maxValue = 2f;
        slider.wholeNumbers = false;
    }

    private int FindResolutionIndex(int width, int height)
    {
        if (_resolutionOptions.Count == 0)
            return 0;

        int bestIndex = 0;
        int bestScore = int.MaxValue;

        for (int i = 0; i < _resolutionOptions.Count; i++)
        {
            ResolutionOption option = _resolutionOptions[i];

            if (option.Width == width && option.Height == height)
                return i;

            int score = Mathf.Abs(option.Width - width) + Mathf.Abs(option.Height - height);

            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private int FindFullscreenModeIndex(FullScreenMode mode)
    {
        for (int i = 0; i < _fullscreenModes.Count; i++)
        {
            if (_fullscreenModes[i] == mode)
                return i;
        }

        return 0;
    }

    private static int GetRefreshRate(Resolution resolution)
    {
#if UNITY_2022_2_OR_NEWER
        return Mathf.RoundToInt((float)resolution.refreshRateRatio.value);
#else
        return resolution.refreshRate;
#endif
    }
}