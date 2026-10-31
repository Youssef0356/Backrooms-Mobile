using System.Collections.Generic;
using UnityEngine;
using StarterAssets;

/// <summary>
/// Handles the anomalous Backrooms Level 94 house distortion:
/// - If the player enters a suburban house and lingers inside for more than 30 seconds,
///   the house anomalies trigger an undetectable spatial displacement to another house on the map.
/// - The teleport preserves the player's relative room position and smart rotation so that the user
///   cannot notice any transition while inside the room.
/// - Only when the player steps back outside or peers through the windows do they realize the
///   surrounding terrain, water towers, and hills have completely changed!
/// </summary>
[DisallowMultipleComponent]
public class Level94HouseTeleporter : MonoBehaviour
{
    public static Level94HouseTeleporter Instance { get; private set; }

    [Header("Timing")]
    [Tooltip("How many seconds the player must stay inside a house before triggering teleportation.")]
    public float requiredStayDuration = 30.0f;

    [Header("House Detection")]
    [Tooltip("Tag or prefix used to find houses in the scene.")]
    public string housesContainerName = "LEVEL 94 Houses (Randomized)";
    [Tooltip("Half-extents of the house interior bounding box (in meters).")]
    public Vector3 houseInteriorHalfExtents = new Vector3(3.25f, 2.25f, 3.0f);
    public Vector3 houseInteriorCenterOffset = new Vector3(0f, 2.0f, 0.5f);

    [Header("Debug & Status")]
    public Transform currentHouse;
    public float currentStayTimer = 0f;
    public bool isInsideHouse = false;
    public int totalTeleports = 0;

    private List<Transform> registeredHouses = new List<Transform>();
    private Transform exitHouseTransform;
    private Transform player;
    private CharacterController playerCc;
    private FirstPersonController fpc;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        FindPlayer();
        CollectHouses();
    }

    public void RegisterExitHouse(Transform exitHouse)
    {
        exitHouseTransform = exitHouse;
    }

    private void FindPlayer()
    {
        GameObject p = GameObject.FindWithTag("Player") ?? GameObject.Find("PlayerCapsule");
        if (p != null)
        {
            player = p.transform;
            playerCc = p.GetComponent<CharacterController>();
            fpc = p.GetComponent<FirstPersonController>();
        }
    }

    public void CollectHouses()
    {
        registeredHouses.Clear();

        GameObject container = GameObject.Find(housesContainerName);
        if (container != null)
        {
            for (int i = 0; i < container.transform.childCount; i++)
            {
                Transform child = container.transform.GetChild(i);
                if (child.gameObject.activeInHierarchy)
                {
                    registeredHouses.Add(child);
                }
            }
        }
        else
        {
            // Fallback: search by name
            GameObject[] allGo = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude);
            foreach (var go in allGo)
            {
                if (go.name.StartsWith("House_") || go.name.Contains("House"))
                {
                    if (go.transform.parent != null && go.transform.parent.name.Contains("Distant")) continue;
                    registeredHouses.Add(go.transform);
                }
            }
        }

        Debug.Log($"[HouseTeleporter] Registered {registeredHouses.Count} suburban houses for spatial teleportation.");
    }

    private void Update()
    {
        if (player == null)
        {
            FindPlayer();
            if (player == null) return;
        }

        // 1. Detect if player is currently inside any house
        Transform detectedHouse = FindHouseContainingPlayer();

        if (detectedHouse != null)
        {
            if (currentHouse != detectedHouse)
            {
                // Entered a new house
                currentHouse = detectedHouse;
                currentStayTimer = 0f;
                isInsideHouse = true;
            }
            else
            {
                // Lingering inside house: accumulate timer
                currentStayTimer += Time.deltaTime;
                isInsideHouse = true;

                if (currentStayTimer >= requiredStayDuration)
                {
                    ExecuteSmartTeleport();
                }
            }
        }
        else
        {
            // Outside in the suburban streets or hills
            if (isInsideHouse)
            {
                isInsideHouse = false;
                currentHouse = null;
                currentStayTimer = 0f;
            }
        }
    }

    /// <summary>
    /// Checks all registered houses to see if the player is inside one of them.
    /// </summary>
    private Transform FindHouseContainingPlayer()
    {
        Vector3 playerPos = player.position;

        foreach (var house in registeredHouses)
        {
            if (house == null) continue;

            // Compute local position of player relative to house
            Vector3 localPos = house.InverseTransformPoint(playerPos);
            Vector3 diff = localPos - houseInteriorCenterOffset;

            if (Mathf.Abs(diff.x) <= houseInteriorHalfExtents.x &&
                Mathf.Abs(diff.z) <= houseInteriorHalfExtents.z &&
                diff.y >= -0.5f && diff.y <= houseInteriorHalfExtents.y * 2f)
            {
                return house;
            }
        }

        return null;
    }

    /// <summary>
    /// Intelligently teleports the player to another house without changing their relative room perspective.
    /// </summary>
    public void ExecuteSmartTeleport()
    {
        if (currentHouse == null || registeredHouses.Count <= 1) return;

        // Collect destination candidates (exclude current house and exclude exit house)
        List<Transform> candidates = new List<Transform>();
        List<Transform> sameOrientationCandidates = new List<Transform>();

        float currentYaw = currentHouse.eulerAngles.y;

        foreach (var h in registeredHouses)
        {
            if (h == null || h == currentHouse) continue;
            if (exitHouseTransform != null && h == exitHouseTransform) continue; // Don't teleport into the exit house

            candidates.Add(h);

            // Check if house has matching orientation (yaw difference near 0)
            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(currentYaw, h.eulerAngles.y));
            if (angleDiff < 3.0f)
            {
                sameOrientationCandidates.Add(h);
            }
        }

        if (candidates.Count == 0) return;

        // Smart selection: preferentially pick a house with the exact same rotation!
        Transform targetHouse;
        float deltaYaw = 0f;

        if (sameOrientationCandidates.Count > 0)
        {
            targetHouse = sameOrientationCandidates[Random.Range(0, sameOrientationCandidates.Count)];
            deltaYaw = 0f; // Exact same rotation: ZERO visual difference to the camera!
        }
        else
        {
            targetHouse = candidates[Random.Range(0, candidates.Count)];
            deltaYaw = Mathf.DeltaAngle(currentYaw, targetHouse.eulerAngles.y);
        }

        // Map local relative position inside current house to target house
        Vector3 playerLocalPos = currentHouse.InverseTransformPoint(player.position);
        Vector3 targetWorldPos = targetHouse.TransformPoint(playerLocalPos);

        // Perform safe teleport
        if (playerCc != null) playerCc.enabled = false;

        player.position = targetWorldPos;

        // If rotation delta exists, adjust player orientation smoothly to match interior room perspective
        if (Mathf.Abs(deltaYaw) > 0.05f)
        {
            player.rotation = Quaternion.Euler(0f, player.eulerAngles.y + deltaYaw, 0f);
        }

        if (playerCc != null) playerCc.enabled = true;

        currentHouse = targetHouse;
        currentStayTimer = 0f; // Reset 30s timer for the new house
        totalTeleports++;

        Debug.Log($"<color=#9955FF><b>[Liminal Teleport]</b></color> Player stayed >30s in '{currentHouse.name}'. Seamlessly displaced to '{targetHouse.name}' (DeltaYaw: {deltaYaw:F1}°).");
    }
}
