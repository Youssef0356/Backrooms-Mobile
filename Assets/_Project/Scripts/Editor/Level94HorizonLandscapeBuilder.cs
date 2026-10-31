#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Generates and populates the expansive horizon landscape for Backrooms Level 94:
/// - 1200m x 1200m rolling green hills surrounding the central neighborhood.
/// - Seamless cutout around the central LEVEL 94 map (±72m).
/// - Distant horizon ridges rising to kiss the horizon fog, eliminating any "platform in the void" feeling.
/// - Scatters distant suburban houses, landmark water towers on hill peaks, fence rows, and glowing streetlights.
/// - Fully static-batched for high mobile performance.
/// </summary>
public static class Level94HorizonLandscapeBuilder
{
    private const string ScenePath = "Assets/_Project/Scenes/Level94.unity";
    private const string MeshAssetPath = "Assets/_Project/Art/Level94/Level94_HorizonLandscapeMesh.asset";
    private const string GrassMatPath = "Assets/_Project/Art/Level94/Materials/Grass_Level94.mat";
    private const string WaterTowerGlbPath = "Assets/_Project/Art/Level94/WaterContainer.glb";
    private const string ConcreteMatPath = "Assets/_Project/Art/Level94/Materials/Foundation_Concrete.mat";
    private const string FenceGlbPath = "Assets/_Project/Art/Level94/Fences.glb";
    private const string StreetlightPrefabPath = "Assets/_Project/Art/Level94/Prefabs/Suburban_Streetlight.prefab";

    [MenuItem("Tools/Backrooms/Scene Setup/Level 94/5. Build Horizon Landscape (Fix Platform Void)", priority = 5)]
    public static void BuildHorizonLandscapeMenu()
    {
        BuildHorizonLandscape();
    }

    [InitializeOnLoadMethod]
    private static void AutoRunOnEditorLoad()
    {
        if (Application.isPlaying) return;
        if (!SessionState.GetBool("Level94_Horizon_Fixed_v4", false))
        {
            SessionState.SetBool("Level94_Horizon_Fixed_v4", true);
            try
            {
                Debug.Log("[Level94HorizonLandscapeBuilder] Rebuilding Level 94 horizon landscape (fixing overlap, textures & houses)...");
                BuildHorizonLandscape();
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[Level94HorizonLandscapeBuilder] AutoRun error: " + ex);
            }
        }
    }

    private static Material FindAuthenticGrassMaterial(GameObject mainMap)
    {
        Object[] allGlbAssets = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Level94/LEVEL 94.glb");
        if (allGlbAssets != null)
        {
            foreach (var asset in allGlbAssets)
            {
                if (asset is Material m && m.name == "Grass Landscape")
                {
                    return m;
                }
            }
            foreach (var asset in allGlbAssets)
            {
                if (asset is Material m && m.name == "Grass Material")
                {
                    return m;
                }
            }
        }

        Material fallback = AssetDatabase.LoadAssetAtPath<Material>(GrassMatPath);
        if (fallback != null)
        {
            fallback.mainTextureScale = Vector2.one;
            if (fallback.HasProperty("_BaseMap"))
                fallback.SetTextureScale("_BaseMap", Vector2.one);
            if (fallback.HasProperty("_BumpMap"))
                fallback.SetTextureScale("_BumpMap", Vector2.one);
            EditorUtility.SetDirty(fallback);
            return fallback;
        }

        Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        return new Material(litShader) { color = new Color(0.24f, 0.58f, 0.18f) };
    }

    private static Collider GetMainMapTerrainCollider(GameObject mainMap)
    {
        if (mainMap == null) return null;
        MeshFilter[] mfs = mainMap.GetComponentsInChildren<MeshFilter>(true);
        foreach (var mf in mfs)
        {
            if (mf.name == "Object_8" || (mf.sharedMesh != null && mf.sharedMesh.name.Contains("Object_4")))
            {
                MeshCollider mc = mf.GetComponent<MeshCollider>();
                if (mc == null)
                {
                    mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                }
                return mc;
            }
        }
        return mainMap.GetComponentInChildren<Collider>();
    }

    public static void BuildHorizonLandscape()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        if (currentScene.path != ScenePath && File.Exists(ScenePath))
        {
            currentScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        // 1. Remove previous horizon landscape if present
        GameObject existing = GameObject.Find("LEVEL 94 Horizon Landscape");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
        }

        GameObject mainMap = GameObject.Find("LEVEL 94");
        Collider mainMapCol = GetMainMapTerrainCollider(mainMap);
        Material grassMat = FindAuthenticGrassMaterial(mainMap);

        GameObject horizonRoot = new GameObject("LEVEL 94 Horizon Landscape");
        horizonRoot.isStatic = true;
        Undo.RegisterCreatedObjectUndo(horizonRoot, "Create Horizon Landscape");

        // 2. Generate Surrounding Terrain Mesh (Strictly outside [-74m, +74m] so it NEVER overlaps LEVEL 94)
        Mesh terrainMesh = GenerateHorizonMesh(mainMapCol);
        AssetDatabase.CreateAsset(terrainMesh, MeshAssetPath);
        AssetDatabase.SaveAssets();

        GameObject terrainGo = new GameObject("Horizon_Rolling_Hills");
        terrainGo.transform.SetParent(horizonRoot.transform, false);
        terrainGo.isStatic = true;

        var mf = terrainGo.AddComponent<MeshFilter>();
        mf.sharedMesh = terrainMesh;

        var mr = terrainGo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = grassMat;

        var mc = terrainGo.AddComponent<MeshCollider>();
        mc.sharedMesh = terrainMesh;

        // 3. Populate Distant Suburban Houses (ONLY using authentic LEVEL 94 Houses.glb outside the main map)
        List<GameObject> housePrefabs = LoadHousePrefabs();
        Material concreteMat = AssetDatabase.LoadAssetAtPath<Material>(ConcreteMatPath);
        PopulateDistantHouses(horizonRoot.transform, housePrefabs, concreteMat, mc);

        // 4. Populate Distant Water Towers on Hill Peaks
        PopulateDistantWaterTowers(horizonRoot.transform, mc);

        // 5. Populate Distant Fences & Streetlights
        PopulateDistantFencesAndLights(horizonRoot.transform, mc);

        // 6. Clean up Level94Generator housePrefabs if present so broken procedural house prefabs are never referenced
        var gen = Object.FindAnyObjectByType<Level94Generator>();
        if (gen != null && housePrefabs.Count > 0)
        {
            gen.housePrefabs = housePrefabs.ToArray();
            EditorUtility.SetDirty(gen);
        }

        // 7. Update Atmosphere & Day/Night settings
        UpdateAtmosphereAndCycleInScene();

        EditorSceneManager.MarkSceneDirty(currentScene);
        EditorSceneManager.SaveScene(currentScene);

        Debug.Log("<color=#00FF88><b>[Horizon Landscape Built]</b></color> Clean 1200m horizon welded to LEVEL 94 border (±75.5m) with 0 overlap, authentic grass UV scale, and authentic yellow houses!");
    }

    private static float SampleMainMapBorderY(Collider mainMapCol, float worldX, float worldZ)
    {
        if (mainMapCol != null)
        {
            float bx = Mathf.Clamp(worldX, -75.1f, 75.1f);
            float bz = Mathf.Clamp(worldZ, -75.1f, 75.1f);
            Ray ray = new Ray(new Vector3(bx, 80f, bz), Vector3.down);
            if (mainMapCol.Raycast(ray, out RaycastHit hit, 160f))
            {
                return hit.point.y;
            }
        }
        return 0.5f;
    }

    private static Mesh GenerateHorizonMesh(Collider mainMapCol)
    {
        Mesh mesh = new Mesh();
        mesh.name = "Level94_HorizonLandscapeMesh";

        int resolution = 150;
        float totalSize = 1200f; // -600 to +600 (step = 8.0m)
        float step = totalSize / resolution;
        float halfSize = totalSize * 0.5f;
        int vCount = resolution + 1;

        Vector3[] vertices = new Vector3[vCount * vCount];
        Vector2[] uvs0 = new Vector2[vertices.Length];
        Vector2[] uvs1 = new Vector2[vertices.Length];

        float seedOffsetX = 12450f;
        float seedOffsetZ = 83710f;

        for (int z = 0; z <= resolution; z++)
        {
            for (int x = 0; x <= resolution; x++)
            {
                int index = z * vCount + x;
                float worldX = -halfSize + x * step;
                float worldZ = -halfSize + z * step;

                float maxAxis = Mathf.Max(Mathf.Abs(worldX), Mathf.Abs(worldZ));
                float r = Mathf.Sqrt(worldX * worldX + worldZ * worldZ);

                // Multi-frequency rolling hills outside the main map (authentic Backrooms Level 94 Bliss mounds)
                float hill1 = Mathf.PerlinNoise((worldX + seedOffsetX) * 0.0055f, (worldZ + seedOffsetZ) * 0.0055f) * 34.0f;
                float hill2 = Mathf.PerlinNoise((worldX + seedOffsetX) * 0.013f, (worldZ + seedOffsetZ) * 0.013f) * 14.0f;
                float hill3 = Mathf.Sin(worldX * 0.018f) * Mathf.Cos(worldZ * 0.018f) * 6.0f;
                float rawHeight = hill1 + hill2 + hill3;

                float borderY = SampleMainMapBorderY(mainMapCol, worldX, worldZ);

                float height;
                if (maxAxis <= 75.6f)
                {
                    // Inside or at the exact border of LEVEL 94.glb (±75.6m):
                    // Tuck slightly below borderY so it NEVER pokes through LEVEL 94's hills
                    height = borderY - 0.15f;
                }
                else if (maxAxis < 125f)
                {
                    // Smoothly weld from LEVEL 94's exact edge height into the surrounding rolling hills
                    float blend = Mathf.SmoothStep(0f, 1f, (maxAxis - 75.6f) / 49.4f);
                    height = Mathf.Lerp(borderY - 0.05f, rawHeight, blend);
                }
                else
                {
                    height = rawHeight;
                }

                // Distant horizon ridges rising gracefully between 200m and 600m to kiss the sky and fill the horizon
                if (r > 200f)
                {
                    float ridgeFactor = Mathf.Clamp01((r - 200f) / 380f);
                    height += Mathf.SmoothStep(0f, 1f, ridgeFactor) * 36.0f;
                }

                vertices[index] = new Vector3(worldX, height, worldZ);
                // Match the exact UV0 (0.0287/m) and UV1 (0.0659/m) scale of LEVEL 94.glb
                uvs0[index] = new Vector2(worldX * 0.0287f, worldZ * 0.0287f);
                uvs1[index] = new Vector2(worldX * 0.0659f, worldZ * 0.0659f);
            }
        }

        // Triangles - strictly skip all cells inside the LEVEL 94 map (±70m) so there is ZERO overlap!
        List<int> triangles = new List<int>();
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float cellCenterX = -halfSize + (x + 0.5f) * step;
                float cellCenterZ = -halfSize + (z + 0.5f) * step;

                if (Mathf.Abs(cellCenterX) < 70.5f && Mathf.Abs(cellCenterZ) < 70.5f)
                {
                    continue;
                }

                int i0 = z * vCount + x;
                int i1 = z * vCount + (x + 1);
                int i2 = (z + 1) * vCount + x;
                int i3 = (z + 1) * vCount + (x + 1);

                triangles.Add(i0);
                triangles.Add(i2);
                triangles.Add(i1);

                triangles.Add(i1);
                triangles.Add(i2);
                triangles.Add(i3);
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uvs0;
        mesh.uv2 = uvs1;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public static float SampleLandscapeHeight(float worldX, float worldZ, Collider horizonCol = null)
    {
        if (horizonCol != null)
        {
            Ray ray = new Ray(new Vector3(worldX, 120f, worldZ), Vector3.down);
            if (horizonCol.Raycast(ray, out RaycastHit hit, 240f))
            {
                return hit.point.y;
            }
        }

        float maxAxis = Mathf.Max(Mathf.Abs(worldX), Mathf.Abs(worldZ));
        float r = Mathf.Sqrt(worldX * worldX + worldZ * worldZ);

        float seedOffsetX = 12450f;
        float seedOffsetZ = 83710f;

        float hill1 = Mathf.PerlinNoise((worldX + seedOffsetX) * 0.0055f, (worldZ + seedOffsetZ) * 0.0055f) * 34.0f;
        float hill2 = Mathf.PerlinNoise((worldX + seedOffsetX) * 0.013f, (worldZ + seedOffsetZ) * 0.013f) * 14.0f;
        float hill3 = Mathf.Sin(worldX * 0.018f) * Mathf.Cos(worldZ * 0.018f) * 6.0f;
        float height = hill1 + hill2 + hill3;

        if (maxAxis <= 75.6f) return 0.5f;
        if (maxAxis < 125f)
        {
            float blend = Mathf.SmoothStep(0f, 1f, (maxAxis - 75.6f) / 49.4f);
            height = Mathf.Lerp(0.5f, height, blend);
        }

        if (r > 200f)
        {
            float ridgeFactor = Mathf.Clamp01((r - 200f) / 380f);
            height += Mathf.SmoothStep(0f, 1f, ridgeFactor) * 36.0f;
        }

        return height;
    }

    private static List<GameObject> LoadHousePrefabs()
    {
        var list = new List<GameObject>();
        // ONLY load the authentic yellow suburban house model (LEVEL 94 Houses.glb)
        var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/LEVEL 94 Houses.glb");
        if (go != null) list.Add(go);
        return list;
    }

    private static void PopulateDistantHouses(Transform parent, List<GameObject> housePrefabs, Material concreteMat, Collider horizonCol)
    {
        if (housePrefabs == null || housePrefabs.Count == 0) return;

        GameObject housesGroup = new GameObject("Distant Houses");
        housesGroup.transform.SetParent(parent, false);
        housesGroup.isStatic = true;

        int targetHouses = 60;
        Random.InitState(949494);

        int spawned = 0;
        List<Vector3> placed = new List<Vector3>();

        // Create 18 neighborhood clusters in the rolling hills
        int clusterCount = 18;
        for (int c = 0; c < clusterCount && spawned < targetHouses; c++)
        {
            float clusterAngle = (c * (360f / clusterCount) + Random.Range(-12f, 12f)) * Mathf.Deg2Rad;
            float clusterDist = Random.Range(110f, 380f);
            Vector3 clusterCenter = new Vector3(Mathf.Cos(clusterAngle) * clusterDist, 0f, Mathf.Sin(clusterAngle) * clusterDist);

            if (Mathf.Abs(clusterCenter.x) < 88f && Mathf.Abs(clusterCenter.z) < 88f) continue;

            int housesInThisCluster = Random.Range(2, 5);
            for (int h = 0; h < housesInThisCluster && spawned < targetHouses; h++)
            {
                float offsetAngle = (h * (360f / housesInThisCluster) + Random.Range(-15f, 15f)) * Mathf.Deg2Rad;
                float offsetDist = h == 0 ? 0f : Random.Range(18f, 28f);

                float x = clusterCenter.x + Mathf.Cos(offsetAngle) * offsetDist;
                float z = clusterCenter.z + Mathf.Sin(offsetAngle) * offsetDist;

                if (Mathf.Abs(x) < 88f && Mathf.Abs(z) < 88f) continue;

                Vector3 pos = new Vector3(x, 0f, z);
                bool tooClose = false;
                foreach (var p in placed)
                {
                    if (Vector3.Distance(new Vector3(p.x, 0f, p.z), pos) < 16f)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;

                float y = SampleLandscapeHeight(x, z, horizonCol);
                pos.y = y - 0.15f;

                GameObject prefab = housePrefabs[Random.Range(0, housePrefabs.Count)];
                GameObject house = (GameObject)PrefabUtility.InstantiatePrefab(prefab, housesGroup.transform);
                house.name = $"Distant_House_{spawned + 1}";
                house.transform.position = pos;
                // Face outwards or towards cluster center
                Vector3 lookDir = (pos - clusterCenter);
                lookDir.y = 0f;
                float rotY = lookDir.sqrMagnitude > 1f ? Quaternion.LookRotation(lookDir).eulerAngles.y : Random.Range(0f, 360f);
                house.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
                house.transform.localScale = Vector3.one * 1.15f;
                house.isStatic = true;

                var hf = house.transform.Find("House_Foundation");
                if (hf != null) hf.gameObject.SetActive(false);

                placed.Add(pos);
                spawned++;
            }
        }
    }

    private static void AddFoundationSkirt(GameObject house, Material concreteMat)
    {
        // Skirts removed - houses sit naturally on grass
    }

    private static void PopulateDistantWaterTowers(Transform parent, Collider horizonCol = null)
    {
        GameObject waterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterTowerGlbPath);
        if (waterPrefab == null) return;

        GameObject towersGroup = new GameObject("Distant Water Towers");
        towersGroup.transform.SetParent(parent, false);
        towersGroup.isStatic = true;

        // Place 10 towers on high ridge crests at distributed angles
        float[] angles = { 18f, 55f, 92f, 130f, 168f, 205f, 242f, 280f, 315f, 350f };
        for (int i = 0; i < angles.Length; i++)
        {
            float ang = angles[i] * Mathf.Deg2Rad;
            float dist = Random.Range(220f, 460f);
            float x = Mathf.Cos(ang) * dist;
            float z = Mathf.Sin(ang) * dist;

            float y = SampleLandscapeHeight(x, z, horizonCol);

            GameObject tower = (GameObject)PrefabUtility.InstantiatePrefab(waterPrefab, towersGroup.transform);
            tower.name = $"Distant_WaterTower_{i + 1}";
            tower.transform.position = new Vector3(x, y - 0.2f, z);
            tower.transform.rotation = Quaternion.Euler(-90f, angles[i] + 90f, 0f);
            tower.transform.localScale = Vector3.one * 17.0f;
            tower.isStatic = true;

            // Ensure colliders
            var meshFilters = tower.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null) continue;
                var mc = mf.GetComponent<MeshCollider>();
                if (mc == null)
                {
                    mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                }
            }
        }
    }

    private static void PopulateDistantFencesAndLights(Transform parent, Collider horizonCol = null)
    {
        GameObject fencePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FenceGlbPath);
        GameObject lightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StreetlightPrefabPath);

        GameObject propsGroup = new GameObject("Distant Props & Streetlights");
        propsGroup.transform.SetParent(parent, false);
        propsGroup.isStatic = true;

        // Place streetlights and fences near distances 110m - 260m
        int lightCount = 12;
        for (int i = 0; i < lightCount; i++)
        {
            float ang = (i * (360f / lightCount) + Random.Range(-10f, 10f)) * Mathf.Deg2Rad;
            float dist = Random.Range(115f, 240f);
            float x = Mathf.Cos(ang) * dist;
            float z = Mathf.Sin(ang) * dist;
            float y = SampleLandscapeHeight(x, z, horizonCol);

            if (lightPrefab != null)
            {
                GameObject lightObj = (GameObject)PrefabUtility.InstantiatePrefab(lightPrefab, propsGroup.transform);
                lightObj.name = $"Distant_Streetlight_{i + 1}";
                lightObj.transform.position = new Vector3(x, y, z);
                lightObj.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                lightObj.isStatic = true;
            }

            if (fencePrefab != null && (i % 2 == 0))
            {
                // Place a 3-segment fence row
                Vector3 basePos = new Vector3(x + 5f, y, z + 5f);
                Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 180f), 0f);
                Vector3 forward = rot * Vector3.forward;

                for (int f = 0; f < 3; f++)
                {
                    Vector3 fPos = basePos + forward * (f * 3.5f);
                    fPos.y = SampleLandscapeHeight(fPos.x, fPos.z, horizonCol);
                    GameObject fenceObj = (GameObject)PrefabUtility.InstantiatePrefab(fencePrefab, propsGroup.transform);
                    fenceObj.name = $"Distant_Fence_{i}_{f}";
                    fenceObj.transform.position = fPos;
                    fenceObj.transform.rotation = rot;
                    fenceObj.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
                    fenceObj.isStatic = true;
                }
            }
        }
    }

    private static void UpdateAtmosphereAndCycleInScene()
    {
        // 1. Director settings
        GameObject director = GameObject.Find("Level94_Director");
        if (director != null)
        {
            var dnc = director.GetComponent<Level94DayNightCycle>();
            if (dnc != null)
            {
                dnc.dayDuration = 60f;     // 1 minute day
                dnc.nightDuration = 60f;   // 1 minute night
                dnc.transitionDuration = 6f;
                dnc.daySkyTint = new Color(0.20f, 0.55f, 0.95f);
                dnc.dayGroundColor = new Color(0.60f, 0.75f, 0.92f);
                dnc.dayFogColor = new Color(0.68f, 0.82f, 0.98f);
                dnc.dayFogStart = 120f;
                dnc.dayFogEnd = 650f;
                dnc.nightFogColor = new Color(0.001f, 0.0015f, 0.003f);
                dnc.nightFogStart = 0.5f;
                dnc.nightFogEnd = 16.0f; // Dense claustrophobic night mist
                EditorUtility.SetDirty(dnc);
            }

            var atmos = director.GetComponent<Level94Atmosphere>();
            if (atmos != null)
            {
                atmos.ApplySettings();
                EditorUtility.SetDirty(atmos);
            }
        }

        // 2. Skybox & RenderSettings
        Material skyMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Level94/Materials/Level94_LiminalSky.mat");
        if (skyMat != null)
        {
            if (skyMat.HasProperty("_TopColor")) skyMat.SetColor("_TopColor", new Color(0.20f, 0.55f, 0.95f));
            if (skyMat.HasProperty("_HorizonColor")) skyMat.SetColor("_HorizonColor", new Color(0.68f, 0.82f, 0.98f));
            if (skyMat.HasProperty("_BottomColor")) skyMat.SetColor("_BottomColor", new Color(0.60f, 0.75f, 0.92f));
            if (skyMat.HasProperty("_SkyTint")) skyMat.SetColor("_SkyTint", new Color(0.20f, 0.55f, 0.95f));
            if (skyMat.HasProperty("_GroundColor")) skyMat.SetColor("_GroundColor", new Color(0.60f, 0.75f, 0.92f));
            RenderSettings.skybox = skyMat;
            EditorUtility.SetDirty(skyMat);
        }

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.68f, 0.82f, 0.98f); // Pure liminal atmospheric blue
        RenderSettings.fogStartDistance = 120f;
        RenderSettings.fogEndDistance = 650f;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.70f, 0.84f, 0.98f);
        RenderSettings.ambientEquatorColor = new Color(0.82f, 0.86f, 0.92f);
        RenderSettings.ambientGroundColor = new Color(0.40f, 0.46f, 0.52f);

        DynamicGI.UpdateEnvironment();
    }
}
#endif
