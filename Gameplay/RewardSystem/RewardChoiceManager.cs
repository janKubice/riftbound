using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class RewardChoiceManager : NetworkBehaviour
{
    [Header("Rewards")]
    [SerializeField] private RewardChoicePool _rewardPool;
    [SerializeField] private int _choicesCount = 3;

    [Header("Pause")]
    [SerializeField] private bool _pauseGameForLocalChoice = true;

    private PlayerProgression _progression;
    private WeaponManager _weaponManager;

    private readonly Dictionary<ulong, List<GeneratedRewardChoice>> _pendingChoicesByClient = new Dictionary<ulong, List<GeneratedRewardChoice>>();

    private void Awake()
    {
        _progression = GetComponent<PlayerProgression>();
        _weaponManager = GetComponent<WeaponManager>();
    }

    public override void OnNetworkSpawn()
    {
        if (_progression == null)
            _progression = GetComponent<PlayerProgression>();

        if (_weaponManager == null)
            _weaponManager = GetComponent<WeaponManager>();
    }

    public RewardChoiceDefinition GetDefinition(int index)
    {
        if (_rewardPool == null)
            return null;

        return _rewardPool.GetDefinition(index);
    }

    public void OfferRewardChoicesServer(string reason)
    {
        if (!IsServer)
            return;

        if (_rewardPool == null)
            return;

        float runMinute = DirectorSpawner.Instance != null
            ? DirectorSpawner.Instance.GetRunTimeMinutes()
            : 0f;

        List<GeneratedRewardChoice> choices = _rewardPool.GenerateChoices(
            _progression,
            _weaponManager,
            runMinute,
            _choicesCount
        );

        if (choices.Count == 0)
            return;

        _pendingChoicesByClient[OwnerClientId] = choices;

        int[] definitionIndices = new int[choices.Count];
        int[] rarities = new int[choices.Count];
        int[] amounts = new int[choices.Count];
        float[] statValues = new float[choices.Count];

        for (int i = 0; i < choices.Count; i++)
        {
            definitionIndices[i] = choices[i].DefinitionIndex;
            rarities[i] = (int)choices[i].Rarity;
            amounts[i] = choices[i].Amount;
            statValues[i] = choices[i].StatValue;
        }

        ClientRpcParams rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };

        ShowRewardChoicesClientRpc(
            reason,
            definitionIndices,
            rarities,
            amounts,
            statValues,
            _pauseGameForLocalChoice,
            rpcParams
        );
    }

    [ClientRpc]
    private void ShowRewardChoicesClientRpc(
        string reason,
        int[] definitionIndices,
        int[] rarities,
        int[] amounts,
        float[] statValues,
        bool pauseGame,
        ClientRpcParams rpcParams = default)
    {
        if (!IsOwner)
            return;

        RewardChoiceUI ui = FindFirstObjectByType<RewardChoiceUI>(FindObjectsInactive.Include);

        if (ui == null)
        {
            Debug.LogError("[RewardChoiceManager] RewardChoiceUI not found in scene.");
            return;
        }

        ui.Open(
            this,
            reason,
            definitionIndices,
            rarities,
            amounts,
            statValues,
            pauseGame
        );
    }

    public void SelectChoice(int choiceIndex)
    {
        if (!IsOwner)
            return;

        SelectChoiceServerRpc(choiceIndex);
    }

    [ServerRpc]
    private void SelectChoiceServerRpc(int choiceIndex)
    {
        if (!_pendingChoicesByClient.TryGetValue(OwnerClientId, out List<GeneratedRewardChoice> choices))
            return;

        if (choiceIndex < 0 || choiceIndex >= choices.Count)
            return;

        GeneratedRewardChoice selected = choices[choiceIndex];

        ApplyRewardServer(selected);

        _pendingChoicesByClient.Remove(OwnerClientId);

        RewardChoiceResolvedClientRpc();
    }

    private void ApplyRewardServer(GeneratedRewardChoice choice)
    {
        RewardChoiceDefinition def = _rewardPool.GetDefinition(choice.DefinitionIndex);

        if (def == null)
            return;

        switch (def.Type)
        {
            case RewardChoiceType.Gold:
                _progression.AddGoldRewardServer(choice.Amount);
                break;

            case RewardChoiceType.XP:
                _progression.AddXPRewardServer(choice.Amount);
                break;

            case RewardChoiceType.Heal:
                _progression.HealRewardServer(choice.Amount);
                break;

            case RewardChoiceType.StatUpgrade:
                if (def.StatUpgrade != null)
                    _progression.GrantRewardUpgradeServer(def.StatUpgrade, choice.StatValue);
                break;

            case RewardChoiceType.WeaponHitEffect:
                if (def.WeaponHitEffect != null && _weaponManager != null)
                    _weaponManager.AddRuntimeEffect(def.WeaponHitEffect);
                break;
        }

        Debug.Log($"[RewardChoiceManager] Applied reward: {def.RewardName} ({choice.Rarity})");
    }

    [ClientRpc]
    private void RewardChoiceResolvedClientRpc()
    {
        if (!IsOwner)
            return;

        RewardChoiceUI ui = FindFirstObjectByType<RewardChoiceUI>(FindObjectsInactive.Include);

        if (ui != null)
            ui.CloseFromServerConfirmation();
    }
}