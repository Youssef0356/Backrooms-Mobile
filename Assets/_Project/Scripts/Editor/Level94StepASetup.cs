#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Step A: Sets up the base Level 94 terrain mesh (LEVEL 94.glb) with accurate MeshColliders,
/// configures the liminal daylight atmosphere, skybox, horizon fog, and sun lighting.
/// </summary>
public static class Level94StepASetup
{
    private const string ScenePath = "Assets/_Project/Scenes/Level94.unity";
    private const string TerrainGlbPath = "Assets/_Project/Art/Level94/LEVEL 94.glb";

    [MenuItem("Tools/Backrooms/Scene Setup/Level 94/1. Step A - Setup Terrain & Atmosphere", priority = 1)]
    public static void ExecuteStepA()
    {
        // 1. Ensure scene is open
        Scene currentScene = SceneManager.GetActiveScene();
        if (currentScene.path != ScenePath)
        {
            if (File.Exists(ScenePath))
            {
                currentScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            else
            {
                currentScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(currentScene, ScenePath);
            }
        }

        // 2. Setup or locate Directional Light (Level 94 Warm Sun)
        Light dirLight = Object.FindAnyObjectByType<Light>();
        if (dirLight == null || dirLight.type != LightType.Directional)
        {
            GameObject lightGo = new GameObject("Directional Light");
            dirLight = lightGo.AddComponent<Light>();
            dirLight.type = LightType.Directional;
        }

        dirLight.gameObject.name = "Directional Light";
        dirLight.color = new Color(1.0f, 0.96f, 0.88f); // Soft warm sun
        dirLight.intensity = 1.35f;
        dirLight.transform.rotation = Quaternion.Euler(42f, -38f, 0f);
        dirLight.shadows = LightShadows.Soft;

        // 3. Configure Liminal Skybox Material
        Material skyMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Level94/Materials/Level94_LiminalSky.mat");
        if (skyMat == null)
        {
            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                skyMat = new Material(skyShader);
                skyMat.name = "Level94_LiminalSky";
                skyMat.SetFloat("_SunSize", 0.04f);
                skyMat.SetFloat("_SunSizeConvergence", 5f);
                skyMat.SetFloat("_AtmosphereThickness", 0.78f);
                skyMat.SetColor("_SkyTint", new Color(0.28f, 0.60f, 0.98f)); // Crisp surreal cyan-blue
                skyMat.SetColor("_GroundColor", new Color(0.70f, 0.85f, 0.98f)); // Clean soft horizon haze (no green mud)
                skyMat.SetFloat("_Exposure", 1.20f);

                string matDir = "Assets/_Project/Art/Level94/Materials";
                if (!Directory.Exists(matDir)) Directory.CreateDirectory(matDir);
                AssetDatabase.CreateAsset(skyMat, "Assets/_Project/Art/Level94/Materials/Level94_LiminalSky.mat");
            }
        }

        // 4. Configure RenderSettings (Environment & Horizon Fog)
        if (skyMat != null)
        {
            RenderSettings.skybox = skyMat;
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.70f, 0.84f, 0.98f);
        RenderSettings.ambientEquatorColor = new Color(0.82f, 0.86f, 0.92f);
        RenderSettings.ambientGroundColor = new Color(0.40f, 0.46f, 0.52f);
        RenderSettings.ambientIntensity = 1.0f;

        // Horizon blend fog: clear foreground (0 - 140m), soft horizon fade (700m)
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.68f, 0.82f, 0.98f); // Matches the bright clean horizon
        RenderSettings.fogStartDistance = 140f;
        RenderSettings.fogEndDistance = 700f;

        // 5. Setup Terrain Mesh (LEVEL 94.glb)
        GameObject terrainGlb = AssetDatabase.LoadAssetAtPath<GameObject>(TerrainGlbPath);
        if (terrainGlb == null)
        {
            Debug.LogError($"[Step A] Could not find terrain model at {TerrainGlbPath}!");
        }
        else
        {
            GameObject existingTerrain = GameObject.Find("LEVEL 94");
            if (existingTerrain == null)
            {
                existingTerrain = (GameObject)PrefabUtility.InstantiatePrefab(terrainGlb);
                existingTerrain.name = "LEVEL 94";
                existingTerrain.transform.position = Vector3.zero;
                existingTerrain.transform.rotation = Quaternion.identity;
                existingTerrain.transform.localScale = Vector3.one;
                Undo.RegisterCreatedObjectUndo(existingTerrain, "Instantiate LEVEL 94 Terrain");
            }

            // Ensure all submeshes (hills, road) have accurate MeshColliders
            MeshFilter[] meshFilters = existingTerrain.GetComponentsInChildren<MeshFilter>(true);
            int collidersAdded = 0;
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null) continue;
                MeshCollider mc = mf.GetComponent<MeshCollider>();
                if (mc == null)
                {
                    mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = false;
                    collidersAdded++;
                }
                else if (mc.sharedMesh == null)
                {
                    mc.sharedMesh = mf.sharedMesh;
                }
            }
            Debug.Log($"[Step A] Configured terrain '{existingTerrain.name}' with {collidersAdded} MeshColliders.");
        }

        // 6. Ensure Level94_Director with Atmosphere component exists
        GameObject director = GameObject.Find("Level94_Director");
        if (director == null)
        {
            director = new GameObject("Level94_Director");
            Undo.RegisterCreatedObjectUndo(director, "Create Level94_Director");
        }

        Level94Atmosphere atmosphere = director.GetComponent<Level94Atmosphere>();
        if (atmosphere == null)
        {
            atmosphere = Undo.AddComponent<Level94Atmosphere>(director);
        }
        atmosphere.ApplySettings();

        // 7. Ensure Player is positioned comfortably above ground
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            // Raycast down from high up to find exact terrain surface
            Ray ray = new Ray(new Vector3(-80f, 50f, -30f), Vector3.down);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                player.transform.position = hit.point + Vector3.up * 1.2f;
            }
            else
            {
                player.transform.position = new Vector3(-80f, 3.5f, -30f);
            }

            if (cc != null) cc.enabled = true;
        }

        // 8. Save & Update
        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(currentScene);
        EditorSceneManager.SaveScene(currentScene);

        Debug.Log("<color=#00FF88><b>[Step A Completed]</b></color> Terrain mesh placed with colliders, liminal daylight sky & horizon fog configured!");
    }
}
#endif
