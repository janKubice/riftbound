using UnityEngine;
using Unity.Netcode;
using System.Collections;
using UnityEngine.InputSystem;
using Unity.Netcode.Components; // DŮLEŽITÉ: Nutné pro nový Input System

[RequireComponent(typeof(NetworkObject))]
public class MultiSittableObject : NetworkBehaviour, IInteractable
{
    [Header("Nastavení")]
    [Tooltip("Seznam transformů, kam si lze sednout. Ujisti se, že jsou umístěny tam, kde má mít hráč nohy (pivot).")]
    [SerializeField] private Transform[] _sitPoints;

    [SerializeField] private string _sitPrompt = "E - Sednout";
    [SerializeField] private string _fullPrompt = "Obsazeno";

    // Minimální doba sezení
    private float _minSitDuration = 0.5f;

    // Sledování obsazenosti
    private NetworkList<ulong> _seatOccupants;

    // Lokální pomocná proměnná
    private float _sitStartTime;

    private void Awake()
    {
        _seatOccupants = new NetworkList<ulong>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            for (int i = 0; i < _sitPoints.Length; i++)
            {
                _seatOccupants.Add(ulong.MaxValue);
            }
        }
    }

    private void LateUpdate() // Používáme LateUpdate pro potlačení třesu
    {
        if (!IsSpawned) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        int mySeatIndex = GetSeatIndexForPlayer(localId);

        if (mySeatIndex != -1)
        {
            CheckInputForStandUp(localId, mySeatIndex);

            // HARD FIX POZICE: Neustále držíme hráče na místě
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(NetworkManager.Singleton.LocalClient.PlayerObject.NetworkObjectId, out NetworkObject playerObj))
            {
                // Jen pokud je to můj lokální hráč
                if (playerObj.IsOwner) 
                {
                    playerObj.transform.position = _sitPoints[mySeatIndex].position;
                    // Rotaci neřešíme tady, pokud chceme volný pohled myší (viz předchozí úprava PlayerController)
                }
            }
        }
    }

    private void CheckInputForStandUp(ulong playerId, int seatIndex)
    {
        // Pokud sedíme příliš krátce, ignorujeme input
        if (Time.time < _sitStartTime + _minSitDuration) return;

        bool wantsToStand = false;

        // --- Kontrola Klávesnice (WASD + Space) ---
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed ||
                Keyboard.current.sKey.isPressed ||
                Keyboard.current.aKey.isPressed ||
                Keyboard.current.dKey.isPressed ||
                Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                wantsToStand = true;
            }
        }

        // --- Kontrola Gamepadu (Levá páčka + South Button/X/A) ---
        if (!wantsToStand && Gamepad.current != null)
        {
            // Deadzone 0.15f pro páčku
            if (Gamepad.current.leftStick.ReadValue().magnitude > 0.15f ||
                Gamepad.current.buttonSouth.wasPressedThisFrame)
            {
                wantsToStand = true;
            }
        }

        // Pokud hráč projevil snahu se pohnout, zvedneme ho
        if (wantsToStand)
        {
            RequestStandUpServerRpc();
        }
    }

    // --- Interaction ---

    public string InteractionPrompt
    {
        get
        {
            ulong localId = NetworkManager.Singleton.LocalClientId;
            if (IsPlayerSittingHere(localId)) return "";
            if (HasFreeSeat()) return _sitPrompt;
            return _fullPrompt;
        }
    }

    public void Interact(NetworkObject interactor)
    {
        ulong playerId = interactor.OwnerClientId;
        ulong playerObjectId = interactor.NetworkObjectId;

        int currentSeatIndex = GetSeatIndexForPlayer(playerId);
        if (currentSeatIndex != -1)
        {
            RequestStandUpServerRpc();
            return;
        }

        int bestSeatIndex = GetClosestFreeSeatIndex(interactor.transform.position);
        if (bestSeatIndex != -1)
        {
            SitDownLogic(playerId, playerObjectId, bestSeatIndex);
        }
    }

    // --- Server Logic ---

    [ServerRpc(RequireOwnership = false)]
    private void RequestStandUpServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        int seatIndex = GetSeatIndexForPlayer(senderId);

        if (seatIndex != -1)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(senderId, out var client))
            {
                if (client.PlayerObject != null)
                {
                    StandUpLogic(senderId, client.PlayerObject.NetworkObjectId, seatIndex);
                }
            }
        }
    }

    private void SitDownLogic(ulong playerId, ulong playerObjectId, int seatIndex)
    {
        _seatOccupants[seatIndex] = playerId;
        SitDownClientRpc(playerObjectId, seatIndex);
    }

    private void StandUpLogic(ulong playerId, ulong playerObjectId, int seatIndex)
    {
        _seatOccupants[seatIndex] = ulong.MaxValue;
        StandUpClientRpc(playerObjectId, seatIndex);
    }

    // --- Client RPCs ---

    [ClientRpc]
    private void SitDownClientRpc(ulong playerObjectId, int seatIndex)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerObjectId, out NetworkObject playerObj))
        {
            Transform seat = _sitPoints[seatIndex];
            if (playerObj.IsOwner) _sitStartTime = Time.time;
            StartCoroutine(SitDownRoutine(playerObj, seat));
        }
    }

    private IEnumerator SitDownRoutine(NetworkObject playerObj, Transform seat)
    {
        // 1. Zastavení PlayerControlleru (přepnutí do režimu sezení)
        var pc = playerObj.GetComponent<PlayerController>();
        if (pc) pc.SetSittingState(true);

        // 2. Vypnutí fyziky
        var cc = playerObj.GetComponent<CharacterController>();
        if (cc) cc.enabled = false;

        // 3. Vypnutí NetworkTransform - KLÍČOVÉ PROTI TŘESENÍ
        // Hledáme NetworkTransform nebo ClientNetworkTransform
        var netTransform = playerObj.GetComponent<NetworkTransform>();
        if (netTransform) netTransform.enabled = false;
        
        // Vypnutí kolizí
        var benchCol = GetComponent<Collider>();
        var playerCol = playerObj.GetComponent<Collider>();
        if (benchCol && playerCol) Physics.IgnoreCollision(playerCol, benchCol, true);

        // Vypnutí procedurálních animací (Wobble, Lean)
        var wobble = playerObj.GetComponent<WobbleBone>();
        if (wobble) wobble.enabled = false;

        yield return new WaitForEndOfFrame();

        // 4. Teleport a Animace
        playerObj.transform.position = seat.position;
        playerObj.transform.rotation = seat.rotation;

        var anim = playerObj.GetComponent<Animator>();
        if (anim) 
        {
            anim.SetBool("IsSitting", true);
            // Pojistka: Vynutíme okamžitý přechod, pokud existuje state "Sit"
            // anim.Play("Sit"); 
        }
    }

    [ClientRpc]
    private void StandUpClientRpc(ulong playerObjectId, int seatIndex)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerObjectId, out NetworkObject playerObj))
        {
            Transform seat = _sitPoints[seatIndex];
            StartCoroutine(StandUpRoutine(playerObj, seat));
        }
    }

    private IEnumerator StandUpRoutine(NetworkObject playerObj, Transform seat)
    {
        // Pozice pro vstání (trochu výš, aby nepropadl podlahou)
        Vector3 standPos = seat.position + seat.forward * 1.0f + Vector3.up * 0.1f;

        // 1. Reset Animace
        var anim = playerObj.GetComponent<Animator>();
        if (anim)
        {
            anim.SetBool("IsSitting", false);
            // KLÍČOVÉ: Vynutíme "Grounded" a "Idle", aby nepřeskočil do pádu
            anim.SetBool("IsGrounded", true); 
            anim.Play("Movement"); // Nebo název tvého Blend Tree pro pohyb
        }

        // 2. Teleportace
        playerObj.transform.position = standPos;
        
        // 3. Synchronizace fyziky (Unity hack pro CharacterController)
        Physics.SyncTransforms(); 

        yield return new WaitForEndOfFrame();

        // 4. Zapnutí NetworkTransform
        var netTransform = playerObj.GetComponent<NetworkTransform>();
        if (netTransform) 
        {
            netTransform.Teleport(standPos, playerObj.transform.rotation, playerObj.transform.localScale);
            netTransform.enabled = true;
        }

        // 5. Zapnutí Kolizí a CharacterControlleru
        var benchCol = GetComponent<Collider>();
        var playerCol = playerObj.GetComponent<Collider>();
        if (benchCol && playerCol) Physics.IgnoreCollision(playerCol, benchCol, false);

        var cc = playerObj.GetComponent<CharacterController>();
        if (cc) cc.enabled = true;
        
        // Zapnutí procedurálních animací
        var wobble = playerObj.GetComponent<WobbleBone>();
        if (wobble) wobble.enabled = true;

        // 6. Vrácení kontroly hráči
        var pc = playerObj.GetComponent<PlayerController>();
        if (pc) pc.SetSittingState(false);
    }

    // --- Helpers ---

    private bool IsPlayerSittingHere(ulong playerId)
    {
        for (int i = 0; i < _seatOccupants.Count; i++)
        {
            if (_seatOccupants[i] == playerId) return true;
        }
        return false;
    }

    private int GetSeatIndexForPlayer(ulong playerId)
    {
        for (int i = 0; i < _seatOccupants.Count; i++)
        {
            if (_seatOccupants[i] == playerId) return i;
        }
        return -1;
    }

    private bool HasFreeSeat()
    {
        for (int i = 0; i < _seatOccupants.Count; i++)
        {
            if (_seatOccupants[i] == ulong.MaxValue) return true;
        }
        return false;
    }

    private int GetClosestFreeSeatIndex(Vector3 playerPos)
    {
        int bestIndex = -1;
        float closestDist = float.MaxValue;

        for (int i = 0; i < _sitPoints.Length; i++)
        {
            if (_seatOccupants[i] != ulong.MaxValue) continue;

            float d = Vector3.Distance(playerPos, _sitPoints[i].position);
            if (d < closestDist)
            {
                closestDist = d;
                bestIndex = i;
            }
        }
        return bestIndex;
    }
}