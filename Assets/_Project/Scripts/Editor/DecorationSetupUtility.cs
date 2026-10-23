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

                // Compute total bounds in root space
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    b.Encapsulate(renderers[i].bounds);

                Vector3 currentSize = b.size;
                Vector3 centerOffset = b.center - inst.transform.position;

                Debug.Log($"[Decorations] '{prefab.name}' before fix: BoundsSize={currentSize}, CenterOffset={centerOffset}, RootScale={inst.transform.localScale}");

                // 1. If child objects have large offsets (from furniture_pack glb), re-center them so pivot is at bottom-center
                Vector3 bottomCenter = new Vector3(b.center.x, b.min.y, b.center.z);
                Vector3 shift = inst.transform.position - bottomCenter;

                // Shift all direct children so visual mesh is centered at local (0, 0, 0)
                for (int i = 0; i < inst.transform.childCount; i++)
                {
                    Transform child = inst.transform.GetChild(i);
                    child.position += shift;
                }

                // 2. Ensure each renderer has a BoxCollider matching its mesh bounds
                foreach (Renderer r in renderers)
                {
                    Collider col = r.GetComponent<Collider>();
                    if (col == null)
                    {
                        MeshFilter mf = r.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null)
                        {
                            BoxCollider bc = r.gameObject.AddComponent<BoxCollider>();
                            bc.center = mf.sharedMesh.bounds.center;
                            bc.size = mf.sharedMesh.bounds.size;
                        }
                        else
                        {
                            BoxCollider bc = r.gameObject.AddComponent<BoxCollider>();
                            bc.center = r.localBounds.center;
                            bc.size = r.localBounds.size;
                        }
                    }
                }

                // Also ensure root or child has a collider if no child collider was added
                if (inst.GetComponentsInChildren<Collider>().Length == 0)
                {
                    BoxCollider rootCol = inst.AddComponent<BoxCollider>();
                    Bounds newB = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) newB.Encapsulate(renderers[i].bounds);
                    rootCol.center = new Vector3(0f, newB.size.y * 0.5f, 0f);
                    rootCol.size = newB.size;
                }

                // 3. Normalize scaling if way off:
                // Target heights:
                // Chair / FatChair / ChairWheel / ChairWithWheels -> ~0.85m to 1.05m
                // Table / ModernTable -> ~0.75m to 0.9m
                // Canapé -> ~0.85m to 1.0m
                // TV 1 -> ~0.5m to 0.7m
                // metal_suitcase_doesnt_open -> ~0.45m to 0.55m
                // backrooms_road_sign -> ~1.8m to 2.2m
                // the_backrooms_chairs -> ~0.85m to 1.0m
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
                        // If current height deviates significantly (e.g. > 30% from realistic height)
                        if (Mathf.Abs(height - targetHeight) / targetHeight > 0.25f)
                        {
                            inst.transform.localScale *= scaleFactor;
                            Debug.Log($"[Decorations] Adjusted scale for '{prefab.name}': original height={height:F2}m -> target={targetHeight:F2}m (factor={scaleFactor:F3})");
                        }
                    }
                }

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
        if (lower.Contains("chair")) return 0.95f;
        if (lower.Contains("canape") || lower.Contains("canapé")) return 0.85f;
        if (lower.Contains("table")) return 0.80f;
        if (lower.Contains("tv")) return 0.65f;
        if (lower.Contains("suitcase")) return 0.45f;
        if (lower.Contains("sign")) return 2.0f;
        return 1.0f;
    }
}
#endif
