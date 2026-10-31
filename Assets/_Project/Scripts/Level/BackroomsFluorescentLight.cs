using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authentic "Inside the Backrooms" fluorescent ceiling light fixture.
/// Features:
/// - Exact Inside the Backrooms sickly pale yellow-green color and soft attenuation.
/// - Synchronized emissive diffuser/tubes via MaterialPropertyBlock (no material cloning).
/// - 3D spatial 60Hz electromagnetic ballast hum with volume tracking light intensity.
/// - Multi-strike ignition spasm with synchronized electrical spark/click SFX.
/// - Micro-tremor electrical jitter for unsettling liminal realism.
/// - Dynamic distance culling to ensure ultra-smooth 60+ FPS on mobile.
/// - Monster proximity electromagnetic stuttering and Blackout event integration.
/// </summary>
[DisallowMultipleComponent]
public class BackroomsFluorescentLight : MonoBehaviour
{
    public static readonly List<BackroomsFluorescentLight> AllFixtures = new List<BackroomsFluorescentLight>();

    public enum FixtureStyle
    {
        Normal,
        Sputtering,
        Dead
    }

    [Header("Fixture State")]
    [SerializeField] private FixtureStyle style = FixtureStyle.Normal;

    [Header("Lighting Properties")]
    [SerializeField] private float baseIntensity = 1.9f;
    [SerializeField] private float lightRange = 8.0f;
    [SerializeField] private Color lightColor = new Color(1.0f, 0.94f, 0.76f);
    [SerializeField] private Color baseEmissionColor = new Color(2.6f, 2.45f, 1.9f);

    [Header("Audio (Ballast Hum & Sparks)")]
    [Range(0f, 1f)] [SerializeField] private float humBaseVolume = 0.32f;
    [SerializeField] private float humMinDistance = 1.5f;
    [SerializeField] private float humMaxDistance = 8.5f;

    [Header("Performance & Mobile Culling")]
    [Tooltip("Distance in meters beyond which the point light and 3D audio are disabled. Mesh remains glowing.")]
    [SerializeField] private float cullDistance = 24.0f;

    // Component bindings
    private Light pointLight;
    private MeshRenderer diffuserRenderer;
    private AudioSource humAudioSource;
    private AudioSource sparkAudioSource;

    private MaterialPropertyBlock propertyBlock;
    private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");

    // Shared procedural audio clips (generated once in memory)
    private static AudioClip sharedHumClip;
    private static AudioClip sharedSparkClip;

    // State tracking
    private float currentDimMultiplier = 1.0f;
    private float nextStrikeTime;
    private bool isStriking;
    private bool isBlackoutActive;
    private float cullDistanceSqr;
    private bool isCulled;
    private float nextCullCheckTime;

    private static Transform cachedPlayerTransform;
    private static Transform cachedMonsterTransform;
    private static float nextEntityCacheTime;

    public FixtureStyle Style => style;
    public Light PointLight => pointLight;
    public bool IsBlackoutActive => isBlackoutActive;

    public void Initialize(FixtureStyle fixtureStyle, MeshRenderer renderer, Color? customColor = null)
    {
        style = fixtureStyle;
        diffuserRenderer = renderer;
        if (customColor.HasValue) lightColor = customColor.Value;

        ConfigureComponents();
        ApplyState();
    }

    private void Awake()
    {
        cullDistanceSqr = cullDistance * cullDistance;
        propertyBlock = new MaterialPropertyBlock();

        EnsureProceduralAudioClips();
        ConfigureComponents();
    }

    private void OnEnable()
    {
        AllFixtures.Add(this);
    }

    private void OnDisable()
    {
        AllFixtures.Remove(this);
    }

    private void Start()
    {
        ScheduleNextStrike();
        ApplyState();
        UpdateCulling(force: true);
    }

    private void ConfigureComponents()
    {
        // 1. Point Light Setup
        if (pointLight == null)
        {
            pointLight = GetComponentInChildren<Light>();
            if (pointLight == null)
            {
                GameObject lightGo = new GameObject("Panel Light");
                lightGo.transform.SetParent(transform, false);
                lightGo.transform.localPosition = new Vector3(0f, -0.22f, 0f);
                pointLight = lightGo.AddComponent<Light>();
            }
        }

        pointLight.type = LightType.Point;
        pointLight.color = lightColor;
        pointLight.intensity = baseIntensity;
        pointLight.range = lightRange;
        pointLight.shadows = LightShadows.None;
        pointLight.bounceIntensity = 0f;

        // 2. Audio Sources Setup
        if (humAudioSource == null)
        {
            humAudioSource = gameObject.AddComponent<AudioSource>();
            humAudioSource.playOnAwake = false;
            humAudioSource.loop = true;
            humAudioSource.spatialBlend = 1.0f; // Full 3D
            humAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            humAudioSource.minDistance = humMinDistance;
            humAudioSource.maxDistance = humMaxDistance;
            humAudioSource.clip = sharedHumClip;
            humAudioSource.volume = humBaseVolume;
        }

        if (sparkAudioSource == null)
        {
            sparkAudioSource = gameObject.AddComponent<AudioSource>();
            sparkAudioSource.playOnAwake = false;
            sparkAudioSource.loop = false;
            sparkAudioSource.spatialBlend = 1.0f;
            sparkAudioSource.rolloffMode = AudioRolloffMode.Linear;
            sparkAudioSource.minDistance = 2.0f;
            sparkAudioSource.maxDistance = 14.0f;
            sparkAudioSource.clip = sharedSparkClip;
            sparkAudioSource.volume = 0.55f;
        }

        // 3. Diffuser Renderer binding
        if (diffuserRenderer == null)
        {
            diffuserRenderer = GetComponentInChildren<MeshRenderer>();
        }
    }

    private void ApplyState()
    {
        if (style == FixtureStyle.Dead)
        {
            currentDimMultiplier = 0f;
            if (pointLight != null) pointLight.enabled = false;
            if (humAudioSource != null && humAudioSource.isPlaying) humAudioSource.Stop();
            SetMeshEmission(0f);
        }
        else
        {
            currentDimMultiplier = 1f;
            if (pointLight != null)
            {
                pointLight.enabled = !isCulled && !isBlackoutActive;
                pointLight.intensity = baseIntensity;
                pointLight.color = lightColor;
                pointLight.range = lightRange;
            }

            if (humAudioSource != null && !isCulled && !isBlackoutActive && !humAudioSource.isPlaying)
            {
                humAudioSource.pitch = Random.Range(0.96f, 1.04f);
                humAudioSource.Play();
            }

            SetMeshEmission(1f);
        }
    }

    private void Update()
    {
        if (style == FixtureStyle.Dead) return;

        // Throttle player/monster reference caching
        if (Time.time >= nextEntityCacheTime)
        {
            nextEntityCacheTime = Time.time + 3.0f;
            CacheEntities();
        }

        // Throttle mobile distance culling check (10Hz)
        if (Time.time >= nextCullCheckTime)
        {
            nextCullCheckTime = Time.time + 0.12f;
            UpdateCulling();
        }

        if (isCulled || isBlackoutActive) return;

        // Normal operations
        if (style == FixtureStyle.Sputtering && !isStriking && Time.time >= nextStrikeTime)
        {
            StartCoroutine(BallastStrikeRoutine());
        }

        // Apply realistic micro-tremor (imperceptible 60Hz liminal electrical buzz)
        float jitter = (Mathf.PerlinNoise(Time.time * 28f, transform.position.x * 3.7f) - 0.5f) * 0.04f;
        float targetMultiplier = Mathf.Clamp01(currentDimMultiplier + jitter);

        // Check monster proximity electromagnetic disruption
        if (cachedMonsterTransform != null && cachedMonsterTransform.gameObject.activeInHierarchy)
        {
            float monsterDistSq = (cachedMonsterTransform.position - transform.position).sqrMagnitude;
            if (monsterDistSq < 196f) // within 14 meters
            {
                float proxDist = Mathf.Sqrt(monsterDistSq);
                float proxSeverity = 1f - (proxDist / 14f);
                float interference = Mathf.PerlinNoise(Time.time * 36f, transform.position.z * 5.1f);
                if (interference < proxSeverity * 0.65f)
                {
                    targetMultiplier *= Random.Range(0.08f, 0.45f);
                }
            }
        }

        if (pointLight != null)
        {
            pointLight.intensity = baseIntensity * targetMultiplier;
        }

        if (humAudioSource != null)
        {
            humAudioSource.volume = humBaseVolume * targetMultiplier;
        }

        SetMeshEmission(targetMultiplier);
    }

    private void UpdateCulling(bool force = false)
    {
        if (cachedPlayerTransform == null)
        {
            CacheEntities();
            if (cachedPlayerTransform == null) return;
        }

        float distSq = (cachedPlayerTransform.position - transform.position).sqrMagnitude;
        bool shouldCull = distSq > cullDistanceSqr;

        if (shouldCull != isCulled || force)
        {
            isCulled = shouldCull;

            if (pointLight != null)
            {
                pointLight.enabled = !isCulled && !isBlackoutActive && style != FixtureStyle.Dead;
            }

            if (humAudioSource != null)
            {
                if (isCulled || isBlackoutActive || style == FixtureStyle.Dead)
                {
                    if (humAudioSource.isPlaying) humAudioSource.Pause();
                }
                else
                {
                    if (!humAudioSource.isPlaying) humAudioSource.Play();
                }
            }
        }
    }

    private void SetMeshEmission(float intensityMultiplier)
    {
        if (diffuserRenderer == null) return;
        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

        diffuserRenderer.GetPropertyBlock(propertyBlock);
        Color targetEmission = baseEmissionColor * intensityMultiplier;
        propertyBlock.SetColor(EmissionColorProp, targetEmission);
        diffuserRenderer.SetPropertyBlock(propertyBlock);
    }

    private IEnumerator BallastStrikeRoutine()
    {
        isStriking = true;

        // Strike spasm: 3 to 7 violent micro-strikes mimicking failing fluorescent ballast
        int strikes = Random.Range(3, 7);
        for (int i = 0; i < strikes; i++)
        {
            // Rapid dip/off
            currentDimMultiplier = Random.Range(0.0f, 0.12f);
            if (sparkAudioSource != null && Random.value < 0.65f)
            {
                sparkAudioSource.pitch = Random.Range(0.85f, 1.25f);
                sparkAudioSource.PlayOneShot(sharedSparkClip, Random.Range(0.4f, 0.75f));
            }
            yield return new WaitForSeconds(Random.Range(0.02f, 0.05f));

            // Instant surge ignition
            currentDimMultiplier = Random.Range(0.85f, 1.35f);
            yield return new WaitForSeconds(Random.Range(0.03f, 0.08f));
        }

        // Momentary after-dip (dark lull before steady hum resumes)
        if (Random.value < 0.45f)
        {
            currentDimMultiplier = Random.Range(0.04f, 0.15f);
            yield return new WaitForSeconds(Random.Range(0.12f, 0.35f));
        }

        currentDimMultiplier = 1.0f;
        isStriking = false;
        ScheduleNextStrike();
    }

    private void ScheduleNextStrike()
    {
        if (style == FixtureStyle.Sputtering)
        {
            nextStrikeTime = Time.time + Random.Range(1.8f, 6.5f);
        }
        else
        {
            // Even normal fixtures have an extremely rare occasional hiccup (every 30-75s)
            nextStrikeTime = Time.time + Random.Range(30f, 75f);
        }
    }

    public void SetBlackout(bool active)
    {
        isBlackoutActive = active;

        if (isBlackoutActive)
        {
            if (pointLight != null) pointLight.enabled = false;
            if (humAudioSource != null && humAudioSource.isPlaying) humAudioSource.Pause();
            SetMeshEmission(0f);
        }
        else
        {
            ApplyState();
            UpdateCulling(force: true);
        }
    }

    private static void CacheEntities()
    {
        if (cachedPlayerTransform == null)
        {
            try
            {
                var playerGo = GameObject.FindWithTag("Player");
                if (playerGo != null) cachedPlayerTransform = playerGo.transform;
            }
            catch (System.Exception) { }

            if (cachedPlayerTransform == null && Camera.main != null)
            {
                cachedPlayerTransform = Camera.main.transform;
            }
        }

        if (cachedMonsterTransform == null)
        {
            if (VillainAI.Instance != null)
            {
                cachedMonsterTransform = VillainAI.Instance.transform;
            }
            else if (AgathaAI.Instance != null)
            {
                cachedMonsterTransform = AgathaAI.Instance.transform;
            }
            else
            {
                try
                {
                    var monsterGo = GameObject.FindWithTag("Enemy");
                    if (monsterGo != null) cachedMonsterTransform = monsterGo.transform;
                }
                catch (System.Exception) { }

                if (cachedMonsterTransform == null)
                {
                    var fallback = GameObject.Find("Bacteria") ?? GameObject.Find("Agatha_Stalker");
                    if (fallback != null) cachedMonsterTransform = fallback.transform;
                }
            }
        }
    }

    /// <summary>
    /// Procedurally synthesizes authentic 60Hz electromagnetic fluorescent ballast hum & electrical spark sound.
    /// Runs once at startup, creating perfectly looping audio in memory with 0 bytes of external asset baggage!
    /// </summary>
    private static void EnsureProceduralAudioClips()
    {
        if (sharedHumClip != null && sharedSparkClip != null) return;

        const int sampleRate = 22050; // Optimized sample rate for mobile memory

        // 1. Synthesize 60Hz + 120Hz + 180Hz Fluorescent Ballast Hum (1 second loop)
        if (sharedHumClip == null)
        {
            int humSamples = sampleRate; // 1 second
            float[] humData = new float[humSamples];
            const float f1 = 60.0f;
            const float f2 = 120.0f;
            const float f3 = 180.0f;
            const float f4 = 360.0f;

            for (int i = 0; i < humSamples; i++)
            {
                float t = (float)i / sampleRate;
                // Rich harmonic distortion of AC mains 60Hz transformer
                float s1 = Mathf.Sin(2f * Mathf.PI * f1 * t) * 0.45f;
                float s2 = Mathf.Sin(2f * Mathf.PI * f2 * t) * 0.35f;
                float s3 = Mathf.Sin(2f * Mathf.PI * f3 * t) * 0.15f;
                float s4 = Mathf.Sin(2f * Mathf.PI * f4 * t) * 0.05f;

                // Subtle uneven buzz saturation
                float mixed = s1 + s2 + s3 + s4;
                humData[i] = Mathf.Clamp(mixed * 1.15f, -1.0f, 1.0f);
            }

            sharedHumClip = AudioClip.Create("Fluorescent_Ballast_60Hz_Hum", humSamples, 1, sampleRate, false);
            sharedHumClip.SetData(humData, 0);
        }

        // 2. Synthesize Electrical Ballast Spark / Click (0.05s pop)
        if (sharedSparkClip == null)
        {
            int sparkSamples = (int)(sampleRate * 0.05f);
            float[] sparkData = new float[sparkSamples];

            for (int i = 0; i < sparkSamples; i++)
            {
                float progress = (float)i / sparkSamples;
                float decay = Mathf.Exp(-progress * 22f);
                float noise = (Random.value * 2f - 1f) * decay;
                float pop = Mathf.Sin(progress * Mathf.PI * 8f) * decay * 0.7f;
                sparkData[i] = Mathf.Clamp(noise + pop, -1f, 1f);
            }

            sharedSparkClip = AudioClip.Create("Fluorescent_Spark_Snap", sparkSamples, 1, sampleRate, false);
            sharedSparkClip.SetData(sparkData, 0);
        }
    }
}
