using System;
using UnityEngine;

namespace StarterAssets
{
    /// <summary>
    /// Manages player stamina consumption and regeneration.
    /// Sprinting drains stamina; walking or standing idle regenerates it.
    /// When depleted, the player is exhausted and forced to walk until stamina recovers.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerStamina : MonoBehaviour
    {
        public static PlayerStamina Instance { get; private set; }

        public static event Action<float, float> OnStaminaChanged; // current, max
        public static event Action<bool> OnExhaustionChanged;       // isExhausted

        [Header("Stamina Settings")]
        [Tooltip("Maximum stamina capacity.")]
        [SerializeField] private float maxStamina = 100f;

        [Tooltip("Stamina drained per second while sprinting.")]
        [SerializeField] private float sprintDrainRate = 18f;

        [Tooltip("Stamina recovered per second while walking.")]
        [SerializeField] private float walkRegenRate = 16f;

        [Tooltip("Stamina recovered per second while standing still.")]
        [SerializeField] private float idleRegenRate = 24f;

        [Tooltip("Delay in seconds after stopping sprint before regeneration begins.")]
        [SerializeField] private float regenDelay = 0.55f;

        [Tooltip("Minimum stamina percentage (0..1) required to recover from exhaustion before sprinting again.")]
        [Range(0.05f, 0.5f)]
        [SerializeField] private float exhaustionRecoveryFraction = 0.20f;

        private float _currentStamina;
        private bool _isExhausted;
        private bool _isSprinting;
        private float _regenTimer;

        public float CurrentStamina => _currentStamina;
        public float MaxStamina => maxStamina;
        public float NormalizedStamina => maxStamina > 0f ? Mathf.Clamp01(_currentStamina / maxStamina) : 0f;
        public bool IsExhausted => _isExhausted;
        public bool IsSprinting => _isSprinting;

        /// <summary>
        /// True if the player has enough stamina and is not currently exhausted.
        /// </summary>
        public bool CanSprint => _currentStamina > 1f && !_isExhausted;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            _currentStamina = maxStamina;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Updates stamina state driven by the player controller's actual movement.
        /// </summary>
        /// <param name="sprinting">Whether the player is actively sprinting.</param>
        /// <param name="moving">Whether the player has directional movement input.</param>
        /// <param name="deltaTime">Delta time step.</param>
        public void UpdateMovement(bool sprinting, bool moving, float deltaTime)
        {
            if (deltaTime <= 0f) return;

            _isSprinting = sprinting;

            if (sprinting)
            {
                // Draining stamina while sprinting
                _regenTimer = regenDelay;
                _currentStamina = Mathf.Max(0f, _currentStamina - sprintDrainRate * deltaTime);

                if (_currentStamina <= 0.01f && !_isExhausted)
                {
                    _isExhausted = true;
                    OnExhaustionChanged?.Invoke(true);
                }

                OnStaminaChanged?.Invoke(_currentStamina, maxStamina);
            }
            else
            {
                // Delay before recovery
                if (_regenTimer > 0f)
                {
                    _regenTimer -= deltaTime;
                }
                else if (_currentStamina < maxStamina)
                {
                    // Regenerating stamina (walking vs idle)
                    float rate = moving ? walkRegenRate : idleRegenRate;
                    _currentStamina = Mathf.Min(maxStamina, _currentStamina + rate * deltaTime);

                    // Check if recovered from exhaustion
                    float recoveryThreshold = maxStamina * exhaustionRecoveryFraction;
                    if (_isExhausted && _currentStamina >= recoveryThreshold)
                    {
                        _isExhausted = false;
                        OnExhaustionChanged?.Invoke(false);
                    }

                    OnStaminaChanged?.Invoke(_currentStamina, maxStamina);
                }
            }
        }

        /// <summary>
        /// Restores stamina by the specified amount (e.g. drinking Almond Water).
        /// </summary>
        public void RestoreStamina(float amount)
        {
            if (amount <= 0f) return;
            _currentStamina = Mathf.Min(maxStamina, _currentStamina + amount);
            float recoveryThreshold = maxStamina * exhaustionRecoveryFraction;
            if (_isExhausted && _currentStamina >= recoveryThreshold)
            {
                _isExhausted = false;
                OnExhaustionChanged?.Invoke(false);
            }
            OnStaminaChanged?.Invoke(_currentStamina, maxStamina);
        }

        /// <summary>
        /// Instantly restores stamina to full (e.g., after player revive).
        /// </summary>
        public void ResetStamina()
        {
            _currentStamina = maxStamina;
            _isExhausted = false;
            _isSprinting = false;
            _regenTimer = 0f;
            OnExhaustionChanged?.Invoke(false);
            OnStaminaChanged?.Invoke(_currentStamina, maxStamina);
        }
    }
}
