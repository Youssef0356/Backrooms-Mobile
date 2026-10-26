using System.Collections;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Allows the player to hide inside storage lockers found inside Backrooms rooms.
/// Plays a smooth first-person sequence: approach -> locker door swings open ->
/// step inside -> turn around -> locker door shuts.
/// While hidden, the player can peek through the door louvers while the monster searches.
/// </summary>
[DisallowMultipleComponent]
public class PlayerLockerController : MonoBehaviour
{
    public static PlayerLockerController Instance { get; private set; }

    /// <summary>True from the moment the player commits to entering until they step back out.</summary>
    public static bool IsInLocker { get; private set; }

    /// <summary>True during any scripted locker sequence or while hidden.</summary>
    public static bool HasControl { get; private set; }

    public enum LockerState { Idle, Entering, Hidden, Exiting }

    [Header("Interaction")]
    [Tooltip("How far in front of the locker door the prompt appears (metres).")]
    public float interactRange = 2.4f;
    [Tooltip("Maximum angle between view direction and locker for prompt to show.")]
    [Range(20f, 120f)] public float facingAngle = 75f;
    [Tooltip("The villain must be at least this far away or he would see you step in.")]
    public float minVillainDistance = 2.5f;
    [Tooltip("Cooldown after leaving before entering another locker.")]
    public float reentryCooldown = 1.0f;

    [Header("Look While Hidden")]
    public float maxYaw = 65f;
    public Vector2 pitchRange = new Vector2(-20f, 25f);

    public LockerState State { get; private set; } = LockerState.Idle;

    private FirstPersonController fpc;
    private CharacterController cc;
    private StarterAssetsInputs inputs;
    private FirstPersonMotion motion;
    private Transform camRoot;

    private LockerHideSpot currentLocker;
    private VentHUD hud;

    private Vector3 savedPos;
    private float savedYaw;
    private float savedPitch;
    private Vector3 savedCamLocalPos;
    private float floorY;
    private float standEye;

    private float hiddenYawOffset;
    private float hiddenPitch;
    private float cooldown;
    private bool tapped;

    private void Awake()
    {
        Instance = this;
        IsInLocker = false;
        HasControl = false;

        fpc = GetComponent<FirstPersonController>();
        cc = GetComponent<CharacterController>();
        inputs = GetComponent<StarterAssetsInputs>();
        motion = GetComponent<FirstPersonMotion>();
        camRoot = fpc != null && fpc.CinemachineCameraTarget != null ? fpc.CinemachineCameraTarget.transform : null;
    }

    private void OnEnable()
    {
        VentHUD.OnActionPressed += HandleTap;
        GameOverController.OnPlayerDefeated += HandleDefeated;
        VictoryController.OnPlayerEscaped += HandleDefeated;
    }

    private void OnDisable()
    {
        VentHUD.OnActionPressed -= HandleTap;
        GameOverController.OnPlayerDefeated -= HandleDefeated;
        VictoryController.OnPlayerEscaped -= HandleDefeated;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        IsInLocker = false;
        HasControl = false;
    }

    private void HandleTap()
    {
        if (Time.timeScale > 0f) tapped = true;
    }

    private void HandleDefeated()
    {
        if (State != LockerState.Idle) ForceRelease();
    }

    private void Update()
    {
        if (fpc == null || camRoot == null) return;
        if (hud == null) hud = VentHUD.GetOrCreate();

        // If player is already inside a vent, vent controller has priority
        if (PlayerVentController.IsInVent) return;

        bool interact = ConsumeInteract();

        if (GameOverController.IsGameOver || VictoryController.IsGameWon)
        {
            if (State == LockerState.Hidden) hud.SetPrompt(VentHUD.PromptMode.None);
            return;
        }

        if (PauseMenuController.IsPaused) return;

        if (cooldown > 0f) cooldown -= Time.deltaTime;

        switch (State)
        {
            case LockerState.Idle:
                UpdateIdlePrompt(interact);
                break;

            case LockerState.Hidden:
                UpdateHidden(interact);
                break;
        }
    }

    private bool ConsumeInteract()
    {
        bool pressed = tapped;
        tapped = false;

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame) pressed = true;

        Gamepad gp = Gamepad.current;
        if (gp != null && gp.buttonWest.wasPressedThisFrame) pressed = true;

        return pressed;
    }

    private void UpdateIdlePrompt(bool interact)
    {
        if (cooldown > 0f) return;

        LockerHideSpot spot = FindUsableLocker();
        if (spot == null) return;

        VillainAI villain = VillainAI.Instance;
        bool threatened = villain != null && villain.isActiveAndEnabled &&
            (villain.CurrentState == VillainAI.State.Chase || villain.CurrentState == VillainAI.State.Investigate);

        if (threatened && villain != null)
        {
            float villainDist = Vector3.Distance(villain.transform.position, transform.position);
            if (villainDist < minVillainDistance)
            {
                hud.SetPrompt(VentHUD.PromptMode.TooClose);
                return;
            }
        }

        hud.SetPrompt(VentHUD.PromptMode.EnterLocker);
        if (interact)
        {
            StartCoroutine(EnterRoutine(spot));
        }
    }

    public LockerHideSpot FindUsableLocker()
    {
        if (fpc != null && !fpc.Grounded) return null;

        LockerHideSpot best = null;
        float bestDist = float.MaxValue;
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        fwd.Normalize();

        for (int i = 0; i < LockerHideSpot.All.Count; i++)
        {
            LockerHideSpot locker = LockerHideSpot.All[i];
            if (locker == null) continue;

            Vector3 local = locker.transform.InverseTransformPoint(transform.position);
            // Must be in front of the locker door, within interact range
            if (local.z < 0.2f || local.z > interactRange) continue;
            if (Mathf.Abs(local.x) > 1.2f) continue;

            float groundY = cc != null ? cc.bounds.min.y : transform.position.y;
            if (Mathf.Abs(groundY - locker.transform.position.y) > 0.5f) continue;

            Vector3 toDoor = locker.DoorPoint - transform.position;
            toDoor.y = 0f;
            if (toDoor.sqrMagnitude > 0.25f && Vector3.Angle(fwd, toDoor) > facingAngle) continue;

            float d = toDoor.sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = locker;
            }
        }

        return best;
    }

    // =========================================================================
    // Cinematic Enter Sequence
    // =========================================================================

    private IEnumerator EnterRoutine(LockerHideSpot spot)
    {
        BeginTakeover(spot);
        State = LockerState.Entering;
        IsInLocker = true;
        hud.SetPrompt(VentHUD.PromptMode.None);

        VillainAI villain = VillainAI.Instance;
        if (villain != null) villain.OnPlayerHidInVent(spot.FrontPoint, spot.DoorPoint);

        Vector3 front = spot.FrontPoint;
        Vector3 inside = spot.HidePoint;
        Vector3 faceIn = -spot.ForwardDir;
        Vector3 faceOut = spot.ForwardDir;

        float inYaw = YawOf(faceIn);
        float outYaw = YawOf(faceOut);

        const float approachTime = 0.45f;
        const float doorOpenTime = 0.40f;
        const float stepInsideTime = 0.65f;
        const float turnAroundTime = 0.55f;
        const float doorCloseTime = 0.45f;

        // 1. Approach locker & swing locker door open
        spot.PlayCreak();
        float t = 0f;
        while (t < doorOpenTime)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / doorOpenTime);
            spot.SetDoorAngle(LockerHideSpot.DoorOpenAngle * EaseOutCubic(p));
            Vector3 pos = Vector3.Lerp(savedPos, front, Mathf.Clamp01(t / approachTime));
            float yaw = Mathf.LerpAngle(savedYaw, inYaw, Mathf.Clamp01(t / approachTime));
            ApplyPose(pos, standEye, yaw, 0f, 0f, Vector3.zero);
            yield return null;
        }

        spot.SetDoorAngle(LockerHideSpot.DoorOpenAngle);

        // 2. Step forward inside locker cavity
        t = 0f;
        while (t < stepInsideTime)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / stepInsideTime);
            float move = Smooth(p);
            Vector3 pos = Vector3.Lerp(front, inside, move);

            // Subtle step head bob
            float bob = Mathf.Sin(p * Mathf.PI * 2f) * 0.02f;
            ApplyPose(pos, standEye + bob, inYaw, 0f, 0f, Vector3.zero);
            yield return null;
        }

        // 3. Turn around 180 degrees to face out through louvers
        t = 0f;
        while (t < turnAroundTime)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / turnAroundTime);
            float yaw = Mathf.LerpAngle(inYaw, outYaw, Smooth(p));
            ApplyPose(inside, standEye, yaw, 0f, 0f, Vector3.zero);
            yield return null;
        }

        // 4. Locker door shuts
        t = 0f;
        while (t < doorCloseTime)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / doorCloseTime);
            spot.SetDoorAngle(LockerHideSpot.DoorOpenAngle * (1f - p * p));
            ApplyPose(inside, standEye, outYaw, 0f, 0f, Vector3.zero);
            yield return null;
        }

        spot.SetDoorAngle(0f);
        spot.PlayThunk();

        hiddenYawOffset = 0f;
        hiddenPitch = 0f;
        State = LockerState.Hidden;
        hud.SetHidden(true);
        hud.SetPrompt(VentHUD.PromptMode.LeaveLocker);
        cooldown = 0.5f;
    }

    // =========================================================================
    // Hidden Update
    // =========================================================================

    private void UpdateHidden(bool interact)
    {
        if (currentLocker == null) { ForceRelease(); return; }

        float dt = Time.deltaTime;

        // Stamina refills while hidden
        if (fpc != null && fpc.Stamina != null)
        {
            fpc.Stamina.UpdateMovement(false, false, dt);
        }

        // Look around through the louvers
        if (inputs != null && inputs.look.sqrMagnitude > 0.0001f)
        {
            float mult = fpc.UsesDirectLookDelta ? 1f : dt;
            float s = fpc.RotationSpeed * mult * fpc.LookSensitivity;
            hiddenPitch = Mathf.Clamp(hiddenPitch + inputs.look.y * s, pitchRange.x, pitchRange.y);
            hiddenYawOffset = Mathf.Clamp(hiddenYawOffset + inputs.look.x * s, -maxYaw, maxYaw);
        }

        // Tense, subtle breathing motion
        float breath = Mathf.Sin(Time.time * 1.8f);
        Vector3 camOffset = new Vector3(0f, breath * 0.005f, 0f);
        float roll = Mathf.Sin(Time.time * 0.9f) * 0.35f;

        float outYaw = YawOf(currentLocker.ForwardDir);
        ApplyPose(currentLocker.HidePoint, standEye, outYaw + hiddenYawOffset, hiddenPitch + breath * 0.15f, roll, camOffset);

        hud.SetPrompt(VentHUD.PromptMode.LeaveLocker);
        if (interact && cooldown <= 0f)
        {
            StartCoroutine(ExitRoutine());
        }
    }

    // =========================================================================
    // Cinematic Exit Sequence
    // =========================================================================

    private IEnumerator ExitRoutine()
    {
        LockerHideSpot spot = currentLocker;
        State = LockerState.Exiting;
        hud.SetPrompt(VentHUD.PromptMode.None);
        hud.SetHidden(false);

        Vector3 inside = spot.HidePoint;
        Vector3 front = spot.FrontPoint;
        float outYaw = YawOf(spot.ForwardDir);

        const float openTime = 0.35f;
        const float stepOutTime = 0.65f;
        const float closeTime = 0.40f;

        // 1. Push locker door open
        spot.PlayCreak();
        float t = 0f;
        while (t < openTime)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / openTime);
            spot.SetDoorAngle(LockerHideSpot.DoorOpenAngle * EaseOutCubic(p));
            ApplyPose(inside, standEye, outYaw, 0f, 0f, Vector3.zero);
            yield return null;
        }

        spot.SetDoorAngle(LockerHideSpot.DoorOpenAngle);

        // Player is now exposed to monster
        IsInLocker = false;
        VillainAI villain = VillainAI.Instance;
        if (villain != null) villain.OnPlayerLeftVent();

        // 2. Step forward out into the room
        t = 0f;
        while (t < stepOutTime)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / stepOutTime);
            Vector3 pos = Vector3.Lerp(inside, front, Smooth(p));
            float bob = Mathf.Sin(p * Mathf.PI * 2f) * 0.02f;
            ApplyPose(pos, standEye + bob, outYaw, 0f, 0f, Vector3.zero);
            yield return null;
        }

        // 3. Locker door swings shut behind player
        t = 0f;
        while (t < closeTime)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / closeTime);
            spot.SetDoorAngle(LockerHideSpot.DoorOpenAngle * (1f - p * p));
            yield return null;
        }

        spot.SetDoorAngle(0f);
        spot.PlayThunk();

        EndTakeover(front, outYaw);
        cooldown = reentryCooldown;
    }

    private void BeginTakeover(LockerHideSpot spot)
    {
        currentLocker = spot;
        HasControl = true;

        savedPos = transform.position;
        savedYaw = transform.eulerAngles.y;
        savedPitch = fpc.CurrentPitch;
        savedCamLocalPos = camRoot.localPosition;
        floorY = cc != null ? cc.bounds.min.y : transform.position.y;
        standEye = Mathf.Max(1.5f, savedPos.y + savedCamLocalPos.y - floorY);

        if (inputs != null) inputs.jump = false;
        if (motion != null) motion.enabled = false;
        fpc.enabled = false;
        if (cc != null) cc.enabled = false;
    }

    private void EndTakeover(Vector3 standPoint, float yaw)
    {
        transform.position = new Vector3(standPoint.x, savedPos.y, standPoint.z);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        camRoot.localPosition = savedCamLocalPos;

        fpc.SetPitch(0f);
        fpc.ResetMotionState();

        if (cc != null) cc.enabled = true;
        fpc.enabled = true;
        if (motion != null) motion.enabled = true;

        State = LockerState.Idle;
        IsInLocker = false;
        HasControl = false;
        currentLocker = null;
        hud.SetHidden(false);
        hud.SetPrompt(VentHUD.PromptMode.None);
    }

    public void ForceRelease()
    {
        if (State == LockerState.Idle && !HasControl) return;

        StopAllCoroutines();
        LockerHideSpot spot = currentLocker;
        Vector3 safe = spot != null ? spot.FrontPoint : savedPos;
        float yaw = spot != null ? YawOf(spot.ForwardDir) : savedYaw;

        VillainAI villain = VillainAI.Instance;
        if (villain != null) villain.OnPlayerLeftVent();

        if (spot != null) spot.SetDoorAngle(0f);

        if (fpc != null && camRoot != null)
        {
            EndTakeover(safe, yaw);
        }
        else
        {
            State = LockerState.Idle;
            IsInLocker = false;
            HasControl = false;
        }
    }

    private void ApplyPose(Vector3 xzPos, float eyeAboveFloor, float yaw, float pitch, float roll, Vector3 camOffset)
    {
        float rootY = floorY + eyeAboveFloor - savedCamLocalPos.y;
        transform.position = new Vector3(xzPos.x, rootY, xzPos.z);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        camRoot.localPosition = savedCamLocalPos + camOffset;
        camRoot.localRotation = Quaternion.Euler(pitch, 0f, roll);
    }

    private static float YawOf(Vector3 dir)
    {
        return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
    }

    private static float Smooth(float t)
    {
        return t * t * (3f - 2f * t);
    }

    private static float EaseOutCubic(float t)
    {
        float f = t - 1f;
        return f * f * f + 1f;
    }
}
