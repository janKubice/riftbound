using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using System.IO; // Pro screenshoty

public class TrailerDirector : MonoBehaviour
{
    [Header("Setup")]
    public Camera gameCamera; // Přetáhni sem MainCamera (s CinemachineBrain)

    [Header("1. Orbit Camera")]
    public Transform playerTarget;
    public float orbitDistance = 5f;
    public float orbitHeight = 2f;
    public float orbitSpeed = 10f;
    public Key orbitToggleKey = Key.O;

    [Header("2. UI & Tools")]
    public Key uiToggleKey = Key.H;
    public Key screenshotKey = Key.K;
    public Key pauseKey = Key.L; // L pro Pause (P je pro Parade)
    public int screenshotSuperSize = 4;

    [Header("3. Item Showcase")]
    public Transform showcasePoint; 
    public List<GameObject> itemsToShowcase;
    public float itemRotationSpeed = 30f;
    public float timePerItem = 3f;
    public float itemTilt = 0f; 
    public Key showcaseKey = Key.I;

    [Header("4. Character Parade")]
    public Transform paradeCenterPoint;
    public List<GameObject> charactersToParade;
    public float slideDuration = 0.5f;
    public float poseDuration = 2.0f;
    public float slideOffset = 10f;
    public Key paradeKey = Key.P;

    // --- Interní stav ---
    private Camera _trailerCam;
    private AudioListener _trailerListener;
    private bool _uiVisible = true;
    private Canvas[] _cachedCanvases;
    private float _currentOrbitAngle = 0f;
    private bool _isPaused = false;
    private float _originalTimeScale;
    
    // Režimy
    private enum Mode { None, Orbiting, Showcasing, Parading }
    private Mode _currentMode = Mode.None;

    private Coroutine _activeRoutine;
    private GameObject _currentModelInstance;

    private void Start()
    {
        // 1. Najdeme hlavní kameru, pokud není přiřazena
        if (gameCamera == null) gameCamera = Camera.main;

        // 2. Vytvoříme si vlastní "Trailer Kameru"
        CreateTrailerCamera();

        // 3. Inicializace bodů (pokud chybí)
        if (showcasePoint == null)
        {
            GameObject go = new GameObject("Auto_ShowcasePoint");
            go.transform.position = transform.position + Vector3.forward * 10f;
            go.transform.rotation = Quaternion.Euler(0, 180, 0);
            showcasePoint = go.transform;
        }
        if (paradeCenterPoint == null) paradeCenterPoint = showcasePoint;
        if (playerTarget == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTarget = p.transform;
        }

        _originalTimeScale = Time.timeScale;
    }

    private void CreateTrailerCamera()
    {
        // Vytvoří nový GameObject
        GameObject camObj = new GameObject("Trailer_Camera_Internal");
        camObj.transform.SetParent(this.transform); // Uklidíme ho pod tento objekt
        
        // Přidáme kameru
        _trailerCam = camObj.AddComponent<Camera>();
        _trailerCam.enabled = false; // Ve výchozím stavu vypnuta
        
        // Zkopírujeme nastavení z hlavní kamery (aby hra vypadala stejně)
        if (gameCamera != null)
        {
            _trailerCam.clearFlags = gameCamera.clearFlags;
            _trailerCam.backgroundColor = gameCamera.backgroundColor;
            _trailerCam.cullingMask = gameCamera.cullingMask;
            _trailerCam.fieldOfView = gameCamera.fieldOfView;
            _trailerCam.nearClipPlane = gameCamera.nearClipPlane;
            _trailerCam.farClipPlane = gameCamera.farClipPlane;
            
            // URP data (pokud používáš URP, zkopírujeme i post-processing)
            var gameCamData = gameCamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (gameCamData != null)
            {
                var trailerCamData = camObj.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                trailerCamData.renderPostProcessing = gameCamData.renderPostProcessing;
                trailerCamData.volumeLayerMask = gameCamData.volumeLayerMask;
                trailerCamData.antialiasing = gameCamData.antialiasing;
            }
        }

        // Přidáme AudioListener (aby byl zvuk slyšet z nové pozice)
        _trailerListener = camObj.AddComponent<AudioListener>();
        _trailerListener.enabled = false;
    }

    private void Update()
    {
        if (Keyboard.current == null) return;
        HandleInputs();

        // Logika Orbitu (pokud běží)
        if (_currentMode == Mode.Orbiting && playerTarget != null)
        {
            DoOrbitLogic();
        }
    }

    private void HandleInputs()
    {
        // UI
        if (Keyboard.current[uiToggleKey].wasPressedThisFrame) ToggleUI();
        
        // Screenshot
        if (Keyboard.current[screenshotKey].wasPressedThisFrame) TakeScreenshot();

        // Pause
        if (Keyboard.current[pauseKey].wasPressedThisFrame)
        {
            _isPaused = !_isPaused;
            Time.timeScale = _isPaused ? 0f : _originalTimeScale;
        }

        // Modes
        if (Keyboard.current[orbitToggleKey].wasPressedThisFrame)
        {
            if (_currentMode == Mode.Orbiting) StopMode();
            else StartMode(Mode.Orbiting);
        }

        if (Keyboard.current[showcaseKey].wasPressedThisFrame)
        {
            StartMode(Mode.Showcasing);
            StartCoroutineHelper(ShowcaseRoutine());
        }

        if (Keyboard.current[paradeKey].wasPressedThisFrame)
        {
            StartMode(Mode.Parading);
            StartCoroutineHelper(ParadeRoutine());
        }

        // Cancel
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            StopMode();
        }
    }

    // --- PŘEPÍNÁNÍ KAMER (JÁDRO PROBLÉMU) ---

    private void StartMode(Mode newMode)
    {
        StopMode(); // Vyčistit předchozí

        _currentMode = newMode;

        // PRINCIP PHOTOCAM:
        // 1. Vypneme hlavní kameru (tím umlčíme Cinemachine)
        if (gameCamera != null) gameCamera.gameObject.SetActive(false);
        
        // 2. Zapneme naši trailer kameru
        _trailerCam.enabled = true;
        _trailerListener.enabled = true;
        _trailerCam.gameObject.SetActive(true);

        Debug.Log($"[TrailerDirector] Started Mode: {newMode}");
    }

    private void StopMode()
    {
        if (_currentMode == Mode.None) return;

        // Zastavit coroutiny
        if (_activeRoutine != null) StopCoroutine(_activeRoutine);
        if (_currentModelInstance != null) Destroy(_currentModelInstance);

        // RESET KAMER:
        // 1. Vypneme trailer kameru
        _trailerCam.enabled = false;
        _trailerListener.enabled = false;
        _trailerCam.gameObject.SetActive(false);

        // 2. Zapneme zpět hlavní kameru (Cinemachine se chytne)
        if (gameCamera != null) gameCamera.gameObject.SetActive(true);

        _currentMode = Mode.None;
        Debug.Log("[TrailerDirector] Stopped.");
    }

    // --- LOGIKA REŽIMŮ (Stejná jako předtím, jen používá _trailerCam) ---

    private void DoOrbitLogic()
    {
        _currentOrbitAngle += orbitSpeed * Time.deltaTime;
        Quaternion rotation = Quaternion.Euler(0, _currentOrbitAngle, 0);
        Vector3 direction = rotation * Vector3.back;
        Vector3 targetPos = playerTarget.position + (direction * orbitDistance) + (Vector3.up * orbitHeight);

        _trailerCam.transform.position = targetPos;
        _trailerCam.transform.LookAt(playerTarget.position + Vector3.up * (orbitHeight * 0.5f));
    }

    private IEnumerator ShowcaseRoutine()
    {
        // Nastavit kameru
        _trailerCam.transform.position = showcasePoint.position + showcasePoint.forward * -orbitDistance;
        _trailerCam.transform.LookAt(showcasePoint);

        float currentYRotation = 0f;

        foreach (var itemPrefab in itemsToShowcase)
        {
            if (itemPrefab == null) continue;

            if (_currentModelInstance != null) Destroy(_currentModelInstance);
            _currentModelInstance = Instantiate(itemPrefab, showcasePoint.position, Quaternion.identity);
            
            // Aplikovat rotaci
            _currentModelInstance.transform.rotation = Quaternion.Euler(itemTilt, currentYRotation, 0);

            float timer = 0f;
            while (timer < timePerItem)
            {
                if (_currentMode != Mode.Showcasing) yield break;
                float delta = Time.unscaledDeltaTime; // Unscaled aby to jelo i v pauze
                timer += delta;
                currentYRotation += itemRotationSpeed * delta;
                
                if (_currentModelInstance != null)
                    _currentModelInstance.transform.rotation = Quaternion.Euler(itemTilt, currentYRotation, 0);
                
                yield return null;
            }
        }
        StopMode();
    }

    private IEnumerator ParadeRoutine()
    {
        _trailerCam.transform.position = paradeCenterPoint.position + paradeCenterPoint.forward * -orbitDistance;
        _trailerCam.transform.LookAt(paradeCenterPoint);

        Vector3 centerPos = paradeCenterPoint.position;
        Vector3 leftPos = centerPos - paradeCenterPoint.right * slideOffset;
        Vector3 rightPos = centerPos + paradeCenterPoint.right * slideOffset;

        foreach (var charPrefab in charactersToParade)
        {
            if (charPrefab == null) continue;

            if (_currentModelInstance != null) Destroy(_currentModelInstance);
            _currentModelInstance = Instantiate(charPrefab, leftPos, Quaternion.identity);
            
            // Otočení na kameru
            _currentModelInstance.transform.LookAt(_trailerCam.transform);
            Vector3 euler = _currentModelInstance.transform.rotation.eulerAngles;
            _currentModelInstance.transform.rotation = Quaternion.Euler(0, euler.y, 0);

            // Slide In
            yield return StartCoroutine(SlideObject(_currentModelInstance.transform, leftPos, centerPos, slideDuration));

            // Pose / Animation
            var anim = _currentModelInstance.GetComponent<Animator>();
            if (anim != null) anim.SetTrigger("Attack");

            yield return new WaitForSecondsRealtime(poseDuration); // Realtime pro pauzu
            if (_currentMode != Mode.Parading) yield break;

            // Slide Out
            yield return StartCoroutine(SlideObject(_currentModelInstance.transform, centerPos, rightPos, slideDuration));
        }
        StopMode();
    }

    private IEnumerator SlideObject(Transform target, Vector3 start, Vector3 end, float duration)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / duration;
            if (target != null) target.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0, 1, t));
            yield return null;
        }
    }

    // --- TOOLS ---

    private void ToggleUI()
    {
        _uiVisible = !_uiVisible;
        // Najde Canvasy i pokud jsou vypnuté (includeInactive: true)
        _cachedCanvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in _cachedCanvases)
        {
            // Neskrývat náš vlastní UI pokud bys nějaké měl
            c.enabled = _uiVisible;
        }
    }

    private void TakeScreenshot()
    {
        string dir = "Screenshots";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string file = $"{dir}/Trailer_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
        
        bool wasUi = _uiVisible;
        if (wasUi) ToggleUI(); // Schovat UI

        ScreenCapture.CaptureScreenshot(file, screenshotSuperSize);
        Debug.Log($"Screenshot saved: {file}");

        // UI vrátíme ručně nebo necháme vypnuté, obvykle chceš v trailer toolu UI vypnuté
        if (wasUi) StartCoroutine(RestoreUI()); 
    }

    private IEnumerator RestoreUI()
    {
        yield return null; // Počkat frame
        ToggleUI();
    }

    private void StartCoroutineHelper(IEnumerator routine)
    {
        _activeRoutine = StartCoroutine(routine);
    }
}