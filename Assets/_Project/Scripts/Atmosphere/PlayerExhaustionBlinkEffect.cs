using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using StarterAssets;

/// <summary>
/// Triggers exhaustion and tired eyelid blinking when player stamina hits 0:
/// - Screen-space overlay eyelids (Top & Bottom) that droop and blink shut during exhaustion.
/// - Uses a high-priority ScreenSpaceOverlay Canvas (sortingOrder 32000) so it is 100% visible on PC, Mobile, and URP.
/// - Instantly blinks the moment stamina reaches 0, repeating rhythmic tired blinks while exhausted.
/// - Sluggish movement speed and lockout of sprinting until recovered.
/// </summary>
[DisallowMultipleComponent]
public class PlayerExhaustionBlinkEffect : MonoBehaviour
{
    public static event System.Action OnBlinkStart;
    public static event System.Action OnBlinkPeak;
    public static event System.Action OnBlinkEnd;
    public static bool IsEyelidsShut { get; private set; } = false;
    [Header("Exhaustion Sluggishness")]
    [Tooltip("Movement speed multiplier when exhausted (e.g. 0.60 = 40% slower).")]
    [SerializeField] private float exhaustedSpeedMultiplier = 0.60f;

    [Header("Tired Eyelid Blinking")]
    [Tooltip("How fast eyelids shut in seconds.")]
    [FormerlySerializedAs("blinkCloseDuration")]
    [SerializeField] private float blinkCloseSpeed = 0.22f;

    [Tooltip("How long eyes stay shut in darkness in seconds.")]
    [SerializeField] private float blinkHoldDuration = 0.16f;

    [Tooltip("How fast eyelids open in seconds.")]
    [FormerlySerializedAs("blinkOpenDuration")]
    [SerializeField] private float blinkOpenSpeed = 0.32f;

    [Tooltip("Time between blinks while exhausted.")]
    [FormerlySerializedAs("blinkInterval")]
    [SerializeField] private float timeBetweenBlinks = 2.4f;

    private FirstPersonController fpc;
    private PlayerStamina stamina;
    private float defaultMoveSpeed = 4.0f;
    private float defaultSprintSpeed = 6.0f;

    // Runtime Canvas & Eyelid RectTransforms
    private Canvas blinkCanvas;
    private RectTransform topEyelid;
    private RectTransform bottomEyelid;
    private Image topEyelidImg;
    private Image bottomEyelidImg;

    private float currentEyelidFraction = 0f; // 0 = fully open, 1 = fully shut (covers 50% each)
    private Coroutine blinkCoroutine;
    private bool isExhausted = false;
    private float nextBlinkTimer = 0f;

    private void Awake()
    {
        fpc = GetComponent<FirstPersonController>() ?? GetComponentInParent<FirstPersonController>();
        stamina = GetComponent<PlayerStamina>() ?? GetComponentInParent<PlayerStamina>();

        if (fpc != null)
        {
            defaultMoveSpeed = fpc.MoveSpeed;
            defaultSprintSpeed = fpc.SprintSpeed;
        }

        BuildEyelidOverlayCanvas();
    }

    private void BuildEyelidOverlayCanvas()
    {
        // Create dedicated full-screen overlay canvas
        GameObject canvasGo = new GameObject("Player_EyelidOverlayCanvas");
        canvasGo.transform.SetParent(transform, false);

        blinkCanvas = canvasGo.AddComponent<Canvas>();
        blinkCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        blinkCanvas.sortingOrder = 32000; // Always render on top of all HUDs and game views

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        canvasGo.AddComponent<GraphicRaycaster>(); // Required for UI

        // Top Eyelid (anchored to top stretch)
        GameObject topGo = new GameObject("TopEyelid");
        topGo.transform.SetParent(canvasGo.transform, false);
        topEyelid = topGo.AddComponent<RectTransform>();
        topEyelid.anchorMin = new Vector2(0f, 1f);
        topEyelid.anchorMax = new Vector2(1f, 1f);
        topEyelid.pivot = new Vector2(0.5f, 1f);
        topEyelid.anchoredPosition = Vector2.zero;
        topEyelid.sizeDelta = new Vector2(0f, 0f);

        topEyelidImg = topGo.AddComponent<Image>();
        topEyelidImg.color = Color.black;
        topEyelidImg.raycastTarget = false;

        // Bottom Eyelid (anchored to bottom stretch)
        GameObject botGo = new GameObject("BottomEyelid");
        botGo.transform.SetParent(canvasGo.transform, false);
        bottomEyelid = botGo.AddComponent<RectTransform>();
        bottomEyelid.anchorMin = new Vector2(0f, 0f);
        bottomEyelid.anchorMax = new Vector2(1f, 0f);
        bottomEyelid.pivot = new Vector2(0.5f, 0f);
        bottomEyelid.anchoredPosition = Vector2.zero;
        bottomEyelid.sizeDelta = new Vector2(0f, 0f);

        bottomEyelidImg = botGo.AddComponent<Image>();
        bottomEyelidImg.color = Color.black;
        bottomEyelidImg.raycastTarget = false;

        UpdateEyelidPositions(0f);
    }

    private void OnEnable()
    {
        PlayerStamina.OnExhaustionChanged += HandleExhaustionChanged;
        PlayerStamina.OnStaminaChanged += HandleStaminaChanged;
    }

    private void OnDisable()
    {
        PlayerStamina.OnExhaustionChanged -= HandleExhaustionChanged;
        PlayerStamina.OnStaminaChanged -= HandleStaminaChanged;
        ResetExhaustion();
    }

    private void HandleExhaustionChanged(bool exhausted)
    {
        if (exhausted && !isExhausted)
        {
            TriggerExhaustion();
        }
        else if (!exhausted && isExhausted)
        {
            ResetExhaustion();
        }
    }

    private void HandleStaminaChanged(float current, float max)
    {
        // Instant trigger if current stamina drains to exhaustion threshold
        float threshold = Mathf.Max(1.0f, max * 0.015f);
        if (current <= threshold && !isExhausted)
        {
            TriggerExhaustion();
        }
    }

    private void TriggerExhaustion()
    {
        isExhausted = true;
        ApplySluggishSpeed();

        // Immediately blink when stamina hits 0!
        if (blinkCoroutine != null) StopCoroutine(blinkCoroutine);
        blinkCoroutine = StartCoroutine(DoTiredBlinkSequence());
        nextBlinkTimer = timeBetweenBlinks;
    }

    private void ResetExhaustion()
    {
        isExhausted = false;
        if (fpc != null)
        {
            fpc.MoveSpeed = defaultMoveSpeed;
            fpc.SprintSpeed = defaultSprintSpeed;
        }

        if (blinkCoroutine != null)
        {
            StopCoroutine(blinkCoroutine);
            blinkCoroutine = null;
        }
        UpdateEyelidPositions(0f);
    }

    [Header("Natural Periodic Blinking")]
    [Tooltip("Whether the character blinks periodically even when not exhausted.")]
    [SerializeField] private bool enablePeriodicBlinking = true;
    [Tooltip("Average seconds between periodic involuntary blinks.")]
    [SerializeField] private float periodicBlinkInterval = 32f;

    private float naturalBlinkTimer = 18f;

    private void Update()
    {
        if (stamina != null && stamina.IsExhausted)
        {
            if (!isExhausted) TriggerExhaustion();

            nextBlinkTimer -= Time.deltaTime;
            if (nextBlinkTimer <= 0f && blinkCoroutine == null)
            {
                blinkCoroutine = StartCoroutine(DoTiredBlinkSequence());
                nextBlinkTimer = timeBetweenBlinks + Random.Range(-0.3f, 0.5f);
            }
        }
        else if (isExhausted && stamina != null && !stamina.IsExhausted)
        {
            ResetExhaustion();
        }
        else if (enablePeriodicBlinking && blinkCoroutine == null)
        {
            naturalBlinkTimer -= Time.deltaTime;
            if (naturalBlinkTimer <= 0f)
            {
                blinkCoroutine = StartCoroutine(DoTiredBlinkSequence());
                naturalBlinkTimer = periodicBlinkInterval + Random.Range(-6f, 10f);
            }
        }

#if ENABLE_INPUT_SYSTEM
        // Keyboard 'B' shortcut to manually blink (useful for testing or manual blink mechanic)
        if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.bKey.wasPressedThisFrame && blinkCoroutine == null)
        {
            TriggerBlink();
        }
#endif
    }

    /// <summary>
    /// Manually triggers a single blink sequence.
    /// </summary>
    public void TriggerBlink()
    {
        if (blinkCoroutine == null && gameObject.activeInHierarchy)
        {
            blinkCoroutine = StartCoroutine(DoTiredBlinkSequence());
        }
    }

    private IEnumerator DoTiredBlinkSequence()
    {
        OnBlinkStart?.Invoke();

        // 1. Eyelids shut down firmly into darkness
        float elapsed = 0f;
        while (elapsed < blinkCloseSpeed)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / blinkCloseSpeed);
            currentEyelidFraction = Mathf.SmoothStep(0f, 1f, t);
            UpdateEyelidPositions(currentEyelidFraction);
            yield return null;
        }
        currentEyelidFraction = 1f;
        UpdateEyelidPositions(1f);
        IsEyelidsShut = true;

        // Fired precisely at peak eyelid darkness
        OnBlinkPeak?.Invoke();

        // 2. Hold eyes shut (pitch darkness fatigue)
        yield return new WaitForSeconds(blinkHoldDuration);
        IsEyelidsShut = false;

        // 3. Eyelids struggle back open
        elapsed = 0f;
        while (elapsed < blinkOpenSpeed)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / blinkOpenSpeed);
            currentEyelidFraction = Mathf.SmoothStep(1f, 0f, t);
            UpdateEyelidPositions(currentEyelidFraction);
            yield return null;
        }
        currentEyelidFraction = 0f;
        UpdateEyelidPositions(0f);

        blinkCoroutine = null;
        OnBlinkEnd?.Invoke();
    }

    private void UpdateEyelidPositions(float fraction)
    {
        if (topEyelid == null || bottomEyelid == null) return;

        float canvasHeight = 1080f;
        if (blinkCanvas != null)
        {
            RectTransform canvasRect = blinkCanvas.GetComponent<RectTransform>();
            if (canvasRect != null && canvasRect.rect.height > 100f)
            {
                canvasHeight = canvasRect.rect.height;
            }
        }

        // Half screen plus slight overlap cushion (+15px) so eyelids meet and seal completely in the middle
        float halfHeight = (canvasHeight * 0.5f) + 15f;
        float targetHeight = halfHeight * Mathf.Clamp01(fraction);
        topEyelid.sizeDelta = new Vector2(0f, targetHeight);
        bottomEyelid.sizeDelta = new Vector2(0f, targetHeight);

        bool visible = fraction > 0.005f;
        if (topEyelidImg != null) topEyelidImg.enabled = visible;
        if (bottomEyelidImg != null) bottomEyelidImg.enabled = visible;
    }

    private void ApplySluggishSpeed()
    {
        if (fpc != null)
        {
            fpc.MoveSpeed = defaultMoveSpeed * exhaustedSpeedMultiplier;
            fpc.SprintSpeed = defaultMoveSpeed * exhaustedSpeedMultiplier; // Cannot sprint while exhausted
        }
    }
}
