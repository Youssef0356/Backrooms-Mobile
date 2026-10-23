using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

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

    [Header("Effects")]
    [SerializeField] bool typingEffect = true;
    [SerializeField] float typingSpeed = 0.06f;
    [SerializeField] bool flickerEffect = true;
    [SerializeField] float fadeOutDuration = 0.35f;

    [Header("Music")]
    [Tooltip("A random track from Resources/SFX plays on loop in the menu. Clips whose name contains this fragment are skipped, so the level's own soundtrack is not used here.")]
    [SerializeField] string levelSoundtrackName = "backrooms level0";

    UIDocument document;
    VisualElement root;

    Label titleLabel;
    Label versionLabel;
    Label buildLabel;
    Label volumeValueLabel;
    Label musicValueLabel;
    Label sensitivityValueLabel;
    Label brightnessValueLabel;
    Label recLabel;
    VisualElement scanlines;

    Button playButton;
    Button settingsButton;
    Button controlsButton;
    Button quitButton;
    Button settingsCloseButton;
    Button controlsCloseButton;

    VisualElement settingsPanel;
    VisualElement controlsPanel;
    Slider volumeSlider;
    Slider musicSlider;
    Slider sensitivitySlider;
    Slider brightnessSlider;
    DropdownField graphicsDropdown;
    DropdownField framerateDropdown;

    string titleText;
    Coroutine flickerRoutine;
    AudioSource menuMusic;

    void Awake()
    {
        document = GetComponent<UIDocument>();
        root = document.rootVisualElement;

        titleLabel = root.Q<Label>("title");
        versionLabel = root.Q<Label>("version-label");
        buildLabel = root.Q<Label>("build-label");
        volumeValueLabel = root.Q<Label>("volume-value");
        musicValueLabel = root.Q<Label>("music-value");
        sensitivityValueLabel = root.Q<Label>("sensitivity-value");
        brightnessValueLabel = root.Q<Label>("brightness-value");
        recLabel = root.Q<Label>("rec-label");
        scanlines = root.Q<VisualElement>("fx-scanlines");

        playButton = root.Q<Button>("play-button");
        settingsButton = root.Q<Button>("settings-button");
        controlsButton = root.Q<Button>("controls-button");
        quitButton = root.Q<Button>("quit-button");
        settingsCloseButton = root.Q<Button>("settings-close-button");
        controlsCloseButton = root.Q<Button>("controls-close-button");

        settingsPanel = root.Q<VisualElement>("settings-panel");
        controlsPanel = root.Q<VisualElement>("controls-panel");
        volumeSlider = root.Q<Slider>("volume-slider");
        musicSlider = root.Q<Slider>("music-slider");
        sensitivitySlider = root.Q<Slider>("sensitivity-slider");
        brightnessSlider = root.Q<Slider>("brightness-slider");
        graphicsDropdown = root.Q<DropdownField>("graphics-dropdown");
        framerateDropdown = root.Q<DropdownField>("framerate-dropdown");

        if (titleLabel != null) titleText = titleLabel.text;
    }

    void OnEnable()
    {
        ApplyFont();
        FillVersionBlock();
        WireButtons();
        WireSettings();
        StartMenuMusic();

        if (typingEffect && titleLabel != null) StartCoroutine(TypeTitle());
        if (flickerEffect) flickerRoutine = StartCoroutine(Flicker());
    }

    void OnDisable()
    {
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
        if (playButton != null) playButton.clicked += OnPlayClicked;
        if (quitButton != null) quitButton.clicked += OnQuitClicked;
        if (settingsButton != null) settingsButton.clicked += OnSettingsClicked;
        if (controlsButton != null) controlsButton.clicked += OnControlsClicked;
        if (settingsCloseButton != null) settingsCloseButton.clicked += CloseSettings;
        if (controlsCloseButton != null) controlsCloseButton.clicked += CloseControls;

        // light up the ">" marker of a row when its button is hovered or focused
        HookHover(playButton);
        HookHover(settingsButton);
        HookHover(controlsButton);
        HookHover(quitButton);
    }

    void HookHover(Button button)
    {
        if (button == null) return;

        VisualElement row = button.parent;
        if (row == null) return;

        button.RegisterCallback<PointerEnterEvent>(_ => row.AddToClassList("is-active"));
        button.RegisterCallback<PointerLeaveEvent>(_ => row.RemoveFromClassList("is-active"));
        button.RegisterCallback<FocusInEvent>(_ => row.AddToClassList("is-active"));
        button.RegisterCallback<FocusOutEvent>(_ => row.RemoveFromClassList("is-active"));
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

    /// Picks one of the non-level tracks at random and loops it under the menu.
    void StartMenuMusic()
    {
        if (menuMusic != null)
        {
            if (!menuMusic.isPlaying) menuMusic.Play();
            return;
        }

        AudioClip[] all = Resources.LoadAll<AudioClip>("SFX");

        List<AudioClip> pool = new List<AudioClip>();
        foreach (AudioClip clip in all)
        {
            if (clip == null) continue;

            bool isLevelTrack = !string.IsNullOrEmpty(levelSoundtrackName) &&
                clip.name.IndexOf(levelSoundtrackName, System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isLevelTrack) pool.Add(clip);
        }

        if (pool.Count == 0) pool.AddRange(all);   // nothing left: fall back to every clip
        if (pool.Count == 0) return;

        menuMusic = gameObject.AddComponent<AudioSource>();
        menuMusic.clip = pool[Random.Range(0, pool.Count)];
        menuMusic.loop = true;
        menuMusic.playOnAwake = false;
        menuMusic.spatialBlend = 0f;
        menuMusic.volume = GameSettings.MusicVolume;
        menuMusic.Play();
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
        if (graphicsDropdown == null) return;

        string[] names = QualitySettings.names;
        if (names.Length == 0) return;

        graphicsDropdown.choices = new List<string>(names);

        int current = GameSettings.QualityLevel >= 0
            ? GameSettings.QualityLevel
            : QualitySettings.GetQualityLevel();
        current = Mathf.Clamp(current, 0, names.Length - 1);
        graphicsDropdown.SetValueWithoutNotify(names[current]);

        graphicsDropdown.RegisterValueChangedCallback(evt =>
        {
            int index = System.Array.IndexOf(names, evt.newValue);
            if (index >= 0) GameSettings.SetQualityLevel(index);
        });
    }

    void WireFrameRate()
    {
        if (framerateDropdown == null) return;

        List<string> options = new List<string> { "30 FPS (Battery Saver)", "60 FPS (Default)", "Max / Uncapped" };
        int[] fpsValues = new int[] { 30, 60, -1 };

        framerateDropdown.choices = options;

        int currentFps = GameSettings.TargetFrameRate;
        int selectedIndex = 1; // default to 60
        if (currentFps == 30) selectedIndex = 0;
        else if (currentFps <= 0 || currentFps > 60) selectedIndex = 2;

        framerateDropdown.SetValueWithoutNotify(options[selectedIndex]);

        framerateDropdown.RegisterValueChangedCallback(evt =>
        {
            int idx = options.IndexOf(evt.newValue);
            if (idx >= 0 && idx < fpsValues.Length)
                GameSettings.SetTargetFrameRate(fpsValues[idx]);
        });
    }

    void UpdatePercentLabel(Label label, float value)
    {
        if (label != null) label.text = Mathf.RoundToInt(value * 100f) + "%";
    }

    // ------------------------------------------------------------- behaviour

    void OnPlayClicked() => StartCoroutine(FadeOutThen(LoadGame));

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
        if (titleLabel == null) yield break;

        titleLabel.text = string.Empty;

        for (int i = 0; i < titleText.Length; i++)
        {
            titleLabel.text = titleText.Substring(0, i + 1) + "_";
            yield return new WaitForSecondsRealtime(typingSpeed);
        }

        titleLabel.text = titleText;
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

        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetOpacity(Mathf.Lerp(start, 0f, elapsed / fadeOutDuration));
            yield return null;
        }

        SetOpacity(0f);
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
    }
}
