using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using StarterAssets;
using Unity.Cinemachine;
using Cursor = UnityEngine.Cursor;
using Object = UnityEngine.Object;

/// <summary>
/// Handles game over (lose) logic and displays the analog-horror / CRT terminal
/// game over screen when the player is caught by an entity.
///
/// Builds its UI dynamically using UI Toolkit and the "GameOver style" stylesheet.
/// Works on both mobile (touch, safe-area aware) and PC (mouse/keyboard).
/// </summary>
public class GameOverController : MonoBehaviour
{
    public static GameOverController Instance { get; private set; }

    /// True while the game over sequence is active or screen is showing.
    public static bool IsGameOver { get; private set; }

    /// Fired when the player is defeated/caught.
    public static event Action OnPlayerDefeated;

    [Header("Timing & Jumpscare")]
    [Tooltip("Delay in seconds between being caught and showing the UI (plays attack/turn).")]
    [SerializeField] float caughtDelay = 2.2f;
    [Tooltip("If true, turns the camera to face the entity when caught.")]
    [SerializeField] bool lookAtKiller = true;
    [SerializeField] float cameraTurnSpeed = 10f;

    [Header("Audio")]
    [Tooltip("Sound played upon defeat. If null, picks a monster composition clip.")]
    [SerializeField] AudioClip defeatAudio;
    [Range(0f, 1f)]
    [SerializeField] float defeatAudioVolume = 0.9f;
    [Tooltip("Fade-in duration in seconds for defeat audio.")]
    [SerializeField] float defeatFadeInDuration = 0.8f;
    [Tooltip("Fade-out duration in seconds for defeat audio.")]
    [SerializeField] float defeatFadeOutDuration = 2.2f;

    [Header("Scene Navigation")]
    [SerializeField] string menuSceneName = "Menu";

    [Header("Labels & Text")]
    [SerializeField] string headingLabel = "SIGNAL LOST";
    [SerializeField] string subtitleLabel = "ANOMALOUS ENTITY CONTACT - VITAL SIGNS ZERO";
    [SerializeField] string retryButtonLabel = "RE-ENTER LEVEL";
    [SerializeField] string menuButtonLabel = "RETURN TO MENU";
    [SerializeField] string threatName = "ENTITY-001 (BACTERIA)";

    [Header("Look & Fonts")]
    [SerializeField] Font uiFont;
    [SerializeField] string fontResourcePath = "Fonts/RobotoMono-Regular";
    [SerializeField] string styleResourcePath = "GameOver style";

    UIDocument document;
    PanelSettings panelSettings;
    Font font;

    VisualElement root;
    VisualElement backdrop;
    VisualElement card;
    Label recBadge;
    Label timeStatLabel;
    Button retryButton;
    Button menuButton;

    AudioSource audioSource;
    float timeSurvived;
    bool cursorUnlocked;
    CursorLockMode previousLockState;
    bool previousCursorVisible;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        IsGameOver = false;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        BuildUI();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            IsGameOver = false;
        }

        StopAllCoroutines();
        if (audioSource != null) audioSource.Stop();

        RestoreCursor();
    }

    /// <summary>
    /// Static helper called by VillainAI (or any hazard) when the player is caught.
    /// Auto-creates a controller if none exists in the scene.
    /// </summary>
    public static void TriggerDefeat(Transform killer = null)
    {
        if (IsGameOver) return;

        if (Instance == null)
        {
            GameObject go = new GameObject("GameOverController");
            Instance = go.AddComponent<GameOverController>();
        }

        Instance.StartGameOverSequence(killer);
    }

    public void StartGameOverSequence(Transform killer)
    {
        if (IsGameOver) return;
        IsGameOver = true;

        timeSurvived = Time.timeSinceLevelLoad;
        OnPlayerDefeated?.Invoke();

        // 1. Disable player movement and look
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            FirstPersonController fpc = player.GetComponent<FirstPersonController>();
            if (fpc != null) fpc.enabled = false;

            StarterAssetsInputs inputs = player.GetComponent<StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.MoveInput(Vector2.zero);
                inputs.LookInput(Vector2.zero);
                inputs.JumpInput(false);
                inputs.SprintInput(false);
            }
        }

        // 2. Hide mobile touchscreen UI so it doesn't overlap
        HideMobileControls();

        // 3. Play defeat sound
        PlayDefeatAudio();

        // 4. Run caught sequence (camera turn + delay, then show UI)
        StartCoroutine(CaughtSequenceRoutine(player != null ? player.transform : null, killer));
    }

    IEnumerator CaughtSequenceRoutine(Transform player, Transform killer)
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) mainCam = FindAnyObjectByType<Camera>();

        // 1. Release Cinemachine control so we have complete authority over the Main Camera
        if (mainCam != null)
        {
            CinemachineBrain brain = mainCam.GetComponent<CinemachineBrain>();
            if (brain != null) brain.enabled = false;
        }

        CinemachineCamera[] cineCams = FindObjectsByType<CinemachineCamera>();
        foreach (var c in cineCams) c.enabled = false;

        // Trigger maximum VHS glitch and static distortion for the jumpscare
        CameraVHSGlitch.TriggerKillBurst();

        // 2. Orient killer toward the camera and trigger the kill attack animation
        if (killer != null && mainCam != null)
        {
            Vector3 toCam = mainCam.transform.position - killer.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.01f)
            {
                killer.rotation = Quaternion.LookRotation(toCam);
            }

            Animator anim = killer.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.speed = 1f;
                anim.SetTrigger("Attack1");
            }
        }

        // 3. Jumpscare Camera Choreography (Snap to eyes, dolly in, tremor, lethal strike, floor collapse)
        if (mainCam != null && killer != null)
        {
            Transform cam = mainCam.transform;
            Vector3 killerHead = killer.position + Vector3.up * 1.55f;
            Vector3 startPos = cam.position;
            Vector3 dollyTarget = killer.position + (startPos - killer.position).normalized * 1.05f + Vector3.up * 1.5f;

            float timer = 0f;

            while (timer < caughtDelay)
            {
                float dt = Time.unscaledDeltaTime;
                timer += dt;

                if (timer < 1.15f)
                {
                    // Phase 1: Snap view into Clark's face and lunge forward toward him
                    if (lookAtKiller)
                    {
                        Vector3 lookDir = killerHead - cam.position;
                        if (lookDir.sqrMagnitude > 0.01f)
                        {
                            Quaternion lookRot = Quaternion.LookRotation(lookDir);
                            cam.rotation = Quaternion.Slerp(cam.rotation, lookRot, dt * cameraTurnSpeed);
                        }
                    }

                    cam.position = Vector3.MoveTowards(cam.position, dollyTarget, dt * 2.4f);

                    // Violent head tremor
                    Vector3 tremor = UnityEngine.Random.insideUnitSphere * (0.035f * (1.2f - timer));
                    cam.position += tremor;
                }
                else
                {
                    // Phase 2: Kill impact! Clark's strike hits the camera and player collapses
                    Quaternion collapseRot = Quaternion.Euler(68f, cam.rotation.eulerAngles.y + 20f, 25f);
                    cam.rotation = Quaternion.Slerp(cam.rotation, collapseRot, dt * 7f);
                    cam.position = Vector3.MoveTowards(cam.position, new Vector3(cam.position.x, 0.28f, cam.position.z), dt * 3.8f);
                }

                yield return null;
            }
        }
        else
        {
            yield return new WaitForSecondsRealtime(caughtDelay);
        }

        ShowGameOverUI();
    }

    void ShowGameOverUI()
    {
        if (timeStatLabel != null)
        {
            TimeSpan span = TimeSpan.FromSeconds(timeSurvived);
            timeStatLabel.text = string.Format("{0:00}:{1:00}", (int)span.TotalMinutes, span.Seconds);
        }

        if (backdrop != null)
        {
            backdrop.style.display = DisplayStyle.Flex;
            backdrop.AddToClassList("is-visible");
        }

        UnlockCursor();
        retryButton?.Focus();

        StartCoroutine(BlinkRecRoutine());
    }

    IEnumerator BlinkRecRoutine()
    {
        while (IsGameOver && recBadge != null)
        {
            recBadge.style.opacity = 1f;
            yield return new WaitForSecondsRealtime(0.6f);
            if (!IsGameOver) break;
            recBadge.style.opacity = 0.15f;
            yield return new WaitForSecondsRealtime(0.4f);
        }
    }

    void OnRetryClicked()
    {
        IsGameOver = false;
        Time.timeScale = 1f;
        RestoreCursor();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void OnMenuClicked()
    {
        IsGameOver = false;
        Time.timeScale = 1f;
        RestoreCursor();

        if (Application.CanStreamedLevelBeLoaded(menuSceneName))
            SceneManager.LoadScene(menuSceneName);
        else
            SceneManager.LoadScene(0);
    }

    void HideMobileControls()
    {
        // Search and disable UI_TouchScreenInput or TouchscreenInput objects
        TouchscreenInput touchUI = FindAnyObjectByType<TouchscreenInput>(FindObjectsInactive.Include);
        if (touchUI != null) touchUI.gameObject.SetActive(false);

        GameObject touchGo = GameObject.Find("UI_TouchScreenInput");
        if (touchGo != null) touchGo.SetActive(false);
    }

    void PlayDefeatAudio()
    {
        AudioClip clip = defeatAudio;
        if (clip == null)
            clip = MonsterCompositionAudio.GetRandomCompositionClip();
        if (clip == null)
            clip = Resources.Load<AudioClip>("SFX/juanjo_ sound - signal lost");

        if (clip != null && audioSource != null)
        {
            StartCoroutine(FadeDefeatAudioRoutine(clip));
        }
    }

    IEnumerator FadeDefeatAudioRoutine(AudioClip clip)
    {
        audioSource.clip = clip;
        audioSource.ignoreListenerPause = true;
        audioSource.volume = 0f;
        audioSource.Play();

        float targetVolume = defeatAudioVolume * GameSettings.MasterVolume;
        float clipDuration = clip.length;

        float fadeIn = Mathf.Min(defeatFadeInDuration, clipDuration * 0.3f);
        float fadeOut = Mathf.Min(defeatFadeOutDuration, clipDuration * 0.45f);
        float sustain = Mathf.Max(0f, clipDuration - fadeIn - fadeOut);

        // 1. Fade in
        float timer = 0f;
        while (timer < fadeIn)
        {
            timer += Time.unscaledDeltaTime;
            audioSource.volume = Mathf.Lerp(0f, targetVolume, Mathf.Clamp01(timer / fadeIn));
            yield return null;
        }
        audioSource.volume = targetVolume;

        // 2. Sustain
        if (sustain > 0f)
        {
            yield return new WaitForSecondsRealtime(sustain);
        }

        // 3. Fade out
        timer = 0f;
        while (timer < fadeOut)
        {
            timer += Time.unscaledDeltaTime;
            audioSource.volume = Mathf.Lerp(targetVolume, 0f, Mathf.Clamp01(timer / fadeOut));
            yield return null;
        }

        audioSource.volume = 0f;
        audioSource.Stop();
    }

    void UnlockCursor()
    {
        if (cursorUnlocked) return;
        previousLockState = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        cursorUnlocked = true;
    }

    void RestoreCursor()
    {
        if (!cursorUnlocked) return;
        Cursor.lockState = previousLockState;
        Cursor.visible = previousCursorVisible;
        cursorUnlocked = false;
    }

    void BuildUI()
    {
        font = uiFont != null ? uiFont : LoadFont();

        panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
        if (panelSettings == null)
        {
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "GameOver Panel Settings";
            panelSettings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
            panelSettings.sortingOrder = 150; // Above pause menu and touch controls
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
        }

        document = gameObject.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;

        root = document.rootVisualElement;
        root.AddToClassList("gameover-root");

        StyleSheet sheet = Resources.Load<StyleSheet>(styleResourcePath);
        if (sheet != null) root.styleSheets.Add(sheet);
        else Debug.LogWarning("GameOverController: stylesheet not found at " + styleResourcePath, this);

        // Backdrop
        backdrop = new VisualElement();
        backdrop.AddToClassList("gameover-backdrop");
        backdrop.style.display = DisplayStyle.None;
        root.Add(backdrop);

        // Vignette & Scanlines
        VisualElement vignette = new VisualElement();
        vignette.AddToClassList("gameover-vignette");
        backdrop.Add(vignette);

        VisualElement scanlines = new VisualElement();
        scanlines.AddToClassList("gameover-scanlines");
        backdrop.Add(scanlines);

        // Top badges
        recBadge = MakeLabel("● REC [SIGNAL LOST]", "gameover-rec-badge");
        backdrop.Add(recBadge);

        Label chBadge = MakeLabel("CAM_01 // LEVEL_0", "gameover-channel-badge");
        backdrop.Add(chBadge);

        // Center card
        card = new VisualElement();
        card.AddToClassList("gameover-card");
        backdrop.Add(card);

        card.Add(MakeLabel(headingLabel, "gameover-heading"));
        card.Add(MakeLabel(subtitleLabel, "gameover-subheading"));

        VisualElement divider1 = new VisualElement();
        divider1.AddToClassList("gameover-divider");
        card.Add(divider1);

        // Stats Box
        VisualElement statsBox = new VisualElement();
        statsBox.AddToClassList("gameover-stats-box");
        card.Add(statsBox);

        statsBox.Add(BuildStatRow("ZONE", "LEVEL 0 - THE LOBBY"));
        statsBox.Add(BuildStatRow("THREAT", threatName));
        statsBox.Add(BuildStatRow("STATUS", "VITAL SIGNS TERMINATED", isDanger: true));

        VisualElement timeRow = new VisualElement();
        timeRow.AddToClassList("gameover-stat-row");
        timeRow.Add(MakeLabel("TIME SURVIVED", "gameover-stat-label"));
        timeStatLabel = MakeLabel("00:00", "gameover-stat-value");
        timeRow.Add(timeStatLabel);
        statsBox.Add(timeRow);

        // Buttons
        retryButton = MakeButton(retryButtonLabel, OnRetryClicked);
        card.Add(retryButton);

        menuButton = MakeButton(menuButtonLabel, OnMenuClicked, secondary: true);
        card.Add(menuButton);
    }

    VisualElement BuildStatRow(string labelText, string valueText, bool isDanger = false)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("gameover-stat-row");
        row.Add(MakeLabel(labelText, "gameover-stat-label"));
        string valueClass = isDanger ? "gameover-stat-value gameover-stat-value--danger" : "gameover-stat-value";
        row.Add(MakeLabel(valueText, valueClass));
        return row;
    }

    Label MakeLabel(string text, string ussClass)
    {
        Label label = new Label(text);
        if (!string.IsNullOrEmpty(ussClass))
        {
            string[] classes = ussClass.Split(' ');
            foreach (string c in classes)
            {
                if (!string.IsNullOrEmpty(c)) label.AddToClassList(c);
            }
        }
        ApplyFont(label);
        return label;
    }

    Button MakeButton(string text, Action onClick, bool secondary = false)
    {
        Button button = new Button(onClick) { text = text };
        button.AddToClassList("gameover-button");
        if (secondary) button.AddToClassList("gameover-button--secondary");
        ApplyFont(button);
        return button;
    }

    void ApplyFont(VisualElement element)
    {
        if (font != null) element.style.unityFontDefinition = new StyleFontDefinition(font);
    }

    Font LoadFont()
    {
        if (string.IsNullOrEmpty(fontResourcePath)) return null;
        return Resources.Load<Font>(fontResourcePath);
    }
}
