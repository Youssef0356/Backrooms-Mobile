#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class AgathaDiagnostic
{
    [InitializeOnLoadMethod]
    public static void AutoRunDiagnostic()
    {
        EditorApplication.delayCall += InspectAgatha;
    }

    [MenuItem("Tools/Backrooms/Diagnostics/Inspect Agatha in Scene")]
    public static void InspectAgatha()
    {
        // 1. Inspect Model Asset
        string modelPath = "Assets/_Project/Art/Level94/agatha_rig.glb";
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model != null)
        {
            var anim = model.GetComponent<Animator>();
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds assetBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in renderers)
            {
                if (first) { assetBounds = r.bounds; first = false; }
                else assetBounds.Encapsulate(r.bounds);
                Debug.Log($"[Diagnostic-Asset] Renderer '{r.name}': bounds size={r.bounds.size}, center={r.bounds.center}, min={r.bounds.min}, max={r.bounds.max}");
            }
            Debug.Log($"[Diagnostic-Asset] TOTAL MODEL ASSET BOUNDS: size={assetBounds.size}, min={assetBounds.min}, max={assetBounds.max}");
        }

        // 2. Inspect Scene Object
        GameObject agatha = GameObject.Find("Agatha_Stalker");
        if (agatha == null)
        {
            AgathaAI ai = Object.FindAnyObjectByType<AgathaAI>();
            if (ai != null) agatha = ai.gameObject;
        }

        if (agatha != null)
        {
            Debug.Log($"[Diagnostic-Scene] Agatha Name: {agatha.name}, ActiveSelf: {agatha.activeSelf}, ActiveInHierarchy: {agatha.activeInHierarchy}");
            Debug.Log($"[Diagnostic-Scene] Agatha World Pos: {agatha.transform.position}, LocalScale: {agatha.transform.localScale}");

            var agent = agatha.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null)
            {
                Debug.Log($"[Diagnostic-Scene] NavMeshAgent: height={agent.height}, radius={agent.radius}, baseOffset={agent.baseOffset}, isOnNavMesh={agent.isOnNavMesh}");
            }
        }
        else
        {
            Debug.Log("[Diagnostic-Scene] Agatha not in current open scene: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }
    }
}
#endif
