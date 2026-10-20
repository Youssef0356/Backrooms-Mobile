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
    public int roomCount = 14;
    public Vector2Int roomSizeMin = new Vector2Int(3, 3);
    public Vector2Int roomSizeMax = new Vector2Int(6, 6);   // normal rooms stay in this range
    [Range(0f, 1f)] public float bigRoomChance = 0.15f;     // chance a room is a rare big hall
    public Vector2Int bigRoomSizeMax = new Vector2Int(9, 9);
    public int maxBigRooms = 2;                             // hard cap on big halls per map
    [Range(1, 3)] public int corridorWidth = 1;
    public int extraConnections = 3;        // extra corridors = loops, so the villain can't be dodged easily
    public int seed = 0;                    // 0 = random each time

    [Header("Details")]
    public int pillarSpacing = 2;           // 0 = no pillars
    [Range(0f, 1f)] public float pillarChance = 0.7f;
    public int lightEvery = 3;              // ceiling light panel every N cells
    public GameObject lightPrefab;          // optional point light prefab (no shadows)
    [Range(0f, 1f)] public float lightPrefabChance = 0.3f;

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
        BuildFloorsAndCeilings();
        BuildWalls();
        BuildPillars();
        BuildLights();

        StaticBatchingUtility.Combine(root.gameObject);

        if (surface != null)
        {
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }

        PlaceActors();
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

        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                if (!floor[x, z] || x % lightEvery != 0 || z % lightEvery != 0) continue;

                Vector3 c = CellCenter(x, z);

                if (lightPanelMaterial != null)
                    Box("LightPanel", new Vector3(c.x, wallHeight - 0.02f, c.z),
                        new Vector3(1.2f, 0.04f, 0.6f), lightPanelMaterial, true);

                if (lightPrefab != null && Random.value < lightPrefabChance)
                {
                    GameObject l = Instantiate(lightPrefab, root);
                    l.transform.position = new Vector3(c.x, wallHeight - 0.4f, c.z);
                }
            }
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
}