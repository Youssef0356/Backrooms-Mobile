using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Controls the player's flashlight with realistic handheld physical motion:
/// 1. Handheld offset (carried in the right hand below camera eye level).
/// 2. Rotational lag / inertia when looking around.
/// 3. Arm sway while moving and breathing.
/// 4. Procedural mechanical switch click audio.
/// 5. Subtle battery flicker.
/// </summary>
[RequireComponent(typeof(Light))]
public class Flashlight : MonoBehaviour
{
    [Header("Light Settings")]
    public bool startOn = true;
    public float intensity = 3.8f;
    public float range = 24.0f;
    public Color lightColor = new Color(1.0f, 0.95f, 0.86f); // warm incandescent flashlight bulb

    [Header("Handheld Physical Inertia")]
    public bool enableHandheldSway = true;
    [Tooltip("Resting local offset representing right hand carry position.")]
    public Vector3 restingHandOffset = new Vector3(0.18f, -0.16f, 0.20f);
    [Tooltip("How fast the flashlight catches up to camera rotation (lower = heavier feel).")]
    public float rotationLagSpeed = 12.0f;
    [Tooltip("Maximum allowed angle difference before snapping.")]
    public float maxLagAngle = 20.0f;
    [Tooltip("Arm sway amplitude while moving.")]
    public float armSwayMultiplier = 0.020f;
    [Tooltip("Arm sway rotation tilt while moving in degrees.")]
    public float armSwayTiltAngle = 1.6f;

    [Header("Flicker (optional)")]
    public bool flicker = true;
    [Range(0f, 1f)] public float flickerAmount = 0.15f;
    public float flickerSpeed = 12f;

    private Light lamp;
    private bool isOn;
    public bool IsOn => isOn;

    private Transform parentCam;
    private Quaternion lastTargetRot;
    private float swayTimer;
    private AudioSource audioSource;
    private AudioClip clickOnClip;
    private AudioClip clickOffClip;

    private void Awake()
    {
        lamp = GetComponent<Light>();
        parentCam = transform.parent;

        if (lamp != null)
        {
            lamp.type = LightType.Spot;
            lamp.color = lightColor;
            lamp.spotAngle = 60f;
            lamp.innerSpotAngle = 26f;
            lamp.range = range;
        }

        isOn = startOn;
        Apply(intensity);
        CreateProceduralClickClips();

        if (parentCam != null)
        {
            lastTargetRot = parentCam.rotation;
            transform.localPosition = restingHandOffset;
        }
    }

    private void CreateProceduralClickClips()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0.0f;
        audioSource.volume = 0.75f;

        // Generate high quality mechanical switch clicks
        clickOnClip = GenerateSwitchClick(true);
        clickOffClip = GenerateSwitchClick(false);
    }

    private AudioClip GenerateSwitchClick(bool isOnSwitch)
    {
        int sampleRate = 44100;
        int sampleCount = (int)(sampleRate * 0.035f); // 35ms snap
        float[] samples = new float[sampleCount];

        float freq1 = isOnSwitch ? 3200f : 2400f;
        float freq2 = isOnSwitch ? 5800f : 4200f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float decay = Mathf.Exp(-t * 220f);
            float noise = (Random.value * 2f - 1f) * 0.35f;
            float tone = Mathf.Sin(2f * Mathf.PI * freq1 * t) * 0.45f + Mathf.Sin(2f * Mathf.PI * freq2 * t) * 0.20f;
            samples[i] = (tone + noise) * decay;
        }

        AudioClip clip = AudioClip.Create(isOnSwitch ? "SwitchOn" : "SwitchOff", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void OnEnable()
    {
        StarterAssets.TouchscreenInput.OnFlashlightToggleRequested += Toggle;
        StarterAssets.TouchscreenInput.SetFlashlightState(isOn);
    }

    private void OnDisable()
    {
        StarterAssets.TouchscreenInput.OnFlashlightToggleRequested -= Toggle;
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) Toggle();
#endif
        if (!isOn) return;

        float level = intensity;
        if (flicker)
        {
            float n = Mathf.PerlinNoise(Time.time * flickerSpeed, 0f);
            level *= 1f - flickerAmount * n;
        }
        lamp.intensity = level;
    }

    private void LateUpdate()
    {
        if (parentCam == null || !enableHandheldSway) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // 1. Organic Handheld Rotational Lag
        Quaternion targetRot = parentCam.rotation;
        float angleDiff = Quaternion.Angle(transform.rotation, targetRot);

        if (angleDiff > maxLagAngle)
        {
            // Clamp lag so flashlight doesn't swing wildly out of screen
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, angleDiff - maxLagAngle);
        }

        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, dt * rotationLagSpeed);

        // 2. Forearm sway while moving
        CharacterController cc = parentCam.GetComponentInParent<CharacterController>();
        float moveSpeed = (cc != null) ? new Vector2(cc.velocity.x, cc.velocity.z).magnitude : 0f;

        if (moveSpeed > 0.15f)
        {
            swayTimer += dt * (moveSpeed * 2.2f);
            float swayX = Mathf.Sin(swayTimer) * armSwayMultiplier;
            float swayY = -Mathf.Abs(Mathf.Cos(swayTimer)) * armSwayMultiplier;
            transform.localPosition = restingHandOffset + new Vector3(swayX, swayY, 0f);
        }
        else
        {
            // Resting gentle breath sway
            float idleBreath = Mathf.Sin(Time.time * 1.5f) * 0.003f;
            transform.localPosition = Vector3.Lerp(transform.localPosition, restingHandOffset + new Vector3(0f, idleBreath, 0f), dt * 4f);
        }
    }

    public void Toggle()
    {
        SetState(!isOn);
    }

    public void SetState(bool state, bool playAudio = true)
    {
        isOn = state;
        Apply(intensity);
        StarterAssets.TouchscreenInput.SetFlashlightState(isOn);

        if (playAudio && audioSource != null)
        {
            AudioClip clip = isOn ? clickOnClip : clickOffClip;
            if (clip != null)
            {
                audioSource.pitch = Random.Range(0.96f, 1.04f);
                audioSource.PlayOneShot(clip, 0.85f);
            }
        }
    }

    private void Apply(float level)
    {
        if (lamp != null)
        {
            lamp.enabled = isOn;
            lamp.intensity = level;
        }
    }
}
