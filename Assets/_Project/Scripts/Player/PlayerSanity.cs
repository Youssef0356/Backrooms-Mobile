using System;
using UnityEngine;
using StarterAssets;

/// <summary>
/// Manages player mental sanity and hallucination levels.
/// As the player spends prolonged time navigating the endless liminal corridors,
/// sanity slowly degrades. Darkness and being chased by the entity accelerate decay.
/// Resting inside vents slowly calms the player, while drinking Almond Water
/// significantly restores sanity and relieves psychological horror.
/// </summary>
[DisallowMultipleComponent]
public class PlayerSanity : MonoBehaviour
{
    public static PlayerSanity Instance { get; private set; }

    /// <summary>Fired when current sanity or max sanity changes (current, max).</summary>
    public static event Action<float, float> OnSanityChanged;

    /// <summary>Fired when sanity is restored, passing the restored amount.</summary>
    public static event Action<float> OnSanityRestored;

    [Header("Sanity Capacity")]
    [SerializeField] private float maxSanity = 100f;
    [SerializeField] private float startingSanity = 100f;

    [Header("Decay Rates (points per minute)")]
    [Tooltip("Passive sanity drain per minute of surviving in the Backrooms.")]
    [SerializeField] private float passiveDrainPerMinute = 2.4f;

    [Tooltip("Additional drain per minute when flashlight is off in pitch darkness.")]
    [SerializeField] private float darknessDrainPerMinute = 2.0f;

    [Header("Intense Fear Decay (points per second)")]
    [Tooltip("Sanity drained per second while actively pursued by the creature.")]
    [SerializeField] private float chaseDrainPerSecond = 1.4f;

    [Tooltip("Sanity drained per second while the creature is searching nearby.")]
    [SerializeField] private float searchDrainPerSecond = 0.5f;

    [Header("Recovery Rates")]
    [Tooltip("Sanity points restored per minute while safely tucked inside a vent.")]
    [SerializeField] private float ventRecoveryPerMinute = 4.0f;

    [Tooltip("Sanity points restored upon drinking an Almond Water bottle.")]
    [SerializeField] private float almondWaterRestoreAmount = 45f;

    [Tooltip("Stamina points restored upon drinking an Almond Water bottle.")]
    [SerializeField] private float almondWaterStaminaBonus = 40f;

    private float _currentSanity;
    private Flashlight _flashlight;

    public float CurrentSanity => _currentSanity;
    public float MaxSanity => maxSanity;

    /// <summary>
    /// Normalized sanity from 1.0 (fully lucid and calm) down to 0.0 (catatonic terror).
    /// </summary>
    public float NormalizedSanity => maxSanity > 0f ? Mathf.Clamp01(_currentSanity / maxSanity) : 1f;

    /// <summary>
    /// Hallucination intensity from 0.0 (no hallucinations) up to 1.0 (extreme hallucinations).
    /// Inverted representation of sanity for convenience.
    /// </summary>
    public float HallucinationLevel => 1f - NormalizedSanity;

    public float AlmondWaterRestoreAmount => almondWaterRestoreAmount;
    public float AlmondWaterStaminaBonus => almondWaterStaminaBonus;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        _currentSanity = Mathf.Clamp(startingSanity, 0f, maxSanity);
    }

    private void Start()
    {
        _flashlight = FindAnyObjectByType<Flashlight>();
        OnSanityChanged?.Invoke(_currentSanity, maxSanity);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (PauseMenuController.IsPaused || GameOverController.IsGameOver || VictoryController.IsGameWon)
            return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // 1. Check if safe inside a vent
        if (PlayerVentController.IsInVent)
        {
            // Recover sanity slowly while safe in the duct
            float recovery = (ventRecoveryPerMinute / 60f) * dt;
            ApplySanityDelta(recovery);
            return;
        }

        // 2. Active creature terror
        VillainAI villain = VillainAI.Instance;
        float fearDrain = 0f;
        if (villain != null && villain.isActiveAndEnabled)
        {
            if (villain.CurrentState == VillainAI.State.Chase)
            {
                fearDrain += chaseDrainPerSecond * dt;
            }
            else if (villain.CurrentState == VillainAI.State.Investigate)
            {
                fearDrain += searchDrainPerSecond * dt;
            }
        }

        // 3. Environmental liminal decay
        float envDrain = (passiveDrainPerMinute / 60f) * dt;

        // Darkness penalty if flashlight is switched off
        if (_flashlight != null && !_flashlight.IsOn)
        {
            envDrain += (darknessDrainPerMinute / 60f) * dt;
        }

        float totalDrain = fearDrain + envDrain;
        if (totalDrain > 0f)
        {
            ApplySanityDelta(-totalDrain);
        }
    }

    /// <summary>
    /// Restores sanity by a specified amount (e.g. from Almond Water).
    /// </summary>
    public void RestoreSanity(float amount)
    {
        if (amount <= 0f) return;
        ApplySanityDelta(amount);
        OnSanityRestored?.Invoke(amount);
    }

    /// <summary>
    /// Drains sanity by a specified amount (e.g. jumpscare or supernatural encounter).
    /// </summary>
    public void DrainSanity(float amount)
    {
        if (amount <= 0f) return;
        ApplySanityDelta(-amount);
    }

    /// <summary>
    /// Resets sanity back to full 100%.
    /// </summary>
    public void ResetSanity()
    {
        _currentSanity = maxSanity;
        OnSanityChanged?.Invoke(_currentSanity, maxSanity);
    }

    private void ApplySanityDelta(float delta)
    {
        float prev = _currentSanity;
        _currentSanity = Mathf.Clamp(_currentSanity + delta, 0f, maxSanity);

        if (!Mathf.Approximately(prev, _currentSanity))
        {
            OnSanityChanged?.Invoke(_currentSanity, maxSanity);
        }
    }
}
