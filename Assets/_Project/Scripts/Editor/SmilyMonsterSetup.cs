#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Sets up the "smily horror monster" 4-legged beast:
/// 1. Bakes a procedural 4-legged Gallop animation (.anim).
/// 2. Bakes a pounce Attack animation (.anim).
/// 3. Builds SmilyMonster.controller with Idle (glTF clip) and Gallop states.
/// 4. Provides one-click scene setup to place or set as the level villain.
/// </summary>
public static class SmilyMonsterSetup
{
    const string ModelPath = "Assets/_Project/Art/Characters/smily dog/smily_horror_monster.glb";
    const string OutputFolder = "Assets/_Project/Art/Characters/smily dog";
    const string ControllerPath = OutputFolder + "/SmilyMonster.controller";
    const string GallopClipPath = OutputFolder + "/SmilyDog_Gallop.anim";
    const string AttackClipPath = OutputFolder + "/SmilyDog_Attack.anim";

    [InitializeOnLoadMethod]
    private static void AutoInit()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            // Build the animation clips and controller if not already created
            if (!File.Exists(GallopClipPath) || !File.Exists(ControllerPath))
            {
                BuildAnimatorAndClips();
            }
        };
    }

    [MenuItem("Tools/Backrooms/Entities/Smily Monster/1. Build Gallop & Controller")]
    public static void BuildAnimatorAndClips()
    {
        // 1. Bake 4-Legged Gallop Animation Clip
        AnimationClip gallopClip = BakeGallopClip();
        Debug.Log($"[SmilyMonster] Baked Gallop clip: {GallopClipPath}");

        // 2. Bake Pounce Attack Clip
        AnimationClip attackClip = BakeAttackClip();
        Debug.Log($"[SmilyMonster] Baked Attack clip: {AttackClipPath}");

        // 3. Find glTF Idle clip
        AnimationClip[] modelClips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().ToArray();
        AnimationClip idleClip = modelClips.FirstOrDefault(c => c.name.Contains("ArmatureAction")) ?? modelClips.FirstOrDefault();

        // 4. Build Animator Controller
        AnimatorController controller = BuildController(idleClip, gallopClip, attackClip);
        Debug.Log($"[SmilyMonster] Successfully built controller: {ControllerPath}");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Tools/Backrooms/Entities/Smily Monster/3. Set Up Hound at Exit Portal")]
    public static void SetUpHoundAtExitMenu()
    {
        BuildAnimatorAndClips();

        GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelPrefab == null)
        {
            Debug.LogError($"[SmilyMonster] Could not load model at {ModelPath}");
            return;
        }

        string houndPrefabPath = OutputFolder + "/SmilyHoundPrefab.prefab";

        // 1. Root GameObject (carries AI, NavMeshAgent, Audio, Gait)
        GameObject rootGo = new GameObject("SmilyHorrorHound");

        // 2. Visual Model Child (normalizes scale from 100m down to 0.85m, aligned with origin facing forward +Z)
        GameObject visualChild = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
        visualChild.name = "VisualModel";
        visualChild.transform.SetParent(rootGo.transform, false);

        const float modelScale = 0.013f; // Authentic Inside the Backrooms crawling quadruped proportion (~0.95m tall)
        visualChild.transform.localPosition = new Vector3(-0.98f, 0f, -0.18f);
        visualChild.transform.localRotation = Quaternion.identity;
        visualChild.transform.localScale = new Vector3(modelScale, modelScale, modelScale);

        // 3. Animator on visualChild so relative clip paths match
        Animator animator = visualChild.GetComponent<Animator>();
        if (animator == null) animator = visualChild.AddComponent<Animator>();
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // 4. NavMeshAgent on rootGo
        NavMeshAgent agent = rootGo.AddComponent<NavMeshAgent>();
        agent.radius = 0.42f;
        agent.height = 0.90f;
        agent.speed = 4.4f;
        agent.acceleration = 24f;
        agent.angularSpeed = 720f;
        agent.stoppingDistance = 1.1f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        agent.avoidancePriority = 35;

        // CapsuleCollider for hitboxes and raycasts
        CapsuleCollider col = rootGo.AddComponent<CapsuleCollider>();
        col.radius = 0.42f;
        col.height = 0.85f;
        col.center = new Vector3(0f, 0.425f, 0f);
        col.direction = 1;

        // 5. SmilyMonsterGait (Procedural 4-Legged Runner)
        SmilyMonsterGait gait = rootGo.AddComponent<SmilyMonsterGait>();
        gait.maxSpeed = 4.4f;

        // 6. SmilyHoundAI (Exit Portal Guardian)
        SmilyHoundAI ai = rootGo.AddComponent<SmilyHoundAI>();
        ai.territoryRadius = 24f;
        ai.leashMaxDistance = 34f;
        ai.wanderRadius = 14f;
        ai.waitAtPoint = 2.0f;
        ai.chaseSpeed = 4.4f;

        // 7. AudioSource
        AudioSource audio = rootGo.AddComponent<AudioSource>();
        audio.spatialBlend = 1.0f;
        audio.minDistance = 2.0f;
        audio.maxDistance = 24.0f;

        // Save as prefab asset
        GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(rootGo, houndPrefabPath);
        Object.DestroyImmediate(rootGo);

        // Cleanup: remove any redundant roaming SmilyHorrorMonster in scene so only 1 dog exists
        GameObject roamingDog = GameObject.Find("SmilyHorrorMonster");
        if (roamingDog != null)
        {
            Undo.DestroyObjectImmediate(roamingDog);
            Debug.Log("[SmilyMonster] Removed duplicate roaming dog; only 1 dog now exists at the Exit Portal!");
        }

        // Reassign generator.villain to Pirate Clark if it was pointing to the dog
        BackroomsRoomGenerator generator = Object.FindAnyObjectByType<BackroomsRoomGenerator>();
        if (generator != null)
        {
            SerializedObject so = new SerializedObject(generator);
            so.FindProperty("enableSmilyHound").boolValue = true;
            SerializedProperty houndProp = so.FindProperty("smilyHoundPrefab");
            if (houndProp != null) houndProp.objectReferenceValue = savedPrefab;

            SerializedProperty villainProp = so.FindProperty("villain");
            if (villainProp != null && (villainProp.objectReferenceValue == null || villainProp.objectReferenceValue.name.Contains("Smily")))
            {
                // Find Clark in scene
                VillainAI clarkAi = Object.FindObjectsByType<VillainAI>(FindObjectsInactive.Include)
                    .FirstOrDefault(v => v != null && v.gameObject.name.ToLowerInvariant().Contains("clark"));
                if (clarkAi != null) villainProp.objectReferenceValue = clarkAi.gameObject;
            }
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[SmilyMonster] BackroomsRoomGenerator configured with normalized SmilyHoundPrefab at Exit Portal!");
        }

        Debug.Log($"[SmilyMonster] Created Normalized Smart Hound Prefab: {houndPrefabPath}");
    }

    [MenuItem("Tools/Backrooms/Entities/Smily Monster/2. Set Up Monster as Level Villain")]
    public static void SetUpVillainMenu()
    {
        BuildAnimatorAndClips();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (prefab == null)
        {
            Debug.LogError($"[SmilyMonster] Could not load model at {ModelPath}");
            return;
        }

        // Check if an instance already exists in scene
        GameObject monsterGo = GameObject.Find("SmilyHorrorMonster");
        if (monsterGo == null)
        {
            monsterGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            monsterGo.name = "SmilyHorrorMonster";
            Undo.RegisterCreatedObjectUndo(monsterGo, "Spawn Smily Monster");
        }

        // Place at 0, 0, 0 or ground
        monsterGo.transform.position = new Vector3(0f, 0f, 0f);
        monsterGo.transform.localScale = Vector3.one;

        // Configure Animator
        Animator animator = monsterGo.GetComponent<Animator>();
        if (animator == null) animator = Undo.AddComponent<Animator>(monsterGo);
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // Configure NavMeshAgent (calibrated for 4-legged beast)
        NavMeshAgent agent = monsterGo.GetComponent<NavMeshAgent>();
        if (agent == null) agent = Undo.AddComponent<NavMeshAgent>(monsterGo);
        agent.radius = 0.45f;
        agent.height = 1.35f;
        agent.speed = 4.2f;
        agent.acceleration = 12f;
        agent.angularSpeed = 500f;
        agent.stoppingDistance = 1.2f;

        // Configure Procedural Gait component for dynamic speed syncing
        SmilyMonsterGait gait = monsterGo.GetComponent<SmilyMonsterGait>();
        if (gait == null) gait = Undo.AddComponent<SmilyMonsterGait>(monsterGo);
        gait.maxSpeed = 4.2f;

        // Configure VillainAI
        VillainAI villainAI = monsterGo.GetComponent<VillainAI>();
        if (villainAI == null) villainAI = Undo.AddComponent<VillainAI>(monsterGo);
        villainAI.wanderSpeed = 1.6f;
        villainAI.investigateSpeed = 2.8f;
        villainAI.chaseSpeed = 4.2f;
        villainAI.attackDistance = 2.2f;
        villainAI.eyeHeight = 0.85f; // Low head height on all fours!

        // Point BackroomsRoomGenerator at this monster
        BackroomsRoomGenerator generator = Object.FindAnyObjectByType<BackroomsRoomGenerator>();
        if (generator != null)
        {
            SerializedObject so = new SerializedObject(generator);
            SerializedProperty villainProp = so.FindProperty("villain");
            if (villainProp != null)
            {
                villainProp.objectReferenceValue = monsterGo;
                so.ApplyModifiedProperties();
                Debug.Log("[SmilyMonster] BackroomsRoomGenerator.villain now points to SmilyHorrorMonster!");
            }
        }

        // Monster starts inactive until level is generated
        monsterGo.SetActive(false);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[SmilyMonster] Smily Horror Monster fully configured as level villain!");
    }

    // -------------------------------------------------------------------------
    // Animation Baking
    // -------------------------------------------------------------------------

    static AnimationClip BakeGallopClip()
    {
        AnimationClip clip = new AnimationClip { name = "SmilyDog_Gallop", frameRate = 30f };

        // Relative bone paths in glTF
        string rootPrefix = "Sketchfab_model/7ee9998422e14e4e8b2859eed21c8595.fbx/Object_2/RootNode/Armature/Object_6/_rootJoint";
        string hipsPath = rootPrefix + "/spinebase_01";
        string spineMidPath = hipsPath + "/belly_02";
        string chestPath = spineMidPath + "/chest_03";
        string neckPath = chestPath + "/neck_04";
        string headPath = neckPath + "/head_05";
        string jawPath = headPath + "/jaw_06";

        string frontArmLPath = chestPath + "/shoulder.L_08/Arm.L_09";
        string frontForearmLPath = frontArmLPath + "/Forearm.L_010";
        string frontHandLPath = frontForearmLPath + "/Hand.L_011";

        string frontArmRPath = chestPath + "/shoulder.R_021/Arm.R_022";
        string frontForearmRPath = frontArmRPath + "/Forearm.R_023";
        string frontHandRPath = frontForearmRPath + "/Hand.R_024";

        string rearLegLPath = rootPrefix + "/Hip.L_035/leg.L_036";
        string rearCalfLPath = rearLegLPath + "/sheen.L_037";
        string rearFootLPath = rearCalfLPath + "/foot.L_038";

        string rearLegRPath = rootPrefix + "/Hip.R_040/leg.R_041";
        string rearCalfRPath = rearLegRPath + "/sheen.R_042";
        string rearFootRPath = rearCalfRPath + "/foot.R_043";

        const float duration = 0.60f; // Rapid 4-legged gallop loop
        const int samples = 19;       // 0 to 18 frames at 30fps

        // Build keyframes for each bone
        // Pitch swing on X axis (degrees)
        AddPitchCurve(clip, hipsPath, samples, duration, 0f, -171.6f, 10f, 0f);
        AddPitchCurve(clip, spineMidPath, samples, duration, 0f, -23.9f, -14f, 0f);
        AddPitchCurve(clip, chestPath, samples, duration, 0f, -1.9f, -10f, 0f);
        AddPitchCurve(clip, headPath, samples, duration, 0f, -28.5f, 12f, 0f);
        AddPitchCurve(clip, jawPath, samples, duration, 0f, 94.0f, 22f, 0.25f);

        // Front Limbs (Forelegs)
        AddPitchCurve(clip, frontArmLPath, samples, duration, 0.00f, -62.6f, 32f, 0f);
        AddPitchCurve(clip, frontForearmLPath, samples, duration, -0.15f, -17.9f, -24f, 0f);
        AddPitchCurve(clip, frontHandLPath, samples, duration, 0.00f, 1.1f, -16f, 0f);

        AddPitchCurve(clip, frontArmRPath, samples, duration, 0.22f, -28.4f, 32f, 0f);
        AddPitchCurve(clip, frontForearmRPath, samples, duration, 0.07f, -39.2f, -24f, 0f);
        AddPitchCurve(clip, frontHandRPath, samples, duration, 0.22f, 4.2f, -16f, 0f);

        // Rear Limbs (Hindlegs)
        AddPitchCurve(clip, rearLegLPath, samples, duration, 0.50f, -25.5f, 36f, 0f);
        AddPitchCurve(clip, rearCalfLPath, samples, duration, 0.65f, -58.8f, -32f, 0f);
        AddPitchCurve(clip, rearFootLPath, samples, duration, 0.50f, 47.5f, -20f, 0f);

        AddPitchCurve(clip, rearLegRPath, samples, duration, 0.72f, -69.1f, 36f, 0f);
        AddPitchCurve(clip, rearCalfRPath, samples, duration, 0.87f, -36.6f, -32f, 0f);
        AddPitchCurve(clip, rearFootRPath, samples, duration, 0.72f, 47.5f, -20f, 0f);

        // Mark clip as looping
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        // Save asset
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(GallopClipPath) != null)
            AssetDatabase.DeleteAsset(GallopClipPath);

        AssetDatabase.CreateAsset(clip, GallopClipPath);
        return clip;
    }

    static AnimationClip BakeAttackClip()
    {
        AnimationClip clip = new AnimationClip { name = "SmilyDog_Attack", frameRate = 30f };

        string rootPrefix = "Sketchfab_model/7ee9998422e14e4e8b2859eed21c8595.fbx/Object_2/RootNode/Armature/Object_6/_rootJoint";
        string hipsPath = rootPrefix + "/spinebase_01";
        string jawPath = hipsPath + "/belly_02/chest_03/neck_04/head_05/jaw_06";
        string frontArmLPath = hipsPath + "/belly_02/chest_03/shoulder.L_08/Arm.L_09";
        string frontArmRPath = hipsPath + "/belly_02/chest_03/shoulder.R_021/Arm.R_022";

        const float duration = 0.75f;
        const int samples = 23;

        // Pounce lunge curve: wind up -> leap forward -> land
        AnimationCurve jawCurve = new AnimationCurve();
        AnimationCurve hipsCurve = new AnimationCurve();
        AnimationCurve armCurve = new AnimationCurve();

        for (int i = 0; i < samples; i++)
        {
            float tNorm = (float)i / (samples - 1);
            float time = tNorm * duration;
            float lunge = Mathf.Sin(tNorm * Mathf.PI);

            jawCurve.AddKey(time, 94.0f + lunge * 38f);
            hipsCurve.AddKey(time, -171.6f - lunge * 18f);
            armCurve.AddKey(time, -62.6f + lunge * 28f);
        }

        SetEulerCurve(clip, jawPath, "localEulerAnglesRaw.x", jawCurve);
        SetEulerCurve(clip, hipsPath, "localEulerAnglesRaw.x", hipsCurve);
        SetEulerCurve(clip, frontArmLPath, "localEulerAnglesRaw.x", armCurve);
        SetEulerCurve(clip, frontArmRPath, "localEulerAnglesRaw.x", armCurve);

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(AttackClipPath) != null)
            AssetDatabase.DeleteAsset(AttackClipPath);

        AssetDatabase.CreateAsset(clip, AttackClipPath);
        return clip;
    }

    static void AddPitchCurve(AnimationClip clip, string bonePath, int samples, float duration,
                              float phaseOffsetNorm, float baseAngle, float amplitude, float timeOffset)
    {
        AnimationCurve curve = new AnimationCurve();
        float twoPi = Mathf.PI * 2f;

        for (int i = 0; i < samples; i++)
        {
            float tNorm = (float)i / (samples - 1);
            float time = tNorm * duration;
            float phase = (tNorm + phaseOffsetNorm) * twoPi;
            float val = baseAngle + Mathf.Sin(phase) * amplitude;
            curve.AddKey(time, val);
        }

        // Smooth tangents for seamless looping
        for (int i = 0; i < curve.keys.Length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
        }

        SetEulerCurve(clip, bonePath, "localEulerAnglesRaw.x", curve);
    }

    static void SetEulerCurve(AnimationClip clip, string path, string prop, AnimationCurve curve)
    {
        EditorCurveBinding binding = new EditorCurveBinding
        {
            path = path,
            type = typeof(Transform),
            propertyName = prop
        };
        AnimationUtility.SetEditorCurve(clip, binding, curve);
    }

    // -------------------------------------------------------------------------
    // Animator Controller Construction
    // -------------------------------------------------------------------------

    static AnimatorController BuildController(AnimationClip idleClip, AnimationClip gallopClip, AnimationClip attackClip)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("IsChasing", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Attack1", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        // Idle State
        AnimatorState idleState = sm.AddState("Idle", new Vector3(300f, 0f, 0f));
        if (idleClip != null) idleState.motion = idleClip;

        // Gallop State (4-legged run)
        AnimatorState gallopState = sm.AddState("Gallop", new Vector3(300f, 100f, 0f));
        if (gallopClip != null) gallopState.motion = gallopClip;

        sm.defaultState = idleState;

        // Transitions Idle <-> Gallop
        AnimatorStateTransition toGallop = idleState.AddTransition(gallopState);
        toGallop.hasExitTime = false;
        toGallop.duration = 0.15f;
        toGallop.AddCondition(AnimatorConditionMode.Greater, 0.15f, "Speed");

        AnimatorStateTransition toIdle = gallopState.AddTransition(idleState);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.20f;
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.12f, "Speed");

        // Attack State
        if (attackClip != null)
        {
            AnimatorState attackState = sm.AddState("Attack", new Vector3(560f, 50f, 0f));
            attackState.motion = attackClip;

            AnimatorStateTransition toAttack = sm.AddAnyStateTransition(attackState);
            toAttack.hasExitTime = false;
            toAttack.duration = 0.1f;
            toAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack1");

            AnimatorStateTransition attackToGallop = attackState.AddTransition(gallopState);
            attackToGallop.hasExitTime = true;
            attackToGallop.exitTime = 0.85f;
            attackToGallop.duration = 0.15f;
            attackToGallop.AddCondition(AnimatorConditionMode.Greater, 0.15f, "Speed");

            AnimatorStateTransition attackToIdle = attackState.AddTransition(idleState);
            attackToIdle.hasExitTime = true;
            attackToIdle.exitTime = 0.85f;
            attackToIdle.duration = 0.15f;
            attackToIdle.AddCondition(AnimatorConditionMode.Less, 0.15f, "Speed");
        }

        EditorUtility.SetDirty(controller);
        return controller;
    }
}
#endif
