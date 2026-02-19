using UnityEngine;
using UnityEngine.InputSystem; // Nutný namespace
using System.IO;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PhotoCam : MonoBehaviour
{
    [Header("Setup")]
    public Camera gameCamera;
    public Volume globalVolume;

    [Header("Controls")]
    public Key toggleKey = Key.F8;
    public Key hideUIKey = Key.H;
    public Key pauseKey = Key.P;

    [Header("Defaults")]
    public float defaultMoveSpeed = 5f;
    public float defaultFov = 60f;

    [Header("Capture")]
    public int superSize = 4;

    // --- Interní stav ---
    private bool _isActive = false;
    private bool _isGamePaused = true;
    private bool _hideUI = false;

    private Camera _photoCam;
    private UniversalAdditionalCameraData _photoCamData;
    private UniversalAdditionalCameraData _gameCamData;
    private AudioListener _photoListener;
    
    // Pohyb
    private Vector3 _targetPos;
    private Quaternion _targetRot;
    private float _targetFOV;
    private float _yaw, _pitch, _roll;
    
    // Nastavení (editovatelné v GUI)
    private float _moveSpeed;
    private float _focusDist = 10f;
    private float _aperture = 5.6f;
    private bool _autoFocus = true;

    // DoF
    private DepthOfField _dof;

    // Cache
    private float _originalTimeScale;
    private bool _wasCursorVisible;
    private CursorLockMode _wasCursorLock;

    private void Awake()
    {
        _photoCam = GetComponent<Camera>();
        _photoCamData = GetComponent<UniversalAdditionalCameraData>();
        
        // Přidat AudioListener
        if (!TryGetComponent(out _photoListener))
            _photoListener = gameObject.AddComponent<AudioListener>();

        _photoCam.enabled = false;
        _photoListener.enabled = false;
        
        // Získání DoF
        if (globalVolume != null && globalVolume.profile != null)
        {
            if (!globalVolume.profile.TryGet(out _dof))
            {
                // Pokud tam není, zkusíme ho přidat (jen runtime)
                _dof = globalVolume.profile.Add<DepthOfField>(true);
            }
        }

        if (gameCamera == null) gameCamera = Camera.main;
        if (gameCamera != null)
            _gameCamData = gameCamera.GetComponent<UniversalAdditionalCameraData>();

        _moveSpeed = defaultMoveSpeed;
    }

    private void Update()
    {
        // Toggle Photo Mode
        if (Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            TogglePhotoMode();
        }

        if (!_isActive) return;

        // Toggle UI
        if (Keyboard.current[hideUIKey].wasPressedThisFrame)
            _hideUI = !_hideUI;

        // Toggle Pause
        if (Keyboard.current[pauseKey].wasPressedThisFrame)
            TogglePause();

        HandleMovement();
        HandleOptics();
        HandleDoF();
        HandleScreenshot();
    }

    public void TogglePhotoMode()
    {
        _isActive = !_isActive;

        if (_isActive)
        {
            // --- AKTIVACE ---
            
            // 1. Uložit stav hry
            _originalTimeScale = Time.timeScale;
            _wasCursorVisible = Cursor.visible;
            _wasCursorLock = Cursor.lockState;
            _isGamePaused = true;
            Time.timeScale = 0f;

            // 2. Zkopírovat parametry kamery (OPRAVA BAREV)
            if (gameCamera != null)
            {
                // Transform
                transform.position = gameCamera.transform.position;
                transform.rotation = gameCamera.transform.rotation;
                
                // Camera Settings
                _photoCam.fieldOfView = gameCamera.fieldOfView;
                _photoCam.farClipPlane = gameCamera.farClipPlane;
                _photoCam.nearClipPlane = gameCamera.nearClipPlane;
                _photoCam.cullingMask = gameCamera.cullingMask;
                _photoCam.clearFlags = gameCamera.clearFlags;
                _photoCam.backgroundColor = gameCamera.backgroundColor;

                // URP Specifika (Post Processing, Volume Mask, Anti-aliasing)
                if (_gameCamData != null && _photoCamData != null)
                {
                    _photoCamData.renderPostProcessing = _gameCamData.renderPostProcessing;
                    _photoCamData.volumeLayerMask = _gameCamData.volumeLayerMask;
                    _photoCamData.antialiasing = _gameCamData.antialiasing;
                    _photoCamData.antialiasingQuality = _gameCamData.antialiasingQuality;
                    _photoCamData.renderShadows = _gameCamData.renderShadows;
                }

                // Inicializace pohybu
                _targetPos = transform.position;
                _targetRot = transform.rotation;
                _targetFOV = _photoCam.fieldOfView;
                Vector3 euler = transform.eulerAngles;
                _yaw = euler.y;
                _pitch = euler.x;
                _roll = 0f;

                // Vypnout herní kameru
                gameCamera.gameObject.SetActive(false);
            }

            // Zapnout foto kameru
            _photoCam.enabled = true;
            _photoListener.enabled = true;
            
            // Myš
            Cursor.visible = true; // Chceme vidět myš pro ovládání menu
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            // --- DEAKTIVACE ---
            if (gameCamera != null) gameCamera.gameObject.SetActive(true);
            
            _photoCam.enabled = false;
            _photoListener.enabled = false;

            Time.timeScale = _originalTimeScale;
            Cursor.visible = _wasCursorVisible;
            Cursor.lockState = _wasCursorLock;
        }
    }

    public void TogglePause()
    {
        _isGamePaused = !_isGamePaused;
        Time.timeScale = _isGamePaused ? 0f : 1f;
    }

    private void HandleMovement()
    {
        // Pokud držíme pravé tlačítko myši, otáčíme se (jako v Unity Editoru)
        bool isLooking = Mouse.current.rightButton.isPressed;
        Cursor.visible = !isLooking;
        Cursor.lockState = isLooking ? CursorLockMode.Locked : CursorLockMode.None;

        float dt = Time.unscaledDeltaTime;

        // Pohyb
        var kb = Keyboard.current;
        float speed = _moveSpeed;
        if (kb.leftShiftKey.isPressed) speed *= 3f;
        if (kb.leftCtrlKey.isPressed) speed *= 0.2f;

        Vector3 dir = Vector3.zero;
        if (kb.wKey.isPressed) dir.z += 1;
        if (kb.sKey.isPressed) dir.z -= 1;
        if (kb.aKey.isPressed) dir.x -= 1;
        if (kb.dKey.isPressed) dir.x += 1;
        if (kb.qKey.isPressed) dir.y -= 1;
        if (kb.eKey.isPressed) dir.y += 1;

        if (dir != Vector3.zero)
        {
            _targetPos += transform.TransformDirection(dir.normalized) * speed * dt;
        }
        transform.position = Vector3.Lerp(transform.position, _targetPos, 10f * dt);

        // Rotace
        if (isLooking)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            _yaw += delta.x * 0.2f;
            _pitch -= delta.y * 0.2f;
        }
        
        // Roll
        if (kb.zKey.isPressed) _roll += 20f * dt;
        if (kb.cKey.isPressed) _roll -= 20f * dt;
        if (kb.rKey.wasPressedThisFrame) _roll = 0;

        _targetRot = Quaternion.Euler(_pitch, _yaw, _roll);
        transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, 10f * dt);
    }

    private void HandleOptics()
    {
        // Scroll mění FOV
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            _targetFOV -= scroll * 0.05f;
            _targetFOV = Mathf.Clamp(_targetFOV, 10f, 120f);
        }
        _photoCam.fieldOfView = Mathf.Lerp(_photoCam.fieldOfView, _targetFOV, 5f * Time.unscaledDeltaTime);
    }

    private void HandleDoF()
    {
        if (_dof == null) return;
        if (!_dof.active) _dof.active = true;

        // Auto Focus Logic
        if (_autoFocus)
        {
            if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, 200f))
            {
                _focusDist = Mathf.Lerp(_focusDist, hit.distance, 5f * Time.unscaledDeltaTime);
            }
        }

        _dof.focusDistance.Override(_focusDist);
        // Clona (Aperture) - nižší = rozmazanější pozadí
        _dof.aperture.Override(_aperture); 
    }

    private void HandleScreenshot()
    {
        if (Keyboard.current.kKey.wasPressedThisFrame)
        {
            // Dočasně vypnout GUI pro screenshot
            bool wasUiHidden = _hideUI;
            _hideUI = true;

            // Uložit
            string dir = "Screenshots";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string file = $"{dir}/Shot_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
            
            // ScreenCapture bere obrazovku tak jak je (bez GUI, pokud se vykresluje v OnGUI a dáme yield, ale tady to děláme jednoduše)
            // U OnGUI je problém, že se kreslí na konci.
            // Pro jednoduchost: ScreenCapture zachytí i GUI, pokud ho nevypneme frame předtím.
            // Nejlepší je zmáčknout H a pak K.
            
            ScreenCapture.CaptureScreenshot(file, superSize);
            Debug.Log($"Uloženo: {file}");
            
            _hideUI = wasUiHidden;
        }
    }

    private void OnGUI()
    {
        if (!_isActive || _hideUI) return;

        // Styl
        GUI.backgroundColor = new Color(0, 0, 0, 0.8f);
        GUI.contentColor = Color.white;
        
        // Okno napravo (Šířka 300px)
        float width = 300;
        float padding = 20;
        Rect r = new Rect(Screen.width - width - padding, padding, width, 550);

        GUILayout.BeginArea(r, GUI.skin.box);
        GUILayout.BeginVertical();

        GUILayout.Label("<b>PHOTO MODE SETTINGS</b>");
        GUILayout.Space(10);

        // --- GAME CONTROL ---
        GUILayout.Label($"Game State: {(_isGamePaused ? "<color=red>PAUSED</color>" : "<color=green>RUNNING</color>")}");
        if (GUILayout.Button(_isGamePaused ? "RESUME GAME (P)" : "PAUSE GAME (P)"))
        {
            TogglePause();
        }
        GUILayout.Label("<i>Stiskni 'H' pro skrytí tohoto menu pro nahrávání videa!</i>");
        
        GUILayout.Space(15);
        
        // --- CAMERA ---
        GUILayout.Label($"Move Speed: {_moveSpeed:F1}");
        _moveSpeed = GUILayout.HorizontalSlider(_moveSpeed, 1f, 50f);

        GUILayout.Label($"Field of View: {_targetFOV:F0}");
        _targetFOV = GUILayout.HorizontalSlider(_targetFOV, 10f, 120f);

        GUILayout.Label($"Roll (Tilt): {_roll:F0}");
        _roll = GUILayout.HorizontalSlider(_roll, -45f, 45f);
        if (GUILayout.Button("Reset Roll (R)")) _roll = 0;

        GUILayout.Space(15);

        // --- DEPTH OF FIELD ---
        GUILayout.Label("<b>DEPTH OF FIELD</b>");
        _autoFocus = GUILayout.Toggle(_autoFocus, " Auto Focus (Center)");
        
        if (!_autoFocus)
        {
            GUILayout.Label($"Focus Distance: {_focusDist:F1}m");
            _focusDist = GUILayout.HorizontalSlider(_focusDist, 0.1f, 100f);
        }

        GUILayout.Label($"Aperture (Blur): {_aperture:F1}");
        // Nižší clona = více rozmazané. Slider obráceně pro intuitivnost (Vlevo = ostré, Vpravo = rozmazané)
        _aperture = GUILayout.HorizontalSlider(_aperture, 32f, 1f); 

        GUILayout.Space(20);
        
        // --- ACTION ---
        if (GUILayout.Button("<b>TAKE SCREENSHOT (K)</b>", GUILayout.Height(40)))
        {
            // Screenshot logika je v Update
        }
        
        GUILayout.EndVertical();
        GUILayout.EndArea();

        // Crosshair (jen pokud je AutoFocus)
        if (_autoFocus)
        {
            GUI.color = new Color(1, 1, 1, 0.5f);
            GUI.Box(new Rect(Screen.width/2 - 2, Screen.height/2 - 2, 4, 4), "");
        }
    }
}