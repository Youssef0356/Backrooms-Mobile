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
    int lastOsdSec = -1;
    bool lastDotOn = false;

    // Layout tracking for OSD safe area and HUD button spacing
    Rect lastAppliedSafeArea;
    int lastScreenWidth;
    int lastScreenHeight;
    float lastClusterLeftEdge = -1f;

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
        vhsCanvas.sortingOrder = 2; // Render directly above 3D world, below all interactive HUDs and touch buttons

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
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(45f, -38f), new Vector2(300f, 60f));

        // Placed cleanly to the left of the top-right button cluster ([?] and [⚙])
        spText = CreateOSDLabel("SP_Label", osdContainer, font, osdColor, 26, TextAnchor.UpperRight,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-180f, -38f), new Vector2(300f, 60f));
        spText.text = "SP  [|||]";

        playText = CreateOSDLabel("PLAY_Label", osdContainer, font, osdColor, 26, TextAnchor.LowerLeft,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(45f, 38f), new Vector2(400f, 60f));

        dateText = CreateOSDLabel("DATE_Label", osdContainer, font, osdColor, 26, TextAnchor.LowerRight,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-45f, 38f), new Vector2(400f, 60f));
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
            float pulse = ChaseSoundtrackDirector.CurrentHeartbeatPulse;
            glitchMaterial.SetFloat("_PursuitPulse", ChaseSoundtrackDirector.IsPursuitActive ? pulse : 0f);
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
        float pursuitGlitch = 0f;
        if (ChaseSoundtrackDirector.IsPursuitActive)
        {
            float pDist = ChaseSoundtrackDirector.CurrentClosestDistance;
            float pt = 1f - Mathf.Clamp01((pDist - intenseGlitchDistance) / (maxGlitchDistance - intenseGlitchDistance));
            pursuitGlitch = Mathf.Clamp01(pt * pt + chaseGlitchBoost * 1.5f);
        }

        if (!enableProximityGlitch && pursuitGlitch <= 0.001f)
        {
            targetGlitch = 0f;
            return;
        }

        float dist = (player != null && villain != null) ? Vector3.Distance(player.position, villain.position) : 999f;

        if (dist >= maxGlitchDistance)
        {
            targetGlitch = pursuitGlitch;
            return;
        }

        // Linear ramp between max distance and intense distance
        float t = 1f - Mathf.Clamp01((dist - intenseGlitchDistance) / (maxGlitchDistance - intenseGlitchDistance));
        targetGlitch = Mathf.Max(pursuitGlitch, t * t); // exponential curve: builds dread smoothly, then spikes

        // Check villain state
        if (villain != null)
        {
            VillainAI ai = villain.GetComponent<VillainAI>();
            if (ai != null && ai.CurrentState == VillainAI.State.Chase)
            {
                targetGlitch = Mathf.Clamp01(targetGlitch + chaseGlitchBoost);
            }
        }
    }

    void UpdatePostProcessing(float glitch)
    {
        if (!enableChromaticAberration) return;

        float heartbeatPulse = ChaseSoundtrackDirector.IsPursuitActive ? ChaseSoundtrackDirector.CurrentHeartbeatPulse : 0f;

        if (chromaticAberration != null)
        {
            // Flares outward during pursuit heartbeat pulses
            float targetChromatic = Mathf.Lerp(baseChromatic, 0.28f, glitch) + heartbeatPulse * 0.15f;
            chromaticAberration.intensity.value = Mathf.Clamp01(targetChromatic);
        }

        if (filmGrain != null)
        {
            // Keep grain crisp and non-blinding so view is clear
            filmGrain.intensity.value = Mathf.Lerp(baseGrain, 0.22f, glitch);
        }

        if (vignette != null)
        {
            // Vignette darkens slightly during chase pulses for tunnel-vision fear
            float targetVig = Mathf.Lerp(baseVignette, 0.44f, glitch) + heartbeatPulse * 0.08f;
            vignette.intensity.value = Mathf.Clamp(targetVig, 0f, 0.58f);
        }
    }

    /// <summary>
    /// Dynamically aligns all camera OSD elements to respect the device safe area
    /// and maintains clean spacing from the top-right button cluster ([?] guide and [⚙] pause).
    /// </summary>
    void UpdateOSDLayout()
    {
        Rect safeArea = Screen.safeArea;
        if (safeArea.width <= 0f || safeArea.height <= 0f)
            safeArea = new Rect(0f, 0f, Screen.width, Screen.height);

        float clusterLeftEdge = -1f;
        if (PauseMenuController.Instance != null)
        {
            clusterLeftEdge = PauseMenuController.Instance.TopRightButtonClusterLeftEdge;
        }

        if (safeArea == lastAppliedSafeArea &&
            Screen.width == lastScreenWidth &&
            Screen.height == lastScreenHeight &&
            Mathf.Approximately(clusterLeftEdge, lastClusterLeftEdge))
        {
            return;
        }

        lastAppliedSafeArea = safeArea;
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        lastClusterLeftEdge = clusterLeftEdge;

        float toCanvasX = 1920f / Mathf.Max(1f, Screen.width);
        float toCanvasY = 1080f / Mathf.Max(1f, Screen.height);

        float insetLeft = safeArea.xMin * toCanvasX;
        float insetRight = (Screen.width - safeArea.xMax) * toCanvasX;
        float insetTop = (Screen.height - safeArea.yMax) * toCanvasY;
        float insetBottom = safeArea.yMin * toCanvasY;

        // Top-left REC indicator
        if (recText != null)
        {
            recText.rectTransform.anchoredPosition = new Vector2(45f + insetLeft, -(38f + insetTop));
        }

        // Top-right SP & Battery HUD: dynamically spaced left of button cluster with generous padding
        if (spText != null)
        {
            float spOffset;
            if (clusterLeftEdge > 0f)
            {
                // Top-right buttons are active: place SP indicator to the left of the button cluster with a clean 28px gap
                spOffset = clusterLeftEdge + 28f;
            }
            else if (PauseMenuController.Instance == null)
            {
                // Default fallback when buttons exist in standard scene: cluster width (~148f) + safe area inset + 28px gap
                spOffset = 148f + insetRight + 28f;
            }
            else
            {
                // No buttons active: standard camcorder edge margin
                spOffset = 45f + insetRight;
            }

            spText.rectTransform.anchoredPosition = new Vector2(-spOffset, -(38f + insetTop));
        }

        // Bottom-left PLAY timecode
        if (playText != null)
        {
            playText.rectTransform.anchoredPosition = new Vector2(45f + insetLeft, 38f + insetBottom);
        }

        // Bottom-right date stamp
        if (dateText != null)
        {
            dateText.rectTransform.anchoredPosition = new Vector2(-(45f + insetRight), 38f + insetBottom);
        }
    }

    void UpdateOSD(float glitch)
    {
        UpdateOSDLayout();

        if (recText != null)
        {
            // Blinking red dot every 1.2 seconds
            bool dotOn = (Mathf.FloorToInt(Time.timeSinceLevelLoad * 1.5f) % 2) == 0;
            if (dotOn != lastDotOn)
            {
                lastDotOn = dotOn;
                recText.text = dotOn ? "REC <color=#FF2222>●</color>" : "REC <color=#551111>●</color>";
            }
        }

        if (playText != null)
        {
            int totalSec = Mathf.FloorToInt(Time.timeSinceLevelLoad);
            bool heavyGlitch = glitch > 0.45f && UnityEngine.Random.value < 0.25f;

            if (totalSec != lastOsdSec || heavyGlitch)
            {
                lastOsdSec = totalSec;
                int hrs = totalSec / 3600;
                int mins = (totalSec % 3600) / 60;
                int secs = totalSec % 60;

                if (heavyGlitch)
                {
                    // Jitter/corrupt timecode under heavy glitch
                    playText.text = $"PLAY ▶   {hrs:00}:{mins:00}:{UnityEngine.Random.Range(0, 99):00}";
                }
                else
                {
                    playText.text = $"PLAY ▶   {hrs:00}:{mins:00}:{secs:00}";
                }
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
