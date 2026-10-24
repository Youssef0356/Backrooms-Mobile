using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class VillainAI : MonoBehaviour
{
    [Header("Target")]
    public Transform player;                 // leave empty to auto-find the object tagged "Player"

    [Header("Speeds")]
    public float wanderSpeed = 1.35f;
    public float investigateSpeed = 2.4f;    // brisk stalking pace when hunting or hearing noise
    public float chaseSpeed = 3.8f;

    [Header("Wandering")]
    public float wanderRadius = 15f;         // how far he picks random points
    public float waitAtPoint = 2f;           // pause at each point (seconds)

    [Header("Vision & Flashlight")]
    public float viewDistance = 12f;
    [Tooltip("Detection range when the player's flashlight is turned on.")]
    public float viewDistanceFlashlight = 24f;
    [Range(10, 180)] public float viewAngle = 100f;
    public float eyeHeight = 1.5f;
    public float loseSightTime = 2.0f;       // seconds after losing sight before switching to hunting the last known spot

    [Header("Hearing System")]
    [Tooltip("Whether the villain can hear player footsteps and noises.")]
    public bool canHear = true;
    [Range(0.5f, 2f)] public float hearingSensitivity = 1.0f;

    [Header("Hunting / Last Known Position")]
    [Tooltip("Seconds he stops and 'listens' / looks around when reaching a noise or last seen spot.")]
    public float searchPauseDuration = 1.8f;
    [Tooltip("Number of branching corridor spots to inspect around the last known position before giving up.")]
    public int searchBranchesToInspect = 2;
    public float searchBranchRadius = 8f;

    [Header("Ambush & Interception (Loops)")]
    [Tooltip("Predicts player movement ahead in corridors instead of trailing behind them.")]
    public bool enableInterception = true;
    public float leadDistance = 3.5f;
    public float interceptCheckInterval = 0.35f;

    [Header("Catch")]
    public float catchDistance = 1.2f;

    [Header("Editable Materials (Clark)")]
    [Tooltip("Drag Clark's external materials here to easily adjust smoothness, roughness, and textures.")]
    public Material headMaterial;
    public Material tunicMaterial;
    public Material pantsMaterial;
    public Material bootMaterial;
    public Material eyeMaterial;
    public Material handMaterial;
    public Material teethMaterial;

    [Header("Turning & Alignment")]
    [Tooltip("Whether to manually control turning so the villain never slides or strafes sideways.")]
    public bool customTurning = true;
    [Tooltip("How fast the villain pivots to face where he is moving while wandering or investigating (deg/sec).")]
    public float turnSpeed = 480f;
    [Tooltip("How fast the villain pivots when chasing the player (deg/sec).")]
    public float chaseTurnSpeed = 750f;
    [Tooltip("Angle threshold (degrees) that triggers an in-place turn in Idle before forward movement begins.")]
    public float turnAngleThreshold = 25f;
    [Tooltip("Angle threshold (degrees) below which the in-place turn finishes and forward movement resumes.")]
    public float turnExitAngleThreshold = 10f;
    [Tooltip("Subtle centrifugal torso roll when cornering at speed (degrees).")]
    public float maxBankAngle = 4.5f;

    [Header("Ground Alignment")]
    [Tooltip("Vertical offset on NavMeshAgent to ground Clark's boots firmly on the carpet without floating.")]
    public float groundOffset = -0.06f;

    [Header("Animation")]
    [Tooltip("Base speed walk clip was authored at (calibrated to 0.16 model scale for step synchronization).")]
    public float walkAnimReferenceSpeed = 0.70f;
    [Tooltip("Base speed run clip was authored at (calibrated to 0.16 model scale for step synchronization).")]
    public float runAnimReferenceSpeed = 1.65f;

    [Header("Attack (optional)")]
    [Tooltip("Starts swinging when the player is closer than this while chasing.")]
    public float attackDistance = 2.6f;
    [Tooltip("Seconds between swings.")]
    public float attackCooldown = 1.5f;

    [Header("Repositioning (keeps him near the player)")]
    public bool enableTeleport = true;
    public float maxDistanceFromPlayer = 40f;   // if he drifts beyond this, he teleports back
    public Vector2 teleportRange = new Vector2(15f, 30f); // distance from player after teleporting
    public float firstTeleportDelay = 6f;       // waits this long after spawn before first reposition
    public float teleportCooldown = 10f;        // min seconds between teleports
    [Range(1, 30)] public int teleportAttempts = 12; // random spots to try per teleport
    public float teleportSampleRadius = 3f;     // how far a spot may snap onto the NavMesh
    public bool avoidVisibleSpots = true;
    public float playerSightHalfAngle = 60f;

    [Header("Chase Audio (Composition SFX)")]
    [Tooltip("Clips played while running after the player. Auto-loaded from Resources/Composition if empty.")]
    public AudioClip[] chaseClips;
    [Tooltip("Volume multiplier for chase audio.")]
    [Range(0f, 1f)] public float chaseVolume = 0.95f;
    [Tooltip("Minimum 3D distance where chase noises are heard at maximum volume (meters).")]
    public float chaseMinDistance = 2.5f;
    [Tooltip("Maximum 3D distance where chase noises attenuate into silence (meters).")]
    public float chaseMaxDistance = 32f;
    [Tooltip("Minimum silent interval between noises while chasing (seconds).")]
    public float chaseMinInterval = 0.8f;
    [Tooltip("Maximum silent interval between noises while chasing (seconds).")]
    public float chaseMaxInterval = 2.8f;
    [Tooltip("Play a terrifying noise immediately upon spotting the player and beginning the chase.")]
    public bool playSoundImmediatelyOnChaseStart = true;
    [Tooltip("Random pitch range applied to each chase noise for organic horror variation.")]
    public Vector2 chasePitchRange = new Vector2(0.92f, 1.08f);

    [Header("Vent Hiding")]
    [Tooltip("Min / max seconds he lingers at the vent the player vanished into before giving up and wandering off.")]
    public Vector2 ventLingerTime = new Vector2(5f, 8f);

    public enum State { Wander, Investigate, Chase, VentWait }
    [Header("Debug Info")]
    public State currentState = State.Wander;
    public State CurrentState => currentState;

    // ---- Vent hiding runtime ----
    bool playerHiddenInVent;
    Vector3 ventFrontPoint;
    Vector3 ventLookPoint;
    bool ventArrived;
    float ventApproachTimer;
    float ventLingerTimer;
    float ventLingerDuration;

    /// <summary>True while the player is crouched inside a wall vent (he can't see, hear or catch the player).</summary>
    public bool PlayerHiddenInVent => playerHiddenInVent;

    NavMeshAgent agent;
    Animator anim;
    Flashlight cachedFlashlight;

    float waitTimer;
    float lostTimer;
    float teleportTimer;
    float attackTimer;
    float interceptTimer;

    Vector3 lastSeenPlayerPos;
    Vector3 investigateTarget;
    Vector3 lastPlayerPos;
    bool isPausedSearching;
    float searchPauseTimer;
    int remainingBranches;
    Vector3 investigateLookForward;

    Transform modelRoot;
    Transform headBone;
    float currentBankAngle;
    float previousYaw;
    bool isPivotingOnSpot;
    float currentForwardSpeed;

    bool hasTeleported;
    bool hasCaught;

    bool hasSpeed, hasChasing, hasAttack1, hasAttack2;
    int speedHash, chasingHash, attack1Hash, attack2Hash;

    AudioSource chaseAudioSource;
    Coroutine chaseAudioRoutine;
    Coroutine chaseFadeRoutine;
    int lastPlayedChaseClipIndex = -1;

    public static VillainAI Instance { get; private set; }
    float reviveGraceTimer = 0f;

    void Awake()
    {
        Instance = this;
        ApplyEditableMaterials();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        StopChaseAudio(fadeOut: false);
    }

    [ContextMenu("Apply Editable Materials")]
    public void ApplyEditableMaterials()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var rend in renderers)
        {
            Material[] mats = rend.sharedMaterials;
            bool modified = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string mName = mats[i].name.Replace(" (Instance)", "").Trim();

                Material replacement = null;
                if (headMaterial != null && mName.Equals("head", System.StringComparison.OrdinalIgnoreCase))
                    replacement = headMaterial;
                else if (tunicMaterial != null && mName.Equals("tunic", System.StringComparison.OrdinalIgnoreCase))
                    replacement = tunicMaterial;
                else if (pantsMaterial != null && mName.Equals("pants", System.StringComparison.OrdinalIgnoreCase))
                    replacement = pantsMaterial;
                else if (bootMaterial != null && mName.Equals("boot", System.StringComparison.OrdinalIgnoreCase))
                    replacement = bootMaterial;
                else if (eyeMaterial != null && (mName.Equals("EyeB", System.StringComparison.OrdinalIgnoreCase) || mName.Contains("eye", System.StringComparison.OrdinalIgnoreCase)))
                    replacement = eyeMaterial;
                else if (handMaterial != null && mName.Equals("hand", System.StringComparison.OrdinalIgnoreCase))
                    replacement = handMaterial;
                else if (teethMaterial != null && mName.Equals("teeth", System.StringComparison.OrdinalIgnoreCase))
                    replacement = teethMaterial;

                if (replacement != null && mats[i] != replacement)
                {
                    mats[i] = replacement;
                    modified = true;
                }
            }
            if (modified)
            {
                rend.sharedMaterials = mats;
            }
        }
    }

    void OnEnable()
    {
        PlayerNoiseEmitter.OnNoiseEmitted += OnNoiseHeard;
    }

    void OnDisable()
    {
        PlayerNoiseEmitter.OnNoiseEmitted -= OnNoiseHeard;
        StopChaseAudio(fadeOut: false);
    }

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        anim = GetComponentInChildren<Animator>();
        if (anim == null) anim = GetComponent<Animator>();

        if (anim != null)
        {
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.applyRootMotion = false;
        }

        if (transform.localScale.x > 0.5f && gameObject.name.ToLowerInvariant().Contains("clark"))
        {
            transform.localScale = new Vector3(0.16f, 0.16f, 0.16f);
        }

        CacheAnimatorParameters();
        SetChasing(false);

        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
            else Debug.LogWarning("VillainAI: no object tagged 'Player' found.");
        }

        if (player != null)
        {
            lastPlayerPos = player.position;
            cachedFlashlight = player.GetComponentInChildren<Flashlight>();

            // Ensure player has a noise emitter attached
            if (player.GetComponent<PlayerNoiseEmitter>() == null)
            {
                player.gameObject.AddComponent<PlayerNoiseEmitter>();
            }
        }

        currentState = State.Wander;
        agent.speed = wanderSpeed;

        if (agent != null)
        {
            agent.radius = 0.4f;
            agent.baseOffset = groundOffset;
            if (customTurning)
            {
                agent.updateRotation = false;
                agent.acceleration = 14f;
            }
        }

        if (transform.childCount > 0)
        {
            modelRoot = transform.GetChild(0);
        }

        FindHeadBone();
        SetupChaseAudio();
        previousYaw = transform.eulerAngles.y;

        if (agent != null && agent.isOnNavMesh)
        {
            GoToRandomPoint();
        }
    }

    public void OnNavMeshReady()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.radius = 0.4f;
            agent.baseOffset = groundOffset;
            if (customTurning)
            {
                agent.updateRotation = false;
                agent.acceleration = 14f;
            }
        }

        if (agent != null && agent.isOnNavMesh)
        {
            currentState = State.Wander;
            agent.speed = wanderSpeed;
            waitTimer = 0f;
            lostTimer = 0f;
            teleportTimer = 0f;
            StopChaseAudio(fadeOut: false);
            GoToRandomPoint();
        }
    }

    void Update()
    {
        if (reviveGraceTimer > 0f)
        {
            reviveGraceTimer -= Time.deltaTime;
        }

        if (hasCaught)
        {
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }
            return;
        }

        if (player == null) return;
        if (agent == null || !agent.isOnNavMesh) return;

        CheckReposition();
        if (customTurning) UpdateRotationAndVelocity();
        SyncAnimationSpeed();

        bool canSee = CanSeePlayer();

        switch (currentState)
        {
            case State.Wander:
                UpdateWander(canSee);
                break;

            case State.Investigate:
                UpdateInvestigate(canSee);
                break;

            case State.Chase:
                UpdateChase(canSee);
                break;

            case State.VentWait:
                UpdateVentWait(canSee);
                break;
        }
    }

    // ---------------- Vent Hiding ----------------

    /// <summary>
    /// Called when the player ducks into a vent. A chasing villain loses the trail, walks to the
    /// grille the player vanished behind, lingers there, then gives up and goes back to wandering.
    /// </summary>
    public void OnPlayerHidInVent(Vector3 frontPoint, Vector3 lookPoint)
    {
        playerHiddenInVent = true;
        ventFrontPoint = frontPoint;
        ventLookPoint = lookPoint;

        if (currentState != State.Chase && currentState != State.Investigate) return;
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

        currentState = State.VentWait;
        ventArrived = false;
        ventApproachTimer = 0f;
        ventLingerTimer = 0f;
        ventLingerDuration = Random.Range(ventLingerTime.x, Mathf.Max(ventLingerTime.x, ventLingerTime.y));

        agent.speed = investigateSpeed;
        agent.isStopped = false;
        SetChasing(false);
        StopChaseAudio(fadeOut: true);

        Vector3 dest = ventFrontPoint;
        if (NavMesh.SamplePosition(ventFrontPoint, out NavMeshHit hit, 2f, NavMesh.AllAreas)) dest = hit.position;
        agent.SetDestination(dest);
    }

    /// <summary>Called as soon as the player starts crawling back out of the vent.</summary>
    public void OnPlayerLeftVent()
    {
        playerHiddenInVent = false;
    }

    void UpdateVentWait(bool canSee)
    {
        // Player came out while he is still standing guard: he spots them again
        if (canSee && !playerHiddenInVent)
        {
            StartChasing();
            return;
        }

        if (!ventArrived)
        {
            ventApproachTimer += Time.deltaTime;
            bool close = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.4f;
            if (close || ventApproachTimer > 12f)
            {
                ventArrived = true;
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
                ventLingerTimer = 0f;
            }
            return;
        }

        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        ventLingerTimer += Time.deltaTime;

        // Stare at the grille, with a slow, unsettling sway as if listening
        Vector3 look = ventLookPoint - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.01f)
        {
            float sway = Mathf.Sin(ventLingerTimer * 1.7f) * 14f;
            Quaternion target = Quaternion.LookRotation(look.normalized) * Quaternion.Euler(0f, sway, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * 4f);
        }

        if (ventLingerTimer >= ventLingerDuration)
        {
            // Gives up: forgets the player and goes back to patrolling
            currentState = State.Wander;
            agent.isStopped = false;
            agent.speed = wanderSpeed;
            waitTimer = 0f;
            lostTimer = 0f;
            SetChasing(false);
            GoToRandomPoint();
        }
    }

    // ---------------- State Handlers ----------------

    void UpdateWander(bool canSee)
    {
        if (canSee)
        {
            StartChasing();
            return;
        }

        // Reached the wander point? Stop, wait, then pick a new one.
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.25f)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;

            waitTimer += Time.deltaTime;
            if (waitTimer >= waitAtPoint)
            {
                waitTimer = 0f;
                agent.isStopped = false;
                GoToRandomPoint();
            }
        }
    }

    void UpdateInvestigate(bool canSee)
    {
        if (canSee)
        {
            StartChasing();
            return;
        }

        if (isPausedSearching)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;

            searchPauseTimer += Time.deltaTime;

            // Intelligent corridor scanning: look left, then right
            float scanProgress = searchPauseTimer / searchPauseDuration;
            float scanYaw = 0f;
            if (scanProgress > 0.18f && scanProgress < 0.50f)
            {
                float p = Mathf.Sin((scanProgress - 0.18f) / 0.32f * Mathf.PI);
                scanYaw = -38f * p;
            }
            else if (scanProgress >= 0.50f && scanProgress < 0.84f)
            {
                float p = Mathf.Sin((scanProgress - 0.50f) / 0.34f * Mathf.PI);
                scanYaw = 38f * p;
            }

            if (investigateLookForward.sqrMagnitude > 0.01f)
            {
                Quaternion baseLook = Quaternion.LookRotation(investigateLookForward);
                transform.rotation = Quaternion.Slerp(transform.rotation, baseLook * Quaternion.Euler(0f, scanYaw, 0f), Time.deltaTime * 5f);
            }

            if (searchPauseTimer >= searchPauseDuration)
            {
                searchPauseTimer = 0f;
                isPausedSearching = false;
                agent.isStopped = false;

                if (remainingBranches > 0)
                {
                    remainingBranches--;
                    // Search another nearby branching hallway
                    investigateTarget = GetNearbySearchSpot(investigateTarget, searchBranchRadius);
                    agent.SetDestination(investigateTarget);
                }
                else
                {
                    // Thoroughly searched, return to wander
                    currentState = State.Wander;
                    agent.speed = wanderSpeed;
                    SetChasing(false);
                    GoToRandomPoint();
                }
            }
            return;
        }

        // Moving towards investigate point
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.4f)
        {
            // Reached spot: pause and listen
            isPausedSearching = true;
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            searchPauseTimer = 0f;
            investigateLookForward = transform.forward;
        }
    }

    void UpdateChase(bool canSee)
    {
        if (canSee)
        {
            lostTimer = 0f;
            lastSeenPlayerPos = player.position;

            // Ambush / Interception on loops
            interceptTimer += Time.deltaTime;
            if (enableInterception && interceptTimer >= interceptCheckInterval)
            {
                interceptTimer = 0f;
                Vector3 playerVel = (player.position - lastPlayerPos) / Mathf.Max(Time.deltaTime, 0.001f);
                lastPlayerPos = player.position;

                if (playerVel.sqrMagnitude > 4f)
                {
                    Vector3 leadPoint = player.position + playerVel.normalized * leadDistance;
                    if (NavMesh.SamplePosition(leadPoint, out NavMeshHit leadHit, 2.5f, NavMesh.AllAreas))
                    {
                        agent.SetDestination(leadHit.position);
                    }
                    else
                    {
                        agent.SetDestination(player.position);
                    }
                }
                else
                {
                    agent.SetDestination(player.position);
                }
            }
            else if (!enableInterception)
            {
                agent.SetDestination(player.position);
            }
        }
        else
        {
            lostTimer += Time.deltaTime;
            lastPlayerPos = player.position;

            // Lost sight: continue rushing to where he was last seen
            agent.SetDestination(lastSeenPlayerPos);

            if (lostTimer >= loseSightTime)
            {
                // Switch to Hunt / Investigate mode at the last known position
                currentState = State.Investigate;
                agent.speed = investigateSpeed;
                investigateTarget = lastSeenPlayerPos;
                agent.SetDestination(investigateTarget);
                remainingBranches = searchBranchesToInspect;
                isPausedSearching = false;
                SetChasing(false);
                StopChaseAudio(fadeOut: true);
                return;
            }
        }

        // Check catch & attack
        if (reviveGraceTimer <= 0f && Vector3.Distance(transform.position, player.position) <= catchDistance)
        {
            OnCaughtPlayer();
        }
        else
        {
            hasCaught = false;
            if (reviveGraceTimer <= 0f) UpdateAttack();
        }
    }

    void StartChasing()
    {
        currentState = State.Chase;
        agent.speed = chaseSpeed;
        if (agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = false;
        SetChasing(true);
        lostTimer = 0f;
        attackTimer = 0f;
        interceptTimer = 0f;
        lastSeenPlayerPos = player.position;
        lastPlayerPos = player.position;
        agent.SetDestination(player.position);

        StartChaseAudio();
    }

    // ---------------- Hearing System ----------------

    void OnNoiseHeard(Vector3 noiseOrigin, float radius)
    {
        if (!canHear || player == null) return;
        if (playerHiddenInVent) return;
        if (agent == null || !agent.isOnNavMesh) return;

        float dist = Vector3.Distance(transform.position, noiseOrigin);
        float effectiveRadius = radius * hearingSensitivity;

        if (dist <= effectiveRadius)
        {
            // If already chasing, he's actively locked on
            if (currentState == State.Chase)
            {
                lastSeenPlayerPos = noiseOrigin;
                return;
            }

            // Alerted! Investigate sound location
            currentState = State.Investigate;
            agent.speed = investigateSpeed;
            investigateTarget = noiseOrigin;
            agent.SetDestination(investigateTarget);
            remainingBranches = searchBranchesToInspect;
            isPausedSearching = false;
            SetChasing(false);
        }
    }

    // ---------------- Vision & Flashlight ----------------

    bool CanSeePlayer()
    {
        if (player == null || reviveGraceTimer > 0f || playerHiddenInVent) return false;

        if (cachedFlashlight == null)
            cachedFlashlight = player.GetComponentInChildren<Flashlight>();

        bool isLightOn = cachedFlashlight != null && cachedFlashlight.IsOn;
        float currentDistance = isLightOn ? viewDistanceFlashlight : viewDistance;
        float currentAngle = isLightOn ? Mathf.Min(150f, viewAngle * 1.3f) : viewAngle;

        Vector3 eyes = transform.position + Vector3.up * eyeHeight;
        Vector3 target = player.position + Vector3.up * 1f;
        Vector3 dir = target - eyes;

        if (dir.magnitude > currentDistance) return false;
        if (Vector3.Angle(transform.forward, dir) > currentAngle * 0.5f) return false;

        // Line-of-sight raycast
        if (Physics.Raycast(eyes, dir.normalized, out RaycastHit hit, currentDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.transform == player || hit.transform.IsChildOf(player) || player.IsChildOf(hit.transform);
        }
        return false;
    }

    Vector3 GetNearbySearchSpot(Vector3 origin, float radius)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 randomPoint = origin + Random.insideUnitSphere * radius;
            if (NavMesh.SamplePosition(randomPoint, out NavMeshHit navHit, radius, NavMesh.AllAreas))
            {
                return navHit.position;
            }
        }
        return origin;
    }

    void GoToRandomPoint()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        // Forward-biased patrol: look ahead down corridors (prevents pacing backward and forward aimlessly)
        for (int i = 0; i < 5; i++)
        {
            Vector3 forwardBias = transform.forward * wanderRadius * Random.Range(0.6f, 1.0f);
            Vector3 lateralBias = transform.right * Random.Range(-wanderRadius * 0.35f, wanderRadius * 0.35f);
            Vector3 target = transform.position + forwardBias + lateralBias;

            if (NavMesh.SamplePosition(target, out NavMeshHit hit, wanderRadius * 0.5f, NavMesh.AllAreas))
            {
                if (Vector3.Dot(transform.forward, (hit.position - transform.position).normalized) > 0.2f)
                {
                    agent.SetDestination(hit.position);
                    return;
                }
            }
        }

        // Fallback if at a corner/dead-end
        Vector3 random = transform.position + Random.insideUnitSphere * wanderRadius;
        if (NavMesh.SamplePosition(random, out NavMeshHit navHit, wanderRadius, NavMesh.AllAreas))
        {
            agent.SetDestination(navHit.position);
        }
    }

    // ---------------- Animation ----------------

    void CacheAnimatorParameters()
    {
        hasSpeed = hasChasing = hasAttack1 = hasAttack2 = false;
        if (anim == null || anim.runtimeAnimatorController == null) return;

        foreach (AnimatorControllerParameter p in anim.parameters)
        {
            switch (p.name)
            {
                case "Speed":     hasSpeed = true;     speedHash = p.nameHash;     break;
                case "IsChasing": hasChasing = true;   chasingHash = p.nameHash;   break;
                case "Attack1":   hasAttack1 = true;   attack1Hash = p.nameHash;   break;
                case "Attack2":   hasAttack2 = true;   attack2Hash = p.nameHash;   break;
            }
        }
    }

    void SetChasing(bool value)
    {
        if (anim != null && hasChasing) anim.SetBool(chasingHash, value);
    }

    float GetBaseStateSpeed()
    {
        switch (currentState)
        {
            case State.Chase: return chaseSpeed;
            case State.Investigate: return investigateSpeed;
            case State.VentWait: return investigateSpeed;
            default: return wanderSpeed;
        }
    }

    void UpdateRotationAndVelocity()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        float baseSpeed = GetBaseStateSpeed();
        float currentTurnSpeed = (currentState == State.Chase) ? chaseTurnSpeed : turnSpeed;

        // Check if there is an active destination along the path
        bool hasDestination = agent.hasPath && !agent.isStopped && agent.remainingDistance > (agent.stoppingDistance + 0.15f);

        Vector3 targetDir = Vector3.zero;

        if (hasDestination)
        {
            targetDir = agent.steeringTarget - transform.position;
            targetDir.y = 0f;
            if (targetDir.sqrMagnitude < 0.04f && agent.desiredVelocity.sqrMagnitude > 0.04f)
            {
                targetDir = agent.desiredVelocity;
                targetDir.y = 0f;
            }
        }
        else if (currentState == State.Chase && player != null)
        {
            targetDir = player.position - transform.position;
            targetDir.y = 0f;
        }

        if (targetDir.sqrMagnitude > 0.04f)
        {
            targetDir.Normalize();
            float angleDelta = Vector3.Angle(transform.forward, targetDir);

            // Trigger an in-place turn if heading deviates significantly from target direction
            if (!isPivotingOnSpot && angleDelta > turnAngleThreshold)
            {
                isPivotingOnSpot = true;
            }
            // Finish in-place turn once aligned within tolerance
            else if (isPivotingOnSpot && angleDelta <= turnExitAngleThreshold)
            {
                isPivotingOnSpot = false;
            }

            // Pivot towards the target direction
            Quaternion targetRot = Quaternion.LookRotation(targetDir);
            float turnBoost = isPivotingOnSpot ? 1.35f : 1.0f;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, currentTurnSpeed * turnBoost * Time.deltaTime);

            if (isPivotingOnSpot || !hasDestination)
            {
                // In-place turn: halt forward translation completely so animator plays IDLE
                currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, 0f, 18f * Time.deltaTime);
                agent.velocity = Vector3.zero;
            }
            else
            {
                // Smoothly accelerate forward along facing direction (ZERO lateral slide!)
                float alignment = Mathf.Clamp01((Vector3.Dot(transform.forward, targetDir) - 0.7f) / 0.3f);
                float desiredSpeed = Mathf.Lerp(baseSpeed * 0.5f, baseSpeed, alignment);
                currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, desiredSpeed, 12f * Time.deltaTime);

                // Movement is STRICTLY in transform.forward: never strafes or slides sideways!
                agent.velocity = transform.forward * currentForwardSpeed;
            }

            // Corner Banking (Subtle physical roll tilt into sharp turns)
            float currentYaw = transform.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(previousYaw, currentYaw);
            previousYaw = currentYaw;

            float targetBank = Mathf.Clamp(-yawDelta * 1.5f, -maxBankAngle, maxBankAngle);
            currentBankAngle = Mathf.Lerp(currentBankAngle, targetBank, Time.deltaTime * 8f);

            if (modelRoot != null)
            {
                modelRoot.localRotation = Quaternion.Euler(0f, 0f, currentBankAngle);
            }
        }
        else
        {
            isPivotingOnSpot = false;
            currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, 0f, 18f * Time.deltaTime);
            agent.velocity = Vector3.zero;

            previousYaw = transform.eulerAngles.y;
            currentBankAngle = Mathf.Lerp(currentBankAngle, 0f, Time.deltaTime * 6f);
            if (modelRoot != null)
            {
                modelRoot.localRotation = Quaternion.Euler(0f, 0f, currentBankAngle);
            }
        }
    }

    void SyncAnimationSpeed()
    {
        if (anim == null) return;

        if (hasSpeed)
        {
            // If pivoting in place or stopped, feed 0 so the animator plays the IDLE clip cleanly
            float targetDisplaySpeed = isPivotingOnSpot ? 0f : currentForwardSpeed;
            anim.SetFloat(speedHash, targetDisplaySpeed, 0.12f, Time.deltaTime);
        }

        // When standing still or pivoting in place, normal 1.0x playback for Idle / Attack clips
        if (currentForwardSpeed < 0.10f || isPivotingOnSpot)
        {
            anim.speed = 1f;
            return;
        }

        float reference = walkAnimReferenceSpeed;
        if (currentState == State.Chase || currentForwardSpeed > 2.2f)
        {
            reference = runAnimReferenceSpeed;
        }

        if (reference < 0.1f) reference = 0.1f;

        // Directly link playback rate to real forward ground speed so footsteps never slide
        anim.speed = Mathf.Clamp(currentForwardSpeed / reference, 0.4f, 3.0f);
    }

    void LateUpdate()
    {
        if (headBone == null || player == null) return;

        if (hasCaught)
        {
            // Lock eyes straight onto the camera as the monster executes the attack
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                Vector3 toCam = mainCam.transform.position - headBone.position;
                if (toCam.sqrMagnitude > 0.01f)
                {
                    Quaternion lookRot = Quaternion.LookRotation(toCam, Vector3.up);
                    headBone.rotation = Quaternion.Slerp(headBone.rotation, lookRot, Time.deltaTime * 12f);
                }
            }
            return;
        }

        if (currentState == State.Wander && !CanSeePlayer()) return;
        if (playerHiddenInVent) return;   // he must not "know" where the hidden player is

        // When hunting, investigating, or chasing, head tracks the player
        Vector3 targetPoint = player.position + Vector3.up * 1.4f;
        Vector3 toTarget = targetPoint - headBone.position;

        if (Vector3.Angle(transform.forward, toTarget) < 65f)
        {
            Quaternion lookRot = Quaternion.LookRotation(toTarget, Vector3.up);
            headBone.rotation = Quaternion.Slerp(headBone.rotation, lookRot, Time.deltaTime * 6f);
        }
    }

    void FindHeadBone()
    {
        headBone = FindChildRecursive(transform, "head", "neck", "spine_03", "spine.003", "spine_02");
    }

    Transform FindChildRecursive(Transform parent, params string[] names)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            string lowerName = child.name.ToLowerInvariant();
            foreach (string n in names)
            {
                if (lowerName.Contains(n)) return child;
            }
            Transform found = FindChildRecursive(child, names);
            if (found != null) return found;
        }
        return null;
    }

    void UpdateAttack()
    {
        if (anim == null || (!hasAttack1 && !hasAttack2)) return;

        attackTimer -= Time.deltaTime;
        if (attackTimer > 0f) return;

        if (Vector3.Distance(transform.position, player.position) > attackDistance) return;

        attackTimer = attackCooldown + Random.Range(0f, 0.35f);

        if (hasAttack2 && (!hasAttack1 || Random.value < 0.5f)) anim.SetTrigger(attack2Hash);
        else anim.SetTrigger(attack1Hash);
    }

    // ---------------- Catch & Teleport ----------------

    void OnCaughtPlayer()
    {
        if (hasCaught || playerHiddenInVent || PlayerVentController.IsInVent) return;
        hasCaught = true;
        StopChaseAudio(fadeOut: false);

        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            Vector3 toCam = mainCam.transform.position - transform.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(toCam);
            }
        }

        if (anim != null)
        {
            anim.speed = 1f;
            if (hasAttack1) anim.ResetTrigger(attack1Hash);
            if (hasAttack2) anim.ResetTrigger(attack2Hash);
            anim.Play("Attack 1", 0, 0f);
        }

        GameOverController.TriggerDefeat(transform);
    }

    /// <summary>
    /// Resets the entity when the player watches a rewarded revive ad.
    /// Clears caught state, teleports the monster away to an unseen corridor,
    /// and grants a grace period where the monster cannot detect or catch the player.
    /// </summary>
    public void ResetAfterRevive(float graceSeconds = 6f)
    {
        hasCaught = false;
        reviveGraceTimer = graceSeconds;
        currentState = State.Wander;
        SetChasing(false);
        StopChaseAudio(fadeOut: false);

        if (anim != null)
        {
            if (hasAttack1) anim.ResetTrigger(attack1Hash);
            if (hasAttack2) anim.ResetTrigger(attack2Hash);
            anim.Play("Walk", 0, 0f);
        }

        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.speed = wanderSpeed;
        }

        TeleportNearPlayer();
    }

    void CheckReposition()
    {
        if (!enableTeleport) return;

        teleportTimer += Time.deltaTime;

        float threshold = hasTeleported ? teleportCooldown : firstTeleportDelay;
        if (teleportTimer < threshold) return;

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist <= maxDistanceFromPlayer) return;

        TeleportNearPlayer();
    }

    void TeleportNearPlayer()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        if (!TryFindTeleportSpot(out Vector3 chosen))
        {
            teleportTimer = 0f;
            return;
        }

        agent.Warp(chosen);
        currentState = State.Wander;
        SetChasing(false);
        StopChaseAudio(fadeOut: false);
        agent.speed = wanderSpeed;
        waitTimer = 0f;
        lostTimer = 0f;
        teleportTimer = 0f;
        hasTeleported = true;
        GoToRandomPoint();
    }

    bool TryFindTeleportSpot(out Vector3 spot)
    {
        spot = Vector3.zero;
        Vector3 playerPos = player.position;
        float minDist = teleportRange.x * 0.6f;

        for (int i = 0; i < teleportAttempts; i++)
        {
            float dist = Random.Range(teleportRange.x, teleportRange.y);
            Vector2 dir = Random.insideUnitCircle.normalized;
            Vector3 candidate = playerPos + new Vector3(dir.x, 0f, dir.y) * dist;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, teleportSampleRadius, NavMesh.AllAreas))
                continue;

            Vector3 pos = navHit.position;
            float flatDist = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(playerPos.x, playerPos.z));
            if (flatDist < minDist) continue;

            if (avoidVisibleSpots && IsSpotVisibleToPlayer(pos)) continue;

            spot = pos;
            return true;
        }

        return false;
    }

    bool IsSpotVisibleToPlayer(Vector3 spot)
    {
        Vector3 eyes = player.position + Vector3.up * eyeHeight;
        Vector3 target = spot + Vector3.up * 1.1f;
        Vector3 dir = target - eyes;

        if (Vector3.Angle(player.forward, dir) > playerSightHalfAngle) return false;

        if (Physics.Raycast(eyes, dir.normalized, out _, dir.magnitude, ~0, QueryTriggerInteraction.Ignore))
            return false;

        return true;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, viewDistance);

        if (currentState == State.Investigate)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(investigateTarget, 1.5f);
            Gizmos.DrawLine(transform.position, investigateTarget);
        }
        else if (currentState == State.Chase)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(lastSeenPlayerPos, 1.2f);
        }
    }

    // ---------------- Chase Audio (Composition SFX) ----------------

    void SetupChaseAudio()
    {
        LoadChaseClipsIfEmpty();

        Transform attachTarget = headBone != null ? headBone : transform;
        Transform existing = attachTarget.Find("VillainChaseAudio");
        GameObject audioObj;
        if (existing != null)
        {
            audioObj = existing.gameObject;
        }
        else
        {
            audioObj = new GameObject("VillainChaseAudio");
            audioObj.transform.SetParent(attachTarget, false);
            audioObj.transform.localPosition = Vector3.zero;
        }

        chaseAudioSource = audioObj.GetComponent<AudioSource>();
        if (chaseAudioSource == null)
        {
            chaseAudioSource = audioObj.AddComponent<AudioSource>();
        }

        chaseAudioSource.playOnAwake = false;
        chaseAudioSource.loop = false;
        chaseAudioSource.spatialBlend = 1.0f; // 100% 3D spatial audio
        chaseAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        chaseAudioSource.minDistance = chaseMinDistance;
        chaseAudioSource.maxDistance = chaseMaxDistance;
        chaseAudioSource.spread = 60f;
        chaseAudioSource.dopplerLevel = 0f;
        chaseAudioSource.priority = 64; // High priority so it is never dropped by the audio engine
        chaseAudioSource.volume = chaseVolume * GameSettings.MasterVolume;
    }

    void LoadChaseClipsIfEmpty()
    {
        if (chaseClips == null || chaseClips.Length == 0)
        {
            chaseClips = Resources.LoadAll<AudioClip>("Composition");
            if (chaseClips == null || chaseClips.Length == 0)
            {
                if (MonsterCompositionAudio.Instance != null && MonsterCompositionAudio.Instance.compositionClips != null)
                {
                    chaseClips = MonsterCompositionAudio.Instance.compositionClips;
                }
                else
                {
                    chaseClips = Resources.LoadAll<AudioClip>("SFX");
                }
            }
        }
    }

    AudioClip PickNextChaseClip()
    {
        if (chaseClips == null || chaseClips.Length == 0)
        {
            LoadChaseClipsIfEmpty();
        }

        if (chaseClips == null || chaseClips.Length == 0) return null;
        if (chaseClips.Length == 1) return chaseClips[0];

        int index = Random.Range(0, chaseClips.Length);
        if (index == lastPlayedChaseClipIndex)
        {
            index = (index + 1 + Random.Range(0, chaseClips.Length - 1)) % chaseClips.Length;
        }
        lastPlayedChaseClipIndex = index;
        return chaseClips[index];
    }

    void StartChaseAudio()
    {
        if (chaseFadeRoutine != null)
        {
            StopCoroutine(chaseFadeRoutine);
            chaseFadeRoutine = null;
        }

        if (chaseAudioRoutine != null) return;
        chaseAudioRoutine = StartCoroutine(ChaseAudioLoop());
    }

    void StopChaseAudio(bool fadeOut = true)
    {
        if (chaseAudioRoutine != null)
        {
            StopCoroutine(chaseAudioRoutine);
            chaseAudioRoutine = null;
        }

        if (chaseAudioSource != null && chaseAudioSource.isPlaying)
        {
            if (fadeOut && gameObject.activeInHierarchy)
            {
                if (chaseFadeRoutine != null) StopCoroutine(chaseFadeRoutine);
                chaseFadeRoutine = StartCoroutine(FadeOutChaseAudio(0.45f));
            }
            else
            {
                chaseAudioSource.Stop();
            }
        }
    }

    System.Collections.IEnumerator FadeOutChaseAudio(float duration)
    {
        if (chaseAudioSource == null) yield break;
        float startVol = chaseAudioSource.volume;
        float timer = 0f;
        while (timer < duration && chaseAudioSource != null && chaseAudioSource.isPlaying)
        {
            timer += Time.deltaTime;
            chaseAudioSource.volume = Mathf.Lerp(startVol, 0f, timer / duration);
            yield return null;
        }
        if (chaseAudioSource != null)
        {
            chaseAudioSource.Stop();
            chaseAudioSource.volume = startVol;
        }
        chaseFadeRoutine = null;
    }

    System.Collections.IEnumerator ChaseAudioLoop()
    {
        if (playSoundImmediatelyOnChaseStart)
        {
            PlayChaseNoise();
            if (chaseAudioSource != null && chaseAudioSource.clip != null)
            {
                float clipDuration = chaseAudioSource.clip.length / Mathf.Max(0.1f, chaseAudioSource.pitch);
                yield return new WaitForSeconds(clipDuration);
            }
        }

        while (currentState == State.Chase && !hasCaught)
        {
            float gap = Random.Range(chaseMinInterval, chaseMaxInterval);
            yield return new WaitForSeconds(gap);

            if (currentState != State.Chase || hasCaught) break;

            PlayChaseNoise();

            if (chaseAudioSource != null && chaseAudioSource.clip != null)
            {
                float clipDuration = chaseAudioSource.clip.length / Mathf.Max(0.1f, chaseAudioSource.pitch);
                yield return new WaitForSeconds(clipDuration);
            }
        }

        chaseAudioRoutine = null;
    }

    void PlayChaseNoise()
    {
        if (chaseAudioSource == null)
        {
            SetupChaseAudio();
            if (chaseAudioSource == null) return;
        }

        AudioClip clip = PickNextChaseClip();
        if (clip == null) return;

        float pitch = Random.Range(chasePitchRange.x, chasePitchRange.y);
        chaseAudioSource.pitch = pitch;
        chaseAudioSource.volume = chaseVolume * GameSettings.MasterVolume;
        chaseAudioSource.clip = clip;
        chaseAudioSource.Play();
    }
}
