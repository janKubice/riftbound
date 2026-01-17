using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Unity.Netcode;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject _dialoguePanel; // Celý panel dialogu
    [SerializeField] private TextMeshProUGUI _npcNameText; // Jméno NPC
    [SerializeField] private TextMeshProUGUI _dialogueText; // Hlavní text
    [SerializeField] private Transform _optionsContainer; // Kam se generují tlačítka
    [SerializeField] private GameObject _optionButtonPrefab; // Prefab tlačítka

    private NPCInteractable _currentNpc;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        _dialoguePanel.SetActive(false);
    }

    /// <summary>
    /// Tuto metodu volá NPCInteractable přes ClientRpc
    /// </summary>
    public void StartDialogue(DialogueNode rootNode, NPCInteractable npc)
    {
        _currentNpc = npc;
        _dialoguePanel.SetActive(true);

        // Nastavíme jméno NPC (přidáme getter do NPCInteractable, viz níže)
        _npcNameText.text = npc.NpcName;

        // Odemkneme myš
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        DisplayNode(rootNode);
    }

    public void EndDialogue()
    {
        _dialoguePanel.SetActive(false);
        _currentNpc = null;

        // Zamkneme myš zpět (pokud nejsme v Shopu)
        // Pokud se přechází do Shopu, ShopManager si myš ohlídá sám.
        if (ShopManager.Instance == null || !ShopManager.Instance.IsShopOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void DisplayNode(DialogueNode node)
    {
        if (node == null)
        {
            EndDialogue();
            return;
        }

        _dialogueText.text = node.NpcText;

        // 1. Smazat stará tlačítka
        foreach (Transform child in _optionsContainer)
        {
            Destroy(child.gameObject);
        }

        // 2. Vytvořit nová tlačítka
        foreach (var option in node.Options)
        {
            GameObject btnObj = Instantiate(_optionButtonPrefab, _optionsContainer);
            btnObj.GetComponentInChildren<TextMeshProUGUI>().text = option.ButtonText;

            Button btn = btnObj.GetComponent<Button>();

            // Hack pro zachycení proměnné v loopu
            DialogueOption opt = option;
            btn.onClick.AddListener(() => OnOptionSelected(opt));
        }
    }

    private void OnOptionSelected(DialogueOption option)
    {
        // 1. Vykonat akci (pokud nějaká je)
        HandleAction(option.Action);

        // 2. Pokud akce neukončila dialog a máme další node, jdeme dál
        // Pokud akce otevřela Shop, dialog se obvykle zavře nebo skryje
        if (option.Action == DialogueAction.None || option.Action == DialogueAction.HealPlayer)
        {
            if (option.NextNode != null)
            {
                DisplayNode(option.NextNode);
            }
            else
            {
                EndDialogue();
            }
        }
    }

    private void HandleAction(DialogueAction action)
    {
        switch (action)
        {
            case DialogueAction.OpenWeaponShop:
                // 1. ZÁLOHA: Uložíme si, s kým mluvíme, JEŠTĚ PŘED UKONČENÍM DIALOGU
                NPCInteractable trader = _currentNpc;

                // 2. UKONČENÍ: Teď můžeme bezpečně zavřít dialog (i kdyby to smazalo _currentNpc)
                EndDialogue();

                // 3. AKCE: Otevřeme obchod se zálohovanou referencí 'trader'
                if (ShopManager.Instance != null && trader != null)
                {
                    ShopManager.Instance.OpenShop(trader);
                }
                else
                {
                    Debug.LogError("[DialogueManager] Chyba: ShopManager nebo NPC chybí!");
                }
                break;

            case DialogueAction.HealPlayer:
                // Tady reference není tak kritická, ale pro jistotu:
                if (_currentNpc != null)
                {
                    // _currentNpc.RequestHealServerRpc(...); 
                }
                // Dialog může pokračovat ("Díky za vyléčení"), takže zde EndDialogue volat nemusíme
                // Nebo pokud končí:
                // EndDialogue();
                break;

            case DialogueAction.Exit:
                EndDialogue();
                break;

            case DialogueAction.OpenUpgradeShop:
                // Zde bys mohl otevřít panel pro upgrade statů (Step 1 z předchozí odpovědi)
                Debug.Log("Otevírám Upgrade Shop (TODO)");
                EndDialogue();
                break;
        }
    }
}