using System.Collections.Generic;
using UnityEngine;
using StarterAssets;

/// <summary>
/// Central director orchestrating the complete Level 94 gameplay:
/// 1. Day (1 min) / Night (2 min) cycle with dense nocturnal fog and rare blink night trigger.
/// 2. Player torchlight (Flashlight like Level 0) auto-equipped and activated at night.
/// 3. Interactive house doors with open/close logic for player and stalkers.
/// 4. Objective: Find the right house that holds the non-glowing Level 0 exit door.
/// 5. Undetectable 30-second house stay teleportation to other houses.
/// 6. Agatha scary stalker entity roaming at night, breaching houses, and opening doors.
/// </summary>
[DisallowMultipleComponent]
public class Level94GameplayDirector : MonoBehaviour
{
    public static Level94GameplayDirector Instance { get; private set; }

    [Header("Exit House & Objective")]
    [Tooltip("The designated house that conceals the escape threshold.")]
    public Transform exitHouse;
    [Tooltip("World position of the non-glowing exit portal inside the exit house.")]
    public GameObject spawnedExitPortal;

    [Header("Agatha Night Stalker")]
    public GameObject agathaInstance;
    public GameObject agathaPrefab;

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
        InitializeLevel94Gameplay();
    }

    [ContextMenu("Initialize / Refresh Level 94 Gameplay")]
    public void InitializeLevel94Gameplay()
    {
        SetupPlayerSystems();
        SetupHouseDoorsAndInteriorZones();
        SetupExitHouseAndPortal();
        SetupTeleporter();
        SetupAgathaStalker();
        SetupDayNightAndFog();
    }

    // =========================================================================
    // 1. Player Systems: Torchlight, Door Interaction, Blinking
    // =========================================================================

    private void SetupPlayerSystems()
    {
        GameObject player = GameObject.FindWithTag("Player") ?? GameObject.Find("PlayerCapsule");
        if (player == null) return;

        // PlayerStamina
        if (player.GetComponent<PlayerStamina>() == null)
        {
            player.AddComponent<PlayerStamina>();
        }

        // Exhaustion & Natural Blinking Effect
        if (player.GetComponent<PlayerExhaustionBlinkEffect>() == null)
        {
            player.AddComponent<PlayerExhaustionBlinkEffect>();
        }

        // CharacterController sizing for standard house doorways (0.44m collision diameter, 1.55m height with clearance)
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.radius = 0.22f; // 0.44m diameter glides effortlessly through 0.87m doorways
            cc.height = 1.55f; // 1.55m height provides 22cm headroom below the porch lintel
            cc.center = new Vector3(0f, 0.78f, 0f);
            cc.stepOffset = 0.45f; // Steps easily over porches and door thresholds
            cc.slopeLimit = 60.0f;
            cc.skinWidth = 0.025f;
        }

        // Door Interaction ([E] / Mobile Tap prompt)
        if (player.GetComponent<PlayerDoorInteraction>() == null)
        {
            player.AddComponent<PlayerDoorInteraction>();
        }

        // Torchlight (Flashlight like Level 0) on PlayerCameraRoot
        Transform camRoot = player.transform.Find("PlayerCameraRoot") ?? player.transform;
        Flashlight fl = camRoot.GetComponentInChildren<Flashlight>();
        if (fl == null)
        {
            GameObject flGo = new GameObject("Flashlight");
            flGo.transform.SetParent(camRoot, false);
            flGo.transform.localPosition = new Vector3(0.18f, -0.16f, 0.20f);
            flGo.transform.localRotation = Quaternion.identity;

            fl = flGo.AddComponent<Flashlight>();
            fl.intensity = 3.6f;
            fl.range = 26f;
            fl.startOn = false; // Starts off during day, turns on at night
        }
    }

    // =========================================================================
    // 2. Interactive House Doors & Colliders
    // =========================================================================

    private void SetupHouseDoorsAndInteriorZones()
    {
        GameObject housesContainer = GameObject.Find("LEVEL 94 Houses (Randomized)");
        if (housesContainer == null) return;

        for (int i = 0; i < housesContainer.transform.childCount; i++)
        {
            Transform house = housesContainer.transform.GetChild(i);
            ConfigureHouseDoor(house);
        }
    }

    public static void ConfigureHouseDoor(Transform house)
    {
        if (house == null) return;

        // Find Door parent node
        Transform doorRoot = house.Find("Animated Interior Door");
        if (doorRoot == null)
        {
            // Search deeper in children
            doorRoot = FindDeepChild(house, "Animated Interior Door");
        }

        if (doorRoot == null) return;

        Transform doorLeaf = doorRoot.Find("Door");
        if (doorLeaf == null) doorLeaf = FindDeepChild(doorRoot, "Door");

        // Ensure BackroomsDoor component on Animated Interior Door
        BackroomsDoor bd = doorRoot.GetComponent<BackroomsDoor>();
        if (bd == null)
        {
            bd = doorRoot.gameObject.AddComponent<BackroomsDoor>();
        }

        if (doorLeaf != null)
        {
            bd.customHingePivot = doorLeaf;
            bd.openAngle = 100f; // Wide swing opens completely flat against wall
            bd.openDuration = 0.50f;
            bd.closeDuration = 0.60f;
            bd.autoCloseDelay = 4.5f;
            bd.monsterDetectRadius = 3.8f;
            bd.EnsureHingeBinding();

            // 1. Remove static MeshColliders from rotating parts so PhysX doesn't block the doorway!
            MeshCollider[] meshCols = doorRoot.GetComponentsInChildren<MeshCollider>(true);
            foreach (var mc in meshCols)
            {
                if (mc.name.Contains("Door") || mc.name.Contains("Cube") || mc.name.Contains("Latch") || mc.name.Contains("Handle"))
                {
                    if (Application.isPlaying) Object.Destroy(mc);
                    else Object.DestroyImmediate(mc);
                }
            }

            // 2. Add rotating BoxCollider to the door leaf that swings out of the opening when opened
            BoxCollider bc = doorLeaf.GetComponent<BoxCollider>();
            if (bc == null) bc = doorLeaf.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0.40f, -0.04f, -0.01f);
            bc.size = new Vector3(0.82f, 1.95f, 0.04f);
            bc.isTrigger = false;
        }
    }

    private static Transform FindDeepChild(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName) return child;
            Transform result = FindDeepChild(child, childName);
            if (result != null) return result;
        }
        return null;
    }

    // =========================================================================
    // 3. Objective: Exit House with Non-Glowing Level 0 Exit Door
    // =========================================================================

    private void SetupExitHouseAndPortal()
    {
        GameObject housesContainer = GameObject.Find("LEVEL 94 Houses (Randomized)");
        if (housesContainer == null || housesContainer.transform.childCount == 0) return;

        // Choose the farthest house from origin as the secret Exit House
        Transform bestHouse = null;
        float maxDistSqr = -1f;

        for (int i = 0; i < housesContainer.transform.childCount; i++)
        {
            Transform h = housesContainer.transform.GetChild(i);
            float d2 = h.position.sqrMagnitude;
            if (d2 > maxDistSqr)
            {
                maxDistSqr = d2;
                bestHouse = h;
            }
        }

        if (bestHouse == null) bestHouse = housesContainer.transform.GetChild(0);

        exitHouse = bestHouse;
        exitHouse.name = "House_Exit_Target";

        // Check if exit portal already spawned inside this house
        Transform existingPortal = exitHouse.Find("BackroomsExit_NonGlowing");
        if (existingPortal != null)
        {
            spawnedExitPortal = existingPortal.gameObject;
            return;
        }

        // Procedurally build the non-glowing Level 0 exit portal against the back wall of the house interior
        // Interior back wall is at local Z = -4.85m, Y = 0.05m
        Vector3 portalLocalPos = new Vector3(0.0f, 0.05f, -4.80f);
        // Face forward towards the front entrance
        Quaternion portalLocalRot = Quaternion.Euler(0f, 0f, 0f);

        Vector3 portalWorldPos = exitHouse.TransformPoint(portalLocalPos);
        Quaternion portalWorldRot = exitHouse.rotation * portalLocalRot;

        spawnedExitPortal = BackroomsExitZone.BuildNonGlowingExitPortal(portalWorldPos, portalWorldRot);
        spawnedExitPortal.transform.SetParent(exitHouse, true);

        Debug.Log($"<color=#00FF88><b>[Objective Setup]</b></color> Designated '{exitHouse.name}' as the Exit House containing the non-glowing Level 0 Exit Door!");
    }

    // =========================================================================
    // 4. Undetectable 30-Second House Stay Teleportation
    // =========================================================================

    private void SetupTeleporter()
    {
        Level94HouseTeleporter teleporter = GetComponent<Level94HouseTeleporter>();
        if (teleporter == null) teleporter = gameObject.AddComponent<Level94HouseTeleporter>();

        teleporter.requiredStayDuration = 30.0f;
        teleporter.houseInteriorCenterOffset = new Vector3(0f, 1.2f, -2.5f);
        teleporter.houseInteriorHalfExtents = new Vector3(2.6f, 1.8f, 2.5f);

        if (exitHouse != null)
        {
            teleporter.RegisterExitHouse(exitHouse);
        }

        teleporter.CollectHouses();
    }

    // =========================================================================
    // 5. Agatha Scary Stalker Entity
    // =========================================================================

    private void SetupAgathaStalker()
    {
        if (agathaInstance == null)
        {
            agathaInstance = GameObject.Find("Agatha_Stalker");
            if (agathaInstance == null)
            {
                // Look for any AgathaAI in scene
                AgathaAI existingAi = FindAnyObjectByType<AgathaAI>();
                if (existingAi != null) agathaInstance = existingAi.gameObject;
            }
        }

        if (agathaInstance == null)
        {
            // Load Agatha prefab if present
            if (agathaPrefab == null)
            {
                agathaPrefab = Resources.Load<GameObject>("Prefabs/Agatha_Stalker");
            }

            if (agathaPrefab != null)
            {
                agathaInstance = Instantiate(agathaPrefab);
                agathaInstance.name = "Agatha_Stalker";
            }
        }
    }

    // =========================================================================
    // 6. Day/Night Cycle, Fog & Lighting
    // =========================================================================

    private void SetupDayNightAndFog()
    {
        Level94DayNightCycle dnc = GetComponent<Level94DayNightCycle>();
        if (dnc == null) dnc = gameObject.AddComponent<Level94DayNightCycle>();

        dnc.dayDuration = 60.0f;   // 1 minute day
        dnc.nightDuration = 120.0f;// 2 minutes night
        dnc.rareBlinkNightChance = 0.08f;
        dnc.enableBlinkNightTrigger = true;
        dnc.nightFogStart = 8.0f;
        dnc.nightFogEnd = 45.0f;
        dnc.autoActivateFlashlightAtNight = true;
    }
}
