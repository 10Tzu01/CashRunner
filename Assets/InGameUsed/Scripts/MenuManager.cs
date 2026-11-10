using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using TMPro;

public class MenuManager : MonoBehaviour
{
    [Header("Menu Panels")]
    public GameObject mainMenuPanel;       
    public GameObject settingsMenuPanel;   
    public GameObject pauseMenuPanel;      

    [Header("Game Over")]
    public GameObject restartMenuPanel;    
    public TextMeshProUGUI restartScoreText;  

   
    private static bool skipMainMenuOnce = false;
    private bool isGameOver = false;

    void Start()
    {
        if (skipMainMenuOnce)
        {
            skipMainMenuOnce = false;
            StartGame();               // straight back into the game
        }
        else
        {
            OpenMainMenu();
        }
    }

    void Update()
    {
        // Player ran out of lives -> show the restart page (only once)
        if (!isGameOver && GameData.playerLives <= 0 && !mainMenuPanel.activeSelf)
        {
            OpenGameOver();
            return;
        }
        if (isGameOver) return;        // no pause key on the game over screen

        // Press 9 only works while playing (not on the start menu)
        if (mainMenuPanel.activeSelf) return;

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.digit9Key.wasPressedThisFrame)
        {
            if (pauseMenuPanel.activeSelf)
                ClosePauseMenu();
            else
                OpenPauseMenu();
        }
    }

    // ---------- Start Menu ----------

    public void OpenMainMenu()
    {
        mainMenuPanel.SetActive(true);
        settingsMenuPanel.SetActive(false);
        pauseMenuPanel.SetActive(false);
        if (restartMenuPanel) restartMenuPanel.SetActive(false);
        Pause(true);
    }

    public void StartGame()
    {
        if (restartMenuPanel) restartMenuPanel.SetActive(false);
        mainMenuPanel.SetActive(false);
        settingsMenuPanel.SetActive(false);
        pauseMenuPanel.SetActive(false);
        Pause(false);
    }

    // ---------- Options from START MENU ----------

    public void OpenOptionsMenu()       // Start Menu → Options button
    {
        mainMenuPanel.SetActive(false);
        settingsMenuPanel.SetActive(true);
        pauseMenuPanel.SetActive(false);
        Pause(true);
    }

    public void BackToMainMenu()        // Options → Back button (returns to start menu)
    {
        settingsMenuPanel.SetActive(false);
        pauseMenuPanel.SetActive(false);
        mainMenuPanel.SetActive(true);
        Pause(true);
    }

    // ---------- In-game Pause Menu (key 9) ----------

    public void OpenPauseMenu()
    {
        mainMenuPanel.SetActive(false);
        settingsMenuPanel.SetActive(false);
        pauseMenuPanel.SetActive(true);
        Pause(true);
    }

    public void ClosePauseMenu()       
    {
        pauseMenuPanel.SetActive(false);
        settingsMenuPanel.SetActive(false);
        mainMenuPanel.SetActive(false);
        Pause(false);
    }

    // ---------- Game Over / Restart ----------

    public void OpenGameOver()
    {
        isGameOver = true;
        mainMenuPanel.SetActive(false);
        settingsMenuPanel.SetActive(false);
        pauseMenuPanel.SetActive(false);

        if (restartScoreText) restartScoreText.text = "Score: " + GameData.playerScore;
        if (restartMenuPanel) restartMenuPanel.SetActive(true);
        else Debug.LogError("[MenuManager] Restart Menu Panel is not assigned.", this);

        Pause(true);
    }

    public void RestartGame()         
    {
        GameData.ResetData();
        skipMainMenuOnce = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ---------- Quit ----------

    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ---------- Shared pause helper ----------

    private void Pause(bool paused)
    {
        GameData.GamePause = paused;
        Time.timeScale = paused ? 0f : 1f;
        Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = paused;
    }
}