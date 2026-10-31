#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Positions the Player (PlayerCapsule) cleanly on the asphalt road in Level 94:
/// - Finds the road surface inside the neighborhood.
/// - Sets player position firmly on top of the road pavement (no spawning in the void/out of bounds).
/// - Aligns rotation facing forward down the scenic neighborhood street.
/// - Synchronizes CharacterController, Cinemachine camera, and camera target.
/// </summary>
public static class Level94PlayerSpawnSetup
{
    private static readonly Vector3 PreferredRoadSample = new Vector3(-18.0f, 0f, -10.0f);
    private static readonly Vector3 LookDirection = new Vector3(0.57f, 0f, 0.82f); // Facing northeast along the road

    [MenuItem("Tools/Backrooms/Scene Setup/Level 94/5. Place Player on Road (Safe Spawn)", priority = 5)]
    public static void PlacePlayerOnRoad()
    {
        Scene currentScene = SceneManager.GetActiveScene();

        // 1. Locate Player
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) player = GameObject.Find("PlayerCapsule");
        if (player == null)
        {
            EditorUtility.DisplayDialog("Player Not Found", "Could not find 'PlayerCapsule' or any object tagged 'Player' in the scene.", "OK");
            return;
        }

        // 2. Ensure terrain colliders are active for raycasting
        GameObject terrain = GameObject.Find("LEVEL 94");
        if (terrain != null)
        {
            MeshFilter[] tMeshes = terrain.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in tMeshes)
            {
                if (mf.sharedMesh == null) continue;
                if (mf.GetComponent<Collider>() == null)
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = false;
                }
            }
        }

        // 3. Raycast down at preferred road coordinates
        Vector3 targetPos = PreferredRoadSample;
        Ray ray = new Ray(new Vector3(PreferredRoadSample.x, 50f, PreferredRoadSample.z), Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            targetPos = hit.point + Vector3.up * 1.05f;
        }
        else
        {
            // Fallback ground test
            targetPos = new Vector3(-18.0f, 1.2f, -10.0f);
        }

        Quaternion targetRot = Quaternion.LookRotation(LookDirection, Vector3.up);

        // 4. Temporarily disable CharacterController during teleport to avoid physics locking
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        Undo.RecordObject(player.transform, "Place Player on Road");
        player.transform.position = targetPos;
        player.transform.rotation = targetRot;

        if (cc != null) cc.enabled = true;

        // 5. Align PlayerCameraRoot
        Transform cameraRoot = player.transform.Find("PlayerCameraRoot");
        if (cameraRoot == null)
        {
            GameObject rootGo = GameObject.Find("PlayerCameraRoot");
            if (rootGo != null) cameraRoot = rootGo.transform;
        }

        if (cameraRoot != null)
        {
            Undo.RecordObject(cameraRoot, "Align Camera Root");
            cameraRoot.position = targetPos + Vector3.up * 1.375f;
            cameraRoot.rotation = targetRot;
        }

        // 6. Align Virtual / Cinemachine Cameras
        var virtualCams = Object.FindObjectsByType<Unity.Cinemachine.CinemachineCamera>(FindObjectsSortMode.None);
        foreach (var vCam in virtualCams)
        {
            Undo.RecordObject(vCam.transform, "Align Virtual Camera");
            vCam.transform.position = targetPos + Vector3.up * 1.375f;
            vCam.transform.rotation = targetRot;
        }

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            Undo.RecordObject(mainCam.transform, "Align Main Camera");
            mainCam.transform.position = targetPos + Vector3.up * 1.375f;
            mainCam.transform.rotation = targetRot;
        }

        EditorSceneManager.MarkSceneDirty(currentScene);
        EditorSceneManager.SaveScene(currentScene);

        Debug.Log($"<color=#00FF88><b>[Player Spawn]</b></color> Successfully placed Player on the neighborhood road at {targetPos} facing {targetRot.eulerAngles.y:F0}°!");
    }
}
#endif
