using System;
using UnityEngine;
using StarterAssets;

/// <summary>
/// Emits sound / footstep vibrations when the player moves or sprints.
/// Listened to by VillainAI to stalk and hunt the player.
/// </summary>
public class PlayerNoiseEmitter : MonoBehaviour
{
    public static event Action<Vector3, float> OnNoiseEmitted;

    [Header("Noise Radii (in meters)")]
    [Tooltip("Audible radius when walking on carpet.")]
    public float walkNoiseRadius = 3.5f;

    [Tooltip("Audible radius when sprinting on carpet (heavy footsteps).")]
    public float sprintNoiseRadius = 15f;

    [Tooltip("Noise interval in seconds while moving.")]
    public float stepInterval = 0.32f;

    CharacterController controller;
    StarterAssetsInputs inputs;
    float stepTimer;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        inputs = GetComponent<StarterAssetsInputs>();
    }

    void Update()
    {
        if (GameOverController.IsGameOver || PauseMenuController.IsPaused) return;

        bool isMoving = false;
        bool isSprinting = false;

        if (inputs != null)
        {
            isMoving = inputs.move != Vector2.zero;
            isSprinting = inputs.sprint && isMoving;
        }
        else if (controller != null)
        {
            Vector3 horizontalVelocity = new Vector3(controller.velocity.x, 0f, controller.velocity.z);
            isMoving = horizontalVelocity.magnitude > 0.5f;
            isSprinting = horizontalVelocity.magnitude > 4.5f;
        }

        if (!isMoving)
        {
            stepTimer = 0f;
            return;
        }

        stepTimer += Time.deltaTime;
        float currentInterval = isSprinting ? stepInterval * 0.75f : stepInterval;

        if (stepTimer >= currentInterval)
        {
            stepTimer = 0f;
            float radius = isSprinting ? sprintNoiseRadius : walkNoiseRadius;
            EmitNoise(transform.position, radius);
        }
    }

    /// <summary>
    /// Broadcasts a sound event into the level.
    /// Can also be called externally (e.g. for dropped items or doors).
    /// </summary>
    public static void EmitNoise(Vector3 position, float radius)
    {
        OnNoiseEmitted?.Invoke(position, radius);
    }
}
