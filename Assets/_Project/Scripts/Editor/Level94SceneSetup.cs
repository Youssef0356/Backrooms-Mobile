#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sets up the complete Level 94 ("Motion") scene, binds all procedural generators,
/// assets, mobile touch inputs, connects the player camera, and registers the scene in Build Settings.
/// </summary>
public static class Level94SceneSetup
{
    private const string ScenePath = "Assets/_Project/Scenes/Level94.unity";
    private const string PlayerCapsulePrefabPath = "Assets/Starter Assets/Runtime/FirstPersonController/Prefabs/PlayerCapsule.prefab";
    private const string FollowCamPrefabPath = "Assets/Starter Assets/Runtime/FirstPersonController/Prefabs/PlayerFollowCamera.prefab";
    private const string MainCamPrefabPath = "Assets/Starter Assets/Runtime/FirstPersonController/Prefabs/MainCamera.prefab";
    private const string TouchUIPrefabPath = "Assets/Starter Assets/Runtime/Mobile/Prefabs/UI_TouchScreenInput.prefab";
    private const string EventSystemPrefabPath = "Assets/Starter Assets/Runtime/Mobile/Prefabs/EventSystem/UI_EventSystem.prefab";

    [MenuItem("Tools/Backrooms/Scene Setup/Level 94/Setup & Open Level 94 Scene", priority = 21)]
    public static void CreateOrOpenLevel94Scene()
    {
        // 1. Ensure all 3D house and prop prefabs exist first
        Level94AssetGenerator.GenerateAllAssets();

        // 2. Open or create the Level94 scene
        Scene scene;
        bool createdNew = false;
        if (System.IO.File.Exists(ScenePath))
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        else
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            createdNew = true;
        }

        // 3. Populate Scene if needed
        SetupSceneObjects();

        // 4. Save Scene
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        // 5. Add to Build Settings
        EnsureSceneInBuildSettings();

        Debug.Log("[Level94SceneSetup] Level 94 scene setup successfully and registered in Build Settings!");
    }

    private static void SetupSceneObjects()
    {
        // 0. Clean manual duplicates if present
        Scene activeScene = EditorSceneManager.GetActiveScene();
        if (activeScene.isLoaded)
        {
            var roots = activeScene.GetRootGameObjects();
            foreach (var r in roots)
            {
                string n = r.name;
                if ((n.StartsWith("LEVEL 94") || n.StartsWith("WaterContainer") || n.StartsWith("House_Prologue_Set") || n.StartsWith("House_Set")) &&
                    !n.Contains("Director") && !n.Contains("Atmosphere") && !n.Contains("Environment"))
                {
                    Object.DestroyImmediate(r);
                }
            }
        }

        // Unpack NestedParent_Unpack if present
        GameObject nestedParent = GameObject.Find("NestedParent_Unpack");
        if (nestedParent != null && PrefabUtility.IsPartOfAnyPrefab(nestedParent))
        {
            PrefabUtility.UnpackPrefabInstance(nestedParent, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        // 1. Directional Sun Light
        Light sun = Object.FindAnyObjectByType<Light>();
        if (sun == null)
        {
            GameObject sunGo = new GameObject("Directional Light");
            sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.95f, 0.85f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(38f, -40f, 0f);
        }

        // 2. PlayerCapsule
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null)
        {
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCapsulePrefabPath);
            if (playerPrefab != null)
            {
                playerObj = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                playerObj.name = "PlayerCapsule";
                playerObj.tag = "Player";
                playerObj.transform.position = new Vector3(0f, 4.0f, 0f);
            }
        }

        // Locate or create PlayerCameraRoot
        Transform cameraRoot = null;
        if (playerObj != null)
        {
            cameraRoot = playerObj.transform.Find("PlayerCameraRoot");
            if (cameraRoot == null)
            {
                GameObject crGo = new GameObject("PlayerCameraRoot");
                cameraRoot = crGo.transform;
                cameraRoot.SetParent(playerObj.transform, false);
                cameraRoot.localPosition = new Vector3(0f, 1.375f, 0f);
            }

            var fpc = playerObj.GetComponent<StarterAssets.FirstPersonController>();
            if (fpc != null)
            {
                fpc.CinemachineCameraTarget = cameraRoot.gameObject;
            }
        }

        // 3. PlayerFollowCamera (Cinemachine Camera)
        var cmCam = Object.FindAnyObjectByType<Unity.Cinemachine.CinemachineCamera>();
        if (cmCam == null)
        {
            GameObject followPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FollowCamPrefabPath);
            if (followPrefab != null)
            {
                GameObject followGo = (GameObject)PrefabUtility.InstantiatePrefab(followPrefab);
                followGo.name = "PlayerFollowCamera";
                cmCam = followGo.GetComponent<Unity.Cinemachine.CinemachineCamera>();
            }
        }

        if (cmCam != null && cameraRoot != null)
        {
            cmCam.Follow = cameraRoot;
            cmCam.LookAt = cameraRoot;
            cmCam.transform.position = cameraRoot.position;
            EditorUtility.SetDirty(cmCam);
        }

        if (playerObj != null)
        {
            EditorUtility.SetDirty(playerObj);
        }

        // 4. MainCamera
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            var camGo = GameObject.Find("MainCamera") ?? GameObject.FindWithTag("MainCamera");
            if (camGo != null) mainCam = camGo.GetComponent<Camera>();
        }

        if (mainCam == null)
        {
            GameObject mainCamPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MainCamPrefabPath);
            if (mainCamPrefab != null)
            {
                GameObject camObj = (GameObject)PrefabUtility.InstantiatePrefab(mainCamPrefab);
                camObj.name = "MainCamera";
                camObj.tag = "MainCamera";
                mainCam = camObj.GetComponent<Camera>();
            }
        }

        if (mainCam != null)
        {
            var brain = mainCam.GetComponent<Unity.Cinemachine.CinemachineBrain>();
            if (brain == null)
            {
                mainCam.gameObject.AddComponent<Unity.Cinemachine.CinemachineBrain>();
            }
            if (cameraRoot != null)
            {
                mainCam.transform.position = cameraRoot.position;
            }
        }

        // 5. Touch Screen Input & EventSystem
        GameObject touchGo = GameObject.Find("UI_TouchScreenInput");
        if (touchGo == null)
        {
            GameObject touchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TouchUIPrefabPath);
            if (touchPrefab != null)
            {
                touchGo = (GameObject)PrefabUtility.InstantiatePrefab(touchPrefab);
                touchGo.name = "UI_TouchScreenInput";
            }
        }

        if (touchGo != null && playerObj != null)
        {
            var vInput = touchGo.GetComponent<StarterAssets.VirtualInput>() ?? touchGo.GetComponentInChildren<StarterAssets.VirtualInput>();
            if (vInput != null)
            {
                vInput.StarterAssetsInputs = playerObj.GetComponent<StarterAssets.StarterAssetsInputs>();
            }
        }

        if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject esPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EventSystemPrefabPath);
            if (esPrefab != null)
            {
                GameObject esGo = (GameObject)PrefabUtility.InstantiatePrefab(esPrefab);
                esGo.name = "UI_EventSystem";
            }
        }

        // 6. Level94 Director (Procedural Generator + Atmosphere)
        GameObject directorGo = GameObject.Find("Level94_Director");
        if (directorGo == null)
        {
            directorGo = new GameObject("Level94_Director");
        }

        Level94Generator generator = directorGo.GetComponent<Level94Generator>();
        if (generator == null) generator = directorGo.AddComponent<Level94Generator>();

        Level94Atmosphere atmosphere = directorGo.GetComponent<Level94Atmosphere>();
        if (atmosphere == null) directorGo.AddComponent<Level94Atmosphere>();

        // Wire Prefabs to Generator
        generator.grassMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Level94/Materials/Grass_Level94.mat");

        var houseList = new List<GameObject>();
        // Add custom Blender house model (with foundation skirt)
        GameObject customHouse = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/LEVEL 94 Houses.glb");
        if (customHouse != null) houseList.Add(customHouse);

        string[] houseNames = { "House_Classic_Gable", "House_Tall_Dormer", "House_Ranch_Bungalow" };
        foreach (var h in houseNames)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Art/Level94/Prefabs/{h}.prefab");
            if (p != null) houseList.Add(p);
        }
        generator.housePrefabs = houseList.ToArray();

        generator.waterTowerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/WaterContainer.glb");
        generator.fencePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/Fences.glb");
        if (generator.fencePrefab == null)
            generator.fencePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/Prefabs/Suburban_PicketFence.prefab");

        generator.exitHousePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/Prefabs/Level94_ExitHouse.prefab");
        if (generator.exitHousePrefab == null && customHouse != null) generator.exitHousePrefab = customHouse;

        generator.streetlightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/Prefabs/Suburban_Streetlight.prefab");
        generator.mailboxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/Prefabs/Suburban_Mailbox.prefab");
        generator.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCapsulePrefabPath);

        // House Interior Decorations & Lockers
        generator.bedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Environment/Backrooms/Decoration/Bed.prefab");

        var tables = new List<GameObject>();
        string[] tNames = { "Table", "ModernTable" };
        foreach (var t in tNames)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Art/Environment/Backrooms/Decoration/{t}.prefab");
            if (p != null) tables.Add(p);
        }
        generator.tablePrefabs = tables.ToArray();

        var chairs = new List<GameObject>();
        string[] cNames = { "Canapé", "FatChair", "ChairWithWheels" };
        foreach (var c in cNames)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Art/Environment/Backrooms/Decoration/{c}.prefab");
            if (p != null) chairs.Add(p);
        }
        generator.chairPrefabs = chairs.ToArray();

        var tvs = new List<GameObject>();
        string[] tvNames = { "CRT_TV", "TV 1" };
        foreach (var tv in tvNames)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Art/Environment/Backrooms/Decoration/{tv}.prefab");
            if (p != null) tvs.Add(p);
        }
        generator.tvPrefabs = tvs.ToArray();

        if (playerObj != null)
        {
            generator.player = playerObj.transform;
        }

        atmosphere.ApplySettings();

        // 7. UI & Victory Controllers
        if (Object.FindAnyObjectByType<VictoryController>() == null)
        {
            new GameObject("VictoryController").AddComponent<VictoryController>();
        }

        if (Object.FindAnyObjectByType<PauseMenuController>() == null)
        {
            new GameObject("PauseMenuController").AddComponent<PauseMenuController>();
        }

        EditorUtility.SetDirty(directorGo);
    }

    public static void EnsureSceneInBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        bool found = false;

        foreach (var s in scenes)
        {
            if (s.path == ScenePath)
            {
                found = true;
                s.enabled = true;
                break;
            }
        }

        if (!found)
        {
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[Level94SceneSetup] Added {ScenePath} to EditorBuildSettings.");
        }
    }
}
#endif
