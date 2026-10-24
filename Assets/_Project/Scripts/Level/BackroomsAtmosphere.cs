using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Pushes the generated level towards the "found footage" look of the backrooms:
/// warm distance haze, a graded and grainy image, glowing ceiling fixtures and a
/// camera that actually runs URP post-processing.
///
/// <see cref="BackroomsRoomGenerator"/> creates this at runtime, so nothing has
/// to be wired in the scene. Add the component by hand if you want to tweak the
/// values and keep them.
/// </summary>
public class BackroomsAtmosphere : MonoBehaviour
{
    [Header("Haze")]
    [Tooltip("Dark distance fog. This makes corridors fade into eerie pitch-black shadows.")]
    [SerializeField] bool enableFog = true;
    [SerializeField] Color fogColor = new Color(0.010f, 0.008f, 0.006f);
    [SerializeField] float fogStartDistance = 3f;
    [SerializeField] float fogEndDistance = 16f;

    [Header("Ambient")]
    [Tooltip("Replaces the blue-ish skybox ambient with a dim warm base, so the ceiling panels do the actual lighting.")]
    [SerializeField] bool overrideAmbient = true;
    [SerializeField] Color ambientColor = new Color(0.015f, 0.012f, 0.008f);

    [Header("Camera")]
    [Tooltip("URP post-processing is off on the level camera. Turning it on is what makes the grading / grain / bloom visible.")]
    [SerializeField] bool enablePostProcessing = true;
    [SerializeField] bool enableAntiAliasing = true;

    [Header("Grade")]
    [SerializeField] float postExposure = -0.95f;
    [SerializeField] float contrast = 22f;
    [SerializeField] float saturation = -14f;
    [SerializeField] Color colourFilter = new Color(1f, 0.95f, 0.82f);
    [SerializeField] float temperature = 10f;
    [SerializeField] float tint = 3f;

    [Header("Lens")]
    [SerializeField] float bloomIntensity = 0.5f;
    [SerializeField] float bloomThreshold = 0.95f;
    [SerializeField] Color bloomTint = new Color(1f, 0.94f, 0.72f);
    [SerializeField] float vignetteIntensity = 0.55f;
    [SerializeField] float grainIntensity = 0.30f;
    [SerializeField] float chromaticAberration = 0.05f;

    Volume volume;

    void OnEnable()
    {
        GameSettings.OnQualityChanged += HandleQualityChanged;
        GameSettings.OnBrightnessChanged += HandleBrightnessChanged;
    }

    void OnDisable()
    {
        GameSettings.OnQualityChanged -= HandleQualityChanged;
        GameSettings.OnBrightnessChanged -= HandleBrightnessChanged;
    }

    void HandleQualityChanged(int q)
    {
        ApplyQualitySettings(q);
    }

    void HandleBrightnessChanged(float b)
    {
        ApplyBrightnessSettings(b);
    }

    void Awake()
    {
        ApplyFog();
        ApplyAmbient();
    }

    void Start()
    {
        ApplyCamera();
        BuildVolume();
        ApplyQualitySettings(GameSettings.QualityLevel);
    }

    void ApplyFog()
    {
        if (!enableFog) return;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogStartDistance = fogStartDistance;
        RenderSettings.fogEndDistance = Mathf.Max(fogStartDistance + 1f, fogEndDistance);
    }

    void ApplyAmbient()
    {
        if (!overrideAmbient) return;

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambientColor;
    }

    void ApplyCamera()
    {
        if (!enablePostProcessing && !enableAntiAliasing) return;

        Camera cam = Camera.main;
        if (cam == null) cam = FindAnyObjectByType<Camera>();
        if (cam == null)
        {
            Debug.LogWarning("BackroomsAtmosphere: no camera found, skipping the camera setup.");
            return;
        }

        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();

        if (enablePostProcessing)
        {
            data.renderPostProcessing = true;
            data.dithering = true;              // no banding in the fog gradient
        }

        if (enableAntiAliasing)
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
    }

    void BuildVolume()
    {
        if (!enablePostProcessing || volume != null) return;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Backrooms Atmosphere";

        // Filmic curve, neutral so the yellow casts stay yellow instead of
        // turning orange the way ACES does.
        Tonemapping tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.Neutral);

        ColorAdjustments color = profile.Add<ColorAdjustments>(true);
        // Fold in the menu's brightness slider: GameSettings applies it on scene
        // load, before this volume exists, so it has to be read in here too.
        float brightnessOffset = (GameSettings.Brightness - 0.5f) * 2f;
        color.postExposure.Override(postExposure + brightnessOffset);
        color.contrast.Override(contrast);
        color.saturation.Override(saturation);
        color.colorFilter.Override(colourFilter);

        WhiteBalance balance = profile.Add<WhiteBalance>(true);
        balance.temperature.Override(temperature);
        balance.tint.Override(tint);

        // Makes the ceiling panels read as light sources instead of white quads.
        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(bloomThreshold);
        bloom.intensity.Override(bloomIntensity);
        bloom.scatter.Override(0.65f);
        bloom.tint.Override(bloomTint);
        bloom.highQualityFiltering.Override(false);

        Vignette vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(vignetteIntensity);
        vignette.smoothness.Override(0.55f);
        vignette.color.Override(new Color(0.04f, 0.04f, 0.03f));

        // Cheap handheld-camera texture over the whole image.
        FilmGrain grain = profile.Add<FilmGrain>(true);
        grain.type.Override(FilmGrainLookup.Thin2);
        grain.intensity.Override(grainIntensity);
        grain.response.Override(0.8f);

        ChromaticAberration aberration = profile.Add<ChromaticAberration>(true);
        aberration.intensity.Override(chromaticAberration);

        volume = new GameObject("Backrooms Post FX").AddComponent<Volume>();
        volume.transform.SetParent(transform, false);
        volume.isGlobal = true;
        volume.priority = 100f;
        volume.weight = 1f;
        volume.profile = profile;

        ApplyBrightnessSettings(GameSettings.Brightness);
        ApplyQualitySettings(GameSettings.QualityLevel);
    }

    public void ApplyBrightnessSettings(float b)
    {
        if (volume != null && volume.profile != null)
        {
            if (volume.profile.TryGet<ColorAdjustments>(out ColorAdjustments color))
            {
                float brightnessOffset = (b - 0.5f) * 2.5f;
                color.postExposure.Override(postExposure + brightnessOffset);
            }
        }

        if (overrideAmbient)
        {
            float ambientMultiplier = Mathf.Lerp(0.35f, 2.4f, b);
            RenderSettings.ambientLight = ambientColor * ambientMultiplier;
        }
    }

    /// <summary>
    /// Throttles heavy post-processing effects based on the selected graphics tier:
    /// - Low / Performance: Skips procedural film grain, chromatic aberration, bloom, and FXAA.
    /// - Balanced: Keeps subtle bloom, skips heavy film grain and chromatic aberration.
    /// - High / PC: Full authentic atmosphere profile.
    /// </summary>
    void ApplyQualitySettings(int qualityIndex)
    {
        if (volume == null || volume.profile == null) return;
        VolumeProfile prof = volume.profile;

        bool isLow = qualityIndex == 0;
        bool isMedium = qualityIndex == 1;

        if (prof.TryGet(out Bloom bloom))
        {
            if (isLow) bloom.intensity.Override(0f);
            else if (isMedium) bloom.intensity.Override(bloomIntensity * 0.7f);
            else bloom.intensity.Override(bloomIntensity);
        }

        if (prof.TryGet(out FilmGrain grain))
        {
            if (isLow || isMedium) grain.intensity.Override(0f);
            else grain.intensity.Override(grainIntensity);
        }

        if (prof.TryGet(out ChromaticAberration aberration))
        {
            if (isLow) aberration.intensity.Override(0f);
            else aberration.intensity.Override(chromaticAberration);
        }

        Camera cam = Camera.main;
        if (cam == null) cam = FindAnyObjectByType<Camera>();
        if (cam != null)
        {
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.antialiasing = isLow ? AntialiasingMode.None : AntialiasingMode.FastApproximateAntialiasing;
            }
        }
    }
}
