using System;
using UnityEngine;
using UnityEngine.UIElements;
using StarterAssets;

/// <summary>
/// Liminal phosphor terminal HUD for Player Inventory (Almond Water) and Mental Sanity.
/// Features:
/// 1. Sanity Status Monitor: Telemetry CRT display pinned to the upper-left (beneath REC ●),
///    completely avoiding collisions with the lower-left virtual joystick and VHS timecode.
/// 2. Mobile Quick-Drink Button: A dedicated circular action button on the lower-right
///    (stacked above Flashlight and Sprint buttons) for instant thumb access.
/// 3. PC Quick-Drink Slot: Clean [Q] DRINK shortcut card beneath the Sanity Monitor.
/// 4. World Pickup Prompt: Centered "[TAP] TAKE ALMOND WATER" / "[E] PICK UP ALMOND WATER".
/// Auto-adapts cleanly between Mobile touch controls and PC desktop inputs.
/// </summary>
public class InventoryHUD : MonoBehaviour
{
    public static InventoryHUD Instance { get; private set; }

    [SerializeField] private string fontResourcePath = "Fonts/RobotoMono-Regular";

    private UIDocument document;
    private PanelSettings panelSettings;
    private Font font;

    private VisualElement root;
    private VisualElement safeContainer;

    // Sanity Monitor Card (Upper-Left, below REC ●)
    private VisualElement sanityCard;
    private Label sanityTitleLabel;
    private Label sanityPercentLabel;
    private Label pursuitAlertLabel;
    private VisualElement sanityBarTrack;
    private VisualElement sanityBarFill;

    // Mobile Drink Action Button (Lower-Right thumb cluster)
    private VisualElement mobileDrinkButton;
    private Label mobileDrinkLabel;
    private Label mobileBadgeLabel;

    // Mobile Chalk Action Button (Next to Drink button)
    private VisualElement mobileChalkButton;
    private Label mobileChalkSymbolLabel;
    private Label mobileChalkBadgeLabel;

    // Chalk Telemetry Label (Inside upper-left Sanity Card)
    private Label chalkPercentLabel;

    // PC Inventory Quick-Slot (Upper-Left, below Sanity Card)
    private VisualElement pcInventoryCard;
    private Label pcDrinkLabel;
    private Label pcCountLabel;

    // Pickup Prompt Card (Centered)
    private VisualElement pickupCard;
    private Label pickupKeyLabel;
    private Label pickupActionLabel;

    // Toast Notification Banner (Centered upper)
    private VisualElement toastCard;
    private Label toastLabel;
    private float toastTimer;

    private float displayedSanity = 100f;
    private float targetSanity = 100f;
    private int currentBottles = 0;
    private int currentChalk = 30;
    private ChalkTextureBuilder.MarkType currentMark = ChalkTextureBuilder.MarkType.Cross;
    private float pulseTimer;
    private int lastSanityPct = -1;
    private string lastStatusTag = null;

    private readonly Color colorPhosphor = new Color(0.88f, 0.85f, 0.46f, 0.95f);  // #DFD876
    private readonly Color colorCaution = new Color(0.92f, 0.65f, 0.22f, 0.95f);   // Amber orange
    private readonly Color colorDanger = new Color(0.92f, 0.25f, 0.20f, 0.98f);    // Critical red
    private readonly Color colorDarkBack = new Color(0.04f, 0.05f, 0.04f, 0.85f);
    private readonly Color colorBorder = new Color(0.88f, 0.85f, 0.46f, 0.40f);

    private static bool IsMobile => PlatformUtils.IsMobile || (StarterAssets.TouchscreenInput.Instance != null && StarterAssets.TouchscreenInput.Instance.isActiveAndEnabled);

    private Rect lastSafeArea;

    public static InventoryHUD GetOrCreate()
    {
        if (Instance != null) return Instance;
        InventoryHUD existing = FindAnyObjectByType<InventoryHUD>();
        if (existing != null) return existing;
        return new GameObject("Inventory HUD").AddComponent<InventoryHUD>();
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

    private void OnEnable()
    {
        PlayerSanity.OnSanityChanged += HandleSanityChanged;
        PlayerInventory.OnInventoryChanged += HandleInventoryChanged;
        PlayerInventory.OnItemPickedUp += HandleItemPickedUp;
        ChalkController.OnChalkUsed += HandleChalkUsed;
        ChalkController.OnMarkTypeChanged += HandleMarkTypeChanged;
        FloodedZone.OnPlayerWaterStateChanged += HandleWaterStateChanged;
    }

    private void OnDisable()
    {
        PlayerSanity.OnSanityChanged -= HandleSanityChanged;
        PlayerInventory.OnInventoryChanged -= HandleInventoryChanged;
        PlayerInventory.OnItemPickedUp -= HandleItemPickedUp;
        ChalkController.OnChalkUsed -= HandleChalkUsed;
        ChalkController.OnMarkTypeChanged -= HandleMarkTypeChanged;
        FloodedZone.OnPlayerWaterStateChanged -= HandleWaterStateChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void HandleSanityChanged(float current, float max)
    {
        targetSanity = current;
    }

    private void HandleInventoryChanged(int count)
    {
        currentBottles = count;
        RefreshInventoryDisplay();
    }

    private void HandleChalkUsed(int remaining, ChalkTextureBuilder.MarkType mark)
    {
        currentChalk = remaining;
        currentMark = mark;
        UpdateChalkDisplay();
    }

    private void HandleMarkTypeChanged(ChalkTextureBuilder.MarkType mark)
    {
        currentMark = mark;
        UpdateChalkDisplay();
    }

    private void HandleWaterStateChanged(bool inWater)
    {
        if (inWater)
        {
            toastTimer = 4.0f;
            if (toastLabel != null)
            {
                toastLabel.text = "FLOODED SECTOR — FOOTSTEP SPLASHES RADIATE EXCESSIVE NOISE";
            }
        }
    }

    private void HandleItemPickedUp()
    {
        toastTimer = 3.6f;
        if (toastLabel != null)
        {
            toastLabel.text = IsMobile
                ? $"ALMOND WATER ACQUIRED ({currentBottles}/4) — TAP [DRINK] TO HEAL"
                : $"ALMOND WATER ACQUIRED ({currentBottles}/4) — PRESS [Q] TO DRINK";
        }
    }

    private void Update()
    {
        if (root == null) return;

        bool paused = PauseMenuController.IsPaused || GameOverController.IsGameOver || VictoryController.IsGameWon;

        if (sanityCard != null) sanityCard.style.display = paused ? DisplayStyle.None : DisplayStyle.Flex;
        if (mobileDrinkButton != null) mobileDrinkButton.style.display = (paused || !IsMobile || currentBottles <= 0) ? DisplayStyle.None : DisplayStyle.Flex;
        if (mobileChalkButton != null) mobileChalkButton.style.display = (paused || !IsMobile) ? DisplayStyle.None : DisplayStyle.Flex;
        if (pcInventoryCard != null) pcInventoryCard.style.display = (paused || IsMobile || currentBottles <= 0) ? DisplayStyle.None : DisplayStyle.Flex;

        if (paused)
        {
            if (pickupCard != null) pickupCard.style.display = DisplayStyle.None;
            if (toastCard != null) toastCard.style.display = DisplayStyle.None;
            return;
        }

        float dt = Time.deltaTime;
        pulseTimer += dt * 3.5f;

        UpdateSafeArea();

        // Toast notification timer
        if (toastTimer > 0f)
        {
            toastTimer -= dt;
            if (toastCard != null) toastCard.style.display = DisplayStyle.Flex;
        }
        else
        {
            if (toastCard != null) toastCard.style.display = DisplayStyle.None;
        }

        // Smoothly update displayed sanity
        displayedSanity = Mathf.MoveTowards(displayedSanity, targetSanity, dt * 30f);
        UpdateSanityDisplay();

        // Update pickup prompt
        UpdatePickupPrompt();
    }

    private void UpdateSanityDisplay()
    {
        float norm = Mathf.Clamp01(displayedSanity / 100f);
        sanityBarFill.style.width = Length.Percent(norm * 100f);

        Color activeColor;
        string statusTag;
        bool isPsychosis = false;

        if (norm > 0.75f)
        {
            activeColor = colorPhosphor;
            statusTag = "[LUCID]";
        }
        else if (norm > 0.45f)
        {
            activeColor = colorCaution;
            statusTag = "[PARANOIA]";
        }
        else if (norm > 0.22f)
        {
            activeColor = Color.Lerp(colorCaution, colorDanger, 0.5f);
            statusTag = "[HALLUCINATING]";
        }
        else
        {
            // Pulsing critical warning
            isPsychosis = true;
            float pulse = 0.7f + Mathf.Sin(pulseTimer * 2f) * 0.3f;
            activeColor = new Color(colorDanger.r, colorDanger.g * pulse, colorDanger.b * pulse, 0.98f);
            statusTag = "[PSYCHOSIS]";
        }

        bool isPursuit = ChaseSoundtrackDirector.IsPursuitActive;
        int currentPct = Mathf.RoundToInt(displayedSanity);

        if (isPursuit)
        {
            float hb = ChaseSoundtrackDirector.CurrentHeartbeatPulse;
            Color pursuitRed = Color.Lerp(new Color(0.92f, 0.15f, 0.15f, 0.95f), new Color(1f, 0.40f, 0.40f, 1f), hb);
            activeColor = pursuitRed;
            isPsychosis = true;

            if (sanityCard != null)
            {
                sanityCard.style.borderTopColor = new StyleColor(pursuitRed);
                sanityCard.style.borderBottomColor = new StyleColor(pursuitRed);
                sanityCard.style.borderLeftColor = new StyleColor(pursuitRed);
                sanityCard.style.borderRightColor = new StyleColor(pursuitRed);
                sanityCard.style.backgroundColor = new StyleColor(new Color(0.18f + 0.12f * hb, 0.02f, 0.02f, 0.94f));

                // Panic Jitter on the HUD card in sync with racing heartbeat
                float jitterX = (UnityEngine.Random.value * 2f - 1f) * (1.2f + 1.8f * hb);
                float jitterY = (UnityEngine.Random.value * 2f - 1f) * (1.2f + 1.8f * hb);
                sanityCard.style.translate = new StyleTranslate(new Translate(jitterX, jitterY, 0));
            }

            if (pursuitAlertLabel != null)
            {
                pursuitAlertLabel.style.display = DisplayStyle.Flex;
                pursuitAlertLabel.text = ChaseSoundtrackDirector.IsHoundPursuing ? "⚠️ BEAST HUNTING — SPRINT!" : "⚠️ PURSUIT IN PROGRESS — RUN!";
                pursuitAlertLabel.style.color = pursuitRed;
            }

            sanityPercentLabel.text = $"{currentPct}%";
            sanityPercentLabel.style.color = pursuitRed;
        }
        else
        {
            if (sanityCard != null)
            {
                Color normalBorder = new Color(0.85f, 0.60f, 0.15f, 0.85f);
                sanityCard.style.borderTopColor = new StyleColor(normalBorder);
                sanityCard.style.borderBottomColor = new StyleColor(normalBorder);
                sanityCard.style.borderLeftColor = new StyleColor(normalBorder);
                sanityCard.style.borderRightColor = new StyleColor(normalBorder);
                sanityCard.style.backgroundColor = new StyleColor(new Color(0.04f, 0.05f, 0.03f, 0.88f));
                sanityCard.style.translate = new StyleTranslate(new Translate(0, 0, 0));
            }

            if (pursuitAlertLabel != null)
            {
                pursuitAlertLabel.style.display = DisplayStyle.None;
            }

            if (currentPct != lastSanityPct || statusTag != lastStatusTag || isPsychosis)
            {
                lastSanityPct = currentPct;
                lastStatusTag = statusTag;
                sanityPercentLabel.text = $"{currentPct}% {statusTag}";
                sanityPercentLabel.style.color = activeColor;
            }
        }

        sanityBarFill.style.backgroundColor = activeColor;
    }

    private void RefreshInventoryDisplay()
    {
        bool hasBottles = currentBottles > 0;

        // Mobile button: ONLY visible when hasBottles > 0
        if (mobileDrinkButton != null)
        {
            if (mobileBadgeLabel != null) mobileBadgeLabel.text = $"x{currentBottles}";
            mobileDrinkButton.style.display = (hasBottles && IsMobile) ? DisplayStyle.Flex : DisplayStyle.None;
            mobileDrinkButton.pickingMode = hasBottles ? PickingMode.Position : PickingMode.Ignore;
        }

        // PC slot: ONLY visible when hasBottles > 0
        if (pcInventoryCard != null)
        {
            if (pcCountLabel != null) pcCountLabel.text = $"x{currentBottles}";
            pcInventoryCard.style.display = (hasBottles && !IsMobile) ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void UpdatePickupPrompt()
    {
        PlayerInventory inv = PlayerInventory.Instance;
        AlmondWaterPickup pickup = (inv != null) ? inv.CurrentTargetPickup : null;
        ChalkController chalk = ChalkController.Instance;

        if (pickup != null && !PlayerVentController.IsInVent)
        {
            pickupCard.style.display = DisplayStyle.Flex;
            pickupCard.pickingMode = PickingMode.Position;

            if (IsMobile)
            {
                pickupKeyLabel.text = "[TAP]";
                pickupActionLabel.text = "TAKE ALMOND WATER";
            }
            else
            {
                pickupKeyLabel.text = "[E]";
                pickupActionLabel.text = "TAKE ALMOND WATER";
            }
        }
        else if (chalk != null && chalk.CanDrawOnWall && !PlayerVentController.IsInVent)
        {
            pickupCard.style.display = DisplayStyle.Flex;
            pickupCard.pickingMode = IsMobile ? PickingMode.Position : PickingMode.Ignore;

            string markName = chalk.CurrentMark switch
            {
                ChalkTextureBuilder.MarkType.Cross => "DANGER [X]",
                ChalkTextureBuilder.MarkType.Arrow => "DIRECTION [->]",
                ChalkTextureBuilder.MarkType.Checkmark => "EXPLORED [V]",
                _ => "MARK"
            };

            if (IsMobile)
            {
                pickupKeyLabel.text = "[TAP]";
                pickupActionLabel.text = $"DRAW CHALK ({markName})";
            }
            else
            {
                pickupKeyLabel.text = "[C]";
                pickupActionLabel.text = $"DRAW {markName}  •  [V] CYCLE";
            }
        }
        else
        {
            pickupCard.style.display = DisplayStyle.None;
            pickupCard.pickingMode = PickingMode.Ignore;
        }
    }

    private void UpdateSafeArea()
    {
        Rect sa = Screen.safeArea;
        if (sa.width <= 0f || sa.height <= 0f)
            sa = new Rect(0f, 0f, Screen.width, Screen.height);

        if (sa == lastSafeArea) return;
        lastSafeArea = sa;

        float factorX = 1920f / Mathf.Max(Screen.width, 1);
        float factorY = 1080f / Mathf.Max(Screen.height, 1);

        float insetLeft = sa.xMin * factorX;
        float insetRight = (Screen.width - sa.xMax) * factorX;
        float insetTop = (Screen.height - sa.yMax) * factorY;
        float insetBottom = sa.yMin * factorY;

        if (safeContainer != null)
        {
            safeContainer.style.left = insetLeft;
            safeContainer.style.right = insetRight;
            safeContainer.style.top = insetTop;
            safeContainer.style.bottom = insetBottom;
            safeContainer.style.width = 1920f - insetLeft - insetRight;
            safeContainer.style.height = 1080f - insetTop - insetBottom;
        }
    }

    // -------------------------------------------------------------------------
    // UI BUILD & STYLING
    // -------------------------------------------------------------------------

    private void BuildUI()
    {
        font = Resources.Load<Font>(fontResourcePath);

        PanelSettings baseSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
        if (baseSettings != null)
        {
            panelSettings = Instantiate(baseSettings);
        }
        else
        {
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
        }

        panelSettings.name = "Inventory HUD Panel Settings";
        panelSettings.sortingOrder = 25; // Renders in front of Camera VHS overlay (2)

        document = gameObject.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;
        document.sortingOrder = 25;

        root = document.rootVisualElement;
        root.pickingMode = PickingMode.Ignore;

        safeContainer = new VisualElement();
        safeContainer.name = "SafeContainer";
        safeContainer.pickingMode = PickingMode.Ignore;
        safeContainer.style.position = Position.Absolute;
        safeContainer.style.left = 0;
        safeContainer.style.top = 0;
        safeContainer.style.right = 0;
        safeContainer.style.bottom = 0;
        safeContainer.style.width = Length.Percent(100);
        safeContainer.style.height = Length.Percent(100);
        root.Add(safeContainer);

        // 1. Sanity Monitor Line (Upper-Left beneath REC ●)
        sanityCard = BuildSanityCard();
        safeContainer.Add(sanityCard);

        // 2. Mobile Quick-Drink Button (Lower-Right above Flashlight)
        mobileDrinkButton = BuildMobileDrinkButton();
        safeContainer.Add(mobileDrinkButton);

        // Mobile Chalk Button (Beside Drink button)
        mobileChalkButton = BuildMobileChalkButton();
        safeContainer.Add(mobileChalkButton);

        // 3. PC Quick-Slot Card (Upper-Left beneath Sanity Monitor)
        pcInventoryCard = BuildPCInventoryCard();
        safeContainer.Add(pcInventoryCard);

        // 4. Pickup & Chalk Prompt Card (Centered horizontally in lower-middle of screen)
        VisualElement promptWrapper = new VisualElement();
        promptWrapper.name = "PickupPrompt_Wrapper";
        promptWrapper.pickingMode = PickingMode.Ignore;
        promptWrapper.style.position = Position.Absolute;
        promptWrapper.style.left = 0;
        promptWrapper.style.right = 0;
        promptWrapper.style.bottom = 120;
        promptWrapper.style.alignItems = Align.Center;

        pickupCard = BuildPickupCard();
        promptWrapper.Add(pickupCard);
        safeContainer.Add(promptWrapper);

        // 5. Toast Notification Banner (Centered horizontally in upper third of screen)
        VisualElement toastWrapper = new VisualElement();
        toastWrapper.name = "Toast_Wrapper";
        toastWrapper.pickingMode = PickingMode.Ignore;
        toastWrapper.style.position = Position.Absolute;
        toastWrapper.style.left = 0;
        toastWrapper.style.right = 0;
        toastWrapper.style.top = 220;
        toastWrapper.style.alignItems = Align.Center;

        toastCard = BuildToastCard();
        toastWrapper.Add(toastCard);
        safeContainer.Add(toastWrapper);

        // Initial state
        if (PlayerSanity.Instance != null)
        {
            displayedSanity = targetSanity = PlayerSanity.Instance.CurrentSanity;
        }
        if (PlayerInventory.Instance != null)
        {
            currentBottles = PlayerInventory.Instance.AlmondWaterCount;
        }
        RefreshInventoryDisplay();
    }

    private VisualElement BuildSanityCard()
    {
        // Analog CRT monitor card pinned to the upper-left with high contrast and legible typography
        VisualElement card = new VisualElement();
        card.pickingMode = PickingMode.Ignore;
        card.style.position = Position.Absolute;
        card.style.left = 40;
        card.style.top = 84; // Positioned cleanly below REC ● (which is at top: 38px)
        card.style.width = 370;
        card.style.backgroundColor = new Color(0.04f, 0.05f, 0.03f, 0.88f);
        card.style.borderLeftColor = colorPhosphor;
        card.style.borderLeftWidth = 3.5f;
        card.style.borderTopColor = new Color(0.88f, 0.85f, 0.46f, 0.25f);
        card.style.borderRightColor = new Color(0.88f, 0.85f, 0.46f, 0.25f);
        card.style.borderBottomColor = new Color(0.88f, 0.85f, 0.46f, 0.25f);
        card.style.borderTopWidth = 1f;
        card.style.borderRightWidth = 1f;
        card.style.borderBottomWidth = 1f;
        card.style.borderTopRightRadius = 5f;
        card.style.borderBottomRightRadius = 5f;
        card.style.paddingLeft = 14;
        card.style.paddingRight = 14;
        card.style.paddingTop = 10;
        card.style.paddingBottom = 10;

        // Header Row: "SANITY" & "100% [LUCID]"
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 5;

        sanityTitleLabel = new Label("SANITY");
        ApplyFont(sanityTitleLabel, 15, true);
        sanityTitleLabel.style.color = colorPhosphor * 0.80f;
        sanityTitleLabel.style.letterSpacing = 1.2f;
        sanityTitleLabel.style.flexShrink = 0;
        row.Add(sanityTitleLabel);

        sanityPercentLabel = new Label("100% [LUCID]");
        ApplyFont(sanityPercentLabel, 16, true);
        sanityPercentLabel.style.color = colorPhosphor;
        sanityPercentLabel.style.letterSpacing = 0.8f;
        sanityPercentLabel.style.flexShrink = 0;
        row.Add(sanityPercentLabel);

        card.Add(row);

        // Clear 8px Bar Track & Fill
        sanityBarTrack = new VisualElement();
        sanityBarTrack.style.height = 8;
        sanityBarTrack.style.backgroundColor = new Color(0.10f, 0.12f, 0.08f, 0.95f);
        sanityBarTrack.style.borderTopLeftRadius = 2;
        sanityBarTrack.style.borderTopRightRadius = 2;
        sanityBarTrack.style.borderBottomLeftRadius = 2;
        sanityBarTrack.style.borderBottomRightRadius = 2;
        sanityBarTrack.style.borderLeftColor = new Color(0.88f, 0.85f, 0.46f, 0.20f);
        sanityBarTrack.style.borderRightColor = new Color(0.88f, 0.85f, 0.46f, 0.20f);
        sanityBarTrack.style.borderTopColor = new Color(0.88f, 0.85f, 0.46f, 0.20f);
        sanityBarTrack.style.borderBottomColor = new Color(0.88f, 0.85f, 0.46f, 0.20f);
        sanityBarTrack.style.borderLeftWidth = 1;
        sanityBarTrack.style.borderRightWidth = 1;
        sanityBarTrack.style.borderTopWidth = 1;
        sanityBarTrack.style.borderBottomWidth = 1;

        sanityBarFill = new VisualElement();
        sanityBarFill.style.height = Length.Percent(100);
        sanityBarFill.style.width = Length.Percent(100);
        sanityBarFill.style.backgroundColor = colorPhosphor;
        sanityBarTrack.Add(sanityBarFill);

        card.Add(sanityBarTrack);

        // Pursuit Emergency Alert Label (Displayed when beast is actively hunting player)
        pursuitAlertLabel = new Label("⚠️ BEAST HUNTING — SPRINT!");
        ApplyFont(pursuitAlertLabel, 13, true);
        pursuitAlertLabel.style.color = new Color(1f, 0.25f, 0.25f, 1f);
        pursuitAlertLabel.style.letterSpacing = 1.0f;
        pursuitAlertLabel.style.marginTop = 5;
        pursuitAlertLabel.style.display = DisplayStyle.None;
        card.Add(pursuitAlertLabel);

        // Header Row 2: "CHALK" & "30 [X]"
        VisualElement chalkRow = new VisualElement();
        chalkRow.style.flexDirection = FlexDirection.Row;
        chalkRow.style.justifyContent = Justify.SpaceBetween;
        chalkRow.style.alignItems = Align.Center;
        chalkRow.style.marginTop = 8;

        Label chalkTitleLabel = new Label("CHALK MARKS");
        ApplyFont(chalkTitleLabel, 14, true);
        chalkTitleLabel.style.color = colorPhosphor * 0.80f;
        chalkTitleLabel.style.letterSpacing = 1.0f;
        chalkRow.Add(chalkTitleLabel);

        chalkPercentLabel = new Label("30 [X]");
        ApplyFont(chalkPercentLabel, 15, true);
        chalkPercentLabel.style.color = colorPhosphor;
        chalkRow.Add(chalkPercentLabel);

        card.Add(chalkRow);
        return card;
    }

    private VisualElement BuildMobileDrinkButton()
    {
        VisualElement btn = new VisualElement();
        btn.pickingMode = PickingMode.Position;
        btn.style.position = Position.Absolute;
        btn.style.right = 40;
        btn.style.bottom = 285;
        btn.style.width = 88;
        btn.style.height = 88;
        btn.style.backgroundColor = new Color(0.06f, 0.08f, 0.06f, 0.85f);
        btn.style.borderLeftColor = colorCaution;
        btn.style.borderRightColor = colorCaution;
        btn.style.borderTopColor = colorCaution;
        btn.style.borderBottomColor = colorCaution;
        btn.style.borderLeftWidth = 2;
        btn.style.borderRightWidth = 2;
        btn.style.borderTopWidth = 2;
        btn.style.borderBottomWidth = 2;
        btn.style.borderTopLeftRadius = 44;
        btn.style.borderTopRightRadius = 44;
        btn.style.borderBottomLeftRadius = 44;
        btn.style.borderBottomRightRadius = 44;
        btn.style.alignItems = Align.Center;
        btn.style.justifyContent = Justify.Center;
        btn.style.display = DisplayStyle.None; // shown only when holding water

        Label iconTag = new Label("ALMOND");
        ApplyFont(iconTag, 10, true);
        iconTag.style.color = colorCaution;
        btn.Add(iconTag);

        mobileDrinkLabel = new Label("DRINK");
        ApplyFont(mobileDrinkLabel, 15, true);
        mobileDrinkLabel.style.color = colorPhosphor;
        btn.Add(mobileDrinkLabel);

        // Quantity Badge
        mobileBadgeLabel = new Label("x0");
        ApplyFont(mobileBadgeLabel, 12, true);
        mobileBadgeLabel.style.position = Position.Absolute;
        mobileBadgeLabel.style.top = -3;
        mobileBadgeLabel.style.right = -3;
        mobileBadgeLabel.style.backgroundColor = colorCaution;
        mobileBadgeLabel.style.color = colorDarkBack;
        mobileBadgeLabel.style.paddingLeft = 5;
        mobileBadgeLabel.style.paddingRight = 5;
        mobileBadgeLabel.style.paddingTop = 2;
        mobileBadgeLabel.style.paddingBottom = 2;
        mobileBadgeLabel.style.borderTopLeftRadius = 8;
        mobileBadgeLabel.style.borderTopRightRadius = 8;
        mobileBadgeLabel.style.borderBottomLeftRadius = 8;
        mobileBadgeLabel.style.borderBottomRightRadius = 8;
        btn.Add(mobileBadgeLabel);

        btn.RegisterCallback<PointerDownEvent>(evt =>
        {
            btn.style.backgroundColor = new Color(0.92f, 0.88f, 0.50f, 0.35f);
        });

        btn.RegisterCallback<PointerUpEvent>(evt =>
        {
            btn.style.backgroundColor = new Color(0.06f, 0.08f, 0.06f, 0.85f);
            if (PlayerInventory.Instance != null)
            {
                PlayerInventory.Instance.UseAlmondWater();
            }
        });

        return btn;
    }

    private VisualElement BuildMobileChalkButton()
    {
        VisualElement btn = new VisualElement();
        btn.pickingMode = PickingMode.Position;
        btn.style.position = Position.Absolute;
        btn.style.right = 138;
        btn.style.bottom = 285;
        btn.style.width = 88;
        btn.style.height = 88;
        btn.style.backgroundColor = new Color(0.06f, 0.08f, 0.06f, 0.85f);
        btn.style.borderLeftColor = colorPhosphor;
        btn.style.borderRightColor = colorPhosphor;
        btn.style.borderTopColor = colorPhosphor;
        btn.style.borderBottomColor = colorPhosphor;
        btn.style.borderLeftWidth = 2;
        btn.style.borderRightWidth = 2;
        btn.style.borderTopWidth = 2;
        btn.style.borderBottomWidth = 2;
        btn.style.borderTopLeftRadius = 44;
        btn.style.borderTopRightRadius = 44;
        btn.style.borderBottomLeftRadius = 44;
        btn.style.borderBottomRightRadius = 44;
        btn.style.alignItems = Align.Center;
        btn.style.justifyContent = Justify.Center;

        Label iconTag = new Label("CHALK");
        ApplyFont(iconTag, 10, true);
        iconTag.style.color = colorPhosphor * 0.75f;
        btn.Add(iconTag);

        mobileChalkSymbolLabel = new Label("X");
        ApplyFont(mobileChalkSymbolLabel, 16, true);
        mobileChalkSymbolLabel.style.color = colorPhosphor;
        btn.Add(mobileChalkSymbolLabel);

        // Quantity Badge
        mobileChalkBadgeLabel = new Label($"x{currentChalk}");
        ApplyFont(mobileChalkBadgeLabel, 11, true);
        mobileChalkBadgeLabel.style.position = Position.Absolute;
        mobileChalkBadgeLabel.style.top = -3;
        mobileChalkBadgeLabel.style.right = -3;
        mobileChalkBadgeLabel.style.backgroundColor = colorPhosphor;
        mobileChalkBadgeLabel.style.color = colorDarkBack;
        mobileChalkBadgeLabel.style.paddingLeft = 5;
        mobileChalkBadgeLabel.style.paddingRight = 5;
        mobileChalkBadgeLabel.style.paddingTop = 2;
        mobileChalkBadgeLabel.style.paddingBottom = 2;
        mobileChalkBadgeLabel.style.borderTopLeftRadius = 8;
        mobileChalkBadgeLabel.style.borderTopRightRadius = 8;
        mobileChalkBadgeLabel.style.borderBottomLeftRadius = 8;
        mobileChalkBadgeLabel.style.borderBottomRightRadius = 8;
        btn.Add(mobileChalkBadgeLabel);

        btn.RegisterCallback<PointerDownEvent>(evt =>
        {
            btn.style.backgroundColor = new Color(0.92f, 0.88f, 0.50f, 0.35f);
        });

        btn.RegisterCallback<PointerUpEvent>(evt =>
        {
            btn.style.backgroundColor = new Color(0.06f, 0.08f, 0.06f, 0.85f);
            if (ChalkController.Instance != null)
            {
                if (ChalkController.Instance.CanDrawOnWall)
                {
                    ChalkController.Instance.RequestMobileDraw();
                }
                else
                {
                    // Tapping while not aimed at wall cycles the chalk mark
                    ChalkController.Instance.RequestMobileCycle();
                }
            }
        });

        return btn;
    }

    private void UpdateChalkDisplay()
    {
        string symbol = currentMark switch
        {
            ChalkTextureBuilder.MarkType.Cross => "X",
            ChalkTextureBuilder.MarkType.Arrow => "->",
            ChalkTextureBuilder.MarkType.Checkmark => "V",
            _ => "X"
        };

        if (chalkPercentLabel != null)
        {
            chalkPercentLabel.text = currentChalk > 0 ? $"{currentChalk} [{symbol}]" : "0 [EMPTY]";
            chalkPercentLabel.style.color = currentChalk > 5 ? colorPhosphor : colorCaution;
        }

        if (mobileChalkSymbolLabel != null)
        {
            mobileChalkSymbolLabel.text = symbol;
        }

        if (mobileChalkBadgeLabel != null)
        {
            mobileChalkBadgeLabel.text = $"x{currentChalk}";
        }
    }

    private VisualElement BuildPCInventoryCard()
    {
        // Sleek Almond Water slot: ONLY shown when player actually holds bottles!
        VisualElement card = new VisualElement();
        card.pickingMode = PickingMode.Ignore;
        card.style.position = Position.Absolute;
        card.style.left = 40;
        card.style.top = 210; // Positioned cleanly below Sanity Card
        card.style.width = 330;
        card.style.backgroundColor = new Color(0.04f, 0.05f, 0.03f, 0.88f);
        card.style.borderLeftColor = colorCaution;
        card.style.borderLeftWidth = 3.5f;
        card.style.borderTopColor = new Color(0.92f, 0.65f, 0.22f, 0.25f);
        card.style.borderRightColor = new Color(0.92f, 0.65f, 0.22f, 0.25f);
        card.style.borderBottomColor = new Color(0.92f, 0.65f, 0.22f, 0.25f);
        card.style.borderTopWidth = 1f;
        card.style.borderRightWidth = 1f;
        card.style.borderBottomWidth = 1f;
        card.style.borderTopRightRadius = 5;
        card.style.borderBottomRightRadius = 5;
        card.style.paddingLeft = 14;
        card.style.paddingRight = 14;
        card.style.paddingTop = 8;
        card.style.paddingBottom = 8;
        card.style.flexDirection = FlexDirection.Row;
        card.style.alignItems = Align.Center;
        card.style.justifyContent = Justify.SpaceBetween;
        card.style.display = DisplayStyle.None; // Hidden when 0 bottles!

        pcDrinkLabel = new Label("ALMOND WATER — PRESS [Q]");
        ApplyFont(pcDrinkLabel, 14, true);
        pcDrinkLabel.style.color = colorPhosphor;
        card.Add(pcDrinkLabel);

        pcCountLabel = new Label("x0");
        ApplyFont(pcCountLabel, 16, true);
        pcCountLabel.style.color = colorCaution;
        card.Add(pcCountLabel);

        return card;
    }

    private VisualElement BuildPickupCard()
    {
        // Centered prompt positioned naturally below the player's crosshair inside promptWrapper
        VisualElement card = new VisualElement();
        card.pickingMode = PickingMode.Position;
        card.style.position = Position.Relative;
        card.style.backgroundColor = new Color(0.04f, 0.06f, 0.04f, 0.90f);
        card.style.borderLeftColor = colorCaution;
        card.style.borderRightColor = colorCaution;
        card.style.borderTopColor = colorCaution;
        card.style.borderBottomColor = colorCaution;
        card.style.borderLeftWidth = 1.5f;
        card.style.borderRightWidth = 1.5f;
        card.style.borderTopWidth = 1.5f;
        card.style.borderBottomWidth = 1.5f;
        card.style.borderTopLeftRadius = 4;
        card.style.borderTopRightRadius = 4;
        card.style.borderBottomLeftRadius = 4;
        card.style.borderBottomRightRadius = 4;
        card.style.paddingLeft = 16;
        card.style.paddingRight = 16;
        card.style.paddingTop = 7;
        card.style.paddingBottom = 7;
        card.style.flexDirection = FlexDirection.Row;
        card.style.alignItems = Align.Center;
        card.style.display = DisplayStyle.None;

        pickupKeyLabel = new Label(IsMobile ? "[TAP]" : "[E]");
        ApplyFont(pickupKeyLabel, 16, true);
        pickupKeyLabel.style.color = colorCaution;
        pickupKeyLabel.style.marginRight = 8;
        card.Add(pickupKeyLabel);

        pickupActionLabel = new Label("TAKE ALMOND WATER");
        ApplyFont(pickupActionLabel, 14, true);
        pickupActionLabel.style.color = colorPhosphor;
        card.Add(pickupActionLabel);

        // Click / touch to collect or draw chalk
        card.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (PlayerInventory.Instance != null && PlayerInventory.Instance.CurrentTargetPickup != null)
            {
                PlayerInventory.Instance.NotifyPickupTap();
            }
            else if (ChalkController.Instance != null && ChalkController.Instance.CanDrawOnWall)
            {
                ChalkController.Instance.RequestMobileDraw();
            }
        });

        return card;
    }

    private VisualElement BuildToastCard()
    {
        // Clear tutorial toast informing the player how to drink after collecting
        VisualElement card = new VisualElement();
        card.pickingMode = PickingMode.Ignore;
        card.style.position = Position.Relative;
        card.style.backgroundColor = new Color(0.05f, 0.07f, 0.04f, 0.92f);
        card.style.borderLeftColor = colorCaution;
        card.style.borderRightColor = colorCaution;
        card.style.borderTopColor = colorCaution;
        card.style.borderBottomColor = colorCaution;
        card.style.borderLeftWidth = 1.5f;
        card.style.borderRightWidth = 1.5f;
        card.style.borderTopWidth = 1.5f;
        card.style.borderBottomWidth = 1.5f;
        card.style.borderTopLeftRadius = 4;
        card.style.borderTopRightRadius = 4;
        card.style.borderBottomLeftRadius = 4;
        card.style.borderBottomRightRadius = 4;
        card.style.paddingLeft = 16;
        card.style.paddingRight = 16;
        card.style.paddingTop = 7;
        card.style.paddingBottom = 7;
        card.style.display = DisplayStyle.None;

        toastLabel = new Label("ALMOND WATER ACQUIRED — PRESS [Q] TO DRINK");
        ApplyFont(toastLabel, 15, true);
        toastLabel.style.color = colorPhosphor;
        card.Add(toastLabel);

        return card;
    }

    private void ApplyFont(VisualElement elem, int fontSize, bool bold)
    {
        if (elem == null) return;
        if (font != null) elem.style.unityFont = font;
        elem.style.fontSize = fontSize;
        elem.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
    }
}
