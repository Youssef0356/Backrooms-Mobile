using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Central store for the player's settings (backed by PlayerPrefs).
///
/// The menu writes through the Set... methods; values are cached, saved, and
/// re-applied automatically every time a scene loads, so graphics quality,
/// brightness and audio survive menu -> game transitions and app restarts.
///
/// Sensitivity uses the same PlayerPrefs key that FirstPersonController reads
/// (the player controller lives in the Starter Assets assembly and cannot
/// reference this class directly, so it reads the raw key instead).
/// </summary>
public static class GameSettings
{
    // PlayerPrefs keys
    public const string MasterVolumeKey = "backrooms.masterVolume";
    public const string MusicVolumeKey = "backrooms.musicVolume";
    public const string SensitivityKey = "backrooms.sensitivity";
    public const string BrightnessKey = "backrooms.brightness";
    public const string QualityKey = "backrooms.quality";
    public const string TargetFrameRateKey = "backrooms.targetFrameRate";

    // Defaults = current game behaviour before the player changes anything
    public const float DefaultMasterVolume = 0.8f;
    public const float DefaultMusicVolume = 0.25f;
    public const float DefaultSensitivity = 1f;      // menu slider shows 100%
    public const float DefaultBrightness = 0.5f;     // menu slider shows 50% = unchanged
    public const int DefaultQuality = -1;            // -1 = keep the project default
    public const int DefaultTargetFrameRate = 60;    // 60 FPS standard for mobile to prevent overheating

    static float master = DefaultMasterVolume;
    static float music = DefaultMusicVolume;
    static float sensitivity = DefaultSensitivity;
    static float brightness = DefaultBrightness;
    static int quality = DefaultQuality;
    static int targetFrameRate = DefaultTargetFrameRate;

    public static float MasterVolume => master;
    public static float MusicVolume => music;
    public static float Sensitivity => sensitivity;
    public static float Brightness => brightness;
    public static int QualityLevel => quality;
    public static int TargetFrameRate => targetFrameRate;

    public static event System.Action<float> OnSensitivityChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        Load();

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void Load()
    {
        master = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, DefaultMasterVolume));
        music = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume));
        sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, DefaultSensitivity), 0.1f, 2f);
        brightness = Mathf.Clamp01(PlayerPrefs.GetFloat(BrightnessKey, DefaultBrightness));
        quality = PlayerPrefs.GetInt(QualityKey, DefaultQuality);
        targetFrameRate = PlayerPrefs.GetInt(TargetFrameRateKey, DefaultTargetFrameRate);
    }

    static void OnSceneLoaded(Scene _, LoadSceneMode __) => ApplyAll();

    /// Re-applies everything that must be set up per scene.
    public static void ApplyAll()
    {
        AudioListener.volume = master;
        ApplyTargetFrameRate();
        ApplyQuality();
        ApplyBrightness();
    }

    // --------------------------------------------------------------- setters

    public static void SetMasterVolume(float value)
    {
        master = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(MasterVolumeKey, master);
        AudioListener.volume = master;
        Save();
    }

    public static void SetMusicVolume(float value)
    {
        music = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(MusicVolumeKey, music);
        Save();
        // Any playing ambience reads this key when its scene starts.
    }

    public static void SetSensitivity(float value)
    {
        sensitivity = Mathf.Clamp(value, 0.1f, 2f);
        PlayerPrefs.SetFloat(SensitivityKey, sensitivity);
        Save();
        OnSensitivityChanged?.Invoke(sensitivity);
        StarterAssets.TouchscreenInput.SetGlobalSensitivity(sensitivity);
    }

    public static void SetBrightness(float value)
    {
        brightness = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(BrightnessKey, brightness);
        Save();
        ApplyBrightness();          // live preview while dragging the slider
    }

    public static void SetQualityLevel(int level)
    {
        int max = Mathf.Max(0, QualitySettings.names.Length - 1);
        quality = Mathf.Clamp(level, 0, max);
        PlayerPrefs.SetInt(QualityKey, quality);
        Save();
        ApplyQuality();
    }

    public static void SetTargetFrameRate(int fps)
    {
        targetFrameRate = fps;
        PlayerPrefs.SetInt(TargetFrameRateKey, targetFrameRate);
        Save();
        ApplyTargetFrameRate();
    }

    // -------------------------------------------------------------- applying

    static void ApplyTargetFrameRate()
    {
        Application.targetFrameRate = targetFrameRate;
    }

    static void ApplyQuality()
    {
        if (quality < 0) return;                       // never changed = leave as-is
        int max = QualitySettings.names.Length - 1;
        if (max < 0) return;
        QualitySettings.SetQualityLevel(Mathf.Clamp(quality, 0, max), true);
    }

    /// Brightness is done through URP post exposure (-1..+1 EV around the 50%
    /// midpoint). Each Volume's runtime profile instance is modified, so the
    /// profile assets on disk are never touched.
    static void ApplyBrightness()
    {
        float exposure = (brightness - 0.5f) * 2f;     // 0..1 -> -1..+1 EV

        foreach (Volume volume in Object.FindObjectsByType<Volume>())
        {
            if (volume.sharedProfile == null) continue;

            VolumeProfile profile = volume.profile;    // runtime copy
            if (profile == null) continue;

            if (!profile.TryGet<ColorAdjustments>(out ColorAdjustments color))
                color = profile.Add<ColorAdjustments>(true);

            color.postExposure.overrideState = true;
            color.postExposure.value = exposure;
        }
    }

    static void Save() => PlayerPrefs.Save();
}
