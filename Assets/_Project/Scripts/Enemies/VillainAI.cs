using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class VillainAI : MonoBehaviour
{
    [Header("Target")]
    public Transform player;                 // leave empty to auto-find the object tagged "Player"

    [Header("Speeds")]
    public float wanderSpeed = 1.5f;
    public float chaseSpeed = 4f;

    [Header("Wandering")]
    public float wanderRadius = 15f;         // how far he picks random points
    public float waitAtPoint = 2f;           // pause at each point (seconds)

    [Header("Vision")]
    public float viewDistance = 12f;
    [Range(10, 180)] public float viewAngle = 100f;
    public float eyeHeight = 1.5f;
    public float loseSightTime = 4f;         // keeps chasing this long after losing you

    [Header("Catch")]
    public float catchDistance = 1.2f;

    [Header("Animation")]
    public float walkAnimReferenceSpeed = 1.4f;  // ground speed the walk clip was authored at (lower = faster playback)
    public float runAnimReferenceSpeed = 3.5f;   // ground speed the run clip was authored at

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
    [Tooltip("Only land where the player cannot see him. If no such spot exists the teleport is postponed, so he never pops into view.")]
    public bool avoidVisibleSpots = true;
    [Tooltip("Half-angle (degrees) of the cone the player is considered to be looking at. Spots outside it count as unseen.")]
    public float playerSightHalfAngle = 60f;

    enum State { Wander, Chase }
    State state = State.Wander;

    NavMeshAgent agent;
    Animator anim;
    float waitTimer;
    float lostTimer;
    float teleportTimer;
    float attackTimer;
    bool hasTeleported;
    bool hasCaught;

    // Animator parameters actually present in the controller. Rigs differ
    // (the old Bacteria uses IsChasing only, Clark uses Speed + attack triggers),
    // so everything is optional and only driven when it exists.
    bool hasSpeed, hasChasing, hasAttack1, hasAttack2;
    int speedHash, chasingHash, attack1Hash, attack2Hash;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        anim = GetComponentInChildren<Animator>();
        CacheAnimatorParameters();
        SetChasing(false);                      // the controller defaults IsChasing to true

        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
            else Debug.LogWarning("VillainAI: no object tagged 'Player' found.");
        }

        agent.speed = wanderSpeed;
        GoToRandomPoint();
    }

    void Update()
    {
        if (player == null) return;

        CheckReposition();
        SyncAnimationSpeed();

        bool canSee = CanSeePlayer();

        if (state == State.Wander)
        {
            if (canSee)
            {
                state = State.Chase;
                SetChasing(true);
                agent.speed = chaseSpeed;
                lostTimer = 0f;
                attackTimer = 0f;
                return;
            }

            // Reached the point? Wait, then pick a new one.
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
            {
                waitTimer += Time.deltaTime;
                if (waitTimer >= waitAtPoint)
                {
                    waitTimer = 0f;
                    GoToRandomPoint();
                }
            }
        }
        else // Chase
        {
            agent.SetDestination(player.position);

            if (canSee) lostTimer = 0f;
            else lostTimer += Time.deltaTime;

            if (lostTimer >= loseSightTime)
            {
                state = State.Wander;
                SetChasing(false);
                agent.speed = wanderSpeed;
                GoToRandomPoint();
            }

            if (Vector3.Distance(transform.position, player.position) <= catchDistance)
            {
                OnCaughtPlayer();
            }
            else
            {
                hasCaught = false;
                UpdateAttack();
            }
        }
    }

    bool CanSeePlayer()
    {
        Vector3 eyes = transform.position + Vector3.up * eyeHeight;
        Vector3 target = player.position + Vector3.up * 1f;
        Vector3 dir = target - eyes;

        if (dir.magnitude > viewDistance) return false;
        if (Vector3.Angle(transform.forward, dir) > viewAngle * 0.5f) return false;

        // Make sure no wall is in the way
        if (Physics.Raycast(eyes, dir.normalized, out RaycastHit hit, viewDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.transform == player || hit.transform.IsChildOf(player) || player.IsChildOf(hit.transform);
        }
        return false;
    }

    void GoToRandomPoint()
    {
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

    /// Keeps the playback rate in step with the agent's real ground speed so
    /// the feet never slide (moonwalk) or stutter when the speed changes.
    void SyncAnimationSpeed()
    {
        if (anim == null) return;

        float groundSpeed = agent.velocity.magnitude;

        // Controllers with an Idle/Walk/Run blend read the speed themselves.
        if (hasSpeed)
        {
            anim.speed = 1f;
            anim.SetFloat(speedHash, groundSpeed);
            return;
        }

        float reference = state == State.Chase ? runAnimReferenceSpeed : walkAnimReferenceSpeed;
        if (reference < 0.1f) reference = 0.1f;

        // tiny floor so transitions keep progressing while he stands still
        anim.speed = Mathf.Clamp(groundSpeed / reference, 0.05f, 3f);
    }

    /// Swings when he is close but not yet on top of the player.
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

    // ---------------- Teleport back near the player ----------------

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
        if (!TryFindTeleportSpot(out Vector3 chosen))
        {
            // Every candidate would land in view: wait for the next cooldown
            // instead of popping into the player's sight.
            teleportTimer = 0f;
            return;
        }

        agent.Warp(chosen);
        state = State.Wander;
        SetChasing(false);
        agent.speed = wanderSpeed;
        waitTimer = 0f;
        lostTimer = 0f;
        teleportTimer = 0f;
        hasTeleported = true;
        GoToRandomPoint();

#if UNITY_EDITOR
        Debug.Log("VillainAI: repositioned near the player (unseen).");
#endif
    }

    /// Finds a NavMesh spot at teleportRange from the player that he cannot
    /// currently see. Returns false when every candidate would be in view, so the
    /// caller can postpone instead of teleporting in plain sight.
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

            // Never land on top of the player (NavMesh snapping may have moved the spot closer)
            float flatDist = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(playerPos.x, playerPos.z));
            if (flatDist < minDist) continue;

            if (avoidVisibleSpots && IsSpotVisibleToPlayer(pos)) continue;

            spot = pos;
            return true;
        }

        return false;
    }

    /// Visible = inside the cone the player is facing AND with a clear line of
    /// sight. Spots behind him or around a corner are safe, which is exactly what
    /// makes the teleport invisible.
    bool IsSpotVisibleToPlayer(Vector3 spot)
    {
        Vector3 eyes = player.position + Vector3.up * eyeHeight;
        Vector3 target = spot + Vector3.up * 1.1f;          // body height, not the floor
        Vector3 dir = target - eyes;

        if (Vector3.Angle(player.forward, dir) > playerSightHalfAngle) return false;  // behind him

        if (Physics.Raycast(eyes, dir.normalized, out _, dir.magnitude, ~0, QueryTriggerInteraction.Ignore))
            return false;                                    // a wall hides him

        return true;                                         // in the cone, nothing in the way
    }

    void OnCaughtPlayer()
    {
        if (hasCaught) return;
        hasCaught = true;

        if (anim != null && hasAttack1) anim.SetTrigger(attack1Hash);

        // Put your game over / jumpscare / restart logic here.
        Debug.Log("Player caught!");
    }

    // Shows the vision range in the Scene view when selected
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, viewDistance);
    }
}
