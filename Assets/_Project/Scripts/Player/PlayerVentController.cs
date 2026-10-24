using System.Collections;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lets the player duck into a wall vent to shake off the villain.
///
/// While the villain is hunting the player and a vent is in front of him, a prompt appears
/// ([E] / gamepad X / on-screen tap). Pressing it plays a full first-person sequence:
///   kneel at the grille -> grille swings open -> crawl through the hole -> turn around -> grille shuts.
/// Inside the duct the player can look around a little and watch the corridor through the bars.
/// The villain walks to the grille, lingers, then forgets the player and wanders off.
/// Pressing the prompt again plays the reverse (grille opens, crawl out, stand up).
/// </summary>
[DisallowMultipleComponent]
public class PlayerVentController : MonoBehaviour
{
    public static PlayerVentController Instance { get; private set; }

    /// <summary>True from the moment the player commits to entering until he starts crawling back out.</summary>
    public static bool IsInVent { get; private set; }

    /// <summary>True during any scripted vent sequence or while hidden (player movement is overridden).</summary>
    public static bool HasControl { get; private set; }

    public enum VentState { Idle, Entering, Hidden, Exiting }

    [Header("Interaction")]
    [Tooltip("How far in front of the grille the prompt appears (metres).")]
    public float interactRange = 2.6f;
    [Tooltip("Maximum angle between the view direction and the vent for the prompt to show.")]
    [Range(20f, 120f)] public float facingAngle = 80f;
    [Tooltip("The villain must be at least this far away or he would see you crawl in.")]
    public float minVillainDistance = 2.2f;
    [Tooltip("Also allow hiding while the villain is searching (not just actively chasing).")]
    public bool allowDuringSearch = true;
    [Tooltip("Allow entering vents at any time, not only while being actively pursued.")]
    public bool allowAnytime = true;
    [Tooltip("Seconds after leaving a vent before another can be entered.")]
    public float reentryCooldown = 1.0f;

    [Header("Entry / Exit Timing (seconds)")]
    public float approachTime = 0.6f;
    public float flapOpenDelay = 0.35f;
    public float flapOpenTime = 0.45f;
    public float crawlStart = 0.75f;
    public float crawlTime = 1.15f;
    public float turnStart = 1.9f;
    public float turnTime = 0.7f;

    [Header("Camera Poses (eye height above floor, metres)")]
    public float kneelEyeHeight = 0.95f;
    public float crawlEyeHeight = VentHideSpot.CrawlEyeHeight;

    [Header("Look While Hidden")]
    public float maxYaw = 62f;
    public Vector2 pitchRange = new Vector2(-24f, 32f);

    public VentState State { get; private set; } = VentState.Idle;

    FirstPersonController fpc;
    CharacterController cc;
    StarterAssetsInputs inputs;
    FirstPersonMotion motion;
    Transform camRoot;

    VentHideSpot currentVent;
    VentHUD hud;

    Vector3 savedPos;
    float savedYaw;
    float savedPitch;
    Vector3 savedCamLocalPos;
    float floorY;
    float standEye;

    float hiddenYawOffset;
    float hiddenPitch;
    float cooldown;
    bool tapped;
    float swayPhase;

    // ---------------- Lifecycle ----------------

    void Awake()
    {
        Instance = this;
        IsInVent = false;
        HasControl = false;

        fpc = GetComponent<FirstPersonController>();
        cc = GetComponent<CharacterController>();
        inputs = GetComponent<StarterAssetsInputs>();
        motion = GetComponent<FirstPersonMotion>();
        camRoot = fpc != null && fpc.CinemachineCameraTarget != null ? fpc.CinemachineCameraTarget.transform : null;
    }

    void OnEnable()
    {
        VentHUD.OnActionPressed += HandleTap;
        GameOverController.OnPlayerDefeated += HandleDefeated;
        VictoryController.OnPlayerEscaped += HandleDefeated;
    }

    void OnDisable()
    {
        VentHUD.OnActionPressed -= HandleTap;
        GameOverController.OnPlayerDefeated -= HandleDefeated;
        VictoryController.OnPlayerEscaped -= HandleDefeated;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        IsInVent = false;
        HasControl = false;
    }

    void HandleTap()
    {
        if (Time.timeScale > 0f) tapped = true;
    }

    void HandleDefeated()
    {
        if (State != VentState.Idle) ForceRelease();
    }

    // ---------------- Per-frame ----------------

    void Update()
    {
        if (fpc == null || camRoot == null) return;
        if (hud == null) hud = VentHUD.GetOrCreate();

        bool interact = ConsumeInteract();

        if (GameOverController.IsGameOver || VictoryController.IsGameWon)
        {
            hud.SetPrompt(VentHUD.PromptMode.None);
            return;
        }

        if (PauseMenuController.IsPaused) return;

        if (cooldown > 0f) cooldown -= Time.deltaTime;

        switch (State)
        {
            case VentState.Idle:
                UpdateIdlePrompt(interact);
                break;

            case VentState.Hidden:
                UpdateHidden(interact);
                break;

            default:
                hud.SetPrompt(VentHUD.PromptMode.None);
                break;
        }
    }

    bool ConsumeInteract()
    {
        bool pressed = tapped;
        tapped = false;

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame) pressed = true;

        Gamepad gp = Gamepad.current;
        if (gp != null && gp.buttonWest.wasPressedThisFrame) pressed = true;

        return pressed;
    }

    // ---------------- Idle: show prompt when it makes sense ----------------

    void UpdateIdlePrompt(bool interact)
    {
        VillainAI villain = VillainAI.Instance;
        bool threatened = villain != null && villain.isActiveAndEnabled &&
            (villain.CurrentState == VillainAI.State.Chase ||
             (allowDuringSearch && villain.CurrentState == VillainAI.State.Investigate));

        VentHideSpot spot = null;
        if ((threatened || allowAnytime) && cooldown <= 0f) spot = FindUsableVent();

        if (spot == null)
        {
            hud.SetPrompt(VentHUD.PromptMode.None);
            return;
        }

        if (threatened && villain != null)
        {
            float villainDist = Vector3.Distance(villain.transform.position, transform.position);
            if (villainDist < minVillainDistance)
            {
                hud.SetPrompt(VentHUD.PromptMode.TooClose);
                return;
            }
        }

        hud.SetPrompt(VentHUD.PromptMode.Enter);
        if (interact) StartCoroutine(EnterRoutine(spot));
    }

    VentHideSpot FindUsableVent()
    {
        if (fpc != null && !fpc.Grounded) return null;

        VentHideSpot best = null;
        float bestDist = float.MaxValue;
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        fwd.Normalize();

        for (int i = 0; i < VentHideSpot.All.Count; i++)
        {
            VentHideSpot v = VentHideSpot.All[i];
            if (v == null) continue;

            Vector3 local = v.transform.InverseTransformPoint(transform.position);
            if (local.z > -0.1f || local.z < -interactRange) continue;      // must be on the room side, within range
            if (Mathf.Abs(local.x) > 1.4f) continue;                        // roughly in line with the grille

            float groundY = cc != null ? cc.bounds.min.y : transform.position.y;
            if (Mathf.Abs(groundY - v.transform.position.y) > 0.4f) continue; // not standing on furniture

            Vector3 toVent = v.GrillePoint - transform.position;
            toVent.y = 0f;
            if (toVent.sqrMagnitude > 0.36f && Vector3.Angle(fwd, toVent) > facingAngle) continue;

            float d = toVent.sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = v;
            }
        }
        return best;
    }

    // ---------------- Entering ----------------

    IEnumerator EnterRoutine(VentHideSpot spot)
    {
        BeginTakeover(spot);
        State = VentState.Entering;
        IsInVent = true;
        hud.SetPrompt(VentHUD.PromptMode.None);

        VillainAI villain = VillainAI.Instance;
        if (villain != null) villain.OnPlayerHidInVent(spot.FrontPoint, spot.GrillePoint);

        Vector3 kneel = spot.FrontPoint;
        Vector3 hide = spot.HidePoint;
        Vector3 into = spot.IntoWall;
        float intoYaw = YawOf(into);
        float outYaw = intoYaw + 180f;

        float total = turnStart + turnTime;
        float flapCloseDelay = turnStart + turnTime * 0.3f;

        StartCoroutine(FlapOpenRoutine(spot, flapOpenDelay, flapOpenTime));
        StartCoroutine(FlapCloseRoutine(spot, flapCloseDelay, 0.42f));

        Vector3 startXZ = savedPos;
        float t = 0f;
        while (t < total)
        {
            t += Time.deltaTime;

            Vector3 pos;
            float yaw, eye, pitch, roll = 0f;
            Vector3 camOffset = Vector3.zero;

            if (t < crawlStart)
            {
                // Approach + kneel in front of the grille
                float a = Smooth(Mathf.Clamp01(t / approachTime));
                pos = Vector3.Lerp(startXZ, kneel, a);
                yaw = Mathf.LerpAngle(savedYaw, intoYaw, a);
                eye = Mathf.Lerp(standEye, kneelEyeHeight, a);
                pitch = Mathf.Lerp(savedPitch, 14f, a);

                // slight forward lurch as he drops to his knees
                camOffset.y = -Mathf.Sin(a * Mathf.PI) * 0.03f;
            }
            else if (t < turnStart)
            {
                // Crawl through the hole
                float b = Mathf.Clamp01((t - crawlStart) / crawlTime);
                float move = Smooth(b);
                pos = Vector3.Lerp(kneel, hide, move);
                yaw = intoYaw;
                eye = Mathf.Lerp(kneelEyeHeight, crawlEyeHeight, Smooth(Mathf.Clamp01(b * 2.2f)));
                pitch = Mathf.Lerp(14f, 0f, Smooth(Mathf.Clamp01(b * 1.6f)));

                float weight = Mathf.Sin(b * Mathf.PI);
                swayPhase += Time.deltaTime * 8.5f;
                camOffset.x = Mathf.Sin(swayPhase) * 0.028f * weight;
                camOffset.y = -Mathf.Abs(Mathf.Sin(swayPhase)) * 0.018f * weight;
                roll = Mathf.Sin(swayPhase) * 2.2f * weight;
            }
            else
            {
                // Turn around in the cramped duct so he faces the grille
                float c = Smooth(Mathf.Clamp01((t - turnStart) / turnTime));
                pos = hide;
                yaw = Mathf.LerpAngle(intoYaw, outYaw, c);
                eye = crawlEyeHeight;
                pitch = 0f;
                roll = Mathf.Sin(c * Mathf.PI) * 3f;
            }

            ApplyPose(pos, eye, yaw, pitch, roll, camOffset);
            yield return null;
        }

        ApplyPose(hide, crawlEyeHeight, outYaw, 0f, 0f, Vector3.zero);

        hiddenYawOffset = 0f;
        hiddenPitch = 0f;
        State = VentState.Hidden;
        hud.SetHidden(true);
        hud.SetPrompt(VentHUD.PromptMode.Leave);
        cooldown = 0.4f;
    }

    // ---------------- Hidden ----------------

    void UpdateHidden(bool interact)
    {
        if (currentVent == null) { ForceRelease(); return; }

        float dt = Time.deltaTime;

        // Rest while hiding: stamina refills
        if (fpc.Stamina != null) fpc.Stamina.UpdateMovement(false, false, dt);

        // Limited look: peer out through the grille bars
        if (inputs != null && inputs.look.sqrMagnitude > 0.0001f)
        {
            float mult = fpc.UsesDirectLookDelta ? 1f : dt;
            float s = fpc.RotationSpeed * mult * fpc.LookSensitivity;
            hiddenPitch = Mathf.Clamp(hiddenPitch + inputs.look.y * s, pitchRange.x, pitchRange.y);
            hiddenYawOffset = Mathf.Clamp(hiddenYawOffset + inputs.look.x * s, -maxYaw, maxYaw);
        }

        // Cramped, tense breathing
        float breath = Mathf.Sin(Time.time * 1.7f);
        Vector3 camOffset = new Vector3(0f, breath * 0.004f, 0f);
        float roll = Mathf.Sin(Time.time * 0.8f) * 0.35f;

        float outYaw = YawOf(currentVent.IntoWall) + 180f;
        ApplyPose(currentVent.HidePoint, crawlEyeHeight, outYaw + hiddenYawOffset, hiddenPitch + breath * 0.12f, roll, camOffset);

        hud.SetPrompt(VentHUD.PromptMode.Leave);
        if (interact && cooldown <= 0f) StartCoroutine(ExitRoutine());
    }

    // ---------------- Exiting ----------------

    IEnumerator ExitRoutine()
    {
        VentHideSpot spot = currentVent;
        State = VentState.Exiting;
        hud.SetPrompt(VentHUD.PromptMode.None);
        hud.SetHidden(false);

        Vector3 hide = spot.HidePoint;
        Vector3 kneel = spot.FrontPoint;
        Vector3 into = spot.IntoWall;
        Vector3 outDir = -into;
        Vector3 standPoint = kneel + outDir * 0.45f;
        float outYaw = YawOf(into) + 180f;
        float startYawNow = transform.eulerAngles.y;
        float startPitchNow = hiddenPitch;

        const float openTime = 0.45f;
        const float crawlBegin = 0.35f;
        const float crawlLen = 1.05f;
        const float standLen = 0.55f;
        float total = crawlBegin + crawlLen + standLen;

        StartCoroutine(FlapOpenRoutine(spot, 0f, openTime));
        StartCoroutine(FlapCloseRoutine(spot, crawlBegin + crawlLen * 0.85f, 0.42f));

        bool released = false;
        float t = 0f;
        while (t < total)
        {
            t += Time.deltaTime;

            Vector3 pos;
            float yaw, eye, pitch, roll = 0f;
            Vector3 camOffset = Vector3.zero;

            if (t < crawlBegin)
            {
                // Shove the grille open: brief forward lean
                float a = Smooth(Mathf.Clamp01(t / crawlBegin));
                pos = hide;
                yaw = Mathf.LerpAngle(startYawNow, outYaw, a);
                eye = crawlEyeHeight;
                pitch = Mathf.Lerp(startPitchNow, 4f, a);
                camOffset.z = Mathf.Sin(a * Mathf.PI) * 0.04f;
            }
            else if (t < crawlBegin + crawlLen)
            {
                if (!released)
                {
                    // From here on the villain can see (and catch) the player again
                    released = true;
                    IsInVent = false;
                    VillainAI villain = VillainAI.Instance;
                    if (villain != null) villain.OnPlayerLeftVent();
                }

                float b = Mathf.Clamp01((t - crawlBegin) / crawlLen);
                pos = Vector3.Lerp(hide, kneel, Smooth(b));
                yaw = outYaw;
                eye = Mathf.Lerp(crawlEyeHeight, kneelEyeHeight, Smooth(Mathf.Clamp01((b - 0.45f) / 0.55f)));
                pitch = Mathf.Lerp(4f, 8f, b);

                float weight = Mathf.Sin(b * Mathf.PI);
                swayPhase += Time.deltaTime * 8.5f;
                camOffset.x = Mathf.Sin(swayPhase) * 0.028f * weight;
                camOffset.y = -Mathf.Abs(Mathf.Sin(swayPhase)) * 0.018f * weight;
                roll = Mathf.Sin(swayPhase) * 2.2f * weight;
            }
            else
            {
                // Stand back up in the corridor
                float c = Smooth(Mathf.Clamp01((t - crawlBegin - crawlLen) / standLen));
                pos = Vector3.Lerp(kneel, standPoint, c);
                yaw = outYaw;
                eye = Mathf.Lerp(kneelEyeHeight, standEye, c);
                pitch = Mathf.Lerp(8f, 0f, c);
            }

            ApplyPose(pos, eye, yaw, pitch, roll, camOffset);
            yield return null;
        }

        if (!released)
        {
            IsInVent = false;
            VillainAI v = VillainAI.Instance;
            if (v != null) v.OnPlayerLeftVent();
        }

        EndTakeover(standPoint, outYaw);
        spot.SetFlapAngle(0f);
        cooldown = reentryCooldown;
    }

    // ---------------- Grille animation ----------------

    IEnumerator FlapOpenRoutine(VentHideSpot spot, float delay, float duration)
    {
        float t = 0f;
        while (t < delay) { t += Time.deltaTime; yield return null; }

        spot.PlayCreak();

        t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            spot.SetFlapAngle(VentHideSpot.FlapOpenAngle * EaseOutBack(p));
            yield return null;
        }
        spot.SetFlapAngle(VentHideSpot.FlapOpenAngle);
    }

    IEnumerator FlapCloseRoutine(VentHideSpot spot, float delay, float duration)
    {
        float t = 0f;
        while (t < delay) { t += Time.deltaTime; yield return null; }

        // gravity pulls it shut: accelerating swing
        t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            spot.SetFlapAngle(VentHideSpot.FlapOpenAngle * (1f - p * p * p));
            yield return null;
        }

        spot.PlayThunk();

        // small damped rebound against the frame
        t = 0f;
        const float reboundTime = 0.32f;
        while (t < reboundTime)
        {
            t += Time.deltaTime;
            float damp = Mathf.Exp(-t * 11f);
            spot.SetFlapAngle(Mathf.Abs(Mathf.Sin(t * 26f)) * 9f * damp);
            yield return null;
        }
        spot.SetFlapAngle(0f);
    }

    // ---------------- Control hand-over ----------------

    void BeginTakeover(VentHideSpot spot)
    {
        currentVent = spot;
        HasControl = true;

        savedPos = transform.position;
        savedYaw = transform.eulerAngles.y;
        savedPitch = fpc.CurrentPitch;
        savedCamLocalPos = camRoot.localPosition;
        floorY = cc != null ? cc.bounds.min.y : transform.position.y;
        standEye = Mathf.Max(0.9f, savedPos.y + savedCamLocalPos.y - floorY);

        if (inputs != null) inputs.jump = false;

        if (motion != null) motion.enabled = false;
        fpc.enabled = false;
        if (cc != null) cc.enabled = false;
    }

    void EndTakeover(Vector3 standPoint, float yaw)
    {
        // Restore the player to a normal standing pose at the corridor side of the vent
        transform.position = new Vector3(standPoint.x, savedPos.y, standPoint.z);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        camRoot.localPosition = savedCamLocalPos;

        fpc.SetPitch(0f);
        fpc.ResetMotionState();

        if (cc != null) cc.enabled = true;
        fpc.enabled = true;
        if (motion != null) motion.enabled = true;

        State = VentState.Idle;
        IsInVent = false;
        HasControl = false;
        currentVent = null;
        hud.SetHidden(false);
        hud.SetPrompt(VentHUD.PromptMode.None);
    }

    /// <summary>Immediately aborts any vent sequence (defeat, scene change) and gives control back.</summary>
    public void ForceRelease()
    {
        if (State == VentState.Idle && !HasControl) return;

        StopAllCoroutines();

        VentHideSpot spot = currentVent;
        Vector3 safe = spot != null ? spot.FrontPoint : savedPos;
        float yaw = spot != null ? YawOf(spot.IntoWall) + 180f : savedYaw;

        VillainAI villain = VillainAI.Instance;
        if (villain != null) villain.OnPlayerLeftVent();

        if (spot != null) spot.SetFlapAngle(0f);

        if (fpc != null && camRoot != null)
        {
            EndTakeover(safe, yaw);
        }
        else
        {
            State = VentState.Idle;
            IsInVent = false;
            HasControl = false;
        }
    }

    // ---------------- Pose helpers ----------------

    void ApplyPose(Vector3 xzPos, float eyeAboveFloor, float yaw, float pitch, float roll, Vector3 camOffset)
    {
        // The capsule is parked so the camera root lands at the requested eye height above the floor
        float rootY = floorY + eyeAboveFloor - savedCamLocalPos.y;
        transform.position = new Vector3(xzPos.x, rootY, xzPos.z);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        camRoot.localPosition = savedCamLocalPos + camOffset;
        camRoot.localRotation = Quaternion.Euler(pitch, 0f, roll);
    }

    static float YawOf(Vector3 dir)
    {
        return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
    }

    static float Smooth(float t)
    {
        return t * t * (3f - 2f * t);
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.15f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
