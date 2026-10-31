using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Controls the Day/Night cycle for Backrooms Level 94 ("Motion"):
/// - Daytime duration: 1 minute (60 seconds).
/// - Nighttime duration: 2 minutes (120 seconds).
/// - Rare paranormal event: when the character blinks during daylight, a rare roll can instantly plunge
///   the world into night while the eyelids are sealed shut, so opening the eyes reveals a pitch-dark,
///   foggy nocturnal horror environment with torchlight.
/// - During night, blinking does NOT trigger any random night events.
/// - Dense claustrophobic nocturnal fog (8m to 45m).
/// - Character torchlight (Flashlight like Level 0) automatically activates at night so player can survive.
/// - Stalker entities (Agatha) spawn and stalk during the night.
/// </summary>
public class Level94DayNightCycle : MonoBehaviour
{
    public static Level94DayNightCycle Instance { get; private set; }

    public static event System.Action OnNightStarted;
    public static event System.Action OnNightEnded;
    public static bool IsNightActive { get; private set; } = false;

    public enum Phase
    {
        Day,
        DuskTransition,
        Night,
        DawnTransition
    }

    [Header("Phase Timing")]
    [Tooltip("Day duration in seconds (default 60s = 1 min).")]
    public float dayDuration = 60f;
    [Tooltip("Night duration in seconds (default 60s = 1 min).")]
    public float nightDuration = 60f;
    [Tooltip("Smooth transition duration between day and night in seconds.")]
    public float transitionDuration = 6f;

    // Backward compatibility properties
    public float cycleDuration
    {
        get => dayDuration + nightDuration;
        set { dayDuration = value * 0.5f; nightDuration = value * 0.5f; }
    }
    public float timeOfDay
    {
        get => currentPhase == Phase.Night ? 0.75f : 0.25f;
        set { }
    }

    [Header("Current Phase State")]
    public Phase currentPhase = Phase.Day;
    public float currentPhaseTimer = 0f;
    public bool pauseCycle = false;

    [Header("Blink Rare Night Event")]
    [Tooltip("Whether blinking can rarely trigger an instant night transition.")]
    public bool enableBlinkNightTrigger = true;
    [Tooltip("Chance (0.0 to 1.0) that a blink during daylight instantly triggers night. Rare default: 8%.")]
    [Range(0.01f, 0.50f)]
    public float rareBlinkNightChance = 0.08f;

    [Header("Sun & Moon Directional Light")]
    public Light sunLight;
    public Color daySunColor = new Color(1.0f, 0.96f, 0.90f);
    public Color sunsetSunColor = new Color(1.0f, 0.48f, 0.20f);
    public Color nightMoonColor = Color.black;
    public float daySunIntensity = 1.35f;
    public float nightMoonIntensity = 0.0f; // Absolute pitch black; torchlight required to see anything

    [Header("Liminal Skybox")]
    public Material skyboxMaterial;
    public Color daySkyTint = new Color(0.20f, 0.55f, 0.95f);
    public Color duskSkyTint = new Color(0.85f, 0.40f, 0.20f);
    public Color nightSkyTint = Color.black;

    public Color dayGroundColor = new Color(0.60f, 0.75f, 0.92f);
    public Color duskGroundColor = new Color(0.35f, 0.18f, 0.12f);
    public Color nightGroundColor = Color.black;

    [Header("Dynamic Fog (Nocturnal Foggy Environment)")]
    public Color dayFogColor = new Color(0.68f, 0.82f, 0.98f);
    public Color duskFogColor = new Color(0.55f, 0.30f, 0.20f);
    public Color nightFogColor = Color.black; // Terrifying pitch-black void
    public float dayFogStart = 120f;
    public float dayFogEnd = 650f;
    public float nightFogStart = 0.5f; // Hugs the player immediately
    public float nightFogEnd = 8.0f;   // Total pitch blackness past 8 meters

    [Header("Horror Night Atmosphere Audio & Heartbeat")]
    public bool enableNightHorrorAudio = true;
    public AudioClip nightEntityClip;
    [Range(0f, 1f)] public float nightAudioVolume = 0.75f;
    public bool enableNightHeartbeat = true;
    private AudioSource nightAudioSource;
    private AudioSource dayMusicAudioSource;
    private AudioSource heartbeatAudioSource;
    private AudioClip proceduralHeartbeatClip;
    private float heartbeatTimer = 0f;

    [Header("URP Horror Post-Processing")]
    private Volume horrorVolume;
    private Vignette vignetteEffect;
    private FilmGrain filmGrainEffect;
    private ColorAdjustments colorAdjustmentsEffect;
    private ChromaticAberration chromaticAberrationEffect;

    [Header("Agatha Night Stalker Integration")]
    [Tooltip("Direct reference to the Agatha scary stalker instance in the scene.")]
    public GameObject agathaInstance;
    public AgathaAI agathaAI;

    [Header("Night Lights")]
    [Tooltip("Activate house and streetlight lights during darkness.")]
    public bool manageNightLights = true;

    [Header("Player Torchlight Integration")]
    [Tooltip("Automatically turn on player's flashlight when night falls.")]
    public bool autoActivateFlashlightAtNight = true;

    private Light[] cachedStreetlights;
    private bool lightsAreOn = false;
    private Flashlight cachedPlayerFlashlight;

    private void Awake()
    {
        Instance = this;

        if (sunLight == null)
        {
            Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional)
                {
                    sunLight = l;
                    break;
                }
            }
        }

        if (skyboxMaterial == null && RenderSettings.skybox != null)
        {
            skyboxMaterial = RenderSettings.skybox;
        }

        CacheNightLights();
        SetupNightAudio();
    }

    private void OnEnable()
    {
        PlayerExhaustionBlinkEffect.OnBlinkPeak += HandleBlinkPeak;
    }

    private void OnDisable()
    {
        PlayerExhaustionBlinkEffect.OnBlinkPeak -= HandleBlinkPeak;
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        var atmos = GetComponent<Level94Atmosphere>();
        if (atmos != null) dayMusicAudioSource = atmos.GetComponent<AudioSource>();
        if (dayMusicAudioSource == null) dayMusicAudioSource = GetComponent<AudioSource>();

        SetupNightAudio();
        SetupHeartbeatAudio();
        SetupHorrorVolume();
        CacheNightLights();
        EnsureAgathaReference();

        // Start in Daytime
        currentPhase = Phase.Day;
        currentPhaseTimer = 0f;
        ApplyPhaseBlend(0f); // 0 = full Day, 1 = full Night

        // Ensure Agatha is initial-despawned for daytime
        DeactivateNightHorrorAndAgatha();
    }

    private void SetupNightAudio()
    {
        if (nightAudioSource == null)
        {
            nightAudioSource = gameObject.AddComponent<AudioSource>();
            nightAudioSource.loop = true;
            nightAudioSource.spatialBlend = 0f;
            nightAudioSource.playOnAwake = false;
        }

        if (nightEntityClip == null)
        {
            nightEntityClip = Resources.Load<AudioClip>("SFX/juanjo_ sound - song of entities");
            if (nightEntityClip == null)
                nightEntityClip = Resources.Load<AudioClip>("SFX/juanjo_ sound - signal lost");
        }

        if (nightEntityClip != null)
        {
            nightAudioSource.clip = nightEntityClip;
            nightAudioSource.volume = 0f;
        }
    }

    private void SetupHeartbeatAudio()
    {
        if (heartbeatAudioSource == null)
        {
            heartbeatAudioSource = gameObject.AddComponent<AudioSource>();
            heartbeatAudioSource.loop = false;
            heartbeatAudioSource.spatialBlend = 0f;
            heartbeatAudioSource.playOnAwake = false;
        }

        if (proceduralHeartbeatClip == null)
        {
            proceduralHeartbeatClip = GenerateProceduralHeartbeatClip();
        }
    }

    /// <summary>
    /// Procedurally synthesizes a visceral deep sub-bass heartbeat thump ("lub-dub").
    /// </summary>
    private AudioClip GenerateProceduralHeartbeatClip()
    {
        int sampleRate = 44100;
        float duration = 0.55f;
        int totalSamples = Mathf.FloorToInt(sampleRate * duration);
        float[] samples = new float[totalSamples];

        for (int i = 0; i < totalSamples; i++)
        {
            float t = (float)i / sampleRate;
            float thump1 = 0f;
            float thump2 = 0f;

            // First beat ("lub") at t = 0.0s
            if (t < 0.22f)
            {
                float env = Mathf.Exp(-t * 22f);
                thump1 = Mathf.Sin(2f * Mathf.PI * 52f * t) * env * 0.9f;
            }

            // Second beat ("dub") at t = 0.16s
            if (t >= 0.16f && t < 0.45f)
            {
                float t2 = t - 0.16f;
                float env2 = Mathf.Exp(-t2 * 20f);
                thump2 = Mathf.Sin(2f * Mathf.PI * 46f * t2) * env2 * 0.75f;
            }

            samples[i] = Mathf.Clamp(thump1 + thump2, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create("ProceduralNightHeartbeat", totalSamples, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void SetupHorrorVolume()
    {
        if (horrorVolume == null)
        {
            horrorVolume = GetComponent<Volume>();
            if (horrorVolume == null)
            {
                horrorVolume = gameObject.AddComponent<Volume>();
            }
            horrorVolume.isGlobal = true;
            horrorVolume.priority = 100f;
            horrorVolume.weight = 0f;

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "NightHorrorProfile";

            // 1. Heavy claustrophobic vignette
            if (!profile.TryGet(out vignetteEffect))
            {
                vignetteEffect = profile.Add<Vignette>(true);
            }
            vignetteEffect.active = true;
            vignetteEffect.color.Override(Color.black);
            vignetteEffect.intensity.Override(0.65f);
            vignetteEffect.smoothness.Override(0.48f);

            // 2. Gritty VHS / found-footage film grain
            if (!profile.TryGet(out filmGrainEffect))
            {
                filmGrainEffect = profile.Add<FilmGrain>(true);
            }
            filmGrainEffect.active = true;
            filmGrainEffect.type.Override(FilmGrainLookup.Medium1);
            filmGrainEffect.intensity.Override(0.50f);
            filmGrainEffect.response.Override(0.85f);

            // 3. Bleak desaturation and deep contrast
            if (!profile.TryGet(out colorAdjustmentsEffect))
            {
                colorAdjustmentsEffect = profile.Add<ColorAdjustments>(true);
            }
            colorAdjustmentsEffect.active = true;
            colorAdjustmentsEffect.saturation.Override(-50f); // Drained, colorless dread
            colorAdjustmentsEffect.contrast.Override(40f);    // Stark harsh shadows
            colorAdjustmentsEffect.postExposure.Override(-2.5f); // Crushes any gray ambient into pure pitch black!

            // 4. Subtle lens aberration
            if (!profile.TryGet(out chromaticAberrationEffect))
            {
                chromaticAberrationEffect = profile.Add<ChromaticAberration>(true);
            }
            chromaticAberrationEffect.active = true;
            chromaticAberrationEffect.intensity.Override(0.35f);

            horrorVolume.profile = profile;
        }
    }

    public void CacheNightLights()
    {
        cachedStreetlights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
    }

    public void EnsureAgathaReference()
    {
        if (agathaAI == null)
        {
            agathaAI = FindAnyObjectByType<AgathaAI>(FindObjectsInactive.Include);
        }
        if (agathaInstance == null && agathaAI != null)
        {
            agathaInstance = agathaAI.gameObject;
        }
        if (agathaInstance == null)
        {
            // Search all scene transforms even if disabled in hierarchy
            Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (var t in all)
            {
                if (t.name == "Agatha_Stalker" && t.hideFlags == HideFlags.None)
                {
                    agathaInstance = t.gameObject;
                    agathaAI = agathaInstance.GetComponent<AgathaAI>();
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Fired when character eyelids are completely sealed shut in darkness.
    /// If currently DAY, rolls a rare chance to instantly convert the environment to NIGHT!
    /// If currently NIGHT, blinking does NOT trigger any random night events.
    /// </summary>
    private void HandleBlinkPeak()
    {
        // "if character blinks during night no random night event happens"
        if (currentPhase == Phase.Night || currentPhase == Phase.DuskTransition)
        {
            return;
        }

        if (!enableBlinkNightTrigger) return;

        // "the random event of night happens very rarely"
        if (Random.value < rareBlinkNightChance)
        {
            Debug.Log("<color=#FF4444><b>[Blink Event]</b></color> Character blinked in daytime: The world has suddenly turned into NIGHT!");
            TriggerInstantNight();
        }
    }

    /// <summary>
    /// Instantly switches the world to night (used when character blinks).
    /// </summary>
    public void TriggerInstantNight()
    {
        currentPhase = Phase.Night;
        currentPhaseTimer = 0f;
        IsNightActive = true;

        ApplyPhaseBlend(1.0f); // Full night atmosphere immediately
        ToggleNightLights(true);

        EnsurePlayerTorchlight(true);
        ActivateNightHorrorAndAgatha();

        OnNightStarted?.Invoke();
    }

    public void ActivateNightHorrorAndAgatha()
    {
        EnsureAgathaReference();
        if (agathaInstance != null)
        {
            agathaInstance.SetActive(true);
        }
        if (agathaAI != null)
        {
            agathaAI.SpawnForNight();
        }
    }

    public void DeactivateNightHorrorAndAgatha()
    {
        EnsureAgathaReference();
        if (agathaAI != null)
        {
            agathaAI.DespawnForDay();
        }
        if (agathaInstance != null)
        {
            agathaInstance.SetActive(false);
        }
    }

    private void Update()
    {
        if (pauseCycle) return;

        currentPhaseTimer += Time.deltaTime;

        switch (currentPhase)
        {
            case Phase.Day:
                ApplyPhaseBlend(0f);
                if (currentPhaseTimer >= dayDuration)
                {
                    currentPhase = Phase.DuskTransition;
                    currentPhaseTimer = 0f;
                }
                break;

            case Phase.DuskTransition:
                float duskT = Mathf.Clamp01(currentPhaseTimer / Mathf.Max(0.1f, transitionDuration));
                ApplyPhaseBlend(duskT);
                if (currentPhaseTimer >= transitionDuration)
                {
                    currentPhase = Phase.Night;
                    currentPhaseTimer = 0f;
                    IsNightActive = true;
                    EnsurePlayerTorchlight(true);
                    ActivateNightHorrorAndAgatha();
                    OnNightStarted?.Invoke();
                }
                break;

            case Phase.Night:
                ApplyPhaseBlend(1.0f);
                UpdateHeartbeat();
                if (currentPhaseTimer >= nightDuration)
                {
                    currentPhase = Phase.DawnTransition;
                    currentPhaseTimer = 0f;
                }
                break;

            case Phase.DawnTransition:
                float dawnT = 1.0f - Mathf.Clamp01(currentPhaseTimer / Mathf.Max(0.1f, transitionDuration));
                ApplyPhaseBlend(dawnT);
                if (currentPhaseTimer >= transitionDuration)
                {
                    currentPhase = Phase.Day;
                    currentPhaseTimer = 0f;
                    IsNightActive = false;
                    DeactivateNightHorrorAndAgatha();
                    OnNightEnded?.Invoke();
                }
                break;
        }
    }

    private void UpdateHeartbeat()
    {
        if (!enableNightHeartbeat || heartbeatAudioSource == null || proceduralHeartbeatClip == null) return;

        // Accelerate heartbeat if Agatha is close or chasing
        float beatInterval = 1.15f; // ~52 BPM baseline dread
        float volume = 0.55f;

        if (agathaAI != null && agathaInstance != null && agathaInstance.activeInHierarchy)
        {
            if (agathaAI.currentState == AgathaAI.AgathaState.ChasingPlayer)
            {
                beatInterval = 0.50f; // Rapid panic 120 BPM
                volume = 0.85f;
            }
            else
            {
                float dist = Vector3.Distance(transform.position, agathaInstance.transform.position);
                if (dist < 30f)
                {
                    beatInterval = Mathf.Lerp(0.55f, 1.0f, dist / 30f);
                    volume = Mathf.Lerp(0.80f, 0.55f, dist / 30f);
                }
            }
        }

        heartbeatTimer += Time.deltaTime;
        if (heartbeatTimer >= beatInterval)
        {
            heartbeatTimer = 0f;
            heartbeatAudioSource.PlayOneShot(proceduralHeartbeatClip, volume * GameSettings.MasterVolume);
        }
    }

    /// <summary>
    /// Blend factor: 0.0 = bright liminal Day, 1.0 = deep pitch-black foggy Night.
    /// </summary>
    public void ApplyPhaseBlend(float nightBlend)
    {
        float dayFactor = 1.0f - nightBlend;
        float nightFactor = nightBlend;
        float duskFactor = Mathf.Clamp01(1.0f - Mathf.Abs(nightBlend - 0.5f) * 2.5f);

        // 0. URP Horror Post-Processing Volume weight
        if (horrorVolume != null)
        {
            horrorVolume.weight = nightFactor;
        }

        // 1. Sun & Moon Direction and Intensity
        float sunAngleX = Mathf.Lerp(45f, 225f, nightBlend); // 45 deg midday -> 225 deg midnight
        float sunAngleY = -38f;
        if (sunLight != null)
        {
            sunLight.transform.rotation = Quaternion.Euler(sunAngleX, sunAngleY, 0f);

            if (nightBlend > 0.45f)
            {
                sunLight.color = nightMoonColor;
                sunLight.intensity = Mathf.Lerp(0.01f, nightMoonIntensity, nightFactor);
                sunLight.shadows = LightShadows.Soft;
            }
            else
            {
                Color sunC = Color.Lerp(daySunColor, sunsetSunColor, duskFactor);
                sunLight.color = sunC;
                sunLight.intensity = Mathf.Lerp(0.05f, daySunIntensity, dayFactor);
                sunLight.shadows = LightShadows.Soft;
            }
        }

        // 2. Ambient Trilight Darkness - Pitch Black at Night
        Color ambientSky = Color.Lerp(Color.black, new Color(0.70f, 0.84f, 0.98f), dayFactor);
        Color ambientEquator = Color.Lerp(Color.black, new Color(0.82f, 0.86f, 0.92f), dayFactor);
        Color ambientGround = Color.Lerp(Color.black, new Color(0.40f, 0.46f, 0.52f), dayFactor);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
        RenderSettings.ambientIntensity = Mathf.Lerp(0f, 1.0f, dayFactor);

        // 3. Dense Claustrophobic Pitch-Black Fog at Night
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        Color targetFog = Color.Lerp(Color.black, dayFogColor, dayFactor);
        if (duskFactor > 0.05f) targetFog = Color.Lerp(targetFog, duskFogColor, duskFactor * 0.7f);
        RenderSettings.fogColor = targetFog;

        RenderSettings.fogStartDistance = Mathf.Lerp(nightFogStart, dayFogStart, dayFactor);
        RenderSettings.fogEndDistance = Mathf.Lerp(nightFogEnd, dayFogEnd, dayFactor);

        // 4. Liminal Skybox Tint
        if (skyboxMaterial != null)
        {
            Color curTop = Color.Lerp(nightSkyTint, daySkyTint, dayFactor);
            if (duskFactor > 0.05f) curTop = Color.Lerp(curTop, duskSkyTint, duskFactor * 0.8f);

            Color curHorizon = Color.Lerp(nightFogColor, dayFogColor, dayFactor);
            if (duskFactor > 0.05f) curHorizon = Color.Lerp(curHorizon, duskFogColor, duskFactor * 0.7f);

            Color curBottom = Color.Lerp(nightGroundColor, dayGroundColor, dayFactor);

            if (skyboxMaterial.HasProperty("_TopColor")) skyboxMaterial.SetColor("_TopColor", curTop);
            if (skyboxMaterial.HasProperty("_HorizonColor")) skyboxMaterial.SetColor("_HorizonColor", curHorizon);
            if (skyboxMaterial.HasProperty("_BottomColor")) skyboxMaterial.SetColor("_BottomColor", curBottom);

            if (skyboxMaterial.HasProperty("_SkyTint")) skyboxMaterial.SetColor("_SkyTint", curTop);
            if (skyboxMaterial.HasProperty("_GroundColor")) skyboxMaterial.SetColor("_GroundColor", curBottom);
            if (skyboxMaterial.HasProperty("_Exposure")) skyboxMaterial.SetFloat("_Exposure", Mathf.Lerp(0.0f, 1.20f, dayFactor));
        }

        // 5. Horror Audio
        if (dayMusicAudioSource != null)
        {
            dayMusicAudioSource.pitch = Mathf.Lerp(0.70f, 1.0f, dayFactor);
            dayMusicAudioSource.volume = Mathf.Lerp(0.12f, 0.60f, dayFactor) * GameSettings.MusicVolume;
        }

        if (nightAudioSource != null && enableNightHorrorAudio)
        {
            float targetNightVol = Mathf.Lerp(0f, nightAudioVolume, nightFactor) * GameSettings.MasterVolume;
            nightAudioSource.volume = targetNightVol;
            if (targetNightVol > 0.01f && !nightAudioSource.isPlaying)
            {
                nightAudioSource.Play();
            }
            else if (targetNightVol <= 0.005f && nightAudioSource.isPlaying)
            {
                nightAudioSource.Stop();
            }
        }

        // 6. Streetlights & Windows
        if (manageNightLights)
        {
            bool shouldLightsBeOn = nightBlend > 0.35f;
            if (shouldLightsBeOn != lightsAreOn)
            {
                lightsAreOn = shouldLightsBeOn;
                ToggleNightLights(lightsAreOn);
            }
        }
    }

    private void ToggleNightLights(bool on)
    {
        if (cachedStreetlights == null) return;
        foreach (var l in cachedStreetlights)
        {
            if (l == null || l.type == LightType.Directional) continue;
            l.enabled = on;
        }
    }

    /// <summary>
    /// Equips and turns on player's torchlight like Level 0 when night arrives.
    /// </summary>
    public void EnsurePlayerTorchlight(bool turnOn = true)
    {
        if (cachedPlayerFlashlight == null)
        {
            cachedPlayerFlashlight = FindAnyObjectByType<Flashlight>();
            if (cachedPlayerFlashlight == null)
            {
                GameObject player = GameObject.FindWithTag("Player") ?? GameObject.Find("PlayerCapsule");
                if (player != null)
                {
                    Transform camRoot = player.transform.Find("PlayerCameraRoot") ?? player.transform;
                    cachedPlayerFlashlight = camRoot.GetComponentInChildren<Flashlight>();
                    if (cachedPlayerFlashlight == null)
                    {
                        GameObject flGo = new GameObject("Flashlight");
                        flGo.transform.SetParent(camRoot, false);
                        flGo.transform.localPosition = new Vector3(0.18f, -0.16f, 0.20f);
                        cachedPlayerFlashlight = flGo.AddComponent<Flashlight>();
                    }
                }
            }
        }

        if (cachedPlayerFlashlight != null && autoActivateFlashlightAtNight)
        {
            if (turnOn && !cachedPlayerFlashlight.IsOn)
            {
                cachedPlayerFlashlight.SetState(true, playAudio: true);
            }
        }
    }
}
