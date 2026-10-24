using System;
using UnityEngine;
using UnityEngine.UIElements;
using StarterAssets;

/// <summary>
/// Liminal phosphor terminal HUD for Player Inventory (Almond Water) and Mental Sanity.
/// Features:
/// 1. Quick-Drink Slot: shows Almond Water count with [Q] DRINK prompt (PC) or touchable DRINK button (Mobile).
/// 2. World Pickup Prompt: "[E] PICK UP ALMOND WATER" / "[TAP TO PICK UP]" when looking at a bottle.
/// 3. Sanity Status Monitor: analog CRT text & gauge bar ([LUCID] -> [PARANOIA] -> [PSYCHOSIS]).
/// Auto-adapts between Mobile touch controls and PC desktop inputs.
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

    // Sanity & Inventory Card (Bottom-Left)
    private VisualElement statusCard;
    private Label sanityTitleLabel;
    private Label sanityPercentLabel;
    private VisualElement sanityBarTrack;
    private VisualElement sanityBarFill;

    private VisualElement inventoryRow;
    private VisualElement bottleIcon;
    private Label bottleCountLabel;
    private Button drinkButton;

    // Pickup Prompt Card (Center)
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
    private readonly Color colorDarkBack = new Color(0.04f, 0.05f, 0.04f, 0.82f);
    private readonly Color colorBorder = new Color(0.88f, 0.85f, 0.46f, 0.35f);

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
        statusCard.style.display = paused ? DisplayStyle.None : DisplayStyle.Flex;

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
        if (bottleCountLabel != null)
        {
            bottleCountLabel.text = $"x{currentBottles}";
        }

        if (drinkButton != null)
        {
            bool hasBottles = currentBottles > 0;
            drinkButton.SetEnabled(hasBottles);

            if (IsMobile)
            {
                drinkButton.text = hasBottles ? "DRINK" : "EMPTY";
            }
            else
            {
                drinkButton.text = hasBottles ? "[Q] DRINK" : "[EMPTY]";
            }

            drinkButton.style.opacity = hasBottles ? 1f : 0.45f;
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

        panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
        if (panelSettings == null)
        {
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "Inventory HUD Panel Settings";
            panelSettings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
            panelSettings.sortingOrder = 21;
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
        }

        document = gameObject.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;

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

        // 1. Sanity & Inventory Card (Bottom-Left)
        statusCard = BuildStatusCard();
        safeContainer.Add(statusCard);

        // 2. Pickup Prompt Card (Centered)
        pickupCard = BuildPickupCard();
        safeContainer.Add(pickupCard);

        // Initial refresh
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

    private VisualElement BuildStatusCard()
    {
        VisualElement card = new VisualElement();
        card.pickingMode = PickingMode.Ignore;
        card.style.position = Position.Absolute;
        card.style.left = 28;
        card.style.bottom = 24;
        card.style.width = 280;
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
        card.style.paddingTop = 10;
        card.style.paddingBottom = 10;

        // ---- Sanity Header Row ----
        VisualElement sanityRow = new VisualElement();
        sanityRow.style.flexDirection = FlexDirection.Row;
        sanityRow.style.justifyContent = Justify.SpaceBetween;
        sanityRow.style.marginBottom = 5;

        sanityTitleLabel = new Label("SANITY MONITOR");
        ApplyFont(sanityTitleLabel, 11, true);
        sanityTitleLabel.style.color = colorPhosphor * 0.75f;
        sanityRow.Add(sanityTitleLabel);

        sanityPercentLabel = new Label("100% [LUCID]");
        ApplyFont(sanityPercentLabel, 11, true);
        sanityPercentLabel.style.color = colorPhosphor;
        sanityRow.Add(sanityPercentLabel);

        card.Add(sanityRow);

        // ---- Sanity Bar Track ----
        sanityBarTrack = new VisualElement();
        sanityBarTrack.style.height = 7;
        sanityBarTrack.style.backgroundColor = new Color(0.12f, 0.14f, 0.10f, 0.95f);
        sanityBarTrack.style.borderLeftWidth = 1;
        sanityBarTrack.style.borderRightWidth = 1;
        sanityBarTrack.style.borderTopWidth = 1;
        sanityBarTrack.style.borderBottomWidth = 1;
        sanityBarTrack.style.borderLeftColor = colorBorder * 0.6f;
        sanityBarTrack.style.borderRightColor = colorBorder * 0.6f;
        sanityBarTrack.style.borderTopColor = colorBorder * 0.6f;
        sanityBarTrack.style.borderBottomColor = colorBorder * 0.6f;
        sanityBarTrack.style.marginBottom = 10;

        sanityBarFill = new VisualElement();
        sanityBarFill.style.height = Length.Percent(100);
        sanityBarFill.style.width = Length.Percent(100);
        sanityBarFill.style.backgroundColor = colorPhosphor;
        sanityBarTrack.Add(sanityBarFill);

        card.Add(sanityBarTrack);

        // ---- Inventory Almond Water Row ----
        inventoryRow = new VisualElement();
        inventoryRow.style.flexDirection = FlexDirection.Row;
        inventoryRow.style.alignItems = Align.Center;
        inventoryRow.style.justifyContent = Justify.SpaceBetween;
        inventoryRow.style.borderTopWidth = 1;
        inventoryRow.style.borderTopColor = colorBorder * 0.4f;
        inventoryRow.style.paddingTop = 7;

        // Left info: Icon & Name & Count
        VisualElement leftInfo = new VisualElement();
        leftInfo.style.flexDirection = FlexDirection.Row;
        leftInfo.style.alignItems = Align.Center;

        // Small amber icon glyph
        Label iconLabel = new Label("◆");
        ApplyFont(iconLabel, 12, false);
        iconLabel.style.color = colorPhosphor;
        iconLabel.style.marginRight = 6;
        leftInfo.Add(iconLabel);

        Label itemName = new Label("ALMOND WATER");
        ApplyFont(itemName, 12, true);
        itemName.style.color = colorPhosphor;
        itemName.style.marginRight = 8;
        leftInfo.Add(itemName);

        bottleCountLabel = new Label("x0");
        ApplyFont(bottleCountLabel, 12, true);
        bottleCountLabel.style.color = colorCaution;
        leftInfo.Add(bottleCountLabel);

        inventoryRow.Add(leftInfo);

        // Right: Drink Button / Prompt
        drinkButton = new Button();
        drinkButton.pickingMode = PickingMode.Position;
        ApplyFont(drinkButton, 12, true);
        drinkButton.text = IsMobile ? "DRINK" : "[Q] DRINK";
        drinkButton.style.color = colorDarkBack;
        drinkButton.style.backgroundColor = colorPhosphor;
        drinkButton.style.borderLeftWidth = 0;
        drinkButton.style.borderRightWidth = 0;
        drinkButton.style.borderTopWidth = 0;
        drinkButton.style.borderBottomWidth = 0;
        drinkButton.style.borderTopLeftRadius = 3;
        drinkButton.style.borderTopRightRadius = 3;
        drinkButton.style.borderBottomLeftRadius = 3;
        drinkButton.style.borderBottomRightRadius = 3;
        drinkButton.style.paddingLeft = 10;
        drinkButton.style.paddingRight = 10;
        drinkButton.style.paddingTop = 4;
        drinkButton.style.paddingBottom = 4;

        drinkButton.clicked += () =>
        {
            if (PlayerInventory.Instance != null)
            {
                PlayerInventory.Instance.UseAlmondWater();
            }
        };

        inventoryRow.Add(drinkButton);
        card.Add(inventoryRow);

        return card;
    }

    private VisualElement BuildPickupCard()
    {
        VisualElement card = new VisualElement();
        card.pickingMode = PickingMode.Position;
        card.style.position = Position.Absolute;
        card.style.left = Length.Percent(50);
        card.style.bottom = 120;
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
        card.style.paddingLeft = 16;
        card.style.paddingRight = 16;
        card.style.paddingTop = 8;
        card.style.paddingBottom = 8;
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

        // Click on pickup card to interact (touch friendly)
        card.RegisterCallback<ClickEvent>(evt =>
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
