using UnityEngine;
using UnityEngine.InputSystem;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.Rendering.Universal;

public class PhotoCam : MonoBehaviour
{
    [System.Serializable]
    public struct PathNode
    {
        public Vector3 position;
        public Quaternion rotation;
        public float fov;
    }

    [Header("Setup")]
    public Camera gameCamera;

    [Header("Controls")]
    public Key toggleKey = Key.F8;
    public Key hideUIKey = Key.H;
    public Key pauseKey = Key.P;
    public Key captureKey = Key.K;

    [Header("Cinematic Path")]
    public Key addNodeKey = Key.N;
    public Key clearPathKey = Key.C;
    public Key playPathKey = Key.M;
    public float pathDuration = 10f;
    public int targetFPS = 30;
    public bool saveFramesToDisk = true;

    [Header("Camera Settings")]
    public float defaultMoveSpeed = 5f;
    public int superSize = 4;

    private bool _isActive = false;
    private bool _isGamePaused = true;
    private bool _hideUI = false;
    private float _timeScaleMultiplier = 1f;

    private Camera _photoCam;
    private UniversalAdditionalCameraData _photoCamData;
    private AudioListener _photoListener;

    private Vector3 _targetPos;
    private Quaternion _targetRot;
    private float _targetFOV;
    private float _yaw, _pitch, _roll;
    private float _moveSpeed;

    private bool _wasCursorVisible;
    private CursorLockMode _wasCursorLock;
    private Canvas[] _cachedCanvases;

    // Path Recording State
    private List<PathNode> _pathNodes = new List<PathNode>();
    private Camera _cinematicCam;
    private RenderTexture _captureTexture;
    private bool _isPathPlaying = false;
    private string _currentSessionDir;

    private void Awake()
    {
        if (gameCamera == null) gameCamera = Camera.main;

        GameObject photoCamObj = new GameObject("PhotoCam_Internal");
        photoCamObj.transform.SetParent(transform);
        photoCamObj.transform.localPosition = Vector3.zero;
        photoCamObj.transform.localRotation = Quaternion.identity;

        _photoCam = photoCamObj.AddComponent<Camera>();
        _photoCamData = photoCamObj.AddComponent<UniversalAdditionalCameraData>();
        _photoListener = photoCamObj.AddComponent<AudioListener>();

        _photoCam.enabled = false;
        _photoListener.enabled = false;
        _moveSpeed = defaultMoveSpeed;

        SetupCinematicCamera();
    }

    private void SetupCinematicCamera()
    {
        GameObject camObj = new GameObject("Cinematic_Background_Cam");
        camObj.transform.SetParent(transform);
        camObj.transform.localPosition = Vector3.zero;
        camObj.transform.localRotation = Quaternion.identity;

        _cinematicCam = camObj.AddComponent<Camera>();
        _cinematicCam.enabled = false;

        if (gameCamera != null)
        {
            var gameCamData = gameCamera.GetComponent<UniversalAdditionalCameraData>();
            if (gameCamData != null)
            {
                var cinData = camObj.AddComponent<UniversalAdditionalCameraData>();
                cinData.renderPostProcessing = gameCamData.renderPostProcessing;
                cinData.volumeLayerMask = gameCamData.volumeLayerMask;
                cinData.antialiasing = gameCamData.antialiasing;
            }
        }
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current[toggleKey].wasPressedThisFrame) TogglePhotoMode();

        // Path Control (Dostupné i mimo PhotoCam pro spuštění během hry)
        if (Keyboard.current[playPathKey].wasPressedThisFrame && !_isPathPlaying && _pathNodes.Count >= 2)
        {
            StartCoroutine(PlayCinematicPath());
        }

        if (!_isActive) return;

        if (Keyboard.current[hideUIKey].wasPressedThisFrame) ToggleUI();
        if (Keyboard.current[pauseKey].wasPressedThisFrame) TogglePause();
        if (Keyboard.current[captureKey].wasPressedThisFrame) TakeScreenshot();

        // Node Management v režimu PhotoCam
        if (Keyboard.current[addNodeKey].wasPressedThisFrame) AddPathNode();
        if (Keyboard.current[clearPathKey].wasPressedThisFrame) _pathNodes.Clear();

        HandleMovement();
        HandleOptics();
    }

    private void AddPathNode()
    {
        _pathNodes.Add(new PathNode
        {
            position = transform.position,
            rotation = transform.rotation,
            fov = _photoCam.fieldOfView
        });
        Debug.Log($"Path Node {_pathNodes.Count} přidán.");
    }

    private IEnumerator PlayCinematicPath()
    {
        _isPathPlaying = true;
        _cinematicCam.CopyFrom(gameCamera);
        _cinematicCam.enabled = true;

        if (saveFramesToDisk)
        {
            _currentSessionDir = $"Screenshots/Sequence_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
            Directory.CreateDirectory(_currentSessionDir);
            _captureTexture = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            _cinematicCam.targetTexture = _captureTexture;
        }

        float timer = 0f;
        float frameTimer = 0f;
        float frameInterval = 1f / targetFPS;
        int frameCount = 0;

        while (timer <= pathDuration)
        {
            float t = timer / pathDuration;
            UpdateCinematicTransform(t);

            if (saveFramesToDisk)
            {
                frameTimer += Time.unscaledDeltaTime;
                if (frameTimer >= frameInterval)
                {
                    CaptureFrameAsync(frameCount);
                    frameCount++;
                    frameTimer -= frameInterval;
                }
            }

            timer += Time.unscaledDeltaTime;
            yield return null;
        }

        _cinematicCam.enabled = false;
        _cinematicCam.targetTexture = null;
        if (_captureTexture != null) _captureTexture.Release();
        _isPathPlaying = false;
        Debug.Log("Záznam trasy dokončen.");
    }

    private void UpdateCinematicTransform(float t)
    {
        int totalSegments = _pathNodes.Count - 1;
        float scaledT = t * totalSegments;
        int index = Mathf.Clamp(Mathf.FloorToInt(scaledT), 0, totalSegments - 1);
        float segmentT = scaledT - index;

        PathNode p1 = _pathNodes[index];
        PathNode p2 = _pathNodes[index + 1];

        // Slerp / Lerp interpolace mezi dvěma uzly
        _cinematicCam.transform.position = Vector3.Lerp(p1.position, p2.position, Mathf.SmoothStep(0, 1, segmentT));
        _cinematicCam.transform.rotation = Quaternion.Slerp(p1.rotation, p2.rotation, Mathf.SmoothStep(0, 1, segmentT));
        _cinematicCam.fieldOfView = Mathf.Lerp(p1.fov, p2.fov, Mathf.SmoothStep(0, 1, segmentT));
    }

    private void CaptureFrameAsync(int frameIndex)
    {
        RenderTexture activeText = RenderTexture.active;
        RenderTexture.active = _captureTexture;

        _cinematicCam.Render();

        Texture2D tex = new Texture2D(_captureTexture.width, _captureTexture.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, _captureTexture.width, _captureTexture.height), 0, 0);
        tex.Apply();

        RenderTexture.active = activeText;

        byte[] bytes = tex.EncodeToJPG(85); // JPG pro menší I/O blokaci než PNG
        Destroy(tex);

        string filePath = $"{_currentSessionDir}/frame_{frameIndex:D5}.jpg";

        // Zápis na pozadí pro zamezení lagu
        Task.Run(() => File.WriteAllBytesAsync(filePath, bytes));
    }

    // --- PŮVODNÍ METODY (TogglePhotoMode, HandleMovement, atd.) ---

    private void TogglePhotoMode()
    {
        _isActive = !_isActive;

        if (_isActive)
        {
            _wasCursorVisible = Cursor.visible;
            _wasCursorLock = Cursor.lockState;
            _isGamePaused = true;
            ApplyTimeScale();

            if (gameCamera != null)
            {
                transform.position = gameCamera.transform.position;
                transform.rotation = gameCamera.transform.rotation;

                _photoCam.fieldOfView = gameCamera.fieldOfView;
                _photoCam.farClipPlane = gameCamera.farClipPlane;
                _photoCam.nearClipPlane = gameCamera.nearClipPlane;
                _photoCam.cullingMask = gameCamera.cullingMask;
                _photoCam.clearFlags = gameCamera.clearFlags;
                _photoCam.backgroundColor = gameCamera.backgroundColor;

                var gameCamData = gameCamera.GetComponent<UniversalAdditionalCameraData>();
                if (gameCamData != null)
                {
                    _photoCamData.renderPostProcessing = gameCamData.renderPostProcessing;
                    _photoCamData.volumeLayerMask = gameCamData.volumeLayerMask;
                    _photoCamData.antialiasing = gameCamData.antialiasing;
                    _photoCamData.antialiasingQuality = gameCamData.antialiasingQuality;
                    _photoCamData.renderShadows = gameCamData.renderShadows;
                }

                _targetPos = transform.position;
                _targetRot = transform.rotation;
                _targetFOV = _photoCam.fieldOfView;
                Vector3 euler = transform.eulerAngles;
                _yaw = euler.y;
                _pitch = euler.x;
                _roll = 0f;

                gameCamera.gameObject.SetActive(false);
            }

            _photoCam.enabled = true;
            _photoListener.enabled = true;

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            if (gameCamera != null) gameCamera.gameObject.SetActive(true);

            _photoCam.enabled = false;
            _photoListener.enabled = false;

            Time.timeScale = 1f;
            Cursor.visible = _wasCursorVisible;
            Cursor.lockState = _wasCursorLock;

            if (_hideUI) ToggleUI();
        }
    }

    private void TogglePause()
    {
        _isGamePaused = !_isGamePaused;
        ApplyTimeScale();
    }

    private void ApplyTimeScale()
    {
        Time.timeScale = _isGamePaused ? 0f : _timeScaleMultiplier;
    }

    private void ToggleUI()
    {
        _hideUI = !_hideUI;
        _cachedCanvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in _cachedCanvases)
        {
            c.enabled = !_hideUI;
        }
    }

    private void HandleMovement()
    {
        bool isLooking = Mouse.current.rightButton.isPressed;
        Cursor.visible = !isLooking && !_hideUI;
        Cursor.lockState = isLooking ? CursorLockMode.Locked : CursorLockMode.None;

        float dt = Time.unscaledDeltaTime;
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

        if (isLooking)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            _yaw += delta.x * 0.2f;
            _pitch -= delta.y * 0.2f;
        }

        _targetRot = Quaternion.Euler(_pitch, _yaw, _roll);
        transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, 10f * dt);
    }

    private void HandleOptics()
    {
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            _targetFOV -= scroll * 0.05f;
            _targetFOV = Mathf.Clamp(_targetFOV, 10f, 120f);
        }
        _photoCam.fieldOfView = Mathf.Lerp(_photoCam.fieldOfView, _targetFOV, 5f * Time.unscaledDeltaTime);
    }

    private void TakeScreenshot()
    {
        string dir = "Screenshots";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string file = $"{dir}/PhotoCam_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";

        bool wasUiHidden = _hideUI;
        if (!wasUiHidden) ToggleUI();

        ScreenCapture.CaptureScreenshot(file, superSize);
        Debug.Log($"Screenshot ulozen: {file}");

        if (!wasUiHidden) ToggleUI();
    }

    private void OnGUI()
    {
        if (!_isActive || _hideUI) return;

        GUI.backgroundColor = new Color(0, 0, 0, 0.8f);
        GUI.contentColor = Color.white;

        float width = 300;
        float padding = 20;
        Rect r = new Rect(Screen.width - width - padding, padding, width, 450);

        GUILayout.BeginArea(r, GUI.skin.box);
        GUILayout.BeginVertical();

        GUILayout.Label("<b>PHOTOCAM MENU</b>");
        GUILayout.Space(10);

        GUILayout.Label($"State: {(_isGamePaused ? "<color=red>PAUSED</color>" : "<color=green>RUNNING</color>")}");
        if (GUILayout.Button(_isGamePaused ? "RESUME (P)" : "PAUSE (P)")) TogglePause();

        GUILayout.Space(10);
        GUILayout.Label($"Time Scale (Slowmo): {_timeScaleMultiplier:F2}x");
        float newScale = GUILayout.HorizontalSlider(_timeScaleMultiplier, 0.01f, 3f);
        if (Mathf.Abs(newScale - _timeScaleMultiplier) > 0.01f)
        {
            _timeScaleMultiplier = newScale;
            if (!_isGamePaused) ApplyTimeScale();
        }

        GUILayout.Space(15);
        GUILayout.Label($"Move Speed: {_moveSpeed:F1}");
        _moveSpeed = GUILayout.HorizontalSlider(_moveSpeed, 1f, 50f);

        GUILayout.Space(15);
        GUILayout.Label($"<b>CINEMATIC PATH</b> ({_pathNodes.Count} uzlů)");
        if (GUILayout.Button("Add Node (N)")) AddPathNode();
        if (GUILayout.Button("Clear Path (C)")) _pathNodes.Clear();

        GUILayout.Label($"Duration: {pathDuration}s");
        pathDuration = GUILayout.HorizontalSlider(pathDuration, 1f, 60f);

        GUILayout.Space(20);
        if (GUILayout.Button("<b>TAKE SCREENSHOT (K)</b>", GUILayout.Height(40))) TakeScreenshot();

        GUILayout.Space(10);
        GUILayout.Label("Play Path: M (Lze i mimo PhotoCam)");
        GUILayout.Label("Exit: F8");

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}