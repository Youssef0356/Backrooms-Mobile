#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Setup tool to configure Kane Pixels' "Bacteria" entity as the primary Backrooms villain:
/// 1. Rebuilds VillainController.controller and MenuBacteriaController.controller with
///    Walk, Zombie Run, and Attack 1 (jumpscare death lunge) states.
/// 2. Sets up Bacteria in MainLevel.unity with proper scale (1.0), NavMeshAgent, VillainAI, and VillainSightShake.
/// 3. Sets up Bacteria in Menu.unity with eerie looping walk animation in the hallway.
/// </summary>
public static class BacteriaSetupUtility
{
    const string WalkingFbxPath = "Assets/_Project/Art/Characters/Bacteria/Bacteria@Walking.fbx";
    const string RunFbxPath = "Assets/_Project/Art/Characters/Bacteria/Bacteria@Zombie Run.fbx";
    const string VillainControllerPath = "Assets/_Project/Art/Characters/Villain/VillainController.controller";
    const string MenuControllerPath = "Assets/_Project/Art/Characters/Villain/MenuBacteriaController.controller";

    const string MainLevelScenePath = "Assets/_Project/Scenes/MainLevel.unity";
    const string MenuScenePath = "Assets/_Project/Scenes/Menu.unity";

    [MenuItem("Tools/Backrooms/Entities/Bacteria/1. Build Bacteria Animator Controllers")]
    public static void BuildAnimatorsMenu()
    {
        BuildControllers();
        Debug.Log("[Bacteria] Successfully built VillainController and MenuBacteriaController.");
    }

    [MenuItem("Tools/Backrooms/Entities/Bacteria/2. Set Up Bacteria in Level 0 (MainLevel)")]
    public static void SetUpMainLevelMenu()
    {
        BuildControllers();
        SetUpMainLevel();
    }

    [MenuItem("Tools/Backrooms/Entities/Bacteria/3. Set Up Bacteria in Main Menu")]
    public static void SetUpMenuSceneMenu()
    {
        BuildControllers();
        SetUpMenuScene();
    }

    [MenuItem("Tools/Backrooms/Entities/Bacteria/4. Complete One-Click Setup (Both Scenes)")]
    public static void SetUpAllMenu()
    {
        BuildControllers();
        SetUpMainLevel();
        SetUpMenuScene();
        Debug.Log("[Bacteria] Complete setup finished! Bacteria is now active in Level 0 and Main Menu.");
    }

    // =========================================================================
    // Animator Controller Builder
    // =========================================================================

    public static void BuildControllers()
    {
        // 1. Load Humanoid Animation Clips
        AnimationClip walkClip = AssetDatabase.LoadAllAssetsAtPath(WalkingFbxPath).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        AnimationClip runClip = AssetDatabase.LoadAllAssetsAtPath(RunFbxPath).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        if (walkClip == null || runClip == null)
        {
            Debug.LogError("[Bacteria] Could not find animation clips in Bacteria FBX files.");
            return;
        }

        // 2. Build Main Game VillainController
        BuildGameController(VillainControllerPath, walkClip, runClip);

        // 3. Build MenuBacteriaController (starts in Walk with IsChasing false)
        BuildMenuController(MenuControllerPath, walkClip);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void BuildGameController(string path, AnimationClip walkClip, AnimationClip runClip)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
            AssetDatabase.DeleteAsset(path);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);

        controller.AddParameter("IsChasing", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack1", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Attack2", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        // Walk State (Default)
        AnimatorState walkState = sm.AddState("Walk", new Vector3(250f, 60f, 0f));
        walkState.motion = walkClip;
        walkState.speed = 1.0f;
        sm.defaultState = walkState;

        // Run State (Zombie Run)
        AnimatorState runState = sm.AddState("Zombie Run", new Vector3(250f, 160f, 0f));
        runState.motion = runClip;
        runState.speed = 1.0f;

        // Attack 1 State (Death Jumpscare Lunge)
        AnimatorState attackState = sm.AddState("Attack 1", new Vector3(540f, 110f, 0f));
        attackState.motion = runClip;
        attackState.speed = 1.4f;

        // Walk <-> Run Transitions
        AnimatorStateTransition toRun = walkState.AddTransition(runState);
        toRun.hasExitTime = false;
        toRun.duration = 0.15f;
        toRun.AddCondition(AnimatorConditionMode.If, 0f, "IsChasing");

        AnimatorStateTransition toWalk = runState.AddTransition(walkState);
        toWalk.hasExitTime = false;
        toWalk.duration = 0.20f;
        toWalk.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsChasing");

        // Attack Transitions (from AnyState via trigger)
        AnimatorStateTransition anyToAttack = sm.AddAnyStateTransition(attackState);
        anyToAttack.hasExitTime = false;
        anyToAttack.duration = 0.10f;
        anyToAttack.canTransitionToSelf = false;
        anyToAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack1");

        AnimatorStateTransition attackToRun = attackState.AddTransition(runState);
        attackToRun.hasExitTime = true;
        attackToRun.exitTime = 0.85f;
        attackToRun.duration = 0.15f;
        attackToRun.AddCondition(AnimatorConditionMode.If, 0f, "IsChasing");

        AnimatorStateTransition attackToWalk = attackState.AddTransition(walkState);
        attackToWalk.hasExitTime = true;
        attackToWalk.exitTime = 0.85f;
        attackToWalk.duration = 0.15f;
        attackToWalk.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsChasing");

        EditorUtility.SetDirty(controller);
    }

    private static void BuildMenuController(string path, AnimationClip walkClip)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
            AssetDatabase.DeleteAsset(path);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("IsChasing", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState walkState = sm.AddState("Walk", new Vector3(250f, 60f, 0f));
        walkState.motion = walkClip;
        walkState.speed = 1.0f;
        sm.defaultState = walkState;

        EditorUtility.SetDirty(controller);
    }

    // =========================================================================
    // Scene Setup: Level 0 (MainLevel.unity)
    // =========================================================================

    public static void SetUpMainLevel()
    {
        if (SceneManager.GetActiveScene().path != MainLevelScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(MainLevelScenePath);
        }

        GameObject fbxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WalkingFbxPath);
        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(RunFbxPath).OfType<Avatar>().FirstOrDefault()
                     ?? AssetDatabase.LoadAllAssetsAtPath(WalkingFbxPath).OfType<Avatar>().FirstOrDefault();
        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(VillainControllerPath);

        if (fbxPrefab == null)
        {
            Debug.LogError($"[Bacteria] Could not load model from {WalkingFbxPath}");
            return;
        }

        // 1. Remove or replace existing Clark in MainLevel
        GameObject oldClark = FindObjectByNameSubstring("Pirate clark norpa", "clark");
        Vector3 spawnPos = new Vector3(19.78f, 0f, 65.04f);
        Quaternion spawnRot = Quaternion.identity;

        if (oldClark != null)
        {
            spawnPos = oldClark.transform.position;
            spawnRot = oldClark.transform.rotation;
            Undo.DestroyObjectImmediate(oldClark);
        }

        // 2. Find or Instantiate Bacteria
        GameObject bacteriaObj = FindObjectByNameSubstring("Bacteria", "bacteria");
        if (bacteriaObj == null)
        {
            bacteriaObj = (GameObject)PrefabUtility.InstantiatePrefab(fbxPrefab);
            bacteriaObj.name = "Bacteria";
            Undo.RegisterCreatedObjectUndo(bacteriaObj, "Create Bacteria");
        }

        bacteriaObj.transform.position = spawnPos;
        bacteriaObj.transform.rotation = spawnRot;
        bacteriaObj.transform.localScale = new Vector3(2.0f, 2.0f, 2.0f); // True 2.0 scale for towering Kane Pixels Bacteria (~2.85m tall)

        // 3. Configure Animator
        Animator anim = bacteriaObj.GetComponent<Animator>();
        if (anim == null) anim = Undo.AddComponent<Animator>(bacteriaObj);
        anim.runtimeAnimatorController = controller;
        if (avatar != null) anim.avatar = avatar;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // 4. Configure NavMeshAgent
        NavMeshAgent agent = bacteriaObj.GetComponent<NavMeshAgent>();
        if (agent == null) agent = Undo.AddComponent<NavMeshAgent>(bacteriaObj);
        agent.radius = 0.45f;
        agent.height = 2.70f;
        agent.speed = 3.8f;
        agent.acceleration = 14f;
        agent.angularSpeed = 500f;
        agent.stoppingDistance = 1.2f;

        // 5. Configure AudioSource (3D spatial)
        AudioSource audioSource = bacteriaObj.GetComponent<AudioSource>();
        if (audioSource == null) audioSource = Undo.AddComponent<AudioSource>(bacteriaObj);
        audioSource.spatialBlend = 1.0f;
        audioSource.minDistance = 2.5f;
        audioSource.maxDistance = 32.0f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;

        // 6. Configure VillainAI
        VillainAI villainAI = bacteriaObj.GetComponent<VillainAI>();
        if (villainAI == null) villainAI = Undo.AddComponent<VillainAI>(bacteriaObj);
        villainAI.eyeHeight = 1.95f;
        villainAI.walkAnimReferenceSpeed = 1.2f;
        villainAI.runAnimReferenceSpeed = 3.5f;
        villainAI.catchDistance = 1.25f;
        villainAI.attackDistance = 2.2f;
        villainAI.maxBankAngle = 4.5f;
        villainAI.groundOffset = 0f;
        villainAI.canHear = true;
        villainAI.enableTeleport = true;

        // 7. Configure VillainSightShake
        VillainSightShake sightShake = bacteriaObj.GetComponent<VillainSightShake>();
        if (sightShake == null) sightShake = Undo.AddComponent<VillainSightShake>(bacteriaObj);
        sightShake.eyeHeight = 1.95f;
        sightShake.playerAimHeight = 1.1f;

        // 8. Connect to BackroomsRoomGenerator
        BackroomsRoomGenerator generator = Object.FindAnyObjectByType<BackroomsRoomGenerator>();
        if (generator != null)
        {
            SerializedObject serGen = new SerializedObject(generator);
            SerializedProperty villProp = serGen.FindProperty("villain");
            if (villProp != null)
            {
                villProp.objectReferenceValue = bacteriaObj;
                serGen.ApplyModifiedProperties();
            }
        }

        // Keep inactive initially: the room generator activates and positions it once the map generates
        bacteriaObj.SetActive(false);

        EditorUtility.SetDirty(bacteriaObj);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[Bacteria] Set up Bacteria in Level 0 (MainLevel.unity) successfully.");
    }

    // =========================================================================
    // Scene Setup: Main Menu (Menu.unity)
    // =========================================================================

    public static void SetUpMenuScene()
    {
        if (SceneManager.GetActiveScene().path != MenuScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(MenuScenePath);
        }

        GameObject fbxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WalkingFbxPath);
        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(RunFbxPath).OfType<Avatar>().FirstOrDefault()
                     ?? AssetDatabase.LoadAllAssetsAtPath(WalkingFbxPath).OfType<Avatar>().FirstOrDefault();
        RuntimeAnimatorController menuController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(MenuControllerPath);

        if (fbxPrefab == null)
        {
            Debug.LogError($"[Bacteria] Could not load model from {WalkingFbxPath}");
            return;
        }

        // 1. Remove Clark from Menu scene
        GameObject oldClark = FindObjectByNameSubstring("Pirate clark norpa", "clark");
        Vector3 hallwayPos = new Vector3(-6.363f, 0f, -0.709f);
        Quaternion hallwayRot = Quaternion.Euler(0f, -93.3f, 0f);

        if (oldClark != null)
        {
            hallwayPos = oldClark.transform.position;
            hallwayRot = oldClark.transform.rotation;
            Undo.DestroyObjectImmediate(oldClark);
        }

        // 2. Find or Instantiate Bacteria in Hallway
        GameObject bacteriaObj = FindObjectByNameSubstring("Bacteria", "bacteria");
        if (bacteriaObj == null)
        {
            bacteriaObj = (GameObject)PrefabUtility.InstantiatePrefab(fbxPrefab);
            bacteriaObj.name = "Bacteria";
            Undo.RegisterCreatedObjectUndo(bacteriaObj, "Create Menu Bacteria");
        }

        bacteriaObj.transform.position = hallwayPos;
        bacteriaObj.transform.rotation = hallwayRot;
        bacteriaObj.transform.localScale = new Vector3(2.0f, 2.0f, 2.0f); // True 2.0 scale for Menu scene

        // 3. Configure Animator for idle looping walk
        Animator anim = bacteriaObj.GetComponent<Animator>();
        if (anim == null) anim = Undo.AddComponent<Animator>(bacteriaObj);
        anim.runtimeAnimatorController = menuController;
        if (avatar != null) anim.avatar = avatar;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        bacteriaObj.SetActive(true);

        EditorUtility.SetDirty(bacteriaObj);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[Bacteria] Set up Bacteria in Main Menu (Menu.unity) successfully.");
    }

    private static GameObject FindObjectByNameSubstring(params string[] fragments)
    {
        var all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
        foreach (var go in all)
        {
            if (go == null) continue;
            string lower = go.name.ToLowerInvariant();
            foreach (var frag in fragments)
            {
                if (lower.Contains(frag.ToLowerInvariant())) return go;
            }
        }
        return null;
    }
}
#endif
