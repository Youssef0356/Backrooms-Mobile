#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wires the "Pirate clark norpa" glTF model up as the level villain.
///
/// The .glb (UnityGLTF, Mecanim import) ships six in-place clips - idle, walk,
/// run, attack 1, attack 2, jump - plus a generic Avatar, but no Animator and no
/// controller, so none of them can play on their own. These menu items build the
/// missing pieces:
///
/// 1. "Build Clark Animator" creates ClarkVillain.controller with the parameter
///    names <see cref="VillainAI"/> drives (Speed, IsChasing, Attack1, Attack2).
///    Rebuilds the asset from scratch every time, so it is safe to re-run.
///
/// 2. "Set Up Clark Villain" does (1) if needed, then adds/refreshes the Animator
///    on the scene's "Pirate clark norpa" instance and points
///    BackroomsRoomGenerator.villain at it.
///
/// Run: Backrooms &gt; Pirate Clark, then save the scene (Ctrl+S).
/// </summary>
public static class PirateClarkSetup
{
    const string ModelPath = "Assets/_Project/Art/Characters/Clark/Pirate clark norpa.glb";
    const string ControllerPath = "Assets/_Project/Art/Characters/Clark/ClarkVillain.controller";
    const string VillainNameFragment = "clark";

    [MenuItem("Backrooms/Pirate Clark/Build Clark Animator")]
    public static void BuildAnimatorMenu()
    {
        AnimatorController controller = BuildController();
        if (controller != null)
            Debug.Log($"[Clark] Built {ControllerPath} ({controller.layers[0].stateMachine.states.Length} states).");
    }

    [MenuItem("Backrooms/Pirate Clark/Set Up Clark Villain")]
    public static void SetUpVillainMenu()
    {
        AnimatorController controller = BuildController();
        if (controller == null) return;

        VillainAI villain = Object.FindObjectsByType<VillainAI>(FindObjectsInactive.Include)
            .FirstOrDefault(v => v != null && v.gameObject.name.ToLowerInvariant().Contains(VillainNameFragment));

        if (villain == null)
        {
            Debug.LogError("[Clark] No 'Pirate clark norpa' object with a VillainAI was found in the open scene. " +
                           "Open MainLevel and try again.");
            return;
        }

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();

        Animator animator = villain.GetComponent<Animator>();
        bool added = false;
        if (animator == null)
        {
            animator = Undo.AddComponent<Animator>(villain.gameObject);
            added = true;
        }

        animator.runtimeAnimatorController = controller;
        if (avatar != null) animator.avatar = avatar;
        animator.applyRootMotion = false;                       // the clips are in place, the Agent moves him
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // he is part of the scare, never skip a frame

        EditorUtility.SetDirty(animator);

        string generatorReport = PointGeneratorAt(villain.gameObject);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        string avatarReport = animator.avatar != null ? $"Avatar '{animator.avatar.name}'" : "no Avatar (generic paths only)";
        Debug.Log($"[Clark] {(added ? "Added" : "Refreshed")} Animator on '{villain.gameObject.name}' -> " +
                  $"{controller.name}, {avatarReport}. {generatorReport} Save the scene (Ctrl+S) to keep it.");
    }

    // ------------------------------------------------------------------ controller

    static AnimatorController BuildController()
    {
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().ToArray();
        if (clips.Length == 0)
        {
            Debug.LogError($"[Clark] No animation clips inside {ModelPath}. " +
                           "Set its import 'Animations' option to Mecanim and re-import.");
            return null;
        }

        AnimationClip idle = FindClip(clips, "idle");
        AnimationClip walk = FindClip(clips, "walk");
        AnimationClip run = FindClip(clips, "run");
        AnimationClip attack1 = FindClip(clips, "attack 1");
        AnimationClip attack2 = FindClip(clips, "attack 2");
        AnimationClip jump = FindClip(clips, "jump");

        if (idle == null || walk == null || run == null)
        {
            Debug.LogError("[Clark] The glTF is missing one of the idle / walk / run clips.");
            return null;
        }

        // Rebuild from scratch so the asset always matches the code below.
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("IsChasing", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Attack1", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Attack2", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimatorState idleState = machine.AddState("Idle", new Vector3(300f, 0f, 0f));
        AnimatorState walkState = machine.AddState("Walk", new Vector3(300f, 80f, 0f));
        AnimatorState runState = machine.AddState("Run", new Vector3(300f, 160f, 0f));

        idleState.motion = idle;
        walkState.motion = walk;
        runState.motion = run;
        machine.defaultState = idleState;

        AddSpeedTransition(idleState, walkState, AnimatorConditionMode.Greater, 0.15f);
        AddSpeedTransition(walkState, idleState, AnimatorConditionMode.Less, 0.15f);
        AddSpeedTransition(walkState, runState, AnimatorConditionMode.Greater, 2.5f);
        AddSpeedTransition(runState, walkState, AnimatorConditionMode.Less, 2.4f);

        AddActionState(machine, "Attack 1", attack1, "Attack1", new Vector3(620f, 0f, 0f), idleState, walkState);
        AddActionState(machine, "Attack 2", attack2, "Attack2", new Vector3(620f, 80f, 0f), idleState, walkState);
        AddActionState(machine, "Jump", jump, "Jump", new Vector3(620f, 160f, 0f), idleState, walkState);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static void AddSpeedTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, float threshold)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0.15f;
        transition.AddCondition(mode, threshold, "Speed");
    }

    /// <summary>
    /// One-shot state fired by an AnyState trigger. Once the clip is nearly done it
    /// returns to Walk when he still has ground speed, otherwise to Idle.
    /// </summary>
    static void AddActionState(AnimatorStateMachine machine, string stateName, AnimationClip clip,
                               string trigger, Vector3 position, AnimatorState idleState, AnimatorState walkState)
    {
        if (clip == null) return;

        AnimatorState state = machine.AddState(stateName, position);
        state.motion = clip;

        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        enter.hasExitTime = false;
        enter.duration = 0.1f;
        enter.canTransitionToSelf = false;
        enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);

        AnimatorStateTransition toWalk = state.AddTransition(walkState);
        toWalk.hasExitTime = true;
        toWalk.exitTime = 0.9f;
        toWalk.duration = 0.15f;
        toWalk.AddCondition(AnimatorConditionMode.Greater, 0.15f, "Speed");

        AnimatorStateTransition toIdle = state.AddTransition(idleState);
        toIdle.hasExitTime = true;
        toIdle.exitTime = 0.9f;
        toIdle.duration = 0.15f;
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.15f, "Speed");
    }

    static AnimationClip FindClip(AnimationClip[] clips, string fragment)
    {
        return clips.FirstOrDefault(c => c != null &&
                                         c.name.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0);
    }

    // ------------------------------------------------------------------ scene

    static string PointGeneratorAt(GameObject villain)
    {
        BackroomsRoomGenerator generator = Object.FindAnyObjectByType<BackroomsRoomGenerator>();
        if (generator == null)
            return "No BackroomsRoomGenerator in the scene, so 'villain' was not reassigned.";

        SerializedObject serialized = new SerializedObject(generator);
        SerializedProperty property = serialized.FindProperty("villain");
        if (property == null)
            return "BackroomsRoomGenerator has no 'villain' field.";

        property.objectReferenceValue = villain;
        serialized.ApplyModifiedProperties();

        return "BackroomsRoomGenerator.villain now points at the Clark instance.";
    }
}
#endif
