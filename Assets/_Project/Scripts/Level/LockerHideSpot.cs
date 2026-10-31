using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// A tall Backrooms storage locker / wardrobe cabinet where the player can hide from the monster.
/// Features a dark sheet-metal body and a hinged front door with ventilation louvers
/// that allow the player to peer out into the room while remaining undetected.
/// </summary>
public class LockerHideSpot : MonoBehaviour
{
    public static readonly List<LockerHideSpot> All = new List<LockerHideSpot>();

    // ---------------- Dimensions (metres) ----------------
    public const float LockerWidth = 0.86f;
    public const float LockerHeight = 2.22f;
    public const float LockerDepth = 0.82f;
    public const float DoorOpenAngle = 95.0f;

    public const float EyeHeightStanding = 1.62f;

    private Transform doorPivot;
    private BoxCollider doorCollider;
    private AudioSource audioSource;

    /// <summary>World position in front of the locker where the player stands before stepping in.</summary>
    public Vector3 FrontPoint => transform.TransformPoint(new Vector3(0f, 0f, 0.90f));

    /// <summary>World position inside the locker where the player stands hidden.</summary>
    public Vector3 HidePoint => transform.TransformPoint(new Vector3(0f, 0f, 0.08f));

    /// <summary>World position the villain stares at while investigating the locker.</summary>
    public Vector3 DoorPoint => transform.TransformPoint(new Vector3(0f, 1.2f, 0.40f));

    /// <summary>Unit vector facing out into the room.</summary>
    public Vector3 ForwardDir
    {
        get
        {
            Vector3 f = transform.forward;
            f.y = 0f;
            return f.normalized;
        }
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    // =========================================================================
    // Construction
    // =========================================================================

    public static LockerHideSpot Create(Transform parent, Vector3 floorPos, Vector3 forwardDir,
                                       Material exteriorOverride = null, Material interiorOverride = null)
    {
        GameObject go = new GameObject("LockerHideSpot");
        go.transform.SetParent(parent, false);
        go.transform.position = floorPos;
        go.transform.rotation = Quaternion.LookRotation(forwardDir, Vector3.up);

        LockerHideSpot spot = go.AddComponent<LockerHideSpot>();
        spot.Build(exteriorOverride != null ? exteriorOverride : DoorMaterials.LockerMetal,
                   interiorOverride != null ? interiorOverride : DoorMaterials.LockerInterior);

        // Keep NavMesh clear around the locker
        NavMeshModifier mod = go.AddComponent<NavMeshModifier>();
        mod.ignoreFromBuild = true;

        return spot;
    }

    private void Build(Material exteriorMat, Material interiorMat)
    {
        float w = LockerWidth;
        float h = LockerHeight;
        float d = LockerDepth;
        const float wallT = 0.035f;

        float halfW = w * 0.5f;
        float halfH = h * 0.5f;
        float centerZ = 0f; // locker center is at 0, back is at -d/2, front door is at +d/2

        // ---------- 1. Static Locker Cabinet Body ----------
        List<CombineInstance> bodyParts = new List<CombineInstance>();

        // Floor base
        AddBox(bodyParts, new Vector3(0f, wallT * 0.5f, centerZ), new Vector3(w, wallT, d));
        // Roof top
        AddBox(bodyParts, new Vector3(0f, h - wallT * 0.5f, centerZ), new Vector3(w, wallT, d));
        // Back wall
        AddBox(bodyParts, new Vector3(0f, halfH, -d * 0.5f + wallT * 0.5f), new Vector3(w, h, wallT));
        // Left side wall
        AddBox(bodyParts, new Vector3(-halfW + wallT * 0.5f, halfH, centerZ), new Vector3(wallT, h, d));
        // Right side wall
        AddBox(bodyParts, new Vector3(halfW - wallT * 0.5f, halfH, centerZ), new Vector3(wallT, h, d));
        // Interior high shelf
        AddBox(bodyParts, new Vector3(0f, 1.85f, centerZ - 0.05f), new Vector3(w - wallT * 2f, wallT, d - 0.1f));

        MakeMeshObject("LockerBody", transform, bodyParts, exteriorMat, true);

        // ---------- 2. Hinged Locker Front Door ----------
        GameObject pivotGo = new GameObject("LockerDoorPivot");
        doorPivot = pivotGo.transform;
        doorPivot.SetParent(transform, false);
        // Hinge on left front edge
        doorPivot.localPosition = new Vector3(-halfW + 0.02f, 0f, d * 0.5f - 0.015f);

        List<CombineInstance> doorParts = new List<CombineInstance>();
        float dw = w - 0.04f;
        float dh = h - 0.04f;
        const float dt = 0.024f;

        // Outer door perimeter rails
        const float rail = 0.065f;
        float dHalfH = dh * 0.5f;

        // Top rail
        AddBox(doorParts, new Vector3(dw * 0.5f, dh - rail * 0.5f, 0f), new Vector3(dw, rail, dt));
        // Bottom rail
        AddBox(doorParts, new Vector3(dw * 0.5f, rail * 0.5f, 0f), new Vector3(dw, rail, dt));
        // Left rail (near hinge)
        AddBox(doorParts, new Vector3(rail * 0.5f, dHalfH, 0f), new Vector3(rail, dh, dt));
        // Right rail (latch side)
        AddBox(doorParts, new Vector3(dw - rail * 0.5f, dHalfH, 0f), new Vector3(rail, dh, dt));

        // Lower solid panel (from rail up to 1.25m)
        float lowerPanelH = 1.25f - rail;
        AddBox(doorParts, new Vector3(dw * 0.5f, rail + lowerPanelH * 0.5f, 0f), new Vector3(dw - rail * 2f, lowerPanelH, dt * 0.7f));

        // Upper section: see-through ventilation louvers / slits at eye height (1.30m to 1.80m)
        const int louverCount = 8;
        float louverAreaH = 0.50f;
        float louverAreaY = 1.30f;
        float louverSpacing = louverAreaH / louverCount;

        for (int i = 0; i < louverCount; i++)
        {
            float ly = louverAreaY + (i + 0.5f) * louverSpacing;
            // Angled thin metal slat with open gap between them so player can see through
            AddBox(doorParts, new Vector3(dw * 0.5f, ly, 0f), new Vector3(dw - rail * 2f, 0.022f, dt * 0.8f));
        }

        // Metal chrome handle on right latch side
        AddBox(doorParts, new Vector3(dw - 0.04f, 1.05f, 0.025f), new Vector3(0.025f, 0.15f, 0.035f));

        GameObject doorMeshObj = MakeMeshObject("LockerDoorMesh", doorPivot, doorParts, exteriorMat, false);

        doorCollider = doorMeshObj.AddComponent<BoxCollider>();
        doorCollider.center = new Vector3(dw * 0.5f, dHalfH, 0f);
        doorCollider.size = new Vector3(dw, dh, dt + 0.02f);

        NavMeshModifier doorNavMod = doorMeshObj.AddComponent<NavMeshModifier>();
        doorNavMod.ignoreFromBuild = true;

        // ---------- 3. 3D Audio ----------
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0.85f;
        audioSource.minDistance = 1.5f;
        audioSource.maxDistance = 16.0f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;

        SetDoorAngle(0f);
    }

    public void SetDoorAngle(float degrees)
    {
        if (doorPivot != null)
        {
            doorPivot.localRotation = Quaternion.Euler(0f, -degrees, 0f);
        }
    }

    public void PlayCreak()
    {
        Play(DoorAudio.LockerOpen, 0.85f);
    }

    public void PlayThunk()
    {
        Play(DoorAudio.LockerClose, 0.90f);
    }

    private void Play(AudioClip clip, float volume)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.pitch = Random.Range(0.96f, 1.04f);
            audioSource.PlayOneShot(clip, volume * GameSettings.MasterVolume);
        }
    }

    public static LockerHideSpot FindNearest(Vector3 position, float maxDistance)
    {
        LockerHideSpot best = null;
        float bestSqr = maxDistance * maxDistance;
        for (int i = 0; i < All.Count; i++)
        {
            LockerHideSpot locker = All[i];
            if (locker == null) continue;
            Vector3 diff = locker.FrontPoint - position;
            diff.y = 0f;
            float sqr = diff.sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = locker;
            }
        }
        return best;
    }

    private static void AddBox(List<CombineInstance> list, Vector3 center, Vector3 size)
    {
        CombineInstance ci = new CombineInstance
        {
            mesh = CubeMesh,
            transform = Matrix4x4.TRS(center, Quaternion.identity, size)
        };
        list.Add(ci);
    }

    private GameObject MakeMeshObject(string objName, Transform parent, List<CombineInstance> parts, Material mat, bool addColliders)
    {
        GameObject g = new GameObject(objName);
        g.transform.SetParent(parent, false);

        Mesh m = new Mesh { name = objName };
        m.CombineMeshes(parts.ToArray(), true, true);
        m.RecalculateBounds();

        g.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        if (addColliders)
        {
            // Box colliders on left, right, and back
            BoxCollider leftCol = g.AddComponent<BoxCollider>();
            leftCol.center = new Vector3(-LockerWidth * 0.5f + 0.02f, LockerHeight * 0.5f, 0f);
            leftCol.size = new Vector3(0.04f, LockerHeight, LockerDepth);

            BoxCollider rightCol = g.AddComponent<BoxCollider>();
            rightCol.center = new Vector3(LockerWidth * 0.5f - 0.02f, LockerHeight * 0.5f, 0f);
            rightCol.size = new Vector3(0.04f, LockerHeight, LockerDepth);

            BoxCollider backCol = g.AddComponent<BoxCollider>();
            backCol.center = new Vector3(0f, LockerHeight * 0.5f, -LockerDepth * 0.5f + 0.02f);
            backCol.size = new Vector3(LockerWidth, LockerHeight, 0.04f);
        }

        return g;
    }

    private static Mesh cubeMesh;
    private static Mesh CubeMesh
    {
        get
        {
            if (cubeMesh == null)
            {
                GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cubeMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
            }
            return cubeMesh;
        }
    }
}
