using System.Collections;
using UnityEngine;

/// <summary>
/// Dedicated Horror Chase Soundtrack Director (Outlast 2 Style).
/// Dynamically monitors active pursuit from monsters (VillainAI, SmilyHoundAI).
/// Plays full 2D non-diegetic heart-pounding chase music that crossfades seamlessly
/// with distance intensity, and fades out cleanly when the player escapes or hides.
/// </summary>
public class ChaseSoundtrackDirector : MonoBehaviour
{
    public static ChaseSoundtrackDirector Instance { get; private set; }

    [Header("Soundtrack Settings")]
    [Tooltip("The intense Outlast 2 style chase soundtrack clip.")]
    public AudioClip chaseSoundtrack;

    [Range(0f, 1f)]
    [Tooltip("Target volume of the soundtrack when in active chase.")]
    public float maxChaseVolume = 0.85f;

    [Tooltip("Speed of fade in when chase begins (seconds).")]
    public float fadeInDuration = 0.45f;

    [Tooltip("Speed of fade out when chase ends or player hides (seconds).")]
    public float fadeOutDuration = 2.2f;

    [Header("Intensity & Proximity Modulation")]
    [Tooltip("Pitch ramp up as monster gets dangerously close (1.0 = normal, 1.08 = frantic).")]
    public float maxProximityPitch = 1.08f;

    [Tooltip("Distance at which chase soundtrack is maximum intensity (meters).")]
    public float dangerDistance = 4f;

    [Tooltip("Distance beyond which music starts winding down if line of sight is broken.")]
    public float escapeDistance = 25f;

    private AudioSource audioSource;
    private Transform player;
    private Coroutine fadeRoutine;
    private float currentVolume = 0f;
    private bool isChasingActive = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        SetupAudioSource();
    }

    private void SetupAudioSource()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.spatialBlend = 0f; // 2D Full stereo non-diegetic movie/game soundtrack
        audioSource.bypassEffects = true;
        audioSource.bypassListenerEffects = true;
        audioSource.volume = 0f;

        if (chaseSoundtrack == null)
        {
            chaseSoundtrack = Resources.Load<AudioClip>("SFX/Outlast_Chase_Intense");
        }

        if (chaseSoundtrack != null)
        {
            audioSource.clip = chaseSoundtrack;
        }
    }

    private void Start()
    {
        FindPlayer();
    }

    private void FindPlayer()
    {
        if (player != null) return;
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
    }

    private void Update()
    {
        if (player == null)
        {
            FindPlayer();
            if (player == null) return;
        }

        // Check if ANY threat is actively pursuing the player
        bool villainChasing = false;
        float closestDistance = 999f;

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

        // Check SmilyHoundAI (Exit Guardian Beast)
        if (SmilyHoundAI.Instance != null && SmilyHoundAI.Instance.gameObject.activeInHierarchy)
        {
            var houndState = SmilyHoundAI.Instance.CurrentState;
            if (houndState == SmilyHoundAI.HoundState.Chasing || houndState == SmilyHoundAI.HoundState.Pouncing)
            {
                villainChasing = true;
                float d = Vector3.Distance(SmilyHoundAI.Instance.transform.position, player.position);
                if (d < closestDistance) closestDistance = d;
            }
        }

        // Player hiding in locker or vent immediately breaks active music pursuit
        if (PlayerLockerController.IsInLocker || PlayerVentController.IsInVent)
        {
            villainChasing = false;
        }

        // Victory or GameOver ends chase music
        if (VictoryController.IsGameWon || GameOverController.IsGameOver)
        {
            villainChasing = false;
        }

        if (villainChasing != isChasingActive)
        {
            isChasingActive = villainChasing;
            if (isChasingActive)
            {
                StartFade(maxChaseVolume, fadeInDuration);
            }
            else
            {
                StartFade(0f, fadeOutDuration);
            }
        }

        // Outlast 2 Proximity Pitch Dynamics: as beast closes in, pitch rises slightly creating frantic panic
        if (isChasingActive && audioSource.isPlaying && closestDistance < escapeDistance)
        {
            float t = Mathf.InverseLerp(escapeDistance, dangerDistance, closestDistance);
            audioSource.pitch = Mathf.Lerp(1.0f, maxProximityPitch, t);
        }
        else if (audioSource.isPlaying)
        {
            audioSource.pitch = 1.0f;
        }
    }

    private void StartFade(float targetVol, float duration)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeSoundtrackRoutine(targetVol, duration));
    }

    private IEnumerator FadeSoundtrackRoutine(float targetVol, float duration)
    {
        if (audioSource.clip == null && chaseSoundtrack != null)
        {
            audioSource.clip = chaseSoundtrack;
        }

        if (targetVol > 0.001f && !audioSource.isPlaying)
        {
            audioSource.Play();
        }

        float startVol = audioSource.volume;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / Mathf.Max(0.01f, duration);
            audioSource.volume = Mathf.Lerp(startVol, targetVol, t);
            yield return null;
        }

        audioSource.volume = targetVol;

        if (targetVol <= 0.001f)
        {
            audioSource.Stop();
            audioSource.pitch = 1.0f;
        }

        fadeRoutine = null;
    }

    /// <summary>
    /// Explicitly trigger immediate stop on scene change or reset.
    /// </summary>
    public void ForceStop()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.volume = 0f;
        }
        isChasingActive = false;
    }
}
