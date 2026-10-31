using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls and populates the vast surrounding rolling hills horizon landscape for Backrooms Level 94:
/// - Generates a 1200m x 1200m surrounding rolling hills terrain with seamless cutout for the central map ([-70.5m, +70.5m]).
/// - Never overlaps the user's sculpted LEVEL 94 terrain and uses authentic UV0/UV1 grass scaling.
/// - Scatters distant yellow suburban houses (LEVEL 94 Houses.glb), landmark water towers on hill peaks, fence lines, and streetlights.
/// </summary>
[ExecuteAlways]
public class Level94HorizonLandscape : MonoBehaviour
{
    [Header("Landscape Dimensions")]
    public float totalSize = 1200f; // -600m to +600m
    public int resolution = 130;
    public float centralCutoutRadius = 72f;

    [Header("Rolling Hills Perlin Noise")]
    public float hillFrequency = 0.0075f;
    public float hillHeight = 16.5f;
    public float secondaryFrequency = 0.022f;
    public float secondaryHeight = 4.5f;
    public float edgeRiseStart = 320f;
    public float edgeRiseHeight = 26.0f;

    [Header("Distant Scenery Count")]
    public int distantHouseCount = 36;
    public int distantWaterTowerCount = 8;
    public int distantStreetlightCount = 12;

    [Header("Materials & Prefabs (Auto-acquired from Generator if empty)")]
    public Material grassMaterial;
    public GameObject[] housePrefabs;
    public GameObject waterTowerPrefab;
    public GameObject fencePrefab;
    public GameObject streetlightPrefab;

    private const string ContainerName = "LEVEL 94 Horizon Landscape";

    private void Awake()
    {
        EnsureLandscape();
    }

    private void Start()
    {
        EnsureLandscape();
    }

    [ContextMenu("Rebuild Horizon Landscape")]
    public void RebuildLandscape()
    {
        ClearExistingLandscape();
        BuildLandscape();
    }

    public void EnsureLandscape()
    {
        GameObject existing = GameObject.Find(ContainerName);
        if (existing == null)
        {
            BuildLandscape();
        }
    }

    private void ClearExistingLandscape()
    {
        GameObject existing = GameObject.Find(ContainerName);
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing);
            else DestroyImmediate(existing);
        }
    }

    private void FetchReferencesIfMissing()
    {
        Level94Generator gen = GetComponent<Level94Generator>();
        if (gen != null)
        {
            if (grassMaterial == null) grassMaterial = gen.grassMaterial;
            if (housePrefabs == null || housePrefabs.Length == 0) housePrefabs = gen.housePrefabs;
            if (waterTowerPrefab == null) waterTowerPrefab = gen.waterTowerPrefab;
            if (fencePrefab == null) fencePrefab = gen.fencePrefab;
            if (streetlightPrefab == null) streetlightPrefab = gen.streetlightPrefab;
        }

#if UNITY_EDITOR
        if (grassMaterial == null)
        {
            grassMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Level94/Materials/Grass_Level94.mat");
        }
        if (waterTowerPrefab == null)
        {
            waterTowerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/WaterContainer.glb");
        }
        if (fencePrefab == null)
        {
            fencePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/Fences.glb");
        }
        if (streetlightPrefab == null)
        {
            streetlightPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/Prefabs/Suburban_Streetlight.prefab");
        }
        if (housePrefabs == null || housePrefabs.Length == 0)
        {
            var hg = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Level94/LEVEL 94 Houses.glb");
            if (hg != null) housePrefabs = new GameObject[] { hg };
        }
#endif
        if (housePrefabs != null && housePrefabs.Length > 0)
        {
            var validList = new List<GameObject>();
            foreach (var hp in housePrefabs)
            {
                if (hp != null && hp.name.Contains("LEVEL 94 Houses"))
                    validList.Add(hp);
            }
            if (validList.Count > 0) housePrefabs = validList.ToArray();
        }
    }

    public void BuildLandscape()
    {
        FetchReferencesIfMissing();

        GameObject container = new GameObject(ContainerName);
        container.transform.position = Vector3.zero;
        container.transform.rotation = Quaternion.identity;
        container.isStatic = true;

        // 1. Terrain Mesh
        Mesh terrainMesh = GenerateMesh();
        GameObject hillsObj = new GameObject("Horizon_Rolling_Hills");
        hillsObj.transform.SetParent(container.transform, false);
        hillsObj.isStatic = true;

        var mf = hillsObj.AddComponent<MeshFilter>();
        mf.sharedMesh = terrainMesh;

        var mr = hillsObj.AddComponent<MeshRenderer>();
        if (grassMaterial != null) mr.sharedMaterial = grassMaterial;

        var mc = hillsObj.AddComponent<MeshCollider>();
        mc.sharedMesh = terrainMesh;

        // 2. Distant Houses
        SpawnDistantHouses(container.transform);

        // 3. Distant Water Towers
        SpawnDistantWaterTowers(container.transform);

        // 4. Distant Fences & Streetlights
        SpawnDistantProps(container.transform);

        Debug.Log("[Level94HorizonLandscape] Successfully generated expansive 1200m rolling hills horizon landscape!");
    }

    public float SampleHeight(float worldX, float worldZ)
    {
        float absMax = Mathf.Max(Mathf.Abs(worldX), Mathf.Abs(worldZ));
        float r = Mathf.Sqrt(worldX * worldX + worldZ * worldZ);

        float seedOffsetX = 12450f;
        float seedOffsetZ = 83710f;

        float y1 = Mathf.PerlinNoise((worldX + seedOffsetX) * hillFrequency, (worldZ + seedOffsetZ) * hillFrequency) * hillHeight;
        float y2 = Mathf.PerlinNoise((worldX + seedOffsetX) * secondaryFrequency, (worldZ + seedOffsetZ) * secondaryFrequency) * secondaryHeight;
        float rawHeight = y1 + y2;

        float height;
        if (absMax < 76f)
        {
            height = 0.0f;
        }
        else if (absMax < 110f)
        {
            float blend = Mathf.SmoothStep(0f, 1f, (absMax - 76f) / 34f);
            height = Mathf.Lerp(0.0f, rawHeight, blend);
        }
        else
        {
            height = rawHeight;
        }

        if (r > edgeRiseStart)
        {
            float edgeRise = Mathf.Clamp01((r - edgeRiseStart) / (totalSize * 0.5f - edgeRiseStart));
            height += edgeRise * edgeRise * edgeRiseHeight;
        }

        return height;
    }

    private Mesh GenerateMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Level94_ProceduralHorizonMesh";

        float step = totalSize / resolution;
        float halfSize = totalSize * 0.5f;
        int vCount = resolution + 1;

        Vector3[] vertices = new Vector3[vCount * vCount];
        Vector2[] uv0 = new Vector2[vertices.Length];
        Vector2[] uv1 = new Vector2[vertices.Length];

        for (int z = 0; z <= resolution; z++)
        {
            for (int x = 0; x <= resolution; x++)
            {
                int index = z * vCount + x;
                float worldX = -halfSize + x * step;
                float worldZ = -halfSize + z * step;

                float height = SampleHeight(worldX, worldZ);

                vertices[index] = new Vector3(worldX, height, worldZ);
                uv0[index] = new Vector2(worldX * 0.0287f, worldZ * 0.0287f);
                uv1[index] = new Vector2(worldX * 0.0659f, worldZ * 0.0659f);
            }
        }

        List<int> triangles = new List<int>();
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float cellCenterX = -halfSize + (x + 0.5f) * step;
                float cellCenterZ = -halfSize + (z + 0.5f) * step;
                if (Mathf.Abs(cellCenterX) < 70.5f && Mathf.Abs(cellCenterZ) < 70.5f)
                    continue;

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
        mesh.uv = uv0;
        mesh.uv2 = uv1;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void SpawnDistantHouses(Transform parent)
    {
        if (housePrefabs == null || housePrefabs.Length == 0) return;

        GameObject housesGroup = new GameObject("Distant Houses");
        housesGroup.transform.SetParent(parent, false);
        housesGroup.isStatic = true;

        Random.InitState(949494);
        int spawned = 0;
        int attempts = 0;
        List<Vector3> placed = new List<Vector3>();

        while (spawned < distantHouseCount && attempts < 500)
        {
            attempts++;
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist = Random.Range(95f, 440f);
            float x = Mathf.Cos(angle) * dist;
            float z = Mathf.Sin(angle) * dist;

            if (Mathf.Abs(x) < 88f && Mathf.Abs(z) < 88f)
                continue;

            Vector3 pos = new Vector3(x, 0f, z);
            bool tooClose = false;
            foreach (var p in placed)
            {
                if (Vector3.Distance(new Vector3(p.x, 0f, p.z), pos) < 34f)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            float y = SampleHeight(x, z);
            pos.y = y;

            GameObject prefab = housePrefabs[Random.Range(0, housePrefabs.Length)];
            GameObject house;
#if UNITY_EDITOR
            house = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, housesGroup.transform);
#else
            house = Instantiate(prefab, housesGroup.transform);
#endif
            house.name = $"Distant_House_{spawned + 1}";
            house.transform.position = pos;
            house.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            house.isStatic = true;

            // Add foundation skirt
            GameObject skirt = GameObject.CreatePrimitive(PrimitiveType.Cube);
            skirt.name = "Foundation_Skirt";
            skirt.transform.SetParent(house.transform, false);
            skirt.transform.localPosition = new Vector3(0f, -0.65f, 0f);
            skirt.transform.localScale = new Vector3(9.5f, 1.35f, 9.5f);
            skirt.isStatic = true;
            var col = skirt.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            placed.Add(pos);
            spawned++;
        }
    }

    private void SpawnDistantWaterTowers(Transform parent)
    {
        if (waterTowerPrefab == null) return;

        GameObject towersGroup = new GameObject("Distant Water Towers");
        towersGroup.transform.SetParent(parent, false);
        towersGroup.isStatic = true;

        float[] angles = { 25f, 70f, 115f, 160f, 210f, 255f, 295f, 340f };
        for (int i = 0; i < Mathf.Min(angles.Length, distantWaterTowerCount); i++)
        {
            float ang = angles[i] * Mathf.Deg2Rad;
            float dist = Random.Range(200f, 440f);
            float x = Mathf.Cos(ang) * dist;
            float z = Mathf.Sin(ang) * dist;
            float y = SampleHeight(x, z);

            GameObject tower;
#if UNITY_EDITOR
            tower = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(waterTowerPrefab, towersGroup.transform);
#else
            tower = Instantiate(waterTowerPrefab, towersGroup.transform);
#endif
            tower.name = $"Distant_WaterTower_{i + 1}";
            tower.transform.position = new Vector3(x, y - 0.2f, z);
            tower.transform.rotation = Quaternion.Euler(0f, angles[i] + 90f, 0f);
            tower.transform.localScale = Vector3.one * 16.0f;
            tower.isStatic = true;
        }
    }

    private void SpawnDistantProps(Transform parent)
    {
        GameObject propsGroup = new GameObject("Distant Props & Streetlights");
        propsGroup.transform.SetParent(parent, false);
        propsGroup.isStatic = true;

        for (int i = 0; i < distantStreetlightCount; i++)
        {
            float ang = (i * (360f / distantStreetlightCount) + Random.Range(-10f, 10f)) * Mathf.Deg2Rad;
            float dist = Random.Range(115f, 240f);
            float x = Mathf.Cos(ang) * dist;
            float z = Mathf.Sin(ang) * dist;
            float y = SampleHeight(x, z);

            if (streetlightPrefab != null)
            {
                GameObject lightObj;
#if UNITY_EDITOR
                lightObj = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(streetlightPrefab, propsGroup.transform);
#else
                lightObj = Instantiate(streetlightPrefab, propsGroup.transform);
#endif
                lightObj.name = $"Distant_Streetlight_{i + 1}";
                lightObj.transform.position = new Vector3(x, y, z);
                lightObj.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                lightObj.isStatic = true;
            }

            if (fencePrefab != null && (i % 2 == 0))
            {
                Vector3 basePos = new Vector3(x + 5f, y, z + 5f);
                Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 180f), 0f);
                Vector3 forward = rot * Vector3.forward;

                for (int f = 0; f < 3; f++)
                {
                    Vector3 fPos = basePos + forward * (f * 3.5f);
                    fPos.y = SampleHeight(fPos.x, fPos.z);
                    GameObject fenceObj;
#if UNITY_EDITOR
                    fenceObj = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(fencePrefab, propsGroup.transform);
#else
                    fenceObj = Instantiate(fencePrefab, propsGroup.transform);
#endif
                    fenceObj.name = $"Distant_Fence_{i}_{f}";
                    fenceObj.transform.position = fPos;
                    fenceObj.transform.rotation = rot;
                    fenceObj.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
                    fenceObj.isStatic = true;
                }
            }
        }
    }
}
