using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

/// <summary>
/// Drives the main menu: wires the buttons, fills in the version block, runs the
/// liminal flicker / typing effects and switches between the menu and its
/// settings / controls overlays.
/// Attach it to the same GameObject as the <see cref="UIDocument"/>.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class MainMenuController : MonoBehaviour
{
    [Header("Scene")]
    [Tooltip("Name of the scene that is loaded when the player picks ENTER.")]
    [SerializeField] string gameSceneName = "MainLevel";

    [Header("Look")]
    [Tooltip("Optional monospace font. When empty the font is loaded from Resources.")]
    [SerializeField] Font uiFont;
    [SerializeField] string fontResourcePath = "Fonts/RobotoMono-Regular";
    [Tooltip("If true, displays the high-resolution graphic title logo texture instead of the typed text.")]
    [SerializeField] bool useGraphicTitleLogo = false;

    [Header("Effects")]
    [SerializeField] bool typingEffect = true;
    [SerializeField] float typingSpeed = 0.06f;
    [SerializeField] bool flickerEffect = true;
    [SerializeField] float fadeOutDuration = 0.35f;

    [Header("Music")]
    [Tooltip("Specific audio clip to play for the main menu. If not assigned, an authentic Backrooms Lobby soundtrack is loaded automatically.")]
    [SerializeField] AudioClip menuMusicClip;
    [Tooltip("If true, loops the menu soundtrack.")]
    [SerializeField] bool loopMusic = true;
    [SerializeField] float musicFadeDuration = 1.0f;
    [SerializeField] string levelSoundtrackName = "backrooms level0";

    UIDocument document;
    VisualElement root;

    Label titleLabel;
    Label subtitleLabel;
    VisualElement titleLogo;
    Label versionLabel;
    Label buildLabel;
    Label volumeValueLabel;
    Label musicValueLabel;
    Label sensitivityValueLabel;
    Label brightnessValueLabel;
    Label recLabel;
    Label hintLabel;
    VisualElement scanlines;
    VisualElement leftColumn;
    VisualElement versionBlock;
    VisualElement statusBlock;

    Button continueButton;
    VisualElement continueMenuItem;
    Button playButton;
    Button settingsButton;
    Button controlsButton;
    Button quitButton;
    Button settingsCloseButton;
    Button controlsCloseButton;
    Button tabTouch;
    Button tabKeyboard;

    VisualElement controlsTabs;
    VisualElement settingsPanel;
    VisualElement controlsPanel;
    VisualElement touchContainer;
    VisualElement keyboardContainer;
    Slider volumeSlider;
    Slider musicSlider;
    Slider sensitivitySlider;
    Slider brightnessSlider;
    VisualElement graphicsSelector;
    VisualElement framerateSelector;
    DropdownField graphicsDropdown;
    DropdownField framerateDropdown;

    Rect appliedSafeArea;
    int appliedScreenWidth;
    int appliedScreenHeight;

    string titleText;
    string subtitleText;
    Coroutine flickerRoutine;
    AudioSource menuMusic;

    void Awake()
    {
        document = GetComponent<UIDocument>();
        root = document.rootVisualElement;

        titleLabel = root.Q<Label>("title");
        subtitleLabel = root.Q<Label>("subtitle");
        titleLogo = root.Q<VisualElement>("title-logo");
        versionLabel = root.Q<Label>("version-label");
        buildLabel = root.Q<Label>("build-label");
        volumeValueLabel = root.Q<Label>("volume-value");
        musicValueLabel = root.Q<Label>("music-value");
        sensitivityValueLabel = root.Q<Label>("sensitivity-value");
        brightnessValueLabel = root.Q<Label>("brightness-value");
        recLabel = root.Q<Label>("rec-label");
        hintLabel = root.Q<Label>("hint");
        scanlines = root.Q<VisualElement>("fx-scanlines");
        leftColumn = root.Q<VisualElement>("left-column");
        versionBlock = root.Q<VisualElement>("version-block");
        statusBlock = root.Q<VisualElement>("status-block");

        continueButton = root.Q<Button>("continue-button");
        continueMenuItem = root.Q<VisualElement>("menu-item-continue");
        playButton = root.Q<Button>("play-button");
        settingsButton = root.Q<Button>("settings-button");
        controlsButton = root.Q<Button>("controls-button");
        quitButton = root.Q<Button>("quit-button");
        settingsCloseButton = root.Q<Button>("settings-close-button");
        controlsCloseButton = root.Q<Button>("controls-close-button");
        tabTouch = root.Q<Button>("tab-touch");
        tabKeyboard = root.Q<Button>("tab-keyboard");
        controlsTabs = root.Q<VisualElement>("controls-tabs");

        settingsPanel = root.Q<VisualElement>("settings-panel");
        controlsPanel = root.Q<VisualElement>("controls-panel");
        touchContainer = root.Q<VisualElement>("touch-controls-container");
        keyboardContainer = root.Q<VisualElement>("keyboard-controls-container");
        volumeSlider = root.Q<Slider>("volume-slider");
        musicSlider = root.Q<Slider>("music-slider");
        sensitivitySlider = root.Q<Slider>("sensitivity-slider");
        brightnessSlider = root.Q<Slider>("brightness-slider");
        graphicsSelector = root.Q<VisualElement>("graphics-selector");
        framerateSelector = root.Q<VisualElement>("framerate-selector");
        graphicsDropdown = root.Q<DropdownField>("graphics-dropdown");
        framerateDropdown = root.Q<DropdownField>("framerate-dropdown");

        if (titleLabel != null) titleText = titleLabel.text;
        if (subtitleLabel != null) subtitleText = subtitleLabel.text;
    }

    void OnEnable()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        GameProgressManager.OnProgressChanged += UpdateCheckpointUI;

        ApplyFont();
        FillVersionBlock();
        WireButtons();
        WireSettings();
        StartMenuMusic();

        if (titleLogo != null)
        {
            if (useGraphicTitleLogo)
            {
                titleLogo.RemoveFromClassList("is-hidden");
                titleLogo.style.display = DisplayStyle.Flex;
            }
            else
            {
                titleLogo.AddToClassList("is-hidden");
                titleLogo.style.display = DisplayStyle.None;
            }
        }

        if (titleLabel != null)
        {
            titleLabel.style.display = useGraphicTitleLogo ? DisplayStyle.None : DisplayStyle.Flex;
        }

        if (subtitleLabel != null)
        {
            subtitleLabel.style.display = useGraphicTitleLogo ? DisplayStyle.None : DisplayStyle.Flex;
        }

        if (!useGraphicTitleLogo && typingEffect && (titleLabel != null || subtitleLabel != null))
            StartCoroutine(TypeTitle());

        if (flickerEffect) flickerRoutine = StartCoroutine(Flicker());
    }

    void OnDisable()
    {
        GameProgressManager.OnProgressChanged -= UpdateCheckpointUI;

        if (flickerRoutine != null) StopCoroutine(flickerRoutine);
        flickerRoutine = null;

        if (menuMusic != null) menuMusic.Stop();
    }

    // ------------------------------------------------------------------ look

    /// <summary>
    /// Applies the monospace font to every text element so the menu reads like
    /// an old terminal. Falls back to the default font when nothing is found.
    /// </summary>
    void ApplyFont()
    {
        Font font = uiFont != null ? uiFont : LoadFont();
        if (font == null) return;

        var definition = new StyleFontDefinition(font);
        foreach (TextElement text in root.Query<TextElement>().ToList())
            text.style.unityFontDefinition = definition;
    }

    Font LoadFont()
    {
        if (string.IsNullOrEmpty(fontResourcePath)) return null;
        return Resources.Load<Font>(fontResourcePath);
    }

    void FillVersionBlock()
    {
        if (versionLabel != null)
            versionLabel.text = "v" + Application.version;

        if (buildLabel != null)
            buildLabel.text = "BUILD " + Application.unityVersion;
    }

    // --------------------------------------------------------------- buttons

    void WireButtons()
    {
        WireButtonRow(continueButton, OnContinueClicked);
        WireButtonRow(playButton, OnPlayClicked);
        WireButtonRow(quitButton, OnQuitClicked);
        WireButtonRow(settingsButton, OnSettingsClicked);
        WireButtonRow(controlsButton, OnControlsClicked);

        UpdateCheckpointUI();

        if (settingsCloseButton != null) settingsCloseButton.clicked += CloseSettings;
        if (controlsCloseButton != null) controlsCloseButton.clicked += CloseControls;

        if (tabTouch != null) tabTouch.clicked += () => SwitchControlsTab(touchMode: true);
        if (tabKeyboard != null) tabKeyboard.clicked += () => SwitchControlsTab(touchMode: false);

        UpdatePlatformControls();
    }

    void UpdatePlatformControls()
    {
        bool isMobile = PlatformUtils.IsMobile;
        if (isMobile)
        {
            // On mobile (Android / iOS): show touch controls directly, hide tabs and keyboard controls
            if (controlsTabs != null) controlsTabs.style.display = DisplayStyle.None;
            if (tabKeyboard != null) tabKeyboard.style.display = DisplayStyle.None;
            if (tabTouch != null) tabTouch.style.display = DisplayStyle.Flex;
            SwitchControlsTab(touchMode: true);

            if (hintLabel != null)
                hintLabel.text = "TAP A CHOICE TO SELECT";
        }
        else
        {
            // On PC / Desktop: hide touch tab and controls tabs bar so only PC keyboard/mouse controls show
            if (tabTouch != null) tabTouch.style.display = DisplayStyle.None;
            if (tabKeyboard != null) tabKeyboard.style.display = DisplayStyle.Flex;
            if (controlsTabs != null) controlsTabs.style.display = DisplayStyle.None;
            SwitchControlsTab(touchMode: false);

            if (hintLabel != null)
                hintLabel.text = "PRESS ENTER OR CLICK TO SELECT";
        }
    }

    void SwitchControlsTab(bool touchMode)
    {
        if (touchMode)
        {
            tabTouch?.AddToClassList("is-tab-active");
            tabKeyboard?.RemoveFromClassList("is-tab-active");

            if (touchContainer != null)
            {
                touchContainer.RemoveFromClassList("is-hidden");
                touchContainer.style.display = DisplayStyle.Flex;
            }
            if (keyboardContainer != null)
            {
                keyboardContainer.AddToClassList("is-hidden");
                keyboardContainer.style.display = DisplayStyle.None;
            }
        }
        else
        {
            tabKeyboard?.AddToClassList("is-tab-active");
            tabTouch?.RemoveFromClassList("is-tab-active");

            if (keyboardContainer != null)
            {
                keyboardContainer.RemoveFromClassList("is-hidden");
                keyboardContainer.style.display = DisplayStyle.Flex;
            }
            if (touchContainer != null)
            {
                touchContainer.AddToClassList("is-hidden");
                touchContainer.style.display = DisplayStyle.None;
            }
        }
    }

    void WireButtonRow(Button button, System.Action onClick)
    {
        if (button == null) return;
        if (onClick != null) button.clicked += onClick;

        VisualElement row = button.parent;
        if (row == null) return;

        // Light up the ">" marker on hover, focus, or mobile touch down
        button.RegisterCallback<PointerEnterEvent>(_ => row.AddToClassList("is-active"));
        button.RegisterCallback<PointerLeaveEvent>(_ => row.RemoveFromClassList("is-active"));
        button.RegisterCallback<PointerDownEvent>(_ => row.AddToClassList("is-active"));
        button.RegisterCallback<PointerUpEvent>(_ => row.RemoveFromClassList("is-active"));
        button.RegisterCallback<FocusInEvent>(_ => row.AddToClassList("is-active"));
        button.RegisterCallback<FocusOutEvent>(_ => row.RemoveFromClassList("is-active"));

        // Allow tapping anywhere on the row (marker/padding) on mobile touch screens
        if (onClick != null)
        {
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target != button)
                {
                    button.Focus();
                    onClick();
                }
            });
        }
    }

    // ------------------------------------------------------------- settings

    /// Wires every control in the settings overlay to <see cref="GameSettings"/>.
    void WireSettings()
    {
        WireMasterVolume();
        WireMusicVolume();
        WireSensitivity();
        WireBrightness();
        WireGraphics();
        WireFrameRate();
    }

    void WireMasterVolume()
    {
        if (volumeSlider == null) return;

        float volume = GameSettings.MasterVolume;
        volumeSlider.SetValueWithoutNotify(volume * 100f);
        UpdatePercentLabel(volumeValueLabel, volume);

        volumeSlider.RegisterValueChangedCallback(evt =>
        {
            float value = Mathf.Clamp01(evt.newValue / 100f);
            GameSettings.SetMasterVolume(value);
            UpdatePercentLabel(volumeValueLabel, value);
        });
    }

    void WireMusicVolume()
    {
        if (musicSlider == null) return;

        float volume = GameSettings.MusicVolume;
        musicSlider.SetValueWithoutNotify(volume * 100f);
        UpdatePercentLabel(musicValueLabel, volume);

        musicSlider.RegisterValueChangedCallback(evt =>
        {
            float value = Mathf.Clamp01(evt.newValue / 100f);
            GameSettings.SetMusicVolume(value);
            if (menuMusic != null) menuMusic.volume = value;
            UpdatePercentLabel(musicValueLabel, value);
        });
    }

    /// Starts atmospheric Backrooms ambient music for the main menu.
    void StartMenuMusic()
    {
        if (menuMusic != null)
        {
            if (!menuMusic.isPlaying) menuMusic.Play();
            return;
        }

        AudioClip clipToPlay = menuMusicClip;

        if (clipToPlay == null)
        {
            // First choice: The Lobby soundtrack
            clipToPlay = Resources.Load<AudioClip>("SFX/juanjo_ sound - The Lobby");
        }

        if (clipToPlay == null)
        {
            // Search SFX specifically for dedicated soundtrack tracks
            AudioClip[] all = Resources.LoadAll<AudioClip>("SFX");
            foreach (AudioClip clip in all)
            {
                if (clip == null) continue;
                if (clip.name.IndexOf("The Lobby", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    clip.name.IndexOf("Pseudoliminality", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    clip.name.IndexOf("continuum", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    clip.name.IndexOf("The Bluerooms", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    clipToPlay = clip;
                    break;
                }
            }
        }

        if (clipToPlay == null)
        {
            // Fallback: only select real music/ambient tracks (> 45 seconds), NEVER sound effects/footsteps/roars
            AudioClip[] all = Resources.LoadAll<AudioClip>("SFX");
            foreach (AudioClip clip in all)
            {
                if (clip != null && clip.length > 45f && clip.name.IndexOf("level0 ambience", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    clipToPlay = clip;
                    break;
                }
            }
        }

        if (clipToPlay == null) return;

        menuMusic = gameObject.AddComponent<AudioSource>();
        menuMusic.clip = clipToPlay;
        menuMusic.loop = loopMusic;
        menuMusic.playOnAwake = false;
        menuMusic.spatialBlend = 0f;
        menuMusic.volume = 0f;
        menuMusic.Play();

        StartCoroutine(FadeInMenuMusic(GameSettings.MusicVolume, musicFadeDuration));
    }

    IEnumerator FadeInMenuMusic(float targetVolume, float duration)
    {
        if (menuMusic == null) yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (menuMusic == null) yield break;
            elapsed += Time.unscaledDeltaTime;
            menuMusic.volume = Mathf.Lerp(0f, targetVolume, elapsed / duration);
            yield return null;
        }
        if (menuMusic != null) menuMusic.volume = targetVolume;
    }

    void WireSensitivity()
    {
        if (sensitivitySlider == null) return;

        float value = GameSettings.Sensitivity;
        sensitivitySlider.SetValueWithoutNotify(value * 100f);
        UpdatePercentLabel(sensitivityValueLabel, value);

        sensitivitySlider.RegisterValueChangedCallback(evt =>
        {
            float v = Mathf.Clamp(evt.newValue / 100f, 0.1f, 2f);   // 10%..200%
            GameSettings.SetSensitivity(v);
            UpdatePercentLabel(sensitivityValueLabel, v);
        });
    }

    void WireBrightness()
    {
        if (brightnessSlider == null) return;

        float value = GameSettings.Brightness;
        brightnessSlider.SetValueWithoutNotify(value * 100f);
        UpdatePercentLabel(brightnessValueLabel, value);

        brightnessSlider.RegisterValueChangedCallback(evt =>
        {
            float v = Mathf.Clamp01(evt.newValue / 100f);
            GameSettings.SetBrightness(v);          // saves and previews live
            UpdatePercentLabel(brightnessValueLabel, v);
        });
    }

    void WireGraphics()
    {
        string[] names = QualitySettings.names;
        if (names == null || names.Length == 0) return;

        int current = GameSettings.QualityLevel >= 0
            ? GameSettings.QualityLevel
            : QualitySettings.GetQualityLevel();
        current = Mathf.Clamp(current, 0, names.Length - 1);

        if (graphicsSelector != null)
        {
            graphicsSelector.Clear();
            var buttons = new List<Button>();
            for (int i = 0; i < names.Length; i++)
            {
                int qualityIndex = i;
                Button btn = new Button();
                btn.text = names[i].ToUpper();
                btn.AddToClassList("segmented-pill");
                if (qualityIndex == current) btn.AddToClassList("is-pill-active");

                btn.clicked += () =>
                {
                    foreach (var b in buttons) b.RemoveFromClassList("is-pill-active");
                    btn.AddToClassList("is-pill-active");
                    GameSettings.SetQualityLevel(qualityIndex);
                };

                buttons.Add(btn);
                graphicsSelector.Add(btn);
            }
        }
        else if (graphicsDropdown != null)
        {
            graphicsDropdown.choices = new List<string>(names);
            graphicsDropdown.SetValueWithoutNotify(names[current]);
            graphicsDropdown.RegisterValueChangedCallback(evt =>
            {
                int index = System.Array.IndexOf(names, evt.newValue);
                if (index >= 0) GameSettings.SetQualityLevel(index);
            });
        }
    }

    void WireFrameRate()
    {
        var options = new string[] { "30 FPS", "60 FPS", "MAX" };
        var fpsValues = new int[] { 30, 60, -1 };

        int currentFps = GameSettings.TargetFrameRate;
        int selectedIndex = 1; // default to 60
        if (currentFps == 30) selectedIndex = 0;
        else if (currentFps <= 0 || currentFps > 60) selectedIndex = 2;

        if (framerateSelector != null)
        {
            framerateSelector.Clear();
            var buttons = new List<Button>();
            for (int i = 0; i < options.Length; i++)
            {
                int fpsIndex = i;
                Button btn = new Button();
                btn.text = options[i];
                btn.AddToClassList("segmented-pill");
                if (fpsIndex == selectedIndex) btn.AddToClassList("is-pill-active");

                btn.clicked += () =>
                {
                    foreach (var b in buttons) b.RemoveFromClassList("is-pill-active");
                    btn.AddToClassList("is-pill-active");
                    GameSettings.SetTargetFrameRate(fpsValues[fpsIndex]);
                };

                buttons.Add(btn);
                framerateSelector.Add(btn);
            }
        }
        else if (framerateDropdown != null)
        {
            var optList = new List<string>(options);
            framerateDropdown.choices = optList;
            framerateDropdown.SetValueWithoutNotify(options[selectedIndex]);
            framerateDropdown.RegisterValueChangedCallback(evt =>
            {
                int idx = System.Array.IndexOf(options, evt.newValue);
                if (idx >= 0 && idx < fpsValues.Length)
                    GameSettings.SetTargetFrameRate(fpsValues[idx]);
            });
        }
    }

    void UpdatePercentLabel(Label label, float value)
    {
        if (label != null) label.text = Mathf.RoundToInt(value * 100f) + "%";
    }

    void UpdateCheckpointUI()
    {
        bool hasCheckpoint = GameProgressManager.HasCheckpoint();
        if (continueMenuItem != null)
        {
            if (hasCheckpoint)
            {
                continueMenuItem.style.display = DisplayStyle.Flex;
                continueMenuItem.RemoveFromClassList("is-hidden");
                int lvl = GameProgressManager.GetCheckpointLevelNumber();
                if (continueButton != null)
                {
                    continueButton.text = $"RESUME LEVEL {lvl}";
                }
            }
            else
            {
                continueMenuItem.style.display = DisplayStyle.None;
                continueMenuItem.AddToClassList("is-hidden");
            }
        }

        if (playButton != null)
        {
            playButton.text = hasCheckpoint ? "NEW RUN" : "ENTER";
        }
    }

    // ------------------------------------------------------------- behaviour

    void OnContinueClicked() => StartCoroutine(FadeOutThen(GameProgressManager.ResumeGame));

    void OnPlayClicked()
    {
        if (GameProgressManager.HasCheckpoint())
        {
            // Player chose to restart from Level 0
            StartCoroutine(FadeOutThen(GameProgressManager.StartNewGame));
        }
        else
        {
            StartCoroutine(FadeOutThen(LoadGame));
        }
    }

    void OnQuitClicked() => StartCoroutine(FadeOutThen(QuitGame));

    void LoadGame()
    {
        if (string.IsNullOrEmpty(gameSceneName)) return;
        SceneManager.LoadScene(gameSceneName);
    }

    void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnSettingsClicked()
    {
        controlsPanel?.AddToClassList("is-hidden");
        settingsPanel?.RemoveFromClassList("is-hidden");
        settingsCloseButton?.Focus();
    }

    void OnControlsClicked()
    {
        settingsPanel?.AddToClassList("is-hidden");
        controlsPanel?.RemoveFromClassList("is-hidden");

        UpdatePlatformControls();

        controlsCloseButton?.Focus();
    }

    void CloseSettings()
    {
        settingsPanel?.AddToClassList("is-hidden");
        settingsButton?.Focus();
    }

    void CloseControls()
    {
        controlsPanel?.AddToClassList("is-hidden");
        controlsButton?.Focus();
    }

    // --------------------------------------------------------------- effects

    IEnumerator TypeTitle()
    {
        if (titleLabel != null)
            titleLabel.text = string.Empty;

        if (subtitleLabel != null)
            subtitleLabel.text = string.Empty;

        if (titleLabel != null && !string.IsNullOrEmpty(titleText))
        {
            for (int i = 0; i < titleText.Length; i++)
            {
                titleLabel.text = titleText.Substring(0, i + 1) + "_";
                yield return new WaitForSecondsRealtime(typingSpeed);
            }
            titleLabel.text = titleText;
        }

        if (subtitleLabel != null && !string.IsNullOrEmpty(subtitleText))
        {
            yield return new WaitForSecondsRealtime(typingSpeed * 1.5f);
            for (int i = 0; i < subtitleText.Length; i++)
            {
                subtitleLabel.text = subtitleText.Substring(0, i + 1) + "_";
                yield return new WaitForSecondsRealtime(typingSpeed);
            }
            subtitleLabel.text = subtitleText;
        }
    }

    IEnumerator Flicker()
    {
        while (true)
        {
            float target = 1f;
            float roll = Random.value;

            if (roll > 0.94f) target = 0.62f;                       // hard dropout
            else if (roll > 0.78f) target = 0.86f + Random.value * 0.12f;

            int frames = Random.Range(1, 4);
            for (int i = 0; i < frames; i++)
            {
                SetOpacity(target);
                yield return null;
            }

            yield return new WaitForSecondsRealtime(Random.Range(0.05f, 0.7f));
        }
    }

    IEnumerator FadeOutThen(System.Action action)
    {
        if (flickerRoutine != null)
        {
            StopCoroutine(flickerRoutine);
            flickerRoutine = null;
        }

        float start = 1f;
        float elapsed = 0f;
        float startMusicVol = menuMusic != null ? menuMusic.volume : 0f;

        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / fadeOutDuration;
            SetOpacity(Mathf.Lerp(start, 0f, t));
            if (menuMusic != null)
            {
                menuMusic.volume = Mathf.Lerp(startMusicVol, 0f, t);
            }
            yield return null;
        }

        SetOpacity(0f);
        if (menuMusic != null)
        {
            menuMusic.volume = 0f;
            menuMusic.Stop();
        }
        action?.Invoke();
    }

    void SetOpacity(float value)
    {
        if (root != null) root.style.opacity = value;
    }

    void Update()
    {
        // slow drift of the CRT scanlines
        if (scanlines != null)
        {
            float offset = Mathf.Repeat(Time.unscaledTime * 9f, 3f);
            scanlines.style.translate = new Translate(0f, offset);
        }

        // blinking REC dot
        if (recLabel != null)
        {
            bool blink = Mathf.Repeat(Time.unscaledTime, 1.2f) < 0.75f;
            recLabel.style.opacity = blink ? 1f : 0.25f;
        }

        UpdateSafeArea();
    }

    void UpdateSafeArea()
    {
        Rect safeArea = Screen.safeArea;
        if (safeArea.width <= 0f || safeArea.height <= 0f)
            safeArea = new Rect(0f, 0f, Screen.width, Screen.height);

        if (safeArea == appliedSafeArea &&
            Screen.width == appliedScreenWidth &&
            Screen.height == appliedScreenHeight)
            return;

        if (root == null) return;
        float panelWidth = root.resolvedStyle.width;
        float panelHeight = root.resolvedStyle.height;
        if (float.IsNaN(panelWidth) || float.IsNaN(panelHeight) ||
            panelWidth <= 0f || panelHeight <= 0f)
            return;

        appliedSafeArea = safeArea;
        appliedScreenWidth = Screen.width;
        appliedScreenHeight = Screen.height;

        float toPanelX = panelWidth / Mathf.Max(1f, Screen.width);
        float toPanelY = panelHeight / Mathf.Max(1f, Screen.height);

        float insetLeft = Mathf.Max(0f, safeArea.xMin * toPanelX);
        float insetRight = Mathf.Max(0f, (Screen.width - safeArea.xMax) * toPanelX);
        float insetTop = Mathf.Max(0f, (Screen.height - safeArea.yMax) * toPanelY);
        float insetBottom = Mathf.Max(0f, safeArea.yMin * toPanelY);

        if (leftColumn != null)
            leftColumn.style.left = 80f + insetLeft;

        if (versionBlock != null)
        {
            versionBlock.style.right = 46f + insetRight;
            versionBlock.style.top = 34f + insetTop;
        }

        if (statusBlock != null)
        {
            statusBlock.style.left = 64f + insetLeft;
            statusBlock.style.bottom = 30f + insetBottom;
        }
    }
}
