using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public abstract class WorldPOIBase : NetworkBehaviour, IInteractable
{
    [Header("Identity")]
    [SerializeField] private string _displayName = "World Event";
    [SerializeField] private WorldPOICategory _category = WorldPOICategory.Shrine;

    [Header("Interaction")]
    [SerializeField] public string _dormantPrompt = "Dormant Object";
    [SerializeField] public string _activePrompt = "E - Activate";
    [SerializeField] public string _completedPrompt = "Completed";

    [Header("Visual States")]
    [SerializeField] private GameObject[] _dormantObjects;
    [SerializeField] private GameObject[] _activeObjects;
    [SerializeField] private GameObject[] _completedObjects;

    [Header("Optional Feedback")]
    [SerializeField] private InteractionFeedback _activationFeedback;
    [SerializeField] private InteractionFeedback _completionFeedback;
    [SerializeField] private MoodEmissiveObject _activationMoodemmisive;
    [SerializeField] private MoodEmissiveObject _completionMoodemmisive;
    [SerializeField] private MoodParticleObject _activationParticles;
    [SerializeField] private MoodParticleObject _completionParticles;

    private readonly NetworkVariable<byte> _state = new NetworkVariable<byte>(
        (byte)WorldPOIState.Dormant,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Coroutine _activeTimeoutRoutine;

    public event Action<WorldPOIBase> ServerCompleted;

    public string DisplayName => _displayName;
    public WorldPOICategory Category => _category;
    public WorldPOIState State => (WorldPOIState)_state.Value;

    public bool IsDormant => State == WorldPOIState.Dormant;
    public bool IsActive => State == WorldPOIState.Active;
    public bool IsCompleted => State == WorldPOIState.Completed;

    public virtual string InteractionPrompt
    {
        get
        {
            return State switch
            {
                WorldPOIState.Active => _activePrompt,
                WorldPOIState.Completed => _completedPrompt,
                WorldPOIState.Disabled => "",
                _ => _dormantPrompt
            };
        }
    }

    public override void OnNetworkSpawn()
    {
        _state.OnValueChanged += OnStateChanged;
        ApplyVisualState(State);
    }

    public override void OnNetworkDespawn()
    {
        _state.OnValueChanged -= OnStateChanged;

        if (_activeTimeoutRoutine != null)
        {
            StopCoroutine(_activeTimeoutRoutine);
            _activeTimeoutRoutine = null;
        }
    }

    public void Interact(NetworkObject interactor)
    {
        if (!IsServer)
            return;

        if (State != WorldPOIState.Active)
            return;

        OnInteractedServer(interactor);
    }

    public void SetDormantServer()
    {
        if (!IsServer)
            return;

        SetStateServer(WorldPOIState.Dormant);
    }

    public void ActivateServer(float activeDurationSeconds)
    {
        if (!IsServer)
            return;

        if (State == WorldPOIState.Completed || State == WorldPOIState.Disabled)
            return;

        SetStateServer(WorldPOIState.Active);

        OnActivatedServer();

        PlayActivationClientRpc();

        if (_activeTimeoutRoutine != null)
            StopCoroutine(_activeTimeoutRoutine);

        if (activeDurationSeconds > 0f)
            _activeTimeoutRoutine = StartCoroutine(ActiveTimeoutRoutine(activeDurationSeconds));
    }

    public void CompleteServer()
    {
        if (!IsServer)
            return;

        if (State == WorldPOIState.Completed)
            return;

        SetStateServer(WorldPOIState.Completed);

        if (_activeTimeoutRoutine != null)
        {
            StopCoroutine(_activeTimeoutRoutine);
            _activeTimeoutRoutine = null;
        }

        OnCompletedServer();

        PlayCompletionClientRpc();

        ServerCompleted?.Invoke(this);
    }

    public void DisableServer()
    {
        if (!IsServer)
            return;

        SetStateServer(WorldPOIState.Disabled);
    }

    protected virtual void OnInteractedServer(NetworkObject interactor)
    {
        CompleteServer();
    }

    protected virtual void OnActivatedServer()
    {
    }

    protected virtual void OnCompletedServer()
    {
    }

    protected virtual void OnExpiredServer()
    {
        SetStateServer(WorldPOIState.Dormant);
    }

    private IEnumerator ActiveTimeoutRoutine(float seconds)
    {
        yield return new WaitForSeconds(seconds);

        if (!IsServer)
            yield break;

        if (State == WorldPOIState.Active)
            OnExpiredServer();

        _activeTimeoutRoutine = null;
    }

    private void SetStateServer(WorldPOIState newState)
    {
        _state.Value = (byte)newState;
        ApplyVisualState(newState);
    }

    private void OnStateChanged(byte oldValue, byte newValue)
    {
        ApplyVisualState((WorldPOIState)newValue);
    }

    private void ApplyVisualState(WorldPOIState state)
    {
        SetObjectsActive(_dormantObjects, state == WorldPOIState.Dormant);
        SetObjectsActive(_activeObjects, state == WorldPOIState.Active);
        SetObjectsActive(_completedObjects, state == WorldPOIState.Completed);

        if (_activationMoodemmisive != null)
            _activationMoodemmisive.enabled = state == WorldPOIState.Active;

        if (_completionMoodemmisive != null)
            _completionMoodemmisive.enabled = state == WorldPOIState.Completed;

        if (_activationParticles != null)
        {
            if (state == WorldPOIState.Active)
                _activationParticles.EnableEffect();
            else
                _activationParticles.DisableEffect();
        }

        if (_completionParticles != null)
        {
            if (state == WorldPOIState.Completed)
                _completionParticles.EnableEffect();
            else
                _completionParticles.DisableEffect();
        }

        OnVisualStateApplied(state);
    }

    protected virtual void OnVisualStateApplied(WorldPOIState state)
    {
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

    [ClientRpc]
    private void PlayActivationClientRpc()
    {
        if (_activationFeedback != null)
            _activationFeedback.PlayForAllClients();

        if (_activationMoodemmisive != null)
            _activationMoodemmisive.enabled = true;
        
        if (_activationParticles != null)
            _activationParticles.EnableEffect();
    }

    [ClientRpc]
    private void PlayCompletionClientRpc()
    {
        if (_completionFeedback != null)
            _completionFeedback.PlayForAllClients();

        if (_completionMoodemmisive != null)
            _completionMoodemmisive.enabled = true;

        if (_activationMoodemmisive != null)
            _activationMoodemmisive.enabled = false;
        
        if (_completionParticles != null)
            _completionParticles.EnableEffect();

        if (_activationParticles != null)
            _activationParticles.DisableEffect();
    }
}