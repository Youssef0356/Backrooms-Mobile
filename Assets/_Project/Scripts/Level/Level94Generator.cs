using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

/// <summary>
/// Procedurally generates Backrooms Level 94 ("Motion"):
/// - Random rolling green hills with new unique seeds and layout each time.
/// - Auto-places custom Blender suburban houses (with foundation skirt ground-conformation).
/// - Spawns water towers perched on distant hill peaks.
/// - White picket fences and mailboxes along front yards.
/// - Suburban streetlights casting warm pools of light across the landscape.
/// - The anomalous Exit House containing the extraction portal to complete Level 94.
/// - Full runtime healing and binding for player movement, Cinemachine camera, and touch controls.
/// </summary>
public class Level94Generator : MonoBehaviour
{
    [Header("Terrain Parameters")]
    [Tooltip("World size of the generated hill landscape in meters.")]
    public float terrainSize = 300f;
    [Tooltip("Number of vertex subdivisions along each axis.")]
    public int gridResolution = 100;
    [Tooltip("Primary hill wave scale (lower = broader, more sweeping liminal hills).")]
    public float hillFrequency = 0.010f;
    [Tooltip("Maximum height of rolling hills in meters.")]
    public float hillHeight = 10.5f;
    [Tooltip("Secondary hill frequency for natural undulations.")]
    public float secondaryFrequency = 0.030f;
    public float secondaryHeight = 3.0f;
    public Material grassMaterial;

    [Header("House Placement")]
    [Tooltip("Number of suburban houses to scatter across the hills.")]
    [Range(5, 60)]
    public int houseCount = 28;
    [Tooltip("Minimum distance in meters between any two houses.")]
    public float minHouseDistance = 24f;
    [Tooltip("Custom house prefabs/models to pick from. Auto-loads LEVEL 94 Houses.glb if empty.")]
    public GameObject[] housePrefabs;
    [Tooltip("Scale multiplier for suburban houses.")]
    public float houseScale = 1.25f;

    [Header("Water Towers")]
    [Tooltip("Water tower landmark model (WaterContainer.glb).")]
    public GameObject waterTowerPrefab;
    [Range(1, 12)]
    public int waterTowerCount = 6;
    public float waterTowerScale = 16.4f;

    [Header("Exit House")]
    [Tooltip("Special landmark house containing the Backrooms exit threshold portal.")]
    public GameObject exitHousePrefab;
    [Tooltip("Distance from spawn where the exit house is placed.")]
    public float minExitDistance = 95f;

    [Header("Suburban Props")]
    [Tooltip("Fence prefab/model (Fences.glb).")]
    public GameObject fencePrefab;
    public float fenceScale = 0.75f;
    [Tooltip("Mailbox prefab.")]
    public GameObject mailboxPrefab;
    [Tooltip("Streetlight prefab.")]
    public GameObject streetlightPrefab;
    [Range(0, 40)]
    public int streetlightCount = 20;

    [Header("Perimeter Border Fences")]
    [Tooltip("Generate an enclosed boundary fence perimeter using the fence prefab.")]
    public bool generateBorderFences = true;
    [Tooltip("Distance in meters from the terrain outer edge where border fences are placed.")]
    public float borderInset = 22f;
    [Tooltip("Scale multiplier for border fences.")]
    public float borderFenceScale = 1.0f;

    [Header("House Interior & Decorations")]
    [Tooltip("Bed prefab placed in house bedrooms.")]
    public GameObject bedPrefab;
    [Tooltip("Random living room tables.")]
    public GameObject[] tablePrefabs;
    [Tooltip("Random living room chairs and couches.")]
    public GameObject[] chairPrefabs;
    [Tooltip("Random retro TVs.")]
    public GameObject[] tvPrefabs;
    [Tooltip("Spawn interactive LockerHideSpot in each house.")]
    public bool spawnLockers = true;
    [Tooltip("Chance (0-1) to spawn an Almond Water bottle in a house.")]
    [Range(0f, 1f)]
    public float almondWaterChance = 0.45f;
    [Tooltip("Add warm eerie interior lighting to each decorated house.")]
    public bool enableInteriorLights = true;

    [Header("Player & Navigation")]
    public Transform player;
    public GameObject playerPrefab;
    public bool bakeNavMesh = true;

    [Header("Seed")]
    [Tooltip("0 = random procedural generation every single time")]
    public int seed = 0;

    private Mesh terrainMesh;
    [Header("Generator Control")]
    [Tooltip("If checked, automatically runs procedural generation on Start. Leave unchecked to build step-by-step.")]
    public bool autoGenerateOnStart = false;

    private GameObject terrainObj;
    private List<Vector3> placedHousePositions = new List<Vector3>();

    void Start()
    {
        if (autoGenerateOnStart)
        {
            Generate();
        }
    }

    [ContextMenu("Regenerate Level 94")]
    public void Generate()
    {
        // Randomize seed every time if seed == 0
        if (seed != 0)
        {
            Random.InitState(seed);
        }
        else
        {
            int freshSeed = (int)(System.DateTime.UtcNow.Ticks % 2147483647);
            Random.InitState(freshSeed);
        }

        ClearPreviousGeneration();
        LoadAssetsIfMissing();

        BuildRollingHillsMesh();
        EnsureAtmosphere();

        SpawnExitHouse();
        SpawnHouses();
        SpawnWaterTowers();
        SpawnStreetlightsAndProps();
        SpawnPerimeterBorderFences();

        if (bakeNavMesh)
        {
            BakeNavMesh();
        }

        BindAndHealPlayerAndCamera();
        EnsureHUDAndControllers();
    }

    void ClearPreviousGeneration()
    {
        placedHousePositions.Clear();

        Transform existing = transform.Find("Level94_Environment");
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }

        // Deactivate old manual static scene duplicates so they don't cover the procedural generation
        if (gameObject.scene.isLoaded)
        {
            GameObject[] roots = gameObject.scene.GetRootGameObjects();
            foreach (var r in roots)
            {
                if (r == gameObject || r.name == "Level94_Director" || r.name == "NestedParent_Unpack") continue;
                string n = r.name;
                if ((n.StartsWith("LEVEL 94") || n.StartsWith("WaterContainer") || n.StartsWith("House_Prologue_Set") || n.StartsWith("House_Set")) &&
                    !n.Contains("Director") && !n.Contains("Atmosphere") && !n.Contains("Environment"))
                {
                    if (Application.isPlaying) Destroy(r);
                    else DestroyImmediate(r);
                }
            }
        }
    }

    void LoadAssetsIfMissing()
    {
#if UNITY_EDITOR
        if (grassMaterial == null)
        {
            grassMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Level94/Materials/Grass_Level94.mat");
        }

        if (housePrefabs == null || housePrefabs.Length == 0)
        {
            var list = new List<GameObject>();
            GameObject customHouse = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/LEVEL 94 Houses.glb");
            if (customHouse != null) list.Add(customHouse);

            string[] names = { "House_Classic_Gable", "House_Tall_Dormer", "House_Ranch_Bungalow" };
            foreach (var n in names)
            {
                var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Art/Level94/Prefabs/{n}.prefab");
                if (p != null) list.Add(p);
            }
            if (list.Count > 0) housePrefabs = list.ToArray();
        }

        if (waterTowerPrefab == null)
        {
            waterTowerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/WaterContainer.glb");
        }

        if (fencePrefab == null)
        {
            fencePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/Fences.glb");
            if (fencePrefab == null)
                fencePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/Prefabs/Suburban_PicketFence.prefab");
        }

        if (exitHousePrefab == null)
        {
            exitHousePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/Prefabs/Level94_ExitHouse.prefab");
            if (exitHousePrefab == null && housePrefabs != null && housePrefabs.Length > 0)
                exitHousePrefab = housePrefabs[0];
        }

        if (streetlightPrefab == null)
        {
            streetlightPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/Prefabs/Suburban_Streetlight.prefab");
        }

        if (mailboxPrefab == null)
        {
            mailboxPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/Prefabs/Suburban_Mailbox.prefab");
        }

        if (bedPrefab == null)
        {
            bedPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Environment/Backrooms/Decoration/Bed.prefab");
        }

        if (tablePrefabs == null || tablePrefabs.Length == 0)
        {
            var list = new List<GameObject>();
            string[] tableNames = { "Table", "ModernTable" };
            foreach (var t in tableNames)
            {
                var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Art/Environment/Backrooms/Decoration/{t}.prefab");
                if (p != null) list.Add(p);
            }
            if (list.Count > 0) tablePrefabs = list.ToArray();
        }

        if (chairPrefabs == null || chairPrefabs.Length == 0)
        {
            var list = new List<GameObject>();
            string[] chairNames = { "Canapé", "FatChair", "ChairWithWheels" };
            foreach (var c in chairNames)
            {
                var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Art/Environment/Backrooms/Decoration/{c}.prefab");
                if (p != null) list.Add(p);
            }
            if (list.Count > 0) chairPrefabs = list.ToArray();
        }

        if (tvPrefabs == null || tvPrefabs.Length == 0)
        {
            var list = new List<GameObject>();
            string[] tvNames = { "CRT_TV", "TV 1" };
            foreach (var tv in tvNames)
            {
                var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Art/Environment/Backrooms/Decoration/{tv}.prefab");
                if (p != null) list.Add(p);
            }
            if (list.Count > 0) tvPrefabs = list.ToArray();
        }
#endif

        if (grassMaterial == null)
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            grassMaterial = new Material(litShader) { color = new Color(0.24f, 0.58f, 0.18f) };
        }
    }

    // =========================================================================
    // 1. PROCEDURAL ROLLING HILLS TERRAIN
    // =========================================================================
    void BuildRollingHillsMesh()
    {
        GameObject env = new GameObject("Level94_Environment");
        env.transform.SetParent(transform, false);

        terrainObj = new GameObject("Rolling_Hills_Terrain");
        terrainObj.transform.SetParent(env.transform, false);

        int vCountPerSide = gridResolution + 1;
        Vector3[] vertices = new Vector3[vCountPerSide * vCountPerSide];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[gridResolution * gridResolution * 6];

        float halfSize = terrainSize * 0.5f;
        float step = terrainSize / gridResolution;

        // Completely random offsets each run
        float seedOffsetX = Random.Range(-50000f, 50000f);
        float seedOffsetZ = Random.Range(-50000f, 50000f);

        for (int z = 0; z <= gridResolution; z++)
        {
            for (int x = 0; x <= gridResolution; x++)
            {
                int index = z * vCountPerSide + x;
                float worldX = -halfSize + x * step;
                float worldZ = -halfSize + z * step;

                // Multi-octave Perlin noise for broad, smooth hills
                float y1 = Mathf.PerlinNoise((worldX + seedOffsetX) * hillFrequency, (worldZ + seedOffsetZ) * hillFrequency) * hillHeight;
                float y2 = Mathf.PerlinNoise((worldX + seedOffsetX) * secondaryFrequency, (worldZ + seedOffsetZ) * secondaryFrequency) * secondaryHeight;
                float height = y1 + y2;

                // Flatten the center slightly for initial player spawn
                float distFromCenter = Mathf.Sqrt(worldX * worldX + worldZ * worldZ);
                if (distFromCenter < 24f)
                {
                    float centerBlend = Mathf.SmoothStep(0f, 1f, distFromCenter / 24f);
                    height = Mathf.Lerp(4.0f, height, centerBlend);
                }

                // Steep boundary lip to prevent player falling off edges
                float distFromEdge = Mathf.Min(halfSize - Mathf.Abs(worldX), halfSize - Mathf.Abs(worldZ));
                if (distFromEdge < 18f)
                {
                    float wallFactor = 1f - (distFromEdge / 18f);
                    height += wallFactor * wallFactor * 28f;
                }

                vertices[index] = new Vector3(worldX, height, worldZ);
                uvs[index] = new Vector2(worldX * 0.35f, worldZ * 0.35f);
            }
        }

        int triIndex = 0;
        for (int z = 0; z < gridResolution; z++)
        {
            for (int x = 0; x < gridResolution; x++)
            {
                int botL = z * vCountPerSide + x;
                int botR = botL + 1;
                int topL = (z + 1) * vCountPerSide + x;
                int topR = topL + 1;

                triangles[triIndex++] = botL;
                triangles[triIndex++] = topL;
                triangles[triIndex++] = botR;

                triangles[triIndex++] = botR;
                triangles[triIndex++] = topL;
                triangles[triIndex++] = topR;
            }
        }

        terrainMesh = new Mesh();
        terrainMesh.name = "Level94_Terrain_Mesh";
        terrainMesh.vertices = vertices;
        terrainMesh.uv = uvs;
        terrainMesh.triangles = triangles;
        terrainMesh.RecalculateNormals();
        terrainMesh.RecalculateBounds();

        MeshFilter mf = terrainObj.AddComponent<MeshFilter>();
        mf.sharedMesh = terrainMesh;

        MeshRenderer mr = terrainObj.AddComponent<MeshRenderer>();
        mr.sharedMaterial = grassMaterial;

        MeshCollider mc = terrainObj.AddComponent<MeshCollider>();
        mc.sharedMesh = terrainMesh;

        CreatePerimeterColliders(env.transform, halfSize);
    }

    void CreatePerimeterColliders(Transform parent, float halfSize)
    {
        GameObject bounds = new GameObject("Boundary_Colliders");
        bounds.transform.SetParent(parent, false);

        float wallThickness = 4f;
        float wallHeight = 45f;

        CreateBoundWall(bounds.transform, "North", new Vector3(0f, 18f, halfSize), new Vector3(terrainSize, wallHeight, wallThickness));
        CreateBoundWall(bounds.transform, "South", new Vector3(0f, 18f, -halfSize), new Vector3(terrainSize, wallHeight, wallThickness));
        CreateBoundWall(bounds.transform, "East", new Vector3(halfSize, 18f, 0f), new Vector3(wallThickness, wallHeight, terrainSize));
        CreateBoundWall(bounds.transform, "West", new Vector3(-halfSize, 18f, 0f), new Vector3(wallThickness, wallHeight, terrainSize));
    }

    void CreateBoundWall(Transform parent, string name, Vector3 pos, Vector3 size)
    {
        GameObject wall = new GameObject($"Bound_{name}");
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = pos;
        BoxCollider box = wall.AddComponent<BoxCollider>();
        box.size = size;
    }

    // =========================================================================
    // 2. HEIGHT SAMPLING & 4-CORNER GROUND SNAPPING
    // =========================================================================
    public float SampleHeight(float worldX, float worldZ)
    {
        Vector3 rayOrigin = new Vector3(worldX, 150f, worldZ);
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 300f))
        {
            return hit.point.y;
        }
        return 4.0f;
    }

    public Vector3 GetSurfaceNormal(float worldX, float worldZ)
    {
        Vector3 rayOrigin = new Vector3(worldX, 150f, worldZ);
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 300f))
        {
            return hit.normal;
        }
        return Vector3.up;
    }

    bool EvaluateHousePlacement(float x, float z, Quaternion rot, out float finalY)
    {
        finalY = 0f;
        Vector3 center = new Vector3(x, 0f, z);

        Vector3 normal = GetSurfaceNormal(x, z);
        if (Vector3.Angle(normal, Vector3.up) > 13f) return false;

        Vector3 c1 = center + rot * new Vector3(-2.8f, 0f, -0.3f);
        Vector3 c2 = center + rot * new Vector3( 2.8f, 0f, -0.3f);
        Vector3 c3 = center + rot * new Vector3(-2.8f, 0f,  5.2f);
        Vector3 c4 = center + rot * new Vector3( 2.8f, 0f,  5.2f);
        Vector3 door = center + rot * new Vector3(0f, 0f, -0.9f);

        float h1 = SampleHeight(c1.x, c1.z);
        float h2 = SampleHeight(c2.x, c2.z);
        float h3 = SampleHeight(c3.x, c3.z);
        float h4 = SampleHeight(c4.x, c4.z);
        float hDoor = SampleHeight(door.x, door.z);

        float minH = Mathf.Min(Mathf.Min(h1, h2), Mathf.Min(h3, h4));
        float maxH = Mathf.Max(Mathf.Max(h1, h2), Mathf.Max(h3, h4));

        if ((maxH - minH) > 1.45f) return false;

        finalY = Mathf.Max(maxH - 0.12f, hDoor + 0.05f);
        return true;
    }

    // =========================================================================
    // 3. SPAWNING EXIT PORTAL HOUSE
    // =========================================================================
    void SpawnExitHouse()
    {
        if (exitHousePrefab == null) return;

        float angle = Random.Range(0f, Mathf.PI * 2f);
        float dist = Random.Range(minExitDistance, terrainSize * 0.42f);
        float x = Mathf.Cos(angle) * dist;
        float z = Mathf.Sin(angle) * dist;

        Vector3 pos = new Vector3(x, 0f, z);
        Vector3 toCenter = (Vector3.zero - pos).normalized;
        toCenter.y = 0;
        Quaternion rot = Quaternion.LookRotation(toCenter, Vector3.up);

        if (!EvaluateHousePlacement(x, z, rot, out float finalY))
        {
            finalY = SampleHeight(x, z);
        }
        pos.y = finalY;

        Transform group = GetOrCreateGroup("Exit_Structure");
        GameObject exitObj = Instantiate(exitHousePrefab, pos, rot, group);
        exitObj.name = "Level94_Exit_House";
        exitObj.transform.localScale = Vector3.one * houseScale;

        EnsureCollidersOnModel(exitObj);
        placedHousePositions.Add(pos);
        DecorateHouseInterior(exitObj, pos, rot);

        if (streetlightPrefab != null)
        {
            Vector3 leftPost = pos + rot * new Vector3(-5f, 0f, -6f);
            leftPost.y = SampleHeight(leftPost.x, leftPost.z);
            Instantiate(streetlightPrefab, leftPost, rot, group);

            Vector3 rightPost = pos + rot * new Vector3(5f, 0f, -6f);
            rightPost.y = SampleHeight(rightPost.x, rightPost.z);
            Instantiate(streetlightPrefab, rightPost, rot, group);
        }
    }

    // =========================================================================
    // 4. SPAWNING CUSTOM SUBURBAN HOUSES
    // =========================================================================
    void SpawnHouses()
    {
        if (housePrefabs == null || housePrefabs.Length == 0) return;

        Transform group = GetOrCreateGroup("Houses");
        float usableRadius = (terrainSize * 0.5f) - 32f;
        int attempts = houseCount * 16;
        int spawned = 0;

        for (int i = 0; i < attempts && spawned < houseCount; i++)
        {
            float x = Random.Range(-usableRadius, usableRadius);
            float z = Random.Range(-usableRadius, usableRadius);

            if (Mathf.Sqrt(x * x + z * z) < 24f) continue;

            bool tooClose = false;
            foreach (var p in placedHousePositions)
            {
                if (Vector2.Distance(new Vector2(x, z), new Vector2(p.x, p.z)) < minHouseDistance)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            float yaw = Random.Range(0, 4) * 90f + Random.Range(-10f, 10f);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            if (!EvaluateHousePlacement(x, z, rot, out float finalY))
            {
                continue;
            }

            Vector3 spawnPos = new Vector3(x, finalY, z);
            GameObject prefab = housePrefabs[Random.Range(0, housePrefabs.Length)];
            if (prefab == null) continue;

            GameObject house = Instantiate(prefab, spawnPos, rot, group);
            house.name = $"House_{spawned + 1}";
            house.transform.localScale = Vector3.one * houseScale;
            EnsureCollidersOnModel(house);

            placedHousePositions.Add(spawnPos);
            spawned++;

            SpawnHouseProps(spawnPos, rot, group);
            DecorateHouseInterior(house, spawnPos, rot);
        }
    }

    void SpawnHouseProps(Vector3 housePos, Quaternion houseRot, Transform parent)
    {
        if (fencePrefab != null && Random.value < 0.85f)
        {
            Vector3 fenceL = housePos + houseRot * new Vector3(-3.8f, 0f, -1.2f);
            fenceL.y = SampleHeight(fenceL.x, fenceL.z);
            GameObject fl = Instantiate(fencePrefab, fenceL, houseRot, parent);
            fl.transform.localScale = Vector3.one * fenceScale;
            EnsureCollidersOnModel(fl);

            Vector3 fenceR = housePos + houseRot * new Vector3(3.8f, 0f, -1.2f);
            fenceR.y = SampleHeight(fenceR.x, fenceR.z);
            GameObject fr = Instantiate(fencePrefab, fenceR, houseRot, parent);
            fr.transform.localScale = Vector3.one * fenceScale;
            EnsureCollidersOnModel(fr);
        }

        if (mailboxPrefab != null && Random.value < 0.75f)
        {
            Vector3 mbPos = housePos + houseRot * new Vector3(1.8f, 0f, -1.6f);
            mbPos.y = SampleHeight(mbPos.x, mbPos.z);
            Instantiate(mailboxPrefab, mbPos, houseRot, parent);
        }
    }

    void DecorateHouseInterior(GameObject house, Vector3 housePos, Quaternion houseRot)
    {
        Transform interiorGroup = new GameObject("Interior_Decorations").transform;
        interiorGroup.SetParent(house.transform, true);

        // 1. Bed (Back-left bedroom corner: exact human scale 1.4m x 0.9m x 2.0m)
        Vector3 bedLocal = new Vector3(-1.65f, 0.05f, 3.65f);
        Quaternion bedRot = houseRot * Quaternion.Euler(0f, 90f, 0f);
        Vector3 bedWorld = housePos + houseRot * bedLocal;
        CreateHouseBed(interiorGroup, bedWorld, bedRot);

        // 2. Locker / Hide Spot (Right wall: exact human scale 0.86m x 2.22m x 0.82m)
        if (spawnLockers)
        {
            Vector3 lockerLocal = new Vector3(1.95f, 0.05f, 2.45f);
            Vector3 lockerWorld = housePos + houseRot * lockerLocal;
            Vector3 lockerForward = houseRot * Vector3.left; // Faces into room center

            LockerHideSpot locker = LockerHideSpot.Create(interiorGroup, lockerWorld, lockerForward);
            if (locker != null) locker.name = "House_Locker";
        }

        // 3. Bedside Nightstand with Almond Water (next to bed)
        Vector3 standLocal = new Vector3(-0.75f, 0.05f, 4.35f);
        Vector3 standWorld = housePos + houseRot * standLocal;
        CreateNightstand(interiorGroup, standWorld, houseRot);

        if (Random.value < almondWaterChance)
        {
            GameObject bottle = new GameObject("House_AlmondWater");
            bottle.transform.position = standWorld + Vector3.up * 0.58f;
            bottle.transform.SetParent(interiorGroup, true);
            bottle.AddComponent<AlmondWaterPickup>();
        }

        // 4. Living Area Table (Front-left: 1.2m x 0.65m x 0.8m)
        if (Random.value < 0.80f)
        {
            Vector3 tableLocal = new Vector3(-1.35f, 0.05f, 1.85f);
            Vector3 tableWorld = housePos + houseRot * tableLocal;
            Quaternion tableRot = houseRot * Quaternion.Euler(0f, Random.Range(-10f, 10f), 0f);
            CreateLivingTable(interiorGroup, tableWorld, tableRot);
        }

        // 5. Warm Atmospheric Interior Light
        if (enableInteriorLights)
        {
            GameObject lightGo = new GameObject("Interior_WarmLight");
            lightGo.transform.SetParent(interiorGroup, false);
            lightGo.transform.localPosition = new Vector3(0f, 2.2f, 2.6f);

            Light interiorLight = lightGo.AddComponent<Light>();
            interiorLight.type = LightType.Point;
            interiorLight.color = new Color(1.0f, 0.88f, 0.68f);
            interiorLight.intensity = 1.35f;
            interiorLight.range = 7.0f;
            interiorLight.shadows = LightShadows.Soft;
        }
    }

    GameObject CreateHouseBed(Transform parent, Vector3 worldPos, Quaternion worldRot)
    {
        GameObject bedGo = new GameObject("Bedroom_Bed");
        bedGo.transform.SetParent(parent, false);
        bedGo.transform.position = worldPos;
        bedGo.transform.rotation = worldRot;

        Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material woodMat = new Material(litShader) { color = new Color(0.35f, 0.22f, 0.12f) };
        Material sheetMat = new Material(litShader) { color = new Color(0.92f, 0.90f, 0.84f) };

        List<CombineInstance> combines = new List<CombineInstance>();

        // 1. Bed Frame: 1.40m wide, 0.32m high, 2.00m long
        Mesh frameMesh = CreateBoxMesh(1.40f, 0.30f, 2.00f, new Vector3(0f, 0.15f, 0f));
        combines.Add(new CombineInstance { mesh = frameMesh, transform = Matrix4x4.identity });

        // 2. Headboard: 1.40m wide, 0.85m high, 0.08m thick at head (+Z)
        Mesh headboardMesh = CreateBoxMesh(1.40f, 0.85f, 0.08f, new Vector3(0f, 0.425f, 0.96f));
        combines.Add(new CombineInstance { mesh = headboardMesh, transform = Matrix4x4.identity });

        // 3. Mattress: 1.30m wide, 0.22m high, 1.88m long
        Mesh mattressMesh = CreateBoxMesh(1.30f, 0.22f, 1.88f, new Vector3(0f, 0.41f, -0.04f));
        combines.Add(new CombineInstance { mesh = mattressMesh, transform = Matrix4x4.identity });

        // 4. Pillows (2 side by side)
        Mesh p1 = CreateBoxMesh(0.50f, 0.10f, 0.32f, new Vector3(-0.32f, 0.55f, 0.65f));
        Mesh p2 = CreateBoxMesh(0.50f, 0.10f, 0.32f, new Vector3(0.32f, 0.55f, 0.65f));
        combines.Add(new CombineInstance { mesh = p1, transform = Matrix4x4.identity });
        combines.Add(new CombineInstance { mesh = p2, transform = Matrix4x4.identity });

        Mesh finalMesh = new Mesh();
        finalMesh.name = "House_Bed_Mesh";
        finalMesh.CombineMeshes(combines.ToArray(), true, false);
        finalMesh.RecalculateNormals();

        MeshFilter mf = bedGo.AddComponent<MeshFilter>();
        mf.sharedMesh = finalMesh;

        MeshRenderer mr = bedGo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = sheetMat;

        BoxCollider col = bedGo.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.45f, 0f);
        col.size = new Vector3(1.40f, 0.90f, 2.00f);

        return bedGo;
    }

    GameObject CreateNightstand(Transform parent, Vector3 worldPos, Quaternion worldRot)
    {
        GameObject standGo = new GameObject("Bedside_Nightstand");
        standGo.transform.SetParent(parent, false);
        standGo.transform.position = worldPos;
        standGo.transform.rotation = worldRot;

        Mesh standMesh = CreateBoxMesh(0.48f, 0.52f, 0.42f, new Vector3(0f, 0.26f, 0f));
        MeshFilter mf = standGo.AddComponent<MeshFilter>();
        mf.sharedMesh = standMesh;

        MeshRenderer mr = standGo.AddComponent<MeshRenderer>();
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mr.sharedMaterial = new Material(litShader) { color = new Color(0.35f, 0.22f, 0.12f) };

        BoxCollider col = standGo.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.26f, 0f);
        col.size = new Vector3(0.48f, 0.52f, 0.42f);

        return standGo;
    }

    GameObject CreateLivingTable(Transform parent, Vector3 worldPos, Quaternion worldRot)
    {
        GameObject tableGo = new GameObject("Living_Table");
        tableGo.transform.SetParent(parent, false);
        tableGo.transform.position = worldPos;
        tableGo.transform.rotation = worldRot;

        Mesh tableMesh = CreateBoxMesh(1.20f, 0.60f, 0.75f, new Vector3(0f, 0.30f, 0f));
        MeshFilter mf = tableGo.AddComponent<MeshFilter>();
        mf.sharedMesh = tableMesh;

        MeshRenderer mr = tableGo.AddComponent<MeshRenderer>();
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mr.sharedMaterial = new Material(litShader) { color = new Color(0.45f, 0.32f, 0.20f) };

        BoxCollider col = tableGo.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.30f, 0f);
        col.size = new Vector3(1.20f, 0.60f, 0.75f);

        return tableGo;
    }

    Mesh CreateBoxMesh(float width, float height, float depth, Vector3 offset)
    {
        Mesh m = new Mesh();
        float hw = width * 0.5f;
        float hh = height * 0.5f;
        float hd = depth * 0.5f;

        Vector3[] v = new Vector3[8]
        {
            offset + new Vector3(-hw, -hh, -hd),
            offset + new Vector3( hw, -hh, -hd),
            offset + new Vector3( hw, -hh,  hd),
            offset + new Vector3(-hw, -hh,  hd),
            offset + new Vector3(-hw,  hh, -hd),
            offset + new Vector3( hw,  hh, -hd),
            offset + new Vector3( hw,  hh,  hd),
            offset + new Vector3(-hw,  hh,  hd)
        };

        int[] tris = new int[36]
        {
            0, 2, 1, 0, 3, 2, // bottom
            4, 5, 6, 4, 6, 7, // top
            0, 1, 5, 0, 5, 4, // front
            2, 3, 7, 2, 7, 6, // back
            3, 0, 4, 3, 4, 7, // left
            1, 2, 6, 1, 6, 5  // right
        };

        m.vertices = v;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // =========================================================================
    // 5. WATER TOWERS (LANDMARKS ON HILL PEAKS)
    // =========================================================================
    void SpawnWaterTowers()
    {
        if (waterTowerPrefab == null) return;

        Transform group = GetOrCreateGroup("WaterTowers");
        float usableRadius = (terrainSize * 0.5f) - 30f;
        int attempts = waterTowerCount * 20;
        int spawned = 0;

        for (int i = 0; i < attempts && spawned < waterTowerCount; i++)
        {
            float x = Random.Range(-usableRadius, usableRadius);
            float z = Random.Range(-usableRadius, usableRadius);

            float distFromCenter = Mathf.Sqrt(x * x + z * z);
            if (distFromCenter < 50f) continue;

            float y = SampleHeight(x, z);
            if (y < (hillHeight * 0.60f)) continue;

            Vector3 normal = GetSurfaceNormal(x, z);
            if (Vector3.Angle(normal, Vector3.up) > 15f) continue;

            Vector3 pos = new Vector3(x, y - 0.2f, z);
            Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            GameObject tower = Instantiate(waterTowerPrefab, pos, rot, group);
            tower.name = $"WaterTower_{spawned + 1}";
            tower.transform.localScale = Vector3.one * waterTowerScale;
            EnsureCollidersOnModel(tower);

            spawned++;
        }
    }

    // =========================================================================
    // 6. SUBURBAN STREETLIGHTS
    // =========================================================================
    void SpawnStreetlightsAndProps()
    {
        if (streetlightPrefab == null) return;

        Transform group = GetOrCreateGroup("Streetlights");
        float usableRadius = (terrainSize * 0.5f) - 25f;

        for (int i = 0; i < streetlightCount; i++)
        {
            float x = Random.Range(-usableRadius, usableRadius);
            float z = Random.Range(-usableRadius, usableRadius);

            float y = SampleHeight(x, z);
            Vector3 pos = new Vector3(x, y, z);
            Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            Instantiate(streetlightPrefab, pos, rot, group);
        }
    }

    // =========================================================================
    // 7. PERIMETER BORDER FENCES
    // =========================================================================
    void SpawnPerimeterBorderFences()
    {
        if (!generateBorderFences || fencePrefab == null) return;

        // Auto-correct any uninitialized or 0 values from existing scene files
        if (borderFenceScale < 0.1f) borderFenceScale = 1.0f;
        if (borderInset < 5f || borderInset > (terrainSize * 0.45f)) borderInset = 22f;

        Transform group = GetOrCreateGroup("Boundary_Fences");
        float halfSize = terrainSize * 0.5f;
        float boundary = halfSize - borderInset;
        float segLength = Mathf.Max(4.10f * borderFenceScale, 2.0f);

        // North Border: (X: -boundary to +boundary, Z: +boundary)
        SpawnFenceLine(new Vector2(-boundary, boundary), new Vector2(boundary, boundary), segLength, 0f, group);

        // South Border: (X: -boundary to +boundary, Z: -boundary)
        SpawnFenceLine(new Vector2(-boundary, -boundary), new Vector2(boundary, -boundary), segLength, 0f, group);

        // East Border: (X: +boundary, Z: -boundary to +boundary)
        SpawnFenceLine(new Vector2(boundary, -boundary), new Vector2(boundary, boundary), segLength, 90f, group);

        // West Border: (X: -boundary, Z: -boundary to +boundary)
        SpawnFenceLine(new Vector2(-boundary, -boundary), new Vector2(-boundary, boundary), segLength, 90f, group);
    }

    void SpawnFenceLine(Vector2 start, Vector2 end, float segLength, float baseAngle, Transform parent)
    {
        if (segLength < 1.0f) segLength = 4.10f;
        float totalDist = Vector2.Distance(start, end);
        // Hard-clamp maximum fence count to 75 so it can NEVER loop uncontrollably
        int count = Mathf.Clamp(Mathf.FloorToInt(totalDist / segLength), 1, 75);
        Vector2 dir = (end - start).normalized;

        for (int i = 0; i < count; i++)
        {
            float t = (i + 0.5f) * segLength;
            Vector2 p2D = start + dir * t;

            float y = SampleHeight(p2D.x, p2D.y);

            // Sample ground slope across the fence segment
            Vector2 pStart = p2D - dir * (segLength * 0.5f);
            Vector2 pEnd = p2D + dir * (segLength * 0.5f);
            float yStart = SampleHeight(pStart.x, pStart.y);
            float yEnd = SampleHeight(pEnd.x, pEnd.y);
            float slopeAngle = Mathf.Atan2(yEnd - yStart, segLength) * Mathf.Rad2Deg;

            // Posts sink slightly into the grass for seamless contact
            Vector3 pos = new Vector3(p2D.x, y - 0.08f, p2D.y);
            Quaternion rot;
            if (Mathf.Approximately(baseAngle, 0f))
            {
                // North/South line: runs along X axis, tilts with slope
                rot = Quaternion.Euler(0f, 0f, slopeAngle);
            }
            else
            {
                // East/West line: runs along Z axis (90 deg yaw), tilts with slope
                rot = Quaternion.Euler(-slopeAngle, 90f, 0f);
            }

            GameObject fence = Instantiate(fencePrefab, pos, rot, parent);
            fence.name = $"Border_Fence_{i}";
            fence.transform.localScale = Vector3.one * borderFenceScale;
            EnsureCollidersOnModel(fence);
        }
    }

    void EnsureCollidersOnModel(GameObject obj)
    {
        MeshRenderer[] renderers = obj.GetComponentsInChildren<MeshRenderer>();
        foreach (var r in renderers)
        {
            if (r.GetComponent<Collider>() == null)
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    MeshCollider mc = r.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                }
            }
        }
    }

    // =========================================================================
    // 7. COMPLETE PLAYER & CINEMACHINE CAMERA BINDING / HEALING
    // =========================================================================
    void BindAndHealPlayerAndCamera()
    {
        // 1. Locate player GameObject
        GameObject playerObj = null;
        if (player != null) playerObj = player.gameObject;
        if (playerObj == null) playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null)
        {
            var fpcInScene = Object.FindAnyObjectByType<StarterAssets.FirstPersonController>();
            if (fpcInScene != null) playerObj = fpcInScene.gameObject;
        }

        if (playerObj == null && playerPrefab != null)
        {
            playerObj = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
            playerObj.name = "PlayerCapsule";
            playerObj.tag = "Player";
        }

        if (playerObj == null) return;
        player = playerObj.transform;

        // 2. Locate or create PlayerCameraRoot
        Transform cameraRoot = playerObj.transform.Find("PlayerCameraRoot");
        if (cameraRoot == null)
        {
            foreach (Transform child in playerObj.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "PlayerCameraRoot")
                {
                    cameraRoot = child;
                    break;
                }
            }
        }
        if (cameraRoot == null)
        {
            GameObject crGo = new GameObject("PlayerCameraRoot");
            cameraRoot = crGo.transform;
            cameraRoot.SetParent(playerObj.transform, false);
            cameraRoot.localPosition = new Vector3(0f, 1.375f, 0f);
        }

        // 3. Connect FirstPersonController to CameraTarget so it NEVER throws NullReferenceException
        var fpc = playerObj.GetComponent<StarterAssets.FirstPersonController>();
        if (fpc != null)
        {
            fpc.CinemachineCameraTarget = cameraRoot.gameObject;
        }

        // 4. Find Cinemachine Camera and assign Target / Follow
        var cmCam = Object.FindAnyObjectByType<Unity.Cinemachine.CinemachineCamera>();
        if (cmCam != null)
        {
            cmCam.Follow = cameraRoot;
            cmCam.LookAt = cameraRoot;
        }

        // 5. Ensure MainCamera has CinemachineBrain
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            mainCam = Object.FindAnyObjectByType<Camera>();
        }
        if (mainCam != null)
        {
            var brain = mainCam.GetComponent<Unity.Cinemachine.CinemachineBrain>();
            if (brain == null)
            {
                brain = mainCam.gameObject.AddComponent<Unity.Cinemachine.CinemachineBrain>();
            }
        }

        // 6. Connect Mobile Touch Inputs / Virtual Input
        var vInput = Object.FindAnyObjectByType<StarterAssets.VirtualInput>();
        var sInput = playerObj.GetComponent<StarterAssets.StarterAssetsInputs>();
        if (vInput != null && sInput != null)
        {
            vInput.StarterAssetsInputs = sInput;
        }

        // Lock cursor for desktop editor testing
        if (sInput != null)
        {
            sInput.cursorLocked = true;
            sInput.cursorInputForLook = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // 7. Connect PlayerLockerController for hiding in house lockers
        if (playerObj.GetComponent<PlayerLockerController>() == null)
        {
            playerObj.AddComponent<PlayerLockerController>();
        }

        // 8. Teleport Player onto the generated terrain without CharacterController jamming
        var cc = playerObj.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        float groundY = SampleHeight(0f, 0f);
        playerObj.transform.position = new Vector3(0f, groundY + 1.2f, 0f);

        if (cameraRoot != null && cmCam != null)
        {
            cmCam.transform.position = cameraRoot.position;
            if (mainCam != null) mainCam.transform.position = cameraRoot.position;
        }

        if (cc != null) cc.enabled = true;
    }

    void EnsureAtmosphere()
    {
        Level94Atmosphere atmos = GetComponent<Level94Atmosphere>();
        if (atmos == null) atmos = gameObject.AddComponent<Level94Atmosphere>();
        atmos.ApplySettings();
    }

    void EnsureHUDAndControllers()
    {
        if (Object.FindAnyObjectByType<VictoryController>() == null)
        {
            new GameObject("VictoryController").AddComponent<VictoryController>();
        }
        if (Object.FindAnyObjectByType<PauseMenuController>() == null)
        {
            new GameObject("PauseMenuController").AddComponent<PauseMenuController>();
        }
    }

    void BakeNavMesh()
    {
        var surface = terrainObj.GetComponent<NavMeshSurface>();
        if (surface == null) surface = terrainObj.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
    }

    Transform GetOrCreateGroup(string name)
    {
        Transform env = transform.Find("Level94_Environment");
        if (env == null)
        {
            env = new GameObject("Level94_Environment").transform;
            env.SetParent(transform, false);
        }

        Transform g = env.Find(name);
        if (g == null)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(env, false);
            g = go.transform;
        }
        return g;
    }
}
