using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using StarterAssets;

/// <summary>
/// AI Controller for Agatha, the terrifying nocturnal stalker entity of Backrooms Level 94:
/// - Only active and spawns at night near suburban houses and the player.
/// - Surface-snapped feet alignment to rolling hills so she never sinks or overlaps with the ground.
/// - Wide peripheral vision (140 degrees FOV, up to 65m detection with torchlight).
/// - Unsettling stalking locomotion through foggy nocturnal streets.
/// - Flashlight flicker dread effect when Agatha stalks within 26m of the player.
/// - Randomly approaches house doors at night and slowly creaks them open.
/// - If the player is spotted and retreats inside a house, Agatha pursues, forces open the closed door,
///   and bursts inside to hunt them down.
/// - Lethal catch triggers jumpscare animation, scream, and GameOver screen.
/// - Seamless despawning when daytime returns.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
public class AgathaAI : MonoBehaviour
{
    public static AgathaAI Instance { get; private set; }

    public enum AgathaState
    {
        Inactive,            // Daytime: despawned
        StalkingNeighborhood,// Night: prowling between houses and streetlights
        InvestigatingDoor,   // Night: approaching a closed house door to randomly open it
        ChasingPlayer,       // Player spotted: high-speed pursuit
        BreachingHouse,      // Player fled inside: rushing to door to force it open
        JumpscareAttack      // Lethal close-quarters attack
    }

    [Header("Speeds")]
    public float stalkSpeed = 1.75f;
    public float investigateSpeed = 2.6f;
    public float chaseSpeed = 4.8f;

    [Header("Ground Snapping & Terrain Alignment")]
    [Tooltip("Small clearance so Agatha's shoes rest perfectly on top of turf/grass without clipping.")]
    public float groundOffset = 0.05f;
    [Tooltip("Height above target position to cast ray downwards to find terrain surface.")]
    public float terrainRaycastHeight = 60.0f;

    [Header("Vision Cone & Senses")]
    [Tooltip("Detection distance in daytime/ambient darkness.")]
    public float viewDistance = 42.0f;
    [Tooltip("Detection distance when player's flashlight / torchlight is turned ON.")]
    public float viewDistanceFlashlight = 65.0f;
    [Range(60f, 180f)]
    public float viewAngle = 140.0f; // Wide peripheral field of view
    public float eyeHeight = 1.65f;
    public float hearingRadius = 28.0f;
    public float catchDistance = 1.35f;

    [Header("Door Interaction at Night")]
    [Tooltip("How often Agatha randomly targets a closed house door to open it while stalking (seconds).")]
    public float doorOpenInterval = 20.0f;
    [Tooltip("Maximum distance to look for a closed door to investigate.")]
    public float maxDoorSearchRadius = 32.0f;

    [Header("Audio")]
    [Range(0f, 1f)] public float audioVolume = 0.90f;
    public AudioClip killScreamAudio;
    public AudioClip ambientWhisperClip;

    [Header("Current State")]
    public AgathaState currentState = AgathaState.Inactive;

    private NavMeshAgent agent;
    private Animator animator;
    private AudioSource audioSource;
    private Transform player;
    private Flashlight playerFlashlight;

    private float doorTimer = 0f;
    private BackroomsDoor targetedDoor;
    private Vector3 lastKnownPlayerPos;
    private float lostSightTimer = 0f;
    private bool hasVisualContact = false;
    private Coroutine currentBehaviorRoutine;
    private Coroutine flashlightFlickerRoutine;
    private Vector3 currentFallbackTarget;

    // Animator hashes
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsChasingHash = Animator.StringToHash("IsChasing");
    private static readonly int JumpscareHash = Animator.StringToHash("Jumpscare");

    private void Awake()
    {
        Instance = this;
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();

        if (agent != null)
        {
            agent.baseOffset = groundOffset;
            agent.radius = 0.40f;
            agent.height = 1.85f;
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1.0f;
        audioSource.minDistance = 3.0f;
        audioSource.maxDistance = 35.0f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;

        if (killScreamAudio == null)
        {
            killScreamAudio = Resources.Load<AudioClip>("SFX/Hound/five-nights-at-freddys-2-full-scream-sound");
            if (killScreamAudio == null)
                killScreamAudio = Resources.Load<AudioClip>("Composition/five-nights-at-freddys-2-full-scream-sound");
            if (killScreamAudio == null)
                killScreamAudio = Resources.Load<AudioClip>("SFX/nightmare-jumpscare");
        }
    }

    private void OnEnable()
    {
        Level94DayNightCycle.OnNightStarted += HandleNightStarted;
        Level94DayNightCycle.OnNightEnded += HandleNightEnded;
        PlayerNoiseEmitter.OnNoiseEmitted += HandleNoiseHeard;
    }

    private void OnDisable()
    {
        Level94DayNightCycle.OnNightStarted -= HandleNightStarted;
        Level94DayNightCycle.OnNightEnded -= HandleNightEnded;
        PlayerNoiseEmitter.OnNoiseEmitted -= HandleNoiseHeard;
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        LocatePlayer();
        doorTimer = Random.Range(10f, doorOpenInterval);

        // Snap initial placement to ground surface
        Vector3 groundedPos = SnapToTerrainSurface(transform.position);
        transform.position = groundedPos;

        if (Level94DayNightCycle.IsNightActive)
        {
            SpawnForNight();
        }
        else
        {
            DespawnForDay();
        }
    }

    private void LocatePlayer()
    {
        if (player != null && playerFlashlight != null) return;

        GameObject p = GameObject.FindWithTag("Player") ?? GameObject.Find("PlayerCapsule");
        if (p != null)
        {
            player = p.transform;
            playerFlashlight = p.GetComponentInChildren<Flashlight>();
        }
    }

    // =========================================================================
    // Terrain Surface Snapping (Solves Ground Overlap Completely)
    // =========================================================================

    public Vector3 SnapToTerrainSurface(Vector3 targetPos)
    {
        Vector3 rayStart = new Vector3(targetPos.x, targetPos.y + terrainRaycastHeight, targetPos.z);
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, terrainRaycastHeight * 2.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * groundOffset;
        }
        return targetPos;
    }

    private void KeepGrounded()
    {
        Vector3 rayStart = transform.position + Vector3.up * 2.5f;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 6.0f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.baseOffset = groundOffset;
            }
            else
            {
                Vector3 p = transform.position;
                p.y = Mathf.Lerp(p.y, hit.point.y + groundOffset, Time.deltaTime * 20f);
                transform.position = p;
            }
        }
    }

    // =========================================================================
    // Night Spawning & Despawning
    // =========================================================================

    private void HandleNightStarted()
    {
        SpawnForNight();
    }

    private void HandleNightEnded()
    {
        DespawnForDay();
    }

    public void SpawnForNight()
    {
        LocatePlayer();
        if (player == null) return;

        Vector3 spawnPos = FindNightSpawnPoint();

        // Warp agent or place transform on terrain
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.Warp(spawnPos);
            agent.speed = stalkSpeed;
            agent.isStopped = false;
        }
        else
        {
            transform.position = spawnPos;
        }

        // Enable visibility & collision
        SetVisibility(true);
        currentState = AgathaState.StalkingNeighborhood;
        doorTimer = Random.Range(12f, doorOpenInterval);

        PlayStalkerWhisper();
        Debug.Log($"<color=#FF2244><b>[Agatha]</b></color> Agatha has risen at ({spawnPos.x:F1}, {spawnPos.y:F1}, {spawnPos.z:F1}) firmly on the terrain surface!");
    }

    public void DespawnForDay()
    {
        currentState = AgathaState.Inactive;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
        }

        SetVisibility(false);
        Debug.Log("<color=#88FF88><b>[Agatha]</b></color> Daytime has broken: Agatha retreated into the Backrooms shadows.");
    }

    private void SetVisibility(bool visible)
    {
        Renderer[] rends = GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends) r.enabled = visible;

        Collider[] cols = GetComponentsInChildren<Collider>(true);
        foreach (var c in cols) c.enabled = visible;
    }

    private Vector3 FindNightSpawnPoint()
    {
        if (player == null) return SnapToTerrainSurface(transform.position);

        for (int i = 0; i < 20; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float dist = Random.Range(24f, 38f);
            Vector3 candidate = player.position + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);

            // Sample ground terrain height
            Vector3 groundCandidate = SnapToTerrainSurface(candidate);

            // If NavMesh exists, ensure it is on NavMesh
            if (NavMesh.SamplePosition(groundCandidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                return hit.position + Vector3.up * groundOffset;
            }

            if (groundCandidate.y > -20f)
            {
                return groundCandidate;
            }
        }

        return SnapToTerrainSurface(player.position - player.forward * 22f);
    }

    private void PlayStalkerWhisper()
    {
        if (ambientWhisperClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(ambientWhisperClip, 0.75f * audioVolume * GameSettings.MasterVolume);
        }
    }

    // =========================================================================
    // AI Loop & States
    // =========================================================================

    private void Update()
    {
        if (currentState == AgathaState.Inactive || currentState == AgathaState.JumpscareAttack) return;

        KeepGrounded();

        if (player == null)
        {
            LocatePlayer();
            if (player == null) return;
        }

        // 1. Vision & Flashlight Detection
        bool canSeePlayer = CheckSightToPlayer();

        if (canSeePlayer)
        {
            hasVisualContact = true;
            lostSightTimer = 0f;
            lastKnownPlayerPos = player.position;

            if (currentState != AgathaState.ChasingPlayer && currentState != AgathaState.BreachingHouse)
            {
                StartChase();
            }
        }
        else if (hasVisualContact)
        {
            lostSightTimer += Time.deltaTime;
            if (lostSightTimer > 4.5f)
            {
                hasVisualContact = false;
            }
        }

        // 2. Flashlight Flicker Dread Effect
        float distToPlayer = Vector3.Distance(transform.position, player.position);
        if (distToPlayer < 26.0f && playerFlashlight != null && playerFlashlight.IsOn)
        {
            if (flashlightFlickerRoutine == null && Random.value < 0.015f)
            {
                flashlightFlickerRoutine = StartCoroutine(FlashlightFlickerRoutine());
            }
        }

        // 3. State Machine Execution
        switch (currentState)
        {
            case AgathaState.StalkingNeighborhood:
                UpdateStalking();
                break;

            case AgathaState.InvestigatingDoor:
                UpdateDoorInvestigation();
                break;

            case AgathaState.ChasingPlayer:
                UpdateChase();
                break;

            case AgathaState.BreachingHouse:
                UpdateBreach();
                break;
        }

        // 4. Drive Animator
        UpdateAnimation();
    }

    private bool CheckSightToPlayer()
    {
        if (player == null) return false;

        Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
        Vector3 playerChest = player.position + Vector3.up * 1.0f;
        Vector3 toPlayer = playerChest - eyePos;
        float dist = toPlayer.magnitude;

        bool flashlightOn = playerFlashlight != null && playerFlashlight.IsOn;
        float maxDist = flashlightOn ? viewDistanceFlashlight : viewDistance;

        if (dist > maxDist) return false;

        float angle = Vector3.Angle(transform.forward, toPlayer.normalized);
        if (angle > viewAngle * 0.5f) return false;

        if (Physics.Raycast(eyePos, toPlayer.normalized, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform == player || hit.transform.IsChildOf(player) || player.IsChildOf(hit.transform))
            {
                return true;
            }
        }

        return false;
    }

    private void HandleNoiseHeard(Vector3 noisePos, float radius)
    {
        if (currentState == AgathaState.Inactive || currentState == AgathaState.ChasingPlayer) return;

        float dist = Vector3.Distance(transform.position, noisePos);
        if (dist <= radius + hearingRadius)
        {
            lastKnownPlayerPos = noisePos;
            MoveTowards(noisePos, investigateSpeed);
        }
    }

    // =========================================================================
    // Stalking & Random Door Creaking at Night
    // =========================================================================

    private void UpdateStalking()
    {
        // Random door opening countdown
        doorTimer -= Time.deltaTime;
        if (doorTimer <= 0f)
        {
            doorTimer = doorOpenInterval + Random.Range(-4f, 6f);
            BackroomsDoor doorToOpen = FindNearbyClosedDoor();
            if (doorToOpen != null)
            {
                targetedDoor = doorToOpen;
                currentState = AgathaState.InvestigatingDoor;
                MoveTowards(doorToOpen.transform.position, investigateSpeed);
                return;
            }
        }

        // Wander between houses
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.6f)
            {
                Vector3 randomPoint = PickSuburbanWanderPoint();
                MoveTowards(randomPoint, stalkSpeed);
            }
        }
        else
        {
            if (Vector3.Distance(transform.position, currentFallbackTarget) < 2.0f || currentFallbackTarget == Vector3.zero)
            {
                currentFallbackTarget = PickSuburbanWanderPoint();
            }
            MoveTowards(currentFallbackTarget, stalkSpeed);
        }
    }

    private void UpdateDoorInvestigation()
    {
        if (targetedDoor == null)
        {
            currentState = AgathaState.StalkingNeighborhood;
            return;
        }

        float distToDoor = Vector3.Distance(transform.position, targetedDoor.transform.position);

        if (distToDoor <= 2.2f)
        {
            if (!targetedDoor.IsOpen)
            {
                targetedDoor.Open(autoClose: true, stayOpenTime: 6.5f);
                Debug.Log($"<color=#FF5533><b>[Agatha]</b></color> Agatha reached house door '{targetedDoor.name}' and opened it at night.");
            }

            if (currentBehaviorRoutine == null)
            {
                currentBehaviorRoutine = StartCoroutine(PeepIntoHouseRoutine());
            }
        }
        else
        {
            MoveTowards(targetedDoor.transform.position, investigateSpeed);
        }
    }

    private IEnumerator PeepIntoHouseRoutine()
    {
        if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
        yield return new WaitForSeconds(2.8f);

        if (agent != null && agent.isOnNavMesh) agent.isStopped = false;
        targetedDoor = null;
        currentState = AgathaState.StalkingNeighborhood;
        currentBehaviorRoutine = null;
    }

    private BackroomsDoor FindNearbyClosedDoor()
    {
        BackroomsDoor best = null;
        float bestDist = maxDoorSearchRadius;

        for (int i = 0; i < BackroomsDoor.AllDoors.Count; i++)
        {
            BackroomsDoor d = BackroomsDoor.AllDoors[i];
            if (d == null || d.IsOpen) continue;

            float dist = Vector3.Distance(transform.position, d.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = d;
            }
        }

        return best;
    }

    private Vector3 PickSuburbanWanderPoint()
    {
        for (int i = 0; i < 16; i++)
        {
            Vector3 randomDir = Random.insideUnitSphere * 24.0f;
            randomDir += transform.position;
            Vector3 grounded = SnapToTerrainSurface(randomDir);

            if (NavMesh.SamplePosition(grounded, out NavMeshHit hit, 6f, NavMesh.AllAreas))
            {
                return hit.position + Vector3.up * groundOffset;
            }
            if (grounded.y > -20f)
            {
                return grounded;
            }
        }
        return transform.position;
    }

    // =========================================================================
    // Chase & House Breaching
    // =========================================================================

    private void StartChase()
    {
        if (currentBehaviorRoutine != null)
        {
            StopCoroutine(currentBehaviorRoutine);
            currentBehaviorRoutine = null;
        }

        currentState = AgathaState.ChasingPlayer;
        MoveTowards(player.position, chaseSpeed);
    }

    private void UpdateChase()
    {
        if (hasVisualContact)
        {
            MoveTowards(player.position, chaseSpeed);
        }
        else
        {
            MoveTowards(lastKnownPlayerPos, chaseSpeed);

            // If player broke line of sight by entering a house through a closed door
            BackroomsDoor nearestDoor = FindNearbyClosedDoor();
            if (nearestDoor != null && Vector3.Distance(transform.position, nearestDoor.transform.position) < 3.2f)
            {
                targetedDoor = nearestDoor;
                currentState = AgathaState.BreachingHouse;
                return;
            }

            float distToLastKnown = Vector3.Distance(transform.position, lastKnownPlayerPos);
            if (distToLastKnown < 1.5f)
            {
                currentState = AgathaState.StalkingNeighborhood;
                return;
            }
        }

        // Catch Distance Check
        float distToPlayer = Vector3.Distance(transform.position, player.position);
        if (distToPlayer <= catchDistance)
        {
            TriggerJumpscare();
        }
    }

    private void UpdateBreach()
    {
        if (targetedDoor != null)
        {
            targetedDoor.Open(autoClose: true, stayOpenTime: 8.0f);
            Debug.Log("<color=#FF0000><b>[Agatha]</b></color> Agatha burst open the house door to chase the player inside!");
        }

        currentState = AgathaState.ChasingPlayer;
        MoveTowards(player != null ? player.position : lastKnownPlayerPos, chaseSpeed);
    }

    // =========================================================================
    // Robust Hybrid Movement (NavMesh + Dynamic Terrain Fallback)
    // =========================================================================

    private void MoveTowards(Vector3 targetPos, float speed)
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.speed = speed;
            agent.isStopped = false;
            agent.SetDestination(targetPos);
        }
        else
        {
            Vector3 flatDir = targetPos - transform.position;
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude > 0.1f)
            {
                Quaternion targetRot = Quaternion.LookRotation(flatDir.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 8f);
                transform.position += transform.forward * speed * Time.deltaTime;
            }
            KeepGrounded();
        }
    }

    private IEnumerator FlashlightFlickerRoutine()
    {
        if (playerFlashlight == null) yield break;

        for (int i = 0; i < 4; i++)
        {
            playerFlashlight.SetState(false, playAudio: false);
            yield return new WaitForSeconds(Random.Range(0.04f, 0.12f));
            playerFlashlight.SetState(true, playAudio: false);
            yield return new WaitForSeconds(Random.Range(0.06f, 0.18f));
        }

        flashlightFlickerRoutine = null;
    }

    // =========================================================================
    // Jumpscare Attack & Defeat
    // =========================================================================

    private void TriggerJumpscare()
    {
        if (currentState == AgathaState.JumpscareAttack) return;
        currentState = AgathaState.JumpscareAttack;

        if (agent != null && agent.isOnNavMesh) agent.isStopped = true;

        transform.LookAt(new Vector3(player.position.x, transform.position.y, player.position.z));

        if (animator != null)
        {
            animator.SetTrigger(JumpscareHash);
        }

        if (killScreamAudio != null && audioSource != null)
        {
            audioSource.PlayOneShot(killScreamAudio, audioVolume * GameSettings.MasterVolume);
        }

        GameOverController.TriggerDefeat(transform);
    }

    private void UpdateAnimation()
    {
        if (animator == null) return;

        float currentVelocity = agent != null && agent.isOnNavMesh ? agent.velocity.magnitude : (currentState == AgathaState.ChasingPlayer ? chaseSpeed : stalkSpeed);
        animator.SetFloat(SpeedHash, currentVelocity);
        animator.SetBool(IsChasingHash, currentState == AgathaState.ChasingPlayer || currentState == AgathaState.BreachingHouse);
    }
}
