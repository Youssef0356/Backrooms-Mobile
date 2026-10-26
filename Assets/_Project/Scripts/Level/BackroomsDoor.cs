using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// An interactive Backrooms room door that can be opened by the player,
/// stays open for a few seconds, then automatically closes.
/// Monsters (VillainAI) can also open and enter rooms freely.
/// Also responds to paranormal map-wide door events (rattling and violent slamming).
/// </summary>
public class BackroomsDoor : MonoBehaviour
{
    public static readonly List<BackroomsDoor> AllDoors = new List<BackroomsDoor>();

    public enum DoorState { Closed, Opening, Open, Closing }

    [Header("Door Settings")]
    [Tooltip("Target swing angle when opened (degrees).")]
    public float openAngle = 90f;
    [Tooltip("Duration in seconds for standard open swing.")]
    public float openDuration = 0.55f;
    [Tooltip("Duration in seconds for standard close swing.")]
    public float closeDuration = 0.65f;
    [Tooltip("How many seconds the door stays open before automatically closing.")]
    public float autoCloseDelay = 4.5f;

    [Header("Monster Interaction")]
    [Tooltip("Radius within which a monster (VillainAI) triggers the door to swing open automatically.")]
    public float monsterDetectRadius = 3.5f;

    [Header("Audio")]
    [Range(0f, 1f)] public float soundVolume = 0.85f;

    public DoorState State { get; private set; } = DoorState.Closed;
    public bool IsOpen => State == DoorState.Open || State == DoorState.Opening;
    public bool IsMoving => State == DoorState.Opening || State == DoorState.Closing;

    private Transform hingePivot;
    private BoxCollider doorLeafCollider;
    private AudioSource audioSource;

    private float autoCloseTimer;
    private Coroutine motionRoutine;
    private Coroutine rattleRoutine;
    private float currentAngle = 0f;

    private VillainAI cachedVillain;

    private void OnEnable()
    {
        if (!AllDoors.Contains(this)) AllDoors.Add(this);
    }

    private void OnDisable()
    {
        AllDoors.Remove(this);
    }

    private void Start()
    {
        cachedVillain = VillainAI.Instance;
    }

    private void Update()
    {
        // 1. Monster Auto-Open Sensing: Ensure monsters can enter rooms seamlessly
        CheckMonsterProximity();

        // 2. Auto-Close Countdown: Closes automatically after a few seconds
        if (State == DoorState.Open)
        {
            // Do not close while the monster is standing directly in the doorway
            if (!IsMonsterInThreshold())
            {
                autoCloseTimer -= Time.deltaTime;
                if (autoCloseTimer <= 0f)
                {
                    Close();
                }
            }
            else
            {
                // Refresh timer while monster traverses the doorway
                autoCloseTimer = Mathf.Max(autoCloseTimer, 2.0f);
            }
        }
    }

    private void CheckMonsterProximity()
    {
        if (cachedVillain == null)
        {
            cachedVillain = VillainAI.Instance != null ? VillainAI.Instance : FindFirstObjectByType<VillainAI>();
        }
        if (cachedVillain == null || !cachedVillain.isActiveAndEnabled) return;

        float sqrDist = (cachedVillain.transform.position - transform.position).sqrMagnitude;
        if (sqrDist <= monsterDetectRadius * monsterDetectRadius)
        {
            if (State == DoorState.Closed || State == DoorState.Closing)
            {
                // Monster pushes door open to enter room!
                Open(autoClose: true, stayOpenTime: 5.0f);
            }
            else if (State == DoorState.Open)
            {
                autoCloseTimer = Mathf.Max(autoCloseTimer, 3.0f);
            }
        }
    }

    private bool IsMonsterInThreshold()
    {
        if (cachedVillain == null) return false;
        float sqrDist = (cachedVillain.transform.position - transform.position).sqrMagnitude;
        return sqrDist <= (monsterDetectRadius * 0.85f) * (monsterDetectRadius * 0.85f);
    }

    // =========================================================================
    // Player Interaction
    // =========================================================================

    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open(autoClose: true, stayOpenTime: autoCloseDelay);
    }

    public void Open(bool autoClose = true, float stayOpenTime = 4.5f)
    {
        if (State == DoorState.Open || State == DoorState.Opening)
        {
            autoCloseTimer = stayOpenTime;
            return;
        }

        if (rattleRoutine != null) { StopCoroutine(rattleRoutine); rattleRoutine = null; }
        if (motionRoutine != null) StopCoroutine(motionRoutine);

        autoCloseTimer = autoClose ? stayOpenTime : float.MaxValue;
        motionRoutine = StartCoroutine(AnimateDoor(openAngle, openDuration, DoorState.Opening, DoorState.Open, isOpening: true));
    }

    public void Close()
    {
        if (State == DoorState.Closed || State == DoorState.Closing) return;

        if (rattleRoutine != null) { StopCoroutine(rattleRoutine); rattleRoutine = null; }
        if (motionRoutine != null) StopCoroutine(motionRoutine);

        motionRoutine = StartCoroutine(AnimateDoor(0f, closeDuration, DoorState.Closing, DoorState.Closed, isOpening: false));
    }

    private IEnumerator AnimateDoor(float targetAngle, float duration, DoorState inProgressState, DoorState finalState, bool isOpening)
    {
        State = inProgressState;

        if (isOpening)
        {
            PlayAudio(DoorAudio.DoorOpen, 0.95f * soundVolume);
        }

        float startAngle = currentAngle;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Natural organic swing curve: accelerating initial push, damped arrival
            float curve = isOpening ? EaseOutCubic(t) : EaseInQuad(t);
            currentAngle = Mathf.Lerp(startAngle, targetAngle, curve);
            SetHingeRotation(currentAngle);

            yield return null;
        }

        currentAngle = targetAngle;
        SetHingeRotation(currentAngle);
        State = finalState;

        if (!isOpening)
        {
            PlayAudio(DoorAudio.DoorClose, 1.0f * soundVolume);
        }
    }

    // =========================================================================
    // Paranormal Map Events
    // =========================================================================

    public void StartRattle(float duration)
    {
        if (State != DoorState.Closed) return;
        if (rattleRoutine != null) StopCoroutine(rattleRoutine);
        rattleRoutine = StartCoroutine(RattleRoutine(duration));
    }

    private IEnumerator RattleRoutine(float duration)
    {
        PlayAudio(DoorAudio.DoorRattle, 0.9f * soundVolume);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float offset = Mathf.Sin(elapsed * 45f) * Random.Range(1.5f, 3.5f);
            SetHingeRotation(offset);
            yield return null;
        }

        SetHingeRotation(0f);
        currentAngle = 0f;
        rattleRoutine = null;
    }

    public void SlamOpen()
    {
        if (rattleRoutine != null) { StopCoroutine(rattleRoutine); rattleRoutine = null; }
        if (motionRoutine != null) StopCoroutine(motionRoutine);
        motionRoutine = StartCoroutine(SlamRoutine(openAngle, 0.22f, DoorState.Open, DoorAudio.DoorSlam));
    }

    public void SlamShut()
    {
        if (rattleRoutine != null) { StopCoroutine(rattleRoutine); rattleRoutine = null; }
        if (motionRoutine != null) StopCoroutine(motionRoutine);
        motionRoutine = StartCoroutine(SlamRoutine(0f, 0.20f, DoorState.Closed, DoorAudio.DoorSlam));
    }

    private IEnumerator SlamRoutine(float targetAngle, float duration, DoorState finalState, AudioClip sfx)
    {
        State = (finalState == DoorState.Open) ? DoorState.Opening : DoorState.Closing;
        float startAngle = currentAngle;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            currentAngle = Mathf.Lerp(startAngle, targetAngle, t * t);
            SetHingeRotation(currentAngle);
            yield return null;
        }

        currentAngle = targetAngle;
        SetHingeRotation(currentAngle);
        State = finalState;
        PlayAudio(sfx, 1.15f * soundVolume);

        // Heavy door slam shakes nearby space and alerts monster
        PlayerNoiseEmitter.EmitNoise(transform.position, 14f);
    }

    private void SetHingeRotation(float angle)
    {
        if (hingePivot != null)
        {
            hingePivot.localRotation = Quaternion.Euler(0f, angle, 0f);
        }
    }

    private void PlayAudio(AudioClip clip, float volume)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.pitch = Random.Range(0.96f, 1.04f);
            audioSource.PlayOneShot(clip, volume * GameSettings.MasterVolume);
        }
    }

    private static float EaseOutCubic(float t)
    {
        float f = t - 1f;
        return f * f * f + 1f;
    }

    private static float EaseInQuad(float t)
    {
        return t * t;
    }

    // =========================================================================
    // Procedural Construction
    // =========================================================================

    public static BackroomsDoor Create(Transform parent, Vector3 doorwayThresholdPos, Vector3 forwardDir,
                                      float width = 1.95f, float height = 2.85f,
                                      Material woodMat = null, Material frameMat = null, Material handleMat = null)
    {
        GameObject root = new GameObject("BackroomsDoor");
        root.transform.SetParent(parent, false);
        root.transform.position = doorwayThresholdPos;
        root.transform.rotation = Quaternion.LookRotation(forwardDir, Vector3.up);

        BackroomsDoor door = root.AddComponent<BackroomsDoor>();
        door.Build(width, height,
                   woodMat != null ? woodMat : DoorMaterials.DoorWood,
                   frameMat != null ? frameMat : DoorMaterials.DoorFrame,
                   handleMat != null ? handleMat : DoorMaterials.DoorHandle);

        return door;
    }

    private void Build(float doorwayW, float doorwayH, Material woodMat, Material frameMat, Material handleMat)
    {
        float halfW = doorwayW * 0.5f;
        const float postW = 0.08f;
        const float postD = 0.28f;
        const float lintelH = 0.08f;

        // 1. Static Door Frame (Left post, Right post, Top lintel)
        GameObject frameGo = new GameObject("DoorFrame");
        frameGo.transform.SetParent(transform, false);

        List<CombineInstance> frameParts = new List<CombineInstance>();
        // Left jamb post
        AddBox(frameParts, new Vector3(-halfW - postW * 0.5f, doorwayH * 0.5f, 0f), new Vector3(postW, doorwayH, postD));
        // Right jamb post
        AddBox(frameParts, new Vector3(halfW + postW * 0.5f, doorwayH * 0.5f, 0f), new Vector3(postW, doorwayH, postD));
        // Top lintel
        AddBox(frameParts, new Vector3(0f, doorwayH + lintelH * 0.5f, 0f), new Vector3(doorwayW + postW * 2f, lintelH, postD));

        CreateCombinedMesh(frameGo, "DoorFrameMesh", frameParts, frameMat, false);

        // Frame must never block or carve the baked NavMesh
        NavMeshModifier frameMod = frameGo.AddComponent<NavMeshModifier>();
        frameMod.ignoreFromBuild = true;

        // 2. Hinge Pivot (placed at left edge of the door opening)
        GameObject pivotGo = new GameObject("HingePivot");
        pivotGo.transform.SetParent(transform, false);
        pivotGo.transform.localPosition = new Vector3(-halfW + 0.015f, 0f, 0f);
        hingePivot = pivotGo.transform;

        // 3. Rotating Door Leaf (child of pivot)
        GameObject leafGo = new GameObject("DoorLeaf");
        leafGo.transform.SetParent(hingePivot, false);

        float leafW = doorwayW - 0.03f;
        float leafH = doorwayH - 0.03f;
        const float leafD = 0.055f;

        // Position door leaf center so its left edge is at the hinge pivot
        leafGo.transform.localPosition = new Vector3(leafW * 0.5f, leafH * 0.5f + 0.015f, 0f);

        // Wood door leaf mesh
        GameObject leafMeshGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leafMeshGo.name = "DoorLeafMesh";
        leafMeshGo.transform.SetParent(leafGo.transform, false);
        leafMeshGo.transform.localPosition = Vector3.zero;
        leafMeshGo.transform.localScale = new Vector3(leafW, leafH, leafD);

        MeshRenderer leafMr = leafMeshGo.GetComponent<MeshRenderer>();
        leafMr.sharedMaterial = woodMat;
        leafMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        leafMr.receiveShadows = false;

        // BoxCollider on the door leaf
        doorLeafCollider = leafMeshGo.GetComponent<BoxCollider>();
        doorLeafCollider.isTrigger = false;

        // Ignore door leaf from NavMesh bake so NavMesh flows smoothly through the doorway!
        NavMeshModifier navMod = leafMeshGo.AddComponent<NavMeshModifier>();
        navMod.ignoreFromBuild = true;

        // 4. Brass Lever Door Handles (Front and Back)
        BuildDoorHandle(leafGo.transform, leafW * 0.5f - 0.09f, 1.02f - (leafH * 0.5f), leafD * 0.5f, handleMat);

        // 5. 3D Spatial Audio Source
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0.95f;
        audioSource.minDistance = 2.0f;
        audioSource.maxDistance = 18.0f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
    }

    private void BuildDoorHandle(Transform leafParent, float xPos, float yPos, float halfD, Material handleMat)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject handleGo = new GameObject($"Handle_{(side == 1 ? "Front" : "Back")}");
            handleGo.transform.SetParent(leafParent, false);
            handleGo.transform.localPosition = new Vector3(xPos, yPos, side * (halfD + 0.015f));

            // Small escutcheon plate
            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(plate.GetComponent<Collider>());
            plate.transform.SetParent(handleGo.transform, false);
            plate.transform.localScale = new Vector3(0.045f, 0.12f, 0.012f);
            plate.GetComponent<MeshRenderer>().sharedMaterial = handleMat;

            // Lever bar
            GameObject lever = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(lever.GetComponent<Collider>());
            lever.transform.SetParent(handleGo.transform, false);
            lever.transform.localPosition = new Vector3(-0.045f, 0.015f, side * 0.025f);
            lever.transform.localScale = new Vector3(0.11f, 0.02f, 0.018f);
            lever.GetComponent<MeshRenderer>().sharedMaterial = handleMat;
        }
    }

    private static void AddBox(List<CombineInstance> list, Vector3 center, Vector3 size)
    {
        CombineInstance ci = new CombineInstance
        {
            mesh = PrimitiveHelper.CubeMesh,
            transform = Matrix4x4.TRS(center, Quaternion.identity, size)
        };
        list.Add(ci);
    }

    private static void CreateCombinedMesh(GameObject parentGo, string meshName, List<CombineInstance> parts, Material mat, bool addCollider)
    {
        Mesh m = new Mesh { name = meshName };
        m.CombineMeshes(parts.ToArray(), true, true);
        m.RecalculateBounds();

        MeshFilter mf = parentGo.AddComponent<MeshFilter>();
        mf.sharedMesh = m;

        MeshRenderer mr = parentGo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        if (addCollider)
        {
            MeshCollider mc = parentGo.AddComponent<MeshCollider>();
            mc.sharedMesh = m;
        }
    }

    private static class PrimitiveHelper
    {
        private static Mesh cubeMesh;
        public static Mesh CubeMesh
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
}
