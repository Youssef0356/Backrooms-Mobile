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

    [Header("Repositioning (keeps him near the player)")]
    public bool enableTeleport = true;
    public float maxDistanceFromPlayer = 40f;   // if he drifts beyond this, he teleports back
    public Vector2 teleportRange = new Vector2(15f, 30f); // distance from player after teleporting
    public float firstTeleportDelay = 6f;       // waits this long after spawn before first reposition
    public float teleportCooldown = 10f;        // min seconds between teleports
    [Range(1, 30)] public int teleportAttempts = 12; // random spots to try per teleport
    public float teleportSampleRadius = 3f;     // how far a spot may snap onto the NavMesh
    public bool avoidVisibleSpots = true;       // prefer spots hidden behind walls from the player

    enum State { Wander, Chase }
    State state = State.Wander;

    NavMeshAgent agent;
    float waitTimer;
    float lostTimer;
    float teleportTimer;
    bool hasTeleported;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();

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

        bool canSee = CanSeePlayer();

        if (state == State.Wander)
        {
            if (canSee)
            {
                state = State.Chase;
                agent.speed = chaseSpeed;
                lostTimer = 0f;
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
                agent.speed = wanderSpeed;
                GoToRandomPoint();
            }

            if (Vector3.Distance(transform.position, player.position) <= catchDistance)
            {
                OnCaughtPlayer();
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
        Vector3 playerPos = player.position;
        Vector3 fallback = Vector3.zero;
        bool foundFallback = false;
        Vector3 chosen = Vector3.zero;
        bool found = false;

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
            if (flatDist < teleportRange.x * 0.6f) continue;

            if (!foundFallback) { fallback = pos; foundFallback = true; }

            // Prefer a spot the player can't currently see, so he doesn't pop into view
            if (!avoidVisibleSpots || !SpotIsVisibleToPlayer(pos))
            {
                chosen = pos;
                found = true;
                break;
            }
        }

        if (!found)
        {
            if (!foundFallback) return;         // no valid NavMesh spot found; retry next cooldown
            chosen = fallback;
        }

        agent.Warp(chosen);
        state = State.Wander;
        agent.speed = wanderSpeed;
        waitTimer = 0f;
        lostTimer = 0f;
        teleportTimer = 0f;
        hasTeleported = true;
        GoToRandomPoint();

        Debug.Log("VillainAI: repositioned near the player.");
    }

    bool SpotIsVisibleToPlayer(Vector3 spot)
    {
        Vector3 from = player.position + Vector3.up * 1.5f;
        Vector3 to = spot + Vector3.up * 1.5f;
        Vector3 dir = to - from;

        if (Physics.Raycast(from, dir.normalized, out RaycastHit hit, dir.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.transform == player || hit.transform.IsChildOf(player) || player.IsChildOf(hit.transform);
        }
        return false;                           // something is in the way = hidden
    }

    void OnCaughtPlayer()
    {
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