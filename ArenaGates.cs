using UnityEngine;
using Unity.Netcode;

public class ArenaGates : NetworkBehaviour
{
    [Tooltip("Objekty mříží, které mají být aktivní během boje")]
    [SerializeField] private GameObject[] _gateObjects;

    private void Start()
    {
        // Defaultně vypnuto, pokud neběží boj
        SetGates(false);

        if (ArenaManager.Instance != null)
        {
            ArenaManager.Instance.OnStateChanged += HandleStateChanged;
            // Inicializace podle aktuálního stavu (pokud se připojíme pozdě)
            HandleStateChanged(ArenaManager.Instance.CurrentState.Value);
        }
    }

    private void OnDestroy()
    {
        if (ArenaManager.Instance != null)
        {
            ArenaManager.Instance.OnStateChanged -= HandleStateChanged;
        }
    }

    private void HandleStateChanged(ArenaState newState)
    {
        // Mříže jsou dole (aktivní) pouze při Fighting a Countdown
        bool shouldBeClosed = (newState == ArenaState.Fighting || newState == ArenaState.Countdown);
        SetGates(shouldBeClosed);
    }

    private void SetGates(bool active)
    {
        foreach (var gate in _gateObjects)
        {
            if (gate != null) gate.SetActive(active);
        }
    }
}