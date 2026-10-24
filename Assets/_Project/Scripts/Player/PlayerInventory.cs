using System;
using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;

/// <summary>
/// Manages the player's inventory (specifically Almond Water rations),
/// handles quick-drinking input ([Q] / Gamepad / Mobile button), and detects
/// nearby collectible bottles for pickup ([E] / Mobile tap).
/// </summary>
[DisallowMultipleComponent]
public class PlayerInventory : MonoBehaviour
{
    public static PlayerInventory Instance { get; private set; }

    /// <summary>Fired when Almond Water count changes (newCount).</summary>
    public static event Action<int> OnInventoryChanged;

    /// <summary>Fired when an Almond Water bottle is collected.</summary>
    public static event Action OnItemPickedUp;

    [Header("Inventory Capacity")]
    [SerializeField] private int maxCapacity = 4;
    [SerializeField] private int startingBottles = 1;

    [Header("Interaction Raycasting")]
    [Tooltip("Maximum distance to detect pickups.")]
    [SerializeField] private float lookRange = 2.5f;

    private int _almondWaterCount;
    private Camera _playerCamera;
    private AlmondWaterPickup _currentTargetPickup;
    private bool _mobilePickupTapped;

    public int AlmondWaterCount => _almondWaterCount;
    public int MaxCapacity => maxCapacity;
    public bool HasAlmondWater => _almondWaterCount > 0;
    public AlmondWaterPickup CurrentTargetPickup => _currentTargetPickup;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        _almondWaterCount = Mathf.Clamp(startingBottles, 0, maxCapacity);
    }

    private void Start()
    {
        _playerCamera = GetComponentInChildren<Camera>();
        if (_playerCamera == null) _playerCamera = Camera.main;

        OnInventoryChanged?.Invoke(_almondWaterCount);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (PauseMenuController.IsPaused || GameOverController.IsGameOver || VictoryController.IsGameWon)
        {
            _currentTargetPickup = null;
            return;
        }

        UpdateLookedAtPickup();
        CheckInput();
    }

    private void UpdateLookedAtPickup()
    {
        if (_playerCamera == null)
        {
            _playerCamera = Camera.main;
            if (_playerCamera == null) return;
        }

        Vector3 eyePos = _playerCamera.transform.position;
        Vector3 lookDir = _playerCamera.transform.forward;

        AlmondWaterPickup bestPickup = null;
        float bestDist = lookRange;

        var activePickups = AlmondWaterPickup.ActivePickups;
        for (int i = 0; i < activePickups.Count; i++)
        {
            AlmondWaterPickup p = activePickups[i];
            if (p == null) continue;

            if (p.CanPlayerInteract(eyePos, lookDir, out float dist))
            {
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPickup = p;
                }
            }
        }

        _currentTargetPickup = bestPickup;
    }

    private void CheckInput()
    {
        Keyboard kb = Keyboard.current;
        Gamepad gp = Gamepad.current;

        // 1. Quick Drink: [Q] or Gamepad D-pad Up / Y button
        bool drinkPressed = false;
        if (kb != null && kb.qKey.wasPressedThisFrame) drinkPressed = true;
        if (gp != null && gp.dpad.up.wasPressedThisFrame) drinkPressed = true;

        if (drinkPressed)
        {
            UseAlmondWater();
        }

        // 2. Pick up item: [E] or Gamepad West (square / X) or Mobile Tap
        bool pickupPressed = _mobilePickupTapped;
        _mobilePickupTapped = false;

        if (kb != null && kb.eKey.wasPressedThisFrame) pickupPressed = true;
        if (gp != null && gp.buttonWest.wasPressedThisFrame) pickupPressed = true;

        if (pickupPressed && _currentTargetPickup != null)
        {
            // Pick up the bottle if inventory not full
            if (_almondWaterCount < maxCapacity)
            {
                _currentTargetPickup.Collect();
                _currentTargetPickup = null;
            }
        }
    }

    /// <summary>
    /// Adds Almond Water to the inventory.
    /// </summary>
    public bool AddAlmondWater(int amount = 1)
    {
        if (_almondWaterCount >= maxCapacity) return false;

        _almondWaterCount = Mathf.Min(maxCapacity, _almondWaterCount + amount);
        OnInventoryChanged?.Invoke(_almondWaterCount);
        OnItemPickedUp?.Invoke();
        return true;
    }

    /// <summary>
    /// Drinks one bottle of Almond Water to restore sanity and stamina.
    /// </summary>
    public bool UseAlmondWater()
    {
        if (_almondWaterCount <= 0) return false;
        if (PlayerVentController.IsInVent) return false;

        PlayerDrinkController drinkCtrl = PlayerDrinkController.Instance;
        if (drinkCtrl != null && drinkCtrl.IsDrinking) return false;

        bool started = false;
        if (drinkCtrl != null)
        {
            started = drinkCtrl.StartDrinking();
        }
        else
        {
            // Fallback if controller not initialized: apply instantly
            if (PlayerSanity.Instance != null)
                PlayerSanity.Instance.RestoreSanity(PlayerSanity.Instance.AlmondWaterRestoreAmount);
            if (PlayerStamina.Instance != null)
                PlayerStamina.Instance.RestoreStamina(PlayerSanity.Instance != null ? PlayerSanity.Instance.AlmondWaterStaminaBonus : 40f);
            started = true;
        }

        if (started)
        {
            _almondWaterCount--;
            OnInventoryChanged?.Invoke(_almondWaterCount);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Triggered by mobile HUD button tap to pick up looking bottle.
    /// </summary>
    public void NotifyPickupTap()
    {
        _mobilePickupTapped = true;
    }
}
