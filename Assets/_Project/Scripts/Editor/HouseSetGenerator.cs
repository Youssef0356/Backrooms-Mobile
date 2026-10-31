#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HouseSetGenerator
{
    const string FloorMatPath = "Assets/_Project/Art/Environment/Backrooms/Textures/Carpet/Floor.mat";
    const string WallMatPath = "Assets/_Project/Art/Environment/Backrooms/Textures/Level 0 Wall/Wall.mat";
    const string CeilingMatPath = "Assets/_Project/Art/Environment/Backrooms/Textures/Ceilings/Ceilings.mat";

    const string TablePrefabPath = "Assets/_Project/Art/Environment/Backrooms/Decoration/Table.prefab";
    const string SofaPrefabPath = "Assets/_Project/Art/Environment/Backrooms/Decoration/Canapé.prefab";
    const string TVPrefabPath = "Assets/_Project/Art/Environment/Backrooms/Decoration/CRT_TV.prefab";
    const string BedPrefabPath = "Assets/_Project/Art/Environment/Backrooms/Decoration/Bed.prefab";

    const string SavedPrefabPath = "Assets/_Project/Art/Environment/House_Prologue_Set.prefab";

    [MenuItem("Tools/Backrooms/Scene Setup/MainLevel (Level 0)/Create Editable House Set in Scene", priority = 10)]
    public static void GenerateHouseInScene()
    {
        // Only allow in MainLevel (Level 0), never in Level 94
        var activeScene = SceneManager.GetActiveScene();
        if (activeScene.name != "MainLevel")
        {
            Debug.Log($"[HouseSetGenerator] Skipped: House_Prologue_Set belongs to 'MainLevel' (Level 0 prologue), not '{activeScene.name}'.");
            return;
        }
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog(
                "Stop Play Mode First",
                "Please stop Play Mode (click the Play button at the top) before creating your permanent scene house set, so your scene edits are saved!",
                "OK"
            );
            return;
        }

        // 1. Check for existing
        GameObject existing = GameObject.Find("House_Prologue_Set");
        if (existing != null)
        {
            if (EditorUtility.DisplayDialog("House Set Exists", 
                "A 'House_Prologue_Set' already exists in the scene.\nDo you want to regenerate it and replace it with a fresh, clean layout?", 
                "Replace with Fresh Set", "Keep Existing & Select"))
            {
                Undo.DestroyObjectImmediate(existing);
            }
            else
            {
                Selection.activeGameObject = existing;
                SceneView.lastActiveSceneView?.FrameSelected();
                return;
            }
        }

        Vector3 offset = new Vector3(0f, 65f, 0f);

        // Root
        GameObject root = new GameObject("House_Prologue_Set");
        root.transform.position = offset;
        Undo.RegisterCreatedObjectUndo(root, "Create House Set");

        // Materials
        Material floorMat = AssetDatabase.LoadAssetAtPath<Material>(FloorMatPath);
        Material wallMat = AssetDatabase.LoadAssetAtPath<Material>(WallMatPath);
        Material ceilingMat = AssetDatabase.LoadAssetAtPath<Material>(CeilingMatPath);

        Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        Material darkWoodMat = new Material(litShader) { color = new Color(0.16f, 0.11f, 0.08f) };
        Material tvCasingMat = new Material(litShader) { color = new Color(0.10f, 0.10f, 0.12f) };
        Material rugMat = new Material(litShader) { color = new Color(0.12f, 0.13f, 0.15f) };
        Material bedroomRugMat = new Material(litShader) { color = new Color(0.16f, 0.12f, 0.11f) };
        Material fabricWhiteMat = new Material(litShader) { color = new Color(0.88f, 0.88f, 0.88f) };

        Material clockDisplayMat = new Material(litShader) { color = Color.black };
        clockDisplayMat.EnableKeyword("_EMISSION");
        clockDisplayMat.SetColor("_EmissionColor", new Color(0.95f, 0.40f, 0.05f) * 1.8f);

        Material tvScreenMat = new Material(litShader) { color = new Color(0.75f, 0.88f, 1.0f) };
        tvScreenMat.EnableKeyword("_EMISSION");
        tvScreenMat.SetColor("_EmissionColor", new Color(0.60f, 0.82f, 1.0f) * 1.8f);

        const float w = 9.6f;
        const float h = 3.0f;
        const float d = 8.6f;

        // ---------------- ENVIRONMENT ----------------
        GameObject envGroup = new GameObject("01_Architecture");
        envGroup.transform.SetParent(root.transform, false);

        CreateBox(envGroup.transform, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(w, 0.2f, d), floorMat);
        CreateBox(envGroup.transform, "Ceiling", new Vector3(0f, h + 0.1f, 0f), new Vector3(w, 0.2f, d), ceilingMat);

        CreateBox(envGroup.transform, "Wall_North", new Vector3(0f, h * 0.5f, d * 0.5f), new Vector3(w, h, 0.2f), wallMat);
        CreateBox(envGroup.transform, "Wall_South", new Vector3(0f, h * 0.5f, -d * 0.5f), new Vector3(w, h, 0.2f), wallMat);
        CreateBox(envGroup.transform, "Wall_West", new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(0.2f, h, d), wallMat);
        CreateBox(envGroup.transform, "Wall_East", new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(0.2f, h, d), wallMat);

        CreateBox(envGroup.transform, "Divider_Wall", new Vector3(2.5f, h * 0.5f, 0f), new Vector3(4.6f, h, 0.15f), wallMat);
        CreateBox(envGroup.transform, "Divider_Header", new Vector3(-0.9f, 2.6f, 0f), new Vector3(2.2f, 0.8f, 0.15f), wallMat);

        CreateBox(envGroup.transform, "DoorCasing_Left", new Vector3(-2.0f, 1.1f, 0f), new Vector3(0.08f, 2.2f, 0.18f), darkWoodMat);
        CreateBox(envGroup.transform, "DoorCasing_Right", new Vector3(0.2f, 1.1f, 0f), new Vector3(0.08f, 2.2f, 0.18f), darkWoodMat);
        CreateBox(envGroup.transform, "DoorCasing_Top", new Vector3(-0.9f, 2.24f, 0f), new Vector3(2.28f, 0.08f, 0.18f), darkWoodMat);

        // ---------------- LIVING ROOM ----------------
        GameObject livingGroup = new GameObject("02_LivingRoom");
        livingGroup.transform.SetParent(root.transform, false);

        // Rug
        CreateBox(livingGroup.transform, "LivingRoomRug", new Vector3(0.5f, 0.005f, 2.3f), new Vector3(2.4f, 0.01f, 2.6f), rugMat);

        // Sofa
        GameObject sofaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SofaPrefabPath);
        if (sofaPrefab != null)
        {
            GameObject sofa = (GameObject)PrefabUtility.InstantiatePrefab(sofaPrefab, livingGroup.transform);
            sofa.name = "LivingRoomSofa";
            sofa.transform.localPosition = new Vector3(0.5f, 0f, 1.6f);
            sofa.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            sofa.transform.localScale = Vector3.one * 0.10f;
        }

        // Seating markers
        GameObject sittingSpot = new GameObject("CouchSittingSpot");
        sittingSpot.transform.SetParent(livingGroup.transform, false);
        sittingSpot.transform.localPosition = new Vector3(0.5f, 0.48f, 1.48f);
        sittingSpot.transform.localRotation = Quaternion.identity;

        GameObject standingSpot = new GameObject("StandingSpot");
        standingSpot.transform.SetParent(livingGroup.transform, false);
        standingSpot.transform.localPosition = new Vector3(0.5f, 1.05f, 1.25f);
        standingSpot.transform.localRotation = Quaternion.identity;

        // Coffee Table
        CreateBox(livingGroup.transform, "CoffeeTableTop", new Vector3(0.5f, 0.22f, 2.35f), new Vector3(0.70f, 0.025f, 0.36f), darkWoodMat);
        CreateBox(livingGroup.transform, "CoffeeTableLeg1", new Vector3(0.20f, 0.10f, 2.22f), new Vector3(0.035f, 0.20f, 0.035f), darkWoodMat);
        CreateBox(livingGroup.transform, "CoffeeTableLeg2", new Vector3(0.80f, 0.10f, 2.22f), new Vector3(0.035f, 0.20f, 0.035f), darkWoodMat);
        CreateBox(livingGroup.transform, "CoffeeTableLeg3", new Vector3(0.20f, 0.10f, 2.48f), new Vector3(0.035f, 0.20f, 0.035f), darkWoodMat);
        CreateBox(livingGroup.transform, "CoffeeTableLeg4", new Vector3(0.80f, 0.10f, 2.48f), new Vector3(0.035f, 0.20f, 0.035f), darkWoodMat);
        CreateBox(livingGroup.transform, "TV_Remote", new Vector3(0.46f, 0.245f, 2.35f), new Vector3(0.05f, 0.015f, 0.14f), tvCasingMat);

        // TV Stand
        GameObject tablePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TablePrefabPath);
        if (tablePrefab != null)
        {
            GameObject table = (GameObject)PrefabUtility.InstantiatePrefab(tablePrefab, livingGroup.transform);
            table.name = "TVTable";
            table.transform.localPosition = new Vector3(0.5f, 0f, 3.65f);
            table.transform.localRotation = Quaternion.identity;
            table.transform.localScale = Vector3.one * 0.08f;
        }

        // TV Object
        GameObject tvObj = new GameObject("LivingRoomTV");
        tvObj.transform.SetParent(livingGroup.transform, false);
        tvObj.transform.localPosition = new Vector3(0.5f, 0.44f, 3.65f);
        tvObj.transform.localRotation = Quaternion.identity;

        GameObject tvPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TVPrefabPath);
        if (tvPrefab != null)
        {
            GameObject tvModel = (GameObject)PrefabUtility.InstantiatePrefab(tvPrefab, tvObj.transform);
            tvModel.name = "TV_Model";
            tvModel.transform.localRotation = Quaternion.identity;

            // Normalize size
            Renderer[] rends = tvModel.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                float curW = Mathf.Max(b.size.x, b.size.z);
                if (curW > 0.001f)
                {
                    float factor = 0.52f / curW;
                    tvModel.transform.localScale *= factor;
                }

                // Snap flush to table top
                b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                float bottomDelta = b.min.y - (offset.y + 0.42f);
                tvModel.transform.position -= new Vector3(0f, bottomDelta, 0f);
            }
        }
        else
        {
            CreateBox(tvObj.transform, "TV_Casing", Vector3.zero, new Vector3(0.52f, 0.42f, 0.36f), tvCasingMat);
        }

        // TV Screen
        GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "TV_Screen";
        screen.transform.SetParent(tvObj.transform, false);
        screen.transform.localPosition = new Vector3(0f, 0.22f, -0.191f);
        screen.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        screen.transform.localScale = new Vector3(0.42f, 0.32f, 1f);
        screen.GetComponent<MeshRenderer>().material = tvScreenMat;

        // TV Light
        GameObject tvLightGo = new GameObject("TV_Light");
        tvLightGo.transform.SetParent(tvObj.transform, false);
        tvLightGo.transform.localPosition = new Vector3(0f, 0.22f, -0.55f);
        Light tvL = tvLightGo.AddComponent<Light>();
        tvL.type = LightType.Point;
        tvL.range = 5.5f;
        tvL.intensity = 2.2f;
        tvL.color = new Color(0.72f, 0.86f, 1.0f);

        // ---------------- BEDROOM ----------------
        GameObject bedGroup = new GameObject("03_Bedroom");
        bedGroup.transform.SetParent(root.transform, false);

        CreateBox(bedGroup.transform, "BedroomRug", new Vector3(-1.8f, 0.005f, -2.6f), new Vector3(0.65f, 0.01f, 1.5f), bedroomRugMat);

        GameObject bedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BedPrefabPath);
        if (bedPrefab != null)
        {
            GameObject bed = (GameObject)PrefabUtility.InstantiatePrefab(bedPrefab, bedGroup.transform);
            bed.name = "Bed";
            bed.transform.localPosition = new Vector3(-2.6f, 0f, -2.6f);
            bed.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // Clean colliders
            Collider[] cols = bed.GetComponentsInChildren<Collider>(true);
            for (int c = cols.Length - 1; c >= 0; c--) Object.DestroyImmediate(cols[c]);

            Renderer[] rends = bed.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                float curL = Mathf.Max(b.size.x, b.size.z);
                if (curL > 0.001f)
                {
                    float factor = 1.95f / curL;
                    bed.transform.localScale *= factor;
                }

                // Snap flush to floor
                b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                float bottomDelta = b.min.y - offset.y;
                bed.transform.position -= new Vector3(0f, bottomDelta, 0f);

                // Add single collider
                b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                BoxCollider box = bed.AddComponent<BoxCollider>();
                box.center = bed.transform.InverseTransformPoint(b.center);
                Vector3 worldSize = b.size;
                Vector3 lossy = bed.transform.lossyScale;
                box.size = new Vector3(
                    lossy.x != 0 ? Mathf.Abs(worldSize.x / lossy.x) : worldSize.x,
                    lossy.y != 0 ? Mathf.Abs(worldSize.y / lossy.y) : worldSize.y,
                    lossy.z != 0 ? Mathf.Abs(worldSize.z / lossy.z) : worldSize.z
                );
            }
        }
        else
        {
            GameObject bed = new GameObject("Bed");
            bed.transform.SetParent(bedGroup.transform, false);
            bed.transform.localPosition = new Vector3(-2.6f, 0f, -2.6f);
            bed.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            CreateBox(bed.transform, "BedBase", new Vector3(0f, 0.18f, 0f), new Vector3(1.6f, 0.36f, 2.1f), darkWoodMat);
            CreateBox(bed.transform, "Mattress", new Vector3(0f, 0.42f, 0f), new Vector3(1.5f, 0.16f, 2.0f), fabricWhiteMat);
            CreateBox(bed.transform, "Pillow", new Vector3(0f, 0.52f, -0.72f), new Vector3(1.1f, 0.12f, 0.45f), fabricWhiteMat);
        }

        // Nightstand & details
        CreateBox(bedGroup.transform, "NightstandTop", new Vector3(-3.85f, 0.40f, -3.3f), new Vector3(0.48f, 0.03f, 0.42f), darkWoodMat);
        CreateBox(bedGroup.transform, "NightstandLeg1", new Vector3(-3.65f, 0.19f, -3.15f), new Vector3(0.04f, 0.38f, 0.04f), darkWoodMat);
        CreateBox(bedGroup.transform, "NightstandLeg2", new Vector3(-4.05f, 0.19f, -3.15f), new Vector3(0.04f, 0.38f, 0.04f), darkWoodMat);
        CreateBox(bedGroup.transform, "NightstandLeg3", new Vector3(-3.65f, 0.19f, -3.45f), new Vector3(0.04f, 0.38f, 0.04f), darkWoodMat);
        CreateBox(bedGroup.transform, "NightstandLeg4", new Vector3(-4.05f, 0.19f, -3.45f), new Vector3(0.04f, 0.38f, 0.04f), darkWoodMat);

        CreateBox(bedGroup.transform, "ClockBody", new Vector3(-3.72f, 0.445f, -3.3f), new Vector3(0.10f, 0.06f, 0.06f), tvCasingMat);
        CreateBox(bedGroup.transform, "ClockDisplay", new Vector3(-3.72f, 0.445f, -3.268f), new Vector3(0.08f, 0.04f, 0.005f), clockDisplayMat);

        CreateBox(bedGroup.transform, "LampBase", new Vector3(-3.95f, 0.425f, -3.3f), new Vector3(0.12f, 0.02f, 0.12f), darkWoodMat);
        CreateBox(bedGroup.transform, "LampStem", new Vector3(-3.95f, 0.52f, -3.3f), new Vector3(0.02f, 0.18f, 0.02f), darkWoodMat);
        CreateBox(bedGroup.transform, "LampShade", new Vector3(-3.95f, 0.65f, -3.3f), new Vector3(0.18f, 0.16f, 0.18f), fabricWhiteMat);

        // ---------------- LIGHTING ----------------
        GameObject lightGroup = new GameObject("04_Lighting");
        lightGroup.transform.SetParent(root.transform, false);

        GameObject bMoonGo = new GameObject("BedroomMoonlight");
        bMoonGo.transform.SetParent(lightGroup.transform, false);
        bMoonGo.transform.localPosition = new Vector3(-w * 0.35f, 2.2f, -2.4f);
        Light bMoon = bMoonGo.AddComponent<Light>();
        bMoon.type = LightType.Point;
        bMoon.color = new Color(0.12f, 0.18f, 0.32f);
        bMoon.intensity = 0.40f;
        bMoon.range = 6.0f;

        GameObject lMoonGo = new GameObject("LivingRoomMoonlight");
        lMoonGo.transform.SetParent(lightGroup.transform, false);
        lMoonGo.transform.localPosition = new Vector3(w * 0.35f, 2.2f, 2.5f);
        Light lMoon = lMoonGo.AddComponent<Light>();
        lMoon.type = LightType.Point;
        lMoon.color = new Color(0.12f, 0.18f, 0.32f);
        lMoon.intensity = 0.40f;
        lMoon.range = 6.0f;

        // Auto-assign on BackroomsRoomGenerator in scene if found
        BackroomsRoomGenerator gen = Object.FindFirstObjectByType<BackroomsRoomGenerator>();
        if (gen != null)
        {
            gen.customHouseSet = root;
            EditorUtility.SetDirty(gen);
        }

        // Save as Prefab Backup
        string dir = Path.GetDirectoryName(SavedPrefabPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, SavedPrefabPath, InteractionMode.AutomatedAction);

        // Select and frame in Scene view
        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();

        Debug.Log($"<color=#00FF66><b>[House]</b></color> Successfully created editable scene GameObject <b>'House_Prologue_Set'</b> and saved prefab to <b>{SavedPrefabPath}</b>!");
    }

    private static void CreateBox(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        if (mat != null) go.GetComponent<MeshRenderer>().material = mat;
    }
}
#endif
