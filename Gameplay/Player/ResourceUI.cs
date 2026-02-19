using UnityEngine;
using TMPro;
using System.Collections;

public class ResourceUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI _xpText;
    [SerializeField] private TextMeshProUGUI _goldText;
    [SerializeField] private TextMeshProUGUI _essenceText;

    // Cache pro komponentu, abychom nevolali GetComponent při každé změně
    private PlayerProgression _cachedProgression;

    private void Start()
    {
        StartCoroutine(WaitForPlayer());
    }

    private void OnDestroy()
    {
        // Správný úklid eventů
        if (_cachedProgression != null)
        {
            _cachedProgression.OnResourcesChanged -= UpdateUI;
        }
    }

    private IEnumerator WaitForPlayer()
    {
        yield return new WaitUntil(() => PlayerAttributes.LocalInstance != null);

        // Cachujeme referenci
        _cachedProgression = PlayerAttributes.LocalInstance.GetComponent<PlayerProgression>();

        if (_cachedProgression != null)
        {
            _cachedProgression.OnResourcesChanged += UpdateUI;
            UpdateUI(); // Prvotní refresh
        }
    }

    private void UpdateUI()
    {
        if (_cachedProgression == null) return;

        // Varianta 1: String Interpolation (nejčitelnější, alokuje string)
        _xpText.SetText($"{_cachedProgression.CurrentXP.Value:N0} <color=#AAAAAA>XP</color>");

        _goldText.SetText($"{_cachedProgression.Gold.Value:N0} <color=#FFD700>G</color>");

        _essenceText.SetText($"{_cachedProgression.Essence.Value:N0} <color=#00FFFF>E</color>");
    }
}