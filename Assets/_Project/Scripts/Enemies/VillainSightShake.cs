using System;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Shakes the player's camera when the entity spots or pursues them.
/// Dramatically intensifies the screen shake and vibration frequency when the
/// entity is actively chasing and sprinting after the player down corridors.
/// </summary>
public class VillainSightShake : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Player target (PlayerCapsule). Auto-found by tag 'Player' if left empty.")]
    public Transform player;

    [Tooltip("Cinemachine noise module on PlayerFollowCamera. Auto-found if left empty.")]
    public CinemachineBasicMultiChannelPerlin noise;

    [Header("Sight Detection")]
    public float sightRange = 18f;
    [Range(10f, 360f)] public float viewAngle = 120f;     // total cone angle
    public float eyeHeight = 1.5f;                        // villain eye above its pivot
    public float playerAimHeight = 1.0f;                  // point on the player to look at

    [Header("General Stalk Shake")]
    public float maxAmplitude = 1.8f;
    public float frequency = 3.0f;
    public float fadeIn = 7.0f;                           // how fast the shake ramps up
    public float fadeOut = 2.5f;                          // how fast it calms down
    public bool scaleWithDistance = true;                 // closer villain = stronger shake

    [Header("Chase Shake (Running After Us)")]
    [Tooltip("Peak camera shake amplitude when the entity is actively sprinting after the player.")]
    public float chaseMaxAmplitude = 3.8f;
    [Tooltip("Minimum base shake while actively being chased from a distance.")]
    public float chaseMinAmplitude = 1.3f;
    [Tooltip("Frantic vibration frequency when the entity is chasing.")]
    public float chaseFrequency = 6.5f;
    [Tooltip("Distance threshold within which chase screen shake operates.")]
    public float chaseRange = 22f;

    private VillainAI ai;
    private float currentAmplitude;
    private float currentFrequency;

    private void Awake()
    {
        ai = GetComponent<VillainAI>();
        ResolveReferences();
    }

    private void Start()
    {
        ResolveReferences();
    }

    private void OnDisable()
    {
        if (noise != null)
        {
            noise.AmplitudeGain = 0f;
        }
    }

    private void Update()
    {
        ResolveReferences();
        if (player == null || noise == null) return;

        // If game is over, paused, or player is hidden inside a vent, calm down camera shake
        if (GameOverController.IsGameOver || VictoryController.IsGameWon || PauseMenuController.IsPaused || PlayerVentController.IsInVent)
        {
            currentAmplitude = Mathf.MoveTowards(currentAmplitude, 0f, fadeOut * 2f * Time.deltaTime);
            noise.AmplitudeGain = currentAmplitude;
            return;
        }

        bool isChasing = ai != null && ai.currentState == VillainAI.State.Chase;
        float distToPlayer = Vector3.Distance(transform.position, player.position);

        float targetAmplitude = 0f;
        float targetFrequency = frequency;

        if (isChasing && distToPlayer <= chaseRange)
        {
            // =========================================================================
            // ACTIVE PURSUIT: ENTITY IS RUNNING AFTER US
            // =========================================================================
            // Dynamic non-linear ramp: intensity surges dramatically as the entity gets close
            float normDist = Mathf.Clamp01(distToPlayer / chaseRange);
            float closeness = Mathf.Pow(1f - normDist, 0.85f);

            targetAmplitude = Mathf.Lerp(chaseMinAmplitude, chaseMaxAmplitude, closeness);
            targetFrequency = Mathf.Lerp(frequency * 1.5f, chaseFrequency, closeness);
        }
        else if (CanSeePlayer(out float sightDist))
        {
            // =========================================================================
            // SIGHT / STALKING: ENTITY HAS DIRECT LINE OF SIGHT
            // =========================================================================
            targetAmplitude = maxAmplitude;
            if (scaleWithDistance)
            {
                float normSight = Mathf.Clamp01(sightDist / sightRange);
                targetAmplitude *= Mathf.Lerp(1f, 0.35f, normSight);
            }
            targetFrequency = frequency;
        }

        // Smooth acceleration and deceleration for realistic cinematic camera tremor
        float speed = targetAmplitude > currentAmplitude ? fadeIn : fadeOut;
        currentAmplitude = Mathf.MoveTowards(currentAmplitude, targetAmplitude, speed * Time.deltaTime);
        currentFrequency = Mathf.MoveTowards(currentFrequency, targetFrequency, speed * Time.deltaTime);

        noise.AmplitudeGain = currentAmplitude;
        noise.FrequencyGain = currentFrequency;
    }

    private void ResolveReferences()
    {
        if (ai == null) ai = GetComponent<VillainAI>();

        if (player == null)
        {
            GameObject pGo = GameObject.FindGameObjectWithTag("Player");
            if (pGo != null) player = pGo.transform;
        }

        if (noise == null)
        {
            noise = FindAnyObjectByType<CinemachineBasicMultiChannelPerlin>();
        }
    }

    private bool CanSeePlayer(out float dist)
    {
        dist = 0f;
        if (player == null) return false;

        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 aim = player.position + Vector3.up * playerAimHeight;
        Vector3 toPlayer = aim - eye;
        dist = toPlayer.magnitude;

        if (dist > sightRange) return false;

        Vector3 flat = new Vector3(toPlayer.x, 0f, toPlayer.z);
        if (Vector3.Angle(transform.forward, flat) > viewAngle * 0.5f) return false;

        // Check line of sight (skip own colliders)
        RaycastHit[] hits = Physics.RaycastAll(eye, toPlayer.normalized, dist, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit h in hits)
        {
            if (h.transform.IsChildOf(transform)) continue;
            return h.transform == player || h.transform.IsChildOf(player);
        }

        return false;
    }
}
