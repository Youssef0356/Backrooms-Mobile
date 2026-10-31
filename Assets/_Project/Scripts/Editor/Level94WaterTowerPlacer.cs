#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places landmark Water Towers (WaterContainer.glb) randomly across the rolling hills of LEVEL 94:
/// - Favors elevated hill peaks and ridges.
/// - Avoids the asphalt road (Object_14) and existing houses.
/// - Multi-leg grounding so stilts touch the grass firmly without floating.
/// - Calibrated realistic scale (~16x = ~17.8m water tower height).
/// - Adds colliders for stilt pillars and tank.
/// </summary>
public static class Level94WaterTowerPlacer
{
    private const string WaterGlbPath = "Assets/_Project/Art/Level94/WaterContainer.glb";
    public const float DefaultTowerScale = 16.0f; // Scale 16x -> ~7.2m footprint, ~17.8m height
    public const int DefaultCount = 6;
    public const float MinSpacing = 28.0f;

    [MenuItem("Tools/Backrooms/Scene Setup/Level 94/4. Place Water Towers Randomly on Hills", priority = 4)]
    public static void PlaceWaterTowersMenu()
    {
        PlaceWaterTowers(DefaultCount, DefaultTowerScale, MinSpacing);
    }

    public static void PlaceWaterTowers(int targetCount = 6, float towerScale = 16.0f, float minSpacing = 28.0f)
    {
        Scene currentScene = SceneManager.GetActiveScene();

        // 1. Locate terrain
        GameObject terrain = GameObject.Find("LEVEL 94");
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("Map Not Found", "Could not find 'LEVEL 94' in the current scene Hierarchy.", "OK");
            return;
        }

        // Ensure terrain has MeshColliders
        MeshFilter[] terrainMeshes = terrain.GetComponentsInChildren<MeshFilter>(true);
        foreach (var mf in terrainMeshes)
        {
            if (mf.sharedMesh == null) continue;
            MeshCollider mc = mf.GetComponent<MeshCollider>();
            if (mc == null)
            {
                mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
            }
        }

        // 2. Load Water Container Model
        GameObject waterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterGlbPath);
        if (waterPrefab == null)
        {
            EditorUtility.DisplayDialog("WaterContainer Missing", $"Could not find model at {WaterGlbPath}!", "OK");
            return;
        }

        // 3. Clear existing water container group
        GameObject existingGroup = GameObject.Find("LEVEL 94 Water Towers (Randomized)");
        if (existingGroup != null)
        {
            Undo.DestroyObjectImmediate(existingGroup);
        }

        GameObject towerGroup = new GameObject("LEVEL 94 Water Towers (Randomized)");
        Undo.RegisterCreatedObjectUndo(towerGroup, "Place Random Water Towers");

        // 4. Measure terrain bounds
        Bounds terrainBounds = new Bounds(terrain.transform.position, Vector3.zero);
        Renderer[] rends = terrain.GetComponentsInChildren<Renderer>();
        foreach (var r in rends)
        {
            if (terrainBounds.size == Vector3.zero) terrainBounds = r.bounds;
            else terrainBounds.Encapsulate(r.bounds);
        }

        float minX = terrainBounds.min.x + 14f;
        float maxX = terrainBounds.max.x - 14f;
        float minZ = terrainBounds.min.z + 14f;
        float maxZ = terrainBounds.max.z - 14f;

        // Collect existing house positions to avoid overlapping
        List<Vector3> avoidPositions = new List<Vector3>();
        GameObject houseGroup = GameObject.Find("LEVEL 94 Houses (Randomized)");
        if (houseGroup != null)
        {
            foreach (Transform h in houseGroup.transform)
            {
                avoidPositions.Add(h.position);
            }
        }

        List<Vector3> placedTowers = new List<Vector3>();
        int placed = 0;
        int attempts = 0;
        const int maxAttempts = 600;

        // Stilt radius in world space (~3.6m radius at scale 16)
        float stiltRadius = 0.225f * towerScale;

        while (placed < targetCount && attempts < maxAttempts)
        {
            attempts++;

            float rx = Random.Range(minX, maxX);
            float rz = Random.Range(minZ, maxZ);

            // Spacing from existing water towers
            bool tooClose = false;
            foreach (var p in placedTowers)
            {
                if (Vector2.Distance(new Vector2(rx, rz), new Vector2(p.x, p.z)) < minSpacing)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            // Spacing from houses (at least 15m away)
            foreach (var hp in avoidPositions)
            {
                if (Vector2.Distance(new Vector2(rx, rz), new Vector2(hp.x, hp.z)) < 15.0f)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            // Center raycast
            Ray ray = new Ray(new Vector3(rx, terrainBounds.max.y + 25f, rz), Vector3.down);
            if (!Physics.Raycast(ray, out RaycastHit centerHit, 160f)) continue;

            // Must NOT hit the road
            if (centerHit.collider.name.Contains("14") || centerHit.collider.name.ToLower().Contains("road"))
            {
                continue;
            }

            // In Level 94, water towers look best perched on elevated hills!
            // Prefer positions above the lower 35% of the terrain height range
            float heightNormalized = (centerHit.point.y - terrainBounds.min.y) / Mathf.Max(0.1f, terrainBounds.size.y);
            // Allow mid-to-high elevations (threshold gets slightly lenient after 200 attempts)
            float minHeightThresh = attempts < 250 ? 0.35f : 0.15f;
            if (heightNormalized < minHeightThresh) continue;

            // Check ground under the 4 legs (stilt positions)
            Vector3[] legOffsets = new Vector3[]
            {
                new Vector3(-stiltRadius, 0, -stiltRadius),
                new Vector3(stiltRadius, 0, -stiltRadius),
                new Vector3(-stiltRadius, 0, stiltRadius),
                new Vector3(stiltRadius, 0, stiltRadius)
            };

            bool legOnRoad = false;
            float lowestY = centerHit.point.y;
            float highestY = centerHit.point.y;

            foreach (var offset in legOffsets)
            {
                Vector3 legPos = new Vector3(rx + offset.x, terrainBounds.max.y + 25f, rz + offset.z);
                if (Physics.Raycast(new Ray(legPos, Vector3.down), out RaycastHit legHit, 160f))
                {
                    if (legHit.collider.name.Contains("14") || legHit.collider.name.ToLower().Contains("road"))
                    {
                        legOnRoad = true;
                        break;
                    }
                    lowestY = Mathf.Min(lowestY, legHit.point.y);
                    highestY = Mathf.Max(highestY, legHit.point.y);
                }
                else
                {
                    legOnRoad = true;
                    break;
                }
            }

            if (legOnRoad) continue;

            // If hill is a cliff (variance > 2.8m across the 7m stilt base), skip to a smoother peak
            if (highestY - lowestY > 2.8f) continue;

            // Anchor tower so legs embed down to the lowest leg contact point
            // This guarantees all 4 stilt legs are solidly touching or embedded into the ground
            Vector3 finalPos = new Vector3(rx, lowestY - 0.08f, rz);
            Quaternion finalRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            // Instantiate
            GameObject tower = (GameObject)PrefabUtility.InstantiatePrefab(waterPrefab);
            tower.name = $"WaterContainer_{placed + 1}";
            tower.transform.SetParent(towerGroup.transform, false);
            tower.transform.position = finalPos;
            tower.transform.rotation = finalRot;
            tower.transform.localScale = new Vector3(towerScale, towerScale, towerScale);

            // Add colliders
            MeshFilter[] meshes = tower.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in meshes)
            {
                if (mf.sharedMesh == null) continue;
                MeshCollider mc = mf.GetComponent<MeshCollider>();
                if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }

            placedTowers.Add(finalPos);
            placed++;
        }

        EditorSceneManager.MarkSceneDirty(currentScene);
        EditorSceneManager.SaveScene(currentScene);

        Debug.Log($"<color=#00FF88><b>[Water Towers]</b></color> Successfully placed {placed} Water Towers randomly on hilltops across '{terrain.name}' at scale {towerScale}!");
    }
}
#endif
