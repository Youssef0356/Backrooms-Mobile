using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Unity.AI.Navigation;

/// Generates a backrooms level made of rooms (with pillar grids) joined by corridors.
/// Put the NavMeshSurface object at position 0,0,0 with no rotation or scale.
public class BackroomsRoomGenerator : MonoBehaviour
{
    [Header("Grid")]
    public int gridWidth = 50;              // cells along X
    public int gridHeight = 50;             // cells along Z
    public float tileSize = 2.5f;           // size of one cell in meters
    public float wallHeight = 3f;
    public float wallThickness = 0.25f;

    [Header("Rooms and corridors")]
    public int roomCount = 16;
    public Vector2Int roomSizeMin = new Vector2Int(2, 2);
    public Vector2Int roomSizeMax = new Vector2Int(4, 4);   // normal rooms stay in this range
    [Range(0f, 1f)] public float bigRoomChance = 0.05f;    // chance a room is a rare big hall
    public Vector2Int bigRoomSizeMax = new Vector2Int(5, 5);
    public int maxBigRooms = 1;                             // hard cap on big halls per map
    [Range(1, 3)] public int corridorWidth = 1;
    public int extraConnections = 10;       // extra corridors = loops, so the villain can't be dodged easily
    public int seed = 0;                    // 0 = random each time

    [Header("Maze branches")]
    [Tooltip("Extra corridors that fork off the network and dead-end, so the map is a web of narrow ways instead of a few big halls.")]
    public int deadEndBranches = 12;
    [Tooltip("Longest a dead-end branch may run, in cells.")]
    public int deadEndLength = 4;

    [Header("Details")]
    public int pillarSpacing = 2;           // 0 = no pillars
    [Range(0f, 1f)] public float pillarChance = 0.7f;
    public int lightEvery = 3;              // ceiling light panel every N cells
    public GameObject lightPrefab;          // optional point light prefab (no shadows)
    [Range(0f, 1f)] public float lightPrefabChance = 0.3f;

    [Header("Panel lighting")]
    [Tooltip("Spawns a real point light under every ceiling panel, so the level is lit by its fixtures instead of flat ambient.")]
    public bool panelLights = true;
    [Tooltip("Upper bound on point lights in the whole level. Panels past this keep their glow but cast no light.")]
    public int maxPanelLights = 24;
    public float panelLightIntensity = 1.15f;
    public float panelLightRange = 5.5f;
    public Color panelLightColor = new Color(1f, 0.96f, 0.84f);
    [Tooltip("Chance a panel light is a dying fluorescent that randomly stutters.")]
    [Range(0f, 1f)] public float panelFlickerChance = 0.08f;

    [Header("Decorations (optional prefabs)")]
    [Tooltip("Props scattered around the level: chairs, boxes, junk. Leave empty to disable decorations.")]
    public GameObject[] decorationPrefabs;
    [Tooltip("Props mounted flat on walls: posters, signs, pictures. Leave empty to auto-detect thin props in the list above.")]
    public GameObject[] wallDecorationPrefabs;
    [Range(0f, 1f)] public float decorationChance = 0.07f;  // chance per walkable cell
    public int maxDecorations = 45;
    [Range(0f, 1f)] public float weirdChance = 0.4f;        // chance a prop is placed in an impossible way
    public bool decorateCorridors = true;
    public Vector2 decorationScaleRange = new Vector2(0.9f, 1.1f);
    [Tooltip("Automatically normalizes all decor prefabs to realistic Backrooms human scale (chairs ~0.92m, sofas ~0.82m x ~1.9m, tables ~0.75m) regardless of raw 3D model scale offsets.")]
    public bool normalizeDecorScale = true;

    [Header("Materials (used when no prefab is assigned)")]
    public Material floorMaterial;
    public Material ceilingMaterial;
    public Material wallMaterial;
    public Material pillarMaterial;         // empty = uses wall material
    public Material baseboardMaterial;      // optional trim along walls
    public Material lightPanelMaterial;     // optional emissive ceiling panels

    [Header("Texture size (meters per texture repeat)")]
    public float floorTextureMeters = 2f;   // bigger number = bigger pattern
    public float wallTextureMeters = 2f;
    public float ceilingTextureMeters = 3f;

    [Header("Wall Decals (Horror Markings)")]
    [Tooltip("Scatter creepy horror wall markings (claw marks, bloody prints, eerie warnings) across corridor and room walls.")]
    public bool enableWallDecals = true;
    [Range(0f, 1f)] public float wallDecalChance = 0.16f;
    [Range(0f, 1f)] public float deadEndDecalChance = 0.75f;
    public int maxWallDecals = 45;
    public Material[] customDecalMaterials;

    [Header("Wall Vents (Hiding Spots)")]
    [Tooltip("Scatter crawl-in wall vents across corridors and rooms for escaping the entity.")]
    public bool enableWallVents = true;
    [Range(0, 30)] public int maxVents = 12;
    [Range(0f, 1f)] public float ventChance = 0.12f;
    public Material ventMetalMaterial;
    public Material ventDuctMaterial;

    [Header("Enclosed Rooms & Doors")]
    [Tooltip("Generate enclosed rooms with perimeter partition walls and interactive swinging doors.")]
    public bool enableEnclosedRooms = true;
    [Tooltip("Maximum number of rooms on the entire map that will have doors.")]
    [Range(1, 10)] public int maxEnclosedRooms = 3;
    [Range(0f, 1f)] public float roomDoorChance = 0.28f;
    [Tooltip("Opening width of the doorway in meters. Must be >= 1.85m so monster NavMesh agent can freely pass.")]
    public float doorwayWidth = 1.95f;
    [Tooltip("Opening height of the doorway in meters. Tall enough so tall monster models easily fit.")]
    public float doorwayHeight = 2.85f;
    [Tooltip("Seconds an opened door stays open before closing automatically.")]
    public float doorStayOpenSeconds = 4.5f;
    public Material doorWoodMaterial;
    public Material doorFrameMaterial;

    [Header("Room Lockers")]
    [Tooltip("Scatter hideable lockers inside rooms for evading the entity.")]
    public bool enableRoomLockers = true;
    [Range(0f, 1f)] public float lockerChancePerRoom = 0.80f;
    public Material lockerExteriorMaterial;
    public Material lockerInteriorMaterial;

    [Header("Door Events")]
    [Tooltip("Periodically trigger paranormal events where all doors open and slam shut.")]
    public bool enableDoorEvents = true;

    [Header("Optional prefabs (override the boxes)")]
    public GameObject floorPrefab;
    public GameObject ceilingPrefab;
    public GameObject wallPrefab;
    public GameObject pillarPrefab;
    public bool flipWallFacing = false;     // tick if wall prefabs face the wrong way

    [Header("References")]
    public NavMeshSurface surface;          // your NavMeshBuilder object
    public Transform player;                // PlayerCapsule
    public GameObject villain;              // keep INACTIVE in the scene; the generator enables it

    [Header("Smily Hound (Exit Portal Guardian)")]
    [Tooltip("Spawn the smart 4-legged Smily Hound guarding the exit portal area.")]
    public bool enableSmilyHound = true;
    public GameObject smilyHoundPrefab;

    [Header("Prologue Intro")]
    [Tooltip("Start Level 0 in the player's dark house at night before noclip transition to Backrooms.")]
    public bool enableHousePrologue = true;

    [Header("Audio")]
    public bool autoAmbience = true;        // create a BackroomsAmbience object at level start

    bool[,] floor;
    bool[,] roomCell;
    readonly List<RectInt> rooms = new List<RectInt>();
    readonly HashSet<long> ventEdges = new HashSet<long>();
    readonly List<Vector3> tableTopSpots = new List<Vector3>();
    Transform root;
    Vector2Int reservedExitCell = new Vector2Int(-99, -99);

    void Start()
    {
        if (enableHousePrologue && Application.isPlaying)
        {
            HousePrologueDirector prologue = gameObject.AddComponent<HousePrologueDirector>();
            Transform targetPlayer = player;
            if (targetPlayer == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) targetPlayer = p.transform;
            }

            prologue.BeginPrologue(targetPlayer, () =>
            {
                // Called when player falls asleep and nocslips: generate Backrooms and wake up!
                Generate();
            });
        }
        else
        {
            Generate();
        }
    }

    [ContextMenu("Regenerate")]
    public void Generate()
    {
        if (seed != 0) Random.InitState(seed);

        if (villain != null)
        {
            NavMeshAgent villainAgent = villain.GetComponent<NavMeshAgent>();
            if (villainAgent != null) villainAgent.enabled = false;
            villain.SetActive(false);
        }

        if (root != null) Destroy(root.gameObject);
        root = new GameObject("GeneratedMap").transform;
        root.SetParent(surface != null ? surface.transform : transform, false);

        floor = new bool[gridWidth, gridHeight];
        roomCell = new bool[gridWidth, gridHeight];
        rooms.Clear();
        tableTopSpots.Clear();
        reservedExitCell = new Vector2Int(-99, -99);

        PlaceRooms();
        ConnectRooms();
        CarveBranches();
        ReserveExitSpot();
        SelectVentSlots();
        BuildFloorsAndCeilings();
        BuildWalls();
        BuildEnclosedRoomsAndDoors();
        BuildPillars();
        BuildLights();

        CombineGeneratedMeshes();

        BuildDecorations();
        BuildWallDecals();
        BuildAlmondWaterPickups();

        if (surface != null)
        {
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }

        PlaceActors();
        EnsureAmbience();
        EnsureAtmosphere();
        EnsureBlackoutEvents();
        EnsureDoorEventManager();
        EnsureParticles();
        EnsureVHSGlitch();
        EnsureGameOverSystem();
        EnsureVictorySystem();
        EnsureSurvivalDialogueHUD();
        EnsurePlayerMotion();
        EnsurePlayerStaminaHUD();
        EnsurePlayerVentController();
        EnsurePlayerLockerController();
        EnsureVentHUD();
        EnsureMonsterCompositionAudio();
        EnsurePlayerSanity();
        EnsurePlayerInventory();
        EnsurePlayerDrinkController();
        EnsureInventoryHUD();
        EnsureHallucinationManager();
    }

    // ---------------- Layout ----------------

    /// Normal rooms use roomSizeMin..roomSizeMax. A few rare big halls
    /// use roomSizeMax..bigRoomSizeMax, capped by maxBigRooms.
    Vector2Int RollRoomSize(bool big)
    {
        if (!big)
        {
            return new Vector2Int(
                Random.Range(roomSizeMin.x, roomSizeMax.x + 1),
                Random.Range(roomSizeMin.y, roomSizeMax.y + 1));
        }

        int bx = Mathf.Max(roomSizeMax.x, bigRoomSizeMax.x);
        int bz = Mathf.Max(roomSizeMax.y, bigRoomSizeMax.y);
        return new Vector2Int(
            Random.Range(roomSizeMax.x, bx + 1),
            Random.Range(roomSizeMax.y, bz + 1));
    }

    void PlaceRooms()
    {
        int attempts = 0;
        int bigCount = 0;

        while (rooms.Count < roomCount && attempts < roomCount * 40)
        {
            attempts++;

            bool big = bigCount < maxBigRooms && Random.value < bigRoomChance;
            Vector2Int size = RollRoomSize(big);
            int w = size.x;
            int h = size.y;
            if (gridWidth - w - 1 <= 1 || gridHeight - h - 1 <= 1) continue;

            int x = Random.Range(1, gridWidth - w - 1);
            int z = Random.Range(1, gridHeight - h - 1);
            RectInt r = new RectInt(x, z, w, h);

            bool ok = true;
            foreach (RectInt o in rooms)
            {
                RectInt padded = new RectInt(o.x - 1, o.y - 1, o.width + 2, o.height + 2);
                if (padded.Overlaps(r)) { ok = false; break; }
            }
            if (!ok) continue;

            rooms.Add(r);
            if (big) bigCount++;

            for (int i = x; i < x + w; i++)
                for (int j = z; j < z + h; j++)
                {
                    floor[i, j] = true;
                    roomCell[i, j] = true;
                }
        }
    }

    Vector2Int RoomCenter(RectInt r)
    {
        return new Vector2Int(r.x + r.width / 2, r.y + r.height / 2);
    }

    void ConnectRooms()
    {
        for (int i = 1; i < rooms.Count; i++)
            Carve(RoomCenter(rooms[i - 1]), RoomCenter(rooms[i]));

        for (int e = 0; e < extraConnections && rooms.Count > 2; e++)
        {
            int a = Random.Range(0, rooms.Count);
            int b = Random.Range(0, rooms.Count);
            if (a != b) Carve(RoomCenter(rooms[a]), RoomCenter(rooms[b]));
        }
    }

    void Carve(Vector2Int a, Vector2Int b)
    {
        int x = a.x, z = a.y;
        while (x != b.x) { CarveCell(x, z); x += (int)Mathf.Sign(b.x - x); }
        while (z != b.y) { CarveCell(x, z); z += (int)Mathf.Sign(b.y - z); }
        CarveCell(x, z);
    }

    void CarveCell(int x, int z)
    {
        for (int i = 0; i < corridorWidth; i++)
            for (int j = 0; j < corridorWidth; j++)
            {
                int cx = x + i, cz = z + j;
                if (cx >= 1 && cz >= 1 && cx < gridWidth - 1 && cz < gridHeight - 1)
                    floor[cx, cz] = true;
            }
    }

    /// Forks short corridors off the existing network, so the level is a web of
    /// narrow ways instead of a handful of rooms joined by a few long halls.
    /// Every branch starts on a walkable cell, so it is always reachable.
    void CarveBranches()
    {
        if (deadEndBranches <= 0) return;

        int[] dx = { 0, 0, 1, -1 };
        int[] dz = { 1, -1, 0, 0 };
        int maxLen = Mathf.Max(2, deadEndLength);

        for (int b = 0; b < deadEndBranches; b++)
        {
            // start somewhere that is already walkable
            int sx = 0, sz = 0;
            bool found = false;
            for (int tries = 0; tries < 40 && !found; tries++)
            {
                sx = Random.Range(1, gridWidth - 1);
                sz = Random.Range(1, gridHeight - 1);
                found = floor[sx, sz];
            }
            if (!found) continue;

            int dir = Random.Range(0, 4);
            int len = Random.Range(2, maxLen + 1);
            int x = sx, z = sz;

            for (int step = 0; step < len; step++)
            {
                x += dx[dir];
                z += dz[dir];
                if (x < 1 || z < 1 || x >= gridWidth - 1 || z >= gridHeight - 1) break;
                CarveCell(x, z);
            }
        }
    }

    bool IsFloor(int x, int z)
    {
        return x >= 0 && z >= 0 && x < gridWidth && z < gridHeight && floor[x, z];
    }

    bool IsInterior(int x, int z)
    {
        return IsFloor(x + 1, z) && IsFloor(x - 1, z) && IsFloor(x, z + 1) && IsFloor(x, z - 1);
    }

    Vector3 CellCenter(int x, int z)
    {
        return new Vector3((x + 0.5f) * tileSize, 0f, (z + 0.5f) * tileSize);
    }

    // ---------------- Geometry ----------------

    void BuildFloorsAndCeilings()
    {
        for (int z = 0; z < gridHeight; z++)
        {
            int x = 0;
            while (x < gridWidth)
            {
                if (!floor[x, z]) { x++; continue; }
                int start = x;
                while (x < gridWidth && floor[x, z]) x++;
                int len = x - start;

                // Floor: top surface at y = 0
                if (floorPrefab != null)
                {
                    for (int i = start; i < start + len; i++)
                        Spawn(floorPrefab, CellCenter(i, z), 0f,
                            new Vector3(tileSize, 0f, tileSize), 0f, true, false);
                }
                else
                {
                    Box("Floor", new Vector3((start + len / 2f) * tileSize, -0.1f, (z + 0.5f) * tileSize),
                        new Vector3(len * tileSize, 0.2f, tileSize), floorMaterial, false);
                }

                // Ceiling: bottom surface at y = wallHeight (ignored by the NavMesh)
                if (ceilingPrefab != null)
                {
                    for (int i = start; i < start + len; i++)
                    {
                        GameObject c = Spawn(ceilingPrefab, CellCenter(i, z), 0f,
                            new Vector3(tileSize, 0f, tileSize), wallHeight, false, false);
                        IgnoreForNavMesh(c);
                    }
                }
                else
                {
                    Box("Ceiling", new Vector3((start + len / 2f) * tileSize, wallHeight + 0.1f, (z + 0.5f) * tileSize),
                        new Vector3(len * tileSize, 0.2f, tileSize), ceilingMaterial, true);
                }
            }
        }
    }

    void BuildWalls()
    {
        int[] dx = { 0, 0, 1, -1 };
        int[] dz = { 1, -1, 0, 0 };
        float[] yaws = { 0f, 180f, 90f, 270f };

        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                if (!floor[x, z]) continue;

                for (int d = 0; d < 4; d++)
                {
                    if (IsFloor(x + dx[d], z + dz[d])) continue;

                    Vector3 dir = new Vector3(dx[d], 0, dz[d]);
                    Vector3 edge = CellCenter(x, z) + dir * (tileSize / 2f);
                    bool alongX = (dx[d] == 0);

                    long edgeKey = ((long)x << 32) | ((long)z << 16) | (long)d;
                    bool hasVent = enableWallVents && ventEdges.Contains(edgeKey);

                    if (hasVent)
                    {
                        BuildWallWithVentHole(edge, dir, alongX);
                        Vector3 inward = -dir;
                        Vector3 innerFacePos = edge + inward * (wallThickness / 2f);
                        VentHideSpot.Create(root, innerFacePos, dir, wallThickness, 1.25f, ventMetalMaterial, ventDuctMaterial);
                    }
                    else if (wallPrefab != null)
                    {
                        float yaw = yaws[d] + (flipWallFacing ? 180f : 0f);
                        Vector3 size = alongX ? new Vector3(tileSize + wallThickness, wallHeight, 0f)
                                              : new Vector3(0f, wallHeight, tileSize + wallThickness);
                        Spawn(wallPrefab, edge, yaw, size, 0f, false, true);
                    }
                    else
                    {
                        Vector3 scale = alongX ? new Vector3(tileSize + wallThickness, wallHeight, wallThickness)
                                               : new Vector3(wallThickness, wallHeight, tileSize + wallThickness);
                        Box("Wall", new Vector3(edge.x, wallHeight / 2f, edge.z), scale, wallMaterial, false);

                        if (baseboardMaterial != null)
                        {
                            Vector3 inward = -dir;
                            Vector3 p = edge + inward * (wallThickness / 2f + 0.02f);
                            Vector3 bs = alongX ? new Vector3(tileSize, 0.15f, 0.04f)
                                                : new Vector3(0.04f, 0.15f, tileSize);
                            Box("Baseboard", new Vector3(p.x, 0.075f, p.z), bs, baseboardMaterial, false);
                        }
                    }
                }
            }
        }
    }

    struct VentCandidate
    {
        public int x, z, d;
        public Vector3 worldPos;
    }

    void SelectVentSlots()
    {
        ventEdges.Clear();
        if (!enableWallVents || maxVents <= 0) return;

        int[] dx = { 0, 0, 1, -1 };
        int[] dz = { 1, -1, 0, 0 };

        Vector2Int spawnCell = rooms.Count > 0 ? RoomCenter(rooms[0]) : new Vector2Int(-99, -99);
        List<VentCandidate> candidates = new List<VentCandidate>();

        for (int x = 1; x < gridWidth - 1; x++)
        {
            for (int z = 1; z < gridHeight - 1; z++)
            {
                if (!floor[x, z]) continue;
                // Avoid right at player spawn
                if (Mathf.Abs(spawnCell.x - x) <= 1 && Mathf.Abs(spawnCell.y - z) <= 1) continue;
                // Avoid exit door area
                if (reservedExitCell.x >= 0 && Mathf.Abs(reservedExitCell.x - x) <= 2 && Mathf.Abs(reservedExitCell.y - z) <= 2) continue;

                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d];
                    int nz = z + dz[d];
                    if (IsFloor(nx, nz)) continue; // Must be a wall

                    Vector3 dir = new Vector3(dx[d], 0, dz[d]);
                    Vector3 edge = CellCenter(x, z) + dir * (tileSize / 2f);

                    candidates.Add(new VentCandidate { x = x, z = z, d = d, worldPos = edge });
                }
            }
        }

        if (candidates.Count == 0) return;

        // Shuffle candidates using Fisher-Yates
        for (int i = 0; i < candidates.Count; i++)
        {
            int r = Random.Range(i, candidates.Count);
            var tmp = candidates[i];
            candidates[i] = candidates[r];
            candidates[r] = tmp;
        }

        List<Vector3> placedPositions = new List<Vector3>();
        float minSeparationSqr = (tileSize * 3.5f) * (tileSize * 3.5f); // Keep vents well spaced (~8-10m apart)

        foreach (var c in candidates)
        {
            if (ventEdges.Count >= maxVents) break;
            if (Random.value > ventChance && ventEdges.Count >= 3) continue;

            // Check distance to already placed vents
            bool tooClose = false;
            for (int p = 0; p < placedPositions.Count; p++)
            {
                if ((placedPositions[p] - c.worldPos).sqrMagnitude < minSeparationSqr)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            long key = ((long)c.x << 32) | ((long)c.z << 16) | (long)c.d;
            ventEdges.Add(key);
            placedPositions.Add(c.worldPos);
        }
    }

    void BuildWallWithVentHole(Vector3 edge, Vector3 dir, bool alongX)
    {
        float L = tileSize + wallThickness;
        float H = wallHeight;
        float T = wallThickness;
        float holeW = VentHideSpot.HoleWidth; // 0.90m
        float holeBot = VentHideSpot.HoleBottom; // 0.12m
        float holeTop = VentHideSpot.HoleTop; // 0.87m
        float holeH = holeTop - holeBot; // 0.75m
        float sideW = (L - holeW) * 0.5f;

        if (alongX)
        {
            // 1. Top wall above hole
            float topH = H - holeTop;
            Box("Wall", new Vector3(edge.x, holeTop + topH * 0.5f, edge.z),
                new Vector3(L, topH, T), wallMaterial, false);

            // 2. Bottom wall under hole
            Box("Wall", new Vector3(edge.x, holeBot * 0.5f, edge.z),
                new Vector3(L, holeBot, T), wallMaterial, false);

            // 3. Left side of hole
            float leftX = edge.x - holeW * 0.5f - sideW * 0.5f;
            Box("Wall", new Vector3(leftX, holeBot + holeH * 0.5f, edge.z),
                new Vector3(sideW, holeH, T), wallMaterial, false);

            // 4. Right side of hole
            float rightX = edge.x + holeW * 0.5f + sideW * 0.5f;
            Box("Wall", new Vector3(rightX, holeBot + holeH * 0.5f, edge.z),
                new Vector3(sideW, holeH, T), wallMaterial, false);

            if (baseboardMaterial != null)
            {
                Vector3 inward = -dir;
                Vector3 p = edge + inward * (wallThickness / 2f + 0.02f);
                float bbSideW = (tileSize - holeW) * 0.5f;
                if (bbSideW > 0.05f)
                {
                    float bbLeftX = p.x - holeW * 0.5f - bbSideW * 0.5f;
                    float bbRightX = p.x + holeW * 0.5f + bbSideW * 0.5f;
                    Box("Baseboard", new Vector3(bbLeftX, 0.075f, p.z), new Vector3(bbSideW, 0.15f, 0.04f), baseboardMaterial, false);
                    Box("Baseboard", new Vector3(bbRightX, 0.075f, p.z), new Vector3(bbSideW, 0.15f, 0.04f), baseboardMaterial, false);
                }
            }
        }
        else
        {
            // 1. Top wall above hole
            float topH = H - holeTop;
            Box("Wall", new Vector3(edge.x, holeTop + topH * 0.5f, edge.z),
                new Vector3(T, topH, L), wallMaterial, false);

            // 2. Bottom wall under hole
            Box("Wall", new Vector3(edge.x, holeBot * 0.5f, edge.z),
                new Vector3(T, holeBot, L), wallMaterial, false);

            // 3. Left/back side of hole along Z
            float leftZ = edge.z - holeW * 0.5f - sideW * 0.5f;
            Box("Wall", new Vector3(edge.x, holeBot + holeH * 0.5f, leftZ),
                new Vector3(T, holeH, sideW), wallMaterial, false);

            // 4. Right/forward side of hole along Z
            float rightZ = edge.z + holeW * 0.5f + sideW * 0.5f;
            Box("Wall", new Vector3(edge.x, holeBot + holeH * 0.5f, rightZ),
                new Vector3(T, holeH, sideW), wallMaterial, false);

            if (baseboardMaterial != null)
            {
                Vector3 inward = -dir;
                Vector3 p = edge + inward * (wallThickness / 2f + 0.02f);
                float bbSideW = (tileSize - holeW) * 0.5f;
                if (bbSideW > 0.05f)
                {
                    float bbLeftZ = p.z - holeW * 0.5f - bbSideW * 0.5f;
                    float bbRightZ = p.z + holeW * 0.5f + bbSideW * 0.5f;
                    Box("Baseboard", new Vector3(p.x, 0.075f, bbLeftZ), new Vector3(0.04f, 0.15f, bbSideW), baseboardMaterial, false);
                    Box("Baseboard", new Vector3(p.x, 0.075f, bbRightZ), new Vector3(0.04f, 0.15f, bbSideW), baseboardMaterial, false);
                }
            }
        }
    }

    void BuildPillars()
    {
        if (pillarSpacing <= 0) return;

        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                if (!roomCell[x, z] || !IsInterior(x, z)) continue;
                if (x % pillarSpacing != 0 || z % pillarSpacing != 0) continue;
                if (Random.value > pillarChance) continue;
                if (NearRoomCenter(x, z)) continue;

                Vector3 c = CellCenter(x, z);
                if (pillarPrefab != null)
                    Spawn(pillarPrefab, c, 0f, new Vector3(0f, wallHeight, 0f), 0f, false, false);
                else
                    Box("Pillar", new Vector3(c.x, wallHeight / 2f, c.z),
                        new Vector3(0.8f, wallHeight, 0.8f),
                        pillarMaterial != null ? pillarMaterial : wallMaterial, false);
            }
        }
    }

    bool NearRoomCenter(int x, int z)
    {
        foreach (RectInt r in rooms)
        {
            Vector2Int c = RoomCenter(r);
            if (Mathf.Abs(c.x - x) <= 1 && Mathf.Abs(c.y - z) <= 1) return true;
        }
        return false;
    }

    void BuildLights()
    {
        if (lightEvery <= 0) return;

        // Gather the cells that get a fixture first, so the point lights can be
        // thinned out evenly instead of piling up at the start of the scan.
        List<Vector3> cells = new List<Vector3>();
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                if (!floor[x, z] || x % lightEvery != 0 || z % lightEvery != 0) continue;
                cells.Add(CellCenter(x, z));
            }
        }

        if (cells.Count == 0) return;

        if (lightPanelMaterial != null || panelLights)
        {
            Material panelMaterial = GetPanelMaterial();

            foreach (Vector3 c in cells)
                Box("LightPanel", new Vector3(c.x, wallHeight - 0.02f, c.z),
                    new Vector3(1.2f, 0.04f, 0.6f), panelMaterial, true);
        }

        if (lightPrefab == null && !panelLights) return;

        int effectiveMaxLights = maxPanelLights;
        if (GameSettings.QualityLevel == 0) effectiveMaxLights = Mathf.Min(maxPanelLights, 8);
        else if (GameSettings.QualityLevel == 1) effectiveMaxLights = Mathf.Min(maxPanelLights, 16);

        int stride = Mathf.Max(1, Mathf.CeilToInt(cells.Count / (float)Mathf.Max(1, effectiveMaxLights)));

        for (int i = 0; i < cells.Count; i += stride)
        {
            Vector3 c = cells[i];

            if (lightPrefab != null)
            {
                if (Random.value >= lightPrefabChance) continue;

                GameObject l = Instantiate(lightPrefab, root);
                l.transform.position = new Vector3(c.x, wallHeight - 0.4f, c.z);
                continue;
            }

            SpawnPanelLight(c);
        }
    }

    /// <summary>
    /// The material for the glowing ceiling panels. Falls back to a runtime
    /// emissive material when the scene has none assigned, so the level is never
    /// left without visible fixtures.
    /// </summary>
    Material GetPanelMaterial()
    {
        if (lightPanelMaterial != null) return lightPanelMaterial;

        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) return null;

        lightPanelMaterial = new Material(lit);
        lightPanelMaterial.name = "Light Panel (auto)";
        lightPanelMaterial.SetColor("_BaseColor", new Color(0.95f, 0.93f, 0.85f));
        lightPanelMaterial.SetColor("_EmissionColor", new Color(2.4f, 2.3f, 2.0f));
        lightPanelMaterial.EnableKeyword("_EMISSION");
        lightPanelMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        lightPanelMaterial.SetFloat("_Smoothness", 0.4f);

        return lightPanelMaterial;
    }

    /// <summary>
    /// Shadowless point light tucked just under a ceiling panel. Shadows from
    /// dozens of lights are far too expensive on mobile, so the fixtures only
    /// provide the falloff and the atmosphere handles the mood.
    /// </summary>
    void SpawnPanelLight(Vector3 cellCenter)
    {
        GameObject go = new GameObject("Panel Light");
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3(cellCenter.x, wallHeight - 0.35f, cellCenter.z);

        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = panelLightColor;
        l.intensity = panelLightIntensity * Random.Range(0.9f, 1.05f);
        l.range = panelLightRange;
        l.shadows = LightShadows.None;
        l.bounceIntensity = 0f;

        if (Random.value < panelFlickerChance)
            go.AddComponent<FlickeringLight>();
    }

    // ---------------- Decorations ----------------

    /// Scatters the assigned prop prefabs over the walkable cells. Most land
    /// normally, but a percentage are "clapped" onto walls, ceilings or mid-air
    /// at deliberate-looking angles, which is where the wrongness comes from.
    void BuildDecorations()
    {
        bool haveProps = decorationPrefabs != null && decorationPrefabs.Length > 0;
        bool haveWallProps = wallDecorationPrefabs != null && wallDecorationPrefabs.Length > 0;
        if ((!haveProps && !haveWallProps) || maxDecorations <= 0 || decorationChance <= 0f) return;

        int[] dx = { 0, 0, 1, -1 };
        int[] dz = { 1, -1, 0, 0 };

        Vector2Int spawnCell = rooms.Count > 0 ? RoomCenter(rooms[0]) : new Vector2Int(-99, -99);

        // Collect every usable cell, then shuffle, so the props spread over the
        // whole level instead of clumping in the top-left corner of the grid.
        List<Vector2Int> cells = new List<Vector2Int>();
        for (int x = 1; x < gridWidth - 1; x++)
        {
            for (int z = 1; z < gridHeight - 1; z++)
            {
                if (!floor[x, z]) continue;
                if (!decorateCorridors && !roomCell[x, z]) continue;
                if (Mathf.Abs(spawnCell.x - x) <= 1 && Mathf.Abs(spawnCell.y - z) <= 1) continue;
                // Keep the exit door area completely clear of furniture props
                if (reservedExitCell.x >= 0 && Mathf.Abs(reservedExitCell.x - x) <= 2 && Mathf.Abs(reservedExitCell.y - z) <= 2) continue;
                cells.Add(new Vector2Int(x, z));
            }
        }

        for (int i = 0; i < cells.Count; i++)
        {
            int j = Random.Range(i, cells.Count);
            Vector2Int tmp = cells[i];
            cells[i] = cells[j];
            cells[j] = tmp;
        }

        int placed = 0;
        foreach (Vector2Int c in cells)
        {
            if (placed >= maxDecorations) break;
            if (Random.value > decorationChance) continue;

            // Needs at least one wall to lean on or hang things from.
            int side = PickWallSide(c.x, c.y, dx, dz);
            if (side < 0) continue;

            bool useWallSet = haveWallProps && (!haveProps || Random.value < 0.4f);

            GameObject prefab = useWallSet
                ? wallDecorationPrefabs[Random.Range(0, wallDecorationPrefabs.Length)]
                : decorationPrefabs[Random.Range(0, decorationPrefabs.Length)];

            if (prefab == null) continue;
            // Never spawn raw multi-model GLB asset packs if accidentally dragged into prefab array
            if (prefab.name.ToLowerInvariant().Contains("furniture_pack")) continue;

            SpawnDecoration(prefab, c, side, useWallSet, dx, dz);
            placed++;
        }
    }

    int PickWallSide(int x, int z, int[] dx, int[] dz)
    {
        int start = Random.Range(0, 4);
        for (int i = 0; i < 4; i++)
        {
            int d = (start + i) % 4;
            if (!IsFloor(x + dx[d], z + dz[d])) return d;
        }
        return -1;
    }

    void SpawnDecoration(GameObject prefab, Vector2Int cell, int side, bool forceWallItem, int[] dx, int[] dz)
    {
        if (prefab == null) return;
        if (prefab.name.ToLowerInvariant().Contains("furniture_pack")) return;

        // Clean wrapper so the prop pivot is always centered regardless of original 3D model pivot offsets
        GameObject wrapper = new GameObject(prefab.name);
        wrapper.transform.SetParent(root, false);
        wrapper.transform.position = Vector3.zero;
        wrapper.transform.rotation = Quaternion.identity;
        wrapper.transform.localScale = Vector3.one;

        GameObject g = Instantiate(prefab, wrapper.transform, false);
        g.transform.localPosition = Vector3.zero;
        g.transform.localRotation = Quaternion.identity;

        // Shift inner object so that the wrapper pivot is exactly at the bottom-center of the visual mesh
        Bounds b = GetBounds(g);
        Vector3 bottomCenter = new Vector3(b.center.x, b.min.y, b.center.z);
        g.transform.localPosition -= bottomCenter;

        // Compute normalized human-scale multiplier to make chairs, sofas, tables believable
        float normScale = ComputePropScaleMultiplier(prefab.name, b, forceWallItem);
        float variance = Random.Range(decorationScaleRange.x, decorationScaleRange.y);
        wrapper.transform.localScale = Vector3.one * (normScale * variance);

        bool wallItem = forceWallItem || IsFlatWallProp(wrapper);
        bool weird = Random.value < weirdChance;

        Vector3 dir = new Vector3(dx[side], 0f, dz[side]);   // points out at the wall
        Vector3 center = CellCenter(cell.x, cell.y);

        if (wallItem)
        {
            if (weird) MountWeird(wrapper, center, dir);
            else MountFlat(wrapper, center, dir);
        }
        else if (!weird)
        {
            PlaceOnFloor(wrapper, center, dir, 0f);
            string lower = prefab.name.ToLowerInvariant();
            if (lower.Contains("table") || lower.Contains("desk"))
            {
                Bounds wb = GetBounds(wrapper);
                tableTopSpots.Add(new Vector3(wb.center.x, wb.max.y + 0.02f, wb.center.z));
            }
        }
        else
        {
            int mode = Random.Range(0, 4);
            if (mode == 0) MountWeird(wrapper, center, dir);        // chair glued to the wall
            else if (mode == 1) HangFromCeiling(wrapper, center, dir);
            else if (mode == 2) FloatInAir(wrapper, center, dir);
            else PlaceOnFloor(wrapper, center, dir, Random.Range(0.15f, 0.45f)); // half sunk into the carpet
        }

        FinishDecoration(wrapper);
    }

    /// <summary>
    /// Computes a uniform scaling multiplier to normalize any decor prefab to realistic Backrooms human scale (meters).
    /// Prevents huge chairs (2m+), microscopic sofas (0.2m), or oversized tables that block hallways.
    /// </summary>
    float ComputePropScaleMultiplier(string propName, Bounds bounds, bool isWallItem)
    {
        if (!normalizeDecorScale) return 1.0f;

        float currentHeight = bounds.size.y;
        float currentWidth = bounds.size.x;
        float currentDepth = bounds.size.z;
        float maxHoriz = Mathf.Max(currentWidth, currentDepth);
        float maxDim = Mathf.Max(currentHeight, maxHoriz);

        if (currentHeight <= 0.001f || maxDim <= 0.001f)
            return 1.0f;

        string lower = propName.ToLowerInvariant();
        float multiplier = 1.0f;

        if (lower.Contains("chair") || lower.Contains("seat") || lower.Contains("stool"))
        {
            // Human-sized office / arm / lounge chairs: ~0.90m - 0.95m tall (backrest)
            float targetHeight = 0.92f;
            multiplier = targetHeight / currentHeight;

            // Ensure chair width/depth stays within 0.55m - 0.80m
            float scaledHoriz = maxHoriz * multiplier;
            if (scaledHoriz > 0.85f)
                multiplier *= (0.80f / scaledHoriz);
        }
        else if (lower.Contains("canape") || lower.Contains("canapé") || lower.Contains("sofa") || lower.Contains("couch"))
        {
            // Human-sized sofa: ~0.82m backrest height, ~1.85m - 2.05m length
            float targetHeight = 0.82f;
            multiplier = targetHeight / currentHeight;

            float scaledLength = maxHoriz * multiplier;
            // Keep sofa length between 1.6m and 2.1m so it fits corridors and rooms believably
            if (scaledLength > 2.15f)
                multiplier *= (2.05f / scaledLength);
            else if (scaledLength < 1.4f && maxHoriz > 0.001f)
                multiplier = 1.85f / maxHoriz;
        }
        else if (lower.Contains("table") || lower.Contains("desk"))
        {
            // Standard desk height is ~0.74m - 0.76m
            float targetHeight = 0.75f;
            multiplier = targetHeight / currentHeight;

            float scaledLength = maxHoriz * multiplier;
            // Cap table length at 1.8m so it leaves ample walking room in 2.5m - 3.0m corridors
            if (scaledLength > 1.85f)
                multiplier *= (1.80f / scaledLength);
        }
        else if (lower.Contains("tv") || lower.Contains("television") || lower.Contains("monitor"))
        {
            // CRT TV / monitor: ~0.55m height
            float targetHeight = 0.55f;
            multiplier = targetHeight / currentHeight;
        }
        else if (lower.Contains("suitcase") || lower.Contains("valise") || lower.Contains("luggage") || lower.Contains("box"))
        {
            // Briefcase / luggage: max dimension ~0.45m - 0.55m
            multiplier = 0.50f / maxDim;
        }
        else if (lower.Contains("sign") || lower.Contains("poster") || lower.Contains("picture") || isWallItem)
        {
            // Wall / road sign: target height ~1.95m for road signs, ~1.10m for wall posters
            float targetHeight = lower.Contains("road") ? 1.95f : 1.10f;
            multiplier = targetHeight / currentHeight;
        }
        else
        {
            // General unknown prop fallback:
            // Ensure height doesn't exceed 1.2m and horizontal doesn't exceed 1.6m
            if (currentHeight > 1.3f)
                multiplier = 1.0f / currentHeight;
            else if (maxHoriz > 1.8f)
                multiplier = 1.5f / maxHoriz;
            else if (maxDim < 0.3f)
                multiplier = 0.6f / maxDim;
        }

        return multiplier;
    }

    /// <summary>
    /// Checks whether a prop should be mounted on a wall.
    /// Distinguishes true wall decor (signs, posters) from floor furniture (chairs, sofas, suitcases).
    /// </summary>
    bool IsFlatWallProp(GameObject g)
    {
        string lower = g.name.ToLowerInvariant();
        if (lower.Contains("poster") || lower.Contains("sign") || lower.Contains("picture") || lower.Contains("frame") || lower.Contains("board"))
            return true;

        // Furniture props must never be automatically treated as flat posters
        if (lower.Contains("chair") || lower.Contains("sofa") || lower.Contains("canape") || lower.Contains("canapé") ||
            lower.Contains("table") || lower.Contains("desk") || lower.Contains("tv") || lower.Contains("suitcase"))
            return false;

        Bounds b = GetBounds(g);
        float min = Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z));
        float max = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        // Razor-thin items (thickness < 15cm) with high aspect ratio
        return min < 0.15f && max > 0.4f && (min / max) < 0.18f;
    }

    /// Normal wall mounting: flush, upright, at believable picture height, with
    /// just a little crookedness so it still looks hand-hung.
    void MountFlat(GameObject g, Vector3 cellCenter, Vector3 dir)
    {
        float roll = Random.value < 0.5f ? 0f : Random.Range(-3f, 3f);
        PlaceOnWall(g, cellCenter, dir, roll, 0f, 1.35f, 1.85f, 0f);
    }

    /// The impossible version: exact quarter turns, upside down, half sunk into
    /// the wall, or a chair standing straight out of it.
    void MountWeird(GameObject g, Vector3 cellCenter, Vector3 dir)
    {
        float roll = Random.value < 0.5f
            ? 90f * Random.Range(1, 4)          // exact sideways / upside down
            : Random.Range(-180f, 180f);        // or any angle at all

        float pitch = 0f;
        if (!IsFlatWallProp(g) && Random.value < 0.6f)
            pitch = Random.value < 0.5f ? 90f : -90f;  // legs sticking out of the wall

        float embed = Random.Range(0f, 0.22f);          // clapped into the wall
        PlaceOnWall(g, cellCenter, dir, roll, pitch, 0.3f, wallHeight - 0.3f, embed);
    }

    void PlaceOnWall(GameObject g, Vector3 cellCenter, Vector3 dir,
                     float roll, float pitch, float minCenterY, float maxCenterY, float embed)
    {
        // +Z of the prop faces into the room, so its back lands on the wall.
        Quaternion faceRoom = Quaternion.LookRotation(-dir, Vector3.up);
        g.transform.localRotation = faceRoom * Quaternion.Euler(pitch, 0f, roll);

        Bounds b = GetBounds(g);

        // Walls are grid-aligned, so the world-space extent along dir is exact.
        float halfAlong = (Mathf.Abs(dir.x) > 0.5f ? b.size.x : b.size.z) * 0.5f;

        // Never bury a prop so deep that nothing pokes out of the wall.
        embed = Mathf.Min(embed, halfAlong * 0.9f);

        Vector3 wallPlane = cellCenter + dir * (tileSize * 0.5f);
        Vector3 target = wallPlane - dir * (halfAlong - embed);
        target.y = Random.Range(minCenterY, maxCenterY);

        g.transform.position += target - b.center;
    }

    void PlaceOnFloor(GameObject g, Vector3 cellCenter, Vector3 dir, float sink)
    {
        float yaw = Quaternion.LookRotation(-dir, Vector3.up).eulerAngles.y + Random.Range(-35f, 35f);
        float tilt = sink > 0f ? Random.Range(4f, 14f)
                               : (Random.value < 0.2f ? Random.Range(-6f, 6f) : 0f);
        g.transform.localRotation = Quaternion.Euler(tilt, yaw, tilt * 0.6f);

        // Most props get shoved up against the wall, some stand out in the open.
        float lean = Random.value < 0.7f ? Random.Range(0.55f, 0.8f) : Random.Range(0f, 0.3f);
        Vector3 lateral = new Vector3(-dir.z, 0f, dir.x);

        Vector3 target = cellCenter
                       + dir * (tileSize * 0.5f * lean)
                       + lateral * Random.Range(-0.45f, 0.45f);

        Bounds b = GetBounds(g);
        target.y = b.center.y - b.min.y - sink;     // bottom on the floor (or buried by sink)

        g.transform.position += target - b.center;
    }

    void HangFromCeiling(GameObject g, Vector3 cellCenter, Vector3 dir)
    {
        g.transform.localRotation = Quaternion.Euler(180f, Random.Range(0f, 360f), 0f);

        Vector3 target = cellCenter + dir * (tileSize * 0.5f * Random.Range(0.3f, 0.7f));

        Bounds b = GetBounds(g);
        target.y = wallHeight + Random.Range(-0.05f, 0.05f) - b.max.y + b.center.y;

        g.transform.position += target - b.center;
    }

    void FloatInAir(GameObject g, Vector3 cellCenter, Vector3 dir)
    {
        g.transform.localRotation = Quaternion.Euler(
            Random.Range(-35f, 35f), Random.Range(0f, 360f), Random.Range(-35f, 35f));

        Vector3 target = cellCenter + new Vector3(Random.Range(-0.8f, 0.8f), 0f, Random.Range(-0.8f, 0.8f));
        target.y = Random.Range(0.5f, wallHeight - 0.4f);

        Bounds b = GetBounds(g);
        g.transform.position += target - b.center;
    }

    void FinishDecoration(GameObject g)
    {
        EnsurePropColliders(g);

        foreach (Transform t in g.GetComponentsInChildren<Transform>())
        {
            t.gameObject.isStatic = true;

            // Props must never punch holes in the baked NavMesh - a chair in a
            // corridor would otherwise carve away the walkable area.
            if (t.GetComponent<Collider>() == null && t.GetComponent<Renderer>() == null) continue;
            if (t.GetComponent<NavMeshModifier>() != null) continue;

            NavMeshModifier mod = t.gameObject.AddComponent<NavMeshModifier>();
            mod.ignoreFromBuild = true;
        }
    }

    /// Ensures the prop has solid colliders, preventing the player from clipping through furniture
    /// while strictly avoiding BoxColliders on negatively scaled transforms (which PhysX rejects).
    void EnsurePropColliders(GameObject g)
    {
        // 1. Guard against any BoxCollider on a negatively scaled child transform (common in mirrored 3D models)
        BoxCollider[] boxes = g.GetComponentsInChildren<BoxCollider>(true);
        foreach (BoxCollider box in boxes)
        {
            Vector3 ls = box.transform.lossyScale;
            if (ls.x < 0f || ls.y < 0f || ls.z < 0f)
            {
                MeshFilter mf = box.GetComponent<MeshFilter>();
                Mesh sharedMesh = mf != null ? mf.sharedMesh : null;
                Destroy(box);
                if (sharedMesh != null)
                {
                    MeshCollider mc = box.gameObject.AddComponent<MeshCollider>();
                    mc.convex = true;
                    mc.sharedMesh = sharedMesh;
                }
            }
        }

        // 2. If the prop already has at least one valid collider, nothing more needed
        Collider[] existing = g.GetComponentsInChildren<Collider>();
        if (existing != null && existing.Length > 0) return;

        // 3. Otherwise add a single clean BoxCollider to the root wrapper
        Renderer[] renderers = g.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0) return;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        BoxCollider bc = g.AddComponent<BoxCollider>();
        bc.center = g.transform.InverseTransformPoint(b.center);
        Vector3 worldSize = b.size;
        Vector3 scale = g.transform.lossyScale;
        bc.size = new Vector3(
            scale.x != 0 ? Mathf.Abs(worldSize.x / scale.x) : worldSize.x,
            scale.y != 0 ? Mathf.Abs(worldSize.y / scale.y) : worldSize.y,
            scale.z != 0 ? Mathf.Abs(worldSize.z / scale.z) : worldSize.z
        );
    }

    // ---------------- Wall Decals (Horror Markings) ----------------

    struct WallDecalSlot
    {
        public int x;
        public int z;
        public int d;
        public bool isDeadEnd;
    }

    Material[] GetDecalMaterials()
    {
        if (customDecalMaterials != null && customDecalMaterials.Length > 0)
        {
            List<Material> valid = new List<Material>();
            for (int i = 0; i < customDecalMaterials.Length; i++)
            {
                if (customDecalMaterials[i] != null) valid.Add(customDecalMaterials[i]);
            }
            if (valid.Count > 0) return valid.ToArray();
        }

        Material[] loaded = Resources.LoadAll<Material>("Decals");
        if (loaded != null && loaded.Length > 0)
            return loaded;

        return null;
    }

    void BuildWallDecals()
    {
        if (!enableWallDecals || maxWallDecals <= 0) return;

        Material[] decalMats = GetDecalMaterials();
        if (decalMats == null || decalMats.Length == 0) return;

        int[] dx = { 0, 0, 1, -1 };
        int[] dz = { 1, -1, 0, 0 };

        List<WallDecalSlot> slots = new List<WallDecalSlot>();

        for (int x = 1; x < gridWidth - 1; x++)
        {
            for (int z = 1; z < gridHeight - 1; z++)
            {
                if (!floor[x, z]) continue;

                // Check dead-end status (1 or fewer walkable floor neighbors)
                int floorNeighbors = 0;
                for (int d = 0; d < 4; d++)
                {
                    if (IsFloor(x + dx[d], z + dz[d])) floorNeighbors++;
                }
                bool isDeadEnd = (floorNeighbors <= 1);

                for (int d = 0; d < 4; d++)
                {
                    if (IsFloor(x + dx[d], z + dz[d])) continue;

                    // Exclude exit door cell wall so the exit remains unobscured
                    if (x == reservedExitCell.x && z == reservedExitCell.y) continue;

                    slots.Add(new WallDecalSlot { x = x, z = z, d = d, isDeadEnd = isDeadEnd });
                }
            }
        }

        if (slots.Count == 0) return;

        // Shuffle slots using Fisher-Yates
        for (int i = 0; i < slots.Count; i++)
        {
            int r = Random.Range(i, slots.Count);
            WallDecalSlot temp = slots[i];
            slots[i] = slots[r];
            slots[r] = temp;
        }

        // Shuffle materials to ensure varied distribution
        List<Material> matPool = new List<Material>(decalMats);
        for (int i = 0; i < matPool.Count; i++)
        {
            int r = Random.Range(i, matPool.Count);
            Material temp = matPool[i];
            matPool[i] = matPool[r];
            matPool[r] = temp;
        }

        GameObject decalsParent = new GameObject("WallDecals");
        decalsParent.transform.SetParent(root, false);
        IgnoreForNavMesh(decalsParent);

        int decalsPlaced = 0;
        int matIndex = 0;

        foreach (WallDecalSlot slot in slots)
        {
            if (decalsPlaced >= maxWallDecals) break;

            long edgeKey = ((long)slot.x << 32) | ((long)slot.z << 16) | (long)slot.d;
            if (ventEdges.Contains(edgeKey)) continue;

            float chance = slot.isDeadEnd ? deadEndDecalChance : wallDecalChance;
            if (Random.value > chance) continue;

            Vector3 dir = new Vector3(dx[slot.d], 0, dz[slot.d]);
            Vector3 inward = -dir;
            Vector3 edge = CellCenter(slot.x, slot.z) + dir * (tileSize / 2f);

            // Position flush on inner face (+0.006m into room to prevent z-fighting with the wall)
            Vector3 decalPos = edge + inward * (wallThickness / 2f + 0.006f);

            // Realistic chest / eye level placement (1.25m to 1.75m)
            decalPos.y = Random.Range(1.25f, 1.75f);

            // Lateral offset along the wall plane
            Vector3 lateral = Vector3.Cross(Vector3.up, inward);
            float maxOffset = (tileSize * 0.5f) - 0.45f;
            if (maxOffset > 0.05f)
            {
                decalPos += lateral * Random.Range(-maxOffset, maxOffset);
            }

            Material chosenMat = matPool[matIndex % matPool.Count];
            matIndex++;

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"Decal_{chosenMat.name}_{decalsPlaced}";
            quad.transform.SetParent(decalsParent.transform, false);
            quad.transform.position = decalPos;

            // In Unity Quad, normal is -Vector3.forward. Setting forward = dir makes -forward = inward (faces into room)
            quad.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

            // Eerie subtle hand-painted angle tilt
            quad.transform.Rotate(0f, 0f, Random.Range(-4.5f, 4.5f), Space.Self);

            // Compute aspect ratio dynamically from material's texture
            float aspect = 1f;
            Texture mainTex = chosenMat.mainTexture != null ? chosenMat.mainTexture : (chosenMat.HasProperty("_BaseMap") ? chosenMat.GetTexture("_BaseMap") : null);
            if (mainTex != null && mainTex.height > 0)
            {
                aspect = (float)mainTex.width / mainTex.height;
            }
            else if (chosenMat.name.Contains("dont_look_back") || chosenMat.name.Contains("no_exit") ||
                     chosenMat.name.Contains("it_sees_you") || chosenMat.name.Contains("run") ||
                     chosenMat.name.Contains("help_us"))
            {
                aspect = 2f;
            }

            float baseHeight = Random.Range(0.85f, 1.25f);
            float baseWidth = baseHeight * aspect;
            quad.transform.localScale = new Vector3(baseWidth, baseHeight, 1f);

            // Remove default collider
            Collider col = quad.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = chosenMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            quad.isStatic = true;
            IgnoreForNavMesh(quad);

            decalsPlaced++;
        }
    }

    // ---------------- Helpers ----------------

    void CombineGeneratedMeshes()
    {
        if (root == null) return;

        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>();
        if (filters == null || filters.Length == 0) return;

        const int chunkSizeCells = 10;
        float chunkWorldSize = Mathf.Max(1f, chunkSizeCells * tileSize);

        // Group by Chunk Sector (cx, cz) and Material to allow camera frustum culling
        Dictionary<long, Dictionary<Material, List<CombineInstance>>> chunkBatches =
            new Dictionary<long, Dictionary<Material, List<CombineInstance>>>();
        List<GameObject> toClean = new List<GameObject>();

        Matrix4x4 rootInv = root.worldToLocalMatrix;

        foreach (MeshFilter mf in filters)
        {
            if (mf == null || mf.sharedMesh == null) continue;
            string n = mf.gameObject.name;
            if (n != "Floor" && n != "Ceiling" && n != "Wall" && n != "Pillar" && n != "Baseboard" && n != "LightPanel")
                continue;

            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null) continue;

            Material mat = mr.sharedMaterial;
            Vector3 localPos = rootInv.MultiplyPoint3x4(mf.transform.position);
            int cx = Mathf.FloorToInt(localPos.x / chunkWorldSize);
            int cz = Mathf.FloorToInt(localPos.z / chunkWorldSize);
            long chunkKey = ((long)cx << 32) | (uint)cz;

            if (!chunkBatches.TryGetValue(chunkKey, out var matDict))
            {
                matDict = new Dictionary<Material, List<CombineInstance>>();
                chunkBatches[chunkKey] = matDict;
            }

            if (!matDict.TryGetValue(mat, out List<CombineInstance> list))
            {
                list = new List<CombineInstance>();
                matDict[mat] = list;
            }

            CombineInstance ci = new CombineInstance();
            ci.mesh = mf.sharedMesh;
            ci.transform = rootInv * mf.transform.localToWorldMatrix;
            list.Add(ci);

            toClean.Add(mf.gameObject);
        }

        // Remove redundant individual renderers/filters while keeping colliders for physics & navmesh
        foreach (GameObject go in toClean)
        {
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mr != null) Destroy(mr);
            if (mf != null) Destroy(mf);
        }

        // Create chunk meshes enabling efficient frustum culling per 10x10 cell sector
        foreach (var chunkPair in chunkBatches)
        {
            long chunkKey = chunkPair.Key;
            int cx = (int)(chunkKey >> 32);
            int cz = (int)(chunkKey & 0xFFFFFFFF);

            GameObject chunkRoot = new GameObject($"Chunk_{cx}_{cz}");
            chunkRoot.transform.SetParent(root, false);
            chunkRoot.isStatic = true;

            foreach (var matPair in chunkPair.Value)
            {
                Material mat = matPair.Key;
                List<CombineInstance> instances = matPair.Value;
                if (instances.Count == 0) continue;

                GameObject meshGo = new GameObject($"Mesh_{mat.name}");
                meshGo.transform.SetParent(chunkRoot.transform, false);
                meshGo.isStatic = true;

                MeshFilter mf = meshGo.AddComponent<MeshFilter>();
                MeshRenderer mr = meshGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;

                Mesh combinedMesh = new Mesh();
                combinedMesh.name = $"Chunk_{cx}_{cz}_{mat.name}";
                combinedMesh.indexFormat = IndexFormat.UInt32;
                combinedMesh.CombineMeshes(instances.ToArray(), true, true);
                mf.sharedMesh = combinedMesh;

                IgnoreForNavMesh(meshGo);
            }
        }
    }

    GameObject Box(string name, Vector3 pos, Vector3 scale, Material mat, bool ignoreNav)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(root, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.isStatic = true;

        // Discard redundant physics colliders on non-walkable / purely visual baseboards and lights
        // NOTE: Ceilings KEEP their colliders so the player cannot jump through into the void!
        if (name == "Baseboard" || name == "LightPanel")
        {
            Collider col = g.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        ApplyWorldUVs(g, pos, scale, name);
        if (mat != null) g.GetComponent<Renderer>().sharedMaterial = mat;
        if (ignoreNav) IgnoreForNavMesh(g);
        return g;
    }

    /// Replaces the box's stretched 0-1 UVs with world-space UVs, so the texture
    /// repeats every N meters and lines up across neighbouring pieces.
    void ApplyWorldUVs(GameObject g, Vector3 pos, Vector3 scale, string name)
    {
        float meters;
        switch (name)
        {
            case "Floor": meters = floorTextureMeters; break;
            case "Ceiling": meters = ceilingTextureMeters; break;
            case "Wall":
            case "Pillar": meters = wallTextureMeters; break;
            case "Baseboard": meters = 1f; break;
            default: return;                              // light panels keep their own UVs
        }
        if (meters <= 0.01f) return;

        MeshFilter mf = g.GetComponent<MeshFilter>();
        Mesh m = Instantiate(mf.sharedMesh);              // own copy so other boxes are not affected
        Vector3[] verts = m.vertices;
        Vector3[] norms = m.normals;
        Vector2[] uvs = new Vector2[verts.Length];

        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 w = pos + Vector3.Scale(verts[i], scale);   // world position of this vertex
            Vector3 n = norms[i];
            Vector2 uv;
            if (Mathf.Abs(n.y) > 0.5f) uv = new Vector2(w.x, w.z);        // top / bottom faces
            else if (Mathf.Abs(n.x) > 0.5f) uv = new Vector2(w.z, w.y);   // faces pointing along X
            else uv = new Vector2(w.x, w.y);                              // faces pointing along Z
            uvs[i] = uv / meters;
        }

        m.uv = uvs;
        mf.sharedMesh = m;
    }

    void IgnoreForNavMesh(GameObject g)
    {
        NavMeshModifier m = g.AddComponent<NavMeshModifier>();
        m.ignoreFromBuild = true;
    }

    static Bounds GetBounds(GameObject g)
    {
        Renderer[] rs = g.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    /// Spawns a prefab, rotates it, stretches it to 'size' (0 on an axis = keep native size)
    /// and positions it so its bounds are centered on 'center' with the top or bottom at height y.
    /// This ignores whatever pivot or scale the original model had.
    GameObject Spawn(GameObject prefab, Vector3 center, float yaw, Vector3 size, float y, bool yIsTop, bool alignLength)
    {
        GameObject wrapper = new GameObject(prefab.name);
        wrapper.transform.SetParent(root, false);
        wrapper.transform.position = Vector3.zero;

        GameObject g = Instantiate(prefab, wrapper.transform);
        g.transform.localPosition = Vector3.zero;

        float extra = 0f;
        g.transform.localRotation = Quaternion.identity;
        if (alignLength)
        {
            Bounds nb = GetBounds(g);
            if (nb.size.z > nb.size.x) extra = 90f;   // make the long side run along X first
        }
        g.transform.localRotation = Quaternion.Euler(0f, yaw + extra, 0f);

        Bounds b = GetBounds(g);
        Vector3 s = Vector3.one;
        if (size.x > 0f && b.size.x > 0.0001f) s.x = size.x / b.size.x;
        if (size.y > 0f && b.size.y > 0.0001f) s.y = size.y / b.size.y;
        if (size.z > 0f && b.size.z > 0.0001f) s.z = size.z / b.size.z;
        wrapper.transform.localScale = s;

        b = GetBounds(g);
        float dy = yIsTop ? (y - b.max.y) : (y - b.min.y);
        wrapper.transform.position += new Vector3(center.x - b.center.x, dy, center.z - b.center.z);

        foreach (Transform t in wrapper.GetComponentsInChildren<Transform>())
            t.gameObject.isStatic = true;

        return wrapper;
    }

    // ---------------- Player and villain ----------------

    void PlaceActors()
    {
        if (rooms.Count == 0) return;

        Vector2Int startCell = RoomCenter(rooms[0]);
        Vector3 startPos = CellCenter(startCell.x, startCell.y);

        // Villain goes to the room farthest from the player
        RectInt farthest = rooms[0];
        float best = -1f;
        foreach (RectInt r in rooms)
        {
            Vector2Int c = RoomCenter(r);
            float dist = Vector2Int.Distance(startCell, c);
            if (dist > best) { best = dist; farthest = r; }
        }
        Vector2Int vc = RoomCenter(farthest);
        Vector3 villainPos = CellCenter(vc.x, vc.y);

        if (player != null)
        {
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;               // must be off to teleport
            player.position = startPos + Vector3.up * 1.1f;
            if (cc) cc.enabled = true;
        }

        if (villain != null)
        {
            NavMeshAgent agent = villain.GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;

            Vector3 targetPos = villainPos;
            if (NavMesh.SamplePosition(villainPos, out NavMeshHit hit, tileSize * 4f, NavMesh.AllAreas))
                targetPos = hit.position;

            villain.transform.position = targetPos;
            villain.SetActive(true);

            if (agent != null)
            {
                agent.enabled = true;
                agent.Warp(targetPos);
            }

            VillainAI ai = villain.GetComponent<VillainAI>();
            if (ai != null)
            {
                ai.OnNavMeshReady();
            }
        }

        PlaceExit(farthest, startCell);
    }

    struct WallCandidate
    {
        public int x;
        public int z;
        public int side;
        public WallCandidate(int x, int z, int side) { this.x = x; this.z = z; this.side = side; }
    }

    void ReserveExitSpot()
    {
        if (rooms.Count == 0) return;

        Vector2Int startCell = RoomCenter(rooms[0]);
        RectInt bestRoom = rooms.Count > 1 ? rooms[1] : rooms[0];
        float bestScore = -1f;

        // Strictly exclude rooms[0] (player spawn) so the exit is NEVER reserved in the player's room
        for (int i = 1; i < rooms.Count; i++)
        {
            RectInt r = rooms[i];
            Vector2Int c = RoomCenter(r);
            float dist = Vector2Int.Distance(startCell, c);
            if (dist > bestScore)
            {
                bestScore = dist;
                bestRoom = r;
            }
        }

        int[] edx = { 0, 1, 0, -1 };
        int[] edz = { 1, 0, -1, 0 };
        var candidates = GetWallCandidates(bestRoom, edx, edz);
        if (candidates.Count > 0)
        {
            reservedExitCell = new Vector2Int(candidates[0].x, candidates[0].z);
        }
        else
        {
            reservedExitCell = RoomCenter(bestRoom);
        }
    }

    List<WallCandidate> GetWallCandidates(RectInt room, int[] edx, int[] edz)
    {
        List<WallCandidate> list = new List<WallCandidate>();
        int midX = room.xMin + room.width / 2;
        int midZ = room.yMin + room.height / 2;

        // Prioritize wall midpoints first (North=0, South=2, East=1, West=3)
        if (IsExitWall(midX, room.yMax - 1, 0, edx, edz)) list.Add(new WallCandidate(midX, room.yMax - 1, 0));
        if (IsExitWall(midX, room.yMin, 2, edx, edz)) list.Add(new WallCandidate(midX, room.yMin, 2));
        if (IsExitWall(room.xMax - 1, midZ, 1, edx, edz)) list.Add(new WallCandidate(room.xMax - 1, midZ, 1));
        if (IsExitWall(room.xMin, midZ, 3, edx, edz)) list.Add(new WallCandidate(room.xMin, midZ, 3));

        // Then any other valid wall cell on the room perimeter
        for (int x = room.xMin; x < room.xMax; x++)
        {
            for (int z = room.yMin; z < room.yMax; z++)
            {
                for (int s = 0; s < 4; s++)
                {
                    if (IsExitWall(x, z, s, edx, edz))
                    {
                        var wc = new WallCandidate(x, z, s);
                        bool exists = false;
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i].x == wc.x && list[i].z == wc.z && list[i].side == wc.side)
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (!exists) list.Add(wc);
                    }
                }
            }
        }
        return list;
    }

    bool IsExitWall(int x, int z, int side, int[] edx, int[] edz)
    {
        if (!IsFloor(x, z)) return false;
        int nx = x + edx[side];
        int nz = z + edz[side];
        return nx < 0 || nx >= gridWidth || nz < 0 || nz >= gridHeight || !floor[nx, nz];
    }

    void PlaceExit(RectInt villainRoom, Vector2Int startCell)
    {
        Vector3 startPos = CellCenter(startCell.x, startCell.y);

        // Filter candidate rooms to strictly guarantee the portal NEVER spawns near player spawn!
        // Minimum 14 tiles separation (35 meters away)
        List<RectInt> candidateRooms = new List<RectInt>();
        float minSeparationTiles = 14f;

        foreach (RectInt r in rooms)
        {
            if (r.Equals(rooms[0])) continue; // Never in player spawn room!
            float dist = Vector2Int.Distance(startCell, RoomCenter(r));
            if (dist >= minSeparationTiles)
            {
                candidateRooms.Add(r);
            }
        }

        if (candidateRooms.Count == 0)
        {
            // Fallback for smaller layouts: pick rooms furthest from player, excluding rooms[0]
            foreach (RectInt r in rooms)
            {
                if (!r.Equals(rooms[0])) candidateRooms.Add(r);
            }
        }

        // Sort candidate rooms descending by distance from player spawn
        candidateRooms.Sort((a, b) =>
        {
            float distA = Vector2Int.Distance(startCell, RoomCenter(a));
            float distB = Vector2Int.Distance(startCell, RoomCenter(b));
            return distB.CompareTo(distA);
        });

        int[] edx = { 0, 1, 0, -1 };
        int[] edz = { 1, 0, -1, 0 };
        // Correct facing angles: North wall=0, East wall=90, South wall=180, West wall=270
        // so the door front and green neon light face directly into the room.
        float[] eyaw = { 0f, 90f, 180f, 270f };

        Vector3 finalPos = Vector3.zero;
        Quaternion finalRot = Quaternion.identity;
        bool foundValidPlacement = false;

        NavMeshPath path = new NavMeshPath();

        foreach (RectInt room in candidateRooms)
        {
            if (room.Equals(villainRoom) && candidateRooms.Count > 1) continue;

            List<WallCandidate> candidates = GetWallCandidates(room, edx, edz);

            foreach (var cand in candidates)
            {
                Vector3 cCenter = CellCenter(cand.x, cand.z);
                Vector3 wallOffset = new Vector3(edx[cand.side], 0f, edz[cand.side]) * (tileSize * 0.46f);
                Vector3 testPos = cCenter + wallOffset;
                Quaternion testRot = Quaternion.Euler(0f, eyaw[cand.side], 0f);

                // Doorway entrance test point 0.8m in front of the door inside the room
                Vector3 doorFront = testPos + testRot * new Vector3(0f, 0f, -0.8f);

                // Strictly verify complete, unobstructed reachability via NavMesh from player spawn point
                if (NavMesh.SamplePosition(doorFront, out NavMeshHit hit, tileSize, NavMesh.AllAreas))
                {
                    if (NavMesh.CalculatePath(startPos, hit.position, NavMesh.AllAreas, path) &&
                        path.status == NavMeshPathStatus.PathComplete)
                    {
                        finalPos = testPos;
                        finalRot = testRot;
                        foundValidPlacement = true;
                        break;
                    }
                }
            }

            if (foundValidPlacement) break;
        }

        // Fallback if no wall passed the strict path check
        if (!foundValidPlacement && candidateRooms.Count > 0)
        {
            RectInt fallbackRoom = candidateRooms[0];
            Vector2Int c = RoomCenter(fallbackRoom);
            finalPos = CellCenter(c.x, c.y) + Vector3.forward * (tileSize * 0.44f);
            finalRot = Quaternion.identity;
        }

        // Build procedural green doorway exit portal
        GameObject exitGo = BackroomsExitZone.BuildExitPortal(finalPos, finalRot, wallMaterial, lightPanelMaterial);
        if (root != null) exitGo.transform.SetParent(root, true);

        // Spawn smart 4-legged Smily Hound guarding the exit portal area
        if (enableSmilyHound)
        {
            SpawnSmilyHoundAtExit(finalPos, finalRot);
        }

        // Safety clear: remove any stray colliders in front of the doorway threshold
        Vector3 doorClearArea = finalPos + finalRot * new Vector3(0f, 0f, -0.8f);
        Collider[] overlaps = Physics.OverlapSphere(doorClearArea, 1.4f);
        foreach (Collider col in overlaps)
        {
            Transform t = col.transform;
            while (t != null && t != root && t.parent != root) t = t.parent;
            if (t != null && t.parent == root &&
                !t.name.Contains("Exit") &&
                !t.name.Contains("Wall") &&
                !t.name.Contains("Floor") &&
                !t.name.Contains("GeneratedMesh"))
            {
                Destroy(t.gameObject);
            }
        }
    }

    void SpawnSmilyHoundAtExit(Vector3 portalPos, Quaternion portalRot)
    {
        // Position 5.5m in front of exit portal on NavMesh
        Vector3 spawnTarget = portalPos + portalRot * new Vector3(0f, 0f, -5.5f);
        if (NavMesh.SamplePosition(spawnTarget, out NavMeshHit hit, 8f, NavMesh.AllAreas))
        {
            spawnTarget = hit.position;
        }

        // Face outward into the room/corridor towards incoming players
        Quaternion facing = Quaternion.LookRotation(portalRot * Vector3.back, Vector3.up);

        GameObject houndGo = null;

        if (smilyHoundPrefab != null)
        {
            houndGo = Instantiate(smilyHoundPrefab, spawnTarget, facing);
        }
        else
        {
            // Build normalized Hound with visual child
            houndGo = new GameObject("SmilyHorrorHound");
            houndGo.transform.position = spawnTarget;
            houndGo.transform.rotation = facing;
            GameObject modelAsset = null;
#if UNITY_EDITOR
            modelAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Characters/smily dog/smily_horror_monster.glb");
#endif
            if (modelAsset != null)
            {
                GameObject visual = Instantiate(modelAsset, houndGo.transform, false);
                visual.name = "VisualModel";
                const float s = 0.013f;
                visual.transform.localPosition = new Vector3(75.089f * s, -10f * s, -10f * s);
                visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                visual.transform.localScale = new Vector3(s, s, s);

#if UNITY_EDITOR
                RuntimeAnimatorController ctrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Art/Characters/smily dog/SmilyMonster.controller");
                Animator anim = visual.GetComponent<Animator>();
                if (anim == null) anim = visual.AddComponent<Animator>();
                if (ctrl != null) anim.runtimeAnimatorController = ctrl;
#endif
            }
        }

        if (houndGo == null) return;

        // Ensure any raw sketchfab child inside houndGo is normalized (in case prefab or asset was passed directly)
        Transform sketchfabChild = houndGo.transform.Find("Sketchfab_model");
        if (sketchfabChild != null)
        {
            const float s = 0.013f;
            sketchfabChild.localPosition = new Vector3(75.089f * s, -10f * s, -10f * s);
            sketchfabChild.localRotation = Quaternion.Euler(0f, 180f, 0f);
            sketchfabChild.localScale = new Vector3(s, s, s);
        }

        NavMeshAgent agent = houndGo.GetComponent<NavMeshAgent>();
        if (agent == null) agent = houndGo.AddComponent<NavMeshAgent>();
        agent.enabled = false;

        houndGo.transform.position = spawnTarget;
        houndGo.transform.rotation = facing;

        // NavMeshAgent configuration calibrated for 4-legged beast
        agent.radius = 0.45f;
        agent.height = 0.90f;
        agent.speed = 4.4f;
        agent.acceleration = 14f;
        agent.angularSpeed = 500f;
        agent.stoppingDistance = 1.2f;

        // Ensure 4-legged procedural gallop runner
        SmilyMonsterGait gait = houndGo.GetComponent<SmilyMonsterGait>();
        if (gait == null) gait = houndGo.AddComponent<SmilyMonsterGait>();

        // Ensure smart territorial AI guarding the portal
        SmilyHoundAI ai = houndGo.GetComponent<SmilyHoundAI>();
        if (ai == null) ai = houndGo.AddComponent<SmilyHoundAI>();
        ai.SetPortalAnchor(portalPos);

        houndGo.SetActive(true);
        agent.enabled = true;
        agent.Warp(spawnTarget);
    }

    void EnsureGameOverSystem()
    {
        if (FindAnyObjectByType<GameOverController>() != null) return;
        new GameObject("GameOver Menu").AddComponent<GameOverController>();
    }

    void EnsureVictorySystem()
    {
        if (FindAnyObjectByType<VictoryController>() != null) return;
        new GameObject("Victory Menu").AddComponent<VictoryController>();
    }

    void EnsureSurvivalDialogueHUD()
    {
        if (FindAnyObjectByType<SurvivalDialogueHUD>() != null) return;
        new GameObject("Survival Dialogue HUD").AddComponent<SurvivalDialogueHUD>();
    }

    // Creates the ambience player once, so the backrooms SFX start with the level.
    void EnsureAmbience()
    {
        if (!autoAmbience) return;
        if (FindAnyObjectByType<BackroomsAmbience>() != null) return;

        new GameObject("Backrooms Ambience").AddComponent<BackroomsAmbience>();
    }

    // Haze, colour grading and a camera that runs post-processing, so the level
    // does not look like flat unlit geometry.
    void EnsureAtmosphere()
    {
        if (FindAnyObjectByType<BackroomsAtmosphere>() != null) return;

        new GameObject("Backrooms Atmosphere").AddComponent<BackroomsAtmosphere>();
    }

    // Power fluctuations / blackout horror events
    void EnsureBlackoutEvents()
    {
        if (FindAnyObjectByType<BackroomsBlackoutEvent>() != null) return;

        gameObject.AddComponent<BackroomsBlackoutEvent>();
    }

    void EnsureParticles()
    {
        if (FindAnyObjectByType<BackroomsParticles>() != null) return;

        new GameObject("Backrooms Particles").AddComponent<BackroomsParticles>();
    }

    void EnsureVHSGlitch()
    {
        if (FindAnyObjectByType<CameraVHSGlitch>() != null) return;

        new GameObject("Camera VHS Glitch").AddComponent<CameraVHSGlitch>();
    }

    void EnsurePlayerMotion()
    {
        Transform targetPlayer = player;
        if (targetPlayer == null)
        {
            var fpc = FindAnyObjectByType<StarterAssets.FirstPersonController>();
            if (fpc != null) targetPlayer = fpc.transform;
        }

        if (targetPlayer != null && targetPlayer.GetComponent<StarterAssets.FirstPersonMotion>() == null)
        {
            targetPlayer.gameObject.AddComponent<StarterAssets.FirstPersonMotion>();
        }
    }

    void EnsurePlayerStaminaHUD()
    {
        if (FindAnyObjectByType<PlayerStaminaHUD>() != null) return;
        new GameObject("Player Stamina HUD").AddComponent<PlayerStaminaHUD>();
    }

    void EnsurePlayerVentController()
    {
        Transform targetPlayer = player;
        if (targetPlayer == null)
        {
            var fpc = FindAnyObjectByType<StarterAssets.FirstPersonController>();
            if (fpc != null) targetPlayer = fpc.transform;
        }

        if (targetPlayer != null && targetPlayer.GetComponent<PlayerVentController>() == null)
        {
            targetPlayer.gameObject.AddComponent<PlayerVentController>();
        }
    }

    void EnsureVentHUD()
    {
        VentHUD.GetOrCreate();
    }

    void EnsureMonsterCompositionAudio()
    {
        // Ensure Outlast 2 style chase soundtrack director
        if (FindAnyObjectByType<ChaseSoundtrackDirector>() == null)
        {
            new GameObject("Chase Soundtrack Director").AddComponent<ChaseSoundtrackDirector>();
        }

        MonsterCompositionAudio compAudio = FindAnyObjectByType<MonsterCompositionAudio>();
        if (compAudio == null)
        {
            compAudio = new GameObject("Monster Composition Audio").AddComponent<MonsterCompositionAudio>();
        }

        // Collect spatial audio candidate spots across rooms and corridors
        List<Vector3> candidateSpots = new List<Vector3>();

        // 1. Room centers
        foreach (RectInt r in rooms)
        {
            Vector2Int rc = RoomCenter(r);
            candidateSpots.Add(CellCenter(rc.x, rc.y));
        }

        // 2. Sample various corridor/walkable cells across the map
        for (int x = 2; x < gridWidth - 2; x += 3)
        {
            for (int z = 2; z < gridHeight - 2; z += 3)
            {
                if (floor != null && floor[x, z] && !roomCell[x, z])
                {
                    candidateSpots.Add(CellCenter(x, z));
                }
            }
        }

        compAudio.InitializeSpots(candidateSpots);
    }

    // ---------------- Almond Water & Mental Systems ----------------

    void BuildAlmondWaterPickups()
    {
        Transform pickupRoot = new GameObject("AlmondWaterPickups").transform;
        pickupRoot.SetParent(root, false);

        int spawnedCount = 0;
        int maxBottles = 3; // Rare survival commodity across the entire complex

        // 1. Rarely spawn on a table / desk surface (max 2)
        for (int i = 0; i < tableTopSpots.Count && spawnedCount < 2; i++)
        {
            if (Random.value < 0.25f)
            {
                SpawnAlmondWaterBottle(pickupRoot, tableTopSpots[i]);
                spawnedCount++;
            }
        }

        // 2. Spawn remaining in a secluded room corner or alcove
        Vector2Int spawnCell = rooms.Count > 0 ? RoomCenter(rooms[0]) : new Vector2Int(-99, -99);
        List<Vector2Int> validCells = new List<Vector2Int>();

        for (int x = 2; x < gridWidth - 2; x++)
        {
            for (int z = 2; z < gridHeight - 2; z++)
            {
                if (!floor[x, z]) continue;
                if (Mathf.Abs(spawnCell.x - x) <= 3 && Mathf.Abs(spawnCell.y - z) <= 3) continue;
                if (reservedExitCell.x >= 0 && Mathf.Abs(reservedExitCell.x - x) <= 3 && Mathf.Abs(reservedExitCell.y - z) <= 3) continue;
                validCells.Add(new Vector2Int(x, z));
            }
        }

        for (int i = 0; i < validCells.Count; i++)
        {
            int j = Random.Range(i, validCells.Count);
            Vector2Int tmp = validCells[i];
            validCells[i] = validCells[j];
            validCells[j] = tmp;
        }

        for (int i = 0; i < validCells.Count && spawnedCount < maxBottles; i++)
        {
            Vector2Int c = validCells[i];
            Vector3 center = CellCenter(c.x, c.y);
            Vector3 bottlePos = center + new Vector3(Random.Range(-0.85f, 0.85f), 0.0f, Random.Range(-0.85f, 0.85f));
            SpawnAlmondWaterBottle(pickupRoot, bottlePos);
            spawnedCount++;
        }
    }

    void SpawnAlmondWaterBottle(Transform parent, Vector3 position)
    {
        GameObject bottle = new GameObject("AlmondWater_Bottle");
        bottle.transform.SetParent(parent, false);
        bottle.transform.position = position;
        bottle.AddComponent<AlmondWaterPickup>();
    }

    void EnsurePlayerSanity()
    {
        Transform targetPlayer = player;
        if (targetPlayer == null)
        {
            var fpc = FindAnyObjectByType<StarterAssets.FirstPersonController>();
            if (fpc != null) targetPlayer = fpc.transform;
        }

        if (targetPlayer != null && targetPlayer.GetComponent<PlayerSanity>() == null)
        {
            targetPlayer.gameObject.AddComponent<PlayerSanity>();
        }
    }

    void EnsurePlayerInventory()
    {
        Transform targetPlayer = player;
        if (targetPlayer == null)
        {
            var fpc = FindAnyObjectByType<StarterAssets.FirstPersonController>();
            if (fpc != null) targetPlayer = fpc.transform;
        }

        if (targetPlayer != null && targetPlayer.GetComponent<PlayerInventory>() == null)
        {
            targetPlayer.gameObject.AddComponent<PlayerInventory>();
        }
    }

    void EnsurePlayerDrinkController()
    {
        Transform targetPlayer = player;
        if (targetPlayer == null)
        {
            var fpc = FindAnyObjectByType<StarterAssets.FirstPersonController>();
            if (fpc != null) targetPlayer = fpc.transform;
        }

        if (targetPlayer != null && targetPlayer.GetComponent<PlayerDrinkController>() == null)
        {
            targetPlayer.gameObject.AddComponent<PlayerDrinkController>();
        }
    }

    void EnsureInventoryHUD()
    {
        InventoryHUD.GetOrCreate();
    }

    void EnsureHallucinationManager()
    {
        if (FindAnyObjectByType<HallucinationManager>() != null) return;
        new GameObject("Hallucination Manager").AddComponent<HallucinationManager>();
    }

    void EnsureDoorEventManager()
    {
        if (!enableDoorEvents) return;
        if (FindAnyObjectByType<BackroomsDoorEventManager>() != null) return;
        new GameObject("Backrooms Door Event Manager").AddComponent<BackroomsDoorEventManager>();
    }

    void EnsurePlayerLockerController()
    {
        Transform targetPlayer = player;
        if (targetPlayer == null)
        {
            var fpc = FindAnyObjectByType<StarterAssets.FirstPersonController>();
            if (fpc != null) targetPlayer = fpc.transform;
        }

        if (targetPlayer != null)
        {
            if (targetPlayer.GetComponent<PlayerLockerController>() == null)
                targetPlayer.gameObject.AddComponent<PlayerLockerController>();

            if (targetPlayer.GetComponent<PlayerDoorInteraction>() == null)
                targetPlayer.gameObject.AddComponent<PlayerDoorInteraction>();
        }
    }

    struct RoomBoundaryEdge
    {
        public int roomIndex;
        public int rx, rz;
        public int cx, cz;
        public int d;
        public Vector3 edgePos;
        public Vector3 outwardDir;
        public bool alongX;
    }

    void BuildEnclosedRoomsAndDoors()
    {
        if (!enableEnclosedRooms) return;

        int[] dx = { 0, 0, 1, -1 };
        int[] dz = { 1, -1, 0, 0 };

        int maxRoomsToEnclose = Mathf.Clamp(maxEnclosedRooms, 1, 6);
        float doorChance = Mathf.Min(roomDoorChance, 0.40f);

        // Collect valid rooms eligible for enclosure
        List<int> candidateRoomIndices = new List<int>();
        // Start from rIdx = 1: rooms[0] is player spawn and MUST ALWAYS remain open!
        for (int rIdx = 1; rIdx < rooms.Count; rIdx++)
        {
            RectInt r = rooms[rIdx];

            // Never enclose the room containing the exit portal
            if (reservedExitCell.x >= r.x && reservedExitCell.x < r.x + r.width &&
                reservedExitCell.y >= r.y && reservedExitCell.y < r.y + r.height)
            {
                continue;
            }

            candidateRoomIndices.Add(rIdx);
        }

        // Shuffle candidate rooms
        for (int i = 0; i < candidateRoomIndices.Count; i++)
        {
            int rnd = Random.Range(i, candidateRoomIndices.Count);
            int tmp = candidateRoomIndices[i];
            candidateRoomIndices[i] = candidateRoomIndices[rnd];
            candidateRoomIndices[rnd] = tmp;
        }

        int enclosedCount = 0;

        foreach (int rIdx in candidateRoomIndices)
        {
            if (enclosedCount >= maxRoomsToEnclose) break;

            RectInt r = rooms[rIdx];

            // Roll door spawn chance
            if (Random.value > doorChance) continue;

            // Collect all boundary edges that connect this room to outside corridors
            List<RoomBoundaryEdge> edges = new List<RoomBoundaryEdge>();

            for (int x = r.x; x < r.x + r.width; x++)
            {
                for (int z = r.y; z < r.y + r.height; z++)
                {
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + dx[d];
                        int nz = z + dz[d];
                        if (nx < 0 || nz < 0 || nx >= gridWidth || nz >= gridHeight) continue;

                        if (floor[nx, nz] && !roomCell[nx, nz])
                        {
                            Vector3 dir = new Vector3(dx[d], 0, dz[d]);
                            Vector3 edgePos = CellCenter(x, z) + dir * (tileSize * 0.5f);
                            bool alongX = (dx[d] == 0);

                            edges.Add(new RoomBoundaryEdge
                            {
                                roomIndex = rIdx,
                                rx = x, rz = z,
                                cx = nx, cz = nz,
                                d = d,
                                edgePos = edgePos,
                                outwardDir = dir,
                                alongX = alongX
                            });
                        }
                    }
                }
            }

            if (edges.Count == 0) continue;

            // Prefer rooms that act as actual rooms (1 to 3 connections), avoid central multi-way crossroads
            if (edges.Count > 3) continue;

            // Every corridor connection to this room gets a wide doorway with an interactive door.
            List<Vector3> placedDoorPositions = new List<Vector3>();

            for (int i = 0; i < edges.Count; i++)
            {
                RoomBoundaryEdge edge = edges[i];

                BuildDoorwayWall(edge.edgePos, edge.outwardDir, edge.alongX, doorwayWidth, doorwayHeight);

                BackroomsDoor door = BackroomsDoor.Create(root, edge.edgePos, edge.outwardDir,
                                                         doorwayWidth, doorwayHeight, doorWoodMaterial, doorFrameMaterial);
                door.autoCloseDelay = doorStayOpenSeconds;
                placedDoorPositions.Add(edge.edgePos);
            }

            // Scatter hideable Locker inside this enclosed room
            if (enableRoomLockers && Random.value <= lockerChancePerRoom)
            {
                PlaceLockerInRoom(r, placedDoorPositions, dx, dz);
            }

            enclosedCount++;
        }
    }

    void BuildDoorwayWall(Vector3 edge, Vector3 dir, bool alongX, float doorwayW = 1.95f, float doorwayH = 2.85f)
    {
        float L = tileSize + wallThickness;
        float H = wallHeight;
        float T = wallThickness;
        float sideW = (L - doorwayW) * 0.5f;

        if (alongX)
        {
            // 1. Top lintel wall above door opening (ignored by NavMesh bake so monster can never get obstructed)
            float topH = H - doorwayH;
            Box("Wall", new Vector3(edge.x, doorwayH + topH * 0.5f, edge.z),
                new Vector3(L, topH, T), wallMaterial, true);

            // 2. Left jamb wall
            float leftX = edge.x - doorwayW * 0.5f - sideW * 0.5f;
            Box("Wall", new Vector3(leftX, doorwayH * 0.5f, edge.z),
                new Vector3(sideW, doorwayH, T), wallMaterial, false);

            // 3. Right jamb wall
            float rightX = edge.x + doorwayW * 0.5f + sideW * 0.5f;
            Box("Wall", new Vector3(rightX, doorwayH * 0.5f, edge.z),
                new Vector3(sideW, doorwayH, T), wallMaterial, false);

            if (baseboardMaterial != null)
            {
                Vector3 inward = -dir;
                Vector3 p = edge + inward * (wallThickness / 2f + 0.02f);
                float bbSideW = (tileSize - doorwayW) * 0.5f;
                if (bbSideW > 0.05f)
                {
                    float bbLeftX = p.x - doorwayW * 0.5f - bbSideW * 0.5f;
                    float bbRightX = p.x + doorwayW * 0.5f + bbSideW * 0.5f;
                    Box("Baseboard", new Vector3(bbLeftX, 0.075f, p.z), new Vector3(bbSideW, 0.15f, 0.04f), baseboardMaterial, true);
                    Box("Baseboard", new Vector3(bbRightX, 0.075f, p.z), new Vector3(bbSideW, 0.15f, 0.04f), baseboardMaterial, true);
                }
            }
        }
        else
        {
            // 1. Top lintel wall above door opening (ignored by NavMesh bake)
            float topH = H - doorwayH;
            Box("Wall", new Vector3(edge.x, doorwayH + topH * 0.5f, edge.z),
                new Vector3(T, topH, L), wallMaterial, true);

            // 2. Left/back jamb wall along Z
            float leftZ = edge.z - doorwayW * 0.5f - sideW * 0.5f;
            Box("Wall", new Vector3(edge.x, doorwayH * 0.5f, leftZ),
                new Vector3(T, doorwayH, sideW), wallMaterial, false);

            // 3. Right/forward jamb wall along Z
            float rightZ = edge.z + doorwayW * 0.5f + sideW * 0.5f;
            Box("Wall", new Vector3(edge.x, doorwayH * 0.5f, rightZ),
                new Vector3(T, doorwayH, sideW), wallMaterial, false);

            if (baseboardMaterial != null)
            {
                Vector3 inward = -dir;
                Vector3 p = edge + inward * (wallThickness / 2f + 0.02f);
                float bbSideW = (tileSize - doorwayW) * 0.5f;
                if (bbSideW > 0.05f)
                {
                    float bbLeftZ = p.z - doorwayW * 0.5f - bbSideW * 0.5f;
                    float bbRightZ = p.z + doorwayW * 0.5f + bbSideW * 0.5f;
                    Box("Baseboard", new Vector3(p.x, 0.075f, bbLeftZ), new Vector3(0.04f, 0.15f, bbSideW), baseboardMaterial, true);
                    Box("Baseboard", new Vector3(p.x, 0.075f, bbRightZ), new Vector3(0.04f, 0.15f, bbSideW), baseboardMaterial, true);
                }
            }
        }
    }

    void PlaceLockerInRoom(RectInt r, List<Vector3> doorPositions, int[] dx, int[] dz)
    {
        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int x = r.x; x < r.x + r.width; x++)
        {
            for (int z = r.y; z < r.y + r.height; z++)
            {
                if (reservedExitCell.x >= 0 && Mathf.Abs(reservedExitCell.x - x) <= 1 && Mathf.Abs(reservedExitCell.y - z) <= 1)
                    continue;

                Vector3 cellPos = CellCenter(x, z);
                bool tooCloseToDoor = false;
                for (int dp = 0; dp < doorPositions.Count; dp++)
                {
                    if (Vector3.Distance(cellPos, doorPositions[dp]) < 1.8f)
                    {
                        tooCloseToDoor = true;
                        break;
                    }
                }
                if (tooCloseToDoor) continue;

                candidates.Add(new Vector2Int(x, z));
            }
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            int rnd = Random.Range(i, candidates.Count);
            var tmp = candidates[i];
            candidates[i] = candidates[rnd];
            candidates[rnd] = tmp;
        }

        foreach (var c in candidates)
        {
            for (int d = 0; d < 4; d++)
            {
                int nx = c.x + dx[d];
                int nz = c.y + dz[d];

                if (!IsFloor(nx, nz))
                {
                    Vector3 backDir = new Vector3(dx[d], 0, dz[d]);
                    Vector3 lockerPos = CellCenter(c.x, c.y) + backDir * (tileSize * 0.5f - LockerHideSpot.LockerDepth * 0.5f);
                    Vector3 facingDir = -backDir;

                    LockerHideSpot.Create(root, lockerPos, facingDir, lockerExteriorMaterial, lockerInteriorMaterial);
                    return;
                }
            }
        }
    }
}