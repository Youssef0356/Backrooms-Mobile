using System;
using UnityEngine;
using UnityEngine.UIElements;
using StarterAssets;

/// <summary>
/// Immersive, analog-styled in-game Stamina HUD gauge.
/// Displays player endurance at the bottom-center of the screen matching the
/// Backrooms VHS CRT / phosphor terminal aesthetic. Automatically fades out
/// when stamina is full and unneeded, and pulses warnings when exhausted.
/// </summary>
public class PlayerStaminaHUD : MonoBehaviour
{
    public static PlayerStaminaHUD Instance { get; private set; }

    [Header("Look & Typography")]
    [SerializeField] private Font uiFont;
    [SerializeField] private string fontResourcePath = "Fonts/RobotoMono-Regular";

    [Header("Behavior")]
    [Tooltip("Smoothly fade out the stamina bar when fully charged at 100%.")]
    [SerializeField] private bool autoHideWhenFull = true;
    [Tooltip("Delay in seconds after reaching 100% before starting to fade out.")]
    [SerializeField] private float autoHideDelay = 1.8f;

    private UIDocument document;
    private PanelSettings panelSettings;
    private Font font;

    private VisualElement root;
    private VisualElement container;
    private VisualElement card;
    private VisualElement headerRow;
    private Label titleLabel;
    private Label statusLabel;
    private Label percentLabel;
    private VisualElement barTrack;
    private VisualElement barFill;

    private float targetNormalizedStamina = 1f;
    private float displayedNormalizedStamina = 1f;
    private bool isExhausted;
    private float fullTimer;
    private float currentOpacity = 0f;
    private float exhaustPulseTimer;
    private int lastDisplayedPct = -1;
    private string lastStatusTag = null;

    // Device safe area tracking
    private Rect appliedSafeArea;
    private int appliedScreenWidth;
    private int appliedScreenHeight;

    private readonly Color colorOptimal = new Color(0.88f, 0.85f, 0.46f, 0.95f);  // Liminal phosphor amber #DFD876
    private readonly Color colorMid = new Color(0.92f, 0.65f, 0.22f, 0.95f);      // Caution orange
    private readonly Color colorDanger = new Color(0.92f, 0.25f, 0.20f, 0.98f);   // Critical red
    private readonly Color colorCardBorder = new Color(0.88f, 0.85f, 0.46f, 0.35f);

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

    private void OnEnable()
    {
        PlayerStamina.OnStaminaChanged += HandleStaminaChanged;
        PlayerStamina.OnExhaustionChanged += HandleExhaustionChanged;
        PauseMenuController.PauseStateChanged += HandlePauseStateChanged;
        GameOverController.OnPlayerDefeated += HandlePlayerDefeated;
        VictoryController.OnPlayerEscaped += HandlePlayerDefeated;
    }

    private void OnDisable()
    {
        PlayerStamina.OnStaminaChanged -= HandleStaminaChanged;
        PlayerStamina.OnExhaustionChanged -= HandleExhaustionChanged;
        PauseMenuController.PauseStateChanged -= HandlePauseStateChanged;
        GameOverController.OnPlayerDefeated -= HandlePlayerDefeated;
        VictoryController.OnPlayerEscaped -= HandlePlayerDefeated;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (card == null || GameOverController.IsGameOver || VictoryController.IsGameWon)
        {
            if (card != null) card.style.display = DisplayStyle.None;
            return;
        }

        if (PauseMenuController.IsPaused)
        {
            card.style.display = DisplayStyle.None;
            return;
        }

        card.style.display = DisplayStyle.Flex;
        UpdateSafeArea();

        float dt = Time.unscaledDeltaTime;

        // Smooth fill interpolation
        displayedNormalizedStamina = Mathf.MoveTowards(displayedNormalizedStamina, targetNormalizedStamina, dt * 4.5f);
        if (barFill != null)
        {
            barFill.style.width = new Length(displayedNormalizedStamina * 100f, LengthUnit.Percent);

            // Dynamic color shift based on stamina level
            Color fillColor;
            if (isExhausted)
            {
                exhaustPulseTimer += dt * 8f;
                float pulse = 0.65f + 0.35f * Mathf.Sin(exhaustPulseTimer);
                fillColor = Color.Lerp(colorDanger * 0.7f, colorDanger, pulse);
            }
            else if (displayedNormalizedStamina > 0.45f)
            {
                fillColor = Color.Lerp(colorMid, colorOptimal, (displayedNormalizedStamina - 0.45f) / 0.55f);
            }
            else
            {
                fillColor = Color.Lerp(colorDanger, colorMid, displayedNormalizedStamina / 0.45f);
            }

            barFill.style.backgroundColor = fillColor;
        }

        // Percentage readout
        if (percentLabel != null)
        {
            int pct = Mathf.RoundToInt(displayedNormalizedStamina * 100f);
            if (pct != lastDisplayedPct)
            {
                lastDisplayedPct = pct;
                percentLabel.text = pct.ToString("D2") + "%";
            }
        }

        // Status badge label
        if (statusLabel != null)
        {
            string currentStatus;
            Color statusCol;

            if (isExhausted)
            {
                currentStatus = "[EXHAUSTED]";
                statusCol = colorDanger;
            }
            else if (PlayerStamina.Instance != null && PlayerStamina.Instance.IsSprinting)
            {
                currentStatus = "[DRAINING]";
                statusCol = colorMid;
            }
            else if (displayedNormalizedStamina < 0.99f)
            {
                currentStatus = "[RECHARGING]";
                statusCol = colorOptimal;
            }
            else
            {
                currentStatus = "[RESTED]";
                statusCol = colorOptimal * 0.75f;
            }

            if (currentStatus != lastStatusTag)
            {
                lastStatusTag = currentStatus;
                statusLabel.text = currentStatus;
                statusLabel.style.color = statusCol;
            }
        }

        // Auto-fade logic
        if (autoHideWhenFull && displayedNormalizedStamina >= 0.995f && !isExhausted)
        {
            fullTimer += dt;
            if (fullTimer >= autoHideDelay)
            {
                currentOpacity = Mathf.MoveTowards(currentOpacity, 0f, dt * 2.5f);
            }
        }
        else
        {
            fullTimer = 0f;
            currentOpacity = Mathf.MoveTowards(currentOpacity, 1f, dt * 6.0f);
        }

        card.style.opacity = currentOpacity;
    }

    private void HandleStaminaChanged(float current, float max)
    {
        targetNormalizedStamina = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        if (targetNormalizedStamina < 0.99f)
        {
            fullTimer = 0f;
        }
    }

    private void HandleExhaustionChanged(bool exhausted)
    {
        isExhausted = exhausted;
        if (exhausted)
        {
            fullTimer = 0f;
            exhaustPulseTimer = 0f;
        }
    }

    private void HandlePauseStateChanged(bool isPaused)
    {
        if (card != null)
        {
            card.style.display = (!isPaused && !GameOverController.IsGameOver) ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void HandlePlayerDefeated()
    {
        if (card != null)
        {
            card.style.display = DisplayStyle.None;
        }
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
            panelSettings.name = "Stamina HUD Panel Settings";
            panelSettings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
            panelSettings.sortingOrder = 20; // Gameplay HUD layer
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
        }

        document = gameObject.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;
        document.sortingOrder = 20;

        root = document.rootVisualElement;
        root.pickingMode = PickingMode.Ignore;

        // Container pinned to bottom center
        container = new VisualElement();
        container.pickingMode = PickingMode.Ignore;
        container.style.position = Position.Absolute;
        container.style.bottom = 32f;
        container.style.left = 0f;
        container.style.right = 0f;
        container.style.alignItems = Align.Center;
        container.style.justifyContent = Justify.FlexEnd;
        root.Add(container);

        // Stamina HUD Card
        card = new VisualElement();
        card.pickingMode = PickingMode.Ignore;
        card.style.width = 280f;
        card.style.backgroundColor = new Color(0.04f, 0.05f, 0.02f, 0.82f);
        card.style.borderTopWidth = 1f;
        card.style.borderRightWidth = 1f;
        card.style.borderBottomWidth = 1f;
        card.style.borderLeftWidth = 3f; // Left accent bar
        card.style.borderTopColor = colorCardBorder;
        card.style.borderRightColor = colorCardBorder;
        card.style.borderBottomColor = colorCardBorder;
        card.style.borderLeftColor = colorOptimal;
        card.style.borderTopLeftRadius = 4f;
        card.style.borderTopRightRadius = 4f;
        card.style.borderBottomLeftRadius = 4f;
        card.style.borderBottomRightRadius = 4f;
        card.style.paddingTop = 6f;
        card.style.paddingBottom = 7f;
        card.style.paddingLeft = 10f;
        card.style.paddingRight = 10f;
        card.style.opacity = 0f; // Hidden until movement/sprint activates it
        container.Add(card);

        // Header Row: [STAMINA] ... [STATUS] ... [100%]
        headerRow = new VisualElement();
        headerRow.pickingMode = PickingMode.Ignore;
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.justifyContent = Justify.SpaceBetween;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.marginBottom = 4f;
        card.Add(headerRow);

        titleLabel = new Label("STAMINA");
        titleLabel.pickingMode = PickingMode.Ignore;
        titleLabel.style.fontSize = 11f;
        titleLabel.style.color = colorOptimal;
        titleLabel.style.letterSpacing = 1.5f;
        ApplyFont(titleLabel);
        headerRow.Add(titleLabel);

        statusLabel = new Label("[RESTED]");
        statusLabel.pickingMode = PickingMode.Ignore;
        statusLabel.style.fontSize = 10f;
        statusLabel.style.color = colorOptimal * 0.75f;
        statusLabel.style.letterSpacing = 1.0f;
        ApplyFont(statusLabel);
        headerRow.Add(statusLabel);

        percentLabel = new Label("100%");
        percentLabel.pickingMode = PickingMode.Ignore;
        percentLabel.style.fontSize = 11f;
        percentLabel.style.color = colorOptimal;
        percentLabel.style.letterSpacing = 1.0f;
        ApplyFont(percentLabel);
        headerRow.Add(percentLabel);

        // Bar Track
        barTrack = new VisualElement();
        barTrack.pickingMode = PickingMode.Ignore;
        barTrack.style.height = 7f;
        barTrack.style.width = new Length(100f, LengthUnit.Percent);
        barTrack.style.backgroundColor = new Color(0.10f, 0.12f, 0.08f, 0.90f);
        barTrack.style.borderTopWidth = 1f;
        barTrack.style.borderRightWidth = 1f;
        barTrack.style.borderBottomWidth = 1f;
        barTrack.style.borderLeftWidth = 1f;
        barTrack.style.borderTopColor = new Color(0.88f, 0.85f, 0.46f, 0.22f);
        barTrack.style.borderRightColor = new Color(0.88f, 0.85f, 0.46f, 0.22f);
        barTrack.style.borderBottomColor = new Color(0.88f, 0.85f, 0.46f, 0.22f);
        barTrack.style.borderLeftColor = new Color(0.88f, 0.85f, 0.46f, 0.22f);
        barTrack.style.borderTopLeftRadius = 2f;
        barTrack.style.borderTopRightRadius = 2f;
        barTrack.style.borderBottomLeftRadius = 2f;
        barTrack.style.borderBottomRightRadius = 2f;
        barTrack.style.overflow = Overflow.Hidden;
        barTrack.style.position = Position.Relative;
        card.Add(barTrack);

        // Bar Fill
        barFill = new VisualElement();
        barFill.pickingMode = PickingMode.Ignore;
        barFill.style.height = new Length(100f, LengthUnit.Percent);
        barFill.style.width = new Length(100f, LengthUnit.Percent);
        barFill.style.backgroundColor = colorOptimal;
        barTrack.Add(barFill);

        // Analog tick grid notches for telemetry meter aesthetic
        VisualElement tickContainer = new VisualElement();
        tickContainer.pickingMode = PickingMode.Ignore;
        tickContainer.style.position = Position.Absolute;
        tickContainer.style.left = 0f;
        tickContainer.style.right = 0f;
        tickContainer.style.top = 0f;
        tickContainer.style.bottom = 0f;
        tickContainer.style.flexDirection = FlexDirection.Row;
        tickContainer.style.justifyContent = Justify.SpaceEvenly;
        barTrack.Add(tickContainer);

        for (int i = 0; i < 4; i++)
        {
            VisualElement tick = new VisualElement();
            tick.pickingMode = PickingMode.Ignore;
            tick.style.width = 1f;
            tick.style.height = new Length(100f, LengthUnit.Percent);
            tick.style.backgroundColor = new Color(0.04f, 0.05f, 0.02f, 0.70f);
            tickContainer.Add(tick);
        }
    }

    private void UpdateSafeArea()
    {
        if (container == null) return;

        Rect safeArea = Screen.safeArea;
        if (safeArea.width <= 0f || safeArea.height <= 0f)
        {
            safeArea = new Rect(0f, 0f, Screen.width, Screen.height);
        }

        if (safeArea == appliedSafeArea &&
            Screen.width == appliedScreenWidth &&
            Screen.height == appliedScreenHeight)
        {
            return;
        }

        float panelHeight = root.resolvedStyle.height;
        if (float.IsNaN(panelHeight) || panelHeight <= 0f) return;

        appliedSafeArea = safeArea;
        appliedScreenWidth = Screen.width;
        appliedScreenHeight = Screen.height;

        float toPanelY = panelHeight / Mathf.Max(1f, Screen.height);
        float insetBottom = Mathf.Max(0f, safeArea.yMin * toPanelY);

        container.style.bottom = 32f + insetBottom;
    }

    private void ApplyFont(VisualElement element)
    {
        if (font != null && element != null)
        {
            element.style.unityFontDefinition = new StyleFontDefinition(font);
        }
    }

    private Font LoadFont()
    {
        if (string.IsNullOrEmpty(fontResourcePath)) return null;
        return Resources.Load<Font>(fontResourcePath);
    }
}
