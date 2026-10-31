using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles player interaction with room doors.
/// When looking at a door within range, displays "[E] OPEN DOOR" / "[E] CLOSE DOOR" (or mobile TAP)
/// and toggles the door on interaction.
/// </summary>
[DisallowMultipleComponent]
public class PlayerDoorInteraction : MonoBehaviour
{
    public static PlayerDoorInteraction Instance { get; private set; }

    [Header("Interaction Settings")]
    [Tooltip("Maximum interaction distance in metres.")]
    public float interactDistance = 3.5f;
    [Tooltip("Maximum angle between view direction and door for prompt to show.")]
    [Range(20f, 120f)] public float facingAngle = 75f;

    private FirstPersonController fpc;
    private Camera playerCam;
    private VentHUD hud;
    private bool tapped;

    private BackroomsDoor currentTargetDoor;

    private void Awake()
    {
        Instance = this;
        fpc = GetComponent<FirstPersonController>();
    }

    private void OnEnable()
    {
        VentHUD.OnActionPressed += HandleTap;
    }

    private void OnDisable()
    {
        VentHUD.OnActionPressed -= HandleTap;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void HandleTap()
    {
        if (Time.timeScale > 0f) tapped = true;
    }

    private void Update()
    {
        if (PlayerVentController.IsInVent || PlayerLockerController.IsInLocker)
        {
            currentTargetDoor = null;
            return;
        }

        if (PauseMenuController.IsPaused || GameOverController.IsGameOver || VictoryController.IsGameWon)
        {
            currentTargetDoor = null;
            return;
        }

        if (playerCam == null)
        {
            playerCam = Camera.main;
            if (playerCam == null) return;
        }

        if (hud == null) hud = VentHUD.GetOrCreate();

        // Check if player is already targeting a vent or locker
        PlayerLockerController lockerCtrl = PlayerLockerController.Instance;
        if (lockerCtrl != null && lockerCtrl.FindUsableLocker() != null)
        {
            currentTargetDoor = null;
            return;
        }

        currentTargetDoor = FindTargetDoor();

        bool interact = ConsumeInteract();

        if (currentTargetDoor != null)
        {
            hud.SetPrompt(currentTargetDoor.IsOpen ? VentHUD.PromptMode.CloseDoor : VentHUD.PromptMode.OpenDoor);

            if (interact)
            {
                currentTargetDoor.Toggle();
            }
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

    private BackroomsDoor FindTargetDoor()
    {
        Vector3 eyePos = playerCam.transform.position;
        Vector3 lookDir = playerCam.transform.forward;

        BackroomsDoor best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < BackroomsDoor.AllDoors.Count; i++)
        {
            BackroomsDoor door = BackroomsDoor.AllDoors[i];
            if (door == null) continue;

            Vector3 doorCenter = door.transform.position + Vector3.up * 1.2f;
            float dist = Vector3.Distance(eyePos, doorCenter);
            if (dist > interactDistance) continue;

            Vector3 toDoor = (doorCenter - eyePos).normalized;
            float angle = Vector3.Angle(lookDir, toDoor);
            if (angle > facingAngle) continue;

            if (dist < bestDist)
            {
                bestDist = dist;
                best = door;
            }
        }

        return best;
    }
}
