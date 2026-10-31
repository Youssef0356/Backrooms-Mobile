#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class TVSetupUtility
{
    const string ModelPath = "Assets/_Project/Art/Environment/Backrooms/Models/crt_tv.glb";
    const string OutputPrefabPath = "Assets/_Project/Art/Environment/Backrooms/Decoration/CRT_TV.prefab";

    [MenuItem("Tools/Backrooms/Prefabs & Assets/Build and Calibrate CRT TV Prefab")]
    public static void SetupTVPrefab()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.Log($"[TVSetup] No model found at {ModelPath}");
            return;
        }

        GameObject root = new GameObject("CRT_TV");
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.name = "VisualModel";
        visual.transform.SetParent(root.transform, false);

        // Remove any legacy colliders
        Collider[] existingCols = visual.GetComponentsInChildren<Collider>(true);
        for (int c = existingCols.Length - 1; c >= 0; c--)
        {
            Object.DestroyImmediate(existingCols[c]);
        }

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogError("[TVSetup] No renderers found in TV model!");
            Object.DestroyImmediate(root);
            return;
        }

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            b.Encapsulate(renderers[i].bounds);
        }

        Debug.Log($"[TVSetup] Raw TV bounds: Size={b.size}, Center={b.center}");

        // Normalize to realistic human Backrooms scale: standard 20-inch CRT (~0.54m width, ~0.45m height)
        float currentWidth = Mathf.Max(b.size.x, b.size.z);
        const float targetWidth = 0.54f;
        float scaleFactor = currentWidth > 0.001f ? targetWidth / currentWidth : 0.012f;
        visual.transform.localScale = Vector3.one * scaleFactor;

        // Re-measure after scaling
        b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            b.Encapsulate(renderers[i].bounds);
        }

        // Center on X and Z, place bottom flush at Y=0
        Vector3 centerOffset = b.center - root.transform.position;
        float bottomY = b.min.y - root.transform.position.y;
        visual.transform.localPosition = new Vector3(-centerOffset.x, -bottomY, -centerOffset.z);

        // Add clean BoxCollider
        b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            b.Encapsulate(renderers[i].bounds);
        }

        BoxCollider col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, b.size.y * 0.5f, 0f);
        col.size = new Vector3(b.size.x * 0.95f, b.size.y, b.size.z * 0.95f);

        // Ensure materials use URP Lit
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

        Vector3 colSize = col.size;
        string dir = Path.GetDirectoryName(OutputPrefabPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        PrefabUtility.SaveAsPrefabAsset(root, OutputPrefabPath);
        Object.DestroyImmediate(root);

        Debug.Log($"[TVSetup] Successfully created calibrated CRT TV prefab at {OutputPrefabPath}! Final bounds: {colSize}");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
#endif
