using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages player progression, level checkpoints, and save states across play sessions.
/// Saves data persistently using PlayerPrefs.
/// </summary>
public static class GameProgressManager
{
    private const string CheckpointSceneKey = "backrooms.checkpoint.scene";
    private const string CheckpointLevelNumKey = "backrooms.checkpoint.level_num";
    private const string HighestLevelKey = "backrooms.highest_unlocked_level";
    private const string HasCompletedLevel0Key = "backrooms.completed_level_0";

    public const string Level0SceneName = "MainLevel";
    public const string Level94SceneName = "Level94";
    public const string MenuSceneName = "Menu";

    public static event Action OnProgressChanged;

    /// <summary>
    /// Checks if a valid checkpoint beyond the start of Level 0 exists.
    /// </summary>
    public static bool HasCheckpoint()
    {
        string scene = PlayerPrefs.GetString(CheckpointSceneKey, string.Empty);
        int levelNum = PlayerPrefs.GetInt(CheckpointLevelNumKey, 0);
        return !string.IsNullOrEmpty(scene) && levelNum > 0;
    }

    /// <summary>
    /// Gets the scene name saved at the current checkpoint.
    /// </summary>
    public static string GetCheckpointScene()
    {
        return PlayerPrefs.GetString(CheckpointSceneKey, Level0SceneName);
    }

    /// <summary>
    /// Gets the current checkpoint level number (0 for Level 0, 94 for Level 94).
    /// </summary>
    public static int GetCheckpointLevelNumber()
    {
        return PlayerPrefs.GetInt(CheckpointLevelNumKey, 0);
    }

    /// <summary>
    /// Gets the highest level number reached or unlocked by the player.
    /// </summary>
    public static int GetHighestUnlockedLevel()
    {
        return PlayerPrefs.GetInt(HighestLevelKey, 0);
    }

    /// <summary>
    /// Checks if Level 0 has been completed at least once.
    /// </summary>
    public static bool HasCompletedLevel0()
    {
        return PlayerPrefs.GetInt(HasCompletedLevel0Key, 0) == 1;
    }

    /// <summary>
    /// Saves a checkpoint to persist across game restarts.
    /// </summary>
    /// <param name="sceneName">Scene to load when resuming (e.g. "Level94")</param>
    /// <param name="levelNumber">Level number (e.g. 94)</param>
    public static void SaveCheckpoint(string sceneName, int levelNumber)
    {
        PlayerPrefs.SetString(CheckpointSceneKey, sceneName);
        PlayerPrefs.SetInt(CheckpointLevelNumKey, levelNumber);

        int currentHighest = PlayerPrefs.GetInt(HighestLevelKey, 0);
        if (levelNumber > currentHighest)
        {
            PlayerPrefs.SetInt(HighestLevelKey, levelNumber);
        }

        if (levelNumber >= 94)
        {
            PlayerPrefs.SetInt(HasCompletedLevel0Key, 1);
        }

        PlayerPrefs.Save();
        Debug.Log($"[GameProgressManager] Checkpoint saved: {sceneName} (Level {levelNumber})");
        OnProgressChanged?.Invoke();
    }

    /// <summary>
    /// Clears the active checkpoint (for starting a completely fresh run from Level 0).
    /// </summary>
    public static void ClearCheckpoint()
    {
        PlayerPrefs.DeleteKey(CheckpointSceneKey);
        PlayerPrefs.DeleteKey(CheckpointLevelNumKey);
        PlayerPrefs.Save();
        Debug.Log("[GameProgressManager] Checkpoint cleared.");
        OnProgressChanged?.Invoke();
    }

    /// <summary>
    /// Resumes the game from the stored checkpoint scene.
    /// </summary>
    public static void ResumeGame()
    {
        string sceneToLoad = GetCheckpointScene();
        if (string.IsNullOrEmpty(sceneToLoad) || !Application.CanStreamedLevelBeLoaded(sceneToLoad))
        {
            sceneToLoad = Level0SceneName;
        }

        SceneManager.LoadScene(sceneToLoad);
    }

    /// <summary>
    /// Starts a new run from Level 0, clearing any mid-run checkpoints.
    /// </summary>
    public static void StartNewGame()
    {
        ClearCheckpoint();
        SceneManager.LoadScene(Level0SceneName);
    }
}
