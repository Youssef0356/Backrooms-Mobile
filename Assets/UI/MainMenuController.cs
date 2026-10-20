using System.Collections;
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

    [Header("Audio")]
    [Range(0f, 1f)]
    [SerializeField] float defaultVolume = 0.8f;
    const string volumePrefKey = "backrooms.masterVolume";

    UIDocument document;
    VisualElement root;

    Label titleLabel;
    Label versionLabel;
    Label buildLabel;
    Label volumeValueLabel;
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

    string titleText;
    Coroutine flickerRoutine;

    void Awake()
    {
        document = GetComponent<UIDocument>();
        root = document.rootVisualElement;

        titleLabel = root.Q<Label>("title");
        versionLabel = root.Q<Label>("version-label");
        buildLabel = root.Q<Label>("build-label");
        volumeValueLabel = root.Q<Label>("volume-value");
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

        if (titleLabel != null) titleText = titleLabel.text;
    }

    void OnEnable()
    {
        ApplyFont();
        FillVersionBlock();
        WireButtons();
        WireVolume();

        if (typingEffect && titleLabel != null) StartCoroutine(TypeTitle());
        if (flickerEffect) flickerRoutine = StartCoroutine(Flicker());
    }

    void OnDisable()
    {
        if (flickerRoutine != null) StopCoroutine(flickerRoutine);
        flickerRoutine = null;
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

    void WireVolume()
    {
        if (volumeSlider == null) return;

        float volume = PlayerPrefs.GetFloat(volumePrefKey, defaultVolume);
        volume = Mathf.Clamp01(volume);

        AudioListener.volume = volume;
        volumeSlider.SetValueWithoutNotify(volume * 100f);
        UpdateVolumeLabel(volume);

        volumeSlider.RegisterValueChangedCallback(evt =>
        {
            float value = Mathf.Clamp01(evt.newValue / 100f);
            AudioListener.volume = value;
            PlayerPrefs.SetFloat(volumePrefKey, value);
            UpdateVolumeLabel(value);
        });
    }

    void UpdateVolumeLabel(float volume)
    {
        if (volumeValueLabel != null)
            volumeValueLabel.text = Mathf.RoundToInt(volume * 100f) + "%";
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
