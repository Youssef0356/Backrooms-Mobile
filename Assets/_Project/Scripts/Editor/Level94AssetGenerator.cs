#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Procedurally generates stylized, mobile-optimized 3D suburban house models and props
/// for Backrooms Level 94 ("Motion"), and saves them as ready-to-use prefabs.
/// </summary>
public static class Level94AssetGenerator
{
    private const string BaseDir = "Assets/_Project/Art/Level94";
    private const string MatDir = "Assets/_Project/Art/Level94/Materials";
    private const string PrefabDir = "Assets/_Project/Art/Level94/Prefabs";

    [MenuItem("Tools/Backrooms/Prefabs & Assets/Generate Level 94 House & Prop Prefabs", priority = 20)]
    public static void GenerateAllAssets()
    {
        EnsureDirectories();
        var materials = CreateMaterials();

        CreateClassicGableHouse(materials);
        CreateTallDormerHouse(materials);
        CreateRanchBungalowHouse(materials);
        CreateStreetlight(materials);
        CreatePicketFence(materials);
        CreateMailbox(materials);
        CreateExitPortalHouse(materials);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Level94AssetGenerator] All Level 94 3D house and prop prefabs generated successfully in " + PrefabDir);
    }

    private static void EnsureDirectories()
    {
        if (!Directory.Exists(BaseDir)) Directory.CreateDirectory(BaseDir);
        if (!Directory.Exists(MatDir)) Directory.CreateDirectory(MatDir);
        if (!Directory.Exists(PrefabDir)) Directory.CreateDirectory(PrefabDir);
    }

    public struct Level94Mats
    {
        public Material grass;
        public Material creamSiding;
        public Material blueSiding;
        public Material sageSiding;
        public Material roof;
        public Material whiteTrim;
        public Material concrete;
        public Material glowingWindow;
        public Material darkWindow;
        public Material woodDoor;
        public Material metal;
        public Material bulb;
    }

    public static Level94Mats CreateMaterials()
    {
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Level94Mats m = new Level94Mats();

        m.grass = GetOrCreateMat("Grass_Level94", litShader, mat =>
        {
            mat.color = new Color(0.24f, 0.58f, 0.18f); // Eerie, hyper-vibrant suburban lawn green
        });

        m.creamSiding = GetOrCreateMat("Siding_PastelCream", litShader, mat =>
        {
            mat.color = new Color(0.92f, 0.88f, 0.78f);
        });

        m.blueSiding = GetOrCreateMat("Siding_PaleBlue", litShader, mat =>
        {
            mat.color = new Color(0.70f, 0.78f, 0.85f);
        });

        m.sageSiding = GetOrCreateMat("Siding_SageGreen", litShader, mat =>
        {
            mat.color = new Color(0.72f, 0.80f, 0.73f);
        });

        m.roof = GetOrCreateMat("Roof_DarkShingles", litShader, mat =>
        {
            mat.color = new Color(0.18f, 0.20f, 0.22f);
        });

        m.whiteTrim = GetOrCreateMat("Wood_Trim_White", litShader, mat =>
        {
            mat.color = new Color(0.95f, 0.95f, 0.93f);
        });

        m.concrete = GetOrCreateMat("Foundation_Concrete", litShader, mat =>
        {
            mat.color = new Color(0.48f, 0.48f, 0.47f);
        });

        m.glowingWindow = GetOrCreateMat("Window_EerieGlow", litShader, mat =>
        {
            mat.color = new Color(1.0f, 0.92f, 0.65f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(1.0f, 0.82f, 0.45f) * 1.6f);
        });

        m.darkWindow = GetOrCreateMat("Window_DarkVoid", litShader, mat =>
        {
            mat.color = new Color(0.08f, 0.09f, 0.12f);
        });

        m.woodDoor = GetOrCreateMat("Door_Wood", litShader, mat =>
        {
            mat.color = new Color(0.38f, 0.22f, 0.12f);
        });

        m.metal = GetOrCreateMat("Streetlight_Metal", litShader, mat =>
        {
            mat.color = new Color(0.15f, 0.16f, 0.18f);
        });

        m.bulb = GetOrCreateMat("Streetlight_Bulb", litShader, mat =>
        {
            mat.color = new Color(1.0f, 0.95f, 0.8f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(1.0f, 0.90f, 0.65f) * 3.0f);
        });

        return m;
    }

    private static Material GetOrCreateMat(string name, Shader shader, System.Action<Material> setup)
    {
        string path = $"{MatDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader) { name = name };
            setup(m);
            AssetDatabase.CreateAsset(m, path);
        }
        else
        {
            setup(m);
            EditorUtility.SetDirty(m);
        }
        return m;
    }

    // =========================================================================
    // HOUSE 1: CLASSIC SUBURBAN GABLE
    // =========================================================================
    private static void CreateClassicGableHouse(Level94Mats mats)
    {
        GameObject root = new GameObject("House_Classic_Gable");

        // Dimensions: 8m wide x 6m deep x 6.5m high
        const float w = 8.0f;
        const float d = 6.0f;
        const float wallH = 4.2f;

        // Foundation (sits slightly into ground to prevent floating)
        CreateBox(root.transform, "Foundation", new Vector3(0f, 0.25f, 0f), new Vector3(w + 0.3f, 0.5f, d + 0.3f), mats.concrete, true);

        // Main Walls
        CreateBox(root.transform, "Walls_Main", new Vector3(0f, 0.5f + wallH * 0.5f, 0f), new Vector3(w, wallH, d), mats.creamSiding, true);

        // Roof: sloped slabs
        CreateSlopedRoof(root.transform, w + 0.8f, d + 0.8f, wallH + 0.5f, 2.2f, mats.roof, mats.creamSiding);

        // Chimney
        CreateBox(root.transform, "Chimney", new Vector3(2.4f, wallH + 2.0f, 0.8f), new Vector3(0.9f, 2.4f, 0.9f), mats.roof, true);

        // Front Porch (Z = -d/2 = -3.0f)
        float frontZ = -d * 0.5f;
        CreateBox(root.transform, "Porch_Deck", new Vector3(0f, 0.25f, frontZ - 1.0f), new Vector3(3.6f, 0.3f, 2.0f), mats.whiteTrim, true);
        CreateBox(root.transform, "Porch_Roof", new Vector3(0f, 2.8f, frontZ - 0.9f), new Vector3(3.8f, 0.25f, 2.0f), mats.roof, true);
        CreateBox(root.transform, "Porch_Pillar_L", new Vector3(-1.6f, 1.5f, frontZ - 1.8f), new Vector3(0.18f, 2.6f, 0.18f), mats.whiteTrim, false);
        CreateBox(root.transform, "Porch_Pillar_R", new Vector3(1.6f, 1.5f, frontZ - 1.8f), new Vector3(0.18f, 2.6f, 0.18f), mats.whiteTrim, false);
        CreateBox(root.transform, "Porch_Step", new Vector3(0f, 0.12f, frontZ - 2.2f), new Vector3(1.8f, 0.24f, 0.5f), mats.concrete, true);

        // Front Door
        CreateBox(root.transform, "Door_Frame", new Vector3(0f, 1.55f, frontZ - 0.05f), new Vector3(1.3f, 2.3f, 0.15f), mats.whiteTrim, false);
        CreateBox(root.transform, "Door", new Vector3(0f, 1.5f, frontZ - 0.08f), new Vector3(1.1f, 2.1f, 0.1f), mats.woodDoor, false);

        // Windows (Front)
        CreateWindow(root.transform, new Vector3(-2.4f, 1.6f, frontZ - 0.05f), new Vector3(1.2f, 1.5f, 0.1f), mats.whiteTrim, mats.glowingWindow);
        CreateWindow(root.transform, new Vector3(2.4f, 1.6f, frontZ - 0.05f), new Vector3(1.2f, 1.5f, 0.1f), mats.whiteTrim, mats.glowingWindow);
        CreateWindow(root.transform, new Vector3(-2.4f, 3.6f, frontZ - 0.05f), new Vector3(1.1f, 1.3f, 0.1f), mats.whiteTrim, mats.glowingWindow);
        CreateWindow(root.transform, new Vector3(0f, 3.6f, frontZ - 0.05f), new Vector3(1.1f, 1.3f, 0.1f), mats.whiteTrim, mats.darkWindow);
        CreateWindow(root.transform, new Vector3(2.4f, 3.6f, frontZ - 0.05f), new Vector3(1.1f, 1.3f, 0.1f), mats.whiteTrim, mats.glowingWindow);

        // Add overall compound collider
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 3.0f, -0.2f);
        box.size = new Vector3(w + 0.4f, 6.2f, d + 2.2f);

        SavePrefab(root, "House_Classic_Gable");
    }

    // =========================================================================
    // HOUSE 2: TALL DORMER SUBURBAN HOUSE
    // =========================================================================
    private static void CreateTallDormerHouse(Level94Mats mats)
    {
        GameObject root = new GameObject("House_Tall_Dormer");

        const float w = 7.4f;
        const float d = 6.4f;
        const float wallH = 5.0f;

        CreateBox(root.transform, "Foundation", new Vector3(0f, 0.25f, 0f), new Vector3(w + 0.3f, 0.5f, d + 0.3f), mats.concrete, true);
        CreateBox(root.transform, "Walls_Main", new Vector3(0f, 0.5f + wallH * 0.5f, 0f), new Vector3(w, wallH, d), mats.blueSiding, true);

        // Steep Roof
        CreateSlopedRoof(root.transform, w + 0.8f, d + 0.8f, wallH + 0.5f, 2.8f, mats.roof, mats.blueSiding);

        // Dormer Windows on front slope
        float frontZ = -d * 0.5f;
        CreateDormer(root.transform, new Vector3(-1.8f, wallH + 1.2f, frontZ + 0.6f), mats);
        CreateDormer(root.transform, new Vector3(1.8f, wallH + 1.2f, frontZ + 0.6f), mats);

        // Front Door & Entrance Awning
        CreateBox(root.transform, "Door_Frame", new Vector3(0f, 1.55f, frontZ - 0.05f), new Vector3(1.3f, 2.3f, 0.15f), mats.whiteTrim, false);
        CreateBox(root.transform, "Door", new Vector3(0f, 1.5f, frontZ - 0.08f), new Vector3(1.1f, 2.1f, 0.1f), mats.woodDoor, false);
        CreateBox(root.transform, "Awning", new Vector3(0f, 2.9f, frontZ - 0.6f), new Vector3(2.0f, 0.2f, 1.2f), mats.roof, false);
        CreateBox(root.transform, "Steps", new Vector3(0f, 0.18f, frontZ - 0.6f), new Vector3(1.8f, 0.35f, 1.0f), mats.concrete, true);

        // Windows Lower & Upper Floors
        CreateWindow(root.transform, new Vector3(-2.2f, 1.7f, frontZ - 0.05f), new Vector3(1.2f, 1.6f, 0.1f), mats.whiteTrim, mats.glowingWindow);
        CreateWindow(root.transform, new Vector3(2.2f, 1.7f, frontZ - 0.05f), new Vector3(1.2f, 1.6f, 0.1f), mats.whiteTrim, mats.glowingWindow);
        CreateWindow(root.transform, new Vector3(-2.2f, 3.8f, frontZ - 0.05f), new Vector3(1.2f, 1.4f, 0.1f), mats.whiteTrim, mats.darkWindow);
        CreateWindow(root.transform, new Vector3(2.2f, 3.8f, frontZ - 0.05f), new Vector3(1.2f, 1.4f, 0.1f), mats.whiteTrim, mats.glowingWindow);

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 3.6f, -0.1f);
        box.size = new Vector3(w + 0.4f, 7.5f, d + 1.4f);

        SavePrefab(root, "House_Tall_Dormer");
    }

    // =========================================================================
    // HOUSE 3: RANCH BUNGALOW
    // =========================================================================
    private static void CreateRanchBungalowHouse(Level94Mats mats)
    {
        GameObject root = new GameObject("House_Ranch_Bungalow");

        const float w = 10.5f;
        const float d = 5.5f;
        const float wallH = 3.3f;

        CreateBox(root.transform, "Foundation", new Vector3(0f, 0.25f, 0f), new Vector3(w + 0.3f, 0.5f, d + 0.3f), mats.concrete, true);
        CreateBox(root.transform, "Walls_Main", new Vector3(0f, 0.5f + wallH * 0.5f, 0f), new Vector3(w, wallH, d), mats.sageSiding, true);

        // Low profile roof
        CreateSlopedRoof(root.transform, w + 0.8f, d + 0.8f, wallH + 0.5f, 1.6f, mats.roof, mats.sageSiding);

        float frontZ = -d * 0.5f;

        // Front Porch across left half
        CreateBox(root.transform, "Porch_Deck", new Vector3(-2.2f, 0.22f, frontZ - 0.8f), new Vector3(4.8f, 0.3f, 1.6f), mats.whiteTrim, true);
        CreateBox(root.transform, "Porch_Roof", new Vector3(-2.2f, 2.7f, frontZ - 0.8f), new Vector3(5.0f, 0.2f, 1.7f), mats.roof, false);
        CreateBox(root.transform, "Porch_Pillar_1", new Vector3(-4.2f, 1.4f, frontZ - 1.4f), new Vector3(0.16f, 2.5f, 0.16f), mats.whiteTrim, false);
        CreateBox(root.transform, "Porch_Pillar_2", new Vector3(-0.2f, 1.4f, frontZ - 1.4f), new Vector3(0.16f, 2.5f, 0.16f), mats.whiteTrim, false);

        // Front Door
        CreateBox(root.transform, "Door", new Vector3(-1.8f, 1.4f, frontZ - 0.06f), new Vector3(1.1f, 2.0f, 0.1f), mats.woodDoor, false);

        // Big picture window
        CreateWindow(root.transform, new Vector3(2.6f, 1.5f, frontZ - 0.05f), new Vector3(2.8f, 1.5f, 0.1f), mats.whiteTrim, mats.glowingWindow);
        CreateWindow(root.transform, new Vector3(-3.4f, 1.5f, frontZ - 0.05f), new Vector3(1.2f, 1.3f, 0.1f), mats.whiteTrim, mats.glowingWindow);

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 2.2f, -0.4f);
        box.size = new Vector3(w + 0.4f, 4.8f, d + 1.8f);

        SavePrefab(root, "House_Ranch_Bungalow");
    }

    // =========================================================================
    // EXIT PORTAL HOUSE (Special Anomaly Exit House)
    // =========================================================================
    private static void CreateExitPortalHouse(Level94Mats mats)
    {
        GameObject root = new GameObject("Level94_ExitHouse");

        const float w = 8.5f;
        const float d = 6.5f;
        const float wallH = 4.5f;

        CreateBox(root.transform, "Foundation", new Vector3(0f, 0.25f, 0f), new Vector3(w + 0.3f, 0.5f, d + 0.3f), mats.concrete, true);
        CreateBox(root.transform, "Walls_Main", new Vector3(0f, 0.5f + wallH * 0.5f, 0f), new Vector3(w, wallH, d), mats.creamSiding, true);
        CreateSlopedRoof(root.transform, w + 0.8f, d + 0.8f, wallH + 0.5f, 2.3f, mats.roof, mats.creamSiding);

        float frontZ = -d * 0.5f;

        // Front Porch
        CreateBox(root.transform, "Porch_Deck", new Vector3(0f, 0.25f, frontZ - 1.1f), new Vector3(3.8f, 0.3f, 2.2f), mats.whiteTrim, true);
        CreateBox(root.transform, "Porch_Roof", new Vector3(0f, 3.0f, frontZ - 1.0f), new Vector3(4.0f, 0.25f, 2.2f), mats.roof, false);
        CreateBox(root.transform, "Porch_Pillar_L", new Vector3(-1.7f, 1.6f, frontZ - 1.9f), new Vector3(0.18f, 2.8f, 0.18f), mats.whiteTrim, false);
        CreateBox(root.transform, "Porch_Pillar_R", new Vector3(1.7f, 1.6f, frontZ - 1.9f), new Vector3(0.18f, 2.8f, 0.18f), mats.whiteTrim, false);

        // OPEN PORTAL DOORWAY IN THE CENTER:
        Vector3 portalPos = new Vector3(0f, 0.4f, frontZ - 0.1f);
        GameObject exitObj = BackroomsExitZone.BuildExitPortal(portalPos, Quaternion.identity, mats.whiteTrim, mats.bulb);
        exitObj.transform.SetParent(root.transform, false);

        // Glowing Windows
        CreateWindow(root.transform, new Vector3(-2.6f, 1.8f, frontZ - 0.05f), new Vector3(1.3f, 1.6f, 0.1f), mats.whiteTrim, mats.glowingWindow);
        CreateWindow(root.transform, new Vector3(2.6f, 1.8f, frontZ - 0.05f), new Vector3(1.3f, 1.6f, 0.1f), mats.whiteTrim, mats.glowingWindow);

        // Emergency Exit Beacon Light above porch
        GameObject beaconObj = new GameObject("Portal_Beacon");
        beaconObj.transform.SetParent(root.transform, false);
        beaconObj.transform.localPosition = new Vector3(0f, 4.0f, frontZ - 1.2f);
        Light beacon = beaconObj.AddComponent<Light>();
        beacon.type = LightType.Point;
        beacon.color = new Color(0.2f, 1.0f, 0.5f);
        beacon.intensity = 3.5f;
        beacon.range = 16.0f;

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 3.0f, 0.2f);
        box.size = new Vector3(w + 0.4f, 6.5f, d + 0.8f);

        SavePrefab(root, "Level94_ExitHouse");
    }

    // =========================================================================
    // SUBURBAN PROPS
    // =========================================================================
    private static void CreateStreetlight(Level94Mats mats)
    {
        GameObject root = new GameObject("Suburban_Streetlight");

        // Vertical pole (5.5m high)
        CreateBox(root.transform, "Pole", new Vector3(0f, 2.75f, 0f), new Vector3(0.22f, 5.5f, 0.22f), mats.metal, true);
        // Base plate
        CreateBox(root.transform, "Base", new Vector3(0f, 0.15f, 0f), new Vector3(0.55f, 0.3f, 0.55f), mats.metal, true);
        // Horizontal arm
        CreateBox(root.transform, "Arm", new Vector3(0.6f, 5.4f, 0f), new Vector3(1.2f, 0.14f, 0.14f), mats.metal, false);
        // Lamp head
        CreateBox(root.transform, "Lamp_Head", new Vector3(1.1f, 5.25f, 0f), new Vector3(0.5f, 0.18f, 0.35f), mats.metal, false);
        CreateBox(root.transform, "Bulb", new Vector3(1.1f, 5.12f, 0f), new Vector3(0.35f, 0.08f, 0.25f), mats.bulb, false);

        // Real Light Source (casts warm eerie pool of light on the green grass)
        GameObject lightObj = new GameObject("Street_Light");
        lightObj.transform.SetParent(root.transform, false);
        lightObj.transform.localPosition = new Vector3(1.1f, 4.9f, 0f);

        Light pt = lightObj.AddComponent<Light>();
        pt.type = LightType.Point;
        pt.color = new Color(1.0f, 0.92f, 0.72f);
        pt.intensity = 2.4f;
        pt.range = 14.0f;
        pt.shadows = LightShadows.None;

        SavePrefab(root, "Suburban_Streetlight");
    }

    private static void CreatePicketFence(Level94Mats mats)
    {
        GameObject root = new GameObject("Suburban_PicketFence");

        // 3m long fence section
        // 2 horizontal rails
        CreateBox(root.transform, "Rail_Top", new Vector3(0f, 0.75f, 0f), new Vector3(3.0f, 0.08f, 0.06f), mats.whiteTrim, true);
        CreateBox(root.transform, "Rail_Bot", new Vector3(0f, 0.32f, 0f), new Vector3(3.0f, 0.08f, 0.06f), mats.whiteTrim, false);

        // 7 vertical pickets with pointed tips
        const int picketCount = 7;
        const float span = 2.8f;
        for (int i = 0; i < picketCount; i++)
        {
            float t = (float)i / (picketCount - 1);
            float x = Mathf.Lerp(-span * 0.5f, span * 0.5f, t);
            CreateBox(root.transform, $"Picket_{i}", new Vector3(x, 0.55f, 0.04f), new Vector3(0.12f, 1.0f, 0.04f), mats.whiteTrim, false);
        }

        SavePrefab(root, "Suburban_PicketFence");
    }

    private static void CreateMailbox(Level94Mats mats)
    {
        GameObject root = new GameObject("Suburban_Mailbox");

        // Wooden post (1.1m)
        CreateBox(root.transform, "Post", new Vector3(0f, 0.55f, 0f), new Vector3(0.12f, 1.1f, 0.12f), mats.whiteTrim, true);
        // Cross arm
        CreateBox(root.transform, "Arm", new Vector3(0.15f, 1.0f, 0f), new Vector3(0.42f, 0.1f, 0.12f), mats.whiteTrim, false);
        // Mailbox metal container
        CreateBox(root.transform, "Box", new Vector3(0.22f, 1.18f, 0f), new Vector3(0.5f, 0.26f, 0.28f), mats.metal, true);
        // Red flag
        CreateBox(root.transform, "Flag", new Vector3(0.42f, 1.25f, 0.16f), new Vector3(0.04f, 0.2f, 0.04f), mats.creamSiding, false);

        SavePrefab(root, "Suburban_Mailbox");
    }

    // =========================================================================
    // HELPER PRIMITIVES
    // =========================================================================
    private static void CreateSlopedRoof(Transform parent, float width, float depth, float baseY, float roofHeight, Material roofMat, Material gableMat)
    {
        GameObject roofRoot = new GameObject("Roof");
        roofRoot.transform.SetParent(parent, false);

        // Two angled rectangular slabs forming the A-frame pitched roof
        float halfDepth = depth * 0.5f;
        float slabLength = Mathf.Sqrt(halfDepth * halfDepth + roofHeight * roofHeight);
        float angle = Mathf.Atan2(roofHeight, halfDepth) * Mathf.Rad2Deg;

        // Front slope
        GameObject frontSlope = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frontSlope.name = "Slope_Front";
        frontSlope.transform.SetParent(roofRoot.transform, false);
        frontSlope.transform.localPosition = new Vector3(0f, baseY + roofHeight * 0.5f, -halfDepth * 0.5f);
        frontSlope.transform.localRotation = Quaternion.Euler(angle, 0f, 0f);
        frontSlope.transform.localScale = new Vector3(width, 0.18f, slabLength + 0.4f);
        SetMat(frontSlope, roofMat);

        // Back slope
        GameObject backSlope = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backSlope.name = "Slope_Back";
        backSlope.transform.SetParent(roofRoot.transform, false);
        backSlope.transform.localPosition = new Vector3(0f, baseY + roofHeight * 0.5f, halfDepth * 0.5f);
        backSlope.transform.localRotation = Quaternion.Euler(-angle, 0f, 0f);
        backSlope.transform.localScale = new Vector3(width, 0.18f, slabLength + 0.4f);
        SetMat(backSlope, roofMat);

        // Gable ends (fill the triangular side walls)
        GameObject gableL = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gableL.name = "Gable_Left";
        gableL.transform.SetParent(roofRoot.transform, false);
        gableL.transform.localPosition = new Vector3(-width * 0.48f, baseY + roofHeight * 0.45f, 0f);
        gableL.transform.localScale = new Vector3(0.15f, roofHeight * 0.9f, depth * 0.85f);
        SetMat(gableL, gableMat);

        GameObject gableR = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gableR.name = "Gable_Right";
        gableR.transform.SetParent(roofRoot.transform, false);
        gableR.transform.localPosition = new Vector3(width * 0.48f, baseY + roofHeight * 0.45f, 0f);
        gableR.transform.localScale = new Vector3(0.15f, roofHeight * 0.9f, depth * 0.85f);
        SetMat(gableR, gableMat);
    }

    private static void CreateDormer(Transform parent, Vector3 localPos, Level94Mats mats)
    {
        GameObject dormer = new GameObject("Dormer");
        dormer.transform.SetParent(parent, false);
        dormer.transform.localPosition = localPos;

        CreateBox(dormer.transform, "Dormer_Walls", new Vector3(0f, 0.6f, 0f), new Vector3(1.2f, 1.2f, 1.2f), mats.blueSiding, false);
        CreateWindow(dormer.transform, new Vector3(0f, 0.6f, -0.62f), new Vector3(0.85f, 0.9f, 0.08f), mats.whiteTrim, mats.glowingWindow);
        CreateBox(dormer.transform, "Dormer_Roof_L", new Vector3(-0.35f, 1.35f, 0f), new Vector3(0.8f, 0.12f, 1.4f), mats.roof, false, Quaternion.Euler(0f, 0f, 30f));
        CreateBox(dormer.transform, "Dormer_Roof_R", new Vector3(0.35f, 1.35f, 0f), new Vector3(0.8f, 0.12f, 1.4f), mats.roof, false, Quaternion.Euler(0f, 0f, -30f));
    }

    private static void CreateWindow(Transform parent, Vector3 pos, Vector3 size, Material frameMat, Material glassMat)
    {
        GameObject win = new GameObject("Window");
        win.transform.SetParent(parent, false);
        win.transform.localPosition = pos;

        // Frame
        CreateBox(win.transform, "Frame", Vector3.zero, size, frameMat, false);
        // Glass Pane (slightly recessed)
        CreateBox(win.transform, "Glass", new Vector3(0f, 0f, -0.02f), new Vector3(size.x - 0.2f, size.y - 0.2f, size.z + 0.01f), glassMat, false);
        // Cross Bars
        CreateBox(win.transform, "Bar_H", new Vector3(0f, 0f, -0.03f), new Vector3(size.x - 0.18f, 0.06f, size.z + 0.02f), frameMat, false);
        CreateBox(win.transform, "Bar_V", new Vector3(0f, 0f, -0.03f), new Vector3(0.06f, size.y - 0.18f, size.z + 0.02f), frameMat, false);
    }

    private static GameObject CreateBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat, bool keepCollider, Quaternion rot = default)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = pos;
        box.transform.localScale = scale;
        if (rot != default) box.transform.localRotation = rot;

        if (!keepCollider)
        {
            Collider col = box.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        SetMat(box, mat);
        return box;
    }

    private static void SetMat(GameObject go, Material mat)
    {
        if (mat == null) return;
        var r = go.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = mat;
    }

    private static void SavePrefab(GameObject go, string name)
    {
        string path = $"{PrefabDir}/{name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }
}
#endif
