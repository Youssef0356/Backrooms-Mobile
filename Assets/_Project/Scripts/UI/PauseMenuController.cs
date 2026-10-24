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
    [SerializeField] bool showSensitivitySlider = true;
    [SerializeField] bool showGraphicsOption = true;
    [SerializeField] bool showFrameRateOption = true;
    [SerializeField] bool showControlsButton = true;

    // ------------------------------------------------------------- pause button

    [Header("Pause button (mobile)")]
    [Tooltip("Show the on-screen pause button in the top right corner.")]
    [SerializeField] bool showPauseButton = true;

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
    Button resumeButton;

    // Safe area tracking for corner HUD buttons (notch / camera cut-out / rounded corners).
    // Expressed in panel units, same space the stylesheet uses.
    const float HudButtonMargin = 24f;
    Rect appliedSafeArea;
    int appliedScreenWidth;
    int appliedScreenHeight;

    BackroomsAmbience ambience;
    float previousTimeScale = 1f;
    CursorLockMode previousLockState;
    bool previousCursorVisible;
    bool cursorUnlocked;

    // ------------------------------------------------------------- lifecycle

    void Awake()
    {
        Instance = this;
        BuildUI();
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
    }

    void ShowMenu(bool show)
    {
        if (overlay != null)
            overlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

        if (pauseButton != null)
            pauseButton.style.display = (show || !showPauseButton) ? DisplayStyle.None : DisplayStyle.Flex;

        if (show) resumeButton?.Focus();
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

        float rightPos = HudButtonMargin + insetRight;
        float topPos = HudButtonMargin + insetTop;

        pauseButton.style.right = rightPos;
        pauseButton.style.top = topPos;
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

        // overlay + card
        overlay = new VisualElement();
        overlay.AddToClassList("pause-overlay");
        root.Add(overlay);

        VisualElement card = new VisualElement();
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

        if (showSensitivitySlider)
            card.Add(BuildSettingRow("LOOK SENSITIVITY", GameSettings.Sensitivity, out _, out _,
                v => GameSettings.SetSensitivity(v),
                minValue: 10f, maxValue: 200f, isSensitivity: true));

        if (showGraphicsOption)
            card.Add(BuildGraphicsDropdown());

        if (showFrameRateOption)
            card.Add(BuildFrameRateDropdown());

        VisualElement controlsCard = BuildControlsCard(card);
        overlay.Add(controlsCard);

        if (showControlsButton)
        {
            card.Add(MakeButton("CONTROLS", () =>
            {
                card.AddToClassList("is-hidden");
                controlsCard.RemoveFromClassList("is-hidden");
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

    VisualElement BuildGraphicsDropdown()
    {
        VisualElement setting = new VisualElement();
        setting.AddToClassList("pause-setting");

        VisualElement row = new VisualElement();
        row.AddToClassList("pause-setting-row");
        setting.Add(row);

        row.Add(MakeLabel("GRAPHICS", "pause-setting-label"));

        string[] names = QualitySettings.names;
        DropdownField dropdown = new DropdownField();
        dropdown.AddToClassList("pause-dropdown");
        if (names != null && names.Length > 0)
        {
            dropdown.choices = new System.Collections.Generic.List<string>(names);
            int current = GameSettings.QualityLevel >= 0
                ? GameSettings.QualityLevel
                : QualitySettings.GetQualityLevel();
            current = Mathf.Clamp(current, 0, names.Length - 1);
            dropdown.SetValueWithoutNotify(names[current]);
            dropdown.RegisterValueChangedCallback(evt =>
            {
                int index = Array.IndexOf(names, evt.newValue);
                if (index >= 0) GameSettings.SetQualityLevel(index);
            });
        }
        setting.Add(dropdown);
        return setting;
    }

    VisualElement BuildFrameRateDropdown()
    {
        VisualElement setting = new VisualElement();
        setting.AddToClassList("pause-setting");

        VisualElement row = new VisualElement();
        row.AddToClassList("pause-setting-row");
        setting.Add(row);

        row.Add(MakeLabel("TARGET FPS", "pause-setting-label"));

        var options = new System.Collections.Generic.List<string> { "30 FPS (Battery Saver)", "60 FPS (Default)", "Max / Uncapped" };
        int[] fpsValues = new int[] { 30, 60, -1 };

        DropdownField dropdown = new DropdownField();
        dropdown.AddToClassList("pause-dropdown");
        dropdown.choices = options;

        int currentFps = GameSettings.TargetFrameRate;
        int selectedIndex = 1;
        if (currentFps == 30) selectedIndex = 0;
        else if (currentFps <= 0 || currentFps > 60) selectedIndex = 2;

        dropdown.SetValueWithoutNotify(options[selectedIndex]);
        dropdown.RegisterValueChangedCallback(evt =>
        {
            int idx = options.IndexOf(evt.newValue);
            if (idx >= 0 && idx < fpsValues.Length)
                GameSettings.SetTargetFrameRate(fpsValues[idx]);
        });

        setting.Add(dropdown);
        return setting;
    }

    VisualElement BuildControlsCard(VisualElement mainCard)
    {
        VisualElement controlsCard = new VisualElement();
        controlsCard.AddToClassList("pause-card");
        controlsCard.AddToClassList("is-hidden");

        controlsCard.Add(MakeLabel("CONTROLS", "pause-heading"));
        bool isMobile = Application.isMobilePlatform || Input.touchSupported;
        controlsCard.Add(MakeLabel(isMobile ? "MOBILE TOUCH CONTROLS" : "KEYBOARD & MOUSE", "pause-subheading"));
        controlsCard.Add(MakeDivider());

        if (isMobile)
        {
            controlsCard.Add(MakeControlRow("LEFT JOYSTICK", "MOVE AROUND"));
            controlsCard.Add(MakeControlRow("RIGHT SCREEN", "SWIPE TO LOOK"));
            controlsCard.Add(MakeControlRow("FLASHLIGHT ICON", "TOGGLE FLASHLIGHT"));
            controlsCard.Add(MakeControlRow("SPRINT ICON", "HOLD TO SPRINT"));
            controlsCard.Add(MakeControlRow("TOP RIGHT GEAR", "PAUSE & SETTINGS"));
        }
        else
        {
            controlsCard.Add(MakeControlRow("W A S D", "MOVE"));
            controlsCard.Add(MakeControlRow("MOUSE", "LOOK AROUND"));
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
