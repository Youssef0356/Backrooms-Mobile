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

    enum State { Wander, Chase }
    State state = State.Wander;

    NavMeshAgent agent;
    float waitTimer;
    float lostTimer;

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