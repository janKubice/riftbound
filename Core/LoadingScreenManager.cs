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

    private float _showStartTime;
    private bool _isHidden = true; // Stavová proměnná

    private void Start()
    {
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
        }
        DontDestroyOnLoad(gameObject);
    }

    public void Show(string message = "Loading...")
    {
        _isHidden = false;
        _showStartTime = Time.realtimeSinceStartup;
        
        if (_statusText) _statusText.text = message;

        // Pokud už běží nějaká rutina (třeba schovávání), zastavíme ji a jdeme hned do Show
        StopAllCoroutines();
        StartCoroutine(FadeRoutine(1f));
    }

    public void UpdateMessage(string message)
    {
        if (_statusText) _statusText.text = message;
    }

    public void Hide()
    {
        if (_isHidden) return; // Už je schováno nebo se schovává
        _isHidden = true;

        // NEZASTAVUJEME coroutiny hned! (To byla ta chyba)
        // Místo toho spustíme "Frontu na schování", která si pohlídá dokončení.
        StopAllCoroutines();
        StartCoroutine(SmartHideRoutine());
    }

    private IEnumerator SmartHideRoutine()
    {
        // 1. POJISTKA VIDITELNOSTI:
        // Pokud se fade-in nestihl dokončit (alfa < 1), musíme ho nejdřív dorazit.
        // Jinak by loading "čekal" neviditelný.
        if (_canvasGroup.alpha < 0.99f)
        {
            yield return StartCoroutine(FadeRoutine(1f));
        }

        // 2. POVINNÁ PAUZA:
        // Teď, když jsme určitě vidět (Alpha = 1), počítáme čas.
        float timeAlive = Time.realtimeSinceStartup - _showStartTime;
        if (timeAlive < _minLoadTime)
        {
            yield return new WaitForSecondsRealtime(_minLoadTime - timeAlive);
        }

        // 3. ODCHOD:
        yield return StartCoroutine(FadeRoutine(0f));
    }

    private IEnumerator FadeRoutine(float targetAlpha)
    {
        float startAlpha = _canvasGroup.alpha;
        float time = 0f;

        // Pokud jdeme do viditelna, zapneme blokování hned
        if (targetAlpha > 0.5f) _canvasGroup.blocksRaycasts = true;

        // Vypočítáme dobu trvání podle toho, jak velký kus cesty musíme ujít
        // (aby se to netáhlo 0.5s, když už jsme skoro tam)
        float distance = Mathf.Abs(targetAlpha - startAlpha);
        float currentDuration = _fadeDuration * distance; 

        while (time < currentDuration)
        {
            time += Time.unscaledDeltaTime;
            // Používáme Lerp pro hladký přechod
            float t = time / currentDuration;
            // SmoothStep udělá hezčí křivku (pomalejší rozjezd/dojezd)
            t = t * t * (3f - 2f * t); 
            
            _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }

        _canvasGroup.alpha = targetAlpha;

        // Pokud jsme zhasli, vypneme blokování
        if (targetAlpha < 0.5f) _canvasGroup.blocksRaycasts = false;
    }

    // --- Networking Hook ---
    public void HookIntoNetworkEvents()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;
        }
    }

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        if (sceneEvent.SceneEventType == SceneEventType.Load)
        {
            Show("Loading World...");
        }
    }
}