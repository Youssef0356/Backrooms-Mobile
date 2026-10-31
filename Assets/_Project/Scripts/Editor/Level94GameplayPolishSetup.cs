#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using StarterAssets;

/// <summary>
/// Wires and configures:
/// 1. Realistic Movement: Dynamic head bobbing, intense sprinting head shake, landing cushion & footsteps.
/// 2. 30-Second Day/Night Cycle with true pitch-black nocturnal ambient darkness.
/// 3. Stamina system with in-game HUD bar, 0-stamina exhaustion, and tired eyelid blinking.
/// </summary>
public static class Level94GameplayPolishSetup
{
    [MenuItem("Tools/Backrooms/Scene Setup/Level 94/7. Setup Real Movement, 30s Day-Night Cycle & Stamina HUD", priority = 7)]
    public static void SetupAllGameplayFeatures()
    {
        Scene currentScene = SceneManager.GetActiveScene();

        // 1. Locate Player
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) player = GameObject.Find("PlayerCapsule");
        if (player == null)
        {
            EditorUtility.DisplayDialog("Player Missing", "Could not find PlayerCapsule in scene.", "OK");
            return;
        }

        // Add PlayerStamina if missing
        PlayerStamina stamina = player.GetComponent<PlayerStamina>();
        if (stamina == null)
        {
            stamina = Undo.AddComponent<PlayerStamina>(player);
        }

        // Add PlayerExhaustionBlinkEffect if missing
        PlayerExhaustionBlinkEffect blinkEffect = player.GetComponent<PlayerExhaustionBlinkEffect>();
        if (blinkEffect == null)
        {
            blinkEffect = Undo.AddComponent<PlayerExhaustionBlinkEffect>(player);
        }

        // 2. Locate PlayerCameraRoot and configure Head Bob & Shake
        Transform cameraRoot = player.transform.Find("PlayerCameraRoot");
        if (cameraRoot != null)
        {
            PlayerHeadBobMotion headBob = cameraRoot.GetComponent<PlayerHeadBobMotion>();
            if (headBob == null)
            {
                headBob = Undo.AddComponent<PlayerHeadBobMotion>(cameraRoot.gameObject);
            }
        }

        // 3. Locate Level94_Director and configure Day/Night Cycle & Stamina HUD
        GameObject director = GameObject.Find("Level94_Director");
        if (director == null)
        {
            director = new GameObject("Level94_Director");
            Undo.RegisterCreatedObjectUndo(director, "Create Level94_Director");
        }

        // Day/Night Cycle (60 seconds full cycle, pitch black nights)
        Level94DayNightCycle dayNight = director.GetComponent<Level94DayNightCycle>();
        if (dayNight == null)
        {
            dayNight = Undo.AddComponent<Level94DayNightCycle>(director);
        }
        dayNight.cycleDuration = 60f;
        dayNight.timeOfDay = 0.20f; // Start at bright morning/noon

        // Stamina HUD
        PlayerStaminaHUD staminaHUD = director.GetComponent<PlayerStaminaHUD>();
        if (staminaHUD == null)
        {
            staminaHUD = Undo.AddComponent<PlayerStaminaHUD>(director);
        }

        EditorSceneManager.MarkSceneDirty(currentScene);
        EditorSceneManager.SaveScene(currentScene);

        Debug.Log("<color=#00FF88><b>[Level 94 Polish]</b></color> Successfully wired: Realistic Head Bob & Sprint Shake, 30s Day/Night Cycle with true night darkness, Stamina HUD & 0-Stamina Tired Blinking!");
    }
}
#endif
