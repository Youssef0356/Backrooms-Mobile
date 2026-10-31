#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds perimeter border fences along the outer boundaries of LEVEL 94:
/// - Uses Fences.glb at exact localScale = (0.01, 0.01, 0.01).
/// - Conforms vertically to the terrain surface at each post.
/// - Leaves an opening where the road enters/exits the map boundary.
/// - Adds BoxColliders so the fences act as physical perimeter barriers.
/// </summary>
public static class Level94BorderFenceBuilder
{
    private const string FenceGlbPath = "Assets/_Project/Art/Level94/Fences.glb";
    private const float TargetScale = 0.01f;

    [MenuItem("Tools/Backrooms/Scene Setup/Level 94/3. Build Perimeter Border Fences (Scale 0.01)", priority = 3)]
    public static void BuildBorderFences()
    {
        Scene currentScene = SceneManager.GetActiveScene();

        // 1. Locate terrain
        GameObject terrain = GameObject.Find("LEVEL 94");
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("Map Not Found", "Could not find 'LEVEL 94' in the current scene Hierarchy.", "OK");
            return;
        }

        // Ensure terrain has MeshColliders for raycasting
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

        // 2. Load Fence Model
        GameObject fencePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FenceGlbPath);
        if (fencePrefab == null)
        {
            EditorUtility.DisplayDialog("Fence Missing", $"Could not find fence model at {FenceGlbPath}!", "OK");
            return;
        }

        // 3. Clear previous border fences container if existing
        GameObject existingContainer = GameObject.Find("LEVEL 94 Border Fences");
        if (existingContainer != null)
        {
            Undo.DestroyObjectImmediate(existingContainer);
        }

        GameObject borderContainer = new GameObject("LEVEL 94 Border Fences");
        Undo.RegisterCreatedObjectUndo(borderContainer, "Build Perimeter Border Fences");

        // 4. Measure terrain bounds
        Bounds terrainBounds = new Bounds(terrain.transform.position, Vector3.zero);
        Renderer[] rends = terrain.GetComponentsInChildren<Renderer>();
        foreach (var r in rends)
        {
            if (terrainBounds.size == Vector3.zero) terrainBounds = r.bounds;
            else terrainBounds.Encapsulate(r.bounds);
        }

        // Inset boundary slightly from the sheer edge
        const float inset = 3.5f;
        float minX = terrainBounds.min.x + inset;
        float maxX = terrainBounds.max.x - inset;
        float minZ = terrainBounds.min.z + inset;
        float maxZ = terrainBounds.max.z - inset;

        // 5. Measure test fence instance to get exact segment length and pivot offset
        GameObject testInstance = (GameObject)PrefabUtility.InstantiatePrefab(fencePrefab);
        testInstance.transform.localScale = new Vector3(TargetScale, TargetScale, TargetScale);
        Renderer testRend = testInstance.GetComponentInChildren<Renderer>();
        Bounds testBounds = testRend != null ? testRend.bounds : new Bounds(Vector3.zero, new Vector3(2f, 1.1f, 0.1f));

        float segLength = Mathf.Max(testBounds.size.x, testBounds.size.z);
        if (segLength < 0.5f) segLength = 2.0f; // safety fallback
        float fenceHeight = testBounds.size.y > 0.2f ? testBounds.size.y : 1.1f;
        float pivotBottomDelta = testBounds.min.y - testInstance.transform.position.y;
        Object.DestroyImmediate(testInstance);

        Debug.Log($"[Border Fences] Fence segment length: {segLength:F2}m, Height: {fenceHeight:F2}m at scale {TargetScale}");

        int fenceCount = 0;

        // Helper to spawn a segment
        void PlaceSegment(Vector3 targetCenter, Quaternion targetRot)
        {
            // Raycast down to find ground elevation
            Ray ray = new Ray(new Vector3(targetCenter.x, terrainBounds.max.y + 20f, targetCenter.z), Vector3.down);
            if (!Physics.Raycast(ray, out RaycastHit hit, 150f)) return;

            // Leave opening where road crosses border
            if (hit.collider.name.Contains("14") || hit.collider.name.ToLower().Contains("road"))
            {
                return;
            }

            // Also check 1m around to leave a clean shoulder opening for the road
            if (Physics.Raycast(new Ray(new Vector3(targetCenter.x + 1f, terrainBounds.max.y + 20f, targetCenter.z), Vector3.down), out RaycastHit r1))
            {
                if (r1.collider.name.Contains("14") || r1.collider.name.ToLower().Contains("road")) return;
            }
            if (Physics.Raycast(new Ray(new Vector3(targetCenter.x - 1f, terrainBounds.max.y + 20f, targetCenter.z), Vector3.down), out RaycastHit r2))
            {
                if (r2.collider.name.Contains("14") || r2.collider.name.ToLower().Contains("road")) return;
            }

            GameObject fence = (GameObject)PrefabUtility.InstantiatePrefab(fencePrefab);
            fence.name = $"BorderFence_{fenceCount + 1}";
            fence.transform.SetParent(borderContainer.transform, false);

            // Apply exact requested scale: 0.01
            fence.transform.localScale = new Vector3(TargetScale, TargetScale, TargetScale);
            fence.transform.rotation = targetRot;

            // Position with bottom resting on ground
            fence.transform.position = hit.point - Vector3.up * pivotBottomDelta;

            // Add physical boundary collider
            MeshFilter mf = fence.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                MeshCollider mc = mf.GetComponent<MeshCollider>();
                if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
            else
            {
                BoxCollider col = fence.GetComponent<BoxCollider>();
                if (col == null) col = fence.AddComponent<BoxCollider>();
                col.size = new Vector3(segLength / TargetScale, fenceHeight / TargetScale, 0.25f / TargetScale);
                col.center = new Vector3(0f, (fenceHeight * 0.5f) / TargetScale, 0f);
            }

            fenceCount++;
        }

        // Side 1: South Edge (minZ, running along X from minX to maxX)
        Quaternion rotSouth = Quaternion.identity;
        for (float x = minX + segLength * 0.5f; x <= maxX; x += segLength)
        {
            PlaceSegment(new Vector3(x, 0, minZ), rotSouth);
        }

        // Side 2: North Edge (maxZ, running along X from minX to maxX)
        Quaternion rotNorth = Quaternion.Euler(0f, 180f, 0f);
        for (float x = minX + segLength * 0.5f; x <= maxX; x += segLength)
        {
            PlaceSegment(new Vector3(x, 0, maxZ), rotNorth);
        }

        // Side 3: West Edge (minX, running along Z from minZ to maxZ)
        Quaternion rotWest = Quaternion.Euler(0f, 90f, 0f);
        for (float z = minZ + segLength * 0.5f; z <= maxZ; z += segLength)
        {
            PlaceSegment(new Vector3(minX, 0, z), rotWest);
        }

        // Side 4: East Edge (maxX, running along Z from minZ to maxZ)
        Quaternion rotEast = Quaternion.Euler(0f, -90f, 0f);
        for (float z = minZ + segLength * 0.5f; z <= maxZ; z += segLength)
        {
            PlaceSegment(new Vector3(maxX, 0, z), rotEast);
        }

        EditorSceneManager.MarkSceneDirty(currentScene);
        EditorSceneManager.SaveScene(currentScene);

        Debug.Log($"<color=#00FF88><b>[Border Fences]</b></color> Successfully generated {fenceCount} perimeter border fences at exact scale {TargetScale} enclosing '{terrain.name}'!");
    }
}
#endif
