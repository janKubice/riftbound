using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TimeEchoMaterialDriver : MonoBehaviour
{
    [Serializable]
    private sealed class RendererTarget
    {
        public Renderer renderer;
        public int materialSlotCount;
    }

    [Header("Renderer Search")]
    [SerializeField] private bool autoCollectRenderers = true;

    [Tooltip("Když je zapnuté, script bude nastavovat pouze materiály se shaderem obsahujícím tento text.")]
    [SerializeField] private bool filterByShaderName = true;

    [SerializeField] private string shaderNameContains = "TimeEchoHologram";

    [SerializeField] private List<RendererTarget> targets = new();

    [Header("Timing")]
    [SerializeField] private float lifetime = 4f;
    [SerializeField] private float tickInterval = 1f;

    [Header("Fade")]
    [SerializeField] private float fadeInPercent = 0.08f;
    [SerializeField] private float fadeOutPercent = 0.30f;

    [Header("Pulse")]
    [SerializeField] private float tickFlashDecay = 7f;
    [SerializeField] private float tickFlashStrength = 1.25f;

    private float startedAt;
    private MaterialPropertyBlock block;

    private static readonly int LifeFadeId = Shader.PropertyToID("_LifeFade");
    private static readonly int PulseBoostId = Shader.PropertyToID("_PulseBoost");

    private void Awake()
    {
        block = new MaterialPropertyBlock();

        if (autoCollectRenderers)
        {
            RefreshTargets();
        }
    }

    private void OnEnable()
    {
        Play(lifetime, tickInterval);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying && autoCollectRenderers)
        {
            RefreshTargets();
        }
    }
#endif

    public void Play(float newLifetime, float newTickInterval)
    {
        lifetime = Mathf.Max(0.01f, newLifetime);
        tickInterval = Mathf.Max(0.01f, newTickInterval);

        startedAt = Time.time;

        ApplyProperties(0f, 0f);
    }

    public void RefreshTargets()
    {
        targets.Clear();

        Renderer[] childRenderers = GetComponentsInChildren<Renderer>(true);

        foreach (Renderer childRenderer in childRenderers)
        {
            if (childRenderer == null)
                continue;

            Material[] sharedMaterials = childRenderer.sharedMaterials;

            int matchingSlots = 0;

            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                Material material = sharedMaterials[i];

                if (material == null || material.shader == null)
                    continue;

                if (!filterByShaderName || material.shader.name.Contains(shaderNameContains))
                {
                    matchingSlots++;
                }
            }

            if (matchingSlots <= 0)
                continue;

            targets.Add(new RendererTarget
            {
                renderer = childRenderer,
                materialSlotCount = sharedMaterials.Length
            });
        }
    }

    private void Update()
    {
        float elapsed = Time.time - startedAt;
        float normalizedLife = Mathf.Clamp01(elapsed / lifetime);

        float fadeIn = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(0f, fadeInPercent, normalizedLife)
        );

        float fadeOut = 1f - Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(1f - fadeOutPercent, 1f, normalizedLife)
        );

        float lifeFade = fadeIn * fadeOut;

        float tickPhase = (elapsed % tickInterval) / tickInterval;
        float pulseBoost = Mathf.Exp(-tickPhase * tickFlashDecay) * tickFlashStrength;

        ApplyProperties(lifeFade, pulseBoost);
    }

    private void ApplyProperties(float lifeFade, float pulseBoost)
    {
        if (block == null)
        {
            block = new MaterialPropertyBlock();
        }

        for (int i = targets.Count - 1; i >= 0; i--)
        {
            RendererTarget target = targets[i];

            if (target == null || target.renderer == null)
            {
                targets.RemoveAt(i);
                continue;
            }

            ApplyToRenderer(target.renderer, target.materialSlotCount, lifeFade, pulseBoost);
        }
    }

    private void ApplyToRenderer(Renderer renderer, int materialSlotCount, float lifeFade, float pulseBoost)
    {
        Material[] sharedMaterials = renderer.sharedMaterials;

        int slotCount = Mathf.Min(materialSlotCount, sharedMaterials.Length);

        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            Material material = sharedMaterials[slotIndex];

            if (material == null || material.shader == null)
                continue;

            if (filterByShaderName && !material.shader.name.Contains(shaderNameContains))
                continue;

            renderer.GetPropertyBlock(block, slotIndex);

            block.SetFloat(LifeFadeId, lifeFade);
            block.SetFloat(PulseBoostId, pulseBoost);

            renderer.SetPropertyBlock(block, slotIndex);
        }
    }
}