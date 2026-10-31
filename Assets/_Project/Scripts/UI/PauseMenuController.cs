using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Cursor = UnityEngine.Cursor;

/// <summary>
/// In-game pause menu for the main level (mobile friendly).
///
/// Shows an on-screen pause button (top right) plus Escape / gamepad START as
/// toggles. Pausing freezes the game (Time.timeScale) and opens an overlay with:
///   - RESUME
///   - SOUND slider  (master volume, live)
///   - MUSIC slider  (ambience / music volume, live)
///   - EXIT          (back to the menu scene or quits the app)
///
/// The whole UI is built in code (UI Toolkit) so the scene only needs this one
/// component; the look comes from "Resources/Pause menu style.uss" and the same
/// RobotoMono font as the main menu. All tunables are exposed in the Inspector.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    public enum ExitMode { ReturnToMenu, QuitGame }

    // ------------------------------------------------------------- pause

    [Header("Pause")]
    [Tooltip("Time.timeScale while paused. 0 = fully frozen.")]
    [SerializeField] float pauseTimeScale = 0f;
    [Tooltip("Also allow pausing with the Escape key (desktop).")]
    [SerializeField] bool pauseOnEscape = true;
    [Tooltip("Also allow pausing with the gamepad START button.")]
    [SerializeField] bool pauseOnGamepadStart = true;
    [Tooltip("Free the mouse cursor while the menu is open (desktop).")]
    [SerializeField] bool unlockCursorWhenPaused = true;

    // ------------------------------------------------------------- exit

    [Header("Exit")]
    [Tooltip("What the EXIT button does.")]
    [SerializeField] ExitMode exitMode = ExitMode.ReturnToMenu;
    [Tooltip("Scene loaded when Exit Mode is Return To Menu.")]
    [SerializeField] string menuSceneName = "Menu";

    // ------------------------------------------------------------- sliders

    [Header("Settings rows (live)")]
    [SerializeField] bool showSoundSlider = true;
    [SerializeField] bool showMusicSlider = true;
    [SerializeField] bool showBrightnessSlider = true;
    [SerializeField] bool showSensitivitySlider = true;
    [SerializeField] bool showGraphicsOption = true;
    [SerializeField] bool showFrameRateOption = true;
    [SerializeField] bool showControlsButton = true;

    // ------------------------------------------------------------- pause button

    [Header("Pause button (mobile)")]
    [Tooltip("Show the on-screen pause button in the top right corner.")]
    [SerializeField] bool showPauseButton = true;

    // ------------------------------------------------------------- survival guide

    [Header("Survival Guide (Objectives & Controls)")]
    [Tooltip("Show the on-screen '?' guide button in the top right corner next to pause.")]
    [SerializeField] bool showGuideButton = false;

    // ------------------------------------------------------------- labels

    [Header("Labels")]
    [SerializeField] string headingLabel = "PAUSED";
    [SerializeField] string pausedSubtitle = "SIGNAL SUSPENDED";
    [SerializeField] string resumeLabel = "RESUME";
    [SerializeField] string exitLabel = "EXIT TO MENU";

    // ------------------------------------------------------------- look

    [Header("Look")]
    [Tooltip("Optional monospace font. When empty the font is loaded from Resources.")]
    [SerializeField] Font uiFont;
    [SerializeField] string fontResourcePath = "Fonts/RobotoMono-Regular";

    // ------------------------------------------------------------- state

    /// Singleton instance for scene access.
    public static PauseMenuController Instance { get; private set; }

    /// True while any pause menu on this loader context is open.
    public static bool IsPaused { get; private set; }

    /// Fired with the new paused state whenever the game pauses or resumes.
    public static event Action<bool> PauseStateChanged;

    UIDocument document;
    PanelSettings panelSettings;
    Font font;

    VisualElement root;
    VisualElement overlay;
    VisualElement pauseButton;
    VisualElement guideButton;
    VisualElement card;
    VisualElement controlsCard;
    VisualElement guideCard;
    Button resumeButton;
    Button guideCloseButton;
    bool openedGuideFromPause;

    // Safe area tracking for corner HUD buttons (notch / camera cut-out / rounded corners).
    // Expressed in panel units, same space the stylesheet uses.
    const float HudButtonMargin = 24f;
    Rect appliedSafeArea;
    int appliedScreenWidth;
    int appliedScreenHeight;
    float appliedInsetRight;
    float appliedInsetTop;

    /// <summary>
    /// True when the mobile pause/settings [⚙] button is visible on screen.
    /// </summary>
    public bool HasVisiblePauseButton => showPauseButton && pauseButton != null && pauseButton.style.display != DisplayStyle.None;

    /// <summary>
    /// True when the mobile survival guide [?] button is visible on screen.
    /// </summary>
    public bool HasVisibleGuideButton => showGuideButton && guideButton != null && guideButton.style.display != DisplayStyle.None;

    public float AppliedInsetRight => appliedInsetRight;
    public float AppliedInsetTop => appliedInsetTop;

    /// <summary>
    /// Returns the total distance from the right screen edge to the left edge of the top-right HUD buttons.
    /// Used by CameraVHSGlitch to place the camera HUD cleanly without any overlap.
    /// </summary>
    public float TopRightButtonClusterLeftEdge
    {
        get
        {
            if (!HasVisiblePauseButton && !HasVisibleGuideButton)
                return 0f;

            float rightPos = HudButtonMargin + appliedInsetRight;
            if (HasVisibleGuideButton)
                return rightPos + 68f + 56f;

            return rightPos + 56f;
        }
    }

    BackroomsAmbience ambience;
    float previousTimeScale = 1f;
    CursorLockMode previousLockState;
    bool previousCursorVisible;
    bool cursorUnlocked;

    // ------------------------------------------------------------- lifecycle

    void Awake()
    {
        Instance = this;
        PlayerPrefs.SetInt("backrooms.hideGuideOnStart", 1);
        PlayerPrefs.Save();
        BuildUI();
    }

    void Start()
    {
        // Survival guidance is now presented seamlessly via top dialogue subtitles (SurvivalDialogueHUD)
        // instead of pausing gameplay with a full-screen manual modal.
    }

    void OnEnable()
    {
        GameOverController.OnPlayerDefeated += OnGameOverDefeated;
        VictoryController.OnPlayerEscaped += OnGameOverDefeated;
        // If the scene starts while a stale static flag is set, clear it.
        if (!IsPaused) ShowMenu(false);
    }

    void OnDisable()
    {
        GameOverController.OnPlayerDefeated -= OnGameOverDefeated;
        VictoryController.OnPlayerEscaped -= OnGameOverDefeated;
    }

    void OnGameOverDefeated()
    {
        if (IsPaused)
        {
            IsPaused = false;
            Time.timeScale = 1f;
        }
        ShowMenu(false);
        SetPauseButtonVisible(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;

        // Never carry a paused clock or a locked cursor out of this scene.
        if (IsPaused)
        {
            Time.timeScale = previousTimeScale;
            IsPaused = false;
            RestoreCursor();
        }
    }

    void Update()
    {
        if (GameOverController.IsGameOver || VictoryController.IsGameWon) return;

        Keyboard keyboard = Keyboard.current;
        if (pauseOnEscape && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            TogglePause();

        if (pauseOnGamepadStart)
        {
            Gamepad pad = Gamepad.current;
            if (pad != null && pad.startButton.wasPressedThisFrame)
                TogglePause();
        }

        UpdateSafeArea();
    }

    // ------------------------------------------------------------- pause API

    /// Toggles between paused and playing.
    public void TogglePause()
    {
        if (GameOverController.IsGameOver || VictoryController.IsGameWon) return;
        if (IsPaused) Resume();
        else Pause();
    }

    /// Freezes the game and opens the menu.
    public void Pause()
    {
        if (IsPaused || GameOverController.IsGameOver) return;

        IsPaused = true;
        previousTimeScale = Time.timeScale;
        Time.timeScale = pauseTimeScale;

        ShowMenu(true);
        if (unlockCursorWhenPaused) UnlockCursor();

        PauseStateChanged?.Invoke(true);
    }

    /// Unfreezes the game and closes the menu.
    public void Resume()
    {
        if (!IsPaused) return;

        IsPaused = false;
        Time.timeScale = previousTimeScale;

        ShowMenu(false);
        RestoreCursor();

        PauseStateChanged?.Invoke(false);
    }

    // ------------------------------------------------------------- exit

    void OnExitClicked()
    {
        // Never carry a paused clock or locked cursor into the next scene.
        IsPaused = false;
        Time.timeScale = 1f;
        RestoreCursor();

        if (exitMode == ExitMode.ReturnToMenu && Application.CanStreamedLevelBeLoaded(menuSceneName))
            SceneManager.LoadScene(menuSceneName);
        else
            QuitGame();
    }

    void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------- UI state

    /// Allows external controllers (e.g. GameOverController) to explicitly hide or show corner HUD buttons.
    public void SetPauseButtonVisible(bool visible)
    {
        showPauseButton = visible;
        if (pauseButton != null)
            pauseButton.style.display = (visible && !IsPaused) ? DisplayStyle.Flex : DisplayStyle.None;
        if (guideButton != null)
            guideButton.style.display = (visible && !IsPaused && showGuideButton) ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void ShowMenu(bool show)
    {
        if (overlay != null)
            overlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

        if (pauseButton != null)
            pauseButton.style.display = (show || !showPauseButton) ? DisplayStyle.None : DisplayStyle.Flex;

        if (guideButton != null)
            guideButton.style.display = (show || !showGuideButton) ? DisplayStyle.None : DisplayStyle.Flex;

        if (show)
        {
            if (card != null) card.RemoveFromClassList("is-hidden");
            if (controlsCard != null) controlsCard.AddToClassList("is-hidden");
            if (guideCard != null) guideCard.AddToClassList("is-hidden");
            resumeButton?.Focus();
        }
    }

    /// <summary>
    /// Opens the in-game survival guide and controls manual.
    /// Pauses time and unlocks the cursor for reviewing the guide.
    /// </summary>
    public void OpenGuide(bool fromPauseMenu = false)
    {
        if (GameOverController.IsGameOver || VictoryController.IsGameWon) return;

        openedGuideFromPause = fromPauseMenu;

        if (!IsPaused)
        {
            IsPaused = true;
            previousTimeScale = Time.timeScale;
            Time.timeScale = pauseTimeScale;
            if (unlockCursorWhenPaused) UnlockCursor();
            PauseStateChanged?.Invoke(true);
        }

        if (card != null) card.AddToClassList("is-hidden");
        if (controlsCard != null) controlsCard.AddToClassList("is-hidden");
        if (guideCard != null) guideCard.RemoveFromClassList("is-hidden");

        if (guideCloseButton != null)
        {
            guideCloseButton.text = fromPauseMenu ? "RETURN TO PAUSE" : "ENTER THE BACKROOMS";
        }

        if (overlay != null) overlay.style.display = DisplayStyle.Flex;
        if (pauseButton != null) pauseButton.style.display = DisplayStyle.None;
        if (guideButton != null) guideButton.style.display = DisplayStyle.None;
    }

    /// <summary>
    /// Closes the guide and either returns to the pause menu or unpauses back into active gameplay.
    /// </summary>
    public void CloseGuide()
    {
        if (openedGuideFromPause)
        {
            if (guideCard != null) guideCard.AddToClassList("is-hidden");
            if (card != null) card.RemoveFromClassList("is-hidden");
            resumeButton?.Focus();
        }
        else
        {
            if (guideCard != null) guideCard.AddToClassList("is-hidden");
            Resume();
        }
    }

    void UnlockCursor()
    {
        if (cursorUnlocked) return;
        previousLockState = UnityEngine.Cursor.lockState;
        previousCursorVisible = UnityEngine.Cursor.visible;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
        cursorUnlocked = true;
    }

    void RestoreCursor()
    {
        if (!cursorUnlocked) return;
        UnityEngine.Cursor.lockState = previousLockState;
        UnityEngine.Cursor.visible = previousCursorVisible;
        cursorUnlocked = false;
    }

    // ------------------------------------------------------------- safe area

    /// Keeps the corner HUD buttons inside the device safe area (notch, camera
    /// cut-out, rounded corners). Screen.safeArea is in screen pixels while the
    /// panel lays out in scaled panel units, so the insets are converted with the
    /// panel scale factor. Re-applied when the inset changes - rotating the phone
    /// in landscape moves the cut-out from one side to the other.
    void UpdateSafeArea()
    {
        if (pauseButton == null) return;

        Rect safeArea = Screen.safeArea;
        if (safeArea.width <= 0f || safeArea.height <= 0f)
            safeArea = new Rect(0f, 0f, Screen.width, Screen.height); // bogus reading: assume full screen

        if (safeArea == appliedSafeArea &&
            Screen.width == appliedScreenWidth &&
            Screen.height == appliedScreenHeight)
            return;

        float panelWidth = root.resolvedStyle.width;
        float panelHeight = root.resolvedStyle.height;
        if (float.IsNaN(panelWidth) || float.IsNaN(panelHeight) ||
            panelWidth <= 0f || panelHeight <= 0f)
            return; // layout not resolved yet, retry next frame

        appliedSafeArea = safeArea;
        appliedScreenWidth = Screen.width;
        appliedScreenHeight = Screen.height;

        float toPanelX = panelWidth / Mathf.Max(1f, Screen.width);
        float toPanelY = panelHeight / Mathf.Max(1f, Screen.height);

        // Screen.safeArea is bottom-left based, UI Toolkit positions from top-left.
        float insetRight = Mathf.Max(0f, (Screen.width - safeArea.xMax) * toPanelX);
        float insetTop = Mathf.Max(0f, (Screen.height - safeArea.yMax) * toPanelY);

        appliedInsetRight = insetRight;
        appliedInsetTop = insetTop;

        float rightPos = HudButtonMargin + insetRight;
        float topPos = HudButtonMargin + insetTop;

        pauseButton.style.right = rightPos;
        pauseButton.style.top = topPos;

        if (guideButton != null)
        {
            guideButton.style.right = rightPos + 68f;
            guideButton.style.top = topPos;
        }
    }

    // ------------------------------------------------------------- UI build

    /// Builds the whole overlay in code so the scene only needs this component.
    void BuildUI()
    {
        font = uiFont != null ? uiFont : LoadFont();

        panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
        if (panelSettings == null)
        {
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "Pause Menu Panel Settings";
            panelSettings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
            panelSettings.sortingOrder = 100;
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
        }

        document = gameObject.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;

        root = document.rootVisualElement;
        root.AddToClassList("pause-root");

        StyleSheet sheet = Resources.Load<StyleSheet>("Pause menu style");
        if (sheet != null) root.styleSheets.Add(sheet);
        else Debug.LogWarning("PauseMenuController: 'Resources/Pause menu style' stylesheet not found.", this);

        // corner pause / settings button
        pauseButton = new Button(TogglePause);
        pauseButton.AddToClassList("pause-fab");
        VisualElement gearIcon = new VisualElement();
        gearIcon.AddToClassList("pause-fab-icon");
        gearIcon.pickingMode = PickingMode.Ignore;
        pauseButton.Add(gearIcon);
        root.Add(pauseButton);

        // corner guide / help button
        if (showGuideButton)
        {
            guideButton = new Button(() => OpenGuide(fromPauseMenu: false));
            guideButton.AddToClassList("guide-fab");
            Label qMark = MakeLabel("?", "guide-fab-text");
            qMark.pickingMode = PickingMode.Ignore;
            guideButton.Add(qMark);
            root.Add(guideButton);
        }

        // overlay + card
        overlay = new VisualElement();
        overlay.AddToClassList("pause-overlay");
        root.Add(overlay);

        card = new VisualElement();
        card.AddToClassList("pause-card");
        overlay.Add(card);

        card.Add(MakeLabel(headingLabel, "pause-heading"));
        card.Add(MakeLabel(pausedSubtitle, "pause-subheading"));

        resumeButton = MakeButton(resumeLabel, Resume);
        card.Add(resumeButton);

        card.Add(MakeDivider());

        if (showSoundSlider)
            card.Add(BuildSettingRow("SOUND", GameSettings.MasterVolume, out _, out _,
                v => GameSettings.SetMasterVolume(v)));

        if (showMusicSlider)
            card.Add(BuildSettingRow("MUSIC", GameSettings.MusicVolume, out _, out _,
                ApplyMusicVolume));

        if (showBrightnessSlider)
            card.Add(BuildSettingRow("BRIGHTNESS", GameSettings.Brightness, out _, out _,
                v => GameSettings.SetBrightness(v)));

        if (showSensitivitySlider)
            card.Add(BuildSettingRow("LOOK SENSITIVITY", GameSettings.Sensitivity, out _, out _,
                v => GameSettings.SetSensitivity(v),
                minValue: 10f, maxValue: 200f, isSensitivity: true));

        if (showGraphicsOption)
            card.Add(BuildGraphicsSelector());

        if (showFrameRateOption)
            card.Add(BuildFrameRateSelector());

        controlsCard = BuildControlsCard(card);
        overlay.Add(controlsCard);

        guideCard = BuildGuideCard();
        overlay.Add(guideCard);

        if (showControlsButton)
        {
            card.Add(MakeButton("SURVIVAL MANUAL & CONTROLS", () =>
            {
                OpenGuide(fromPauseMenu: true);
            }));
        }

        card.Add(MakeDivider());

        card.Add(MakeButton(exitLabel, OnExitClicked, danger: true));

        ShowMenu(false);
    }

    /// A labelled slider row with a live percentage readout.
    VisualElement BuildSettingRow(string labelText, float initialValue,
        out Label valueLabel, out Slider slider, Action<float> onChanged,
        float minValue = 0f, float maxValue = 100f, bool isSensitivity = false)
    {
        VisualElement setting = new VisualElement();
        setting.AddToClassList("pause-setting");

        VisualElement row = new VisualElement();
        row.AddToClassList("pause-setting-row");
        setting.Add(row);

        row.Add(MakeLabel(labelText, "pause-setting-label"));

        float displayVal = isSensitivity ? initialValue * 100f : initialValue * 100f;
        Label valueText = MakeLabel(Mathf.RoundToInt(displayVal) + "%", "pause-setting-value");
        row.Add(valueText);
        valueLabel = valueText;

        Slider settingSlider = new Slider(minValue, maxValue);
        settingSlider.AddToClassList("pause-slider");
        settingSlider.SetValueWithoutNotify(displayVal);
        setting.Add(settingSlider);
        slider = settingSlider;

        settingSlider.RegisterValueChangedCallback(evt =>
        {
            float v = isSensitivity ? Mathf.Clamp(evt.newValue / 100f, 0.1f, 2f) : Mathf.Clamp01(evt.newValue / 100f);
            valueText.text = Mathf.RoundToInt(evt.newValue) + "%";
            onChanged(v);
        });

        return setting;
    }

    VisualElement BuildGraphicsSelector()
    {
        VisualElement setting = new VisualElement();
        setting.AddToClassList("pause-setting");

        VisualElement row = new VisualElement();
        row.AddToClassList("pause-setting-row");
        setting.Add(row);

        row.Add(MakeLabel("GRAPHICS QUALITY", "pause-setting-label"));

        VisualElement selectorBar = new VisualElement();
        selectorBar.AddToClassList("pause-segmented-bar");

        string[] names = QualitySettings.names;
        if (names != null && names.Length > 0)
        {
            int current = GameSettings.QualityLevel >= 0
                ? GameSettings.QualityLevel
                : QualitySettings.GetQualityLevel();
            current = Mathf.Clamp(current, 0, names.Length - 1);

            var buttons = new System.Collections.Generic.List<Button>();
            for (int i = 0; i < names.Length; i++)
            {
                int qualityIndex = i;
                Button btn = new Button();
                btn.text = names[i].ToUpper();
                btn.AddToClassList("pause-segmented-pill");
                if (font != null) btn.style.unityFont = font;
                if (qualityIndex == current) btn.AddToClassList("is-pause-pill-active");

                btn.clicked += () =>
                {
                    foreach (var b in buttons) b.RemoveFromClassList("is-pause-pill-active");
                    btn.AddToClassList("is-pause-pill-active");
                    GameSettings.SetQualityLevel(qualityIndex);
                };

                buttons.Add(btn);
                selectorBar.Add(btn);
            }
        }
        setting.Add(selectorBar);
        return setting;
    }

    VisualElement BuildFrameRateSelector()
    {
        VisualElement setting = new VisualElement();
        setting.AddToClassList("pause-setting");

        VisualElement row = new VisualElement();
        row.AddToClassList("pause-setting-row");
        setting.Add(row);

        row.Add(MakeLabel("TARGET FPS", "pause-setting-label"));

        VisualElement selectorBar = new VisualElement();
        selectorBar.AddToClassList("pause-segmented-bar");

        var options = new string[] { "30 FPS", "60 FPS", "MAX" };
        var fpsValues = new int[] { 30, 60, -1 };

        int currentFps = GameSettings.TargetFrameRate;
        int selectedIndex = 1;
        if (currentFps == 30) selectedIndex = 0;
        else if (currentFps <= 0 || currentFps > 60) selectedIndex = 2;

        var buttons = new System.Collections.Generic.List<Button>();
        for (int i = 0; i < options.Length; i++)
        {
            int fpsIndex = i;
            Button btn = new Button();
            btn.text = options[i];
            btn.AddToClassList("pause-segmented-pill");
            if (font != null) btn.style.unityFont = font;
            if (fpsIndex == selectedIndex) btn.AddToClassList("is-pause-pill-active");

            btn.clicked += () =>
            {
                foreach (var b in buttons) b.RemoveFromClassList("is-pause-pill-active");
                btn.AddToClassList("is-pause-pill-active");
                GameSettings.SetTargetFrameRate(fpsValues[fpsIndex]);
            };

            buttons.Add(btn);
            selectorBar.Add(btn);
        }

        setting.Add(selectorBar);
        return setting;
    }

    VisualElement BuildControlsCard(VisualElement mainCard)
    {
        VisualElement controlsCard = new VisualElement();
        controlsCard.AddToClassList("pause-card");
        controlsCard.AddToClassList("is-hidden");

        controlsCard.Add(MakeLabel("CONTROLS", "pause-heading"));
        bool isMobile = PlatformUtils.IsMobile;
        controlsCard.Add(MakeLabel(isMobile ? "MOBILE TOUCH CONTROLS" : "KEYBOARD & MOUSE", "pause-subheading"));
        controlsCard.Add(MakeDivider());

        if (isMobile)
        {
            controlsCard.Add(MakeControlRow("LEFT JOYSTICK", "MOVE AROUND"));
            controlsCard.Add(MakeControlRow("RIGHT SCREEN", "SWIPE TO LOOK"));
            controlsCard.Add(MakeControlRow("FLASHLIGHT ICON", "TOGGLE FLASHLIGHT"));
            controlsCard.Add(MakeControlRow("SPRINT ICON", "HOLD TO SPRINT"));
            controlsCard.Add(MakeControlRow("TAP PROMPT / CHALK", "TAKE WATER / DRAW CHALK"));
            controlsCard.Add(MakeControlRow("CHALK BUTTON", "DRAW OR CYCLE MARK (X, ->, V)"));
            controlsCard.Add(MakeControlRow("TOP RIGHT GEAR", "PAUSE & SETTINGS"));
        }
        else
        {
            controlsCard.Add(MakeControlRow("W A S D", "MOVE"));
            controlsCard.Add(MakeControlRow("MOUSE", "LOOK AROUND"));
            controlsCard.Add(MakeControlRow("E", "TAKE ALMOND WATER / INTERACT"));
            controlsCard.Add(MakeControlRow("Q", "DRINK ALMOND WATER (RESTORES SANITY)"));
            controlsCard.Add(MakeControlRow("C", "DRAW CHALK MARK ON WALL"));
            controlsCard.Add(MakeControlRow("V", "CYCLE CHALK SYMBOL (X, ->, V)"));
            controlsCard.Add(MakeControlRow("F", "FLASHLIGHT"));
            controlsCard.Add(MakeControlRow("SHIFT", "SPRINT"));
            controlsCard.Add(MakeControlRow("SPACE", "JUMP"));
            controlsCard.Add(MakeControlRow("ESC", "PAUSE / SETTINGS"));
        }

        controlsCard.Add(MakeDivider());
        Button backBtn = MakeButton("BACK", () =>
        {
            controlsCard.AddToClassList("is-hidden");
            mainCard.RemoveFromClassList("is-hidden");
        });
        controlsCard.Add(backBtn);

        return controlsCard;
    }

    VisualElement MakeControlRow(string key, string desc)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("pause-control-row");

        Label keyLabel = MakeLabel(key, "pause-control-key");
        Label descLabel = MakeLabel(desc, "pause-control-desc");

        row.Add(keyLabel);
        row.Add(descLabel);
        return row;
    }

    VisualElement BuildGuideCard()
    {
        VisualElement gCard = new VisualElement();
        gCard.AddToClassList("guide-card");
        gCard.AddToClassList("is-hidden");

        gCard.Add(MakeLabel("SURVIVAL MANUAL", "pause-heading"));
        gCard.Add(MakeLabel("LEVEL 0 // ARCHIVAL RECONNAISSANCE PROTOCOLS", "pause-subheading"));

        // Tab row
        VisualElement tabs = new VisualElement();
        tabs.AddToClassList("guide-tabs");
        gCard.Add(tabs);

        Button tabObjective = new Button { text = "OBJECTIVE" };
        tabObjective.AddToClassList("guide-tab-btn");
        tabObjective.AddToClassList("guide-tab-btn--active");
        ApplyFont(tabObjective);
        tabs.Add(tabObjective);

        Button tabControls = new Button { text = "CONTROLS" };
        tabControls.AddToClassList("guide-tab-btn");
        ApplyFont(tabControls);
        tabs.Add(tabControls);

        Button tabSurvival = new Button { text = "SURVIVAL RULES" };
        tabSurvival.AddToClassList("guide-tab-btn");
        ApplyFont(tabSurvival);
        tabs.Add(tabSurvival);

        // Scroll container
        ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.AddToClassList("guide-scroll");
        gCard.Add(scroll);

        // --- SECTION 1: OBJECTIVE ---
        VisualElement secObjective = new VisualElement();
        secObjective.AddToClassList("guide-section");
        scroll.Add(secObjective);

        secObjective.Add(MakeGuideBox(
            "PRIMARY MISSION: ESCAPE LEVEL 0",
            "You have no-clipped into Level 0 ('The Lobby'). Your objective is to navigate the yellow labyrinth, locate the glowing anomalous Exit Threshold, and cross it to escape. Avoid entity contact at all costs.",
            false));

        secObjective.Add(MakeGuideBox(
            "AUDIO BEACON: LISTEN FOR THE ELECTRICAL HUM",
            "The exit threshold emits a continuous 3D electrical humming sound. When you are within 18 meters, you will hear this hum. The louder it gets, the closer you are to the exit door!",
            false));

        secObjective.Add(MakeGuideBox(
            "CORRIDOR NAVIGATION & LOOPS",
            "The map is composed of interconnected rooms, loops, and dead-ends. Keep track of your turns. If an entity spots you, avoid cornering yourself into dead-end branches.",
            false));

        // --- SECTION 2: CONTROLS ---
        VisualElement secControls = new VisualElement();
        secControls.AddToClassList("guide-section");
        secControls.AddToClassList("is-hidden");
        scroll.Add(secControls);

        bool isMobile = PlatformUtils.IsMobile;

        secControls.Add(MakeGuideBox(
            isMobile ? "MOBILE TOUCH CONTROLS" : "KEYBOARD & MOUSE CONTROLS",
            isMobile 
                ? "Controls are calibrated for touchscreens with full device notch / safe-area support."
                : "Standard first-person PC controls with mouse aiming.",
            false));

        if (isMobile)
        {
            secControls.Add(MakeControlRow("LEFT JOYSTICK", "DRAG THUMB TO MOVE"));
            secControls.Add(MakeControlRow("RIGHT SCREEN", "SWIPE ANYWHERE TO AIM CAMERA"));
            secControls.Add(MakeControlRow("TORCH ICON", "TOGGLE FLASHLIGHT ON / OFF"));
            secControls.Add(MakeControlRow("SPRINT ICON", "HOLD TO RUN (WARNING: LOUD NOISE!)"));
            secControls.Add(MakeControlRow("TAP PROMPT", "TAKE ALMOND WATER / INTERACT"));
            secControls.Add(MakeControlRow("ALMOND BUTTON", "DRINK ALMOND WATER (RESTORES SANITY)"));
            secControls.Add(MakeControlRow("CHALK BUTTON", "DRAW CHALK MARK / CYCLE MARK"));
            secControls.Add(MakeControlRow("GEAR ICON [⚙]", "PAUSE, SENSITIVITY, SOUND & VIDEO"));
            secControls.Add(MakeControlRow("HELP ICON [?]", "REOPEN THIS SURVIVAL MANUAL"));
        }
        else
        {
            secControls.Add(MakeControlRow("W A S D", "MOVE AROUND CORRIDORS"));
            secControls.Add(MakeControlRow("MOUSE", "AIM & LOOK AROUND"));
            secControls.Add(MakeControlRow("E", "TAKE ALMOND WATER / INTERACT"));
            secControls.Add(MakeControlRow("Q", "DRINK ALMOND WATER (RESTORES SANITY)"));
            secControls.Add(MakeControlRow("C", "DRAW CHALK MARK ON WALL"));
            secControls.Add(MakeControlRow("V", "CYCLE CHALK SYMBOL (X, ->, V)"));
            secControls.Add(MakeControlRow("F", "TOGGLE FLASHLIGHT"));
            secControls.Add(MakeControlRow("SHIFT (HOLD)", "SPRINT (WARNING: LOUD NOISE!)"));
            secControls.Add(MakeControlRow("SPACE", "JUMP"));
            secControls.Add(MakeControlRow("ESC", "PAUSE MENU"));
        }

        // --- SECTION 3: SURVIVAL RULES ---
        VisualElement secSurvival = new VisualElement();
        secSurvival.AddToClassList("guide-section");
        secSurvival.AddToClassList("is-hidden");
        scroll.Add(secSurvival);

        secSurvival.Add(MakeGuideBox(
            "RULE 1: FLASHLIGHT ATTRACTS THE ENTITY",
            "CRITICAL: Turning your flashlight ON doubles the entity's detection range from 12m to 24m and widens its field-of-view to 150°! Entities spot light beams across long corridors. Keep your light OFF when traveling—flick it on only briefly to inspect room entrances.",
            true));

        secSurvival.Add(MakeGuideBox(
            "RULE 2: SPRINTING RADIATES NOISE THROUGH WALLS",
            "CRITICAL: Sprinting sends heavy carpet vibrations in a 15-meter radius through walls. Entities immediately stalk toward footstep noise. Walk quietly (3.5m noise radius) whenever possible. Only sprint to break away during active chases!",
            true));

        secSurvival.Add(MakeGuideBox(
            "RULE 3: CORRIDOR EVASION & FLANKING",
            "The entity anticipates player movement in loops. When running away, take two sharp turns around consecutive corners, then STOP sprinting. The monster will stop and search your last seen position instead of cutting you off ahead.",
            false));

        secSurvival.Add(MakeGuideBox(
            "RULE 4: FLOODED / WET CARPET ZONES",
            "CRITICAL: Murky stagnant water pools cover certain sectors. Footsteps in water produce loud wet splashes that radiate noise up to 26 meters away, instantly drawing entities from adjacent wings. Tread cautiously through flooded rooms!",
            true));

        secSurvival.Add(MakeGuideBox(
            "RULE 5: NAVIGATIONAL CHALK MARKINGS",
            "Draw chalk markings on walls ([C] or mobile Chalk button) to mark dead ends with 'X', path ways with arrows, or cleared sectors with checkmarks ([V] to cycle). Essential for surviving the labyrinth.",
            false));

        // Tab click events
        tabObjective.clicked += () =>
        {
            tabObjective.AddToClassList("guide-tab-btn--active");
            tabControls.RemoveFromClassList("guide-tab-btn--active");
            tabSurvival.RemoveFromClassList("guide-tab-btn--active");

            secObjective.RemoveFromClassList("is-hidden");
            secControls.AddToClassList("is-hidden");
            secSurvival.AddToClassList("is-hidden");
        };

        tabControls.clicked += () =>
        {
            tabObjective.RemoveFromClassList("guide-tab-btn--active");
            tabControls.AddToClassList("guide-tab-btn--active");
            tabSurvival.RemoveFromClassList("guide-tab-btn--active");

            secObjective.AddToClassList("is-hidden");
            secControls.RemoveFromClassList("is-hidden");
            secSurvival.AddToClassList("is-hidden");
        };

        tabSurvival.clicked += () =>
        {
            tabObjective.RemoveFromClassList("guide-tab-btn--active");
            tabControls.RemoveFromClassList("guide-tab-btn--active");
            tabSurvival.AddToClassList("guide-tab-btn--active");

            secObjective.AddToClassList("is-hidden");
            secControls.AddToClassList("is-hidden");
            secSurvival.RemoveFromClassList("is-hidden");
        };

        // Footer
        VisualElement footer = new VisualElement();
        footer.AddToClassList("guide-footer");
        gCard.Add(footer);

        Button replaySubtitlesBtn = MakeButton("PLAY HUD SUBTITLES", () =>
        {
            CloseGuide();
            Resume();
            SurvivalDialogueHUD.Instance?.ReplaySurvivalIntro();
        });
        replaySubtitlesBtn.style.marginTop = 0;
        replaySubtitlesBtn.style.height = 42;
        replaySubtitlesBtn.style.minWidth = 180;
        footer.Add(replaySubtitlesBtn);

        guideCloseButton = MakeButton("RETURN TO PAUSE", CloseGuide);
        guideCloseButton.style.marginTop = 0;
        guideCloseButton.style.height = 42;
        guideCloseButton.style.minWidth = 180;
        footer.Add(guideCloseButton);

        return gCard;
    }

    VisualElement MakeGuideBox(string title, string desc, bool isDanger)
    {
        VisualElement box = new VisualElement();
        box.AddToClassList("guide-box");
        if (isDanger) box.AddToClassList("guide-box--danger");

        Label titleLabel = MakeLabel(title, "guide-box-title");
        if (isDanger) titleLabel.AddToClassList("guide-box-title--danger");
        box.Add(titleLabel);

        Label descLabel = MakeLabel(desc, "guide-box-desc");
        box.Add(descLabel);

        return box;
    }

    /// Music slider: saves through GameSettings and updates the live ambience.
    void ApplyMusicVolume(float value)
    {
        GameSettings.SetMusicVolume(value);

        if (ambience == null)
            ambience = FindAnyObjectByType<BackroomsAmbience>(FindObjectsInactive.Include);
        if (ambience != null)
            ambience.SetVolume(value);
    }

    Label MakeLabel(string text, string ussClass)
    {
        Label label = new Label(text);
        if (!string.IsNullOrEmpty(ussClass)) label.AddToClassList(ussClass);
        ApplyFont(label);
        return label;
    }

    Button MakeButton(string text, Action onClick, bool danger = false)
    {
        Button button = new Button(onClick) { text = text };
        button.AddToClassList("pause-button");
        if (danger) button.AddToClassList("pause-button--danger");
        ApplyFont(button);
        return button;
    }

    VisualElement MakeDivider()
    {
        VisualElement divider = new VisualElement();
        divider.AddToClassList("pause-divider");
        return divider;
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
