using UnityEngine;
using UnityEngine.SceneManagement;

// AppManager controls the main state of the application, e.g., scene transitions
public class AppManager : PersistentSingleton<AppManager>
{
    private const string MainMenuSceneName = "MainMenuScene";
    private const string GameSceneName = "GameScene";

 

    // Call this function after initializing all systems (e.g., Steam)
    public void GoToMainMenu()
    {
        SceneManager.LoadScene(MainMenuSceneName);
        MusicManager.Instance.PlayMenuMusic();
    }

    public void GoToGameScene()
    {
        SceneManager.LoadScene(GameSceneName);
        MusicManager.Instance.SetState(MusicManager.MusicState.Exploration);
    }
    public void ExitGame()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
}