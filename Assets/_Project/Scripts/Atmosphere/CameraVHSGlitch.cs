using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Authentic analog VHS tape glitch effect for the Backrooms found-footage aesthetic.
/// Dynamically monitors proximity and sightline to the entity (Clark).
/// When the entity is near or stalking the player, scanlines, analog tracking bars,
/// static snow, chromatic aberration, tape hiss, and camera jitter ramp up dramatically.
/// </summary>
public class CameraVHSGlitch : MonoBehaviour
{
    [Header("Target References")]
    public Transform player;
    public Transform villain;

    [Header("Distance Tuning")]
    [Tooltip("Whether the screen glitches as the villain approaches the player. Set to false to disable proximity glitching.")]
    public bool enableProximityGlitch = false;
    [Tooltip("Distance at which subtle glitching begins (metres).")]
    public float maxGlitchDistance = 16f;
    [Tooltip("Distance at which heavy screen tearing and static occurs (metres).")]
    public float intenseGlitchDistance = 4.5f;
    [Tooltip("Glitch boost applied when the monster is actively chasing or has direct sight.")]
    [Range(0f, 0.5f)] public float chaseGlitchBoost = 0.15f;

    [Header("VHS Visual Features")]
    public bool enableOSD = true;
    public bool enableChromaticAberration = true;
    public bool enableStaticAudio = true;
    [Range(0.1f, 1f)] public float maxAudioVolume = 0.35f;

    // Runtime state
    float currentGlitch;
    float targetGlitch;
    float burstTimer;
    float burstIntensity;

    // UI References
    Canvas vhsCanvas;
    Image glitchOverlay;
    Material glitchMaterial;
    Text recText;
    Text spText;
    Text playText;
    Text dateText;
    RectTransform osdContainer;

    // Post-processing references
    Volume atmosphereVolume;
    ChromaticAberration chromaticAberration;
    FilmGrain filmGrain;
    Vignette vignette;

    float baseChromatic = 0.05f;
    float baseGrain = 0.35f;
    float baseVignette = 0.36f;

    // Audio
    AudioSource staticAudio;

    public static CameraVHSGlitch Instance { get; private set; }

    void Awake()
    {
        Instance = this;
        FindTargets();
        SetupVHSOverlay();
        SetupStaticAudio();
    }

    void OnEnable()
    {
        GameSettings.OnQualityChanged += HandleQualityChanged;
        ApplyQualitySettings(GameSettings.QualityLevel);
    }

    void OnDisable()
    {
        GameSettings.OnQualityChanged -= HandleQualityChanged;
    }

    void HandleQualityChanged(int q)
    {
        ApplyQualitySettings(q);
    }

    void ApplyQualitySettings(int qualityIndex)
    {
        bool isLow = qualityIndex == 0;
        if (glitchOverlay != null)
        {
            glitchOverlay.gameObject.SetActive(!isLow);
        }
    }

    /// <summary>
    /// Spikes maximum glitch, chromatic aberration and static for jumpscare sequences.
    /// </summary>
    public static void TriggerKillBurst()
    {
        if (Instance != null)
        {
            Instance.currentGlitch = 1f;
            Instance.targetGlitch = 1f;
            Instance.burstIntensity = 0.5f;
        }
    }

    void Start()
    {
        FindPostProcessVolume();
    }

    void OnDestroy()
    {
        if (glitchMaterial != null) Destroy(glitchMaterial);
        if (vhsCanvas != null) Destroy(vhsCanvas.gameObject);
    }

    void FindTargets()
    {
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
            else
            {
                var cc = FindAnyObjectByType<CharacterController>();
                if (cc != null) player = cc.transform;
            }
        }

        if (villain == null)
        {
            VillainAI ai = FindAnyObjectByType<VillainAI>();
            if (ai != null) villain = ai.transform;
        }
    }

    void FindPostProcessVolume()
    {
        Volume[] volumes = FindObjectsByType<Volume>();
        foreach (Volume v in volumes)
        {
            if (v.profile == null) continue;
            if (v.profile.TryGet(out chromaticAberration))
            {
                baseChromatic = chromaticAberration.intensity.value;
            }
            if (v.profile.TryGet(out filmGrain))
            {
                baseGrain = filmGrain.intensity.value;
            }
            if (v.profile.TryGet(out vignette))
            {
                baseVignette = vignette.intensity.value;
            }

            if (chromaticAberration != null || filmGrain != null)
            {
                atmosphereVolume = v;
                break;
            }
        }
    }

    void SetupVHSOverlay()
    {
        GameObject canvasObj = new GameObject("VHS_Glitch_Canvas");
        canvasObj.transform.SetParent(transform, false);

        vhsCanvas = canvasObj.AddComponent<Canvas>();
        vhsCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        vhsCanvas.sortingOrder = 999; // Render above 3D world, below interactive UI buttons

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Glitch Image
        GameObject overlayObj = new GameObject("GlitchOverlay");
        overlayObj.transform.SetParent(canvasObj.transform, false);

        glitchOverlay = overlayObj.AddComponent<Image>();
        glitchOverlay.raycastTarget = false; // Never block mobile touches
        glitchOverlay.color = Color.clear;   // Safety: transparent by default, NEVER solid white

        RectTransform rt = glitchOverlay.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        // Try loading from Resources material first, then Shader.Find
        Material resourceMat = Resources.Load<Material>("M_VHSGlitch");
        if (resourceMat != null)
        {
            glitchMaterial = new Material(resourceMat);
        }
        else
        {
            Shader shader = Shader.Find("Backrooms/VHSGlitch");
            if (shader != null && shader.isSupported)
            {
                glitchMaterial = new Material(shader);
            }
        }

        if (glitchMaterial != null)
        {
            glitchOverlay.material = glitchMaterial;
            glitchOverlay.color = Color.white;
        }
        else
        {
            Debug.LogWarning("CameraVHSGlitch: Glitch material/shader unavailable. Disabling glitch overlay to avoid obscuring the view.");
            Destroy(overlayObj);
            glitchOverlay = null;
        }

        if (enableOSD)
        {
            BuildOSD(canvasObj.transform);
        }
    }

    void BuildOSD(Transform parent)
    {
        GameObject osdObj = new GameObject("VHS_OSD");
        osdObj.transform.SetParent(parent, false);

        osdContainer = osdObj.AddComponent<RectTransform>();
        osdContainer.anchorMin = Vector2.zero;
        osdContainer.anchorMax = Vector2.one;
        osdContainer.sizeDelta = Vector2.zero;

        Font font = Resources.Load<Font>("Fonts/RobotoMono-Regular");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        Color osdColor = new Color(0.95f, 0.95f, 0.90f, 0.85f);

        recText = CreateOSDLabel("REC_Label", osdContainer, font, osdColor, 28, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -40f), new Vector2(300f, 60f));

        spText = CreateOSDLabel("SP_Label", osdContainer, font, osdColor, 26, TextAnchor.UpperRight,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-120f, -40f), new Vector2(300f, 60f));
        spText.text = "SP  [|||]";

        playText = CreateOSDLabel("PLAY_Label", osdContainer, font, osdColor, 26, TextAnchor.LowerLeft,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(50f, 40f), new Vector2(400f, 60f));

        dateText = CreateOSDLabel("DATE_Label", osdContainer, font, osdColor, 26, TextAnchor.LowerRight,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-50f, 40f), new Vector2(400f, 60f));
        dateText.text = "OCT. 22 1996";
    }

    Text CreateOSDLabel(string name, Transform parent, Font font, Color color, int size, TextAnchor anchor,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 dimensions)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        Text text = obj.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;

        RectTransform rt = text.rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = anchorMin;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = dimensions;

        // Subtle drop shadow for CRT readability
        Shadow shadow = obj.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
        shadow.effectDistance = new Vector2(2f, -2f);

        return text;
    }

    void SetupStaticAudio()
    {
        if (!enableStaticAudio) return;

        staticAudio = gameObject.AddComponent<AudioSource>();
        staticAudio.loop = true;
        staticAudio.playOnAwake = false;
        staticAudio.spatialBlend = 0f; // 2D binaural static
        staticAudio.volume = 0f;

        // Procedural tape hiss / static crackle
        int sampleRate = 44100;
        int length = sampleRate * 2; // 2 second seamless loop
        float[] samples = new float[length];
        System.Random rng = new System.Random(1337);

        for (int i = 0; i < length; i++)
        {
            // Low-pass filtered pink/white noise
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            samples[i] = white * 0.15f;
        }

        AudioClip clip = AudioClip.Create("VHS_Tape_Static", length, 1, sampleRate, false);
        clip.SetData(samples, 0);

        staticAudio.clip = clip;
        staticAudio.Play();
    }

    void Update()
    {
        if (player == null || villain == null)
        {
            FindTargets();
            if (player == null || villain == null) return;
        }

        CalculateGlitchTarget();

        // Smooth transition
        currentGlitch = Mathf.MoveTowards(currentGlitch, targetGlitch, 3f * Time.deltaTime);

        // Micro-bursts of analog tape dropouts
        burstTimer -= Time.deltaTime;
        if (burstTimer <= 0f)
        {
            burstTimer = UnityEngine.Random.Range(0.35f, 1.2f);
            if (currentGlitch > 0.15f)
            {
                burstIntensity = UnityEngine.Random.Range(0.2f, 0.6f) * currentGlitch;
            }
            else
            {
                burstIntensity = 0f;
            }
        }
        else
        {
            burstIntensity = Mathf.MoveTowards(burstIntensity, 0f, 5f * Time.deltaTime);
        }

        float effectiveGlitch = Mathf.Clamp01(currentGlitch + burstIntensity);

        // Update Shader
        if (glitchMaterial != null)
        {
            glitchMaterial.SetFloat("_GlitchIntensity", effectiveGlitch);
        }

        // Update URP Post-Processing
        UpdatePostProcessing(effectiveGlitch);

        // Update Audio
        if (staticAudio != null && enableStaticAudio)
        {
            staticAudio.volume = Mathf.Lerp(0f, maxAudioVolume, effectiveGlitch * effectiveGlitch);
        }

        // Update OSD
        if (enableOSD)
        {
            UpdateOSD(effectiveGlitch);
        }
    }

    void CalculateGlitchTarget()
    {
        if (!enableProximityGlitch)
        {
            targetGlitch = 0f;
            return;
        }

        float dist = Vector3.Distance(player.position, villain.position);

        if (dist >= maxGlitchDistance)
        {
            targetGlitch = 0f;
            return;
        }

        // Linear ramp between max distance and intense distance
        float t = 1f - Mathf.Clamp01((dist - intenseGlitchDistance) / (maxGlitchDistance - intenseGlitchDistance));
        targetGlitch = t * t; // exponential curve: builds dread smoothly, then spikes

        // Check villain state
        VillainAI ai = villain.GetComponent<VillainAI>();
        if (ai != null)
        {
            if (ai.CurrentState == VillainAI.State.Chase)
            {
                targetGlitch = Mathf.Clamp01(targetGlitch + chaseGlitchBoost);
            }
        }
    }

    void UpdatePostProcessing(float glitch)
    {
        if (!enableChromaticAberration) return;

        if (chromaticAberration != null)
        {
            chromaticAberration.intensity.value = Mathf.Lerp(baseChromatic, 0.22f, glitch);
        }

        if (filmGrain != null)
        {
            filmGrain.intensity.value = Mathf.Lerp(baseGrain, 0.35f, glitch);
        }

        if (vignette != null)
        {
            vignette.intensity.value = Mathf.Lerp(baseVignette, 0.42f, glitch);
        }
    }

    void UpdateOSD(float glitch)
    {
        if (recText != null)
        {
            // Blinking red dot every 1.2 seconds
            bool dotOn = (Mathf.FloorToInt(Time.timeSinceLevelLoad * 1.5f) % 2) == 0;
            recText.text = dotOn ? "REC <color=#FF2222>●</color>" : "REC <color=#551111>●</color>";
        }

        if (playText != null)
        {
            int totalSec = Mathf.FloorToInt(Time.timeSinceLevelLoad);
            int hrs = totalSec / 3600;
            int mins = (totalSec % 3600) / 60;
            int secs = totalSec % 60;

            if (glitch > 0.45f && UnityEngine.Random.value < 0.25f)
            {
                // Jitter/corrupt timecode under heavy glitch
                playText.text = $"PLAY ▶   {hrs:00}:{mins:00}:{UnityEngine.Random.Range(0, 99):00}";
            }
            else
            {
                playText.text = $"PLAY ▶   {hrs:00}:{mins:00}:{secs:00}";
            }
        }

        // Slight OSD jitter when glitching
        if (osdContainer != null)
        {
            if (glitch > 0.2f)
            {
                Vector2 jitter = UnityEngine.Random.insideUnitCircle * (glitch * 6f);
                osdContainer.anchoredPosition = jitter;
            }
            else
            {
                osdContainer.anchoredPosition = Vector2.zero;
            }
        }
    }
}
