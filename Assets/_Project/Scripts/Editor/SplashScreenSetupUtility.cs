#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class SplashScreenSetupUtility
{
    const string LogoPath = "Assets/_Project/Art/UI/Logo/backrooms_no_exit_title_transparent_cropped.png";

    [MenuItem("Tools/Backrooms/Build & Mobile/Configure Native Splash Screen")]
    public static void ConfigureNativeSplashScreen()
    {
        Sprite logoSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
        Texture2D logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);

        if (logoSprite == null && logoTexture == null)
        {
            Debug.LogError($"[SplashScreenSetup] Failed to load logo at '{LogoPath}'.");
            return;
        }

        // Configure PlayerSettings.SplashScreen
        PlayerSettings.SplashScreen.show = true;
        PlayerSettings.SplashScreen.backgroundColor = new Color(0.04f, 0.05f, 0.03f, 1f); // Dark Backrooms liminal tint
        PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Dolly;
        PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.DarkOnLight;
        PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.UnityLogoBelow;

        if (logoSprite != null)
        {
            PlayerSettings.SplashScreenLogo gameLogo = PlayerSettings.SplashScreenLogo.Create(2.5f, logoSprite);
            PlayerSettings.SplashScreen.logos = new PlayerSettings.SplashScreenLogo[] { gameLogo };
            Debug.Log($"[SplashScreenSetup] Set native splash logo with duration 2.5s using {logoSprite.name}.");
        }

        // Configure native Android Splash Screen through SerializedObject to ensure exact engine property mapping
        Object[] projectSettingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (projectSettingsAssets != null && projectSettingsAssets.Length > 0)
        {
            SerializedObject serializedSettings = new SerializedObject(projectSettingsAssets[0]);

            // Set Android native splash screen texture and scale
            SerializedProperty androidSplashProp = serializedSettings.FindProperty("androidSplashScreen");
            if (androidSplashProp != null && logoTexture != null)
            {
                androidSplashProp.objectReferenceValue = logoTexture;
            }

            SerializedProperty androidScaleProp = serializedSettings.FindProperty("AndroidSplashScreenScale");
            if (androidScaleProp != null)
            {
                // 0: Center, 1: Scale to fit (letterbox)
                androidScaleProp.intValue = 1;
            }

            serializedSettings.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("[SplashScreenSetup] Successfully configured native Android splash screen.");

            if (!EditorApplication.isUpdating && !EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog(
                    "Native Splash Screen Configured",
                    "The native Android & engine splash screen is configured with the 'Backrooms No Exit' transparent logo!\n\nYou can inspect or preview it anytime in:\nEdit > Project Settings > Player > Splash Image",
                    "OK"
                );
            }
        }
    }
}
#endif
