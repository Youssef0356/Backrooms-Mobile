#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameSetupHelper
{
    const string MainLevelScenePath = "Assets/_Project/Scenes/MainLevel.unity";

    [MenuItem("Tools/Backrooms/Setup GameOver Controller in MainLevel")]
    public static void SetupGameOverController()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        if (currentScene.path != MainLevelScenePath)
        {
            currentScene = EditorSceneManager.OpenScene(MainLevelScenePath, OpenSceneMode.Single);
        }

        GameOverController controller = Object.FindAnyObjectByType<GameOverController>();
        if (controller == null)
        {
            GameObject go = new GameObject("GameOverMenu");
            controller = go.AddComponent<GameOverController>();
            EditorUtility.SetDirty(go);
            EditorSceneManager.MarkSceneDirty(currentScene);
            EditorSceneManager.SaveScene(currentScene);
            Debug.Log("[Backrooms] Created GameOverMenu GameObject with GameOverController in MainLevel.");
        }
        else
        {
            Debug.Log("[Backrooms] GameOverController already present in scene.");
        }
    }
}
#endif
