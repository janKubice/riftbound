using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance; // Singleton pro přístup z UI

    [Header("UI Refs")]
    [SerializeField] private GameObject _shopPanel;
    [SerializeField] private Transform _itemsContainer;
    [SerializeField] private GameObject _itemButtonPrefab;
    [SerializeField] private Button _sellButton;
    [SerializeField] private TextMeshProUGUI _sellButtonText;

    // Cache referencí
    private WeaponManager _localWeaponManager;
    private PlayerProgression _localProgression;
    private NPCInteractable _currentNpc;
    public bool IsShopOpen;

    private void Awake() => Instance = this;

    private void Start()
    {
        _shopPanel.SetActive(false);
        IsShopOpen = false;
    }

    public void OpenShop(NPCInteractable npc)
    {
        _currentNpc = npc;
        _localWeaponManager = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<WeaponManager>();
        _localProgression = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerProgression>();

        RefreshShopUI();
        _shopPanel.SetActive(true);
        IsShopOpen = true;

        // Odemknout kurzor
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseShop()
    {
        _shopPanel.SetActive(false);
        IsShopOpen = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Návrat do dialogu nebo konec?
        // DialogueManager.Instance.EndDialogue();
    }

    private void RefreshShopUI()
{
    Debug.Log("[ShopDebug] === ZAČÍNÁM REFRESH UI OBCHODU ===");

    // 1. Vyčistit stará tlačítka
    foreach (Transform child in _itemsContainer) Destroy(child.gameObject);

    // Kontrola referencí
    if (_currentNpc == null)
    {
        Debug.LogError("[ShopDebug] KRITICKÁ CHYBA: _currentNpc je NULL!");
        return;
    }
    if (_localWeaponManager == null)
    {
        Debug.LogError("[ShopDebug] KRITICKÁ CHYBA: _localWeaponManager je NULL! (Hráč asi nebyl nalezen)");
        return;
    }

    // 2. Zjistit počet položek
    int count = _currentNpc.WeaponIndexesForSale.Count;
    Debug.Log($"[ShopDebug] NPC '{_currentNpc.NpcName}' má v seznamu {count} položek k prodeji.");

    if (count == 0)
    {
        Debug.LogWarning("[ShopDebug] VAROVÁNÍ: Seznam zboží je PRÁZDNÝ! (Zkontroluj 'Weapon Indexes For Sale' v Inspectoru u NPC)");
    }

    // 3. Projít indexy
    foreach (int weaponIndex in _currentNpc.WeaponIndexesForSale)
    {
        Debug.Log($"[ShopDebug] -> Zpracovávám Index zbraně: {weaponIndex}...");

        // TADY VOLÁME TU NOVOU METODU
        WeaponData data = _localWeaponManager.GetWeaponDataByIndex(weaponIndex);

        if (data != null)
        {
            Debug.Log($"[ShopDebug]    -> DATA NALEZENA: '{data.WeaponName}' (Cena: {data.GoldPrice}). Vytvářím tlačítko.");

            // Vytvořit tlačítko
            GameObject btnObj = Instantiate(_itemButtonPrefab, _itemsContainer);

            // Kontrola UI viditelnosti (častá chyba)
            if (btnObj.transform.localScale == Vector3.zero) 
                Debug.LogWarning("[ShopDebug] POZOR: Tlačítko se vytvořilo, ale má Scale (0,0,0) -> nebude vidět!");

            // Nastavit texty
            var texts = btnObj.GetComponentsInChildren<TextMeshProUGUI>();
            
            if (texts.Length > 0) 
                texts[0].text = data.WeaponName; // Název
            else 
                Debug.LogError("[ShopDebug] CHYBA: Prefab tlačítka nemá TextMeshProUGUI (Index 0 - Název)!");

            if (texts.Length > 1) 
                texts[1].text = $"{data.GoldPrice} G"; // Cena

            // Nastavit akci na kliknutí
            Button btn = btnObj.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(() =>
                {
                    Debug.Log($"[ShopDebug] Kliknuto na nákup: {data.WeaponName}");
                    PlayerShopLogic.LocalInstance.RequestBuyWeapon(weaponIndex, data.GoldPrice);
                });
            }
            else
            {
                Debug.LogError("[ShopDebug] CHYBA: Prefab tlačítka nemá komponentu Button!");
            }
        }
        else
        {
            Debug.LogError($"[ShopDebug]    -> CHYBA: Data jsou NULL pro index {weaponIndex}! (WeaponManager nenašel data v listu prefabs)");
        }
    }

    Debug.Log("[ShopDebug] === REFRESH HOTOV ===");
    UpdateSellButton();
}

    private void UpdateSellButton()
    {
        // TADY VOLÁME TVOU SÍŤOVOU PROMĚNNOU
        int currentIndex = _localWeaponManager._currentWeaponIndex.Value;

        if (currentIndex != -1)
        {
            WeaponData currentData = _localWeaponManager.GetWeaponDataByIndex(currentIndex);
            if (currentData != null)
            {
                // Prodejní cena je třeba polovina
                int sellPrice = currentData.GoldPrice / 2;
                _sellButton.interactable = true;
                _sellButtonText.text = $"Sell Current ({sellPrice} G)";

                _sellButton.onClick.RemoveAllListeners();
                _sellButton.onClick.AddListener(() =>
                {
                    PlayerShopLogic.LocalInstance.RequestSellWeapon(sellPrice);
                });
                return;
            }
        }

        _sellButton.interactable = false;
        _sellButtonText.text = "No Weapon";
    }

    // --- LOGIKA TRANSAKCÍ (Server Calls) ---

    private void OnBuyClicked(int weaponIndex, int cost)
    {
        // Klient ověří peníze lokálně pro UX
        if (_localProgression.Gold.Value >= cost)
        {
            // Voláme ServerRPC pro transakci
            PerformTransactionServerRpc(weaponIndex, cost, true);
        }
        else
        {
            Debug.Log("Not enough gold!");
        }
    }

    private void OnSellClicked(int sellPrice)
    {
        PerformTransactionServerRpc(-1, sellPrice, false); // -1 pro odebrání zbraně (Unarmed)
    }

    [ServerRpc(RequireOwnership = false)] // Musí být v NetworkBehaviour, takže ShopManager musí být na NetworkObjectu nebo volat přes PlayerObject
    // UPRAVENO: ShopManager je v UI (Scene object), nemůže mít ServerRpc přímo, pokud není spawnutý.
    // ŘEŠENÍ: ServerRpc dáme na hráče (PlayerInteraction nebo nový PlayerShopController) a odsud ho jen voláme.
    // Pro jednoduchost zde ukážu logiku, kterou přidáš do `PlayerProgression` nebo `WeaponManager`.
    private void PerformTransactionServerRpc(int weaponIndex, int amount, bool isBuying)
    {
        // Přesunuto níže do PlayerShopLogic
    }
}