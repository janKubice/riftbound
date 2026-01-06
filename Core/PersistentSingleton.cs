using UnityEngine;

// Generická třída pro Singletony, které přežívají načítání scén
public abstract class PersistentSingleton<T> : MonoBehaviour where T : Component
{
    public static T Instance { get; private set; }

    protected virtual void Awake()
    {
        if (Instance == null)
        {
            Instance = this as T;
            DontDestroyOnLoad(this.gameObject);
        }
        else if (Instance != this)
        {
            Debug.LogWarning($"Duplicitní instance {typeof(T)} zničena.");
            gameObject.NetDestroy();
        }
    }
}