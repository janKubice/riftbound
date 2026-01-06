using UnityEngine;

public class InstantFreeze : MonoBehaviour
{
    [Header("Settings")]
    public bool freezeOnStart = true;

    void Awake()
    {
        // Awake se volá dříve než Start a Update.
        // Okamžitě zastaví herní čas.
        if (freezeOnStart)
        {
            Time.timeScale = 0f;
        }
    }
}