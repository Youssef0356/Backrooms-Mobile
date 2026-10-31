#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BedSetupUtility
{
    const string ModelPath = "Assets/_Project/Art/Environment/Backrooms/Models/bed_with_extras.glb";
    const string OutputPrefabPath = "Assets/_Project/Art/Environment/Backrooms/Decoration/Bed.prefab";

    [MenuItem("Tools/Backrooms/Prefabs & Assets/Build and Calibrate Bed Prefab")]
    public static void SetupBedPrefab()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError($"[BedSetup] Could not load bed model at {ModelPath}");
            return;
        }

        // Create Root GameObject
        GameObject root = new GameObject("Bed");

        // Instantiate visual model as child
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.name = "VisualModel";
        visual.transform.SetParent(root.transform, false);

        // Remove any existing colliders from imported model
        Collider[] existingCols = visual.GetComponentsInChildren<Collider>(true);
        for (int c = existingCols.Length - 1; c >= 0; c--)
        {
            Object.DestroyImmediate(existingCols[c]);
        }

        // Measure raw bounds
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogError("[BedSetup] No renderers found in bed model!");
            Object.DestroyImmediate(root);
            return;
        }

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            b.Encapsulate(renderers[i].bounds);
        }

        Debug.Log($"[BedSetup] Raw bounds before normalization: Size={b.size}, Center={b.center}, Min={b.min}, Max={b.max}");

        // Target proportional dimensions matching the ~1.28m living room sofa:
        // Length ~ 1.65m, Width ~ 1.30m, Height ~ 0.45m
        float currentLength = Mathf.Max(b.size.x, b.size.z);
        const float targetLength = 1.65f;
        float scaleFactor = currentLength > 0.001f ? targetLength / currentLength : 0.62f;

        visual.transform.localScale = Vector3.one * scaleFactor;

        // Re-measure after scale to find exact floor offset and center
        b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            b.Encapsulate(renderers[i].bounds);
        }

        // Offset visual child so root (0, 0, 0) is at the bottom center of the bed
        Vector3 centerOffset = b.center - root.transform.position;
        float bottomY = b.min.y - root.transform.position.y;
        visual.transform.localPosition = new Vector3(-centerOffset.x, -bottomY, -centerOffset.z);

        // Re-measure final world bounds to size BoxCollider
        b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            b.Encapsulate(renderers[i].bounds);
        }

        BoxCollider col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, b.size.y * 0.5f, 0f);
        col.size = new Vector3(b.size.x * 0.95f, b.size.y, b.size.z * 0.95f);
        Vector3 finalColliderSize = col.size;

        // Ensure materials use URP Lit shader if needed
        Shader urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLitShader != null)
        {
            foreach (var r in renderers)
            {
                Material[] mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null && (mats[m].shader == null || mats[m].shader.name.Contains("Standard") || mats[m].shader.name.Contains("Hidden")))
                    {
                        mats[m].shader = urpLitShader;
                    }
                }
                r.sharedMaterials = mats;
            }
        }

        // Save as Prefab Asset
        string dir = Path.GetDirectoryName(OutputPrefabPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, OutputPrefabPath);
        Object.DestroyImmediate(root);

        Debug.Log($"[BedSetup] Successfully created calibrated Bed prefab at {OutputPrefabPath}! Final size: {finalColliderSize}");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
#endif
