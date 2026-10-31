using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Dedicated Horror Chase Soundtrack Director (Outlast 2 Style).
/// Dynamically monitors active pursuit from monsters (VillainAI, SmilyHoundAI).
/// 1. Plays full 2D non-diegetic heart-pounding chase music that crossfades seamlessly with distance intensity.
/// 2. Shakes the player's camera dynamically as the predator closes in.
/// 3. Accelerates visceral pounding heartbeats that intensify the closer the beast gets.
/// 4. Exposes pursuit telemetry to InventoryHUD and post-processing filters.
/// </summary>
public class ChaseSoundtrackDirector : MonoBehaviour
{
    public static ChaseSoundtrackDirector Instance { get; private set; }

    public static bool IsPursuitActive => Instance != null && Instance.isChasingActive;
    public static bool IsHoundPursuing => Instance != null && Instance.isHoundChasing;
    public static float CurrentClosestDistance => Instance != null ? Instance.lastClosestDist : 999f;
    public static float CurrentHeartbeatPulse { get; private set; }

    private float heartbeatPhase;

    [Header("Soundtrack Settings")]
    [Tooltip("The intense Outlast 2 style chase soundtrack clip.")]
    public AudioClip chaseSoundtrack;

    [Tooltip("The intense chase soundtrack clip played when the Smily Hound / Dog chases the player.")]
    public AudioClip houndChaseSoundtrack;

    [Range(0f, 1f)]
    [Tooltip("Target volume of the soundtrack when in active chase.")]
    public float maxChaseVolume = 0.85f;

    [Tooltip("Speed of fade in when chase begins (seconds).")]
    public float fadeInDuration = 0.45f;

    [Tooltip("Speed of fade out when chase ends or player hides (seconds).")]
    public float fadeOutDuration = 2.0f;

    [Header("Intensity & Proximity Modulation")]
    [Tooltip("Pitch ramp up as monster gets dangerously close (1.0 = normal, 1.08 = frantic).")]
    public float maxProximityPitch = 1.08f;

    [Tooltip("Distance at which chase soundtrack is maximum intensity (meters).")]
    public float dangerDistance = 4f;

    [Tooltip("Distance beyond which music starts winding down if line of sight is broken.")]
    public float escapeDistance = 25f;

    [Header("Screen Shake (Camera Tremor)")]
    [Tooltip("Camera noise component found automatically on Cinemachine follow camera.")]
    public CinemachineBasicMultiChannelPerlin cinemachineNoise;
    public float maxShakeAmplitude = 3.6f;
    public float minShakeAmplitude = 0.8f;
    public float maxShakeFrequency = 6.2f;

    [Header("Heartbeat Modulation")]
    [Range(0f, 1f)] public float maxHeartbeatVolume = 0.72f;

    private AudioSource audioSource;
    private AudioSource heartbeatSource;
    private Transform player;
    private Coroutine fadeRoutine;
    private Coroutine heartbeatFadeRoutine;
    private bool isChasingActive = false;
    private bool isHoundChasing = false;
    private float lastClosestDist = 999f;
    private float currentShakeAmp = 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        SetupAudioSources();
    }

    private void SetupAudioSources()
    {
        // 1. Music AudioSource
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.spatialBlend = 0f; // 2D Full stereo non-diegetic soundtrack
        audioSource.bypassEffects = true;
        audioSource.bypassListenerEffects = true;
        audioSource.volume = 0f;

        if (chaseSoundtrack == null)
        {
            chaseSoundtrack = Resources.Load<AudioClip>("SFX/Outlast_Chase_Intense");
        }

        // Default both to Outlast_Chase_Intense so hound has authentic intense pursuit music
        if (houndChaseSoundtrack == null)
        {
            houndChaseSoundtrack = Resources.Load<AudioClip>("SFX/Outlast_Chase_Intense") ?? chaseSoundtrack;
        }

        if (chaseSoundtrack != null)
        {
            audioSource.clip = chaseSoundtrack;
        }

        // 2. Heartbeat AudioSource
        heartbeatSource = gameObject.AddComponent<AudioSource>();
        heartbeatSource.playOnAwake = false;
        heartbeatSource.loop = true;
        heartbeatSource.spatialBlend = 0f;
        heartbeatSource.volume = 0f;
        heartbeatSource.clip = CreateRapidHeartbeatClip();
    }

    private void Start()
    {
        FindPlayer();
        ResolveNoise();
    }

    private void OnDisable()
    {
        if (cinemachineNoise != null)
        {
            cinemachineNoise.AmplitudeGain = 0f;
        }
    }

    private void FindPlayer()
    {
        if (player != null) return;
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
    }

    private void ResolveNoise()
    {
        if (cinemachineNoise == null)
        {
            cinemachineNoise = FindAnyObjectByType<CinemachineBasicMultiChannelPerlin>();
        }
    }

    private void Update()
    {
        if (player == null)
        {
            FindPlayer();
            if (player == null) return;
        }

        ResolveNoise();

        // Check if ANY threat is actively pursuing the player
        bool villainChasing = false;
        bool houndActive = false;
        float closestDistance = 999f;

        // Check SmilyHoundAI (Exit Guardian Beast / Dog)
        if (SmilyHoundAI.Instance != null && SmilyHoundAI.Instance.gameObject.activeInHierarchy)
        {
            var houndState = SmilyHoundAI.Instance.CurrentState;
            if (houndState == SmilyHoundAI.HoundState.Chasing || houndState == SmilyHoundAI.HoundState.Pouncing)
            {
                houndActive = true;
                villainChasing = true;
                float d = Vector3.Distance(SmilyHoundAI.Instance.transform.position, player.position);
                if (d < closestDistance) closestDistance = d;
            }
        }

        // Check VillainAI (Pirate Clark / Entity)
        if (VillainAI.Instance != null && VillainAI.Instance.gameObject.activeInHierarchy)
        {
            if (VillainAI.Instance.CurrentState == VillainAI.State.Chase)
            {
                villainChasing = true;
                float d = Vector3.Distance(VillainAI.Instance.transform.position, player.position);
                if (d < closestDistance) closestDistance = d;
            }
        }

        // Player hiding in locker or vent immediately breaks active music pursuit
        if (PlayerLockerController.IsInLocker || PlayerVentController.IsInVent ||
            VictoryController.IsGameWon || GameOverController.IsGameOver || PauseMenuController.IsPaused)
        {
            villainChasing = false;
            houndActive = false;
        }

        isHoundChasing = houndActive;
        lastClosestDist = closestDistance;

        AudioClip targetClip = houndActive ? (houndChaseSoundtrack != null ? houndChaseSoundtrack : chaseSoundtrack) : chaseSoundtrack;

        if (villainChasing != isChasingActive || (isChasingActive && audioSource.clip != targetClip))
        {
            isChasingActive = villainChasing;
            if (isChasingActive)
            {
                StartFade(maxChaseVolume, fadeInDuration, targetClip);
                StartHeartbeatFade(maxHeartbeatVolume, 0.5f);
            }
            else
            {
                StartFade(0f, fadeOutDuration, audioSource.clip);
                StartHeartbeatFade(0f, 1.8f);
            }
        }

        // Distance & Pitch Dynamics
        if (isChasingActive && closestDistance < escapeDistance)
        {
            float t = Mathf.InverseLerp(escapeDistance, dangerDistance, closestDistance);

            // Music pitch ramps up slightly when cornered
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.pitch = Mathf.Lerp(1.0f, maxProximityPitch, t);
            }

            // Rapid pounding heartbeat accelerates from 1.0x (~100 BPM) to 1.45x (~145 BPM)
            if (heartbeatSource != null && heartbeatSource.isPlaying)
            {
                heartbeatSource.pitch = Mathf.Lerp(1.05f, 1.45f, t);
            }

            // Screen Shake intensifies closer to the beast
            float targetShake = Mathf.Lerp(minShakeAmplitude, maxShakeAmplitude, t);
            currentShakeAmp = Mathf.MoveTowards(currentShakeAmp, targetShake, Time.deltaTime * 6f);
            if (cinemachineNoise != null)
            {
                cinemachineNoise.AmplitudeGain = currentShakeAmp;
                cinemachineNoise.FrequencyGain = Mathf.Lerp(3f, maxShakeFrequency, t);
            }

            // High-adrenaline heartbeat pulse dynamics (lub-dub double beat for HUD and screen)
            float beatFreq = Mathf.Lerp(1.85f, 2.55f, t);
            heartbeatPhase = (heartbeatPhase + Time.deltaTime * beatFreq) % 1.0f;
            float b1 = Mathf.Clamp01(1f - Mathf.Abs(heartbeatPhase - 0.08f) / 0.10f);
            float b2 = Mathf.Clamp01(1f - Mathf.Abs(heartbeatPhase - 0.30f) / 0.08f) * 0.70f;
            float rawPulse = Mathf.Max(b1, b2);
            CurrentHeartbeatPulse = rawPulse * rawPulse;
        }
        else
        {
            if (audioSource != null && audioSource.isPlaying) audioSource.pitch = 1.0f;
            if (heartbeatSource != null && heartbeatSource.isPlaying) heartbeatSource.pitch = 1.0f;

            currentShakeAmp = Mathf.MoveTowards(currentShakeAmp, 0f, Time.deltaTime * 3f);
            if (cinemachineNoise != null)
            {
                cinemachineNoise.AmplitudeGain = currentShakeAmp;
            }

            heartbeatPhase = 0f;
            CurrentHeartbeatPulse = Mathf.MoveTowards(CurrentHeartbeatPulse, 0f, Time.deltaTime * 4f);
        }
    }

    private void StartFade(float targetVol, float duration, AudioClip targetClip = null)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeSoundtrackRoutine(targetVol, duration, targetClip));
    }

    private IEnumerator FadeSoundtrackRoutine(float targetVol, float duration, AudioClip targetClip = null)
    {
        if (targetClip != null && audioSource.clip != targetClip)
        {
            if (audioSource.isPlaying && audioSource.volume > 0.1f)
            {
                float quickFade = 0.18f;
                float sVol = audioSource.volume;
                float el = 0f;
                while (el < quickFade)
                {
                    el += Time.deltaTime;
                    audioSource.volume = Mathf.Lerp(sVol, 0f, el / quickFade);
                    yield return null;
                }
            }
            audioSource.clip = targetClip;
        }
        else if (audioSource.clip == null && chaseSoundtrack != null)
        {
            audioSource.clip = chaseSoundtrack;
        }

        if (targetVol > 0.001f && !audioSource.isPlaying)
        {
            audioSource.Play();
        }

        float startVol = audioSource.volume;
        float elapsed = 0f;
        float effectiveTargetVol = targetVol * GameSettings.MasterVolume;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / Mathf.Max(0.01f, duration);
            audioSource.volume = Mathf.Lerp(startVol, effectiveTargetVol, t);
            yield return null;
        }

        audioSource.volume = effectiveTargetVol;

        if (targetVol <= 0.001f)
        {
            audioSource.Stop();
            audioSource.pitch = 1.0f;
        }

        fadeRoutine = null;
    }

    private void StartHeartbeatFade(float targetVol, float duration)
    {
        if (heartbeatFadeRoutine != null) StopCoroutine(heartbeatFadeRoutine);
        heartbeatFadeRoutine = StartCoroutine(FadeHeartbeatRoutine(targetVol, duration));
    }

    private IEnumerator FadeHeartbeatRoutine(float targetVol, float duration)
    {
        if (targetVol > 0.001f && heartbeatSource != null && !heartbeatSource.isPlaying)
        {
            heartbeatSource.Play();
        }

        float startVol = heartbeatSource != null ? heartbeatSource.volume : 0f;
        float elapsed = 0f;
        float effectiveTargetVol = targetVol * GameSettings.MasterVolume;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / Mathf.Max(0.01f, duration);
            if (heartbeatSource != null)
            {
                heartbeatSource.volume = Mathf.Lerp(startVol, effectiveTargetVol, t);
            }
            yield return null;
        }

        if (heartbeatSource != null)
        {
            heartbeatSource.volume = effectiveTargetVol;
            if (targetVol <= 0.001f)
            {
                heartbeatSource.Stop();
                heartbeatSource.pitch = 1.0f;
            }
        }

        heartbeatFadeRoutine = null;
    }

    private AudioClip CreateRapidHeartbeatClip()
    {
        int rate = 44100;
        float dur = 0.85f; // Fast, adrenaline-filled lub-dub loop
        int count = (int)(rate * dur);
        AudioClip clip = AudioClip.Create("PursuitAdrenalineHeartbeat", count, 1, rate, false);
        float[] data = new float[count];

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / rate;
            float sample = 0f;

            // Lub (first heavy chest contraction at t = 0.04)
            if (t >= 0.04f && t < 0.26f)
            {
                float lt = t - 0.04f;
                sample += Mathf.Sin(2f * Mathf.PI * 62f * lt) * Mathf.Exp(-lt * 20f) * 0.90f;
                sample += Mathf.Sin(2f * Mathf.PI * 115f * lt) * Mathf.Exp(-lt * 28f) * 0.35f;
            }

            // Dub (second sharp snap at t = 0.28)
            if (t >= 0.28f && t < 0.52f)
            {
                float lt = t - 0.28f;
                sample += Mathf.Sin(2f * Mathf.PI * 72f * lt) * Mathf.Exp(-lt * 24f) * 0.80f;
                sample += Mathf.Sin(2f * Mathf.PI * 140f * lt) * Mathf.Exp(-lt * 35f) * 0.25f;
            }

            data[i] = Mathf.Clamp(sample, -1f, 1f);
        }

        clip.SetData(data, 0);
        return clip;
    }

    public void ForceStop()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        if (heartbeatFadeRoutine != null) StopCoroutine(heartbeatFadeRoutine);
        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.volume = 0f;
        }
        if (heartbeatSource != null)
        {
            heartbeatSource.Stop();
            heartbeatSource.volume = 0f;
        }
        if (cinemachineNoise != null)
        {
            cinemachineNoise.AmplitudeGain = 0f;
        }
        isChasingActive = false;
        isHoundChasing = false;
    }
}
