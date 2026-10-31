#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Sets up the Agatha scary entity:
/// 1. Extracts / binds animation clips from agatha_rig.glb.
/// 2. Builds AgathaController.controller with Idle, Walk, Jog, Run, and Jumpscare states.
/// 3. Creates the ready-to-spawn AgathaPrefab.prefab with NavMeshAgent, AudioSource, and AgathaAI.
/// </summary>
public static class AgathaSetup
{
    private const string ModelPath = "Assets/_Project/Art/Level94/agatha_rig.glb";
    private const string ControllerPath = "Assets/_Project/Art/Level94/AgathaController.controller";
    private const string PrefabPath = "Assets/_Project/Art/Level94/Prefabs/Agatha_Stalker.prefab";

    [MenuItem("Tools/Backrooms/Entities/Agatha/Setup Agatha Entity & Controller", priority = 10)]
    public static void SetupAgatha()
    {
        // 1. Ensure directory
        string dir = Path.GetDirectoryName(PrefabPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // 2. Build Animator Controller
        AnimatorController controller = BuildAgathaController();

        // 3. Create / Configure Prefab
        CreateAgathaPrefab(controller);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("<color=#00FF88><b>[Agatha Setup]</b></color> Successfully created Agatha controller and prefab at: " + PrefabPath);
    }

    public static AnimatorController BuildAgathaController()
    {
        // Load model clips
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().ToArray();
        if (clips.Length == 0)
        {
            Debug.LogError("[Agatha Setup] No animation clips found inside " + ModelPath);
            return null;
        }

        AnimationClip idleClip = clips.FirstOrDefault(c => c.name.Contains("idle")) ?? clips[0];
        AnimationClip walkClip = clips.FirstOrDefault(c => c.name.Contains("walk")) ?? idleClip;
        AnimationClip jogClip = clips.FirstOrDefault(c => c.name.Contains("jog")) ?? walkClip;
        AnimationClip runClip = clips.FirstOrDefault(c => c.name.Contains("run")) ?? jogClip;
        AnimationClip jumpscareClip = clips.FirstOrDefault(c => c.name.Contains("jumpscare1")) ??
                                      clips.FirstOrDefault(c => c.name.Contains("jumpscare")) ?? idleClip;

        // Create controller
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        // Parameters
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("IsChasing", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Jumpscare", AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;

        // States
        var idleState = sm.AddState("Idle");
        idleState.motion = idleClip;

        var walkState = sm.AddState("Walk");
        walkState.motion = walkClip;

        var jogState = sm.AddState("Jog");
        jogState.motion = jogClip;

        var runState = sm.AddState("Run");
        runState.motion = runClip;

        var jumpState = sm.AddState("Jumpscare");
        jumpState.motion = jumpscareClip;

        sm.defaultState = idleState;

        // Transitions: Idle <-> Walk
        var idleToWalk = idleState.AddTransition(walkState);
        idleToWalk.AddCondition(AnimatorConditionMode.Greater, 0.15f, "Speed");
        idleToWalk.hasExitTime = false;
        idleToWalk.duration = 0.2f;

        var walkToIdle = walkState.AddTransition(idleState);
        walkToIdle.AddCondition(AnimatorConditionMode.Less, 0.15f, "Speed");
        walkToIdle.hasExitTime = false;
        walkToIdle.duration = 0.25f;

        // Walk <-> Jog
        var walkToJog = walkState.AddTransition(jogState);
        walkToJog.AddCondition(AnimatorConditionMode.Greater, 2.0f, "Speed");
        walkToJog.hasExitTime = false;
        walkToJog.duration = 0.2f;

        var jogToWalk = jogState.AddTransition(walkState);
        jogToWalk.AddCondition(AnimatorConditionMode.Less, 1.9f, "Speed");
        jogToWalk.hasExitTime = false;
        jogToWalk.duration = 0.2f;

        // Any -> Run (when chasing)
        var anyToRun = sm.AddAnyStateTransition(runState);
        anyToRun.AddCondition(AnimatorConditionMode.If, 0, "IsChasing");
        anyToRun.hasExitTime = false;
        anyToRun.duration = 0.15f;

        var runToIdle = runState.AddTransition(idleState);
        runToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsChasing");
        runToIdle.AddCondition(AnimatorConditionMode.Less, 0.2f, "Speed");
        runToIdle.hasExitTime = false;
        runToIdle.duration = 0.25f;

        // Any -> Jumpscare
        var anyToJump = sm.AddAnyStateTransition(jumpState);
        anyToJump.AddCondition(AnimatorConditionMode.If, 0, "Jumpscare");
        anyToJump.hasExitTime = false;
        anyToJump.duration = 0.05f;

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void CreateAgathaPrefab(AnimatorController controller)
    {
        GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelPrefab == null)
        {
            Debug.LogError("[Agatha Setup] Could not load model from " + ModelPath);
            return;
        }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
        go.name = "Agatha_Stalker";

        // Setup Animator
        Animator anim = go.GetComponent<Animator>();
        if (anim == null) anim = go.AddComponent<Animator>();
        anim.runtimeAnimatorController = controller;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        if (avatar != null) anim.avatar = avatar;

        // Setup NavMeshAgent
        NavMeshAgent agent = go.GetComponent<NavMeshAgent>();
        if (agent == null) agent = go.AddComponent<NavMeshAgent>();
        agent.speed = 1.75f;
        agent.acceleration = 12.0f;
        agent.angularSpeed = 360.0f;
        agent.stoppingDistance = 0.9f;
        agent.radius = 0.40f;
        agent.height = 1.85f;
        agent.baseOffset = 0.05f;

        // Setup AudioSource
        AudioSource audio = go.GetComponent<AudioSource>();
        if (audio == null) audio = go.AddComponent<AudioSource>();
        audio.spatialBlend = 1.0f;
        audio.rolloffMode = AudioRolloffMode.Linear;
        audio.minDistance = 3.0f;
        audio.maxDistance = 35.0f;

        // Setup AgathaAI
        AgathaAI ai = go.GetComponent<AgathaAI>();
        if (ai == null) ai = go.AddComponent<AgathaAI>();

        // Save Prefab
        PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);
    }
}
#endif
