using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// In-game vent HUD: the "[E] ENTER VENT" prompt (a tappable button on mobile), the
/// "too close" warning, and the claustrophobic vignette shown while the player hides inside a duct.
/// Styled to match the phosphor-green VHS look of the stamina HUD.
/// </summary>
public class VentHUD : MonoBehaviour
{
    public enum PromptMode { None, Enter, TooClose, Leave }

    public static VentHUD Instance { get; private set; }

    /// <summary>Raised when the on-screen button is tapped / clicked.</summary>
    public static event Action OnActionPressed;

    [SerializeField] private string fontResourcePath = "Fonts/RobotoMono-Regular";

    private UIDocument document;
    private PanelSettings panelSettings;
    private Font font;

    private VisualElement root;
    private VisualElement vignette;
    private VisualElement promptCard;
    private Label keyLabel;
    private Label actionLabel;
    private Label hintLabel;
    private Label hiddenLabel;

    private PromptMode mode = PromptMode.None;
    private bool hidden;
    private float promptOpacity;
    private float vignetteOpacity;
    private float pulse;
    private Texture2D vignetteTexture;

    private readonly Color colorAmber = new Color(0.88f, 0.85f, 0.46f, 0.98f);
    private readonly Color colorDanger = new Color(0.92f, 0.28f, 0.22f, 0.98f);

    private static bool IsMobile => PlatformUtils.IsMobile;

    public static VentHUD GetOrCreate()
    {
        if (Instance != null) return Instance;
        VentHUD existing = FindAnyObjectByType<VentHUD>();
        if (existing != null) return existing;
        return new GameObject("Vent HUD").AddComponent<VentHUD>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildUI();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (vignetteTexture != null) Destroy(vignetteTexture);
    }

    public void SetPrompt(PromptMode newMode)
    {
        if (mode == newMode) return;
        mode = newMode;
        RefreshPromptText();
    }

    public void SetHidden(bool isHidden)
    {
        hidden = isHidden;
        if (hiddenLabel != null) hiddenLabel.style.display = isHidden ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void Update()
    {
        if (root == null) return;

        float dt = Time.unscaledDeltaTime;
        pulse += dt * 3.2f;

        bool paused = PauseMenuController.IsPaused || GameOverController.IsGameOver || VictoryController.IsGameWon;

        // Prompt
        float targetPrompt = (!paused && mode != PromptMode.None) ? 1f : 0f;
        promptOpacity = Mathf.MoveTowards(promptOpacity, targetPrompt, dt * 7f);
        promptCard.style.opacity = promptOpacity;
        promptCard.style.display = promptOpacity > 0.01f ? DisplayStyle.Flex : DisplayStyle.None;
        promptCard.pickingMode = (promptOpacity > 0.5f && mode != PromptMode.TooClose) ? PickingMode.Position : PickingMode.Ignore;

        if (mode == PromptMode.TooClose)
        {
            float a = 0.65f + 0.35f * Mathf.Sin(pulse * 2.5f);
            actionLabel.style.color = new Color(colorDanger.r, colorDanger.g, colorDanger.b, a);
        }

        // Vignette
        float targetVignette = (!paused && hidden) ? 1f : 0f;
        vignetteOpacity = Mathf.MoveTowards(vignetteOpacity, targetVignette, dt * 2.2f);
        vignette.style.opacity = vignetteOpacity;
        vignette.style.display = vignetteOpacity > 0.01f ? DisplayStyle.Flex : DisplayStyle.None;

        if (hidden && hiddenLabel != null)
            hiddenLabel.style.opacity = (0.55f + 0.45f * Mathf.Sin(pulse)) * vignetteOpacity;
    }

    private void RefreshPromptText()
    {
        if (promptCard == null) return;

        string key = IsMobile ? "TAP" : "[E]";
        switch (mode)
        {
            case PromptMode.Enter:
                keyLabel.text = key;
                actionLabel.text = "HIDE IN VENT";
                actionLabel.style.color = colorAmber;
                hintLabel.text = "crawl in - he will lose you";
                promptCard.style.borderLeftColor = colorAmber;
                break;

            case PromptMode.TooClose:
                keyLabel.text = "!!";
                actionLabel.text = "TOO CLOSE";
                actionLabel.style.color = colorDanger;
                hintLabel.text = "he is right behind you - keep running";
                promptCard.style.borderLeftColor = colorDanger;
                break;

            case PromptMode.Leave:
                keyLabel.text = key;
                actionLabel.text = "LEAVE VENT";
                actionLabel.style.color = colorAmber;
                hintLabel.text = "look around with the mouse / swipe";
                promptCard.style.borderLeftColor = colorAmber;
                break;
        }
    }

    // -------------------------------------------------------------------------
    // UI BUILD
    // -------------------------------------------------------------------------

    private void BuildUI()
    {
        font = Resources.Load<Font>(fontResourcePath);

        panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
        if (panelSettings == null)
        {
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "Vent HUD Panel Settings";
            panelSettings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
            panelSettings.sortingOrder = 22;
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
        }

        document = gameObject.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;

        root = document.rootVisualElement;
        root.pickingMode = PickingMode.Ignore;

        // ---- Vignette (full-screen, behind everything else) ----
        vignetteTexture = MakeVignetteTexture();
        vignette = new VisualElement();
        vignette.pickingMode = PickingMode.Ignore;
        vignette.style.position = Position.Absolute;
        vignette.style.left = 0f;
        vignette.style.top = 0f;
        vignette.style.right = 0f;
        vignette.style.bottom = 0f;
        vignette.style.backgroundImage = new StyleBackground(vignetteTexture);
        vignette.style.opacity = 0f;
        vignette.style.display = DisplayStyle.None;
        root.Add(vignette);

        // ---- "HIDDEN" status, top centre ----
        hiddenLabel = new Label("[ HIDDEN ]");
        hiddenLabel.pickingMode = PickingMode.Ignore;
        hiddenLabel.style.position = Position.Absolute;
        hiddenLabel.style.top = 38f;
        hiddenLabel.style.left = 0f;
        hiddenLabel.style.right = 0f;
        hiddenLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        hiddenLabel.style.fontSize = 16f;
        hiddenLabel.style.letterSpacing = 4f;
        hiddenLabel.style.color = colorAmber;
        hiddenLabel.style.display = DisplayStyle.None;
        ApplyFont(hiddenLabel);
        root.Add(hiddenLabel);

        // ---- Prompt card ----
        VisualElement container = new VisualElement();
        container.pickingMode = PickingMode.Ignore;
        container.style.position = Position.Absolute;
        container.style.bottom = IsMobile ? 150f : 110f;
        container.style.left = 0f;
        container.style.right = 0f;
        container.style.alignItems = Align.Center;
        root.Add(container);

        promptCard = new VisualElement();
        promptCard.pickingMode = PickingMode.Ignore;
        promptCard.style.flexDirection = FlexDirection.Row;
        promptCard.style.alignItems = Align.Center;
        promptCard.style.backgroundColor = new Color(0.04f, 0.05f, 0.02f, 0.86f);
        promptCard.style.borderTopWidth = 1f;
        promptCard.style.borderRightWidth = 1f;
        promptCard.style.borderBottomWidth = 1f;
        promptCard.style.borderLeftWidth = 3f;
        Color edge = new Color(0.88f, 0.85f, 0.46f, 0.35f);
        promptCard.style.borderTopColor = edge;
        promptCard.style.borderRightColor = edge;
        promptCard.style.borderBottomColor = edge;
        promptCard.style.borderLeftColor = colorAmber;
        promptCard.style.borderTopLeftRadius = 4f;
        promptCard.style.borderTopRightRadius = 4f;
        promptCard.style.borderBottomLeftRadius = 4f;
        promptCard.style.borderBottomRightRadius = 4f;
        promptCard.style.paddingTop = IsMobile ? 14f : 8f;
        promptCard.style.paddingBottom = IsMobile ? 14f : 8f;
        promptCard.style.paddingLeft = 14f;
        promptCard.style.paddingRight = 18f;
        promptCard.style.opacity = 0f;
        promptCard.style.display = DisplayStyle.None;
        promptCard.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (mode == PromptMode.Enter || mode == PromptMode.Leave)
            {
                OnActionPressed?.Invoke();
                evt.StopPropagation();
            }
        });
        container.Add(promptCard);

        keyLabel = new Label("[E]");
        keyLabel.pickingMode = PickingMode.Ignore;
        keyLabel.style.fontSize = IsMobile ? 20f : 16f;
        keyLabel.style.color = new Color(0.04f, 0.05f, 0.02f, 1f);
        keyLabel.style.backgroundColor = colorAmber;
        keyLabel.style.paddingLeft = 8f;
        keyLabel.style.paddingRight = 8f;
        keyLabel.style.paddingTop = 2f;
        keyLabel.style.paddingBottom = 2f;
        keyLabel.style.borderTopLeftRadius = 3f;
        keyLabel.style.borderTopRightRadius = 3f;
        keyLabel.style.borderBottomLeftRadius = 3f;
        keyLabel.style.borderBottomRightRadius = 3f;
        keyLabel.style.marginRight = 12f;
        ApplyFont(keyLabel);
        promptCard.Add(keyLabel);

        VisualElement textCol = new VisualElement();
        textCol.pickingMode = PickingMode.Ignore;
        promptCard.Add(textCol);

        actionLabel = new Label("HIDE IN VENT");
        actionLabel.pickingMode = PickingMode.Ignore;
        actionLabel.style.fontSize = IsMobile ? 20f : 16f;
        actionLabel.style.letterSpacing = 2f;
        actionLabel.style.color = colorAmber;
        ApplyFont(actionLabel);
        textCol.Add(actionLabel);

        hintLabel = new Label("");
        hintLabel.pickingMode = PickingMode.Ignore;
        hintLabel.style.fontSize = 11f;
        hintLabel.style.letterSpacing = 1f;
        hintLabel.style.color = new Color(0.88f, 0.85f, 0.46f, 0.6f);
        ApplyFont(hintLabel);
        textCol.Add(hintLabel);

        RefreshPromptText();
    }

    private void ApplyFont(VisualElement element)
    {
        if (font != null && element != null)
            element.style.unityFontDefinition = new StyleFontDefinition(font);
    }

    /// <summary>Soft radial falloff: clear centre, near-black edges, like peering through a grille in the dark.</summary>
    private static Texture2D MakeVignetteTexture()
    {
        const int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size * 2f - 1f;
                float ny = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx * 0.9f + ny * ny * 1.25f);
                float a = Mathf.SmoothStep(0.35f, 1.25f, r) * 0.93f;
                px[y * size + x] = new Color32(2, 3, 2, (byte)(Mathf.Clamp01(a) * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }
}
