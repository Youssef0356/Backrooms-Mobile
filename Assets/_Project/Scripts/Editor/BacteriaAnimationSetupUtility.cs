#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class BacteriaAnimationSetupUtility
{
    private const string BacteriaFolder = "Assets/_Project/Art/Characters/Bacteria";
    private const string VillainFolder = "Assets/_Project/Art/Characters/Villain";
    private const string BaseAvatarFbx = "Assets/_Project/Art/Characters/Bacteria/Bacteria@Zombie Run.fbx";
    private const string MainLevelScenePath = "Assets/_Project/Scenes/MainLevel.unity";

    [InitializeOnLoadMethod]
    private static void AutoRunOnceOnCompile()
    {
        if (Application.isPlaying) return;
        if (SessionState.GetBool("BacteriaAnimationSetup_Done_v7", false)) return;
        SessionState.SetBool("BacteriaAnimationSetup_Done_v7", true);
        try
        {
            SetupAllBacteriaAnimations();
            ConfigureMainLevelScene();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[BacteriaAnimationSetup] AutoRun error: " + ex);
        }
    }

    [MenuItem("Tools/Backrooms/Setup Bacteria Animations & MainLevel", priority = 10)]
    public static void SetupAllBacteriaAnimations()
    {
        // 1. Get Base Humanoid Avatar from Bacteria@Zombie Run.fbx
        Avatar baseAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(BaseAvatarFbx);
        if (baseAvatar == null)
        {
            Object[] sub = AssetDatabase.LoadAllAssetsAtPath(BaseAvatarFbx);
            foreach (var o in sub)
            {
                if (o is Avatar a)
                {
                    baseAvatar = a;
                    break;
                }
            }
        }

        if (baseAvatar == null)
        {
            Debug.LogError("[BacteriaAnimationSetup] Could not find Base Avatar in " + BaseAvatarFbx);
            return;
        }

        // 2. Configure the 4 FBX files as Humanoid with CreateFromThisModel so Mixamo bone prefixes retarget cleanly
        ConfigureFbx(Path.Combine(BacteriaFolder, "Idle (2).fbx"), baseAvatar, "Idle (2)", true);
        ConfigureFbx(Path.Combine(BacteriaFolder, "Injured Run.fbx"), baseAvatar, "Injured Run", true);
        ConfigureFbx(Path.Combine(BacteriaFolder, "Running.fbx"), baseAvatar, "Running", true);
        ConfigureFbx(Path.Combine(BacteriaFolder, "Zombie Neck Bite.fbx"), baseAvatar, "Zombie Neck Bite", false);

        AssetDatabase.SaveAssets();

        // 3. Load animation clips
        AnimationClip idleClip = LoadClip(Path.Combine(BacteriaFolder, "Idle (2).fbx"));
        AnimationClip injuredRunClip = LoadClip(Path.Combine(BacteriaFolder, "Injured Run.fbx"));
        AnimationClip runningClip = LoadClip(Path.Combine(BacteriaFolder, "Running.fbx"));
        AnimationClip killClip = LoadClip(Path.Combine(BacteriaFolder, "Zombie Neck Bite.fbx"));

        // 4. Update MenuBacteriaController.controller
        string menuCtrlPath = Path.Combine(VillainFolder, "MenuBacteriaController.controller");
        AnimatorController menuCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(menuCtrlPath);
        if (menuCtrl != null && idleClip != null)
        {
            var sm = menuCtrl.layers[0].stateMachine;
            foreach (var s in sm.states)
            {
                if (s.state.name == "Walk" || s.state.name == "Idle")
                {
                    s.state.name = "Idle";
                    s.state.motion = idleClip;
                    break;
                }
            }
            EditorUtility.SetDirty(menuCtrl);
        }

        // 5. Update VillainController.controller
        string villainCtrlPath = Path.Combine(VillainFolder, "VillainController.controller");
        AnimatorController villainCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(villainCtrlPath);
        if (villainCtrl != null)
        {
            var sm = villainCtrl.layers[0].stateMachine;

            EnsureParameter(villainCtrl, "IsChasing", AnimatorControllerParameterType.Bool);
            EnsureParameter(villainCtrl, "Speed", AnimatorControllerParameterType.Float);
            EnsureParameter(villainCtrl, "Attack1", AnimatorControllerParameterType.Trigger);
            EnsureParameter(villainCtrl, "Kill", AnimatorControllerParameterType.Trigger);

            AnimatorState idleState = null;
            AnimatorState patrolState = null;
            AnimatorState chaseState = null;
            AnimatorState killState = null;

            foreach (var s in sm.states)
            {
                if (s.state.name == "Idle")
                {
                    idleState = s.state;
                }
                else if (s.state.name == "Walk" || s.state.name == "Patrol")
                {
                    patrolState = s.state;
                }
                else if (s.state.name == "Zombie Run" || s.state.name == "Running" || s.state.name == "Run" || s.state.name == "Chase")
                {
                    chaseState = s.state;
                }
                else if (s.state.name == "Attack 1" || s.state.name == "Zombie Neck Bite" || s.state.name == "Kill" || s.state.name == "NeckBite")
                {
                    killState = s.state;
                }
                else if (s.state.name == "Attack 2" && killClip != null)
                {
                    s.state.motion = killClip;
                }
            }

            if (idleState == null && idleClip != null)
            {
                idleState = sm.AddState("Idle", new Vector3(260f, 30f, 0f));
                idleState.motion = idleClip;
            }
            else if (idleState != null && idleClip != null)
            {
                idleState.motion = idleClip;
            }

            if (patrolState != null)
            {
                patrolState.name = "Walk";
                if (injuredRunClip != null) patrolState.motion = injuredRunClip;
            }

            if (chaseState != null)
            {
                chaseState.name = "Zombie Run";
                if (runningClip != null) chaseState.motion = runningClip;
            }

            if (killState != null)
            {
                killState.name = "Attack 1";
                if (killClip != null) killState.motion = killClip;
            }

            if (idleState != null && patrolState != null)
            {
                bool hasIdleToWalk = false;
                foreach (var tr in idleState.transitions)
                {
                    if (tr.destinationState == patrolState) { hasIdleToWalk = true; break; }
                }
                if (!hasIdleToWalk)
                {
                    var t1 = idleState.AddTransition(patrolState);
                    t1.AddCondition(AnimatorConditionMode.Greater, 0.15f, "Speed");
                    t1.duration = 0.2f;
                    t1.hasExitTime = false;
                }

                bool hasWalkToIdle = false;
                foreach (var tr in patrolState.transitions)
                {
                    if (tr.destinationState == idleState) { hasWalkToIdle = true; break; }
                }
                if (!hasWalkToIdle)
                {
                    var t2 = patrolState.AddTransition(idleState);
                    t2.AddCondition(AnimatorConditionMode.Less, 0.12f, "Speed");
                    t2.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsChasing");
                    t2.duration = 0.25f;
                    t2.hasExitTime = false;
                }
            }

            if (idleState != null && chaseState != null)
            {
                bool hasIdleToChase = false;
                foreach (var tr in idleState.transitions)
                {
                    if (tr.destinationState == chaseState) { hasIdleToChase = true; break; }
                }
                if (!hasIdleToChase)
                {
                    var t3 = idleState.AddTransition(chaseState);
                    t3.AddCondition(AnimatorConditionMode.If, 0f, "IsChasing");
                    t3.duration = 0.12f;
                    t3.hasExitTime = false;
                }
            }

            bool hasKillTransition = false;
            foreach (var tr in sm.anyStateTransitions)
            {
                if (tr.destinationState == killState)
                {
                    hasKillTransition = true;
                    break;
                }
            }

            if (!hasKillTransition && killState != null)
            {
                var tr = sm.AddAnyStateTransition(killState);
                tr.AddCondition(AnimatorConditionMode.If, 0, "Attack1");
                tr.duration = 0.1f;
                tr.hasExitTime = false;
            }

            EditorUtility.SetDirty(villainCtrl);
            Debug.Log("<color=#00FF88><b>[BacteriaAnimationSetup]</b></color> VillainController configured with Idle (2), Injured Run (Walk), Running (Zombie Run), and Zombie Neck Bite (Attack 1)!");
        }

        AssetDatabase.SaveAssets();
    }

    public static void ConfigureMainLevelScene()
    {
        if (!File.Exists(MainLevelScenePath)) return;

        Scene prevScene = SceneManager.GetActiveScene();
        string prevPath = prevScene.path;

        Scene mainLevel = EditorSceneManager.OpenScene(MainLevelScenePath, OpenSceneMode.Single);
        BackroomsRoomGenerator gen = Object.FindAnyObjectByType<BackroomsRoomGenerator>();

        // Find all root objects in MainLevel
        GameObject keptBacteria = null;
        if (gen != null && gen.villain != null && gen.villain.name.ToLowerInvariant().Contains("bacteria"))
        {
            keptBacteria = gen.villain;
        }

        GameObject[] roots = mainLevel.GetRootGameObjects();
        foreach (var go in roots)
        {
            if (go == null) continue;
            string n = go.name;
            if (keptBacteria == null && n == "Bacteria")
            {
                keptBacteria = go;
            }
        }

        // Remove duplicate/stray Bacteria@Walking or leftover Clark objects in MainLevel.unity (Phase 0.2)
        int removedCount = 0;
        foreach (var go in roots)
        {
            if (go == null || go == keptBacteria) continue;
            string n = go.name.ToLowerInvariant();
            if (n.Contains("bacteria@") || n == "clark" || n.Contains("pirateclark"))
            {
                Object.DestroyImmediate(go);
                removedCount++;
            }
        }

        if (keptBacteria != null)
        {
            keptBacteria.transform.localScale = new Vector3(2.0f, 2.0f, 2.0f);

            NavMeshAgent agent = keptBacteria.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.radius = 0.38f;
                agent.height = 1.80f;
                EditorUtility.SetDirty(agent);
            }

            VillainAI ai = keptBacteria.GetComponent<VillainAI>();
            if (ai != null)
            {
                ai.firstTeleportDelay = 75f;
                ai.teleportCooldown = 90f;
                ai.maxDistanceFromPlayer = 45f;
                ai.teleportRange = new Vector2(18f, 32f);
                ai.walkAnimReferenceSpeed = 1.75f;
                ai.runAnimReferenceSpeed = 3.60f;
                ai.eyeHeight = 2.2f;
                EditorUtility.SetDirty(ai);
            }

            if (keptBacteria.GetComponent<BacteriaTwitch>() == null)
            {
                keptBacteria.AddComponent<BacteriaTwitch>();
            }

            if (gen != null)
            {
                gen.villain = keptBacteria;
                gen.enableSmilyHound = true;
                EditorUtility.SetDirty(gen);
            }
        }

        EditorSceneManager.MarkSceneDirty(mainLevel);
        EditorSceneManager.SaveScene(mainLevel);
        Debug.Log($"<color=#00FF88><b>[Level 0 MainLevel Configured]</b></color> Cleaned {removedCount} stray monster objects, configured 2x Bacteria with BacteriaTwitch, 75s/90s teleport pacing, and 1.80m NavMeshAgent!");

        if (!string.IsNullOrEmpty(prevPath) && prevPath != MainLevelScenePath && File.Exists(prevPath))
        {
            EditorSceneManager.OpenScene(prevPath, OpenSceneMode.Single);
        }
    }

    private static void ConfigureFbx(string assetPath, Avatar baseAvatar, string clipName, bool loop)
    {
        ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogWarning("[BacteriaAnimationSetup] Missing FBX at " + assetPath);
            return;
        }

        bool dirty = false;
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            dirty = true;
        }

        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.autoGenerateAvatarMappingIfUnspecified = true;
            dirty = true;
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.defaultClipAnimations;
        }

        if (clips != null && clips.Length > 0)
        {
            clips[0].name = clipName;
            clips[0].loopTime = loop;
            if (loop)
            {
                clips[0].loopPose = true;
                clips[0].lockRootRotation = true;
                clips[0].lockRootHeightY = true;
                clips[0].lockRootPositionXZ = true;
            }
            importer.clipAnimations = clips;
            dirty = true;
        }

        if (dirty)
        {
            importer.SaveAndReimport();
            Debug.Log($"[BacteriaAnimationSetup] Reimported {Path.GetFileName(assetPath)} as Humanoid ({clipName}, loop={loop}).");
        }
    }

    private static AnimationClip LoadClip(string fbxPath)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        foreach (var a in assets)
        {
            if (a is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                return clip;
            }
        }
        return null;
    }

    private static void EnsureParameter(AnimatorController ctrl, string paramName, AnimatorControllerParameterType type)
    {
        foreach (var p in ctrl.parameters)
        {
            if (p.name == paramName) return;
        }
        ctrl.AddParameter(paramName, type);
    }
}
#endif
