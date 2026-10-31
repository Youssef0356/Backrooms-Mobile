using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Smart Territorial AI for the 4-Legged Smily Hound guarding the exit portal.
/// Features:
/// 1. Silent Portal Guard Patrol: Prowls choke points around the exit without noisy step/scratch spam.
/// 2. Fair Stealth & Detection Meter: 9m vision when flashlight is off (22m when on), ignores quiet movement > 6m.
/// 3. Smooth Custom Rotation: agent.updateRotation = false; faces movement direction around corners and locks onto player within 6.5m.
/// 4. Wall Clearance Solver: Front snout, mid-spine, and rear raycast push-out so the body never clips walls.
/// 5. Guarantees ChaseSoundtrackDirector plays nightmare-jumpscare during pursuit and FNAF2 scream on kill.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
public class SmilyHoundAI : MonoBehaviour
{
    public static SmilyHoundAI Instance { get; private set; }

    public enum HoundState
    {
        Wander,             // Freely wandering the entire Backrooms maze like Bacteria
        InvestigatingSound, // Alerted by noise, hunting the sound source
        AmbushWaiting,      // Lurking around a corner/choke point
        Chasing,            // Direct pursuit, fast 4-legged gallop
        Pouncing,           // Lethal close-range lunge bite
        InspectingHiding,   // Sniffing at a locker/vent
        GuardingPortal = Wander // Alias for backward compatibility
    }

    [Header("Territory & Anchor (Exit Sector)")]
    [Tooltip("World position of the exit portal/territory anchor being guarded.")]
    public Vector3 portalPosition;
    [Tooltip("Maximum radius around the portal the hound considers its patrol territory.")]
    public float territoryRadius = 24f;
    [Tooltip("Distance from portal beyond which the hound breaks off pursuit to protect the exit.")]
    public float leashMaxDistance = 34f;
    [Tooltip("Radius around current position to pick random wander points.")]
    public float wanderRadius = 14f;
    [Tooltip("Seconds to pause at each wander waypoint before selecting a new path.")]
    public float waitAtPoint = 2.0f;

    [Header("Speeds")]
    public float patrolSpeed = 1.6f;
    public float stalkSpeed = 2.6f;
    public float chaseSpeed = 4.4f;

    [Header("Senses (Fair Stealth)")]
    [Tooltip("View distance when the player's flashlight is OFF.")]
    public float viewDistance = 9f;
    [Tooltip("View distance when the player's flashlight is ON.")]
    public float viewDistanceFlashlight = 22f;
    [Range(20f, 180f)] public float viewAngle = 105f;
    public float eyeHeight = 0.70f;
    public float hearingRadius = 22f;
    public float scentRadius = 4.2f;

    [Header("Combat & Pounce")]
    public float pounceDistance = 2.4f;
    public float catchDistance = 1.35f;

    [Header("Audio")]
    [Range(0f, 1f)] public float soundVolume = 0.85f;
    [Tooltip("Special jumpscare scream played when this beast kills the player (defaults to FNAF 2 scream).")]
    public AudioClip killScreamAudio;

    [Header("Awareness & Stealth")]
    [Tooltip("Detection awareness meter from 0 (unaware) to 1 (full pursuit).")]
    public float detectionMeter = 0f;

    public HoundState CurrentState { get; private set; } = HoundState.Wander;
    public bool PlayerInSight => playerInSight;

    private NavMeshAgent agent;
    private CapsuleCollider bodyCollider;
    private SmilyMonsterGait gait;
    private AudioSource audioSource;
    private AudioLowPassFilter occlusionFilter;
    private Transform player;

    private float stateTimer;
    private float waitTimer;
    private float wanderTimeoutTimer;
    private NavMeshPath testPath;

    private Vector3 lastHeardNoisePos;
    private Vector3 ambushCornerPos;
    private Vector3 lastKnownPlayerPos;
    private bool playerInSight;
    private bool hasPlayedAlertGrowl;
    private float chaseVocalTimer;
    private float wanderGrowlTimer = 6f;

    private Coroutine pounceRoutine;

    private void Awake()
    {
        Instance = this;

        // Enforce fair stealth & territory defaults calibrated for Inside the Backrooms quadruped
        viewDistance = 9f;
        viewDistanceFlashlight = 22f;
        scentRadius = 4.2f;
        hearingRadius = 22f;
        pounceDistance = 2.4f;
        catchDistance = 1.35f;
        eyeHeight = 0.70f;

        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.radius = 0.42f;
            agent.height = 0.90f;
            agent.acceleration = 24f;
            agent.angularSpeed = 720f;
            agent.stoppingDistance = 1.1f;
            agent.updateRotation = false; // Custom smooth rotation so the hound never snaps sideways
            agent.updatePosition = true;  // NATIVE NavMesh positioning guarantees hound NEVER crosses through walls!
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.avoidancePriority = 35;
        }

        // Remove CharacterController if present so it doesn't fight NavMeshAgent or pop through walls
        CharacterController oldCC = GetComponent<CharacterController>();
        if (oldCC != null) Destroy(oldCC);
        Rigidbody legacyRb = GetComponent<Rigidbody>();
        if (legacyRb != null) Destroy(legacyRb);

        // Standard non-conflicting CapsuleCollider for hitboxes and raycasts
        bodyCollider = GetComponent<CapsuleCollider>();
        if (bodyCollider == null) bodyCollider = gameObject.AddComponent<CapsuleCollider>();
        bodyCollider.radius = 0.42f;
        bodyCollider.height = 0.85f;
        bodyCollider.center = new Vector3(0f, 0.425f, 0f);
        bodyCollider.direction = 1;
        bodyCollider.isTrigger = false;

        // Auto-center and scale visual child to align 3D glTF mesh with NavMeshAgent and CapsuleCollider
        Transform visualChild = transform.Find("VisualModel") ?? transform.Find("Sketchfab_model") ?? (transform.childCount > 0 ? transform.GetChild(0) : null);
        if (visualChild != null)
        {
            // Center the GLB creature model so the spine, paws, and collision capsule align at (0,0,0) facing forward (+Z)
            visualChild.localPosition = new Vector3(-0.98f, 0f, -0.18f);
            visualChild.localRotation = Quaternion.identity;
            visualChild.localScale = new Vector3(0.013f, 0.013f, 0.013f);
        }

        gait = GetComponent<SmilyMonsterGait>();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 1.0f;
        audioSource.minDistance = 2.0f;
        audioSource.maxDistance = 20.0f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;

        occlusionFilter = GetComponent<AudioLowPassFilter>();
        if (occlusionFilter == null) occlusionFilter = gameObject.AddComponent<AudioLowPassFilter>();
        occlusionFilter.cutoffFrequency = 22000f;
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
        if (killScreamAudio == null) killScreamAudio = HoundAudio.KillScream;
        testPath = new NavMeshPath();
        waitTimer = 1.0f;

        // Guarantee ChaseSoundtrackDirector exists in scene so nightmare-jumpscare always plays during pursuit
        if (ChaseSoundtrackDirector.Instance == null)
        {
            GameObject directorGo = new GameObject("ChaseSoundtrackDirector");
            directorGo.AddComponent<ChaseSoundtrackDirector>();
        }

        FindPlayer();
    }

    private void FindPlayer()
    {
        if (player != null) return;
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            if (gait != null) gait.lookTarget = player;

            CharacterController playerCC = p.GetComponent<CharacterController>();
            if (playerCC != null && bodyCollider != null)
            {
                Physics.IgnoreCollision(bodyCollider, playerCC, true);
            }
        }
    }

    /// <summary>
    /// Sets the territorial patrol anchor (Exit Portal) for the hound to guard.
    /// </summary>
    public void SetPortalAnchor(Vector3 portalPos)
    {
        portalPosition = portalPos;
    }

    private void Update()
    {
        if (player == null) { FindPlayer(); return; }
        if (PauseMenuController.IsPaused || GameOverController.IsGameOver || VictoryController.IsGameWon)
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
            return;
        }

        CheckPlayerSenses();
        UpdateAudioOcclusion();

        switch (CurrentState)
        {
            case HoundState.Wander:
                UpdateWander();
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
        }

        UpdateBodyFacing();
    }

    /// <summary>
    /// Smoothly rotates the hound's body so it follows its path around corners and locks its face onto the player during pursuit.
    /// </summary>
    private void UpdateBodyFacing()
    {
        if (CurrentState == HoundState.Pouncing) return;

        Vector3 desiredDir = Vector3.zero;
        float turnSpeed = 8.5f;

        if (CurrentState == HoundState.Chasing && player != null)
        {
            turnSpeed = 12.0f;
            float dist = Vector3.Distance(transform.position, player.position);
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;

            Vector3 vel = agent != null ? agent.velocity : Vector3.zero;
            vel.y = 0f;

            if (playerInSight && dist <= 6.5f && toPlayer.sqrMagnitude > 0.04f)
            {
                // Within 6.5m with line of sight: face directly at the player so the smiling head charges straight at you
                desiredDir = toPlayer.normalized;
            }
            else if (vel.sqrMagnitude > 0.08f && toPlayer.sqrMagnitude > 0.04f && playerInSight)
            {
                // Blend path velocity with player direction so corners look natural while still angling toward target
                desiredDir = Vector3.Slerp(vel.normalized, toPlayer.normalized, 0.40f).normalized;
            }
            else if (vel.sqrMagnitude > 0.08f)
            {
                desiredDir = vel.normalized;
            }
            else if (toPlayer.sqrMagnitude > 0.04f)
            {
                desiredDir = toPlayer.normalized;
            }
        }
        else if (CurrentState == HoundState.AmbushWaiting && player != null)
        {
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.04f) desiredDir = toPlayer.normalized;
        }
        else if (agent != null)
        {
            Vector3 vel = agent.velocity;
            vel.y = 0f;
            if (vel.sqrMagnitude > 0.08f)
            {
                desiredDir = vel.normalized;
            }
            else if (agent.desiredVelocity.sqrMagnitude > 0.08f)
            {
                desiredDir = agent.desiredVelocity.normalized;
            }
        }

        if (desiredDir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(desiredDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * turnSpeed);
        }
    }

    private void UpdateAudioOcclusion()
    {
        if (occlusionFilter == null || player == null) return;
        Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
        Vector3 playerHead = player.position + Vector3.up * 1.5f;
        bool blocked = Physics.Linecast(eyePos, playerHead, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore);
        float targetCutoff = blocked ? 900f : 22000f;
        occlusionFilter.cutoffFrequency = Mathf.Lerp(occlusionFilter.cutoffFrequency, targetCutoff, Time.deltaTime * 8f);
    }

    private bool HasClearLineOfSightToPlayer()
    {
        if (player == null) return false;
        Vector3 origin = transform.position + Vector3.up * 0.85f;
        Vector3 target = player.position + Vector3.up * 1.0f;
        return !Physics.Linecast(origin, target, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore);
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
            if (CurrentState == HoundState.Chasing && distToPlayer < 12f)
            {
                InvestigatePlayerHiding();
            }
            return;
        }

        Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
        Vector3 playerAim = player.position + Vector3.up * 1.0f;
        Vector3 toPlayer = (playerAim - eyePos).normalized;

        Flashlight fl = player.GetComponentInChildren<Flashlight>();
        bool flashlightOn = (fl != null && fl.IsOn);

        // Phase 2.3: 9m view distance when flashlight is OFF, 22m when flashlight is ON
        float maxRange = flashlightOn ? viewDistanceFlashlight : viewDistance;

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

        if (playerInSight)
        {
            StarterAssets.FirstPersonController fpc = player.GetComponent<StarterAssets.FirstPersonController>();
            bool isSprinting = fpc != null && fpc.IsSprinting;

            // Quiet walking with flashlight off beyond 6m builds suspicion slowly so player can sneak past
            float rate = 0.28f;
            if (flashlightOn) rate += 0.95f;
            if (isSprinting) rate += 1.65f;
            if (distToPlayer < 4.5f) rate += 1.15f;
            else if (!flashlightOn && !isSprinting && distToPlayer > 6.0f) rate = 0.12f;

            detectionMeter = Mathf.Clamp01(detectionMeter + rate * Time.deltaTime);

            if (CurrentState != HoundState.Chasing && CurrentState != HoundState.Pouncing)
            {
                if (detectionMeter >= 0.45f && CurrentState == HoundState.Wander)
                {
                    SetState(HoundState.InvestigatingSound, 4.5f);
                    agent.isStopped = false;
                    agent.speed = stalkSpeed;
                    agent.SetDestination(lastKnownPlayerPos);

                    // Single alarming screech snarl when genuinely alert with line of sight
                    if (!hasPlayedAlertGrowl)
                    {
                        hasPlayedAlertGrowl = true;
                        PlayAudio(HoundAudio.Alert, 0.85f);
                    }
                }

                if (detectionMeter >= 1.0f)
                {
                    StartChase();
                }
            }
        }
        else if (CurrentState != HoundState.Chasing)
        {
            detectionMeter = Mathf.MoveTowards(detectionMeter, 0f, Time.deltaTime * 0.55f);
            if (detectionMeter < 0.15f) hasPlayedAlertGrowl = false;
        }
    }

    private void HandleNoiseHeard(Vector3 noiseOrigin, float noiseRadius)
    {
        if (CurrentState == HoundState.Chasing || CurrentState == HoundState.Pouncing) return;

        float distToNoise = Vector3.Distance(transform.position, noiseOrigin);
        float distNoiseFromPortal = Vector3.Distance(portalPosition, noiseOrigin);

        // Do not respond to noises far outside the hound's territory (Bacteria handles the rest of the maze)
        if (portalPosition != Vector3.zero && distNoiseFromPortal > territoryRadius * 1.35f && distToNoise > 12.0f) return;

        // Ignore quiet walking noises beyond 6m so player doesn't trigger constant alert reactions
        if (noiseRadius < 8f && distToNoise > 6.0f) return;

        if (distToNoise <= hearingRadius)
        {
            lastHeardNoisePos = noiseOrigin;
            PlayAudio(HoundAudio.Growl, 0.55f);

            if (distToNoise < 16f && Random.value < 0.40f && FindAmbushSpot(noiseOrigin, out Vector3 ambushSpot))
            {
                ambushCornerPos = ambushSpot;
                SetState(HoundState.AmbushWaiting, Random.Range(4f, 7f));
                agent.isStopped = false;
                agent.speed = stalkSpeed;
                agent.SetDestination(ambushCornerPos);
            }
            else
            {
                SetState(HoundState.InvestigatingSound, 8f);
                agent.isStopped = false;
                agent.speed = stalkSpeed;
                agent.SetDestination(noiseOrigin);
            }
        }
    }

    // =========================================================================
    // State Handlers: Exit Portal Territorial Patrol & Ambush
    // =========================================================================

    private void UpdateWander()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        // Active mutual separation: if Bacteria is too close in the same hallway, diverge immediately!
        if (VillainAI.Instance != null && VillainAI.Instance.gameObject.activeInHierarchy)
        {
            float distToBacteria = Vector3.Distance(transform.position, VillainAI.Instance.transform.position);
            if (distToBacteria < 16f)
            {
                agent.isStopped = false;
                agent.speed = patrolSpeed;
                Vector3 dest = PickWanderDestination();
                agent.SetDestination(dest);
                waitTimer = 0f;
                wanderTimeoutTimer = 0f;
                return;
            }
        }

        agent.speed = patrolSpeed;

        if (waitTimer > 0f)
        {
            waitTimer -= Time.deltaTime;
            agent.isStopped = true;
            if (waitTimer <= 0f)
            {
                agent.isStopped = false;
                Vector3 dest = PickWanderDestination();
                agent.SetDestination(dest);
                wanderTimeoutTimer = 0f;
            }
            return;
        }

        agent.isStopped = false;
        wanderTimeoutTimer += Time.deltaTime;

        // Arrived at destination or took too long (stuck watchdog) -> pause and pick next path
        if ((!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.35f) || wanderTimeoutTimer > 14f)
        {
            waitTimer = Random.Range(waitAtPoint * 0.6f, waitAtPoint * 1.5f);
            wanderTimeoutTimer = 0f;
        }

        // Occasional eerie guttural growl when player is within earshot (18m)
        wanderGrowlTimer -= Time.deltaTime;
        if (wanderGrowlTimer <= 0f)
        {
            wanderGrowlTimer = Random.Range(9f, 15f);
            if (player != null && Vector3.Distance(transform.position, player.position) < 18f)
            {
                PlayAudio(HoundAudio.Growl, 0.40f);
            }
        }
    }

    private void UpdateInvestigatingSound()
    {
        agent.speed = stalkSpeed;
        agent.isStopped = false;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.35f)
        {
            stateTimer -= Time.deltaTime;
            if (stateTimer <= 0f)
            {
                SetState(HoundState.Wander, 0f);
                waitTimer = 1.0f;
            }
        }
    }

    private void UpdateAmbushWaiting()
    {
        agent.speed = stalkSpeed;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.35f)
        {
            stateTimer -= Time.deltaTime;
            if (stateTimer <= 0f)
            {
                SetState(HoundState.Wander, 0f);
                waitTimer = 1.0f;
            }
        }
    }

    private void UpdateChasing()
    {
        agent.isStopped = false;
        agent.speed = chaseSpeed;

        float distToPlayer = Vector3.Distance(transform.position, player.position);
        float distFromPortal = Vector3.Distance(transform.position, portalPosition);

        // Unlimited Pursuit: No boundaries lock once the player is spotted!
        // The hound relentlessly tracks and pursues the player across the Backrooms.

        if (playerInSight)
        {
            agent.SetDestination(player.position);

            // Frequent aggressive barking & snarling while galloping after player
            chaseVocalTimer -= Time.deltaTime;
            if (chaseVocalTimer <= 0f)
            {
                chaseVocalTimer = Random.Range(1.6f, 2.8f);
                PlayAudio(Random.value < 0.80f ? HoundAudio.Bark : HoundAudio.Roar, 0.90f);
            }
        }
        else
        {
            agent.SetDestination(lastKnownPlayerPos);
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.35f)
            {
                // Lost sight and reached last known spot: search around briefly
                SetState(HoundState.InvestigatingSound, 5f);
                PlayAudio(HoundAudio.Growl, 0.60f);
                return;
            }
        }

        if (distToPlayer <= pounceDistance && playerInSight && HasClearLineOfSightToPlayer())
        {
            TriggerPounceAttack();
        }
    }

    private void UpdateInspectingHiding()
    {
        agent.speed = stalkSpeed;
        stateTimer -= Time.deltaTime;

        if (stateTimer <= 0f)
        {
            SetState(HoundState.Wander, 0f);
            waitTimer = 1.5f;
        }
    }

    // =========================================================================
    // Attacks & Dynamic Navigation
    // =========================================================================

    private void StartChase()
    {
        SetState(HoundState.Chasing, 0f);
        agent.isStopped = false;
        agent.speed = chaseSpeed;
        agent.SetDestination(player.position);
        chaseVocalTimer = Random.Range(1.8f, 2.6f);
        PlayAudio(HoundAudio.Roar, 0.95f);
        StartCoroutine(ChaseStartVocalRoutine());
    }

    private IEnumerator ChaseStartVocalRoutine()
    {
        yield return new WaitForSeconds(0.65f);
        if (CurrentState == HoundState.Chasing)
        {
            PlayAudio(HoundAudio.Bark, 0.95f);
        }
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

        Vector3 toPlayer = (player.position - transform.position).normalized;
        toPlayer.y = 0;
        if (toPlayer != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(toPlayer);
        }

        PlayAudio(HoundAudio.Bark, 0.95f); // explosive leaping snarl

        yield return new WaitForSeconds(0.35f);

        float hitDist = Vector3.Distance(transform.position, player.position);
        if (hitDist <= pounceDistance * 1.15f && HasClearLineOfSightToPlayer() && !PlayerLockerController.IsInLocker && !PlayerVentController.IsInVent)
        {
            PlayAudio(HoundAudio.Bite, 1.0f); // lethal crunch / jaw snap
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
        Vector3 hidingPos = player.position;
        SetState(HoundState.InspectingHiding, Random.Range(6f, 9f));

        agent.speed = stalkSpeed;
        agent.SetDestination(hidingPos);
    }

    private bool FindAmbushSpot(Vector3 noiseOrigin, out Vector3 spot)
    {
        spot = Vector3.zero;
        Vector3 dir = (transform.position - noiseOrigin).normalized;
        Vector3 side = Vector3.Cross(dir, Vector3.up);

        for (int i = -1; i <= 1; i += 2)
        {
            Vector3 test = noiseOrigin + side * (i * 3.5f);
            if (NavMesh.SamplePosition(test, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                spot = hit.position;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Picks a dynamic reachable wander waypoint within the exit portal territory,
    /// actively avoiding Bacteria's location so the two monsters patrol separate sectors.
    /// </summary>
    private Vector3 PickWanderDestination()
    {
        if (testPath == null) testPath = new NavMeshPath();

        Vector3 bacteriaPos = Vector3.zero;
        bool hasBacteria = (VillainAI.Instance != null && VillainAI.Instance.gameObject.activeInHierarchy);
        if (hasBacteria) bacteriaPos = VillainAI.Instance.transform.position;

        // If outside territory, immediately path back towards the portal anchor
        float distFromPortal = Vector3.Distance(transform.position, portalPosition);
        if (distFromPortal > territoryRadius && portalPosition != Vector3.zero)
        {
            Vector3 returnTarget = portalPosition + (transform.position - portalPosition).normalized * (territoryRadius * 0.5f);
            if (NavMesh.SamplePosition(returnTarget, out NavMeshHit retHit, 6.0f, NavMesh.AllAreas))
            {
                if (NavMesh.CalculatePath(transform.position, retHit.position, NavMesh.AllAreas, testPath) &&
                    testPath.status == NavMeshPathStatus.PathComplete)
                {
                    return retHit.position;
                }
            }
        }

        // 1. Try corridor sweep angles, scoring candidates by:
        //    a) Staying within territoryRadius of portalPosition
        //    b) Moving AWAY from Bacteria
        Vector3 fwd = transform.forward;
        if (hasBacteria && Vector3.Distance(transform.position, bacteriaPos) < 28f)
        {
            // Bias forward direction away from Bacteria
            Vector3 awayFromBacteria = (transform.position - bacteriaPos).normalized;
            awayFromBacteria.y = 0f;
            if (awayFromBacteria.sqrMagnitude > 0.01f) fwd = awayFromBacteria;
        }

        float[] angles = { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 150f, -150f, 180f };

        Vector3 bestCandidate = Vector3.zero;
        float bestScore = float.MinValue;

        for (int i = 0; i < angles.Length; i++)
        {
            float dist = Random.Range(wanderRadius * 0.6f, wanderRadius);
            Vector3 dir = Quaternion.Euler(0f, angles[i], 0f) * fwd;
            Vector3 candidate = transform.position + dir * dist;

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4.0f, NavMesh.AllAreas))
            {
                // Must not leave portal territory too far
                float candDistPortal = (portalPosition != Vector3.zero) ? Vector3.Distance(hit.position, portalPosition) : 0f;
                if (portalPosition != Vector3.zero && candDistPortal > territoryRadius * 1.25f)
                {
                    continue;
                }

                // Check distance to Bacteria (disqualify points closer than 18m to Bacteria)
                float distToBact = hasBacteria ? Vector3.Distance(hit.position, bacteriaPos) : 999f;
                if (distToBact < 18f) continue;

                if (NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, testPath))
                {
                    if (testPath.status == NavMeshPathStatus.PathComplete && testPath.corners.Length >= 2)
                    {
                        // Higher score for staying well-separated from Bacteria and inside territory
                        float score = distToBact * 1.5f - candDistPortal * 0.5f;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestCandidate = hit.position;
                        }
                    }
                }
            }
        }

        if (bestScore > float.MinValue)
        {
            return bestCandidate;
        }

        // 2. Random reachable point around portal anchor
        for (int i = 0; i < 12; i++)
        {
            Vector2 circle = Random.insideUnitCircle * territoryRadius;
            Vector3 randomTarget = portalPosition + new Vector3(circle.x, 0f, circle.y);
            if (NavMesh.SamplePosition(randomTarget, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
            {
                if (hasBacteria && Vector3.Distance(hit.position, bacteriaPos) < 18f) continue;

                if (NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, testPath))
                {
                    if (testPath.status == NavMeshPathStatus.PathComplete)
                    {
                        return hit.position;
                    }
                }
            }
        }

        return portalPosition != Vector3.zero ? portalPosition : transform.position;
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
