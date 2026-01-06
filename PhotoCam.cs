using UnityEngine;
using UnityEngine.InputSystem; // Nutný namespace
using System.IO;

public class PhotoCam : MonoBehaviour
{
    [Header("Settings")]
    public float moveSpeed = 10f;
    public float lookSpeed = 0.5f; // Citlivost je u New Input System jiná
    public int superSize = 4;
    
    // U New Input System nepoužíváme KeyCode, ale Key enum, 
    // nebo hardcodujeme klávesy pro jednoduchost debug toolu.

    private float rotationX = 0;
    private float rotationY = 0;

    void Update()
    {
        // Safety check, kdyby nebyla klávesnice/myš
        if (Keyboard.current == null || Mouse.current == null) return;

        // 1. Movement (WASD + QE)
        Vector3 moveInput = Vector3.zero;
        
        if (Keyboard.current.wKey.isPressed) moveInput.z += 1;
        if (Keyboard.current.sKey.isPressed) moveInput.z -= 1;
        if (Keyboard.current.aKey.isPressed) moveInput.x -= 1;
        if (Keyboard.current.dKey.isPressed) moveInput.x += 1;
        if (Keyboard.current.qKey.isPressed) moveInput.y -= 1;
        if (Keyboard.current.eKey.isPressed) moveInput.y += 1;

        transform.Translate(moveInput * moveSpeed * Time.unscaledDeltaTime);

        // 2. Rotation (Right Mouse Button)
        if (Mouse.current.rightButton.isPressed)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            
            rotationX += -mouseDelta.y * lookSpeed;
            rotationY += mouseDelta.x * lookSpeed;
            transform.localRotation = Quaternion.Euler(rotationX, rotationY, 0);
        }

        // 3. Freeze Time (P key)
        if (Keyboard.current.pKey.wasPressedThisFrame)
        {
            Time.timeScale = (Time.timeScale == 0) ? 1 : 0;
        }

        // 4. Capture Screenshot (K key)
        if (Keyboard.current.kKey.wasPressedThisFrame)
        {
            TakeScreenshot();
        }
    }

    void TakeScreenshot()
    {
        string folderPath = "Screenshots";
        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

        string filename = $"{folderPath}/Promo_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
        ScreenCapture.CaptureScreenshot(filename, superSize);
        Debug.Log($"Saved: {filename}");
    }
}