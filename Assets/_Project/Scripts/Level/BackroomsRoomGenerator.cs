using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
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
    public int maxPanelLights = 64;
    public float panelLightIntensity = 3f;
    public float panelLightRange = 9f;
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

    [Header("Audio")]
    public bool autoAmbience = true;        // create a BackroomsAmbience object at level start

    bool[,] floor;
    bool[,] roomCell;
    readonly List<RectInt> rooms = new List<RectInt>();
    Transform root;

    void Start()
    {
        Generate();
    }

    [ContextMenu("Regenerate")]
    public void Generate()
    {
        if (seed != 0) Random.InitState(seed);

        if (root != null) Destroy(root.gameObject);
        root = new GameObject("GeneratedMap").transform;
        root.SetParent(surface != null ? surface.transform : transform, false);

        floor = new bool[gridWidth, gridHeight];
        roomCell = new bool[gridWidth, gridHeight];
        rooms.Clear();

        PlaceRooms();
        ConnectRooms();
        CarveBranches();
        BuildFloorsAndCeilings();
        BuildWalls();
        BuildPillars();
        BuildLights();
        BuildDecorations();

        StaticBatchingUtility.Combine(root.gameObject);

        if (surface != null)
        {
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }

        PlaceActors();
        EnsureAmbience();
        EnsureAtmosphere();
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

                    if (wallPrefab != null)
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

        int stride = Mathf.Max(1, Mathf.CeilToInt(cells.Count / (float)Mathf.Max(1, maxPanelLights)));

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
        GameObject g = Instantiate(prefab, root, false);
        g.transform.localPosition = Vector3.zero;
        g.transform.localRotation = Quaternion.identity;
        g.transform.localScale = prefab.transform.localScale *
                                 Random.Range(decorationScaleRange.x, decorationScaleRange.y);

        bool wallItem = forceWallItem || IsFlatWallProp(g);
        bool weird = Random.value < weirdChance;

        Vector3 dir = new Vector3(dx[side], 0f, dz[side]);   // points out at the wall
        Vector3 center = CellCenter(cell.x, cell.y);

        if (wallItem)
        {
            if (weird) MountWeird(g, center, dir);
            else MountFlat(g, center, dir);
        }
        else if (!weird)
        {
            PlaceOnFloor(g, center, dir, 0f);
        }
        else
        {
            int mode = Random.Range(0, 4);
            if (mode == 0) MountWeird(g, center, dir);        // chair glued to the wall
            else if (mode == 1) HangFromCeiling(g, center, dir);
            else if (mode == 2) FloatInAir(g, center, dir);
            else PlaceOnFloor(g, center, dir, Random.Range(0.15f, 0.45f)); // half sunk into the carpet
        }

        FinishDecoration(g);
    }

    /// A prop much thinner than it is wide reads as a poster / picture / sign.
    bool IsFlatWallProp(GameObject g)
    {
        Bounds b = GetBounds(g);
        float min = Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z));
        float max = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        return max > 0.001f && min / max < 0.35f;
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

    // ---------------- Helpers ----------------

    GameObject Box(string name, Vector3 pos, Vector3 scale, Material mat, bool ignoreNav)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(root, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.isStatic = true;
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
            if (NavMesh.SamplePosition(villainPos, out NavMeshHit hit, tileSize * 2f, NavMesh.AllAreas))
                villain.transform.position = hit.position;
            else
                villain.transform.position = villainPos;

            villain.SetActive(true);
        }
    }

    // Creates the ambience player once, so the backrooms SFX start with the level.
    void EnsureAmbience()
    {
        if (!autoAmbience) return;
        if (FindFirstObjectByType<BackroomsAmbience>() != null) return;

        new GameObject("Backrooms Ambience").AddComponent<BackroomsAmbience>();
    }

    // Haze, colour grading and a camera that runs post-processing, so the level
    // does not look like flat unlit geometry.
    void EnsureAtmosphere()
    {
        if (FindAnyObjectByType<BackroomsAtmosphere>() != null) return;

        new GameObject("Backrooms Atmosphere").AddComponent<BackroomsAtmosphere>();
    }
}