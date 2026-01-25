using UnityEngine;
using Unity.Netcode;
using TMPro;
using UnityEngine.InputSystem;
public class PlayerInteractor : NetworkBehaviour
{
    [Header("Nastavení Interakce")]
    [SerializeField] private float _interactionDistance = 3.0f;
    [SerializeField] private LayerMask _interactableMask; // Nastavte v inspektoru

    [Header("Odkazy na UI")]
    [Tooltip("Textové pole (TMP) na HUDu pro zobrazení výzvy (např. 'E')")]
    [SerializeField] private TextMeshProUGUI _interactionPromptUI;

    [Header("Odkazy na Hráče")]
    [Tooltip("Odkaz na kameru hráče pro míření paprsku")]
    [SerializeField] private Camera _playerCamera;
    private bool _isUiReady = false;
    private IInteractable _currentTarget; // Objekt, na který se díváme
    private bool _canInteract;

    private void Start()
    {
        if (_interactionPromptUI != null)
            _interactionPromptUI.gameObject.SetActive(false);

        // Pokus o automatické nalezení, pokud není přiřazeno
        if (IsOwner)
        {
            if (_playerCamera == null)
            {
                _playerCamera = FindAnyObjectByType<Camera>();
            }
        }
    }

    public override void OnDestroy()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned && !NetworkManager.Singleton.IsServer)
        {
            if (!NetworkManager.Singleton.ShutdownInProgress)
            {
                Debug.LogError($"[Security Alert] Objekt {gameObject.name} byl smazán lokálně na klientovi! " +
                               $"To způsobí Invalid Destroy chybu. Prověřte volání v tomto skriptu.");
            }
        }
        base.OnDestroy();
    }


    public override void OnNetworkSpawn()
    {
        try
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            if (_playerCamera == null)
            {
                _playerCamera = GetComponentInChildren<Camera>();
            }

            // První pokus o nalezení UI
            TryFindUI();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CRITICAL SPAWN ERROR] Chyba v {name}: {e.Message}\n{e.StackTrace}");
        }

    }

    void Update()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (_interactionPromptUI != null && _interactionPromptUI.gameObject.activeSelf)
            {
                _interactionPromptUI.gameObject.SetActive(false);
            }
            return; // Ukončíme Update, takže FindInteractable se vůbec nezavolá
        }

        // Pokud UI ještě není nalezeno, zkusíme to znovu
        if (!_isUiReady)
        {
            TryFindUI();
            if (!_isUiReady) return; // UI stále není, tento frame nic neděláme
        }

        // Zbytek Update logiky
        FindInteractable();
        HandleInput();
    }

    /// <summary>
    /// Najde interaktivní objekt přesně pod křížkem (středem kamery).
    /// </summary>
    private void FindInteractable()
    {
        // Pokud nemáme kameru, nemůžeme mířit
        if (_playerCamera == null) return;

        _canInteract = false;
        _currentTarget = null;

        // Vytvoříme paprsek ze středu kamery (pozice) směrem dopředu (pohled)
        Ray ray = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);
        RaycastHit hit;

        // Vystřelíme paprsek do vzdálenosti _interactionDistance
        // Používáme _interactableMask pro filtrování
        if (Physics.Raycast(ray, out hit, _interactionDistance, _interactableMask))
        {
            // Zkontrolujeme, zda objekt, který jsme trefili, je interaktivní
            if (hit.collider.TryGetComponent(out IInteractable interactable))
            {
                _currentTarget = interactable;
                _canInteract = true;
            }
        }

        // Aktualizace UI (zobrazí text jen když na objekt přímo míříme)
        UpdatePromptUI();
    }

    /// <summary>
    /// Zobrazí nebo skryje UI výzvu
    /// </summary>
    private void UpdatePromptUI()
    {
        if (_interactionPromptUI == null) return;

        if (_canInteract && _currentTarget != null)
        {
            _interactionPromptUI.text = _currentTarget.InteractionPrompt;
            _interactionPromptUI.gameObject.SetActive(true);
        }
        else
        {
            _interactionPromptUI.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Kontroluje stisk klávesy "E"
    /// </summary>
    private void HandleInput()
    {
        // Zkontrolujeme, zda je klávesnice vůbec připojena (dobrý zvyk)
        if (Keyboard.current == null) return;

        // Starý kód: if (_canInteract && Input.GetKeyDown(KeyCode.E))
        // Nový kód:
        if (_canInteract && Keyboard.current.eKey.wasPressedThisFrame)
        {
            // Máme cíl, pošleme požadavek na server
            // Musíme poslat NetworkObjectReference, protože nemůžeme poslat 
            // přímý odkaz na komponentu.
            NetworkObject targetNetObj = ((Component)_currentTarget).GetComponent<NetworkObject>();
            if (targetNetObj != null)
            {
                InteractServerRpc(targetNetObj);
            }
        }
    }

    /// <summary>
    /// Běží na serveru. Řekne cíli, aby provedl interakci.
    /// </summary>
    [ServerRpc]
    private void InteractServerRpc(NetworkObjectReference targetRef)
    {
        if (targetRef.TryGet(out NetworkObject targetNetObj))
        {
            // Máme NetworkObject na serveru
            if (targetNetObj.TryGetComponent(out IInteractable interactable))
            {
                // Zavoláme metodu Interact na serverové kopii objektu
                // Předáme NetworkObject hráče, který o to požádal
                interactable.Interact(NetworkObject);
            }
        }
    }

    private void TryFindUI()
    {
        // Pokud už je přiřazeno z inspektoru (pro testování)
        if (_interactionPromptUI != null)
        {
            _interactionPromptUI.gameObject.SetActive(false);
            _isUiReady = true;
            return;
        }

        // Hledání pomocí tagu
        GameObject promptObject = GameObject.FindWithTag("InteractionPromptUI");
        if (promptObject != null)
        {
            _interactionPromptUI = promptObject.GetComponent<TextMeshProUGUI>();
            if (_interactionPromptUI != null)
            {
                _interactionPromptUI.gameObject.SetActive(false);
                _isUiReady = true;
            }
        }
    }
}