using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using Unity.Netcode;

public class LoadingScreenManager : PersistentSingleton<LoadingScreenManager>
{
    [Header("UI References")]
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private TMP_Text _statusText;

    [Header("Settings")]
    [SerializeField] private float _fadeDuration = 0.5f;
    [Tooltip("Minimální doba, po kterou bude loading plně viditelný.")]
    [SerializeField] private float _minLoadTime = 1.5f;
    [Tooltip("Doba, po kterou loading zůstane na obrazovce PO dokončení načítání.")]
    [SerializeField] private float _postLoadDelay = 0.5f;

    private float _showStartTime;
    private bool _isHidden = true;

    private void Start()
    {
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
        }
        DontDestroyOnLoad(gameObject);

        // AUTOMATICKÝ HOOK: Pokud už NetworkManager existuje, hned se napojíme
        HookIntoNetworkEvents();
    }

    // Volá se automaticky, případně zvenčí, pokud se NetworkManager inicializuje později
    public void HookIntoNetworkEvents()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            // Odběr událostí přechodu scény
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;
            Debug.Log("[LoadingScreenManager] Úspěšně napojeno na NGO SceneManager.");
        }
    }

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        switch (sceneEvent.SceneEventType)
        {
            // 1. Scéna se začíná načítat (volá se na serveru i klientech)
            case SceneEventType.Load:
                Show($"Loading {sceneEvent.SceneName}...");
                break;

            // 2. Lokální klient dokončil načítání scény (ale čeká se na ostatní)
            case SceneEventType.LoadComplete:
                UpdateMessage("Synchronizing world...");
                break;

            // 3. Všichni klienti se synchronizovali a scéna je plně připravena ke hraní
            case SceneEventType.LoadEventCompleted:
                Hide();
                break;
        }
    }

    public void Show(string message = "Loading...")
    {
        if (_statusText) _statusText.text = message;

        // OPRAVA SKOKU: Pokud už jsme viditelní (např. ruční volání a hned na to NGO event),
        // jen jsme updatnuli text a končíme. Neresetujeme animaci!
        if (!_isHidden) return;

        _isHidden = false;
        _showStartTime = Time.realtimeSinceStartup;

        StopAllCoroutines();
        StartCoroutine(FadeRoutine(1f));
    }

    public void UpdateMessage(string message)
    {
        if (_statusText) _statusText.text = message;
    }

    public void Hide()
    {
        if (_isHidden) return;
        _isHidden = true;

        StopAllCoroutines();
        StartCoroutine(SmartHideRoutine());
    }

    public void ForceHide()
    {
        StopAllCoroutines();
        _isHidden = true;
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
        }
    }

    private IEnumerator SmartHideRoutine()
    {
        // 1. POJISTKA: Pokud se fade-in nestihl dokončit, musíme ho nejdřív dorazit.
        if (_canvasGroup.alpha < 0.99f)
        {
            yield return StartCoroutine(FadeRoutine(1f));
        }

        // 2. MINIMÁLNÍ ČAS CELKOVĚ
        float timeAlive = Time.realtimeSinceStartup - _showStartTime;
        if (timeAlive < _minLoadTime)
        {
            yield return new WaitForSecondsRealtime(_minLoadTime - timeAlive);
        }

        // 3. EXTRA ČAS PO NAČTENÍ (Hráč vidí hlášku "Synchronizing world..." a má čas se zorientovat)
        if (_postLoadDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(_postLoadDelay);
        }

        // 4. ODCHOD
        yield return StartCoroutine(FadeRoutine(0f));
    }

    private IEnumerator FadeRoutine(float targetAlpha)
    {
        float startAlpha = _canvasGroup.alpha;
        float time = 0f;

        if (targetAlpha > 0.5f) _canvasGroup.blocksRaycasts = true;

        float distance = Mathf.Abs(targetAlpha - startAlpha);
        float currentDuration = _fadeDuration * distance;

        while (time < currentDuration)
        {
            time += Time.unscaledDeltaTime;
            float t = time / currentDuration;
            t = t * t * (3f - 2f * t);

            _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }

        _canvasGroup.alpha = targetAlpha;

        if (targetAlpha < 0.5f) _canvasGroup.blocksRaycasts = false;
    }

    private void OnDestroy()
    {
        // Odhlášení z událostí při zničení (prevence memory leaků)
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;
        }
    }
}