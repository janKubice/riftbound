using System;
using UnityEngine;

public enum ManaShrineVisualPhase
{
    Dormant,
    Active,
    Channeling,
    Completed
}

[Serializable]
public class ManaShrineVisualPhaseSettings
{
    public ManaShrineVisualPhase Phase;

    [Header("Color")]
    [ColorUsage(true, true)]
    public Color Color = Color.cyan;

    [Header("Mood")]
    [Min(0f)] public float LightIntensity = 1f;
    [Min(0f)] public float ParticleIntensity = 1f;

    [Range(0f, 1f)]
    public float ParticleActivation = 1f;

    [Header("Objects")]
    public GameObject[] EnableObjects;
    public GameObject[] DisableObjects;

    [Header("Transition")]
    public bool TriggerParticleBurstOnEnter = false;
    public string AnimatorTrigger;
}

public class ShrineVisualController : MonoBehaviour
{
    [Header("Mood References")]
    [SerializeField] private MoodLightObject[] _moodLights;
    [SerializeField] private MoodParticleObject[] _moodParticles;

    [Header("Direct Renderers")]
    [SerializeField] private Renderer[] _emissiveRenderers;
    [SerializeField] private string _emissionColorProperty = "_EmissionColor";
    [SerializeField] private string _progressProperty = "_Progress";

    [Header("Animator")]
    [SerializeField] private Animator _animator;

    [Header("Phases")]
    [SerializeField] private ManaShrineVisualPhaseSettings _dormant;
    [SerializeField] private ManaShrineVisualPhaseSettings _active;
    [SerializeField] private ManaShrineVisualPhaseSettings _channeling;
    [SerializeField] private ManaShrineVisualPhaseSettings _completed;

    [Header("Channel Progress Feel")]
    [SerializeField] private AnimationCurve _channelIntensityByProgress = new AnimationCurve(
        new Keyframe(0f, 0.7f),
        new Keyframe(0.65f, 1.1f),
        new Keyframe(1f, 2.0f)
    );

    [SerializeField] private AnimationCurve _channelActivationByProgress = new AnimationCurve(
        new Keyframe(0f, 0.35f),
        new Keyframe(1f, 1f)
    );

    private MaterialPropertyBlock _propertyBlock;
    private ManaShrineVisualPhase _currentPhase;
    private bool _hasPhase;
    private float _progress01;

    private void Awake()
    {
        _propertyBlock = new MaterialPropertyBlock();
    }

    private void Reset()
    {
        _moodLights = GetComponentsInChildren<MoodLightObject>(true);
        _moodParticles = GetComponentsInChildren<MoodParticleObject>(true);
        _emissiveRenderers = GetComponentsInChildren<Renderer>(true);
        _animator = GetComponentInChildren<Animator>(true);
    }

    public void ApplyPhase(ManaShrineVisualPhase phase, bool force = false)
    {
        if (!force && _hasPhase && _currentPhase == phase)
            return;

        ManaShrineVisualPhaseSettings settings = GetSettings(phase);

        if (settings == null)
            return;

        bool enteredNewPhase = !_hasPhase || _currentPhase != phase;

        _currentPhase = phase;
        _hasPhase = true;

        SetObjectsActive(settings.EnableObjects, true);
        SetObjectsActive(settings.DisableObjects, false);

        ApplyMood(settings);
        ApplyRenderers(settings.Color, _progress01);

        if (enteredNewPhase)
        {
            if (settings.TriggerParticleBurstOnEnter)
                TriggerParticleBurst();

            if (_animator != null && !string.IsNullOrWhiteSpace(settings.AnimatorTrigger))
                _animator.SetTrigger(settings.AnimatorTrigger);
        }
    }

    public void SetProgress(float progress01)
    {
        _progress01 = Mathf.Clamp01(progress01);

        if (!_hasPhase)
            return;

        ManaShrineVisualPhaseSettings settings = GetSettings(_currentPhase);

        if (settings == null)
            return;

        if (_currentPhase == ManaShrineVisualPhase.Channeling)
        {
            float intensityMultiplier = Mathf.Max(0f, _channelIntensityByProgress.Evaluate(_progress01));
            float activation = Mathf.Clamp01(_channelActivationByProgress.Evaluate(_progress01));

            ApplyMood(settings, intensityMultiplier, activation);
        }

        ApplyRenderers(settings.Color, _progress01);
    }

    public void PlayChannelStarted()
    {
        TriggerParticleBurst();
    }

    public void PlayCompleted()
    {
        TriggerParticleBurst();
    }

    private void ApplyMood(
        ManaShrineVisualPhaseSettings settings,
        float intensityMultiplier = 1f,
        float? forcedActivation = null
    )
    {
        float particleActivation = forcedActivation ?? settings.ParticleActivation;

        if (_moodLights != null)
        {
            for (int i = 0; i < _moodLights.Length; i++)
            {
                MoodLightObject moodLight = _moodLights[i];

                if (moodLight == null)
                    continue;

                moodLight.SetRuntimeColor(settings.Color);
                moodLight.SetExternalIntensity(settings.LightIntensity * intensityMultiplier);
            }
        }

        if (_moodParticles != null)
        {
            for (int i = 0; i < _moodParticles.Length; i++)
            {
                MoodParticleObject moodParticle = _moodParticles[i];

                if (moodParticle == null)
                    continue;

                moodParticle.SetColorOverride(settings.Color);
                moodParticle.SetExternalIntensity(settings.ParticleIntensity * intensityMultiplier);
                moodParticle.SetActivation(particleActivation);

                if (settings.ParticleIntensity <= 0f || particleActivation <= 0.001f)
                    moodParticle.DisableEffect(false);
                else
                    moodParticle.EnableEffect();
            }
        }
    }

    private void ApplyRenderers(Color emissiveColor, float progress01)
    {
        if (_emissiveRenderers == null)
            return;

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        for (int i = 0; i < _emissiveRenderers.Length; i++)
        {
            Renderer renderer = _emissiveRenderers[i];

            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(_propertyBlock);

            _propertyBlock.SetColor(_emissionColorProperty, emissiveColor);
            _propertyBlock.SetFloat(_progressProperty, progress01);

            renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    private void TriggerParticleBurst()
    {
        if (_moodParticles == null)
            return;

        for (int i = 0; i < _moodParticles.Length; i++)
        {
            if (_moodParticles[i] != null)
                _moodParticles[i].TriggerBurst();
        }
    }

    private ManaShrineVisualPhaseSettings GetSettings(ManaShrineVisualPhase phase)
    {
        return phase switch
        {
            ManaShrineVisualPhase.Dormant => _dormant,
            ManaShrineVisualPhase.Active => _active,
            ManaShrineVisualPhase.Channeling => _channeling,
            ManaShrineVisualPhase.Completed => _completed,
            _ => null
        };
    }

    private static void SetObjectsActive(GameObject[] objects, bool active)
    {
        if (objects == null)
            return;

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
                objects[i].SetActive(active);
        }
    }
}