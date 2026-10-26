using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
#if GOOGLE_MOBILE_ADS
using GoogleMobileAds.Api;
#endif

/// <summary>
/// Centralized monetization and ad orchestrator for Backrooms No Exit.
/// Handles Google Mobile Ads (AdMob) Rewarded Ads (Revives) and Interstitial Ads,
/// with intelligent frequency capping, death-pacing, and an analog-horror
/// CRT in-game/in-editor simulator when SDK is not present or in test mode.
/// </summary>
public class AdManager : MonoBehaviour
{
    public static AdManager Instance { get; private set; }

#pragma warning disable CS0414
    [Header("AdMob Configuration (Android)")]
    [Tooltip("Official AdMob App ID for Android.")]
    [SerializeField] string adMobAppId = "ca-app-pub-6774620515484669~7634094699";
    [Tooltip("Official AdMob Rewarded Ad Unit ID (Revive).")]
    [SerializeField] string rewardedAdUnitId = "ca-app-pub-6774620515484669/4588421116";
    [Tooltip("Official AdMob Interstitial Ad Unit ID (Death Screens).")]
    [SerializeField] string interstitialAdUnitId = "ca-app-pub-6774620515484669/9497132864";
#pragma warning restore CS0414

    [Header("Pacing & Frequency Capping")]
    [Tooltip("Minimum survival duration in seconds required to trigger an interstitial ad (prevents punishment on fast deaths).")]
    [SerializeField] float minSurvivalSeconds = 40f;
    [Tooltip("Minimum cooldown in seconds between full-screen interstitial ads.")]
    [SerializeField] float interstitialCooldownSeconds = 180f;
    [Tooltip("Show interstitial every Nth death (e.g. 2 = every second death).")]
    [SerializeField] int interstitialDeathInterval = 2;
    [Tooltip("Maximum revives permitted per level run to maintain horror tension.")]
    [SerializeField] int maxRevivesPerRun = 1;

    [Header("Simulation & Testing")]
    [Tooltip("If true or when SDK is missing, shows an analog-horror CRT ad simulation popup.")]
    [SerializeField] bool enableEditorSimulation = true;
    [Tooltip("Duration in seconds for simulated ads.")]
    [SerializeField] float simulationDuration = 2.4f;

    // Runtime tracking
    int deathCounter = 0;
    int revivesUsedThisRun = 0;
    float lastInterstitialTimestamp = -999f;
    float lastRunSurvivalTime = 0f;

    // Simulation UI elements
    UIDocument simDocument;
    VisualElement simBackdrop;
    Label simTitleLabel;
    Label simTimerLabel;
    Button simSkipButton;
    Action pendingRewardAction;
    Action pendingCompleteAction;

#if GOOGLE_MOBILE_ADS
    RewardedAd rewardedAd;
    InterstitialAd interstitialAd;
    bool isGmaInitialized = false;
#endif

    /// <summary>
    /// Checks if a player is eligible to revive via rewarded ad in the current run.
    /// </summary>
    public bool CanShowRevive => revivesUsedThisRun < maxRevivesPerRun;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoInitialize()
    {
        if (Instance == null)
        {
            GameObject go = new GameObject("AdManager");
            Instance = go.AddComponent<AdManager>();
            DontDestroyOnLoad(go);
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildSimulationUI();
        InitializeAds();
    }

    void InitializeAds()
    {
#if GOOGLE_MOBILE_ADS
        Debug.Log("[AdManager] Initializing Google Mobile Ads SDK...");
        MobileAds.Initialize(initStatus =>
        {
            Debug.Log("[AdManager] Google Mobile Ads initialized successfully.");
            isGmaInitialized = true;
            LoadRewardedAd();
            LoadInterstitialAd();
        });
#else
        Debug.Log("[AdManager] Running in Simulation Mode (Google Mobile Ads package not imported or GOOGLE_MOBILE_ADS symbol not set).");
#endif
    }

    // =========================================================================
    // PUBLIC INTERFACE: REWARDED (REVIVE)
    // =========================================================================

    /// <summary>
    /// Displays a rewarded video ad for resuscitation/revive.
    /// </summary>
    public void ShowRewardedRevive(Action onReward, Action onFailedOrSkipped = null)
    {
        if (!CanShowRevive)
        {
            Debug.LogWarning("[AdManager] Maximum revives reached for this run.");
            onFailedOrSkipped?.Invoke();
            return;
        }

#if GOOGLE_MOBILE_ADS
        if (isGmaInitialized && rewardedAd != null && rewardedAd.CanShowAd())
        {
            rewardedAd.Show(reward =>
            {
                revivesUsedThisRun++;
                Debug.Log($"[AdManager] Rewarded ad completed. Revives used: {revivesUsedThisRun}");
                onReward?.Invoke();
                LoadRewardedAd(); // Preload next
            });
            return;
        }
#endif

        if (enableEditorSimulation || Application.isEditor)
        {
            StartCoroutine(RunSimulationRoutine(
                title: "RECOVERING SIGNAL // EMERGENCY BROADCAST TRANSMISSION",
                isRewarded: true,
                onSuccess: () =>
                {
                    revivesUsedThisRun++;
                    onReward?.Invoke();
                },
                onFailed: onFailedOrSkipped
            ));
        }
        else
        {
            Debug.LogWarning("[AdManager] Rewarded ad not ready and simulation disabled.");
            onFailedOrSkipped?.Invoke();
        }
    }

    // =========================================================================
    // PUBLIC INTERFACE: INTERSTITIAL
    // =========================================================================

    /// <summary>
    /// Evaluates frequency capping, survival duration, and death counter.
    /// If eligible, shows an interstitial and invokes onComplete when closed.
    /// If not eligible, immediately invokes onComplete.
    /// </summary>
    public void TryShowInterstitial(Action onComplete)
    {
        deathCounter++;
        float timeSinceLastAd = Time.unscaledTime - lastInterstitialTimestamp;

        bool cooldownPassed = timeSinceLastAd >= interstitialCooldownSeconds;
        bool survivalTimePassed = lastRunSurvivalTime >= minSurvivalSeconds;
        bool frequencyIntervalMet = (deathCounter % interstitialDeathInterval) == 0;

        Debug.Log($"[AdManager] Interstitial Check -> Deaths: {deathCounter}, TimeSinceLast: {timeSinceLastAd:F1}s, RunTime: {lastRunSurvivalTime:F1}s | Eligible: {cooldownPassed && survivalTimePassed && frequencyIntervalMet}");

        if (!cooldownPassed || !survivalTimePassed || !frequencyIntervalMet)
        {
            onComplete?.Invoke();
            return;
        }

        lastInterstitialTimestamp = Time.unscaledTime;

#if GOOGLE_MOBILE_ADS
        if (isGmaInitialized && interstitialAd != null && interstitialAd.CanShowAd())
        {
            interstitialAd.OnAdFullScreenContentClosed += () =>
            {
                onComplete?.Invoke();
                LoadInterstitialAd(); // Preload next
            };
            interstitialAd.OnAdFullScreenContentFailed += (err) =>
            {
                Debug.LogWarning("[AdManager] Interstitial failed to show: " + err);
                onComplete?.Invoke();
                LoadInterstitialAd();
            };
            interstitialAd.Show();
            return;
        }
#endif

        if (enableEditorSimulation || Application.isEditor)
        {
            StartCoroutine(RunSimulationRoutine(
                title: "TRANSMISSION INTERRUPTION // INTERSTITIAL BROADCAST",
                isRewarded: false,
                onSuccess: onComplete,
                onFailed: onComplete
            ));
        }
        else
        {
            onComplete?.Invoke();
        }
    }

    /// <summary>
    /// Records survival duration of the run for ad-gating logic.
    /// </summary>
    public void RecordSurvivalTime(float seconds)
    {
        lastRunSurvivalTime = seconds;
    }

    /// <summary>
    /// Resets per-run state when starting or reloading a level.
    /// </summary>
    public void ResetRunState()
    {
        revivesUsedThisRun = 0;
        lastRunSurvivalTime = 0f;
    }

    // =========================================================================
    // SDK AD LOADING (ADMOB)
    // =========================================================================

#if GOOGLE_MOBILE_ADS
    void LoadRewardedAd()
    {
        if (rewardedAd != null)
        {
            rewardedAd.Destroy();
            rewardedAd = null;
        }

        var adRequest = new AdRequest();
        RewardedAd.Load(rewardedAdUnitId, adRequest, (ad, error) =>
        {
            if (error != null || ad == null)
            {
                Debug.LogWarning("[AdManager] Failed to load Rewarded Ad: " + error);
                return;
            }
            rewardedAd = ad;
            Debug.Log("[AdManager] Rewarded Ad successfully loaded.");
        });
    }

    void LoadInterstitialAd()
    {
        if (interstitialAd != null)
        {
            interstitialAd.Destroy();
            interstitialAd = null;
        }

        var adRequest = new AdRequest();
        InterstitialAd.Load(interstitialAdUnitId, adRequest, (ad, error) =>
        {
            if (error != null || ad == null)
            {
                Debug.LogWarning("[AdManager] Failed to load Interstitial Ad: " + error);
                return;
            }
            interstitialAd = ad;
            Debug.Log("[AdManager] Interstitial Ad successfully loaded.");
        });
    }
#endif

    // =========================================================================
    // ANALOG HORROR CRT IN-GAME SIMULATION (EDITOR & FALLBACK)
    // =========================================================================

    void BuildSimulationUI()
    {
        simDocument = gameObject.AddComponent<UIDocument>();
        simDocument.panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");

        VisualElement root = simDocument.rootVisualElement;
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.right = 0;
        root.style.top = 0;
        root.style.bottom = 0;

        simBackdrop = new VisualElement();
        simBackdrop.style.position = Position.Absolute;
        simBackdrop.style.left = 0;
        simBackdrop.style.right = 0;
        simBackdrop.style.top = 0;
        simBackdrop.style.bottom = 0;
        simBackdrop.style.backgroundColor = new Color(0.04f, 0.04f, 0.05f, 0.95f);
        simBackdrop.style.alignItems = Align.Center;
        simBackdrop.style.justifyContent = Justify.Center;
        simBackdrop.style.display = DisplayStyle.None;
        root.Add(simBackdrop);

        VisualElement card = new VisualElement();
        card.style.width = 540;
        card.style.paddingLeft = 32;
        card.style.paddingRight = 32;
        card.style.paddingTop = 28;
        card.style.paddingBottom = 28;
        card.style.backgroundColor = new Color(0.08f, 0.08f, 0.09f, 0.98f);
        card.style.borderLeftWidth = 4;
        card.style.borderLeftColor = new Color(0.95f, 0.75f, 0.2f, 1f); // Amber signal
        simBackdrop.Add(card);

        simTitleLabel = new Label("EMERGENCY BROADCAST TRANSMISSION");
        simTitleLabel.style.fontSize = 18;
        simTitleLabel.style.color = new Color(0.95f, 0.75f, 0.2f, 1f);
        simTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        simTitleLabel.style.whiteSpace = WhiteSpace.Normal;
        simTitleLabel.style.marginBottom = 16;
        card.Add(simTitleLabel);

        simTimerLabel = new Label("SIMULATING ADVERTISEMENT (2.4s)...");
        simTimerLabel.style.fontSize = 16;
        simTimerLabel.style.color = new Color(0.8f, 0.8f, 0.85f, 0.9f);
        simTimerLabel.style.marginBottom = 20;
        card.Add(simTimerLabel);

        simSkipButton = new Button(() => { StopAllCoroutines(); FinishSimulation(true); })
        {
            text = "▶ SKIP TEST AD (EDITOR ONLY)"
        };
        simSkipButton.style.height = 42;
        simSkipButton.style.backgroundColor = new Color(0.18f, 0.18f, 0.22f, 0.85f);
        simSkipButton.style.color = Color.white;
        simSkipButton.style.borderLeftWidth = 3;
        simSkipButton.style.borderLeftColor = new Color(0.95f, 0.75f, 0.2f, 1f);
        card.Add(simSkipButton);
    }

    IEnumerator RunSimulationRoutine(string title, bool isRewarded, Action onSuccess, Action onFailed)
    {
        pendingRewardAction = onSuccess;
        pendingCompleteAction = onFailed;

        if (simBackdrop != null)
        {
            simTitleLabel.text = title;
            simBackdrop.style.display = DisplayStyle.Flex;
        }

        float timer = simulationDuration;
        while (timer > 0f)
        {
            timer -= Time.unscaledDeltaTime;
            if (simTimerLabel != null)
            {
                simTimerLabel.text = $"TRANSMISSION IN PROGRESS: {Mathf.CeilToInt(timer)}s REMAINING...";
            }
            yield return null;
        }

        FinishSimulation(true);
    }

    void FinishSimulation(bool success)
    {
        if (simBackdrop != null)
        {
            simBackdrop.style.display = DisplayStyle.None;
        }

        if (success) pendingRewardAction?.Invoke();
        else pendingCompleteAction?.Invoke();

        pendingRewardAction = null;
        pendingCompleteAction = null;
    }
}
