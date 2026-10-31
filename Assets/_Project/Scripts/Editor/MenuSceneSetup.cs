using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// One click setup for the main menu scene.
/// Puts the UI document in front of the camera as a world space panel, adds the
/// menu scripts and makes sure the menu is the first scene of the build.
/// </summary>
public static class MenuSceneSetup
{
    const string MenuScenePath = "Assets/_Project/Scenes/Menu.unity";
    const string PanelSettingsPath = "Assets/_Project/UI/Menu Panel Settings.asset";

    [MenuItem("Tools/Backrooms/Scene Setup/Main Menu/Setup Main Menu Scene")]
    public static void SetupMenuScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MenuScenePath)
            scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);

        UIDocument document = Object.FindAnyObjectByType<UIDocument>();
        if (document == null)
        {
            Debug.LogError("[Backrooms] No UIDocument found in " + MenuScenePath);
            return;
        }

        Camera camera = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
        if (camera == null)
        {
            Debug.LogError("[Backrooms] No camera found in " + MenuScenePath);
            return;
        }

        // world space panel so the UI lives inside the level
        PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (settings != null)
        {
            settings.renderMode = PanelRenderMode.WorldSpace;
            settings.clearDepthStencil = false;
            settings.clearColor = false;
            EditorUtility.SetDirty(settings);
        }

        document.panelSettings = settings;
        document.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
        document.worldSpaceSize = new Vector2(1920f, 1080f);
        document.pivot = Pivot.Center;
        document.pivotReferenceSize = PivotReferenceSize.Layout;

        // attach it to the camera
        Transform panel = document.transform;
        panel.SetParent(camera.transform, false);
        panel.localPosition = new Vector3(0f, 0f, 1.5f);
        panel.localRotation = Quaternion.identity;

        float viewHeight = 2f * 1.5f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float viewWidth = viewHeight * 16f / 9f;
        panel.localScale = new Vector3(viewWidth * 100f / 1920f, viewHeight * 100f / 1080f, 1f);

        // menu scripts
        if (panel.GetComponent<MenuCameraHUD>() == null) panel.gameObject.AddComponent<MenuCameraHUD>();
        if (panel.GetComponent<MainMenuController>() == null) panel.gameObject.AddComponent<MainMenuController>();

        // In Unity 6, having both UIDocument and PanelRenderer on the same GameObject causes
        // the UI to render twice on top of itself. Remove any redundant PanelRenderer.
        foreach (var r in panel.GetComponents<Renderer>())
        {
            if (r != null && r.GetType().Name == "PanelRenderer")
            {
                Object.DestroyImmediate(r);
            }
        }

        // the HUD needs the same pixels per unit as the panel settings
        MenuCameraHUD hud = panel.GetComponent<MenuCameraHUD>();
        if (hud != null && settings != null)
        {
            var so = new SerializedObject(hud);
            SerializedProperty ppu = so.FindProperty("pixelsPerUnit");
            SerializedProperty settingsPpu = new SerializedObject(settings).FindProperty("m_PixelsPerUnit");
            if (ppu != null && settingsPpu != null)
            {
                ppu.floatValue = settingsPpu.floatValue;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        EditorUtility.SetDirty(document);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        AddSceneToBuildSettings();
        SetupMenuClarkAnimation();

        Debug.Log("[Backrooms] Main menu scene is ready. " + PanelSettingsPath + " is used as the world space panel.");
    }

    [MenuItem("Tools/Backrooms/Scene Setup/Main Menu/Setup Menu Clark Idle Animation")]
    public static void SetupMenuClarkAnimation()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MenuScenePath)
            scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);

        // Find Clark in the Menu scene
        GameObject clark = GameObject.Find("Pirate clark norpa");
        if (clark == null)
        {
            var allGo = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            clark = allGo.FirstOrDefault(go => go != null && go.name.ToLowerInvariant().Contains("clark"));
        }

        if (clark == null)
        {
            Debug.LogWarning("[MenuSceneSetup] Could not find Pirate Clark in Menu scene!");
            return;
        }

        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Art/Characters/Clark/ClarkVillain.controller");
        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Characters/Clark/Pirate clark norpa.glb").OfType<Avatar>().FirstOrDefault();

        Animator animator = clark.GetComponent<Animator>();
        if (animator == null)
        {
            animator = Undo.AddComponent<Animator>(clark);
        }

        Undo.RecordObject(animator, "Configure Menu Clark Animator");
        animator.runtimeAnimatorController = controller;
        if (avatar != null) animator.avatar = avatar;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.enabled = true;

        EditorUtility.SetDirty(animator);
        EditorUtility.SetDirty(clark);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("<color=#00FF88><b>[Menu Clark]</b></color> Successfully configured Pirate Clark in Menu scene! Idle animation is now active.");
    }

    /// <summary>Makes sure the menu is the first scene of the build.</summary>
    [MenuItem("Tools/Backrooms/Scene Setup/Main Menu/Add Menu To Build Settings")]
    public static void AddSceneToBuildSettings()
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        scenes.RemoveAll(s => s.path == MenuScenePath);
        scenes.Insert(0, new EditorBuildSettingsScene(MenuScenePath, true));

        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
