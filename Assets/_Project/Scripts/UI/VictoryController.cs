using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using StarterAssets;
using Cursor = UnityEngine.Cursor;
using Object = UnityEngine.Object;

/// <summary>
/// Handles victory/escape logic and displays the analog-horror / CRT terminal
/// extraction screen when the player reaches an exit threshold in the Backrooms.
///
/// Builds its UI dynamically using UI Toolkit and the "Victory style" stylesheet.
/// Works on both mobile (touch, safe-area aware) and PC (mouse/keyboard).
/// </summary>
public class VictoryController : MonoBehaviour
{
    public static VictoryController Instance { get; private set; }

    /// True while the victory sequence is active or screen is showing.
    public static bool IsGameWon { get; private set; }

    /// Fired when the player successfully escapes.
    public static event Action OnPlayerEscaped;

    [Header("Timing")]
    [Tooltip("Delay in seconds before showing the UI after reaching the exit.")]
    [SerializeField] float showDelay = 0.5f;

    [Header("Audio")]
    [Tooltip("Sound played upon victory. If null, loads Resources/SFX/juanjo_ sound - elevated.")]
    [SerializeField] AudioClip victoryAudio;
    [Range(0f, 1f)]
    [SerializeField] float victoryAudioVolume = 0.9f;

    [Header("Scene Navigation")]
    [SerializeField] string menuSceneName = "Menu";
    [SerializeField] string nextSceneName = "Level94";

    [Header("Labels & Text")]
    [SerializeField] string headingLabel = "LEVEL 0 ESCAPED";
    [SerializeField] string subtitleLabel = "ANOMALOUS THRESHOLD BREACHED - STATUS: EXTRACTED";
    [SerializeField] string retryButtonLabel = "ENTER LEVEL 94";
    [SerializeField] string menuButtonLabel = "RETURN TO MENU";
    [SerializeField] string clearanceLevel = "ARCHIVE CLEAR // CLASS 1";

    [Header("Look & Fonts")]
    [SerializeField] Font uiFont;
    [SerializeField] string fontResourcePath = "Fonts/RobotoMono-Regular";
    [SerializeField] string styleResourcePath = "Victory style";

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
    float timeElapsed;
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
        IsGameWon = false;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        BuildUI();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            IsGameWon = false;
        }

        Time.timeScale = 1f;
        RestoreCursor();
    }

    void Update()
    {
        if (!IsGameWon && !GameOverController.IsGameOver)
        {
            timeElapsed += Time.deltaTime;
        }
    }

    /// <summary>
    /// Static entry point called when player reaches the exit zone.
    /// </summary>
    public static void TriggerVictory()
    {
        if (IsGameWon || GameOverController.IsGameOver) return;

        if (Instance != null)
        {
            if (AdManager.Instance != null)
            {
                AdManager.Instance.RecordSurvivalTime(Instance.timeElapsed);
            }
            Instance.StartCoroutine(Instance.VictorySequenceRoutine());
        }
        else
        {
            Debug.LogWarning("[VictoryController] No instance in scene; creating fallback.");
            var go = new GameObject("VictoryController_Fallback");
            var ctrl = go.AddComponent<VictoryController>();
            ctrl.StartCoroutine(ctrl.VictorySequenceRoutine());
        }
    }

    IEnumerator VictorySequenceRoutine()
    {
        IsGameWon = true;
        OnPlayerEscaped?.Invoke();

        // Save progress / checkpoint for the next level
        string activeScene = SceneManager.GetActiveScene().name;
        if (activeScene.IndexOf("94", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // Level 94 victory
            GameProgressManager.SaveCheckpoint(GameProgressManager.MenuSceneName, 94);
        }
        else
        {
            // Level 0 victory -> Checkpoint at Level 94!
            GameProgressManager.SaveCheckpoint(GameProgressManager.Level94SceneName, 94);
        }

        DisablePlayerInput();
        HideMobileControls();
        PlayVictoryAudio();
        FreezeAllEntities();

        yield return new WaitForSecondsRealtime(showDelay);

        ShowVictoryUI();
    }

    void FreezeAllEntities()
    {
        VillainAI[] villains = Object.FindObjectsByType<VillainAI>(FindObjectsSortMode.None);
        foreach (var v in villains)
        {
            if (v == null) continue;

            var agent = v.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
                agent.ResetPath();
            }

            var anim = v.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.speed = 0f;
            }
        }

        Animator[] animators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
        foreach (var a in animators)
        {
            if (a != null && !a.CompareTag("Player"))
            {
                a.speed = 0f;
            }
        }
    }

    void ShowVictoryUI()
    {
        Time.timeScale = 0f;

        if (timeStatLabel != null)
        {
            TimeSpan span = TimeSpan.FromSeconds(timeElapsed);
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
        while (IsGameWon && recBadge != null)
        {
            recBadge.style.opacity = 1f;
            yield return new WaitForSecondsRealtime(0.6f);
            if (!IsGameWon) break;
            recBadge.style.opacity = 0.2f;
            yield return new WaitForSecondsRealtime(0.4f);
        }
    }

    void OnRetryClicked()
    {
        IsGameWon = false;
        Time.timeScale = 1f;
        RestoreCursor();

        string targetScene = SceneManager.GetActiveScene().name;
        if (!string.IsNullOrEmpty(nextSceneName) && Application.CanStreamedLevelBeLoaded(nextSceneName))
        {
            targetScene = nextSceneName;
        }

        string finalTargetScene = targetScene;
        if (AdManager.Instance != null)
        {
            AdManager.Instance.TryShowInterstitial(() =>
            {
                AdManager.Instance.ResetRunState();
                SceneManager.LoadScene(finalTargetScene);
            });
        }
        else
        {
            SceneManager.LoadScene(finalTargetScene);
        }
    }

    void OnMenuClicked()
    {
        IsGameWon = false;
        Time.timeScale = 1f;
        RestoreCursor();

        Action loadMenu = () =>
        {
            if (AdManager.Instance != null) AdManager.Instance.ResetRunState();
            if (Application.CanStreamedLevelBeLoaded(menuSceneName))
                SceneManager.LoadScene(menuSceneName);
            else
                SceneManager.LoadScene(0);
        };

        if (AdManager.Instance != null)
        {
            AdManager.Instance.TryShowInterstitial(loadMenu);
        }
        else
        {
            loadMenu();
        }
    }

    void HideMobileControls()
    {
        TouchscreenInput touchUI = FindAnyObjectByType<TouchscreenInput>(FindObjectsInactive.Include);
        if (touchUI != null) touchUI.gameObject.SetActive(false);

        GameObject touchGo = GameObject.Find("UI_TouchScreenInput");
        if (touchGo != null) touchGo.SetActive(false);
    }

    void PlayVictoryAudio()
    {
        AudioClip clip = victoryAudio;
        if (clip == null)
            clip = Resources.Load<AudioClip>("SFX/juanjo_ sound - elevated");

        if (clip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clip, victoryAudioVolume);
        }
    }

    void DisablePlayerInput()
    {
        var player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            var fpc = player.GetComponent<FirstPersonController>();
            if (fpc != null) fpc.enabled = false;

            var motion = player.GetComponent<FirstPersonMotion>();
            if (motion != null) motion.enabled = false;

            var tpc = player.GetComponent<ThirdPersonController>();
            if (tpc != null) tpc.enabled = false;

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            var input = player.GetComponent<StarterAssetsInputs>();
            if (input != null)
            {
                input.MoveInput(Vector2.zero);
                input.LookInput(Vector2.zero);
                input.SprintInput(false);
                input.JumpInput(false);
            }

            var uInput = player.GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if (uInput != null) uInput.enabled = false;
        }
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

    // ------------------------------------------------------------- UI Builder

    void BuildUI()
    {
        document = gameObject.GetComponent<UIDocument>();
        if (document == null) document = gameObject.AddComponent<UIDocument>();

        panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
        if (panelSettings == null)
        {
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "VictoryPanelSettings";
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.match = 0.5f;
            panelSettings.sortingOrder = 99; // Above pause menu & touch input
        }
        document.panelSettings = panelSettings;

        StyleSheet styleSheet = Resources.Load<StyleSheet>(styleResourcePath);
        font = uiFont != null ? uiFont : Resources.Load<Font>(fontResourcePath);

        root = document.rootVisualElement;
        root.Clear();
        root.AddToClassList("victory-root");
        if (styleSheet != null) root.styleSheets.Add(styleSheet);
        if (font != null) root.style.unityFont = new StyleFont(font);

        // Backdrop
        backdrop = new VisualElement();
        backdrop.AddToClassList("victory-backdrop");
        backdrop.style.display = DisplayStyle.None; // Hidden initially
        root.Add(backdrop);

        // Vignette & Scanlines
        var vignette = new VisualElement();
        vignette.AddToClassList("victory-vignette");
        vignette.pickingMode = PickingMode.Ignore;
        backdrop.Add(vignette);

        var scanlines = new VisualElement();
        scanlines.AddToClassList("victory-scanlines");
        scanlines.pickingMode = PickingMode.Ignore;
        backdrop.Add(scanlines);

        // Top HUD
        recBadge = new Label("[ EXTRACTED ]");
        recBadge.AddToClassList("victory-rec-badge");
        backdrop.Add(recBadge);

        var chBadge = new Label(clearanceLevel);
        chBadge.AddToClassList("victory-channel-badge");
        backdrop.Add(chBadge);

        // Center Card
        card = new VisualElement();
        card.AddToClassList("victory-card");
        backdrop.Add(card);

        var heading = new Label(headingLabel);
        heading.AddToClassList("victory-heading");
        card.Add(heading);

        var sub = new Label(subtitleLabel);
        sub.AddToClassList("victory-subheading");
        card.Add(sub);

        var div = new VisualElement();
        div.AddToClassList("victory-divider");
        card.Add(div);

        // Stats Box
        var statsBox = new VisualElement();
        statsBox.AddToClassList("victory-stats-box");
        card.Add(statsBox);

        statsBox.Add(CreateStatRow("TIME ELAPSED", out timeStatLabel, "00:00"));
        statsBox.Add(CreateStatRow("SURVIVAL STATUS", "CONFIRMED ALIVE", true));
        statsBox.Add(CreateStatRow("ZONE PURGE", "COMPLETED", false));

        // Action Buttons
        retryButton = new Button(OnRetryClicked) { text = "> " + retryButtonLabel };
        retryButton.AddToClassList("victory-button");
        card.Add(retryButton);

        menuButton = new Button(OnMenuClicked) { text = "> " + menuButtonLabel };
        menuButton.AddToClassList("victory-button");
        menuButton.AddToClassList("victory-button--secondary");
        card.Add(menuButton);
    }

    VisualElement CreateStatRow(string labelText, out Label valueLabel, string initialValue, bool isSuccess = false)
    {
        var row = new VisualElement();
        row.AddToClassList("victory-stat-row");

        var lbl = new Label(labelText);
        lbl.AddToClassList("victory-stat-label");
        row.Add(lbl);

        valueLabel = new Label(initialValue);
        valueLabel.AddToClassList("victory-stat-value");
        if (isSuccess) valueLabel.AddToClassList("victory-stat-value--success");
        row.Add(valueLabel);

        return row;
    }

    VisualElement CreateStatRow(string labelText, string initialValue, bool isSuccess = false)
    {
        return CreateStatRow(labelText, out _, initialValue, isSuccess);
    }
}
