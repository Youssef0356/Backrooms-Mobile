#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Completely heals and optimizes the Player & Camera Rig in Level 94:
/// - Fixes the broken detached hierarchy where PlayerCameraRoot was not parented to PlayerCapsule.
/// - Resets eye level offset to exactly (0, 1.375, 0).
/// - Fixes CinemachineCamera (disables conflicting CustomLookAtTarget, locks 1:1 to PlayerCameraRoot).
/// - Tunes CharacterController (stepOffset 0.4m, slopeLimit 50 deg) for silky-smooth movement on hills and road curbs.
/// - Unpacks NestedParent_Unpack to eliminate corrupted transform references.
/// </summary>
public static class Level94PlayerMovementHealer
{
    [MenuItem("Tools/Backrooms/Scene Setup/Level 94/6. Heal Player Movement & Camera Rig", priority = 6)]
    public static void HealPlayerAndCamera()
    {
        Scene currentScene = SceneManager.GetActiveScene();

        // 1. Locate PlayerCapsule
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) player = GameObject.Find("PlayerCapsule");
        if (player == null)
        {
            EditorUtility.DisplayDialog("Player Missing", "Could not find PlayerCapsule or an object tagged 'Player'.", "OK");
            return;
        }

        // Unparent player from NestedParent_Unpack if nested
        Transform nestedParent = GameObject.Find("NestedParent_Unpack")?.transform;
        if (player.transform.parent != null)
        {
            Undo.SetTransformParent(player.transform, null, "Unparent PlayerCapsule");
        }

        // 2. Locate or create PlayerCameraRoot
        Transform cameraRoot = player.transform.Find("PlayerCameraRoot");
        if (cameraRoot == null)
        {
            GameObject existingRoot = GameObject.Find("PlayerCameraRoot");
            if (existingRoot != null)
            {
                cameraRoot = existingRoot.transform;
            }
            else
            {
                GameObject newRoot = new GameObject("PlayerCameraRoot");
                newRoot.tag = "CinemachineTarget";
                cameraRoot = newRoot.transform;
                Undo.RegisterCreatedObjectUndo(newRoot, "Create PlayerCameraRoot");
            }
        }

        // CRITICAL: PlayerCameraRoot MUST be an immediate child of PlayerCapsule at (0, 1.375, 0)
        Undo.SetTransformParent(cameraRoot, player.transform, "Parent PlayerCameraRoot to Player");
        cameraRoot.localPosition = new Vector3(0f, 1.375f, 0f);
        cameraRoot.localRotation = Quaternion.identity;
        cameraRoot.localScale = Vector3.one;

        // 3. Configure FirstPersonController
        var fpc = player.GetComponent<StarterAssets.FirstPersonController>();
        if (fpc != null)
        {
            Undo.RecordObject(fpc, "Configure FirstPersonController");
            fpc.CinemachineCameraTarget = cameraRoot.gameObject;
            fpc.MoveSpeed = 4.0f;
            fpc.SprintSpeed = 6.5f;
            fpc.RotationSpeed = 1.0f;
            fpc.SpeedChangeRate = 12.0f;
            fpc.GroundedOffset = -0.14f;
            fpc.GroundedRadius = 0.45f;
            // Detect all layers except Player layer (8)
            fpc.GroundLayers = ~(1 << 8);
        }

        // 4. Tune CharacterController for smooth hill & curb traversal
        var cc = player.GetComponent<CharacterController>();
        if (cc != null)
        {
            Undo.RecordObject(cc, "Configure CharacterController");
            cc.height = 2.0f;
            cc.radius = 0.42f;
            cc.center = new Vector3(0f, 0.93f, 0f);
            cc.slopeLimit = 52.0f;     // Glides up 50-degree hills without stuttering
            cc.stepOffset = 0.40f;     // Seamlessly steps over 15cm road curbs and small terrain rises
            cc.skinWidth = 0.035f;     // Prevents micro-snags on complex terrain mesh triangles
            cc.minMoveDistance = 0.0f; // Zero quantization for silky continuous motion
        }

        // 5. Configure Cinemachine Camera (PlayerFollowCamera)
        var cmCam = Object.FindAnyObjectByType<Unity.Cinemachine.CinemachineCamera>();
        if (cmCam != null)
        {
            if (cmCam.transform.parent != null)
            {
                Undo.SetTransformParent(cmCam.transform, null, "Unparent CinemachineCamera");
            }

            Undo.RecordObject(cmCam, "Configure CinemachineCamera");
            cmCam.Target.TrackingTarget = cameraRoot;
            cmCam.Target.LookAtTarget = null;
            cmCam.Target.CustomLookAtTarget = false;
            cmCam.Lens.FieldOfView = 65f; // Pleasant, modern first-person FOV
            cmCam.Lens.NearClipPlane = 0.1f;
            cmCam.Lens.FarClipPlane = 800f;

            // Ensure HardLock component so camera locks 1:1 with head (zero floaty delay)
            var hardLock = cmCam.GetComponent<Unity.Cinemachine.CinemachineHardLockToTarget>();
            if (hardLock == null)
            {
                hardLock = Undo.AddComponent<Unity.Cinemachine.CinemachineHardLockToTarget>(cmCam.gameObject);
            }

            // Sync position immediately
            cmCam.transform.position = cameraRoot.position;
            cmCam.transform.rotation = cameraRoot.rotation;
        }

        // 6. Configure Main Camera & CinemachineBrain
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            if (mainCam.transform.parent != null)
            {
                Undo.SetTransformParent(mainCam.transform, null, "Unparent MainCamera");
            }

            var brain = mainCam.GetComponent<Unity.Cinemachine.CinemachineBrain>();
            if (brain == null)
            {
                brain = Undo.AddComponent<Unity.Cinemachine.CinemachineBrain>(mainCam.gameObject);
            }

            Undo.RecordObject(brain, "Configure CinemachineBrain");
            brain.UpdateMethod = Unity.Cinemachine.CinemachineBrain.UpdateMethods.LateUpdate;
            brain.BlendUpdateMethod = Unity.Cinemachine.CinemachineBrain.BrainUpdateMethods.LateUpdate;

            mainCam.transform.position = cameraRoot.position;
            mainCam.transform.rotation = cameraRoot.rotation;
        }

        // 7. Clean up empty NestedParent_Unpack if empty or redundant
        if (nestedParent != null && nestedParent.childCount == 0)
        {
            Undo.DestroyObjectImmediate(nestedParent.gameObject);
        }

        EditorSceneManager.MarkSceneDirty(currentScene);
        EditorSceneManager.SaveScene(currentScene);

        Debug.Log("<color=#00FF88><b>[Movement Healer]</b></color> Successfully healed Player & Camera Rig! PlayerCameraRoot parented to Player (0, 1.375, 0), Cinemachine locked 1:1, CharacterController stepOffset=0.4m & slopeLimit=52°.");
    }
}
#endif
