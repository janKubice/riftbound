using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public enum InteractionFeedbackMode
{
    LocalOnly,
    InteractorOnly,
    AllClients
}

public class InteractionFeedback : NetworkBehaviour
{
    [Header("Effect Objects")]
    [Tooltip("Může obsahovat buď objekty ze scény, nebo prefab assety z Project okna.")]
    [SerializeField] private GameObject[] _effectObjects;

    [Header("Prefab Handling")]
    [Tooltip("Pokud je v Effect Objects prefab asset, script ho automaticky vytvoří ve scéně.")]
    [SerializeField] private bool _instantiatePrefabEffects = true;

    [Tooltip("Pokud je zapnuto, instancovaný prefab se vytvoří jako child tohoto objektu.")]
    [SerializeField] private bool _parentInstantiatedEffectsToThis = true;

    [Tooltip("Volitelný parent pro instancované efekty. Pokud je prázdný, použije se transform tohoto objektu.")]
    [SerializeField] private Transform _effectSpawnParent;

    [Tooltip("Pokud je zapnuto, prefab efekt se instancuje jen jednou a pak se znovu používá.")]
    [SerializeField] private bool _reuseInstantiatedPrefabEffects = true;

    [Tooltip("Pokud je vypnuto reuse, instancované efekty se po čase smažou.")]
    [SerializeField] private bool _destroyTemporaryPrefabInstances = true;

    [SerializeField] private float _temporaryInstanceLifetime = 4f;

    [Header("Particles")]
    [SerializeField] private bool _triggerMoodParticles = true;
    [SerializeField] private bool _emitParticleSystems = true;
    [SerializeField] private int _particleEmitCount = 20;

    [Header("Temporary Activation")]
    [SerializeField] private bool _activateObjectsTemporarily = false;
    [SerializeField] private float _activeDuration = 1.5f;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _oneShotClip;
    [Range(0f, 1f)]
    [SerializeField] private float _volume = 1f;

    [Header("Animation")]
    [SerializeField] private Animator _animator;
    [SerializeField] private string _triggerName = "Interact";

    [Header("Debug")]
    [SerializeField] private bool _logFeedback = false;

    private readonly Dictionary<GameObject, GameObject> _runtimePrefabInstances = new();
    private Coroutine _activationRoutine;

    public void PlayLocal()
    {
        PlayFeedbackInternal();
    }

    public void PlayForAllClients()
    {
        if (!IsServer)
        {
            PlayFeedbackInternal();
            return;
        }

        PlayFeedbackClientRpc();
    }

    public void PlayForInteractor(NetworkObject interactor)
    {
        if (interactor == null)
            return;

        if (!IsServer)
        {
            PlayFeedbackInternal();
            return;
        }

        ulong clientId = interactor.OwnerClientId;

        ClientRpcParams rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };

        PlayFeedbackClientRpc(rpcParams);
    }

    public void PlayFromServer(NetworkObject interactor, InteractionFeedbackMode mode)
    {
        switch (mode)
        {
            case InteractionFeedbackMode.LocalOnly:
                PlayLocal();
                break;

            case InteractionFeedbackMode.InteractorOnly:
                PlayForInteractor(interactor);
                break;

            case InteractionFeedbackMode.AllClients:
                PlayForAllClients();
                break;
        }
    }

    [ClientRpc]
    private void PlayFeedbackClientRpc(ClientRpcParams rpcParams = default)
    {
        PlayFeedbackInternal();
    }

    private void PlayFeedbackInternal()
    {
        if (_logFeedback)
            Debug.Log($"InteractionFeedback played on {name}", this);

        List<GameObject> resolvedObjects = ResolveEffectObjects();

        TriggerMoodParticleObjects(resolvedObjects);
        EmitParticles(resolvedObjects);
        PlayAudio();
        TriggerAnimation();
        ActivateObjectsTemporarily(resolvedObjects);
    }

    private List<GameObject> ResolveEffectObjects()
    {
        List<GameObject> resolvedObjects = new List<GameObject>();

        if (_effectObjects == null)
            return resolvedObjects;

        for (int i = 0; i < _effectObjects.Length; i++)
        {
            GameObject effectObject = _effectObjects[i];

            if (effectObject == null)
                continue;

            if (IsSceneInstance(effectObject))
            {
                resolvedObjects.Add(effectObject);
                continue;
            }

            if (!_instantiatePrefabEffects)
            {
                Debug.LogWarning(
                    $"InteractionFeedback '{name}' má v Effect Objects prefab asset '{effectObject.name}', " +
                    "ale Instantiate Prefab Effects je vypnuté.",
                    this
                );

                continue;
            }

            GameObject runtimeInstance = GetOrCreateRuntimePrefabInstance(effectObject);

            if (runtimeInstance != null)
                resolvedObjects.Add(runtimeInstance);
        }

        return resolvedObjects;
    }

    private GameObject GetOrCreateRuntimePrefabInstance(GameObject prefab)
    {
        if (_reuseInstantiatedPrefabEffects &&
            _runtimePrefabInstances.TryGetValue(prefab, out GameObject existingInstance) &&
            existingInstance != null)
        {
            return existingInstance;
        }

        Transform parent = null;

        if (_parentInstantiatedEffectsToThis)
            parent = _effectSpawnParent != null ? _effectSpawnParent : transform;

        GameObject instance = Instantiate(prefab, parent);
        instance.name = $"{prefab.name}_RuntimeFeedback";

        if (parent != null)
        {
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
        }
        else
        {
            instance.transform.SetPositionAndRotation(transform.position, transform.rotation);
        }

        instance.SetActive(true);

        if (_reuseInstantiatedPrefabEffects)
        {
            _runtimePrefabInstances[prefab] = instance;
        }
        else if (_destroyTemporaryPrefabInstances)
        {
            Destroy(instance, Mathf.Max(0.1f, _temporaryInstanceLifetime));
        }

        return instance;
    }

    private static bool IsSceneInstance(GameObject obj)
    {
        return obj.scene.IsValid() && obj.scene.isLoaded;
    }

    private void TriggerMoodParticleObjects(List<GameObject> effectObjects)
    {
        if (!_triggerMoodParticles)
            return;

        for (int i = 0; i < effectObjects.Count; i++)
        {
            GameObject obj = effectObjects[i];

            if (obj == null)
                continue;

            if (!obj.activeSelf)
                obj.SetActive(true);

            MoodParticleObject[] moodParticles = obj.GetComponentsInChildren<MoodParticleObject>(true);

            for (int j = 0; j < moodParticles.Length; j++)
            {
                if (moodParticles[j] != null)
                    moodParticles[j].TriggerBurst();
            }
        }
    }

    private void EmitParticles(List<GameObject> effectObjects)
    {
        if (!_emitParticleSystems)
            return;

        int count = Mathf.Max(0, _particleEmitCount);

        for (int i = 0; i < effectObjects.Count; i++)
        {
            GameObject obj = effectObjects[i];

            if (obj == null)
                continue;

            if (!obj.activeSelf)
                obj.SetActive(true);

            ParticleSystem[] particleSystems = obj.GetComponentsInChildren<ParticleSystem>(true);

            for (int j = 0; j < particleSystems.Length; j++)
            {
                ParticleSystem ps = particleSystems[j];

                if (ps == null)
                    continue;

                if (!ps.gameObject.activeSelf)
                    ps.gameObject.SetActive(true);

                if (!ps.isPlaying)
                    ps.Play(true);

                ps.Emit(count);
            }
        }
    }

    private void PlayAudio()
    {
        if (_audioSource == null || _oneShotClip == null)
            return;

        _audioSource.PlayOneShot(_oneShotClip, _volume);
    }

    private void TriggerAnimation()
    {
        if (_animator == null)
            return;

        if (string.IsNullOrWhiteSpace(_triggerName))
            return;

        _animator.SetTrigger(_triggerName);
    }

    private void ActivateObjectsTemporarily(List<GameObject> effectObjects)
    {
        if (!_activateObjectsTemporarily)
            return;

        if (_activationRoutine != null)
            StopCoroutine(_activationRoutine);

        _activationRoutine = StartCoroutine(ActivateRoutine(effectObjects));
    }

    private IEnumerator ActivateRoutine(List<GameObject> effectObjects)
    {
        for (int i = 0; i < effectObjects.Count; i++)
        {
            if (effectObjects[i] != null)
                effectObjects[i].SetActive(true);
        }

        yield return new WaitForSeconds(_activeDuration);

        for (int i = 0; i < effectObjects.Count; i++)
        {
            if (effectObjects[i] != null)
                effectObjects[i].SetActive(false);
        }

        _activationRoutine = null;
    }

    private void OnDestroy()
    {
        foreach (KeyValuePair<GameObject, GameObject> pair in _runtimePrefabInstances)
        {
            if (pair.Value != null)
                Destroy(pair.Value);
        }

        _runtimePrefabInstances.Clear();
    }
}