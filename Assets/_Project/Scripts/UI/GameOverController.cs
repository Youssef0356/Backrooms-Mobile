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
    [Tooltip("Iconic jumpscare scream played when killed by the Smily Hound / Dog (FNAF 2 full scream).")]
    [SerializeField] AudioClip houndKillScream;
    [Range(0f, 1f)]
    [SerializeField] float defeatAudioVolume = 0.9f;
    [Tooltip("Fade-in duration in seconds for defeat audio.")]
    [SerializeField] float defeatFadeInDuration = 0.8f;
    [Tooltip("Fade-out duration in seconds for defeat audio.")]
    [SerializeField] float defeatFadeOutDuration = 2.2f;

    private Transform activeKiller;

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
        activeKiller = killer;

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
        SmilyMonsterGait houndGait = killer != null ? (killer.GetComponent<SmilyMonsterGait>() ?? killer.GetComponentInChildren<SmilyMonsterGait>()) : null;
        SmilyHoundAI houndAI = killer != null ? (killer.GetComponent<SmilyHoundAI>() ?? killer.GetComponentInChildren<SmilyHoundAI>()) : null;
        bool isHound = houndGait != null || houndAI != null || (killer != null && (killer.name.ToLower().Contains("hound") || killer.name.ToLower().Contains("dog")));

        if (killer != null && mainCam != null)
        {
            if (houndGait != null && houndGait.Head != null)
            {
                killerHead = houndGait.Head;
            }
            else
            {
                killerHead = FindHeadBone(killer);
            }

            // Disable CharacterController on killer during death choreography so placement is exact
            CharacterController killerCC = killer.GetComponent<CharacterController>();
            if (killerCC != null) killerCC.enabled = false;

            // Frame killer at exact choreography distance:
            // - Bacteria (2.0x scale playing Zombie Neck Bite): 1.65m so its forward grab/bite lands directly on the camera without chest clipping
            // - Smily Hound (authentic quadruped scale rearing up & mauling): 1.10m so its snarling jaws fill the frame cleanly
            bool isBacteria = !isHound && killer.name.ToLowerInvariant().Contains("bacteria");
            float standDist = isHound ? 1.10f : (isBacteria ? 1.65f : 1.25f);

            Vector3 flatCamForward = new Vector3(cam.forward.x, 0f, cam.forward.z);
            if (flatCamForward.sqrMagnitude < 0.01f) flatCamForward = Vector3.forward;
            flatCamForward.Normalize();

            Vector3 toKiller = killer.position - cam.position;
            toKiller.y = 0f;
            Vector3 placementDir = toKiller.sqrMagnitude > 0.04f ? toKiller.normalized : flatCamForward;

            // Ensure placementDir does not place the killer inside or behind a 0.25m wall
            int wallMask = LayerMask.GetMask("Default");
            Vector3 chestOrigin = new Vector3(cam.position.x, killer.position.y + 0.9f, cam.position.z);
            if (Physics.Raycast(chestOrigin, placementDir, out RaycastHit wallHit, standDist + 0.55f, wallMask, QueryTriggerInteraction.Ignore))
            {
                // Find the clearest open direction around the player
                float bestClearDist = wallHit.distance;
                Vector3 bestDir = placementDir;
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * 45f;
                    Vector3 testDir = Quaternion.Euler(0f, angle, 0f) * placementDir;
                    if (!Physics.Raycast(chestOrigin, testDir, out RaycastHit th, standDist + 0.55f, wallMask, QueryTriggerInteraction.Ignore))
                    {
                        bestDir = testDir;
                        bestClearDist = standDist + 0.55f;
                        break;
                    }
                    else if (th.distance > bestClearDist)
                    {
                        bestClearDist = th.distance;
                        bestDir = testDir;
                    }
                }
                placementDir = bestDir;
                standDist = Mathf.Min(standDist, Mathf.Max(0.95f, bestClearDist - 0.45f));
            }

            Vector3 desiredPos = cam.position + placementDir * standDist;
            desiredPos.y = killer.position.y;
            killer.position = desiredPos;

            // Orient killer directly toward the player camera
            Vector3 toCam = cam.position - killer.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.01f)
            {
                killer.rotation = Quaternion.LookRotation(toCam.normalized);
            }

            // Trigger dedicated visceral kill animation
            if (houndGait != null)
            {
                houndGait.TriggerKillJumpscare(cam);
            }

            Animator anim = killer.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.speed = 1f;
                if (isHound)
                {
                    if (anim.HasState(0, Animator.StringToHash("Attack")))
                        anim.Play("Attack", 0, 0f);
                }
                else
                {
                    if (anim.HasState(0, Animator.StringToHash("Attack 1")))
                        anim.Play("Attack 1", 0, 0f);
                    else if (anim.HasState(0, Animator.StringToHash("Zombie Neck Bite")))
                        anim.Play("Zombie Neck Bite", 0, 0f);
                }
            }
        }

        Vector3 startCamPos = cam != null ? cam.position : Vector3.zero;
        bool isBacteriaKill = !isHound && killer != null && killer.name.ToLowerInvariant().Contains("bacteria");
        float totalDuration = Mathf.Max(caughtDelay, 2.45f);
        float impactTime = isHound ? 0.48f : (isBacteriaKill ? 0.78f : 0.52f);
        float collapseStartTime = isHound ? 0.65f : (isBacteriaKill ? 1.45f : 0.65f);
        bool impactTriggered = false;

        float timer = 0f;

        if (mainCam != null && killer != null)
        {
            while (timer < totalDuration)
            {
                float dt = Time.unscaledDeltaTime;
                timer += dt;

                Vector3 facePos = killerHead != null
                    ? killerHead.position
                    : (killer.position + Vector3.up * (isHound ? 1.15f : (isBacteriaKill ? 2.15f : 1.55f)));

                // Safety guarantee: if a mesh/bone ever reports a low Y for 2.0x Bacteria, keep focus on its face level
                if (isBacteriaKill && facePos.y < killer.position.y + 1.55f)
                {
                    facePos.y = killer.position.y + 2.15f;
                }

                if (timer < impactTime)
                {
                    // =========================================================================
                    // PHASE 1: Grappled / Cornered — Camera Immediately Locks Onto Monster's Face
                    // =========================================================================
                    float phaseProgress = Mathf.Clamp01(timer / impactTime);

                    if (lookAtKiller)
                    {
                        Vector3 lookDir = (facePos - cam.position).normalized;
                        if (lookDir.sqrMagnitude > 0.01f)
                        {
                            Quaternion targetRot = Quaternion.LookRotation(lookDir);
                            // Rapidly whip camera straight onto the monster's face so player sees the attack clearly
                            cam.rotation = Quaternion.Slerp(cam.rotation, targetRot, dt * Mathf.Max(cameraTurnSpeed, 14f) * 2.5f);
                        }
                    }

                    // For Bacteria (Zombie Neck Bite), lift camera slightly to 1.82m as it grabs the player's shoulders/neck
                    // For Hound, hold eye level at 0.92m in front of its rearing jaws
                    Vector3 targetCamPos = startCamPos;
                    if (isBacteriaKill)
                    {
                        targetCamPos = new Vector3(startCamPos.x, Mathf.Lerp(startCamPos.y, killer.position.y + 1.82f, phaseProgress), startCamPos.z);
                    }

                    float shiver = (1f - phaseProgress * 0.5f) * 0.022f;
                    Vector3 tremor = (Mathf.Sin(timer * 58f) * cam.right + Mathf.Cos(timer * 68f) * cam.up) * shiver;
                    cam.position = Vector3.Lerp(cam.position, targetCamPos, dt * 10f) + tremor;
                }
                else
                {
                    // =========================================================================
                    // PHASE 2 & 3: LETHAL NECK BITE / MAUL IMPACT -> FLOOR COLLAPSE
                    // =========================================================================
                    if (!impactTriggered)
                    {
                        impactTriggered = true;
                        CameraVHSGlitch.TriggerKillBurst();

                        AudioClip landClip = Resources.Load<AudioClip>("SFX/Footsteps/Player_Land");
                        if (landClip != null && audioSource != null)
                        {
                            audioSource.PlayOneShot(landClip, 1.0f);
                        }
                    }

                    float timeSinceImpact = timer - impactTime;
                    float trauma = Mathf.Clamp01(1f - (timeSinceImpact / 0.75f));
                    trauma = trauma * trauma;

                    float shakeYaw = Mathf.Sin(timer * 42f) * 18f * trauma;
                    float shakePitch = (Mathf.Cos(timer * 48f) * 14f - 12f) * trauma;
                    float shakeRoll = Mathf.Sin(timer * 36f) * 24f * trauma;

                    float collapseProgress = timer < collapseStartTime
                        ? 0f
                        : Mathf.Clamp01((timer - collapseStartTime) / 0.85f);

                    float heldY = isBacteriaKill ? (killer.position.y + 1.75f) : startCamPos.y;
                    float floorY = Mathf.Max(killer.position.y + 0.24f, 0.22f);
                    float currentY = Mathf.Lerp(heldY, floorY, Mathf.SmoothStep(0f, 1f, collapseProgress));

                    float deathRoll = Mathf.Lerp(0f, 64f, collapseProgress);
                    float deathPitch = Mathf.Lerp(0f, 20f, collapseProgress);

                    Vector3 lookDir = (facePos - cam.position).normalized;
                    if (lookDir.sqrMagnitude < 0.01f) lookDir = cam.forward;
                    Quaternion baseRot = Quaternion.LookRotation(lookDir);
                    Quaternion shakeRot = Quaternion.Euler(shakePitch + deathPitch, shakeYaw, shakeRoll + deathRoll);

                    cam.rotation = Quaternion.Slerp(cam.rotation, baseRot * shakeRot, dt * 16f);

                    Vector3 shakePos = (cam.right * Mathf.Sin(timer * 44f) + cam.up * Mathf.Cos(timer * 54f)) * (0.06f * trauma);
                    cam.position = new Vector3(startCamPos.x, currentY, startCamPos.z) + shakePos;
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

        Animator anim = root.GetComponentInChildren<Animator>();
        if (anim != null && anim.isHuman)
        {
            Transform humanHead = anim.GetBoneTransform(HumanBodyBones.Head);
            if (humanHead != null) return humanHead;
        }

        return FindChildRecursive(root, "mixamorig:head", "head_05", "head", "neck");
    }

    Transform FindChildRecursive(Transform parent, params string[] names)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            // Ignore SkinnedMeshRenderer / MeshRenderer child objects (like Bacteria's 'CableHead' or 'Head' mesh at y=0)
            if (child.GetComponent<Renderer>() == null)
            {
                string lowerName = child.name.ToLowerInvariant();
                foreach (string n in names)
                {
                    if (lowerName.Contains(n)) return child;
                }
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
        AudioClip clip = null;
        bool isHoundKill = false;

        // Check if killed by Hound / Dog
        if (activeKiller != null)
        {
            string killerName = activeKiller.name.ToLower();
            if (activeKiller.GetComponent<SmilyHoundAI>() != null ||
                activeKiller.GetComponentInChildren<SmilyHoundAI>() != null ||
                activeKiller.GetComponent<SmilyMonsterGait>() != null ||
                activeKiller.GetComponentInChildren<SmilyMonsterGait>() != null ||
                killerName.Contains("hound") || killerName.Contains("dog") || killerName.Contains("smily"))
            {
                isHoundKill = true;
                clip = HoundAudio.KillScream;
            }
        }

        if (clip == null)
            clip = defeatAudio;
        if (clip == null)
            clip = MonsterCompositionAudio.GetRandomCompositionClip();
        if (clip == null)
            clip = Resources.Load<AudioClip>("SFX/juanjo_ sound - signal lost");

        if (clip != null && audioSource != null)
        {
            StartCoroutine(FadeDefeatAudioRoutine(clip, isHoundKill));
        }
    }

    IEnumerator FadeDefeatAudioRoutine(AudioClip clip, bool isInstantScream = false)
    {
        audioSource.clip = clip;
        audioSource.ignoreListenerPause = true;

        float targetVolume = (isInstantScream ? 1.0f : defeatAudioVolume) * GameSettings.MasterVolume;
        float clipDuration = clip.length;

        float fadeIn = isInstantScream ? 0f : Mathf.Min(defeatFadeInDuration, clipDuration * 0.3f);
        float fadeOut = isInstantScream ? 0.3f : Mathf.Min(defeatFadeOutDuration, clipDuration * 0.45f);
        float sustain = Mathf.Max(0f, clipDuration - fadeIn - fadeOut);

        audioSource.volume = isInstantScream ? targetVolume : 0f;
        audioSource.Play();

        // 1. Fade in
        if (fadeIn > 0.01f)
        {
            float timer = 0f;
            while (timer < fadeIn)
            {
                timer += Time.unscaledDeltaTime;
                audioSource.volume = Mathf.Lerp(0f, targetVolume, Mathf.Clamp01(timer / fadeIn));
                yield return null;
            }
        }
        audioSource.volume = targetVolume;

        // 2. Sustain
        if (sustain > 0f)
        {
            yield return new WaitForSecondsRealtime(sustain);
        }

        // 3. Fade out
        float outTimer = 0f;
        while (outTimer < fadeOut)
        {
            outTimer += Time.unscaledDeltaTime;
            audioSource.volume = Mathf.Lerp(targetVolume, 0f, Mathf.Clamp01(outTimer / fadeOut));
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
