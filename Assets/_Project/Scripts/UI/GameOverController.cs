using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using StarterAssets;
using Unity.Cinemachine;
using Cursor = UnityEngine.Cursor;
using Object = UnityEngine.Object;

/// <summary>
/// Handles game over (lose) logic and displays the analog-horror / CRT terminal
/// game over screen when the player is caught by an entity.
///
/// Builds its UI dynamically using UI Toolkit and the "GameOver style" stylesheet.
/// Works on both mobile (touch, safe-area aware) and PC (mouse/keyboard).
/// </summary>
public class GameOverController : MonoBehaviour
{
    public static GameOverController Instance { get; private set; }

    /// True while the game over sequence is active or screen is showing.
    public static bool IsGameOver { get; private set; }

    /// Fired when the player is defeated/caught.
    public static event Action OnPlayerDefeated;

    [Header("Timing & Jumpscare")]
    [Tooltip("Delay in seconds between being caught and showing the UI (plays attack/turn).")]
    [SerializeField] float caughtDelay = 2.4f;
    [Tooltip("If true, turns the camera to face the entity when caught.")]
    [SerializeField] bool lookAtKiller = true;
    [SerializeField] float cameraTurnSpeed = 12f;

    [Header("Audio")]
    [Tooltip("Sound played upon defeat. If null, picks a monster composition clip.")]
    [SerializeField] AudioClip defeatAudio;
    [Range(0f, 1f)]
    [SerializeField] float defeatAudioVolume = 0.9f;
    [Tooltip("Fade-in duration in seconds for defeat audio.")]
    [SerializeField] float defeatFadeInDuration = 0.8f;
    [Tooltip("Fade-out duration in seconds for defeat audio.")]
    [SerializeField] float defeatFadeOutDuration = 2.2f;

    [Header("Scene Navigation")]
    [SerializeField] string menuSceneName = "Menu";

    [Header("Labels & Text")]
    [SerializeField] string headingLabel = "SIGNAL TERMINATED";
    [SerializeField] string subtitleLabel = "ANOMALOUS ENTITY CONTACT // TRAUMATIC TERMINATION";
    [SerializeField] string retryButtonLabel = "RE-ENTER LEVEL";
    [SerializeField] string menuButtonLabel = "RETURN TO MENU";
    [SerializeField] string threatName = "ENTITY-001 (PIRATE CLARK)";

    [Header("Look & Fonts")]
    [SerializeField] Font uiFont;
    [SerializeField] string fontResourcePath = "Fonts/RobotoMono-Regular";
    [SerializeField] string styleResourcePath = "GameOver style";

    UIDocument document;
    PanelSettings panelSettings;
    Font font;

    VisualElement root;
    VisualElement backdrop;
    VisualElement card;
    Label recBadge;
    Label timeStatLabel;
    Button reviveButton;
    Button retryButton;
    Button menuButton;

    AudioSource audioSource;
    float timeSurvived;
    bool cursorUnlocked;
    CursorLockMode previousLockState;
    bool previousCursorVisible;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        IsGameOver = false;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        BuildUI();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            IsGameOver = false;
        }

        StopAllCoroutines();
        if (audioSource != null) audioSource.Stop();

        RestoreCursor();
    }

    /// <summary>
    /// Static helper called by VillainAI (or any hazard) when the player is caught.
    /// Auto-creates a controller if none exists in the scene.
    /// </summary>
    public static void TriggerDefeat(Transform killer = null)
    {
        if (IsGameOver || VictoryController.IsGameWon) return;

        if (Instance == null)
        {
            GameObject go = new GameObject("GameOverController");
            Instance = go.AddComponent<GameOverController>();
        }

        Instance.StartGameOverSequence(killer);
    }

    public void StartGameOverSequence(Transform killer)
    {
        if (IsGameOver || VictoryController.IsGameWon) return;
        IsGameOver = true;

        timeSurvived = Time.timeSinceLevelLoad;
        if (AdManager.Instance != null)
        {
            AdManager.Instance.RecordSurvivalTime(timeSurvived);
        }
        OnPlayerDefeated?.Invoke();

        // 1. Disable player movement and look
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            FirstPersonController fpc = player.GetComponent<FirstPersonController>();
            if (fpc != null) fpc.enabled = false;

            StarterAssetsInputs inputs = player.GetComponent<StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.MoveInput(Vector2.zero);
                inputs.LookInput(Vector2.zero);
                inputs.JumpInput(false);
                inputs.SprintInput(false);
            }
        }

        // 2. Hide mobile touchscreen UI so it doesn't overlap
        HideMobileControls();

        // 3. Play defeat sound
        PlayDefeatAudio();

        // 4. Run caught sequence (camera turn + delay, then show UI)
        StartCoroutine(CaughtSequenceRoutine(player != null ? player.transform : null, killer));
    }

    IEnumerator CaughtSequenceRoutine(Transform player, Transform killer)
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) mainCam = FindAnyObjectByType<Camera>();
        Transform cam = mainCam != null ? mainCam.transform : null;

        // 1. Release Cinemachine control so we have complete authority over the Main Camera
        if (mainCam != null)
        {
            CinemachineBrain brain = mainCam.GetComponent<CinemachineBrain>();
            if (brain != null) brain.enabled = false;
        }

        CinemachineCamera[] cineCams = FindObjectsByType<CinemachineCamera>();
        foreach (var c in cineCams) c.enabled = false;

        // Trigger maximum VHS glitch and static distortion for the jumpscare
        CameraVHSGlitch.TriggerKillBurst();

        Transform killerHead = null;
        if (killer != null && mainCam != null)
        {
            killerHead = FindHeadBone(killer);

            // Nudge killer to stand directly in front of the camera (1.15m away) for maximum jumpscare impact
            Vector3 toKiller = killer.position - cam.position;
            toKiller.y = 0f;
            float currentDist = toKiller.magnitude;
            if (currentDist > 1.35f || currentDist < 0.75f)
            {
                Vector3 desiredPos = cam.position + (currentDist > 0.1f ? toKiller.normalized : cam.forward) * 1.15f;
                desiredPos.y = killer.position.y;
                if (UnityEngine.AI.NavMesh.SamplePosition(desiredPos, out UnityEngine.AI.NavMeshHit navHit, 1.6f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    killer.position = navHit.position;
                }
            }

            // Orient killer directly toward the player camera
            Vector3 toCam = cam.position - killer.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.01f)
            {
                killer.rotation = Quaternion.LookRotation(toCam);
            }

            // Immediately launch the attack animation right at the camera
            Animator anim = killer.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.speed = 1f;
                anim.Play("Attack 1", 0, 0f);
            }
        }

        Vector3 startCamPos = cam != null ? cam.position : Vector3.zero;
        float totalDuration = Mathf.Max(caughtDelay, 2.4f);
        float impactTime = 0.52f; // Time when monster's strike connects with player's face
        bool impactTriggered = false;

        float timer = 0f;

        if (mainCam != null && killer != null)
        {
            while (timer < totalDuration)
            {
                float dt = Time.unscaledDeltaTime;
                timer += dt;

                Vector3 facePos = killerHead != null ? killerHead.position : (killer.position + Vector3.up * 1.55f);

                if (timer < impactTime)
                {
                    // =========================================================================
                    // PHASE 1: Jumpscare Snap into Monster's Face + Looming Dread Tremor
                    // =========================================================================
                    float phaseProgress = Mathf.Clamp01(timer / impactTime);

                    // Whip-pan rotation directly into Clark's face/eyes
                    if (lookAtKiller)
                    {
                        Vector3 lookDir = (facePos - cam.position).normalized;
                        if (lookDir.sqrMagnitude > 0.01f)
                        {
                            Quaternion targetRot = Quaternion.LookRotation(lookDir);
                            cam.rotation = Quaternion.Slerp(cam.rotation, targetRot, dt * cameraTurnSpeed * 1.6f);
                        }
                    }

                    // Smooth dolly in toward the face
                    Vector3 forwardDir = (facePos - cam.position).normalized;
                    Vector3 dollyTarget = facePos - forwardDir * 0.95f;
                    cam.position = Vector3.MoveTowards(cam.position, dollyTarget, dt * 2.2f);

                    // High-frequency fear shivering tremor
                    float shiver = (1f - phaseProgress) * 0.025f;
                    Vector3 tremor = (Mathf.Sin(timer * 55f) * cam.right + Mathf.Cos(timer * 65f) * cam.up) * shiver;
                    cam.position += tremor;
                }
                else
                {
                    // =========================================================================
                    // PHASE 2 & 3: LETHAL STRIKE IMPACT & VIOLENT SCREEN SHAKE & FLOOR COLLAPSE
                    // =========================================================================
                    if (!impactTriggered)
                    {
                        impactTriggered = true;

                        // Play impact thud
                        AudioClip landClip = Resources.Load<AudioClip>("SFX/Footsteps/Player_Land");
                        if (landClip != null && audioSource != null)
                        {
                            audioSource.PlayOneShot(landClip, 1.0f);
                        }
                    }

                    float timeSinceImpact = timer - impactTime;

                    // Violent screen shake trauma that decays smoothly over 0.65s
                    float trauma = Mathf.Clamp01(1f - (timeSinceImpact / 0.65f));
                    trauma = trauma * trauma; // exponential decay

                    // High-intensity rotational jolt (pitch head whip-back, roll tilt, yaw recoil)
                    float shakeYaw = Mathf.Sin(timer * 38f) * 16f * trauma;
                    float shakePitch = (Mathf.Cos(timer * 44f) * 12f - 14f) * trauma; // recoil back
                    float shakeRoll = Mathf.Sin(timer * 32f) * 22f * trauma;

                    // Player body collapse towards carpet floor
                    float collapseProgress = Mathf.Clamp01((timeSinceImpact - 0.15f) / 1.1f);
                    float floorY = Mathf.Max(startCamPos.y - 1.32f, 0.22f);
                    float currentY = Mathf.Lerp(cam.position.y, floorY, collapseProgress);

                    // Dutch angle as the player's head rests on the floor
                    float deathRoll = Mathf.Lerp(0f, 68f, collapseProgress);
                    float deathPitch = Mathf.Lerp(0f, 25f, collapseProgress);

                    Vector3 lookDir = (facePos - cam.position).normalized;
                    Quaternion baseRot = Quaternion.LookRotation(lookDir);
                    Quaternion shakeRot = Quaternion.Euler(shakePitch + deathPitch, shakeYaw, shakeRoll + deathRoll);

                    cam.rotation = Quaternion.Slerp(cam.rotation, baseRot * shakeRot, dt * 14f);

                    // Positional shake impulse
                    Vector3 shakePos = (cam.right * Mathf.Sin(timer * 40f) + cam.up * Mathf.Cos(timer * 50f)) * (0.065f * trauma);
                    cam.position = new Vector3(cam.position.x, currentY, cam.position.z) + shakePos;
                }

                yield return null;
            }
        }
        else
        {
            yield return new WaitForSecondsRealtime(caughtDelay);
        }

        ShowGameOverUI();
    }

    Transform FindHeadBone(Transform root)
    {
        if (root == null) return null;
        return FindChildRecursive(root, "head", "neck", "spine_03", "spine.003", "spine_02");
    }

    Transform FindChildRecursive(Transform parent, params string[] names)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            string lowerName = child.name.ToLowerInvariant();
            foreach (string n in names)
            {
                if (lowerName.Contains(n)) return child;
            }
            Transform found = FindChildRecursive(child, names);
            if (found != null) return found;
        }
        return null;
    }

    void ShowGameOverUI()
    {
        if (timeStatLabel != null)
        {
            TimeSpan span = TimeSpan.FromSeconds(timeSurvived);
            timeStatLabel.text = string.Format("{0:00}:{1:00}", (int)span.TotalMinutes, span.Seconds);
        }

        if (reviveButton != null)
        {
            bool canRevive = PlatformUtils.IsMobile && (AdManager.Instance == null || AdManager.Instance.CanShowRevive);
            reviveButton.style.display = canRevive ? DisplayStyle.Flex : DisplayStyle.None;
            reviveButton.SetEnabled(true);
            reviveButton.text = "▶ RECOVER SIGNAL (REVIVE)";
        }

        if (backdrop != null)
        {
            backdrop.style.display = DisplayStyle.Flex;
            backdrop.AddToClassList("is-visible");
        }

        UnlockCursor();
        if (reviveButton != null && reviveButton.style.display == DisplayStyle.Flex)
            reviveButton.Focus();
        else
            retryButton?.Focus();

        StartCoroutine(BlinkRecRoutine());
    }

    IEnumerator BlinkRecRoutine()
    {
        while (IsGameOver && recBadge != null)
        {
            recBadge.style.opacity = 1f;
            yield return new WaitForSecondsRealtime(0.6f);
            if (!IsGameOver) break;
            recBadge.style.opacity = 0.15f;
            yield return new WaitForSecondsRealtime(0.4f);
        }
    }

    void OnReviveClicked()
    {
        if (AdManager.Instance == null)
        {
            ExecuteRevive();
            return;
        }

        reviveButton.SetEnabled(false);
        reviveButton.text = "CONNECTING BROADCAST...";

        AdManager.Instance.ShowRewardedRevive(
            onReward: () =>
            {
                ExecuteRevive();
            },
            onFailedOrSkipped: () =>
            {
                if (reviveButton != null)
                {
                    reviveButton.SetEnabled(true);
                    reviveButton.text = "▶ RECOVER SIGNAL (FAILED - RETRY)";
                }
            }
        );
    }

    public void ExecuteRevive()
    {
        IsGameOver = false;
        StopAllCoroutines();
        if (audioSource != null) audioSource.Stop();

        // 1. Hide UI elements
        if (backdrop != null)
        {
            backdrop.style.display = DisplayStyle.None;
            backdrop.RemoveFromClassList("is-visible");
        }

        RestoreCursor();

        // 2. Restore camera & Cinemachine
        Camera mainCam = Camera.main;
        if (mainCam == null) mainCam = FindAnyObjectByType<Camera>();
        if (mainCam != null)
        {
            CinemachineBrain brain = mainCam.GetComponent<CinemachineBrain>();
            if (brain != null) brain.enabled = true;
        }

        CinemachineCamera[] cineCams = FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include);
        foreach (var c in cineCams) c.enabled = true;

        // 3. Restore player controller & inputs
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            FirstPersonController fpc = player.GetComponent<FirstPersonController>();
            if (fpc != null) fpc.enabled = true;

            PlayerStamina.Instance?.ResetStamina();

            StarterAssetsInputs inputs = player.GetComponent<StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.MoveInput(Vector2.zero);
                inputs.LookInput(Vector2.zero);
                inputs.JumpInput(false);
                inputs.SprintInput(false);
            }
        }

        // 4. Restore touch controls (if on touch platform)
        bool isTouch = PlatformUtils.IsMobile;
        if (isTouch)
        {
            TouchscreenInput touchUI = FindAnyObjectByType<TouchscreenInput>(FindObjectsInactive.Include);
            if (touchUI != null) touchUI.gameObject.SetActive(true);
            GameObject touchGo = GameObject.Find("UI_TouchScreenInput");
            if (touchGo != null) touchGo.SetActive(true);
        }

        // 5. Reposition and calm monster
        VillainAI villain = VillainAI.Instance != null ? VillainAI.Instance : FindAnyObjectByType<VillainAI>();
        if (villain != null)
        {
            villain.ResetAfterRevive(6f);
        }

        // 6. Play recover audio sting / effect
        AudioClip recoverClip = Resources.Load<AudioClip>("SFX/juanjo_ sound - elevated");
        if (recoverClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(recoverClip, 0.85f);
        }

        // 7. VHS tape glitch burst to indicate signal recovery
        CameraVHSGlitch.TriggerKillBurst();
    }

    void OnRetryClicked()
    {
        IsGameOver = false;
        Time.timeScale = 1f;
        RestoreCursor();

        if (AdManager.Instance != null)
        {
            AdManager.Instance.TryShowInterstitial(() =>
            {
                AdManager.Instance.ResetRunState();
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            });
        }
        else
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }

    void OnMenuClicked()
    {
        IsGameOver = false;
        Time.timeScale = 1f;
        RestoreCursor();

        Action loadMenu = () =>
        {
            if (AdManager.Instance != null) AdManager.Instance.ResetRunState();
            if (Application.CanStreamedLevelBeLoaded(menuSceneName))
                SceneManager.LoadScene(menuSceneName);
            else
                SceneManager.LoadScene(0);
        };

        if (AdManager.Instance != null)
        {
            AdManager.Instance.TryShowInterstitial(loadMenu);
        }
        else
        {
            loadMenu();
        }
    }

    void HideMobileControls()
    {
        // Search and disable UI_TouchScreenInput or TouchscreenInput objects
        TouchscreenInput touchUI = FindAnyObjectByType<TouchscreenInput>(FindObjectsInactive.Include);
        if (touchUI != null) touchUI.gameObject.SetActive(false);

        GameObject touchGo = GameObject.Find("UI_TouchScreenInput");
        if (touchGo != null) touchGo.SetActive(false);
    }

    void PlayDefeatAudio()
    {
        AudioClip clip = defeatAudio;
        if (clip == null)
            clip = MonsterCompositionAudio.GetRandomCompositionClip();
        if (clip == null)
            clip = Resources.Load<AudioClip>("SFX/juanjo_ sound - signal lost");

        if (clip != null && audioSource != null)
        {
            StartCoroutine(FadeDefeatAudioRoutine(clip));
        }
    }

    IEnumerator FadeDefeatAudioRoutine(AudioClip clip)
    {
        audioSource.clip = clip;
        audioSource.ignoreListenerPause = true;
        audioSource.volume = 0f;
        audioSource.Play();

        float targetVolume = defeatAudioVolume * GameSettings.MasterVolume;
        float clipDuration = clip.length;

        float fadeIn = Mathf.Min(defeatFadeInDuration, clipDuration * 0.3f);
        float fadeOut = Mathf.Min(defeatFadeOutDuration, clipDuration * 0.45f);
        float sustain = Mathf.Max(0f, clipDuration - fadeIn - fadeOut);

        // 1. Fade in
        float timer = 0f;
        while (timer < fadeIn)
        {
            timer += Time.unscaledDeltaTime;
            audioSource.volume = Mathf.Lerp(0f, targetVolume, Mathf.Clamp01(timer / fadeIn));
            yield return null;
        }
        audioSource.volume = targetVolume;

        // 2. Sustain
        if (sustain > 0f)
        {
            yield return new WaitForSecondsRealtime(sustain);
        }

        // 3. Fade out
        timer = 0f;
        while (timer < fadeOut)
        {
            timer += Time.unscaledDeltaTime;
            audioSource.volume = Mathf.Lerp(targetVolume, 0f, Mathf.Clamp01(timer / fadeOut));
            yield return null;
        }

        audioSource.volume = 0f;
        audioSource.Stop();
    }

    void UnlockCursor()
    {
        if (cursorUnlocked) return;
        previousLockState = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        cursorUnlocked = true;
    }

    void RestoreCursor()
    {
        if (!cursorUnlocked) return;
        Cursor.lockState = previousLockState;
        Cursor.visible = previousCursorVisible;
        cursorUnlocked = false;
    }

    void BuildUI()
    {
        font = uiFont != null ? uiFont : LoadFont();

        panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
        if (panelSettings == null)
        {
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "GameOver Panel Settings";
            panelSettings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
            panelSettings.sortingOrder = 150; // Above pause menu and touch controls
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
        }

        document = gameObject.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;

        root = document.rootVisualElement;
        root.AddToClassList("gameover-root");

        StyleSheet sheet = Resources.Load<StyleSheet>(styleResourcePath);
        if (sheet != null) root.styleSheets.Add(sheet);
        else Debug.LogWarning("GameOverController: stylesheet not found at " + styleResourcePath, this);

        // Backdrop
        backdrop = new VisualElement();
        backdrop.AddToClassList("gameover-backdrop");
        backdrop.style.display = DisplayStyle.None;
        root.Add(backdrop);

        // Vignette & Scanlines
        VisualElement vignette = new VisualElement();
        vignette.AddToClassList("gameover-vignette");
        backdrop.Add(vignette);

        VisualElement scanlines = new VisualElement();
        scanlines.AddToClassList("gameover-scanlines");
        backdrop.Add(scanlines);

        // Top badges
        recBadge = MakeLabel("● REC [SIGNAL TERMINATED]", "gameover-rec-badge");
        backdrop.Add(recBadge);

        Label chBadge = MakeLabel("CAM_01 // LEVEL_0", "gameover-channel-badge");
        backdrop.Add(chBadge);

        // Center card
        card = new VisualElement();
        card.AddToClassList("gameover-card");
        backdrop.Add(card);

        card.Add(MakeLabel(headingLabel, "gameover-heading"));
        card.Add(MakeLabel(subtitleLabel, "gameover-subheading"));

        VisualElement divider1 = new VisualElement();
        divider1.AddToClassList("gameover-divider");
        card.Add(divider1);

        // Stats Box
        VisualElement statsBox = new VisualElement();
        statsBox.AddToClassList("gameover-stats-box");
        card.Add(statsBox);

        statsBox.Add(BuildStatRow("ZONE", "LEVEL 0 - THE LOBBY"));
        statsBox.Add(BuildStatRow("THREAT", threatName));
        statsBox.Add(BuildStatRow("TRAUMA", "FATAL CRANIAL IMPACT", isDanger: true));
        statsBox.Add(BuildStatRow("VITALS", "000 BPM [ASYSTOLE]", isDanger: true));

        VisualElement timeRow = new VisualElement();
        timeRow.AddToClassList("gameover-stat-row");
        timeRow.Add(MakeLabel("TIME SURVIVED", "gameover-stat-label"));
        timeStatLabel = MakeLabel("00:00", "gameover-stat-value");
        timeRow.Add(timeStatLabel);
        statsBox.Add(timeRow);

        // Buttons
        if (PlatformUtils.IsMobile)
        {
            reviveButton = MakeButton("▶ RECOVER SIGNAL (REVIVE)", OnReviveClicked);
            reviveButton.AddToClassList("gameover-button--revive");
            card.Add(reviveButton);
        }

        retryButton = MakeButton(retryButtonLabel, OnRetryClicked);
        card.Add(retryButton);

        menuButton = MakeButton(menuButtonLabel, OnMenuClicked, secondary: true);
        card.Add(menuButton);
    }

    VisualElement BuildStatRow(string labelText, string valueText, bool isDanger = false)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("gameover-stat-row");
        row.Add(MakeLabel(labelText, "gameover-stat-label"));
        string valueClass = isDanger ? "gameover-stat-value gameover-stat-value--danger" : "gameover-stat-value";
        row.Add(MakeLabel(valueText, valueClass));
        return row;
    }

    Label MakeLabel(string text, string ussClass)
    {
        Label label = new Label(text);
        if (!string.IsNullOrEmpty(ussClass))
        {
            string[] classes = ussClass.Split(' ');
            foreach (string c in classes)
            {
                if (!string.IsNullOrEmpty(c)) label.AddToClassList(c);
            }
        }
        ApplyFont(label);
        return label;
    }

    Button MakeButton(string text, Action onClick, bool secondary = false)
    {
        Button button = new Button(onClick) { text = text };
        button.AddToClassList("gameover-button");
        if (secondary) button.AddToClassList("gameover-button--secondary");
        ApplyFont(button);
        return button;
    }

    void ApplyFont(VisualElement element)
    {
        if (font != null) element.style.unityFontDefinition = new StyleFontDefinition(font);
    }

    Font LoadFont()
    {
        if (string.IsNullOrEmpty(fontResourcePath)) return null;
        return Resources.Load<Font>(fontResourcePath);
    }
}
