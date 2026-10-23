using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ClarkMaterialUtility
{
    private const string GlbPath = "Assets/_Project/Art/Characters/Clark/Pirate clark norpa.glb";
    private const string MaterialsFolder = "Assets/_Project/Art/Characters/Clark/Materials";

    static ClarkMaterialUtility()
    {
        EditorApplication.delayCall += Execute;
    }

    [MenuItem("Tools/Backrooms/Setup Clark Editable Materials")]
    public static void Execute()
    {
        if (!Directory.Exists(MaterialsFolder))
        {
            Directory.CreateDirectory(MaterialsFolder);
            AssetDatabase.Refresh();
        }

        Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(GlbPath);
        Debug.Log($"[ClarkMaterialUtility] Inspecting assets in {GlbPath} (total: {allAssets.Length})");

        int matCount = 0;
        foreach (var obj in allAssets)
        {
            if (obj is Material sourceMat)
            {
                matCount++;
                string targetPath = $"{MaterialsFolder}/{sourceMat.name}.mat";
                Material existing = AssetDatabase.LoadAssetAtPath<Material>(targetPath);

                if (existing == null)
                {
                    Material newMat = new Material(sourceMat);
                    // Ensure URP Lit shader is used
                    Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
                    if (urpLit != null)
                    {
                        newMat.shader = urpLit;
                    }

                    // By default, let's make sure Smoothness is set to a nice non-rough value (e.g. 0.4 - 0.5)
                    if (newMat.HasProperty("_Smoothness"))
                    {
                        newMat.SetFloat("_Smoothness", 0.45f);
                    }

                    AssetDatabase.CreateAsset(newMat, targetPath);
                    Debug.Log($"[ClarkMaterialUtility] Created editable material: {targetPath} from {sourceMat.name}");
                }
                else
                {
                    Debug.Log($"[ClarkMaterialUtility] Material already exists: {targetPath}");
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Also check if Clark is in the active scene and assign the external materials
        ApplyMaterialsToSceneClark();
    }

    [MenuItem("Tools/Backrooms/Apply Editable Materials to Scene Clark")]
    public static void ApplyMaterialsToSceneClark()
    {
        GameObject clarkInScene = GameObject.Find("Clark");
        if (clarkInScene == null)
        {
            var allGo = Object.FindObjectsByType<GameObject>();
            foreach (var go in allGo)
            {
                if (go.name.Contains("clark", System.StringComparison.OrdinalIgnoreCase) ||
                    go.name.Contains("Pirate", System.StringComparison.OrdinalIgnoreCase))
                {
                    clarkInScene = go;
                    break;
                }
            }
        }

        if (clarkInScene == null)
        {
            Debug.Log("[ClarkMaterialUtility] Clark not found in open scene.");
            return;
        }

        Renderer[] renderers = clarkInScene.GetComponentsInChildren<Renderer>(true);
        int replacedCount = 0;

        foreach (var rend in renderers)
        {
            Material[] mats = rend.sharedMaterials;
            bool modified = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string matName = mats[i].name.Replace(" (Instance)", "");
                string externalPath = $"{MaterialsFolder}/{matName}.mat";
                Material extMat = AssetDatabase.LoadAssetAtPath<Material>(externalPath);
                if (extMat != null && extMat != mats[i])
                {
                    mats[i] = extMat;
                    modified = true;
                    replacedCount++;
                }
            }

            if (modified)
            {
                Undo.RecordObject(rend, "Assign Editable Clark Materials");
                rend.sharedMaterials = mats;
                EditorUtility.SetDirty(rend);
            }
        }

        Debug.Log($"[ClarkMaterialUtility] Successfully updated {replacedCount} materials on {clarkInScene.name} in scene!");
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
    }
}
