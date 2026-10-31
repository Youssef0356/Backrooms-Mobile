using System.Collections.Generic;
using UnityEngine;
using StarterAssets;

/// <summary>
/// Adds realistic, organic first-person head bobbing, weighted running motion,
/// athletic shoulder tilt, jump-landing cushion dip, and natural rhythmic footsteps.
/// Calibrated to human biomechanical stride cadence (no vibrating camera jitter or machine-gun footsteps).
/// </summary>
[DisallowMultipleComponent]
public class PlayerHeadBobMotion : MonoBehaviour
{
    [Header("Idle Breathing")]
    [SerializeField] private float idleBobFrequency = 1.0f;
    [SerializeField] private float idleBobHeight = 0.004f;

    [Header("Walking Head Motion")]
    [SerializeField] private float walkBobFrequency = 0.90f;
    [SerializeField] private float walkBobHeight = 0.016f;
    [SerializeField] private float walkBobSideSway = 0.009f;
    [SerializeField] private float walkBobRoll = 0.35f;

    [Header("Running / Sprinting Head Motion (Smooth & Controlled)")]
    [SerializeField] private float runBobFrequency = 1.30f;
    [SerializeField] private float runBobHeight = 0.026f;
    [SerializeField] private float runBobSideSway = 0.015f;
    [SerializeField] private float runBobRoll = 0.65f;

    [Header("Jump & Landing Cushion")]
    [SerializeField] private float landingDipAmount = 0.10f;
    [SerializeField] private float landingSpringSpeed = 8.5f;

    [Header("Footstep Audio")]
    [SerializeField] private bool enableFootsteps = true;
    [Range(0f, 1f)]
    [SerializeField] private float footstepVolume = 0.50f;
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField] private AudioClip landingClip;

    private FirstPersonController fpc;
    private CharacterController controller;
    private AudioSource audioSource;

    private Vector3 defaultLocalPos = new Vector3(0f, 1.375f, 0f);
    private float cycleProgress = 0f;
    private float currentLandDip = 0f;
    private bool wasGrounded = true;

    // Strict timing guard to guarantee realistic footstep cadence (no rapid-fire)
    private float lastStepTime = 0f;
    private int lastStepIndex = -1;

    private void Awake()
    {
        // Attached to PlayerCameraRoot, find player on parent
        fpc = GetComponentInParent<FirstPersonController>();
        controller = GetComponentInParent<CharacterController>();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0f; // 2D player audio
            audioSource.playOnAwake = false;
        }

        defaultLocalPos = transform.localPosition;
        LoadDefaultAudioIfMissing();

        // Prevent duplicate footstep audio: if FirstPersonMotion is present, let it handle footsteps exclusively
        FirstPersonMotion fpm = GetComponentInParent<FirstPersonMotion>() ?? GetComponent<FirstPersonMotion>() ?? FindAnyObjectByType<FirstPersonMotion>();
        if (fpm != null && fpm.enableAudio)
        {
            enableFootsteps = false;
        }
    }

    private void LoadDefaultAudioIfMissing()
    {
        if (footstepClips == null || footstepClips.Length == 0)
        {
            List<AudioClip> steps = new List<AudioClip>();
            for (int i = 1; i <= 10; i++)
            {
                string num = i < 10 ? $"0{i}" : $"{i}";
                AudioClip clip = Resources.Load<AudioClip>($"SFX/Footsteps/Player_Footstep_{num}");
                if (clip != null) steps.Add(clip);
            }
            if (steps.Count > 0) footstepClips = steps.ToArray();
        }

        if (landingClip == null)
        {
            landingClip = Resources.Load<AudioClip>("SFX/Footsteps/Player_Land");
        }
    }

    private void Update()
    {
        if (fpc == null || controller == null) return;

        bool isGrounded = fpc.Grounded;
        float horizontalSpeed = new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude;
        bool isMoving = horizontalSpeed > 0.2f && isGrounded;
        bool isSprinting = fpc.IsSprinting && isMoving;

        // Landing Impact Detection
        if (!wasGrounded && isGrounded)
        {
            currentLandDip = landingDipAmount;
            if (enableFootsteps && landingClip != null && Time.time - lastStepTime > 0.25f)
            {
                audioSource.pitch = Random.Range(0.95f, 1.05f);
                audioSource.PlayOneShot(landingClip, footstepVolume * 1.1f);
                lastStepTime = Time.time;
            }
        }
        wasGrounded = isGrounded;

        // Smoothly spring back from landing dip
        currentLandDip = Mathf.MoveTowards(currentLandDip, 0f, Time.deltaTime * landingSpringSpeed);

        Vector3 targetOffset = Vector3.zero;
        float targetRoll = 0f;

        if (!isGrounded)
        {
            // Airborne: gently settle
            targetOffset = new Vector3(0f, -currentLandDip, 0f);
        }
        else if (isMoving)
        {
            // Human stride pace
            float freq = isSprinting ? runBobFrequency : walkBobFrequency;
            float heightAmp = isSprinting ? runBobHeight : walkBobHeight;
            float swayAmp = isSprinting ? runBobSideSway : walkBobSideSway;
            float rollAmp = isSprinting ? runBobRoll : walkBobRoll;

            // Normalized speed scale
            float baseSpeed = isSprinting ? fpc.SprintSpeed : fpc.MoveSpeed;
            float speedFactor = Mathf.Clamp(horizontalSpeed / Mathf.Max(0.1f, baseSpeed), 0.5f, 1.15f);

            // Stride cycle (0 to 2*PI represents 1 full stride = 2 steps)
            float prevCycle = cycleProgress;
            cycleProgress += Time.deltaTime * freq * speedFactor * Mathf.PI * 2f;
            if (cycleProgress >= Mathf.PI * 2f)
            {
                cycleProgress -= Mathf.PI * 2f;
                prevCycle -= Mathf.PI * 2f;
            }

            // Figure-8 / Organic Gait:
            // Vertical: 2 dips per stride (one per foot)
            float verticalBob = -Mathf.Abs(Mathf.Sin(cycleProgress)) * heightAmp;
            // Horizontal: 1 sway per stride (Left then Right)
            float horizontalSway = Mathf.Cos(cycleProgress) * swayAmp;
            // Roll: Natural head tilt with weight transfer
            targetRoll = Mathf.Sin(cycleProgress) * rollAmp;

            targetOffset = new Vector3(horizontalSway, verticalBob - currentLandDip, 0f);

            // Footstep audio: triggers exactly when each foot plants into the ground
            // Foot 1 plants near PI/2, Foot 2 plants near 3*PI/2
            float minStepInterval = isSprinting ? 0.45f : 0.52f; // Physically realistic time between steps
            bool hitStep1 = prevCycle < Mathf.PI * 0.5f && cycleProgress >= Mathf.PI * 0.5f;
            bool hitStep2 = prevCycle < Mathf.PI * 1.5f && cycleProgress >= Mathf.PI * 1.5f;

            if ((hitStep1 || hitStep2) && Time.time - lastStepTime >= minStepInterval)
            {
                PlayFootstep(isSprinting);
                lastStepTime = Time.time;
            }
        }
        else
        {
            // Idle subtle breathing
            cycleProgress += Time.deltaTime * idleBobFrequency;
            if (cycleProgress >= Mathf.PI * 2f) cycleProgress -= Mathf.PI * 2f;

            float breathY = Mathf.Sin(cycleProgress) * idleBobHeight;
            targetOffset = new Vector3(0f, breathY - currentLandDip, 0f);
            targetRoll = 0f;
        }

        // Smooth positional transition (fluid dampening, zero jitter)
        transform.localPosition = Vector3.Lerp(transform.localPosition, defaultLocalPos + targetOffset, Time.deltaTime * 14f);

        // Apply dynamic head roll while preserving first-person pitch
        Vector3 curEuler = transform.localEulerAngles;
        transform.localRotation = Quaternion.Euler(curEuler.x, 0f, targetRoll);
    }

    private void PlayFootstep(bool sprinting)
    {
        if (!enableFootsteps || footstepClips == null || footstepClips.Length == 0) return;

        // Pick random step without repeating the exact same clip consecutively
        int idx = Random.Range(0, footstepClips.Length);
        if (footstepClips.Length > 1 && idx == lastStepIndex)
        {
            idx = (idx + 1) % footstepClips.Length;
        }
        lastStepIndex = idx;

        AudioClip clip = footstepClips[idx];
        if (clip == null) return;

        float vol = footstepVolume * (sprinting ? 1.15f : 0.85f);
        audioSource.pitch = Random.Range(sprinting ? 1.02f : 0.96f, sprinting ? 1.10f : 1.04f);
        audioSource.PlayOneShot(clip, vol);
    }
}
