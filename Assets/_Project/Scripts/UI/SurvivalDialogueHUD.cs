using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using StarterAssets;

/// <summary>
/// Replaces the intrusive full-screen Survival Manual modal with an immersive,
/// non-blocking analog-horror subtitle / dialogue banner at the top of the screen.
///
/// Delivers critical survival instructions, stealth mechanics, navigation tips,
/// and contextual alerts smoothly during real-time gameplay without freezing time
/// or breaking player immersion.
/// </summary>
public class SurvivalDialogueHUD : MonoBehaviour
{
    public static SurvivalDialogueHUD Instance { get; private set; }

    public struct DialogueEntry
    {
        public string Header;
        public string Message;
        public float Duration;
        public bool IsAlert;
        public Action OnComplete;

        public DialogueEntry(string header, string message, float duration = 5f, bool isAlert = false, Action onComplete = null)
        {
            Header = header;
            Message = message;
            Duration = duration;
            IsAlert = isAlert;
            OnComplete = onComplete;
        }
    }

    [Header("Behavior")]
    [Tooltip("Play the core survival orientation subtitles when entering the level.")]
    [SerializeField] bool playIntroSequenceOnStart = true;
    [Tooltip("Initial delay before starting the intro dialogue sequence (seconds).")]
    [SerializeField] float introStartDelay = 1.6f;
    [Tooltip("Enable subtle contextual in-game warnings (first flashlight, first sprint, near exit).")]
    [SerializeField] bool enableContextualAlerts = true;

    [Header("Audio")]
    [Tooltip("Play a subtle, quiet procedural walkie-talkie / radio transmission chirp when dialogue appears.")]
    [SerializeField] bool playTransmissionAudio = true;
    [Range(0f, 1f)]
    [SerializeField] float transmissionVolume = 0.22f;

    [Header("Look & Typography")]
    [SerializeField] Font uiFont;
    [SerializeField] string fontResourcePath = "Fonts/RobotoMono-Regular";

    private UIDocument document;
    private PanelSettings panelSettings;
    private Font font;

    private VisualElement root;
    private VisualElement container;
    private VisualElement banner;
    private VisualElement accentStripe;
    private Label badgeLabel;
    private Label messageLabel;
    private Label dismissHintLabel;

    private readonly Queue<DialogueEntry> messageQueue = new Queue<DialogueEntry>();
    private Coroutine activePlaybackCoroutine;
    private bool isSkippingCurrent;

    // Contextual alert tracking
    private bool hasTriggeredFlashlightAlert;
    private bool hasTriggeredSprintAlert;
    private bool hasTriggeredExitAlert;

    private AudioSource audioSource;
    private AudioClip chirpClip;

    private Rect appliedSafeArea;
    private int appliedScreenWidth;
    private int appliedScreenHeight;

    private Transform playerTransform;
    private BackroomsExitZone exitZone;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        SetupAudio();
        BuildUI();
    }

    private void OnEnable()
    {
        PauseMenuController.PauseStateChanged += HandlePauseStateChanged;
        GameOverController.OnPlayerDefeated += HandleGameOverOrVictory;
        VictoryController.OnPlayerEscaped += HandleGameOverOrVictory;
        TouchscreenInput.OnFlashlightStateChanged += HandleFlashlightChanged;
    }

    private void OnDisable()
    {
        PauseMenuController.PauseStateChanged -= HandlePauseStateChanged;
        GameOverController.OnPlayerDefeated -= HandleGameOverOrVictory;
        VictoryController.OnPlayerEscaped -= HandleGameOverOrVictory;
        TouchscreenInput.OnFlashlightStateChanged -= HandleFlashlightChanged;
    }

    private void Start()
    {
        FindWorldReferences();

        if (playIntroSequenceOnStart)
        {
            StartCoroutine(QueueIntroSequenceRoutine());
        }
    }

    private void Update()
    {
        UpdateSafeArea();
        CheckContextualTriggers();
    }

    private void FindWorldReferences()
    {
        if (playerTransform == null)
        {
            var charController = FindAnyObjectByType<CharacterController>();
            if (charController != null) playerTransform = charController.transform;
            else
            {
                var cam = Camera.main;
                if (cam != null) playerTransform = cam.transform;
            }
        }

        if (exitZone == null)
        {
            exitZone = FindAnyObjectByType<BackroomsExitZone>();
        }
    }

    // -------------------------------------------------------------------------
    // PUBLIC INTERFACE: POST MESSAGES
    // -------------------------------------------------------------------------

    /// <summary>
    /// Enqueues a survival subtitle / dialogue banner at the top of the screen.
    /// </summary>
    public static void PostMessage(string header, string message, float duration = 5.2f, bool isAlert = false, Action onComplete = null)
    {
        if (Instance == null) return;
        Instance.Enqueue(new DialogueEntry(header, message, duration, isAlert, onComplete));
    }

    /// <summary>
    /// Immediately interrupts and displays a critical warning at the top of the queue.
    /// </summary>
    public static void PostPriorityAlert(string header, string message, float duration = 4.8f)
    {
        if (Instance == null) return;
        Instance.EnqueuePriority(new DialogueEntry(header, message, duration, isAlert: true));
    }

    public void Enqueue(DialogueEntry entry)
    {
        if (GameOverController.IsGameOver || VictoryController.IsGameWon) return;

        messageQueue.Enqueue(entry);
        if (activePlaybackCoroutine == null)
        {
            activePlaybackCoroutine = StartCoroutine(PlaybackLoop());
        }
    }

    public void EnqueuePriority(DialogueEntry entry)
    {
        if (GameOverController.IsGameOver || VictoryController.IsGameWon) return;

        // Skip current message to show the urgent alert
        isSkippingCurrent = true;
        // Insert as next message
        var list = new List<DialogueEntry> { entry };
        while (messageQueue.Count > 0) list.Add(messageQueue.Dequeue());
        foreach (var item in list) messageQueue.Enqueue(item);

        if (activePlaybackCoroutine == null)
        {
            activePlaybackCoroutine = StartCoroutine(PlaybackLoop());
        }
    }

    public void ReplaySurvivalIntro()
    {
        messageQueue.Clear();
        isSkippingCurrent = true;
        StartCoroutine(QueueIntroSequenceRoutine());
    }

    // -------------------------------------------------------------------------
    // INTRO SURVIVAL SEQUENCE
    // -------------------------------------------------------------------------

    private IEnumerator QueueIntroSequenceRoutine()
    {
        yield return new WaitForSecondsRealtime(introStartDelay);
        if (GameOverController.IsGameOver || VictoryController.IsGameWon) yield break;

        bool isMobile = PlatformUtils.IsMobile;

        // 1. Mission / Situation
        Enqueue(new DialogueEntry(
            "SURVIVAL PROTOCOL // MISSION OBJECTIVE",
            "You have no-clipped into Level 0. Explore the yellow labyrinth and locate the glowing EXIT threshold to escape.",
            5.2f));

        // 2. Audio Beacon Navigation
        Enqueue(new DialogueEntry(
            "NAVIGATION BEACON // AUDIO CUE",
            "Listen closely... the exit door emits an electrical humming noise that gets louder as you draw near.",
            5.5f));

        // 3. Entity Rule: Light
        Enqueue(new DialogueEntry(
            "SURVIVAL RULE: LIGHT ATTRACTS ENTITIES",
            "Keep your flashlight OFF when exploring. Turning it ON doubles the entity's detection range and spots you across rooms!",
            5.5f,
            isAlert: true));

        // 4. Entity Rule: Noise
        Enqueue(new DialogueEntry(
            "SURVIVAL RULE: RUNNING FOOTSTEPS",
            "Sprinting echoes heavily through walls (15m radius). Walk quietly—only sprint to break away during active chases.",
            5.5f,
            isAlert: true));

        // 5. Controls
        string controlsMsg = isMobile
            ? "Drag left joystick to move • Swipe right to aim • Tap torch to toggle light • Tap runner icon to sprint."
            : "[WASD] Move • [Mouse] Look • [F] Flashlight • [Shift] Sprint • [Esc] Pause & Settings.";

        Enqueue(new DialogueEntry(
            "CONTROLS OVERVIEW",
            controlsMsg,
            5.0f));
    }

    // -------------------------------------------------------------------------
    // CONTEXTUAL IN-GAME TRIGGERS
    // -------------------------------------------------------------------------

    private void HandleFlashlightChanged(bool isOn)
    {
        if (!enableContextualAlerts || !isOn || hasTriggeredFlashlightAlert) return;
        hasTriggeredFlashlightAlert = true;

        PostMessage(
            "WARNING: FLASHLIGHT ACTIVE",
            "Light beams cut through dark corridors. Entities spot you from twice as far away!",
            4.8f,
            isAlert: true);
    }

    private void CheckContextualTriggers()
    {
        if (!enableContextualAlerts) return;
        if (GameOverController.IsGameOver || VictoryController.IsGameWon || PauseMenuController.IsPaused) return;

        // 1. Sprinting detection
        if (!hasTriggeredSprintAlert)
        {
            var noiseEmitter = FindAnyObjectByType<PlayerNoiseEmitter>();
            if (noiseEmitter != null)
            {
                var inputs = noiseEmitter.GetComponent<StarterAssetsInputs>();
                if (inputs != null && inputs.sprint && inputs.move.sqrMagnitude > 0.1f)
                {
                    hasTriggeredSprintAlert = true;
                    PostMessage(
                        "WARNING: HEAVY NOISE",
                        "Running footsteps vibrate through the floor! Entities will track your noise.",
                        4.8f,
                        isAlert: true);
                }
            }
        }

        // 2. Near Exit threshold detection (< 20m)
        if (!hasTriggeredExitAlert && exitZone != null && playerTransform != null)
        {
            float dist = Vector3.Distance(playerTransform.position, exitZone.transform.position);
            if (dist <= 20f)
            {
                hasTriggeredExitAlert = true;
                PostMessage(
                    "SIGNAL DETECTED // AUDIO BEACON",
                    "Electrical hum audible nearby! Follow the humming noise to locate the exit door.",
                    5.2f,
                    isAlert: false);
            }
        }
    }

    // -------------------------------------------------------------------------
    // PLAYBACK PIPELINE
    // -------------------------------------------------------------------------

    private IEnumerator PlaybackLoop()
    {
        while (messageQueue.Count > 0)
        {
            if (GameOverController.IsGameOver || VictoryController.IsGameWon)
            {
                HideBannerInstant();
                messageQueue.Clear();
                activePlaybackCoroutine = null;
                yield break;
            }

            // Wait if paused
            while (PauseMenuController.IsPaused)
            {
                banner.style.display = DisplayStyle.None;
                yield return null;
            }

            DialogueEntry entry = messageQueue.Dequeue();
            isSkippingCurrent = false;

            // Configure visual styling for this entry
            badgeLabel.text = "● " + entry.Header.ToUpper();
            Color accentColor = entry.IsAlert
                ? new Color(0.96f, 0.42f, 0.35f, 1f)   // Danger red-orange
                : new Color(0.88f, 0.85f, 0.46f, 1f);  // Liminal yellow-amber

            badgeLabel.style.color = accentColor;
            accentStripe.style.backgroundColor = accentColor;
            banner.style.borderLeftColor = accentColor;

            messageLabel.text = "";
            banner.style.display = DisplayStyle.Flex;
            banner.style.opacity = 1f;

            PlayChirpSound();

            // Typewriter reveal (fast and satisfying: ~45 chars/sec)
            string fullText = entry.Message;
            float charInterval = 0.022f;
            for (int i = 0; i <= fullText.Length; i++)
            {
                if (isSkippingCurrent || GameOverController.IsGameOver || VictoryController.IsGameWon)
                {
                    messageLabel.text = fullText;
                    break;
                }
                while (PauseMenuController.IsPaused) yield return null;

                messageLabel.text = fullText.Substring(0, i);
                yield return new WaitForSecondsRealtime(charInterval);
            }

            // Display duration hold
            float timer = 0f;
            while (timer < entry.Duration && !isSkippingCurrent)
            {
                if (GameOverController.IsGameOver || VictoryController.IsGameWon)
                {
                    HideBannerInstant();
                    messageQueue.Clear();
                    activePlaybackCoroutine = null;
                    yield break;
                }
                if (!PauseMenuController.IsPaused)
                {
                    timer += Time.unscaledDeltaTime;
                }
                yield return null;
            }

            entry.OnComplete?.Invoke();

            // Smooth fade out
            float fadeTime = 0.22f;
            float elapsed = 0f;
            while (elapsed < fadeTime && !isSkippingCurrent)
            {
                elapsed += Time.unscaledDeltaTime;
                banner.style.opacity = Mathf.Lerp(1f, 0f, elapsed / fadeTime);
                yield return null;
            }

            banner.style.display = DisplayStyle.None;
            banner.style.opacity = 0f;

            // Subtle pause between consecutive messages
            if (messageQueue.Count > 0 && !isSkippingCurrent)
            {
                yield return new WaitForSecondsRealtime(0.35f);
            }
        }

        activePlaybackCoroutine = null;
    }

    private void SkipCurrentDialogue()
    {
        isSkippingCurrent = true;
    }

    private void HideBannerInstant()
    {
        if (banner != null)
        {
            banner.style.display = DisplayStyle.None;
            banner.style.opacity = 0f;
        }
    }

    private void HandlePauseStateChanged(bool isPaused)
    {
        if (banner != null)
        {
            banner.style.display = (!isPaused && activePlaybackCoroutine != null) ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void HandleGameOverOrVictory()
    {
        messageQueue.Clear();
        isSkippingCurrent = true;
        HideBannerInstant();
    }

    // -------------------------------------------------------------------------
    // UI BUILD & STYLING
    // -------------------------------------------------------------------------

    private void BuildUI()
    {
        font = uiFont != null ? uiFont : LoadFont();

        panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
        if (panelSettings == null)
        {
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "Dialogue Panel Settings";
            panelSettings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
            panelSettings.sortingOrder = 25; // Renders above gameplay, below pause menu (100)
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
        }

        document = gameObject.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;

        root = document.rootVisualElement;
        root.pickingMode = PickingMode.Ignore; // Let gameplay touches pass through full screen

        // Top-centered wrapper
        container = new VisualElement();
        container.pickingMode = PickingMode.Ignore;
        container.style.position = Position.Absolute;
        container.style.top = 22f;
        container.style.left = 0f;
        container.style.right = 0f;
        container.style.alignItems = Align.Center;
        container.style.justifyContent = Justify.FlexStart;
        root.Add(container);

        // Subtitle banner box
        banner = new VisualElement();
        banner.pickingMode = PickingMode.Position; // Click/tap anywhere on banner to skip/dismiss
        banner.style.display = DisplayStyle.None;
        banner.style.opacity = 0f;
        banner.style.maxWidth = 720f;
        banner.style.width = new Length(90f, LengthUnit.Percent);
        banner.style.minHeight = 56f;
        banner.style.backgroundColor = new Color(0.04f, 0.05f, 0.02f, 0.88f);
        banner.style.borderTopWidth = 1f;
        banner.style.borderRightWidth = 1f;
        banner.style.borderBottomWidth = 1f;
        banner.style.borderLeftWidth = 4f; // Left accent bar
        banner.style.borderTopColor = new Color(0.88f, 0.85f, 0.46f, 0.28f);
        banner.style.borderRightColor = new Color(0.88f, 0.85f, 0.46f, 0.28f);
        banner.style.borderBottomColor = new Color(0.88f, 0.85f, 0.46f, 0.28f);
        banner.style.borderLeftColor = new Color(0.88f, 0.85f, 0.46f, 0.95f);
        banner.style.borderTopLeftRadius = 6f;
        banner.style.borderTopRightRadius = 6f;
        banner.style.borderBottomLeftRadius = 6f;
        banner.style.borderBottomRightRadius = 6f;
        banner.style.paddingTop = 10f;
        banner.style.paddingBottom = 12f;
        banner.style.paddingLeft = 16f;
        banner.style.paddingRight = 16f;

        // Dismiss upon tapping/clicking the banner
        banner.RegisterCallback<PointerDownEvent>(evt => SkipCurrentDialogue());

        container.Add(banner);

        // Header row (Badge + Dismiss Hint)
        VisualElement headerRow = new VisualElement();
        headerRow.pickingMode = PickingMode.Ignore;
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.justifyContent = Justify.SpaceBetween;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.marginBottom = 5f;
        banner.Add(headerRow);

        // Header badge
        badgeLabel = new Label("● SURVIVAL PROTOCOL");
        badgeLabel.pickingMode = PickingMode.Ignore;
        if (font != null) badgeLabel.style.unityFont = font;
        badgeLabel.style.fontSize = 12f;
        badgeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        badgeLabel.style.color = new Color(0.88f, 0.85f, 0.46f, 1f);
        badgeLabel.style.letterSpacing = 1.2f;
        headerRow.Add(badgeLabel);

        // Dismiss hint
        dismissHintLabel = new Label("[TAP TO SKIP]");
        dismissHintLabel.pickingMode = PickingMode.Ignore;
        if (font != null) dismissHintLabel.style.unityFont = font;
        dismissHintLabel.style.fontSize = 10f;
        dismissHintLabel.style.color = new Color(0.88f, 0.85f, 0.46f, 0.45f);
        dismissHintLabel.style.letterSpacing = 1f;
        headerRow.Add(dismissHintLabel);

        // Message text label
        messageLabel = new Label();
        messageLabel.pickingMode = PickingMode.Ignore;
        if (font != null) messageLabel.style.unityFont = font;
        messageLabel.style.fontSize = 15f;
        messageLabel.style.color = new Color(0.92f, 0.91f, 0.82f, 1f);
        messageLabel.style.whiteSpace = WhiteSpace.Normal;
        banner.Add(messageLabel);

        // Left accent stripe indicator reference
        accentStripe = banner;
    }

    private void UpdateSafeArea()
    {
        if (container == null || root == null) return;

        Rect safeArea = Screen.safeArea;
        if (safeArea.width <= 0f || safeArea.height <= 0f)
            safeArea = new Rect(0f, 0f, Screen.width, Screen.height);

        if (safeArea == appliedSafeArea &&
            Screen.width == appliedScreenWidth &&
            Screen.height == appliedScreenHeight)
            return;

        float panelWidth = root.resolvedStyle.width;
        float panelHeight = root.resolvedStyle.height;
        if (float.IsNaN(panelWidth) || float.IsNaN(panelHeight) || panelWidth <= 0f || panelHeight <= 0f)
            return;

        appliedSafeArea = safeArea;
        appliedScreenWidth = Screen.width;
        appliedScreenHeight = Screen.height;

        float toPanelY = panelHeight / Mathf.Max(1f, Screen.height);
        float insetTop = Mathf.Max(0f, (Screen.height - safeArea.yMax) * toPanelY);

        container.style.top = 18f + insetTop;
    }

    private Font LoadFont()
    {
        Font f = Resources.Load<Font>(fontResourcePath);
        if (f != null) return f;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    // -------------------------------------------------------------------------
    // PROCEDURAL AUDIO
    // -------------------------------------------------------------------------

    private void SetupAudio()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f; // Full 2D UI sound
        audioSource.volume = transmissionVolume;

        chirpClip = GenerateTransmissionChirp();
    }

    private void PlayChirpSound()
    {
        if (!playTransmissionAudio || audioSource == null || chirpClip == null) return;
        audioSource.pitch = UnityEngine.Random.Range(0.96f, 1.04f);
        audioSource.PlayOneShot(chirpClip, transmissionVolume);
    }

    private AudioClip GenerateTransmissionChirp()
    {
        int sampleRate = 44100;
        int sampleCount = (int)(sampleRate * 0.055f); // 55ms soft radio burst
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float progress = (float)i / sampleCount;

            // Attack & decay envelope
            float envelope = progress < 0.2f
                ? progress / 0.2f
                : Mathf.Exp(-(progress - 0.2f) * 6.5f);

            // Dual tone walkie-talkie burst + slight radio hiss
            float tone1 = Mathf.Sin(2f * Mathf.PI * 1850f * t) * 0.35f;
            float tone2 = Mathf.Sin(2f * Mathf.PI * 3420f * t) * 0.15f;
            float noise = (UnityEngine.Random.value * 2f - 1f) * 0.18f;

            samples[i] = (tone1 + tone2 + noise) * envelope;
        }

        AudioClip clip = AudioClip.Create("TransmissionChirp", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
