using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.Cinemachine;
using System.Collections;
using Unity.Netcode.Components;

[RequireComponent(typeof(StatusEffectReceiver))]
[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("Pohyb")]
    [SerializeField] private float _moveSpeed = 5.0f;
    [SerializeField] private float _rotationSpeed = 200f; // Nyní je to citlivost myši
    public Vector3 Velocity => new Vector3(_controller.velocity.x, 0, _controller.velocity.z);

    [Header("Air Control")]
    [Range(0, 1)][SerializeField] private float _airControlFactor = 0.5f; // 50% ovladatelnost ve vzduchu

    // NOVÉ: Přidáno pro Sprint
    [Header("Sprint")]
    [SerializeField] private float _sprintSpeed = 8.0f;
    [SerializeField] private float _sprintStaminaCost = 5.0f; // Cena za sekundu

    [Header("Skok")]
    [SerializeField] private float _jumpHeight = 1.2f;
    [SerializeField] private float _jumpStaminaCost = 15.0f; // Jednorázová cena
    private float _lastJumpInputTime;
    private float _jumpBufferDuration = 0.2f;
    private bool _wasGroundedLastFrame = true;
    [Header("Double Jump")]
    private int _currentJumpCount;

    [Header("Úhyb (Dodge)")]
    [SerializeField] private float _dodgeStaminaCost = 20.0f;
    [SerializeField] private float _dodgeDuration = 0.3f;
    [SerializeField] private float _dodgeSpeed = 15.0f;
    [SerializeField] private float _dodgeInvulnerabilityTime = 0.2f; // Musí být <= _dodgeDuration
    private bool _isDodging = false; // Zabrání spamu a blokuje ostatní pohyb během úhybu
    [SerializeField] private AnimationCurve _dodgeCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

    [Header("Sliding")]
    [SerializeField] private float _slideSpeed = 10f;
    [SerializeField] private float _slideDuration = 0.8f;
    [SerializeField] private float _slideHeight = 1.0f; // Výška collideru při skluzu
    [SerializeField] private float _sprintMemoryDuration = 0.5f; 
    private float _lastSprintTime;
    
    private float _originalHeight;
    private Vector3 _originalCenter;
    private bool _isSliding;
    public bool IsSliding => _isSliding;

    [Header("Look (Pitch)")]
    [SerializeField] private Transform _cameraFollowTarget;
    [SerializeField] private float _minPitch = -40f;
    [SerializeField] private float _maxPitch = 80f;

    [Header("Střelba")]
    [SerializeField] private GameObject _projectilePrefab;
    [SerializeField] private Transform _firePoint;

    [Header("Komponenty Hráče")]
    [SerializeField] private GameObject _playerVirtualCameraObject;
    [SerializeField] private WeaponManager _weaponManager;

    [Header("Komponenty pro deaktivaci")]
    [SerializeField] private CharacterController _controller;
    [SerializeField] private PlayerInput _playerInput;
    [SerializeField] private Animator _animator;

    [Header("Vstupní Buffery")]
    [SerializeField] private float _groundedBufferDuration = 0.1f; // <-- NOVÉ (Coyote Time)
    private float _lastGroundedTime; // <-- NOVÉ
    private bool _isGrounded;

    [Header("Komponenty pro efekty")]
    [SerializeField] private PlayerAudio _playerAudio;
    [SerializeField] private PlayerVFX _playerVFX;
    private CinemachineImpulseSource _impulseSource;
    private CinemachineCamera _virtualCamera;
    private StatusEffectReceiver _statusReceiver;

    [Header("Emotes")]
    [SerializeField] private PlayerEmotes _playerEmotes;

    [Header("Shop Levitation")]
    [SerializeField] private float _levitationHeight = 2.0f; // Jak vysoko vyletí
    [SerializeField] private float _levitationSpeed = 0.5f;  // Jak rychle tam vyletí

    private bool _isInShopMode = false;
    private float _shopGroundY; // Uložená výška podlahy

    // --- Odkazy na komponenty ---
    // Odkaz na PlayerAttributes pro kontrolu staminy
    private PlayerAttributes _attributes;
    private PlayerProgression _progression;
    private bool _isFireInputHeld = false;

    // Vstupy
    private Vector2 _moveInput;
    private Vector2 _lookInput;
    private bool _isSprinting = false;
    private float _cameraPitch = 0.0f;

    // Fyzika (gravitace)
    private Vector3 _playerVelocity;
    private float _gravityValue = -9.81f;

    // Optimalizace Animatoru
    private int _forwardSpeedHash;
    private int _rightSpeedHash;
    private int _isSprintingHash;
    private int _isGroundedHash;
    private int _jumpTriggerHash;
    private int _dodgeForwardHash;
    private int _dodgeBackHash;
    private int _dodgeLeftHash;
    private int _dodgeRightHash;

    private float _lastSentForward = 0f;
    private float _lastSentRight = 0f;
    private bool _lastSentSprinting = false;
    private bool _lastSentGrounded = true;
    // NOVÉ: Proměnná pro zamknutí ovládání (Shop, Cutscény, atd.)
    private bool _inputLocked = false;
    private void Awake()
    {
        if (_controller == null) _controller = GetComponent<CharacterController>();
        if (_playerInput == null) _playerInput = GetComponent<PlayerInput>();
        if (_animator == null) _animator = GetComponent<Animator>();
        if (_playerEmotes == null) _playerEmotes = GetComponent<PlayerEmotes>();

        _attributes = GetComponent<PlayerAttributes>();

        _forwardSpeedHash = Animator.StringToHash("ForwardSpeed");
        _rightSpeedHash = Animator.StringToHash("RightSpeed");
        _isSprintingHash = Animator.StringToHash("IsSprinting"); // NOVÉ
        _isGroundedHash = Animator.StringToHash("IsGrounded"); // NOVÉ
        _jumpTriggerHash = Animator.StringToHash("Jump"); // NOVÉ

        _dodgeForwardHash = Animator.StringToHash("DodgeForward");
        _dodgeBackHash = Animator.StringToHash("DodgeBack");
        _dodgeLeftHash = Animator.StringToHash("DodgeLeft");
        _dodgeRightHash = Animator.StringToHash("DodgeRight");

        _playerAudio = GetComponent<PlayerAudio>();
        _playerVFX = GetComponent<PlayerVFX>();
        _impulseSource = GetComponent<CinemachineImpulseSource>();
        _statusReceiver = GetComponent<StatusEffectReceiver>();

        _originalHeight = _controller.height;
        _originalCenter = _controller.center;
    }

    public override void OnDestroy()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned && !NetworkManager.Singleton.IsServer)
        {
            if (!NetworkManager.Singleton.ShutdownInProgress)
            {
                Debug.LogError($"[Security Alert] Objekt {gameObject.name} byl smazán lokálně na klientovi! " +
                               $"To způsobí Invalid Destroy chybu. Prověřte volání v tomto skriptu.");
                Debug.Log($"[Stack Trace] {System.Environment.StackTrace}");
                if (transform.parent != null) Debug.Log($"[Hierarchy] Rodič objektu byl: {transform.parent.name}");
            }
        }
        base.OnDestroy();
    }

    // --- Metody Input Systemu ---

    public void OnMove(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        _moveInput = context.ReadValue<Vector2>();
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        _lookInput = context.ReadValue<Vector2>();
    }

    public void OnFire(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        _isFireInputHeld = context.ReadValue<float>() > 0.5f;
    }

    // NOVÁ METODA PRO SPRINT
    public void OnSprint(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        // context.ReadValue<float>() > 0.5f převede stisk (1) i držení (1) na true, puštění (0) na false
        _isSprinting = context.ReadValue<float>() > 0.5f;
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.action.triggered) return;

        // Pouze zaznamenáme čas stisku pro buffering
        _lastJumpInputTime = Time.time;
    }

    public void OnDodge(InputAction.CallbackContext context)
    {
        // Povolíme pouze pokud jsme Owner, klávesa byla stisknuta,
        // neprovádíme už úhyb a jsme na zemi.
        if (!IsOwner || !context.action.triggered || _isDodging) return;

        if (_attributes == null) _attributes = PlayerAttributes.LocalInstance;
        if (_attributes == null) return;

        if (_attributes.ConsumeStamina(_dodgeStaminaCost))
        {
            // 1. Logika směru
            Vector2 dodgeInput = _moveInput;
            if (dodgeInput.magnitude < 0.1f) dodgeInput = new Vector2(0, -1);
            Vector3 dodgeDirection = transform.TransformDirection(new Vector3(dodgeInput.x, 0, dodgeInput.y)).normalized;

            // 2. Nesmrtelnost (stále separátní volání, to je v pořádku)
            _attributes.SetInvulnerableServerRpc(_dodgeInvulnerabilityTime);

            // 3. Animace
            TriggerDodgeAnimation(dodgeInput.normalized);

            // 4. Pohyb
            StartCoroutine(DodgeRoutine(dodgeDirection));

            // 5. Efekty
            if (IsOwner)
            {
                if (_playerAudio != null) _playerAudio.RequestPlaySoundServerRpc(PlayerAudio.AUDIO_DODGE);
                if (_impulseSource != null) _impulseSource.GenerateImpulse();
            }
        }
        else if (IsOwner && _playerAudio != null)
        {
            // Pokusil se uhnout, ale nemá staminu
            _playerAudio.RequestPlaySoundServerRpc(PlayerAudio.AUDIO_OUT_OF_STAMINA);
        }
    }


    // --- Update cyklus ---

    private void Update()
    {
        if (!IsOwner) return;

        // 1. KONTROLA STUNU (Hard CC)
        // Pokud jsme omráčení, nesmíme nic dělat
        if (_statusReceiver != null && _statusReceiver.IsStunned)
        {
            // Můžeme případně vypnout animátor parametry pro pohyb
            _moveInput = Vector2.zero;
            HandleAnimation(); // Aby se přehrála Idle animace
            HandleGravity();
            return; // Ukončíme Update před pohybem a střelbou
        }

        if (_inputLocked)
        {
            HandleGravity(); // Abychom neviseli ve vzduchu, pokud nechceme
                             // Pokud chceš levitovat, můžeš dát: if (!_inputLocked) HandleGravity();

            // Aplikujeme pouze vertikální pohyb (pád), žádný WASD
            _controller.Move(new Vector3(0, _playerVelocity.y, 0) * Time.deltaTime);

            // Animace musíme stále posílat (že stojíme), jinak se zaseknou
            HandleAnimation();
            return;
        }

        if (_isInShopMode)
        {
            // Cílová výška = zem + 2 metry
            float targetY = _shopGroundY + _levitationHeight;

            // Plynulý přesun (Lerp) aktuální výšky směrem k cíli
            // Používáme Move(), aby CharacterController respektoval kolize (pro jistotu)
            float nextY = Mathf.Lerp(transform.position.y, targetY, Time.deltaTime * _levitationSpeed);
            float diff = nextY - transform.position.y;

            _controller.Move(Vector3.up * diff);

            // Stále posíláme animace (že stojíme/levitujeme), aby se neasekly
            HandleAnimation();
            return; // Ukončíme Update, aby se nepočítala gravitace a WASD
        }

        if (_isSliding)
        {
            // Můžeme sem přidat logiku kamery/rotace pokud chceš během slidu zatáčet,
            // ale CharacterController.Move by se tu volat neměl.
            HandleAnimation();
            return;
        }

        CheckGrounded();

        if (_isDodging)
        {
            HandleGravity(); // Spočítá _playerVelocity.y

            // Aplikujeme POUZE vertikální pohyb (gravitaci).
            // Horizontální pohyb řeší DodgeRoutine.
            _controller.Move(new Vector3(0, _playerVelocity.y, 0) * Time.deltaTime);
            return; // Ukončíme Update, zbytek se neprovede
        }

        // --- Běžná logika (pokud neuhýbáme) ---

        HandleGravity();       // 1. Spočítá _playerVelocity.y (např. -2f)
        HandleJump();          // 2. POKUD skok, přepíše _playerVelocity.y (např. +4.8f)
        HandleRotation();
        HandleSprintStamina();

        // 3. Získáme horizontální pohyb (při stání to bude Vector3.zero)
        Vector3 horizontalMove = GetHorizontalMovement();

        // 4. Vytvoříme finální vertikální pohyb
        Vector3 verticalMove = new Vector3(0, _playerVelocity.y, 0) * Time.deltaTime;

        // 5. Zkombinujeme oba vektory a zavoláme Move() POUZE JEDNOU
        _controller.Move(horizontalMove + verticalMove);

        // Logika pro automatickou střelbu při držení
        if (_isFireInputHeld)
        {
            // Nemůžeme volat RPC každý frame! Musíme zkontrolovat cooldown.
            // Ale cooldown zná WeaponManager, ne PlayerController.
            // ŘEŠENÍ: Prostě zavoláme metodu ve WeaponManageru a ten si cooldown pohlídá.
            if (_weaponManager != null)
            {
                _weaponManager.TryAttackLocalLoop();
            }
        }

        if (_moveInput.magnitude > 0.1f && _playerEmotes.IsEmoting)
        {
            // Pokud se začneme hýbat, řekneme systému, že už neemotujeme
            // (Animator se o přerušení postará sám díky podmínkám v Transitions, 
            // ale flag v kódu je dobré resetovat).
            _playerEmotes.SetEmotingState(false);
        }

        HandleAnimation();
    }
    private void CheckGrounded()
    {
        bool wasGroundedBeforeCheck = _isGrounded; // Uložíme si stav před kontrolou

        // 1. Zkontrolujeme fyzický stav
        if (_controller.isGrounded)
        {
            _lastGroundedTime = Time.time;
            _isGrounded = true;
        }
        // 2. Pokud nejsme fyzicky na zemi, zkontrolujeme buffer
        else
        {
            _isGrounded = (Time.time < _lastGroundedTime + _groundedBufferDuration);
        }

        // --- DETEKCE DOPADU ---
        // Byl stav _isGrounded změněn z false na true v tomto framu?
        // Používáme _wasGroundedLastFrame, abychom to nespouštěli každý frame na zemi.
        if (_isGrounded && !_wasGroundedLastFrame)
        {
            OnLand(); // Zavoláme novou metodu
        }

        if (_isGrounded)
        {
            _currentJumpCount = 0; // Reset skoků na zemi
        }

        _wasGroundedLastFrame = _isGrounded; // Uložíme stav pro příští frame
    }

    /// <summary>
    /// Zavolá se POUZE v momentě dopadu na zem.
    /// </summary>
    private void OnLand()
    {
        if (!IsOwner) return; // Efekty žádá pouze lokální hráč
        GetComponentInChildren<PlayerSquashStretch>()?.TriggerLandSquash();
        // 1. Spustíme VFX (prach)
        if (_playerVFX != null)
        {
            _playerVFX.SpawnVFXServerRpc(PlayerVFX.VFX_Type.LandingDust);
        }

        // 2. Přehrajeme Zvuk 
        if (_playerAudio != null)
        {
            _playerAudio.RequestPlaySoundServerRpc(PlayerAudio.AUDIO_LAND);
        }

        // Spustíme lokální otřes kamery
        if (_impulseSource != null)
        {
            _impulseSource.GenerateImpulse();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsOwner)
        {
            var netTransform = GetComponent<NetworkTransform>();
            if (netTransform != null)
            {
                netTransform.Teleport(transform.position, transform.rotation, transform.localScale);
            }

            StartCoroutine(WaitForCameraRoutine());
        }
    }

    private IEnumerator WaitForCameraRoutine()
    {
        CinemachineCamera cam = null;

        // Čeká, dokud FindFirstObjectByType nevrátí referenci
        while (cam == null)
        {
            cam = FindFirstObjectByType<CinemachineCamera>(FindObjectsInactive.Include);
            if (cam == null)
            {
                yield return null;
            }
        }

        // --- OPRAVA PODZEMNÍ KAMERY ---

        // 1. Vypneme kameru (pro jistotu, aby nepočítala frame z nuly)
        cam.enabled = false;

        // 2. "Hard" teleport samotného objektu kamery na pozici hráče
        // Tím zajistíme, že fyzicky startuje u hlavy hráče, ne v podzemí na 0,0,0
        // (Použijeme _cameraFollowTarget, což je ten bod za krkem/hlavou)
        if (_cameraFollowTarget != null)
        {
            cam.transform.position = _cameraFollowTarget.position;
            cam.transform.rotation = _cameraFollowTarget.rotation;
        }

        // 3. Přiřadíme cíle
        cam.Follow = _cameraFollowTarget;
        cam.LookAt = _cameraFollowTarget;

        // 4. Resetujeme interní stav Cinemachine (aby si nemyslela, že musí interpolovat)
        // Toto volání řekne: "Cíl se teleportoval, zapomeň na předchozí pozici."
        cam.OnTargetObjectWarped(_cameraFollowTarget, Vector3.zero);

        // 5. Zapneme kameru zpět
        cam.enabled = true;
        _playerVirtualCameraObject = cam.gameObject;
        _playerVirtualCameraObject.SetActive(true);

        if (cam.TryGetComponent(out PlayerCameraEffects effects))
        {
            effects.Initialize(_animator);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void HandleGravity()
    {
        // Pokud jsme na zemi a nepadáme, resetujeme rychlost pádu
        if (_isGrounded && _playerVelocity.y < 0)
        {
            _playerVelocity.y = -5f;
        }

        // Aplikujeme gravitaci
        _playerVelocity.y += _gravityValue * Time.deltaTime;
    }

    /// <summary>
    /// Vystřelí hráče vertikálně nahoru danou silou.
    /// </summary>
    /// <param name="force">Síla výstřelu (např. 15f)</param>
    public void ApplyVerticalImpulse(float force)
    {
        if (!IsOwner) return; // Fyziku počítá jen vlastník

        // 1. Nastavíme rychlost směrem nahoru
        _playerVelocity.y = force;

        // 2. DŮLEŽITÉ: Okamžitě řekneme controlleru, že nejsme na zemi.
        // Jinak by HandleGravity() v příštím framu resetovalo rychlost na -5f.
        _isGrounded = false;
        _lastGroundedTime = 0f; // Vynulujeme coyote time
        _wasGroundedLastFrame = false;

        // 3. Resetujeme animaci, aby to vypadalo jako skok/pád
        if (_animator != null)
        {
            _animator.SetBool(_isGroundedHash, false);
        }
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // Optimalizace: Neřešit kolize s podlahou, jen s objekty do stran
        if (hit.moveDirection.y < -0.3f) return;

        // Zkusíme najít DestructibleProp na objektu, do kterého jsme narazili
        if (hit.gameObject.TryGetComponent<DestructibleProp>(out var prop))
        {
            // Získáme aktuální rychlost hráče (magnitude vektoru velocity z CharacterControlleru)
            float impactForce = _controller.velocity.magnitude;

            // Pošleme informaci bedně
            prop.CheckImpact(impactForce);
        }
    }

    /// <summary>
    /// UPRAVENO: Používá aktuální rychlost (včetně sprintu)
    /// </summary>
    private Vector3 GetHorizontalMovement()
    {
        float currentSpeed = _isSprinting ? _sprintSpeed : _moveSpeed;
        Vector3 moveDirection = new Vector3(_moveInput.x, 0, _moveInput.y);
        moveDirection = transform.TransformDirection(moveDirection);

        if (!_isGrounded)
        {
            currentSpeed *= _airControlFactor;
        }

        if (_statusReceiver != null)
        {
            // Tady se aplikuje ten "Slow" nebo "Haste"
            currentSpeed *= _statusReceiver.CurrentSpeedMultiplier;
        }

        // 2. Progression (Trvalé vylepšení rychlosti)
        // Zde přičítáme procenta (např. baseSpeed * 1.2) nebo flat hodnotu (baseSpeed + 2).
        // Dle StatType.MoveSpeed implementace v GetStatMultiplier (1.0 + bonus).
        if (_progression != null)
            currentSpeed *= _progression.GetStatMultiplier(StatType.MoveSpeed);

        return moveDirection * currentSpeed * Time.deltaTime;
    }

    /// <summary>
    /// NOVÉ: Kontroluje a spotřebovává staminu při sprintu
    /// </summary>
    private void HandleSprintStamina()
    {
        // Chceme sprintovat? (držíme klávesu)
        if (_isSprinting)
        {
            // Sprintujeme pouze, pokud se hýbeme dopředu
            bool isMovingForward = _moveInput.magnitude > 0.1f;

            // Získáme lokální instanci atributů (měla by být naše)
            if (_attributes == null) _attributes = PlayerAttributes.LocalInstance;
            if (_attributes == null)
            {
                _isSprinting = false; // Nemáme atributy, nemůžeme sprintovat
                return;
            }

            // Máme dostatek staminy A hýbeme se dopředu?
            if (isMovingForward && _attributes.ConsumeStamina(_sprintStaminaCost * Time.deltaTime))
            {
                // Úspěch - sprintujeme
                _isSprinting = true;
            }
            else
            {
                // Ne, došla stamina nebo stojíme/couváme -> vypneme sprint
                _isSprinting = false;
            }

            _lastSprintTime = Time.time;
        }

        // Pokud klávesu nedržíme, _isSprinting je už false z OnSprint()
        // a stamina se přirozeně regeneruje (díky PlayerAttributes.cs)
    }


    private void HandleRotation()
    {
        float mouseX = _lookInput.x * _rotationSpeed * Time.deltaTime;
        float mouseY = _lookInput.y * _rotationSpeed * Time.deltaTime;

        transform.Rotate(Vector3.up, mouseX);

        _cameraPitch -= mouseY;
        _cameraPitch = Mathf.Clamp(_cameraPitch, _minPitch, _maxPitch);

        if (_cameraFollowTarget != null)
        {
            _cameraFollowTarget.localRotation = Quaternion.Euler(_cameraPitch, 0, 0);
        }
    }

    private void HandleJump()
    {
        int maxJumps = 1;
        if (_progression != null)
            maxJumps += (int)_progression.GetStatBonus(StatType.JumpCount);

        // Zkontrolujeme, zda byl skok stisknut nedávno
        bool jumpInputBuffered = Time.time < _lastJumpInputTime + _jumpBufferDuration;

        // Musíme být na zemi A mít "nabufferovaný" vstup
        if (jumpInputBuffered && (_isGrounded || _currentJumpCount < maxJumps))
        {
            // --- Zde je "Provedení logiky skoku..." ---
            if (_attributes == null) _attributes = PlayerAttributes.LocalInstance;
            if (_attributes == null) return;

            // Ověříme staminu
            if (_attributes.ConsumeStamina(_jumpStaminaCost))
            {
                _currentJumpCount++;

                _playerVelocity.y = Mathf.Sqrt(_jumpHeight * -2f * _gravityValue);

                // Spustíme animaci (pošleme přes server všem)
                TriggerAnimationServerRpc(_jumpTriggerHash);
                GetComponentInChildren<PlayerSquashStretch>()?.TriggerJumpSquash();

                if (_currentJumpCount > 1) _playerVelocity.y = Mathf.Sqrt(_jumpHeight * -2f * _gravityValue); // Můžeš dát menší výšku pro druhý skok

                if (IsOwner && _playerAudio != null)
                {
                    _playerAudio.RequestPlaySoundServerRpc(PlayerAudio.AUDIO_JUMP);
                }

                // ZNEPLATNÍME BUFFERY
                _lastJumpInputTime = 0f; // Vynulování bufferu skoku
                _lastGroundedTime = 0f; // Vynulujeme Coyote time, abychom nemohli hned uskočit/skočit znovu
                _isGrounded = false; // Vynutíme stav ve vzduchu
            }
            else if (IsOwner && _playerAudio != null)
            {
                // Pokusil se skočit, ale nemá staminu
                _playerAudio.RequestPlaySoundServerRpc(PlayerAudio.AUDIO_OUT_OF_STAMINA);
            }

            // --- Konec logiky skoku ---

            // Vynulování bufferu, aby se skok neprovedl vícekrát
            _lastJumpInputTime = 0f;
        }
    }

    // Přidej do PlayerController.cs

    public void OnEmote1(InputAction.CallbackContext context)
    {
        if (context.performed && _playerEmotes != null)
            _playerEmotes.TryPlayEmote(0); // Index 0 v listu
    }

    public void OnEmote2(InputAction.CallbackContext context)
    {
        if (context.performed && _playerEmotes != null)
            _playerEmotes.TryPlayEmote(1); // Index 1 v listu
    }

    public void OnEmote3(InputAction.CallbackContext context)
    {
        if (context.performed && _playerEmotes != null)
            _playerEmotes.TryPlayEmote(2); // Index 2 v listu
    }

    // Metoda volaná z Input Systemu (nutno přidat akci "Crouch" do InputActions)
    public void OnCrouch(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        // Pokud zmáčknu tlačítko, jsem na zemi a nejsem už ve skluzu
        if (context.performed && _isGrounded && !_isSliding)
        {
            // LOGIKA BUFFERU:
            // 1. Držíme sprint?
            // 2. NEBO jsme sprint pustili před méně než X sekundami? (_sprintMemoryDuration)
            bool wasSprintingRecently = _isSprinting || (Time.time - _lastSprintTime < _sprintMemoryDuration);

            // Musíme se také hýbat dopředu (y > 0.1f), abychom neslidovali z místa
            if (wasSprintingRecently && _moveInput.y > 0.1f)
            {
                StartCoroutine(SlideRoutine());
            }
        }
    }

    private IEnumerator SlideRoutine()
    {
        _isSliding = true;

        // Zmenšení collideru
        _controller.height = _slideHeight;
        _controller.center = new Vector3(_originalCenter.x, _slideHeight / 2f, _originalCenter.z);

        // Směr podle terénu (viz předchozí oprava)
        Vector3 slideDirection = transform.forward;
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 2.0f))
        {
            slideDirection = Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized;
        }

        // NOVÉ: Zachování hybnosti
        // Pokud sprintuju rychleji než je nastavený slide, použiju svou aktuální rychlost jako startovní.
        float currentSpeed = Velocity.magnitude;
        float startSlideSpeed = Mathf.Max(_slideSpeed, currentSpeed); 

        float timer = 0f;

        while (timer < _slideDuration)
        {
            // Lerpujeme z (Možná Boostnuté) rychlosti do nuly nebo do chůze
            // Díky tomu bude slide působit dynamičtěji na začátku
            float speed = Mathf.Lerp(startSlideSpeed, _moveSpeed, timer / _slideDuration);

            Vector3 moveVector = slideDirection * speed;
            moveVector.y += _gravityValue; // Gravitace

            _controller.Move(moveVector * Time.deltaTime);

            if (_controller.velocity.magnitude < 1f && timer > 0.1f) break;

            timer += Time.deltaTime;
            yield return null;
        }

        _controller.height = _originalHeight;
        _controller.center = _originalCenter;
        _isSliding = false;
    }

    private IEnumerator DodgeRoutine(Vector3 dodgeDirection)
    {
        _isDodging = true;
        float timer = 0f;

        if (IsOwner && _playerVFX != null)
        {
            _playerVFX.ToggleVFXServerRpc(PlayerVFX.VFX_Type.DodgeTrail, true);
        }

        while (timer < _dodgeDuration)
        {
            // Aplikujeme pohyb. Gravitaci řeší HandleGravity() v Update.
            float speedMultiplier = _dodgeCurve.Evaluate(timer / _dodgeDuration);
            _controller.Move(dodgeDirection * _dodgeSpeed * speedMultiplier * Time.deltaTime);

            timer += Time.deltaTime;
            yield return null; // Počkáme na další frame
        }

        _isDodging = false;
        if (IsOwner && _playerVFX != null)
        {
            _playerVFX.ToggleVFXServerRpc(PlayerVFX.VFX_Type.DodgeTrail, false);
        }
    }

    /// <summary>
    /// UPRAVENO: Posílá na server i stav sprintu
    /// </summary>
    private void HandleAnimation()
    {
        if (_animator == null) return;

        // 1. Nastavíme animátor lokálně
        _animator.SetFloat(_forwardSpeedHash, _moveInput.y);
        _animator.SetFloat(_rightSpeedHash, _moveInput.x);
        _animator.SetBool(_isSprintingHash, _isSprinting);
        _animator.SetBool(_isGroundedHash, _isGrounded);

        // 2. Kontrola, zda se hodnoty změnily
        if (Mathf.Abs(_moveInput.y - _lastSentForward) > 0.05f ||
            Mathf.Abs(_moveInput.x - _lastSentRight) > 0.05f ||
            _isSprinting != _lastSentSprinting ||
            _isGrounded != _lastSentGrounded)
        {
            _lastSentForward = _moveInput.y;
            _lastSentRight = _moveInput.x;
            _lastSentSprinting = _isSprinting;
            _lastSentGrounded = _isGrounded;

            // 3. Odešleme hodnoty na server
            UpdateAnimationServerRpc(_lastSentForward, _lastSentRight, _lastSentSprinting, _lastSentGrounded); // UPRAVENO
        }
    }

    private void TriggerDodgeAnimation(Vector2 normalizedDodgeInput)
    {
        // Určíme dominantní směr pro animaci
        // (Používáme normalizovaný vstup, abychom rozlišili 4 směry)

        // Více dopředu/dozadu než do stran
        if (Mathf.Abs(normalizedDodgeInput.y) > Mathf.Abs(normalizedDodgeInput.x))
        {
            if (normalizedDodgeInput.y > 0)
                TriggerAnimationServerRpc(_dodgeForwardHash);
            else
                TriggerAnimationServerRpc(_dodgeBackHash);
        }
        // Více do stran než dopředu/dozadu
        else
        {
            if (normalizedDodgeInput.x < 0)
                TriggerAnimationServerRpc(_dodgeLeftHash);
            else
                TriggerAnimationServerRpc(_dodgeRightHash);
        }
    }


    [ServerRpc]
    private void UpdateAnimationServerRpc(float forward, float right, bool isSprinting, bool isGrounded) // UPRAVENO
    {
        // 4. Server nastaví hodnoty na SVOJÍ kopii animátoru
        _animator.SetFloat(_forwardSpeedHash, forward);
        _animator.SetFloat(_rightSpeedHash, right);
        _animator.SetBool(_isSprintingHash, isSprinting);
        _animator.SetBool(_isGroundedHash, isGrounded); // NOVÉ

        // 5. NetworkAnimator rozešle změny klientům
    }

    /// <summary>
    /// Klient žádá server, aby spustil trigger u všech.
    /// Používáme int (hash) místo stringu, je to efektivnější.
    /// </summary>
    [ServerRpc]
    private void TriggerAnimationServerRpc(int triggerHash)
    {
        // Server řekne všem klientům (včetně původního), aby přehráli trigger
        TriggerAnimationClientRpc(triggerHash);
    }

    /// <summary>
    /// Běží na všech klientech. Spustí animaci.
    /// </summary>
    [ClientRpc]
    private void TriggerAnimationClientRpc(int triggerHash)
    {
        // Lokálně spustíme trigger
        if (_animator != null)
        {
            _animator.SetTrigger(triggerHash);
        }
    }

    public void SetInputLocked(bool locked)
    {
        _inputLocked = locked;

        // Pokud zamykáme, okamžitě vynulujeme animace, aby postava neběžela na místě
        if (_inputLocked && _animator != null)
        {
            _animator.SetFloat(_forwardSpeedHash, 0f);
            _animator.SetFloat(_rightSpeedHash, 0f);
            _animator.SetBool(_isSprintingHash, false);
        }
    }

    public void SetShopMode(bool active)
    {
        _isInShopMode = active;

        if (active)
        {
            // 1. Uložíme si, kde je zem (aktuální pozice hráče)
            _shopGroundY = transform.position.y;

            // 2. Vynulujeme animace, aby neběžel ve vzduchu
            if (_animator != null)
            {
                _animator.SetFloat(_forwardSpeedHash, 0f);
                _animator.SetFloat(_rightSpeedHash, 0f);
                _animator.SetBool(_isSprintingHash, false);
                // Můžeme nastavit, že není na zemi, aby animátor mohl přejít do "Floating/Falling" animace, pokud ji máš
                _animator.SetBool(_isGroundedHash, false);
            }

            // 3. Resetujeme rychlost, aby nevystřelil setrvačností
            _playerVelocity = Vector3.zero;
        }
    }
}