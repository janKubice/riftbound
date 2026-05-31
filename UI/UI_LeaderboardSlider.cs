using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class UI_LeaderboardSlider : MonoBehaviour
{
    [SerializeField] private RectTransform panelTransform;
    [SerializeField] private Button toggleButton;
    
    [SerializeField] private Vector2 hiddenPosition; // Např. x = 400 (schované za hranou obrazovky)
    [SerializeField] private Vector2 visiblePosition; // Např. x = 0 (vysunuté na obrazovce)
    [SerializeField] private float speed = 5f;

    private bool isOpen = false;
    private Coroutine moveCoroutine;

    private void Start()
    {
        panelTransform.anchoredPosition = hiddenPosition;
        toggleButton.onClick.AddListener(ToggleLeaderboard);
    }

    private void ToggleLeaderboard()
    {
        isOpen = !isOpen;
        
        // Pokud se načítají data ze Steamu, zavolej tvou načítací funkci zde
        if (isOpen) { LoadSteamLeaderboards(); }

        if (moveCoroutine != null) StopCoroutine(moveCoroutine);
        Vector2 targetPos = isOpen ? visiblePosition : hiddenPosition;
        moveCoroutine = StartCoroutine(MovePanel(targetPos));
    }

    private IEnumerator MovePanel(Vector2 target)
    {
        while (Vector2.Distance(panelTransform.anchoredPosition, target) > 0.1f)
        {
            panelTransform.anchoredPosition = Vector2.Lerp(panelTransform.anchoredPosition, target, Time.deltaTime * speed);
            yield return null;
        }
        panelTransform.anchoredPosition = target;
    }

    private void LoadSteamLeaderboards()
    {
        // Tvoje stávající logika pro stažení dat ze Steamu a naplnění řádků v ScrollRect
    }
}