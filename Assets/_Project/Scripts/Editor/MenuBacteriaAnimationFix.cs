#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;   // Unity 6: AnimatorController lives here (was UnityEditor.Animators pre-6)
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click repair for the Menu scene:
///
/// 1. Removes the stray legacy <see cref="Animation"/> component that was added
///    to the "backrooms_vr (1)" environment model. Its clip was
///    "Walking (1)" from Bacteria@Walking.fbx, which is a Humanoid (non-legacy)
///    clip, so it could never play there - Unity only threw the
///    "must be marked as Legacy" / "Default clip could not be found" errors.
///    The clip also has to stay non-legacy because VillainController uses it.
///
/// 2. Gives the "Bacteria@Walking" instance an Animator driven by a menu-only
///    copy of VillainController (MenuBacteriaController) that starts in the
///    Walking state. The shared controller has IsChasing defaulting to true,
///    which would drop the menu creature straight into Zombie Run, so the copy
///    flips that default - the MainLevel villain keeps the original controller.
///
/// Run: Backrooms &gt; Fix Menu Bacteria Animation, then save the scene (Ctrl+S).
/// Safe to run more than once.
/// </summary>
public static class MenuBacteriaAnimationFix
{
    const string MenuScenePath = "Assets/_Project/Scenes/Menu.unity";
    const string SourceControllerPath = "Assets/_Project/Art/Characters/Villain/VillainController.controller";
    const string MenuControllerPath = "Assets/_Project/Art/Characters/Villain/MenuBacteriaController.controller";
    const string AvatarSourcePath = "Assets/_Project/Art/Characters/Bacteria/Bacteria@Zombie Run.fbx";
    const string EnvironmentObjectPrefix = "backrooms_vr";
    const string BacteriaRootName = "Bacteria@Walking";

    [MenuItem("Backrooms/Fix Menu Bacteria Animation")]
    public static void Fix()
    {
        if (SceneManager.GetActiveScene().path != MenuScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(MenuScenePath);
        }

        int removed = RemoveStrayAnimation();
        string animatorReport = SetupBacteriaAnimator();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[MenuFix] removed {removed} stray Animation component(s). {animatorReport} " +
                  "Save the scene (Ctrl+S) to keep the changes.");
    }

    /// <summary>Deletes the legacy Animation that sits on the environment model.</summary>
    static int RemoveStrayAnimation()
    {
        Animation[] strays = Object.FindObjectsByType<Animation>(FindObjectsInactive.Include)
            .Where(animation => animation != null && animation.gameObject.name.StartsWith(EnvironmentObjectPrefix))
            .ToArray();

        foreach (Animation animation in strays)
            Undo.DestroyObjectImmediate(animation);

        return strays.Length;
    }

    /// <summary>Reuses the Bacteria's Animator when it has one, otherwise adds it.</summary>
    static string SetupBacteriaAnimator()
    {
        Transform bacteria = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .FirstOrDefault(transform => transform.name == BacteriaRootName && transform.parent == null);

        if (bacteria == null)
            return $"ERROR: no root object named '{BacteriaRootName}' found in the Menu scene.";

        RuntimeAnimatorController controller = EnsureMenuController();
        if (controller == null)
            return $"ERROR: could not create {MenuControllerPath} from {SourceControllerPath}.";

        bool added = false;
        Animator animator = bacteria.GetComponent<Animator>();

        if (animator == null)
        {
            animator = Undo.AddComponent<Animator>(bacteria.gameObject);
            added = true;

            // Same Avatar the MainLevel villain instance uses - both FBX files are
            // the same Mixamo rig, so it retargets cleanly.
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(AvatarSourcePath).OfType<Avatar>().FirstOrDefault();
            if (avatar != null) animator.avatar = avatar;
        }

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // keep it animating even when off-camera

        string avatarReport = animator.avatar != null ? $" (Avatar: {animator.avatar.name})" : " (Avatar: none)";
        return $"{(added ? "Added" : "Reused")} Animator on '{BacteriaRootName}' -> {controller.name}{avatarReport}.";
    }

    /// <summary>
    /// Duplicates VillainController for the menu so IsChasing starts false and the
    /// creature stays in the Walking state. The shared controller is untouched.
    /// </summary>
    static RuntimeAnimatorController EnsureMenuController()
    {
        RuntimeAnimatorController existing = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(MenuControllerPath);
        if (existing != null) return existing;

        // CopyAsset (not Instantiate) so every state/transition sub-asset comes along.
        if (!AssetDatabase.CopyAsset(SourceControllerPath, MenuControllerPath)) return null;

        AnimatorController copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(MenuControllerPath);
        if (copy == null) return null;

        AnimatorControllerParameter[] parameters = copy.parameters;
        foreach (AnimatorControllerParameter parameter in parameters)
        {
            if (parameter.name == "IsChasing") parameter.defaultBool = false;
        }
        copy.parameters = parameters;

        EditorUtility.SetDirty(copy);
        AssetDatabase.SaveAssets();
        return copy;
    }
}
#endif
