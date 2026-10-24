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
    private VisualElement sanityBarTrack;
    private VisualElement sanityBarFill;

    // Mobile Drink Action Button (Lower-Right thumb cluster)
    private VisualElement mobileDrinkButton;
    private Label mobileDrinkLabel;
    private Label mobileBadgeLabel;

    // PC Inventory Quick-Slot (Upper-Left, below Sanity Card)
    private VisualElement pcInventoryCard;
    private Label pcDrinkLabel;
    private Label pcCountLabel;

    // Pickup Prompt Card (Centered)
    private VisualElement pickupCard;
    private Label pickupKeyLabel;
    private Label pickupActionLabel;

    private float displayedSanity = 100f;
    private float targetSanity = 100f;
    private int currentBottles = 0;
    private float pulseTimer;

    private readonly Color colorPhosphor = new Color(0.88f, 0.85f, 0.46f, 0.95f);  // #DFD876
    private readonly Color colorCaution = new Color(0.92f, 0.65f, 0.22f, 0.95f);   // Amber orange
    private readonly Color colorDanger = new Color(0.92f, 0.25f, 0.20f, 0.98f);    // Critical red
    private readonly Color colorDarkBack = new Color(0.04f, 0.05f, 0.04f, 0.85f);
    private readonly Color colorBorder = new Color(0.88f, 0.85f, 0.46f, 0.40f);

    private static bool IsMobile => PlatformUtils.IsMobile;

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
    }

    private void OnDisable()
    {
        PlayerSanity.OnSanityChanged -= HandleSanityChanged;
        PlayerInventory.OnInventoryChanged -= HandleInventoryChanged;
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

    private void Update()
    {
        if (root == null) return;

        bool paused = PauseMenuController.IsPaused || GameOverController.IsGameOver || VictoryController.IsGameWon;

        if (sanityCard != null) sanityCard.style.display = paused ? DisplayStyle.None : DisplayStyle.Flex;
        if (mobileDrinkButton != null) mobileDrinkButton.style.display = (paused || !IsMobile) ? DisplayStyle.None : DisplayStyle.Flex;
        if (pcInventoryCard != null) pcInventoryCard.style.display = (paused || IsMobile) ? DisplayStyle.None : DisplayStyle.Flex;

        if (paused)
        {
            if (pickupCard != null) pickupCard.style.display = DisplayStyle.None;
            return;
        }

        float dt = Time.deltaTime;
        pulseTimer += dt * 3.5f;

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
            float pulse = 0.7f + Mathf.Sin(pulseTimer * 2f) * 0.3f;
            activeColor = new Color(colorDanger.r, colorDanger.g * pulse, colorDanger.b * pulse, 0.98f);
            statusTag = "[PSYCHOSIS]";
        }

        sanityBarFill.style.backgroundColor = activeColor;
        sanityPercentLabel.text = $"{Mathf.RoundToInt(displayedSanity)}% {statusTag}";
        sanityPercentLabel.style.color = activeColor;
    }

    private void RefreshInventoryDisplay()
    {
        bool hasBottles = currentBottles > 0;

        // Mobile button
        if (mobileDrinkButton != null)
        {
            if (mobileBadgeLabel != null) mobileBadgeLabel.text = $"x{currentBottles}";
            mobileDrinkButton.style.opacity = hasBottles ? 1f : 0.40f;
            mobileDrinkButton.pickingMode = hasBottles ? PickingMode.Position : PickingMode.Ignore;
        }

        // PC slot
        if (pcInventoryCard != null)
        {
            if (pcCountLabel != null) pcCountLabel.text = $"x{currentBottles}";
            pcInventoryCard.style.opacity = hasBottles ? 1f : 0.50f;
        }
    }

    private void UpdatePickupPrompt()
    {
        PlayerInventory inv = PlayerInventory.Instance;
        AlmondWaterPickup pickup = (inv != null) ? inv.CurrentTargetPickup : null;

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
                pickupActionLabel.text = "PICK UP ALMOND WATER";
            }
        }
        else
        {
            pickupCard.style.display = DisplayStyle.None;
            pickupCard.pickingMode = PickingMode.Ignore;
        }
    }

    // -------------------------------------------------------------------------
    // UI BUILD & STYLING
    // -------------------------------------------------------------------------

    private void BuildUI()
    {
        font = Resources.Load<Font>(fontResourcePath);

        PanelSettings baseSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
        panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        if (baseSettings != null)
        {
            panelSettings.themeUss = baseSettings.themeUss;
        }
        panelSettings.name = "Inventory HUD Panel Settings";
        panelSettings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
        panelSettings.sortingOrder = 25; // Renders in front of Camera VHS overlay (2)
        panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panelSettings.referenceResolution = new Vector2Int(1920, 1080);
        panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panelSettings.match = 0.5f;

        document = gameObject.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;
        document.sortingOrder = 25;

        root = document.rootVisualElement;
        root.pickingMode = PickingMode.Ignore;

        safeContainer = new VisualElement();
        safeContainer.pickingMode = PickingMode.Ignore;
        safeContainer.style.position = Position.Absolute;
        safeContainer.style.left = 0;
        safeContainer.style.top = 0;
        safeContainer.style.right = 0;
        safeContainer.style.bottom = 0;
        root.Add(safeContainer);

        // 1. Sanity Monitor Card (Upper-Left beneath REC ●)
        sanityCard = BuildSanityCard();
        safeContainer.Add(sanityCard);

        // 2. Mobile Quick-Drink Button (Lower-Right above Flashlight)
        mobileDrinkButton = BuildMobileDrinkButton();
        safeContainer.Add(mobileDrinkButton);

        // 3. PC Quick-Slot Card (Upper-Left beneath Sanity Monitor)
        pcInventoryCard = BuildPCInventoryCard();
        safeContainer.Add(pcInventoryCard);

        // 4. Pickup Prompt Card (Centered)
        pickupCard = BuildPickupCard();
        safeContainer.Add(pickupCard);

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
        VisualElement card = new VisualElement();
        card.pickingMode = PickingMode.Ignore;
        card.style.position = Position.Absolute;
        card.style.left = 42;
        card.style.top = 74; // Positioned cleanly under REC ●
        card.style.width = 270;
        card.style.backgroundColor = colorDarkBack;
        card.style.borderLeftColor = colorBorder;
        card.style.borderRightColor = colorBorder;
        card.style.borderTopColor = colorBorder;
        card.style.borderBottomColor = colorBorder;
        card.style.borderLeftWidth = 1.5f;
        card.style.borderRightWidth = 1.5f;
        card.style.borderTopWidth = 1.5f;
        card.style.borderBottomWidth = 1.5f;
        card.style.borderTopLeftRadius = 4;
        card.style.borderTopRightRadius = 4;
        card.style.borderBottomLeftRadius = 4;
        card.style.borderBottomRightRadius = 4;
        card.style.paddingLeft = 12;
        card.style.paddingRight = 12;
        card.style.paddingTop = 8;
        card.style.paddingBottom = 8;

        // Header Row: Title & Status
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.marginBottom = 6;

        sanityTitleLabel = new Label("SANITY MONITOR");
        ApplyFont(sanityTitleLabel, 11, true);
        sanityTitleLabel.style.color = colorPhosphor * 0.8f;
        row.Add(sanityTitleLabel);

        sanityPercentLabel = new Label("100% [LUCID]");
        ApplyFont(sanityPercentLabel, 11, true);
        sanityPercentLabel.style.color = colorPhosphor;
        row.Add(sanityPercentLabel);

        card.Add(row);

        // Bar Track & Fill
        sanityBarTrack = new VisualElement();
        sanityBarTrack.style.height = 6;
        sanityBarTrack.style.backgroundColor = new Color(0.10f, 0.12f, 0.08f, 0.95f);
        sanityBarTrack.style.borderLeftWidth = 1;
        sanityBarTrack.style.borderRightWidth = 1;
        sanityBarTrack.style.borderTopWidth = 1;
        sanityBarTrack.style.borderBottomWidth = 1;
        sanityBarTrack.style.borderLeftColor = colorBorder * 0.6f;
        sanityBarTrack.style.borderRightColor = colorBorder * 0.6f;
        sanityBarTrack.style.borderTopColor = colorBorder * 0.6f;
        sanityBarTrack.style.borderBottomColor = colorBorder * 0.6f;

        sanityBarFill = new VisualElement();
        sanityBarFill.style.height = Length.Percent(100);
        sanityBarFill.style.width = Length.Percent(100);
        sanityBarFill.style.backgroundColor = colorPhosphor;
        sanityBarTrack.Add(sanityBarFill);

        card.Add(sanityBarTrack);
        return card;
    }

    private VisualElement BuildMobileDrinkButton()
    {
        // Circular action button stacked on right thumb column:
        // Sprint (bottom: 30), Flashlight (bottom: 160), Drink (bottom: 285)
        VisualElement btn = new VisualElement();
        btn.pickingMode = PickingMode.Position;
        btn.style.position = Position.Absolute;
        btn.style.right = 40;
        btn.style.bottom = 285;
        btn.style.width = 95;
        btn.style.height = 95;
        btn.style.backgroundColor = new Color(0.06f, 0.08f, 0.06f, 0.82f);
        btn.style.borderLeftColor = colorPhosphor;
        btn.style.borderRightColor = colorPhosphor;
        btn.style.borderTopColor = colorPhosphor;
        btn.style.borderBottomColor = colorPhosphor;
        btn.style.borderLeftWidth = 2;
        btn.style.borderRightWidth = 2;
        btn.style.borderTopWidth = 2;
        btn.style.borderBottomWidth = 2;
        btn.style.borderTopLeftRadius = 48;
        btn.style.borderTopRightRadius = 48;
        btn.style.borderBottomLeftRadius = 48;
        btn.style.borderBottomRightRadius = 48;
        btn.style.alignItems = Align.Center;
        btn.style.justifyContent = Justify.Center;

        // Small top icon/tag
        Label iconTag = new Label("ALMOND");
        ApplyFont(iconTag, 9, true);
        iconTag.style.color = colorPhosphor * 0.75f;
        btn.Add(iconTag);

        mobileDrinkLabel = new Label("DRINK");
        ApplyFont(mobileDrinkLabel, 14, true);
        mobileDrinkLabel.style.color = colorPhosphor;
        btn.Add(mobileDrinkLabel);

        // Quantity Badge (Upper-Right corner)
        mobileBadgeLabel = new Label("x0");
        ApplyFont(mobileBadgeLabel, 11, true);
        mobileBadgeLabel.style.position = Position.Absolute;
        mobileBadgeLabel.style.top = -4;
        mobileBadgeLabel.style.right = -4;
        mobileBadgeLabel.style.backgroundColor = colorPhosphor;
        mobileBadgeLabel.style.color = colorDarkBack;
        mobileBadgeLabel.style.paddingLeft = 5;
        mobileBadgeLabel.style.paddingRight = 5;
        mobileBadgeLabel.style.paddingTop = 2;
        mobileBadgeLabel.style.paddingBottom = 2;
        mobileBadgeLabel.style.borderTopLeftRadius = 10;
        mobileBadgeLabel.style.borderTopRightRadius = 10;
        mobileBadgeLabel.style.borderBottomLeftRadius = 10;
        mobileBadgeLabel.style.borderBottomRightRadius = 10;
        btn.Add(mobileBadgeLabel);

        // Touch feedback & click event
        btn.RegisterCallback<PointerDownEvent>(evt =>
        {
            btn.style.backgroundColor = new Color(0.92f, 0.88f, 0.50f, 0.35f);
        });

        btn.RegisterCallback<PointerUpEvent>(evt =>
        {
            btn.style.backgroundColor = new Color(0.06f, 0.08f, 0.06f, 0.82f);
            if (PlayerInventory.Instance != null)
            {
                PlayerInventory.Instance.UseAlmondWater();
            }
        });

        return btn;
    }

    private VisualElement BuildPCInventoryCard()
    {
        VisualElement card = new VisualElement();
        card.pickingMode = PickingMode.Ignore;
        card.style.position = Position.Absolute;
        card.style.left = 42;
        card.style.top = 125;
        card.style.width = 270;
        card.style.backgroundColor = colorDarkBack;
        card.style.borderLeftColor = colorBorder;
        card.style.borderRightColor = colorBorder;
        card.style.borderTopColor = colorBorder;
        card.style.borderBottomColor = colorBorder;
        card.style.borderLeftWidth = 1.5f;
        card.style.borderRightWidth = 1.5f;
        card.style.borderTopWidth = 1.5f;
        card.style.borderBottomWidth = 1.5f;
        card.style.borderTopLeftRadius = 4;
        card.style.borderTopRightRadius = 4;
        card.style.borderBottomLeftRadius = 4;
        card.style.borderBottomRightRadius = 4;
        card.style.paddingLeft = 12;
        card.style.paddingRight = 12;
        card.style.paddingTop = 6;
        card.style.paddingBottom = 6;
        card.style.flexDirection = FlexDirection.Row;
        card.style.alignItems = Align.Center;
        card.style.justifyContent = Justify.SpaceBetween;

        pcDrinkLabel = new Label("[Q] DRINK ALMOND WATER");
        ApplyFont(pcDrinkLabel, 11, true);
        pcDrinkLabel.style.color = colorPhosphor;
        card.Add(pcDrinkLabel);

        pcCountLabel = new Label("x0");
        ApplyFont(pcCountLabel, 12, true);
        pcCountLabel.style.color = colorCaution;
        card.Add(pcCountLabel);

        return card;
    }

    private VisualElement BuildPickupCard()
    {
        VisualElement card = new VisualElement();
        card.pickingMode = PickingMode.Position;
        card.style.position = Position.Absolute;
        card.style.left = Length.Percent(50);
        card.style.bottom = 135;
        card.style.translate = new StyleTranslate(new Translate(Length.Percent(-50), 0, 0));
        card.style.backgroundColor = colorDarkBack;
        card.style.borderLeftColor = colorPhosphor;
        card.style.borderRightColor = colorPhosphor;
        card.style.borderTopColor = colorPhosphor;
        card.style.borderBottomColor = colorPhosphor;
        card.style.borderLeftWidth = 1.5f;
        card.style.borderRightWidth = 1.5f;
        card.style.borderTopWidth = 1.5f;
        card.style.borderBottomWidth = 1.5f;
        card.style.borderTopLeftRadius = 4;
        card.style.borderTopRightRadius = 4;
        card.style.borderBottomLeftRadius = 4;
        card.style.borderBottomRightRadius = 4;
        card.style.paddingLeft = 18;
        card.style.paddingRight = 18;
        card.style.paddingTop = 9;
        card.style.paddingBottom = 9;
        card.style.flexDirection = FlexDirection.Row;
        card.style.alignItems = Align.Center;
        card.style.display = DisplayStyle.None;

        pickupKeyLabel = new Label(IsMobile ? "[TAP]" : "[E]");
        ApplyFont(pickupKeyLabel, 15, true);
        pickupKeyLabel.style.color = colorCaution;
        pickupKeyLabel.style.marginRight = 10;
        card.Add(pickupKeyLabel);

        pickupActionLabel = new Label("PICK UP ALMOND WATER");
        ApplyFont(pickupActionLabel, 14, true);
        pickupActionLabel.style.color = colorPhosphor;
        card.Add(pickupActionLabel);

        // Click / touch to collect
        card.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (PlayerInventory.Instance != null)
            {
                PlayerInventory.Instance.NotifyPickupTap();
            }
        });

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
