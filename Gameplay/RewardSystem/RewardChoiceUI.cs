using UnityEngine;
using TMPro;

public class RewardChoiceUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject _panel;
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private Transform _cardContainer;
    [SerializeField] private RewardChoiceCardUI _cardPrefab;

    private RewardChoiceManager _manager;
    private bool _pausedGame;

    private void Awake()
    {
        if (_panel != null)
            _panel.SetActive(false);
    }

    public void Open(
        RewardChoiceManager manager,
        string reason,
        int[] definitionIndices,
        int[] rarities,
        int[] amounts,
        float[] statValues,
        bool pauseGame)
    {
        _manager = manager;

        if (_panel != null)
            _panel.SetActive(true);

        if (_titleText != null)
            _titleText.text = string.IsNullOrWhiteSpace(reason) ? "Choose Reward" : reason;

        ClearCards();

        for (int i = 0; i < definitionIndices.Length; i++)
        {
            RewardChoiceDefinition def = manager.GetDefinition(definitionIndices[i]);

            if (def == null)
                continue;

            ItemRarity rarity = (ItemRarity)rarities[i];

            RewardChoiceCardUI card = Instantiate(_cardPrefab, _cardContainer);
            card.Setup(
                i,
                def,
                rarity,
                amounts[i],
                statValues[i],
                OnCardSelected
            );
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        _pausedGame = pauseGame;

        if (_pausedGame)
            Time.timeScale = 0f;
    }

    private void OnCardSelected(int index)
    {
        if (_manager == null)
            return;

        _manager.SelectChoice(index);
    }

    public void CloseFromServerConfirmation()
    {
        if (_pausedGame)
        {
            Time.timeScale = 1f;
            _pausedGame = false;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (_panel != null)
            _panel.SetActive(false);

        ClearCards();
    }

    private void ClearCards()
    {
        if (_cardContainer == null)
            return;

        for (int i = _cardContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(_cardContainer.GetChild(i).gameObject);
        }
    }
}