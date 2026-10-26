using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Smart Territorial AI for the 4-Legged Smily Hound guarding the exit portal.
/// Features:
/// 1. Portal Guard Patrol: Prowls the perimeter and choke points around the exit.
/// 2. Audio & Scent Tracking: Listens for player footsteps, doors, lockers, and flashlight.
/// 3. Corner Ambush: Senses player approaching and hides behind doorframes/corners to ambush.
/// 4. High-Speed 4-Legged Gallop: Rapid predatory chase with dynamic pounce attack.
/// 5. Locker & Vent Awareness: Rushes to and scratches/sniffs hiding spots.
/// 6. Leash Guarding: Never abandons the exit portal if the player runs too far away.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
public class SmilyHoundAI : MonoBehaviour
{
    public static SmilyHoundAI Instance { get; private set; }

    public enum HoundState
    {
        GuardingPortal,     // Prowling and sniffing choke points near the exit
        InvestigatingSound, // Alerted by noise, creeping to investigate source
        AmbushWaiting,      // Hiding just around the corner to ambush the player
        Chasing,            // Direct visual contact, fast 4-legged bounding gallop
        Pouncing,           // Lethal close-range lunge bite
        InspectingHiding,   // Sniffing and scratching at a locker/vent the player entered
        ReturningToPortal   // Player fled too far, retreating to guard the exit
    }

    [Header("Territory & Anchor")]
    [Tooltip("World position of the exit portal being guarded.")]
    public Vector3 portalPosition;
    [Tooltip("Maximum radius around the portal the hound considers its territory.")]
    public float territoryRadius = 22f;
    [Tooltip("Distance from portal beyond which the hound abandons the chase to protect the exit.")]
    public float leashMaxDistance = 32f;

    [Header("Speeds")]
    public float patrolSpeed = 1.6f;
    public float stalkSpeed = 2.6f;
    public float chaseSpeed = 4.5f;

    [Header("Senses")]
    public float viewDistance = 14f;
    public float viewDistanceFlashlight = 24f;
    [Range(20f, 180f)] public float viewAngle = 110f;
    public float eyeHeight = 0.85f; // Low quadruped eye height
    public float hearingRadius = 26f;
    public float scentRadius = 9f;  // Can smell player around corners at close range

    [Header("Combat & Pounce")]
    public float pounceDistance = 2.4f;
    public float catchDistance = 1.2f;

    [Header("Audio")]
    [Range(0f, 1f)] public float soundVolume = 0.9f;

    public HoundState CurrentState { get; private set; } = HoundState.GuardingPortal;

    private NavMeshAgent agent;
    private SmilyMonsterGait gait;
    private AudioSource audioSource;
    private Transform player;

    private float stateTimer;
    private float sniffTimer;
    private Vector3 lastHeardNoisePos;
    private Vector3 ambushCornerPos;
    private Vector3 lastKnownPlayerPos;
    private bool playerInSight;

    private Transform targetedHidingSpot;
    private Coroutine pounceRoutine;

    private void Awake()
    {
        Instance = this;
        agent = GetComponent<NavMeshAgent>();
        gait = GetComponent<SmilyMonsterGait>();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1.0f;
        audioSource.minDistance = 2.0f;
        audioSource.maxDistance = 24.0f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
    }

    private void OnEnable()
    {
        PlayerNoiseEmitter.OnNoiseEmitted += HandleNoiseHeard;
    }

    private void OnDisable()
    {
        PlayerNoiseEmitter.OnNoiseEmitted -= HandleNoiseHeard;
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (portalPosition == Vector3.zero) portalPosition = transform.position;

        FindPlayer();
        sniffTimer = Random.Range(3f, 6f);
    }

    private void FindPlayer()
    {
        if (player != null) return;
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
    }

    public void SetPortalAnchor(Vector3 portalPos)
    {
        portalPosition = portalPos;
    }

    private void Update()
    {
        if (player == null) { FindPlayer(); return; }
        if (PauseMenuController.IsPaused || GameOverController.IsGameOver || VictoryController.IsGameWon)
        {
            if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
            return;
        }

        CheckPlayerSenses();

        switch (CurrentState)
        {
            case HoundState.GuardingPortal:
                UpdateGuardingPortal();
                break;
            case HoundState.InvestigatingSound:
                UpdateInvestigatingSound();
                break;
            case HoundState.AmbushWaiting:
                UpdateAmbushWaiting();
                break;
            case HoundState.Chasing:
                UpdateChasing();
                break;
            case HoundState.InspectingHiding:
                UpdateInspectingHiding();
                break;
            case HoundState.ReturningToPortal:
                UpdateReturningToPortal();
                break;
        }
    }

    // =========================================================================
    // Senses: Sight, Flashlight, Hearing, and Scent
    // =========================================================================

    private void CheckPlayerSenses()
    {
        if (player == null) return;

        bool isHidden = (PlayerLockerController.IsInLocker || PlayerVentController.IsInVent);
        float distToPlayer = Vector3.Distance(transform.position, player.position);

        if (isHidden)
        {
            playerInSight = false;
            // If chasing and player just ducked into hiding right now
            if (CurrentState == HoundState.Chasing && distToPlayer < 12f)
            {
                InvestigatePlayerHiding();
            }
            return;
        }

        // 1. Line of Sight Check
        Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
        Vector3 playerAim = player.position + Vector3.up * 1.0f;
        Vector3 toPlayer = (playerAim - eyePos).normalized;

        float maxRange = viewDistance;

        // Flashlight bonus
        Flashlight fl = player.GetComponentInChildren<Flashlight>();
        if (fl != null && fl.IsOn) maxRange = viewDistanceFlashlight;

        playerInSight = false;

        if (distToPlayer <= maxRange)
        {
            float angle = Vector3.Angle(transform.forward, toPlayer);
            if (angle <= viewAngle * 0.5f || distToPlayer <= scentRadius)
            {
                if (!Physics.Raycast(eyePos, toPlayer, distToPlayer, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore))
                {
                    playerInSight = true;
                    lastKnownPlayerPos = player.position;
                }
            }
        }

        // 2. React to Sight
        if (playerInSight)
        {
            float distFromPortal = Vector3.Distance(player.position, portalPosition);
            if (distFromPortal <= leashMaxDistance)
            {
                if (CurrentState != HoundState.Chasing && CurrentState != HoundState.Pouncing)
                {
                    StartChase();
                }
            }
        }
    }

    private void HandleNoiseHeard(Vector3 noiseOrigin, float noiseRadius)
    {
        if (CurrentState == HoundState.Chasing || CurrentState == HoundState.Pouncing) return;

        float distToNoise = Vector3.Distance(transform.position, noiseOrigin);
        float distNoiseToPortal = Vector3.Distance(noiseOrigin, portalPosition);

        // Alert if noise is within hearing radius AND in portal territory
        if (distToNoise <= hearingRadius && distNoiseToPortal <= leashMaxDistance)
        {
            lastHeardNoisePos = noiseOrigin;

            // Smart Ambush Decision:
            // If the player is creeping towards the portal through a choke point,
            // the hound slips behind a corner instead of giving itself away!
            if (distToNoise < 18f && Random.value < 0.45f && FindAmbushSpot(noiseOrigin, out Vector3 ambushSpot))
            {
                ambushCornerPos = ambushSpot;
                SetState(HoundState.AmbushWaiting, Random.Range(4f, 8f));
                agent.speed = stalkSpeed;
                agent.SetDestination(ambushCornerPos);
                PlayAudio(HoundAudio.Alert, 0.7f);
            }
            else
            {
                SetState(HoundState.InvestigatingSound, 12f);
                agent.speed = stalkSpeed;
                agent.SetDestination(noiseOrigin);
                PlayAudio(HoundAudio.Alert, 0.85f);
            }
        }
    }

    // =========================================================================
    // State Handlers
    // =========================================================================

    private void UpdateGuardingPortal()
    {
        agent.speed = patrolSpeed;

        // Sniffing and growling pauses
        sniffTimer -= Time.deltaTime;
        if (sniffTimer <= 0f)
        {
            sniffTimer = Random.Range(7f, 14f);
            PlayAudio(HoundAudio.Growl, 0.65f);
        }

        // Pick new patrol destination around portal
        if (!agent.pathPending && agent.remainingDistance <= 0.6f)
        {
            Vector3 patrolPoint = PickTerritoryPoint();
            agent.SetDestination(patrolPoint);
        }
    }

    private void UpdateInvestigatingSound()
    {
        agent.speed = stalkSpeed;

        if (!agent.pathPending && agent.remainingDistance <= 1.2f)
        {
            // Reached noise origin, sniffs around
            stateTimer -= Time.deltaTime;
            if (stateTimer <= 0f)
            {
                // Found nothing, returns to guard portal
                SetState(HoundState.ReturningToPortal, 0f);
            }
        }
    }

    private void UpdateAmbushWaiting()
    {
        agent.speed = stalkSpeed;

        if (!agent.pathPending && agent.remainingDistance <= 0.8f)
        {
            // Reached corner, lies in wait silently
            stateTimer -= Time.deltaTime;
            if (stateTimer <= 0f)
            {
                SetState(HoundState.GuardingPortal, 0f);
            }
        }
    }

    private void UpdateChasing()
    {
        agent.speed = chaseSpeed;

        float distToPlayer = Vector3.Distance(transform.position, player.position);
        float distPlayerToPortal = Vector3.Distance(player.position, portalPosition);

        // 1. Leash Guard Check: Never let the player kite the hound to the other end of the map
        if (distPlayerToPortal > leashMaxDistance)
        {
            PlayAudio(HoundAudio.Bark, 0.95f);
            SetState(HoundState.ReturningToPortal, 0f);
            return;
        }

        // 2. Direct Destination Tracking
        if (playerInSight)
        {
            agent.SetDestination(player.position);
        }
        else
        {
            agent.SetDestination(lastKnownPlayerPos);
            if (!agent.pathPending && agent.remainingDistance <= 1.0f)
            {
                // Lost sight at last known spot
                SetState(HoundState.InvestigatingSound, 5f);
                return;
            }
        }

        // 3. Pounce Range Check
        if (distToPlayer <= pounceDistance && playerInSight)
        {
            TriggerPounceAttack();
        }
    }

    private void UpdateReturningToPortal()
    {
        agent.speed = stalkSpeed;
        agent.SetDestination(portalPosition);

        if (!agent.pathPending && agent.remainingDistance <= 2.5f)
        {
            SetState(HoundState.GuardingPortal, 0f);
        }
    }

    private void UpdateInspectingHiding()
    {
        agent.speed = stalkSpeed;
        stateTimer -= Time.deltaTime;

        if (stateTimer <= 0f)
        {
            SetState(HoundState.GuardingPortal, 0f);
        }
    }

    // =========================================================================
    // Attacks & Smart Maneuvers
    // =========================================================================

    private void StartChase()
    {
        SetState(HoundState.Chasing, 0f);
        PlayAudio(HoundAudio.Roar, 1.0f);
        agent.speed = chaseSpeed;
        agent.SetDestination(player.position);
    }

    private void TriggerPounceAttack()
    {
        if (pounceRoutine != null) return;
        pounceRoutine = StartCoroutine(PounceAttackRoutine());
    }

    private IEnumerator PounceAttackRoutine()
    {
        SetState(HoundState.Pouncing, 0.8f);
        agent.isStopped = true;

        if (gait != null) gait.TriggerPounce();
        PlayAudio(HoundAudio.Bite, 1.0f);

        // Turn sharply to face player during leap
        Vector3 toPlayer = (player.position - transform.position).normalized;
        toPlayer.y = 0;
        if (toPlayer != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(toPlayer);
        }

        yield return new WaitForSeconds(0.35f);

        // Damage check
        float hitDist = Vector3.Distance(transform.position, player.position);
        if (hitDist <= pounceDistance * 1.25f && !PlayerLockerController.IsInLocker && !PlayerVentController.IsInVent)
        {
            PlayerSanity sanity = player.GetComponent<PlayerSanity>();
            if (sanity != null) sanity.DrainSanity(50f);

            GameOverController.TriggerDefeat(transform);
        }

        yield return new WaitForSeconds(0.45f);

        agent.isStopped = false;
        pounceRoutine = null;
        SetState(HoundState.Chasing, 0f);
    }

    private void InvestigatePlayerHiding()
    {
        // Player dove into a locker or vent
        Vector3 hidingPos = player.position;
        SetState(HoundState.InspectingHiding, Random.Range(6f, 9f));

        agent.speed = stalkSpeed;
        agent.SetDestination(hidingPos);
        PlayAudio(HoundAudio.Growl, 0.95f);
    }

    private bool FindAmbushSpot(Vector3 noiseOrigin, out Vector3 spot)
    {
        spot = Vector3.zero;
        // Sample points perpendicular to the path between noise and portal
        Vector3 dir = (noiseOrigin - portalPosition).normalized;
        Vector3 side = Vector3.Cross(dir, Vector3.up);

        for (int i = -1; i <= 1; i += 2)
        {
            Vector3 test = portalPosition + dir * 6f + side * (i * 4f);
            if (NavMesh.SamplePosition(test, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                spot = hit.position;
                return true;
            }
        }
        return false;
    }

    private Vector3 PickTerritoryPoint()
    {
        for (int i = 0; i < 12; i++)
        {
            Vector2 circle = Random.insideUnitCircle * territoryRadius;
            Vector3 candidate = portalPosition + new Vector3(circle.x, 0f, circle.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }
        return portalPosition;
    }

    private void SetState(HoundState newState, float duration)
    {
        CurrentState = newState;
        stateTimer = duration;
    }

    private void PlayAudio(AudioClip clip, float volume)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.pitch = Random.Range(0.92f, 1.08f);
            audioSource.PlayOneShot(clip, volume * soundVolume * GameSettings.MasterVolume);
        }
    }
}
