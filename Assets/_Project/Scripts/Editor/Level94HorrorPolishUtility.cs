#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using StarterAssets;

/// <summary>
/// One-click editor utility to configure all requested features in Level 94:
/// 1. Rare Blink Night Event + 1 min Day / 2 min Night cycle with dense fog.
/// 2. Player torchlight (Flashlight like Level 0) auto-equipped and activated at night.
/// 3. Interactive house doors with open/close logic for player and monsters.
/// 4. Objective: Find the right house that contains the non-glowing Level 0 exit door.
/// 5. Undetectable 30-second house stay teleportation to other houses with smart rotation.
/// 6. Agatha scary stalker entity with animations, night spawning, wide vision, and house breaching.
/// </summary>
public static class Level94HorrorPolishUtility
{
    private const string ScenePath = "Assets/_Project/Scenes/Level94.unity";
    private const string AgathaPrefabPath = "Assets/_Project/Art/Level94/Prefabs/Agatha_Stalker.prefab";

    [MenuItem("Tools/Backrooms/Scene Setup/Level 94/Setup Complete Night Stalker, Doors & Exit Gameplay", priority = 8)]
    public static void SetupCompleteGameplay()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        if (currentScene.path != ScenePath)
        {
            currentScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        // 1. Build Agatha Controller & Prefab
        AgathaSetup.SetupAgatha();

        // 2. Setup Player Systems
        SetupPlayer();

        // 3. Setup Director & Core Gameplay Systems
        SetupDirector();

        // 4. Setup Agatha Entity in Scene
        SetupAgathaInScene();

        // 5. Mark Scene Dirty and Save
        EditorSceneManager.MarkSceneDirty(currentScene);
        EditorSceneManager.SaveScene(currentScene);

        Debug.Log("<color=#00FF88><b>[Level 94 Polish Complete]</b></color> All systems successfully wired:\n" +
                  "- 1m Day / 2m Night cycle with rare blink night trigger & thick fog\n" +
                  "- Character torchlight like Level 0\n" +
                  "- Interactive house doors with open/close logic\n" +
                  "- Non-glowing Level 0 exit door inside the target house\n" +
                  "- Undetectable 30s house stay spatial displacement\n" +
                  "- Agatha stalker entity with animations, night spawning, wide vision, and door opening!");
    }

    private static void SetupPlayer()
    {
        GameObject player = GameObject.FindWithTag("Player") ?? GameObject.Find("PlayerCapsule");
        if (player == null)
        {
            Debug.LogError("[Level 94 Setup] PlayerCapsule not found in scene!");
            return;
        }

        // Stamina
        if (player.GetComponent<PlayerStamina>() == null)
        {
            Undo.AddComponent<PlayerStamina>(player);
        }

        // Exhaustion & Natural Blinking
        if (player.GetComponent<PlayerExhaustionBlinkEffect>() == null)
        {
            Undo.AddComponent<PlayerExhaustionBlinkEffect>(player);
        }

        // Door Interaction ([E] / Mobile tap)
        if (player.GetComponent<PlayerDoorInteraction>() == null)
        {
            Undo.AddComponent<PlayerDoorInteraction>(player);
        }

        // CharacterController sizing for standard house doorways (0.44m collision diameter, 1.55m height with clearance)
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null)
        {
            Undo.RecordObject(cc, "Adjust Player CharacterController for Doorways");
            cc.radius = 0.22f;
            cc.height = 1.55f;
            cc.center = new Vector3(0f, 0.78f, 0f);
            cc.stepOffset = 0.45f;
            cc.slopeLimit = 60.0f;
            cc.skinWidth = 0.025f;
        }

        // Flashlight (Torchlight like Level 0)
        Transform camRoot = player.transform.Find("PlayerCameraRoot") ?? player.transform;
        Flashlight fl = camRoot.GetComponentInChildren<Flashlight>();
        if (fl == null)
        {
            GameObject flGo = new GameObject("Flashlight");
            Undo.RegisterCreatedObjectUndo(flGo, "Create Flashlight");
            flGo.transform.SetParent(camRoot, false);
            flGo.transform.localPosition = new Vector3(0.18f, -0.16f, 0.20f);
            flGo.transform.localRotation = Quaternion.identity;

            fl = Undo.AddComponent<Flashlight>(flGo);
            fl.intensity = 3.6f;
            fl.range = 26f;
            fl.startOn = false;
        }
    }

    private static void SetupDirector()
    {
        GameObject director = GameObject.Find("Level94_Director");
        if (director == null)
        {
            director = new GameObject("Level94_Director");
            Undo.RegisterCreatedObjectUndo(director, "Create Level94_Director");
        }

        // Day/Night Cycle (1 min day, 2 min night, rare blink night trigger)
        Level94DayNightCycle dayNight = director.GetComponent<Level94DayNightCycle>();
        if (dayNight == null)
        {
            dayNight = Undo.AddComponent<Level94DayNightCycle>(director);
        }
        dayNight.dayDuration = 60.0f;
        dayNight.nightDuration = 120.0f;
        dayNight.rareBlinkNightChance = 0.08f;
        dayNight.enableBlinkNightTrigger = true;
        dayNight.nightFogStart = 1.0f; // Truly claustrophobic near fog
        dayNight.nightFogEnd = 16.0f;  // Cuts visibility at 16m into absolute blackness
        dayNight.nightMoonIntensity = 0.005f;
        dayNight.autoActivateFlashlightAtNight = true;

        // House Teleporter (30s undetectable displacement)
        Level94HouseTeleporter teleporter = director.GetComponent<Level94HouseTeleporter>();
        if (teleporter == null)
        {
            teleporter = Undo.AddComponent<Level94HouseTeleporter>(director);
        }
        teleporter.requiredStayDuration = 30.0f;
        teleporter.houseInteriorCenterOffset = new Vector3(0f, 1.2f, -2.5f);
        teleporter.houseInteriorHalfExtents = new Vector3(2.6f, 1.8f, 2.5f);

        // Level94 Gameplay Director
        Level94GameplayDirector gameplayDirector = director.GetComponent<Level94GameplayDirector>();
        if (gameplayDirector == null)
        {
            gameplayDirector = Undo.AddComponent<Level94GameplayDirector>(director);
        }

        // Execute initialization to configure doors and exit portal
        gameplayDirector.InitializeLevel94Gameplay();
    }

    private static void SetupAgathaInScene()
    {
        // Check if Agatha already in scene
        GameObject agathaObj = GameObject.Find("Agatha_Stalker");
        if (agathaObj == null)
        {
            AgathaAI existingAi = Object.FindAnyObjectByType<AgathaAI>();
            if (existingAi != null) agathaObj = existingAi.gameObject;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AgathaPrefabPath);

        if (agathaObj == null && prefab != null)
        {
            agathaObj = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(agathaObj, "Instantiate Agatha Stalker");
            agathaObj.name = "Agatha_Stalker";
        }

        if (agathaObj != null)
        {
            // Position near houses and raycast onto terrain surface so it NEVER overlaps with ground
            Vector3 targetGround = new Vector3(20f, 0f, 20f);
            GameObject housesContainer = GameObject.Find("LEVEL 94 Houses (Randomized)");
            if (housesContainer != null && housesContainer.transform.childCount > 2)
            {
                targetGround = housesContainer.transform.GetChild(2).position + new Vector3(8f, 0f, 8f);
            }

            // Ground raycast snapping
            if (Physics.Raycast(targetGround + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 120f))
            {
                agathaObj.transform.position = hit.point + Vector3.up * 0.05f;
            }
            else
            {
                agathaObj.transform.position = targetGround;
            }

            // Wire to director and day-night cycle
            Level94GameplayDirector director = Object.FindAnyObjectByType<Level94GameplayDirector>();
            if (director != null)
            {
                director.agathaInstance = agathaObj;
                director.agathaPrefab = prefab;
            }

            Level94DayNightCycle dayNight = Object.FindAnyObjectByType<Level94DayNightCycle>();
            if (dayNight != null)
            {
                dayNight.agathaInstance = agathaObj;
                dayNight.agathaAI = agathaObj.GetComponent<AgathaAI>();
            }
        }
    }
}
#endif
