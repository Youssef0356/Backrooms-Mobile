#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// Temporary probe: enters play mode on the menu scene, grabs a series of
/// screenshots and prints how the world space panel is sized.
/// Deleted after the verification.
public static class MenuPlayModeProbe
{
    const string MarkerPath = @"C:\Users\Youssef356\AppData\Local\Temp\opencode\menu_probe.marker";
    const string ShotFolder = @"C:\Users\Youssef356\AppData\Local\Temp\opencode";
    const string MenuScenePath = "Assets/UI/Menu.unity";

    static string previousScenePath;

    [InitializeOnLoadMethod]
    static void AutoStart()
    {
        EditorApplication.delayCall += () =>
        {
            if (File.Exists(MarkerPath))
            {
                Debug.Log("[Probe] marker exists, not starting again");
                return;
            }

            File.WriteAllText(MarkerPath, "started");

            Scene active = SceneManager.GetActiveScene();
            if (active.path != MenuScenePath)
            {
                if (active.isDirty)
                {
                    Debug.Log("[Probe] active scene has unsaved changes, skipping");
                    return;
                }

                previousScenePath = active.path;
                EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
            }

            EditorApplication.playModeStateChanged += RestoreScene;
            EditorApplication.EnterPlaymode();
        };
    }

    static void RestoreScene(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= RestoreScene;

        if (!string.IsNullOrEmpty(previousScenePath))
        {
            EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
            previousScenePath = null;
        }
    }

    static int frames;
    static int actionIndex;
    static int waitFrames;
    static string pendingCapture;

    static readonly string[] ShotNames =
    {
        "01_all",
        "02_no_scanlines",
        "03_no_scanlines_no_vignette",
        "04_no_fx",
        "05_scanlines_only",
        "06_scanlines_vignette",
        "07_back_to_all"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        frames = 0;
        actionIndex = 0;
        waitFrames = 0;
        pendingCapture = null;
        EditorApplication.update += Step;
    }

    static void Step()
    {
        frames++;
        if (frames < 60) return;

        if (waitFrames > 0)
        {
            waitFrames--;
            if (waitFrames == 0 && pendingCapture != null)
            {
                Capture(pendingCapture);
                pendingCapture = null;
            }
            return;
        }

        if (actionIndex >= ShotNames.Length)
        {
            EditorApplication.update -= Step;
            EditorApplication.isPlaying = false;
            return;
        }

        if (actionIndex == 0) Report();

        ApplyState(actionIndex);
        pendingCapture = ShotNames[actionIndex];
        actionIndex++;
        waitFrames = 6;
    }

    static void ApplyState(int index)
    {
        UIDocument doc = Object.FindAnyObjectByType<UIDocument>();
        VisualElement root = doc != null ? doc.rootVisualElement : null;
        if (root == null) return;

        switch (index)
        {
            case 0: SetVisible(root, "fx-scanlines", true); SetVisible(root, "fx-vignette", true); SetVisible(root, "fx-scrim", true); break;
            case 1: SetVisible(root, "fx-scanlines", false); break;
            case 2: SetVisible(root, "fx-vignette", false); break;
            case 3: SetVisible(root, "fx-scrim", false); break;
            case 4: SetVisible(root, "fx-scanlines", true); break;
            case 5: SetVisible(root, "fx-vignette", true); break;
            case 6: SetVisible(root, "fx-scrim", true); break;
        }
    }

    static void Report()
    {
        UIDocument doc = Object.FindAnyObjectByType<UIDocument>();
        if (doc == null)
        {
            Debug.Log("[Probe] no UIDocument");
            return;
        }

        VisualElement root = doc.rootVisualElement;

        Debug.Log($"[Probe] root resolved={root.resolvedStyle.width}x{root.resolvedStyle.height}");
        Debug.Log($"[Probe] worldSpaceSize={doc.worldSpaceSize} mode={doc.worldSpaceSizeMode}");
        Debug.Log($"[Probe] panelSettings={(doc.panelSettings == null ? "NULL" : doc.panelSettings.name)} renderMode={doc.panelSettings?.renderMode}");
        Debug.Log($"[Probe] transform parent={doc.transform.parent?.name} localPos={doc.transform.localPosition} localScale={doc.transform.localScale}");
        Debug.Log($"[Probe] title='{root.Q<Label>("title")?.text}' version='{root.Q<Label>("version-label")?.text}' buttons={root.Query<Button>().ToList().Count}");

        Button play = root.Q<Button>("play-button");
        if (play != null)
        {
            Debug.Log($"[Probe] button shadow={play.resolvedStyle.textShadow} bg={play.resolvedStyle.backgroundColor} borderTop={play.resolvedStyle.borderTopWidth} borderLeft={play.resolvedStyle.borderLeftWidth} font={play.resolvedStyle.fontSize} letterSpacing={play.resolvedStyle.letterSpacing}");
        }

        Label subtitle = root.Q<Label>("subtitle");
        if (subtitle != null)
            Debug.Log($"[Probe] subtitle shadow={subtitle.resolvedStyle.textShadow}");

        Label title = root.Q<Label>("title");
        if (title != null)
            Debug.Log($"[Probe] title shadow={title.resolvedStyle.textShadow}");

        Label version = root.Q<Label>("version-label");
        if (version != null)
        {
            Debug.Log($"[Probe] version outline w={version.resolvedStyle.unityTextOutlineWidth} color={version.resolvedStyle.unityTextOutlineColor}");
            Debug.Log($"[Probe] version font={version.resolvedStyle.unityFontDefinition.font} fontDef={version.resolvedStyle.unityFontDefinition}");
            foreach (var property in typeof(IStyle).GetProperties())
            {
                object value;
                try { value = property.GetValue(version.resolvedStyle); }
                catch { continue; }

                string text = value != null ? value.ToString() : "null";
                if (text == "0" || text == "0px" || text == "RGBA(0.000, 0.000, 0.000, 0.000)" ||
                    text == "None" || text == "Auto" || text == "null" || text == "False" ||
                    text == "Stretch" || text == "FlexStart" || text.StartsWith("RGBA(1.000, 1.000, 1.000, 1.000)"))
                    continue;

                Debug.Log($"[Probe]   style.{property.Name} = {text}");
            }
        }
    }

    static void CaptureAll()
    {
        UIDocument doc = Object.FindAnyObjectByType<UIDocument>();
        VisualElement root = doc != null ? doc.rootVisualElement : null;

        Capture("01_all");

        SetVisible(root, "fx-scanlines", false);
        Capture("02_no_scanlines");

        SetVisible(root, "fx-vignette", false);
        Capture("03_no_scanlines_no_vignette");

        SetVisible(root, "fx-scrim", false);
        Capture("04_no_fx");

        SetVisible(root, "fx-scanlines", true);
        Capture("05_scanlines_only");

        SetVisible(root, "fx-vignette", true);
        Capture("06_scanlines_vignette");

        SetVisible(root, "fx-scrim", true);
        Capture("07_back_to_all");
    }

    static void Report2() { }

    static void SetVisible(VisualElement root, string name, bool visible)
    {
        VisualElement element = root.Q<VisualElement>(name);
        if (element == null) return;
        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    static void Capture(string name)
    {
        try
        {
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex == null)
            {
                Debug.Log("[Probe] screenshot was null");
                return;
            }

            string path = Path.Combine(ShotFolder, "menu_" + name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[Probe] wrote {path}");
        }
        catch (System.Exception e)
        {
            Debug.Log("[Probe] screenshot failed: " + e.Message);
        }
    }
}
#endif
