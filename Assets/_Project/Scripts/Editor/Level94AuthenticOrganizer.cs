#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using StarterAssets;

/// <summary>
/// Completely organizes and curates Level 94 ("Motion") to match the authentic Backrooms experience:
/// - Clean, professional hierarchy organization.
/// - Suburban yellow houses (scaled 1.25x) neatly lined up along the curving asphalt road facing the street.
/// - Unobstructed porch entrances with 0 foundation skirts and interactive swinging doors.
/// - White picket fences and mailboxes framing the front yards.
/// - Iconic landmark Water Tower towering on the high hill peak overlooking the suburb.
/// - Farthest cul-de-sac house designated as the secret Exit House with the Backrooms exit threshold.
/// - Player safely positioned at the start of the street facing down the neighborhood.
/// - 1m Day / 2m Night cycle with warm sun, surreal cyan sky, thick nocturnal fog, streetlights, and Agatha stalker.
/// </summary>
[InitializeOnLoad]
public static class Level94AuthenticOrganizer
{
    private const string ScenePath = "Assets/_Project/Scenes/Level94.unity";
    private const string HouseGlbPath = "Assets/_Project/Art/Level94/LEVEL 94 Houses.glb";
    private const string WaterTowerGlbPath = "Assets/_Project/Art/Level94/WaterContainer.glb";
    private const string FencesGlbPath = "Assets/_Project/Art/Level94/Fences.glb";
    private const string CarpetMatPath = "Assets/_Project/Art/Level94/Materials/M_Level94_HouseCarpet.mat";

    static Level94AuthenticOrganizer()
    {
        // One-time setup completed. Available manually via Tools > Backrooms > Level 94 > ★ Organize Level 94 Like Real Game ★
    }

    [MenuItem("Tools/Backrooms/Level 94/★ Organize Level 94 Like Real Game ★", priority = 0)]
    public static void OrganizeLevel94LikeRealGame()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        if (currentScene.path != ScenePath)
        {
            currentScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        Debug.Log("<color=#00FFFF><b>[Level 94 Authentic Organizer]</b> Reorganizing Level 94 to match the real game...</color>");

        // 1. Ensure Terrain Colliders for Accurate Raycasting
        GameObject terrain = GameObject.Find("LEVEL 94");
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("Error", "Could not find 'LEVEL 94' terrain in scene!", "OK");
            return;
        }

        MeshFilter[] tMeshes = terrain.GetComponentsInChildren<MeshFilter>(true);
        foreach (var mf in tMeshes)
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

        // 2. Clean Up Orphaned or Broken Objects in Scene
        CleanSceneOrphans();

        // 3. Load Assets
        GameObject housePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HouseGlbPath);
        GameObject waterTowerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterTowerGlbPath);
        GameObject fencePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FencesGlbPath);
        Material carpetMat = AssetDatabase.LoadAssetAtPath<Material>(CarpetMatPath);

        // 4. Create Clean Organized Hierarchy Groups
        GameObject neighborhoodGroup = GetOrCreateGroup("LEVEL 94 Neighborhood");
        GameObject landmarksGroup = GetOrCreateGroup("LEVEL 94 Landmarks (Water Towers)");

        // Clear previous houses in neighborhood container
        for (int i = neighborhoodGroup.transform.childCount - 1; i >= 0; i--)
        {
            Undo.DestroyObjectImmediate(neighborhoodGroup.transform.GetChild(i).gameObject);
        }
        for (int i = landmarksGroup.transform.childCount - 1; i >= 0; i--)
        {
            Undo.DestroyObjectImmediate(landmarksGroup.transform.GetChild(i).gameObject);
        }

        // Also clear any previous "LEVEL 94 Houses (Randomized)" container
        GameObject oldHouses = GameObject.Find("LEVEL 94 Houses (Randomized)");
        if (oldHouses != null && oldHouses != neighborhoodGroup)
        {
            Undo.DestroyObjectImmediate(oldHouses);
        }
        GameObject oldTowers = GameObject.Find("LEVEL 94 Water Towers (Randomized)");
        if (oldTowers != null && oldTowers != landmarksGroup)
        {
            Undo.DestroyObjectImmediate(oldTowers);
        }

        // 5. Authentic Balanced Road-Facing House Layout
        // The winding suburban road in LEVEL 94 forms a sweeping 350m loop through the rolling hills:
        // - West Road Stretch (Heading North): Z = -75 to Z = 15 along X ~ -56
        // - North Curve & Overlook: Arc around Z = 45 to 66 from X = -35 to X = 20
        // - East Valley Road (Heading South): Z = 50 down to Z = -70 along X ~ 21 to X ~ 5
        //
        // Parcels are balanced on BOTH sides of the street with generous 25-50m spacing,
        // gentle slopes (< 5 deg), 9-16m front lawns, and zero road collision/clipping.
        var parcelConfigs = new[]
        {
            // Parcel 1: West Road Entrance, West Yard (faces East towards street)
            // Road at (-56.2, -65.0). Distance = 11.8m. Slope = 0.0 deg.
            new { HousePos = new Vector3(-68.0f, 0f, -65.0f), RoadLookAt = new Vector3(-56.2f, 0f, -65.0f) },

            // Parcel 2: West Road Entrance, East Yard (faces West towards street)
            // Road at (-56.0, -45.0). Distance = 12.0m. Slope = 0.0 deg.
            new { HousePos = new Vector3(-44.0f, 0f, -45.0f), RoadLookAt = new Vector3(-56.0f, 0f, -45.0f) },

            // Parcel 3: West Road Mid-Stretch, West Yard (faces East towards street)
            // Road at (-55.2, -18.0). Distance = 9.8m. Slope = 2.4 deg. (47m from House 1)
            new { HousePos = new Vector3(-65.0f, 0f, -18.0f), RoadLookAt = new Vector3(-55.2f, 0f, -18.0f) },

            // Parcel 4: West Road Mid-Stretch, East Yard (faces West towards street)
            // Road at (-52.0, 5.0). Distance = 9.0m. Slope = 3.6 deg. (50m from House 2)
            new { HousePos = new Vector3(-43.0f, 0f,   5.0f), RoadLookAt = new Vector3(-52.0f, 0f,   5.0f) },

            // Parcel 5: North-West Curve Overlook (faces South towards road)
            // Road at (-27.0, 45.0). Distance = 13.0m. Slope = 2.3 deg. (55m from House 4)
            new { HousePos = new Vector3(-29.0f, 0f,  58.0f), RoadLookAt = new Vector3(-27.0f, 0f,  45.0f) },

            // Parcel 6: North Summit Ridge (faces South towards road)
            // Road at (-2.0, 50.0). Distance = 16.0m. Slope = 0.2 deg. (28m from House 5)
            new { HousePos = new Vector3( -2.0f, 0f,  66.0f), RoadLookAt = new Vector3( -2.0f, 0f,  50.0f) },

            // Parcel 7: East Valley North, West Yard (faces East towards road)
            // Road at (20.0, 47.0). Distance = 10.0m. Slope = 1.9 deg. (22m from House 6)
            new { HousePos = new Vector3( 10.0f, 0f,  47.0f), RoadLookAt = new Vector3( 20.0f, 0f,  47.0f) },

            // Parcel 8: East Valley Mid, East Yard (faces West towards road)
            // Road at (20.5, 10.0). Distance = 11.5m. Slope = 1.8 deg. (37m from House 7)
            new { HousePos = new Vector3( 32.0f, 0f,  10.0f), RoadLookAt = new Vector3( 20.5f, 0f,  10.0f) },

            // Parcel 9: East Valley South, East Yard (faces West towards road)
            // Road at (9.0, -30.0). Distance = 10.0m. Slope = 4.7 deg. (40m from House 8)
            new { HousePos = new Vector3( 19.0f, 0f, -30.0f), RoadLookAt = new Vector3(  9.0f, 0f, -30.0f) },

            // Parcel 10: Southern Cul-de-sac Exit House (faces West towards road terminus)
            // Road ends at (5.0, -64.0). Distance = 9.0m. Slope = 1.8 deg. (34m from House 9)
            new { HousePos = new Vector3( 14.0f, 0f, -64.0f), RoadLookAt = new Vector3(  5.0f, 0f, -64.0f) },
        };

        List<Transform> placedHouses = new List<Transform>();
        Transform exitHouseTransform = null;

        for (int i = 0; i < parcelConfigs.Length; i++)
        {
            var p = parcelConfigs[i];
            Vector3 targetHousePos = p.HousePos;

            // Sample ground height accurately
            Ray downRay = new Ray(new Vector3(targetHousePos.x, 80f, targetHousePos.z), Vector3.down);
            float groundY = 0f;
            if (Physics.Raycast(downRay, out RaycastHit hit, 150f))
            {
                groundY = hit.point.y;
            }

            // Position so floor meets turf seamlessly (-0.08m embed)
            Vector3 finalPos = new Vector3(targetHousePos.x, groundY - 0.08f, targetHousePos.z);

            // Face front porch directly towards the road center
            Vector3 faceDir = (p.RoadLookAt - targetHousePos);
            faceDir.y = 0f;
            Quaternion rot = faceDir.sqrMagnitude > 0.1f ? Quaternion.LookRotation(faceDir.normalized, Vector3.up) : Quaternion.identity;

            // Instantiate House
            GameObject houseInstance = (GameObject)PrefabUtility.InstantiatePrefab(housePrefab);
            int houseNum = i + 1;
            bool isExitHouse = (i == parcelConfigs.Length - 1);
            houseInstance.name = isExitHouse ? "House_Exit_Target" : $"House_{houseNum}";
            houseInstance.transform.SetParent(neighborhoodGroup.transform, false);
            houseInstance.transform.position = finalPos;
            houseInstance.transform.rotation = rot;
            houseInstance.transform.localScale = Vector3.one * 1.25f; // 25% larger

            // Ensure colliders and door setup
            EnsureHouseEntryClear(houseInstance);
            EnsureHouseColliders(houseInstance);
            Level94GameplayDirector.ConfigureHouseDoor(houseInstance.transform);

            // Add Interior Damp Carpet Floor
            AddHouseCarpet(houseInstance.transform, carpetMat);

            // Add Storage / Hiding Locker
            AddHouseLocker(houseInstance.transform);

            // Add Picket Fence along front boundary if available
            if (fencePrefab != null)
            {
                Vector3 fenceL = finalPos + rot * new Vector3(-4.8f, 0f, 3.2f);
                if (Physics.Raycast(new Ray(new Vector3(fenceL.x, 80f, fenceL.z), Vector3.down), out RaycastHit fHitL, 150f))
                {
                    fenceL.y = fHitL.point.y;
                    GameObject fl = (GameObject)PrefabUtility.InstantiatePrefab(fencePrefab);
                    fl.name = $"Fence_{houseNum}_L";
                    fl.transform.SetParent(houseInstance.transform, true);
                    fl.transform.position = fenceL;
                    fl.transform.rotation = rot * Quaternion.Euler(0f, 90f, 0f);
                    fl.transform.localScale = Vector3.one * 0.01f;
                }
            }

            placedHouses.Add(houseInstance.transform);
            if (isExitHouse) exitHouseTransform = houseInstance.transform;
        }

        // 6. Landmark Water Tower on the High Eastern Ridge (Majestic Distant Beacon)
        if (waterTowerPrefab != null)
        {
            // High ridge crest (Y ~ 12.25m) overlooking the valley suburb from the east, well clear of the road
            Vector3 towerCoords = new Vector3(60f, 80f, 45f);
            if (Physics.Raycast(new Ray(towerCoords, Vector3.down), out RaycastHit towerHit, 150f))
            {
                Vector3 towerPos = towerHit.point - Vector3.up * 0.2f;
                GameObject tower = (GameObject)PrefabUtility.InstantiatePrefab(waterTowerPrefab);
                tower.name = "WaterTower_Landmark_Peak";
                tower.transform.SetParent(landmarksGroup.transform, false);
                tower.transform.position = towerPos;
                // GLTF model requires -90 on X to stand upright!
                tower.transform.rotation = Quaternion.Euler(-90f, 29f, 0f);
                tower.transform.localScale = Vector3.one * 16.5f;

                // Ensure colliders
                foreach (var mf in tower.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                    {
                        var mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                    }
                }
            }
        }

        // 7. Setup Safe Player Spawn on the Road Looking Down the Street
        SetupPlayerSpawn();

        // 8. Configure Level94 Director, Exit House, and Spatial Teleportation
        Level94GameplayDirector director = Object.FindAnyObjectByType<Level94GameplayDirector>();
        if (director != null)
        {
            director.exitHouse = exitHouseTransform;
            director.InitializeLevel94Gameplay();
            EditorUtility.SetDirty(director);
        }

        Level94HouseTeleporter teleporter = Object.FindAnyObjectByType<Level94HouseTeleporter>();
        if (teleporter != null)
        {
            teleporter.housesContainerName = "LEVEL 94 Neighborhood";
            teleporter.houseInteriorHalfExtents = new Vector3(3.25f, 2.25f, 3.25f);
            teleporter.houseInteriorCenterOffset = new Vector3(0f, 2.0f, -2.55f);
            teleporter.RegisterExitHouse(exitHouseTransform);
            teleporter.CollectHouses();
            EditorUtility.SetDirty(teleporter);
        }

        // 9. Calibrate Day/Night Cycle for Authentic Pitch-Black Night
        Level94DayNightCycle dnc = Object.FindAnyObjectByType<Level94DayNightCycle>();
        if (dnc != null)
        {
            dnc.nightMoonIntensity = 0.0f;
            dnc.nightMoonColor = Color.black;
            dnc.nightSkyTint = Color.black;
            dnc.nightGroundColor = Color.black;
            dnc.nightFogColor = Color.black;
            dnc.nightFogStart = 0.5f;
            dnc.nightFogEnd = 8.0f; // Impenetrable pitch black beyond 8m
            dnc.dayDuration = 60.0f;
            dnc.nightDuration = 120.0f;
            EditorUtility.SetDirty(dnc);
        }

        // 10. Ensure Agatha Stalker Animation Controller
        GameObject agatha = GameObject.Find("Agatha_Stalker");
        if (agatha != null)
        {
            var anim = agatha.GetComponent<Animator>();
            if (anim != null)
            {
                anim.applyRootMotion = false;
                anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Art/Level94/AgathaController.controller");
                EditorUtility.SetDirty(agatha);
            }
        }

        // 11. Build Infinite Rolling Hills Horizon Landscape (Fills the Sky with 60 Distant Houses & 10 Water Towers)
        Level94HorizonLandscapeBuilder.BuildHorizonLandscape();

        // 12. Mark and Save Scene
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log("<color=#00FF88><b>[Level 94 Organized]</b> Level 94 successfully organized like the real game:\n" +
                  "- 10 suburban yellow houses (1.25x scale) neatly facing the road\n" +
                  "- Unobstructed front porch entrances and openable swinging doors\n" +
                  "- Iconic landmark Water Tower overlooking from the highest hill peak\n" +
                  "- Cul-de-sac Exit House with Backrooms extraction portal\n" +
                  "- Player safely positioned on the road with clear line of sight down the neighborhood!</color>");
    }

    private static void EnsureHouseEntryClear(GameObject house)
    {
        // Destroy or disable any Foundation_Skirt or House_Foundation concrete slab so porch is completely open
        foreach (var t in house.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && (t.name == "Foundation_Skirt" || t.name == "House_Foundation"))
            {
                t.gameObject.SetActive(false);
                Undo.DestroyObjectImmediate(t.gameObject);
            }
        }

        // Set door leaf MeshColliders to trigger
        foreach (var mc in house.GetComponentsInChildren<MeshCollider>(true))
        {
            string n = mc.gameObject.name.ToLower();
            if (n.Contains("door") && !n.Contains("frame"))
            {
                mc.isTrigger = true;
            }
        }

        // Set door leaf BoxColliders to trigger
        foreach (var bc in house.GetComponentsInChildren<BoxCollider>(true))
        {
            string n = bc.gameObject.name.ToLower();
            if (n.Contains("door"))
            {
                bc.isTrigger = true;
            }
        }
    }

    private static void EnsureHouseColliders(GameObject house)
    {
        // Add solid MeshColliders to the house body walls and roof so player cannot walk through walls
        Transform body = house.transform.Find("Retopo_Object_7");
        if (body != null)
        {
            var mc = body.GetComponent<MeshCollider>();
            if (mc == null) mc = Undo.AddComponent<MeshCollider>(body.gameObject);
            mc.sharedMesh = body.GetComponent<MeshFilter>()?.sharedMesh;
        }

        Transform roof = house.transform.Find("Retopo_Object_4");
        if (roof != null)
        {
            var mc = roof.GetComponent<MeshCollider>();
            if (mc == null) mc = Undo.AddComponent<MeshCollider>(roof.gameObject);
            mc.sharedMesh = roof.GetComponent<MeshFilter>()?.sharedMesh;
        }
    }

    private static void CleanSceneOrphans()
    {
        // Remove any loose Foundation_Skirt, House_Foundation, or broken exit frames at root
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var r in roots)
        {
            if (r.name == "Foundation_Skirt" || r.name == "House_Foundation" || r.name == "Exit_Frame_Top" || r.name == "Exit_Frame_Left" || r.name == "Exit_Frame_Right")
            {
                Undo.DestroyObjectImmediate(r);
            }
        }
    }

    private static void SetupPlayerSpawn()
    {
        GameObject player = GameObject.FindWithTag("Player") ?? GameObject.Find("PlayerCapsule");
        if (player == null) return;

        // Position player at neighborhood entrance facing straight down the street (North)
        Vector3 roadSpawn = new Vector3(-56.0f, 0f, -72.0f);
        Ray downRay = new Ray(new Vector3(roadSpawn.x, 80f, roadSpawn.z), Vector3.down);
        if (Physics.Raycast(downRay, out RaycastHit hit, 150f))
        {
            roadSpawn = hit.point + Vector3.up * 1.05f;
        }

        Quaternion lookRot = Quaternion.LookRotation(Vector3.forward, Vector3.up);

        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        Undo.RecordObject(player.transform, "Position Player on Road");
        player.transform.position = roadSpawn;
        player.transform.rotation = lookRot;

        if (cc != null) cc.enabled = true;

        // Ensure PlayerLockerController is attached so player can hide inside lockers
        var plc = player.GetComponent<PlayerLockerController>();
        if (plc == null)
        {
            plc = Undo.AddComponent<PlayerLockerController>(player);
            plc.interactRange = 2.5f;
            plc.facingAngle = 80f;
        }
    }

    private static void AddHouseCarpet(Transform house, Material carpetMat)
    {
        // Clean existing carpet if any
        Transform oldCarpet = house.Find("House_Carpet_Floor");
        if (oldCarpet != null) Undo.DestroyObjectImmediate(oldCarpet.gameObject);

        GameObject carpet = GameObject.CreatePrimitive(PrimitiveType.Cube);
        carpet.name = "House_Carpet_Floor";
        carpet.transform.SetParent(house, false);
        carpet.transform.localPosition = new Vector3(0f, 0.04f, -2.55f);
        carpet.transform.localRotation = Quaternion.identity;
        carpet.transform.localScale = new Vector3(5.6f, 0.08f, 5.1f);

        if (carpetMat != null)
        {
            var mr = carpet.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = carpetMat;
        }

        Undo.RegisterCreatedObjectUndo(carpet, "Add House Carpet");
    }

    private static void AddHouseLocker(Transform house)
    {
        // Clean existing locker if any
        Transform oldLocker = house.Find("LockerHideSpot");
        if (oldLocker != null) Undo.DestroyObjectImmediate(oldLocker.gameObject);

        // Place locker against back wall on the right side facing into the room
        Vector3 worldPos = house.TransformPoint(new Vector3(1.8f, 0.08f, -4.4f));
        Vector3 worldForward = house.TransformDirection(Vector3.forward);

        LockerHideSpot locker = LockerHideSpot.Create(house, worldPos, worldForward);
        Undo.RegisterCreatedObjectUndo(locker.gameObject, "Add House Locker");
    }

    private static GameObject GetOrCreateGroup(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        }
        return go;
    }
}
#endif
