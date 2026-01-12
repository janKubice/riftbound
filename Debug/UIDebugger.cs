using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class UIDebugger : MonoBehaviour
{
    void Update()
    {
        // Pokud klikneš levým tlačítkem
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            DebugUI();
        }
    }

    void DebugUI()
    {
        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = Mouse.current.position.ReadValue()
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        Debug.Log($"--- KLIK NA POZICI {pointerData.position} ---");
        
        if (results.Count == 0)
        {
            Debug.Log("UI Raycast netrefil NIC. (Chybí EventSystem? Nebo GraphicRaycaster?)");
        }

        foreach (RaycastResult result in results)
        {
            Debug.Log($"Trefeno: {result.gameObject.name} (Depth: {result.depth}, SortingLayer: {result.sortingLayer})");
        }
    }
}