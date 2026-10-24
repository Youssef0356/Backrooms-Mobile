#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class DecorationSetupUtility
{
    const string DecorationFolder = "Assets/_Project/Art/Environment/Backrooms/Decoration";

    static DecorationSetupUtility()
    {
        EditorApplication.delayCall += InspectAndFixPrefabs;
    }

    [MenuItem("Tools/Backrooms/Inspect and Fix Decoration Prefabs")]
    public static void InspectAndFixPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { DecorationFolder });
        if (guids.Length == 0)
        {
            Debug.LogWarning("[Decorations] No prefabs found in " + DecorationFolder);
            return;
        }

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            string lower = prefab.name.ToLowerInvariant();

            // Instantiate to inspect
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                Renderer[] renderers = inst.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    Debug.LogWarning($"[Decorations] Prefab '{prefab.name}' has no renderers!");
                    continue;
                }

                // 1. Remove all existing colliders anywhere in the prefab hierarchy
                // This eliminates dozens of redundant child BoxColliders and prevents negative scale PhysX errors
                Collider[] existingCols = inst.GetComponentsInChildren<Collider>(true);
                for (int c = existingCols.Length - 1; c >= 0; c--)
                {
                    Object.DestroyImmediate(existingCols[c]);
                }

                // Compute total bounds in root space
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    b.Encapsulate(renderers[i].bounds);

                Vector3 currentSize = b.size;
                Vector3 centerOffset = b.center - inst.transform.position;

                Debug.Log($"[Decorations] '{prefab.name}' before fix: BoundsSize={currentSize}, CenterOffset={centerOffset}, RootScale={inst.transform.localScale}");

                // 2. If child objects have large offsets (from furniture_pack glb), re-center them so pivot is at bottom-center
                Vector3 bottomCenter = new Vector3(b.center.x, b.min.y, b.center.z);
                Vector3 shift = inst.transform.position - bottomCenter;

                // Shift all direct children so visual mesh is centered at local (0, 0, 0)
                for (int i = 0; i < inst.transform.childCount; i++)
                {
                    Transform child = inst.transform.GetChild(i);
                    child.position += shift;
                }

                // 3. Normalize scaling if way off:
                float targetHeight = GetTargetHeight(prefab.name);
                if (targetHeight > 0f)
                {
                    // Re-evaluate bounds after centering
                    Bounds finalB = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) finalB.Encapsulate(renderers[i].bounds);

                    float height = finalB.size.y;
                    if (height > 0.001f)
                    {
                        float scaleFactor = targetHeight / height;
                        if (Mathf.Abs(height - targetHeight) / targetHeight > 0.15f)
                        {
                            inst.transform.localScale *= scaleFactor;
                            Debug.Log($"[Decorations] Adjusted scale for '{prefab.name}': original height={height:F2}m -> target={targetHeight:F2}m (factor={scaleFactor:F3})");
                        }

                        // Also cap max horizontal dimension for tables so they don't block hallways
                        if (lower.Contains("table") || lower.Contains("desk"))
                        {
                            Bounds tableB = renderers[0].bounds;
                            for (int i = 1; i < renderers.Length; i++) tableB.Encapsulate(renderers[i].bounds);
                            float maxHoriz = Mathf.Max(tableB.size.x, tableB.size.z);
                            if (maxHoriz > 1.85f)
                            {
                                float horizFactor = 1.80f / maxHoriz;
                                inst.transform.localScale *= horizFactor;
                                Debug.Log($"[Decorations] Adjusted table length for '{prefab.name}': original maxHoriz={maxHoriz:F2}m -> 1.80m (factor={horizFactor:F3})");
                            }
                        }
                    }
                }

                // 4. Add exactly one clean BoxCollider on the root object
                // The root has positive uniform scale, avoiding all PhysX negative scale warnings
                Bounds totalB = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    totalB.Encapsulate(renderers[i].bounds);

                BoxCollider rootCol = inst.AddComponent<BoxCollider>();
                rootCol.center = inst.transform.InverseTransformPoint(totalB.center);
                Vector3 worldSize = totalB.size;
                Vector3 rootScale = inst.transform.lossyScale;
                rootCol.size = new Vector3(
                    rootScale.x != 0 ? Mathf.Abs(worldSize.x / rootScale.x) : worldSize.x,
                    rootScale.y != 0 ? Mathf.Abs(worldSize.y / rootScale.y) : worldSize.y,
                    rootScale.z != 0 ? Mathf.Abs(worldSize.z / rootScale.z) : worldSize.z
                );

                // Save back to prefab
                PrefabUtility.SaveAsPrefabAssetAndConnect(inst, path, InteractionMode.AutomatedAction);
                Debug.Log($"[Decorations] Successfully updated prefab: '{prefab.name}' at {path}");
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Decorations] Inspection and fix completed for all decoration prefabs!");
    }

    static float GetTargetHeight(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("chair")) return 0.92f;
        if (lower.Contains("canape") || lower.Contains("canapé") || lower.Contains("sofa")) return 0.82f;
        if (lower.Contains("table") || lower.Contains("desk")) return 0.75f;
        if (lower.Contains("tv")) return 0.55f;
        if (lower.Contains("suitcase")) return 0.45f;
        if (lower.Contains("sign")) return 1.95f;
        return 1.0f;
    }
}
#endif
