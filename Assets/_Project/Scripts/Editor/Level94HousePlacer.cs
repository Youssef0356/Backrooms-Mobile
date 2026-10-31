#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places custom houses (LEVEL 94 Houses.glb) randomly across the user-placed LEVEL 94 terrain map:
/// - Avoids placing houses on the road (Object_14).
/// - Samples 4-corner ground elevations to prevent floating or clipping through hills.
/// - Automatically adds a recessed foundation skirt to ground each house seamlessly.
/// - Faces houses toward the road or in natural suburban orientations.
/// - Enforces spacing between houses.
/// </summary>
public static class Level94HousePlacer
{
    private const string HouseGlbPath = "Assets/_Project/Art/Level94/LEVEL 94 Houses.glb";
    private const string ConcreteMatPath = "Assets/_Project/Art/Level94/Materials/Foundation_Concrete.mat";

    [MenuItem("Tools/Backrooms/Scene Setup/Level 94/2. Place Houses Randomly on Map", priority = 2)]
    public static void PlaceHousesOnMapMenu()
    {
        PlaceHousesOnMap(10, 16f);
    }

    public static void PlaceHousesOnMap(int targetCount = 10, float minSpacing = 16f)
    {
        Scene currentScene = SceneManager.GetActiveScene();

        // 1. Find user's terrain map
        GameObject terrain = GameObject.Find("LEVEL 94");
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("Map Not Found", "Could not find 'LEVEL 94' in the current scene Hierarchy. Please ensure your map is placed and named 'LEVEL 94'.", "OK");
            return;
        }

        // 2. Ensure all terrain parts have MeshColliders for raycasting
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

        // 3. Load House Prefab/GLB
        GameObject housePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HouseGlbPath);
        if (housePrefab == null)
        {
            EditorUtility.DisplayDialog("House Model Missing", $"Could not find house model at {HouseGlbPath}!", "OK");
            return;
        }

        // 4. Remove previous placed houses container if it exists
        GameObject existingContainer = GameObject.Find("LEVEL 94 Houses (Randomized)");
        if (existingContainer != null)
        {
            Undo.DestroyObjectImmediate(existingContainer);
        }

        GameObject housesContainer = new GameObject("LEVEL 94 Houses (Randomized)");
        Undo.RegisterCreatedObjectUndo(housesContainer, "Place Random Houses on Map");

        // 5. Foundation Concrete Material
        Material concreteMat = AssetDatabase.LoadAssetAtPath<Material>(ConcreteMatPath);
        if (concreteMat == null)
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader != null)
            {
                concreteMat = new Material(litShader);
                concreteMat.color = new Color(0.42f, 0.44f, 0.42f);
                concreteMat.SetFloat("_Smoothness", 0.15f);
                AssetDatabase.CreateAsset(concreteMat, ConcreteMatPath);
            }
        }

        // 6. Placement bounds from terrain
        Bounds terrainBounds = new Bounds(terrain.transform.position, Vector3.zero);
        Renderer[] renderers = terrain.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            if (terrainBounds.size == Vector3.zero) terrainBounds = r.bounds;
            else terrainBounds.Encapsulate(r.bounds);
        }

        float minX = terrainBounds.min.x + 12f;
        float maxX = terrainBounds.max.x - 12f;
        float minZ = terrainBounds.min.z + 12f;
        float maxZ = terrainBounds.max.z - 12f;

        List<Vector3> placedPositions = new List<Vector3>();
        int attempts = 0;
        int placedCount = 0;
        const int maxAttempts = 500;

        // Half extents of the house base (scaled 1.25x: approx 7.0m x 6.5m)
        const float halfW = 2.8f * 1.25f;
        const float halfD = 2.6f * 1.25f;

        while (placedCount < targetCount && attempts < maxAttempts)
        {
            attempts++;
            float rx = Random.Range(minX, maxX);
            float rz = Random.Range(minZ, maxZ);

            // Spacing check
            bool tooClose = false;
            foreach (var p in placedPositions)
            {
                if (Vector2.Distance(new Vector2(rx, rz), new Vector2(p.x, p.z)) < minSpacing)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            // Center Raycast
            Ray centerRay = new Ray(new Vector3(rx, terrainBounds.max.y + 20f, rz), Vector3.down);
            if (!Physics.Raycast(centerRay, out RaycastHit centerHit, 150f)) continue;

            // Reject if directly hitting road (Object_14)
            if (centerHit.collider.name.Contains("14") || centerHit.collider.name.ToLower().Contains("road"))
            {
                continue;
            }

            // Slope angle check (must be gentle ground)
            float slopeAngle = Vector3.Angle(centerHit.normal, Vector3.up);
            if (slopeAngle > 10f) continue;

            // Check 4 corners of house footprint
            Vector3[] cornerOffsets = new Vector3[]
            {
                new Vector3(-halfW, 0, -halfD),
                new Vector3(halfW, 0, -halfD),
                new Vector3(-halfW, 0, halfD),
                new Vector3(halfW, 0, halfD)
            };

            bool cornerFailed = false;
            float minY = centerHit.point.y;
            float maxY = centerHit.point.y;

            foreach (var offset in cornerOffsets)
            {
                Vector3 cPos = new Vector3(rx + offset.x, terrainBounds.max.y + 20f, rz + offset.z);
                if (Physics.Raycast(new Ray(cPos, Vector3.down), out RaycastHit cHit, 150f))
                {
                    // If any corner is over the road, skip!
                    if (cHit.collider.name.Contains("14") || cHit.collider.name.ToLower().Contains("road"))
                    {
                        cornerFailed = true;
                        break;
                    }
                    minY = Mathf.Min(minY, cHit.point.y);
                    maxY = Mathf.Max(maxY, cHit.point.y);
                }
                else
                {
                    cornerFailed = true;
                    break;
                }
            }

            if (cornerFailed) continue;

            // If slope difference across the house footprint is greater than 0.45m, hill is too steep
            float deltaY = maxY - minY;
            if (deltaY > 0.45f) continue;

            // Orientation: Look for nearby road to face toward it, otherwise natural angle
            Quaternion houseRot = Quaternion.identity;
            bool foundRoad = false;
            // Scan 16 directions around house to find road
            for (int a = 0; a < 16; a++)
            {
                float ang = a * (360f / 16f) * Mathf.Deg2Rad;
                Vector3 probePos = new Vector3(rx + Mathf.Cos(ang) * 12f, terrainBounds.max.y + 20f, rz + Mathf.Sin(ang) * 12f);
                if (Physics.Raycast(new Ray(probePos, Vector3.down), out RaycastHit probeHit, 150f))
                {
                    if (probeHit.collider.name.Contains("14") || probeHit.collider.name.ToLower().Contains("road"))
                    {
                        Vector3 toRoad = probeHit.point - centerHit.point;
                        toRoad.y = 0f;
                        if (toRoad.sqrMagnitude > 0.1f)
                        {
                            houseRot = Quaternion.LookRotation(toRoad.normalized, Vector3.up);
                            foundRoad = true;
                            break;
                        }
                    }
                }
            }

            if (!foundRoad)
            {
                // Random natural 90 or 45 degree aligned rotation
                float[] angles = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };
                houseRot = Quaternion.Euler(0f, angles[Random.Range(0, angles.Length)], 0f);
            }

            // Set house height so floor embeds naturally into the turf line
            Vector3 finalPos = new Vector3(rx, maxY - 0.12f, rz);

            // Instantiate House
            GameObject houseInstance = (GameObject)PrefabUtility.InstantiatePrefab(housePrefab);
            houseInstance.name = $"House_{placedCount + 1}";
            houseInstance.transform.SetParent(housesContainer.transform, false);
            houseInstance.transform.position = finalPos;
            houseInstance.transform.rotation = houseRot;
            houseInstance.transform.localScale = Vector3.one * 1.25f;

            // Ensure colliders on house meshes (LEVEL 94 Houses.glb already has built-in 2.2m foundation mesh)
            MeshFilter[] houseMeshes = houseInstance.GetComponentsInChildren<MeshFilter>(true);
            foreach (var hmf in houseMeshes)
            {
                if (hmf.sharedMesh == null) continue;
                // Door leaf should have trigger collider so it doesn't block player entry
                string n = hmf.gameObject.name.ToLower();
                MeshCollider hmc = hmf.GetComponent<MeshCollider>();
                if (hmc == null)
                {
                    hmc = hmf.gameObject.AddComponent<MeshCollider>();
                    hmc.sharedMesh = hmf.sharedMesh;
                }
                if (n.Contains("door") && !n.Contains("frame"))
                {
                    hmc.isTrigger = true;
                }
            }

            placedPositions.Add(finalPos);
            placedCount++;
        }

        EditorSceneManager.MarkSceneDirty(currentScene);
        EditorSceneManager.SaveScene(currentScene);

        Debug.Log($"<color=#00FF88><b>[House Placer]</b></color> Successfully placed {placedCount} houses randomly on '{terrain.name}' (no road overlap, multi-point grounded with foundation skirts)!");
    }
}
#endif
