using UnityEngine;
using TMPro;

public class ResourceUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _xpText;
    [SerializeField] private TextMeshProUGUI _goldText;
    [SerializeField] private TextMeshProUGUI _essenceText;

    private void Start()
    {
        // Čekáme na LocalInstance hráče (stejný pattern jako u PlayerHUD)
        StartCoroutine(WaitForPlayer());
    }

    private System.Collections.IEnumerator WaitForPlayer()
    {
        yield return new WaitUntil(() => PlayerAttributes.LocalInstance != null);
        
        var progression = PlayerAttributes.LocalInstance.GetComponent<PlayerProgression>();
        if (progression != null)
        {
            progression.OnResourcesChanged += UpdateUI;
            UpdateUI(); // Prvotní refresh
        }
    }

    private void UpdateUI()
    {
        if (PlayerAttributes.LocalInstance == null) return;
        var prog = PlayerAttributes.LocalInstance.GetComponent<PlayerProgression>();
        
        _xpText.text = $"{prog.CurrentXP.Value} XP";
        _goldText.text = $"{prog.Gold.Value} G";
        _essenceText.text = $"{prog.Essence.Value} E";
    }
}