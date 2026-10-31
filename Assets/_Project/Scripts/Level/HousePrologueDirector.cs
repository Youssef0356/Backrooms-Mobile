using System;
using System.Collections;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Cinematic House Prologue for Backrooms Level 0.
/// 
/// Sequence of Events:
/// 1. Player starts at night inside their dark, quiet suburban house bedroom/living room.
/// 2. In front of the sofa, an old analog TV is on, buzzing with eerie static and flickering cyan light.
/// 3. Narrative HUD prompt appears: "[E] WATCH TV / TURN OFF" or "[TAP] TURN OFF TV".
/// 4. Player approaches and interacts to switch off the TV.
/// 5. The screen snaps black with a mechanical pop, room plunges into pitch darkness.
/// 6. Prompt updates: "GO TO BED... YOU NEED REST."
/// 7. Player interacts with the bed.
/// 8. Player lays down: vision blurs and fades to black; deep breathing and silence.
/// 9. SUDDEN NOCLIP TRANSITION: A deep subsonic distortion glitch tears through reality,
///    screen shakes violently, tape tracking statics burst, and the floor drops out.
/// 10. Player wakes up face-down on the damp, buzzing yellow carpet of Level 0 as the Backrooms level generator kicks in!
/// </summary>
public class HousePrologueDirector : MonoBehaviour
{
    public static HousePrologueDirector Instance { get; private set; }

    [Header("House World Offset")]
    [Tooltip("Location high above the backrooms where the house set is built.")]
    public Vector3 houseRootOffset = new Vector3(0f, 65f, 0f);

    [Header("Manual Scene Design (Optional)")]
    [Tooltip("If assigned, procedural box-building is skipped and your visual scene design is used! Leave empty to auto-detect 'House_Prologue_Set' in scene.")]
    public GameObject manualHouseRoot;
    [Tooltip("The TV object to interact with (turns off when approached).")]
    public GameObject manualTV;
    [Tooltip("The Bed object to interact with to go to sleep.")]
    public Transform manualBed;
    [Tooltip("Where the player starts seated on the couch.")]
    public Transform manualCouchStartPoint;
    [Tooltip("Where the player stands up to.")]
    public Transform manualStandingPoint;

    private bool isManualSet = false;

    [Header("State")]
    public bool isPrologueActive = true;
    public bool isSittingOnCouch = false;
    private bool isTransitioningStandUp = false;
    private Vector3 sittingPos;
    private Vector3 standingPos;
    public bool tvIsOn = true;
    public bool hasWatchedTV = false;
    public bool isSleeping = false;

    // References created procedurally
    private GameObject houseRoot;
    private GameObject tvObject;
    private Light tvLight;
    private AudioSource tvAudio;
    private Material tvScreenMat;
    private Renderer tvScreenRenderer;

    private Transform bedTransform;
    private Transform sofaTransform;

    // Player references
    private Transform playerTransform;
    private CharacterController playerCc;
    private FirstPersonController playerFpc;
    private Flashlight playerFlashlight;

    // Interaction UI (UI Toolkit)
    private UIDocument uiDoc;
    private VisualElement uiRoot;
    private VisualElement promptCard;
    private Label promptKeyLabel;
    private Label promptActionLabel;
    private VisualElement fadeOverlay;

    // Transition Audio
    private AudioSource DirectorAudio;
    private AudioClip tvStaticClip;
    private AudioClip tvClickOffClip;
    private AudioClip noclipGlitchClip;

    // Action Delegate for when player lands in the Backrooms
    public event Action OnBackroomsAwakened;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        DirectorAudio = gameObject.AddComponent<AudioSource>();
        DirectorAudio.spatialBlend = 0f; // 2D full stereo for cutscene impacts

        tvStaticClip = Resources.Load<AudioClip>("SFX/TV_Static");
        tvClickOffClip = Resources.Load<AudioClip>("SFX/TV_Click_Off");
        noclipGlitchClip = Resources.Load<AudioClip>("SFX/Noclip_Fall");
    }

    private void OnEnable()
    {
        VentHUD.OnActionPressed += HandleHudActionPressed;
    }

    private void OnDisable()
    {
        VentHUD.OnActionPressed -= HandleHudActionPressed;
    }

    private void HandleHudActionPressed()
    {
        cardTapped = true;
        cardTappedFrame = Time.frameCount;
    }

    public void BeginPrologue(Transform player, Action onBackroomsReady)
    {
        playerTransform = player;
        OnBackroomsAwakened = onBackroomsReady;

        playerCc = player.GetComponent<CharacterController>();
        playerFpc = player.GetComponent<FirstPersonController>();
        playerFlashlight = player.GetComponentInChildren<Flashlight>();

        // Turn off flashlight in house (it's your home)
        if (playerFlashlight != null)
        {
            playerFlashlight.SetState(false, playAudio: false);
        }

        // Hide mobile touch controls during prologue intro, or configure for interaction
        if (TouchscreenInput.Instance != null)
        {
            TouchscreenInput.Instance.SetVisible(PlatformUtils.IsMobile);
        }

        // 1. Setup the atmospheric house set (manual scene design or procedural fallback)
        SetupHouseEnvironment();

        // 2. Teleport player into house bedroom/living room
        TeleportPlayerToHouse();

        // 3. Build Prologue UI
        BuildPrologueUI();

        // 4. Start TV static flicker routine
        StartCoroutine(TVFlickerRoutine());
    }

    private void SetupHouseEnvironment()
    {
        if (manualHouseRoot == null)
        {
            GameObject sceneSet = GameObject.Find("House_Prologue_Set");
            if (sceneSet == null) sceneSet = GameObject.Find("House_Set");
            if (sceneSet != null) manualHouseRoot = sceneSet;
        }

        if (manualHouseRoot != null)
        {
            isManualSet = true;
            houseRoot = manualHouseRoot;
            houseRoot.SetActive(true);
            HookManualComponents();
            Debug.Log($"<color=#00FF66><b>[Prologue]</b></color> Using manually designed house: <b>{houseRoot.name}</b>");
            return;
        }

        BuildHouseEnvironment();
    }

    private void HookManualComponents()
    {
        if (houseRoot == null) return;

        // Bed (Always search specifically for actual Bed GameObject, never match 03_Bedroom or rugs)
        if (manualBed == null)
        {
            manualBed = FindBedTransform(houseRoot.transform);
        }
        if (manualBed != null)
        {
            bedTransform = manualBed;
            Debug.Log($"<color=#00FF66><b>[Prologue]</b></color> Bed registered: <b>{bedTransform.name}</b> at {bedTransform.position}");
        }
        else
        {
            Debug.LogWarning("[Prologue] Could not find Bed transform under houseRoot! Performing scene search...");
            GameObject[] allGos = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var go in allGos)
            {
                if (string.Equals(go.name, "Bed", StringComparison.OrdinalIgnoreCase))
                {
                    manualBed = go.transform;
                    bedTransform = manualBed;
                    Debug.Log($"<color=#00FF66><b>[Prologue]</b></color> Found Bed in scene: <b>{bedTransform.name}</b> at {bedTransform.position}");
                    break;
                }
            }
        }

        // TV
        if (manualTV == null)
        {
            Transform t = FindDeepChildExact(houseRoot.transform, "LivingRoomTV") 
                       ?? FindDeepChildExact(houseRoot.transform, "CRT_TV") 
                       ?? FindDeepChildExact(houseRoot.transform, "TV")
                       ?? FindDeepChild(houseRoot.transform, "LivingRoomTV") 
                       ?? FindDeepChild(houseRoot.transform, "CRT_TV");
            if (t != null) manualTV = t.gameObject;
        }
        if (manualTV != null)
        {
            tvObject = manualTV;
            tvLight = manualTV.GetComponentInChildren<Light>();
            Renderer[] rends = manualTV.GetComponentsInChildren<Renderer>();
            foreach (var r in rends)
            {
                if (r.gameObject.name.IndexOf("Screen", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tvScreenRenderer = r;
                    tvScreenMat = r.material;
                    break;
                }
            }
        }

        // Seated and Standing Points
        if (manualCouchStartPoint == null)
        {
            manualCouchStartPoint = FindDeepChild(houseRoot.transform, "CouchSittingSpot") ?? FindDeepChild(houseRoot.transform, "SittingSpot") ?? FindDeepChild(houseRoot.transform, "PlayerSittingSpot");
        }
        if (manualStandingPoint == null)
        {
            manualStandingPoint = FindDeepChild(houseRoot.transform, "StandingSpot") ?? FindDeepChild(houseRoot.transform, "PlayerStandingSpot");
        }

        // Sofa
        if (sofaTransform == null)
        {
            Transform foundSofa = FindDeepChild(houseRoot.transform, "Sofa") ?? FindDeepChild(houseRoot.transform, "Canapé");
            if (foundSofa != null) sofaTransform = foundSofa;
        }

        HealAndCalibrateHouseSet();
    }

    /// <summary>
    /// Automatically repairs any broken GLTF/prefab scales, missing 3D meshes, or oversized colliders
    /// in the manual House_Prologue_Set so TV, Bed, and Floor collisions are 100% reliable.
    /// </summary>
    private void HealAndCalibrateHouseSet()
    {
        if (houseRoot == null) return;

        float floorY = houseRoot.transform.position.y;

        // 1. Disable any redundant child CapsuleCollider on the player so it never fights CharacterController
        if (playerTransform != null)
        {
            CapsuleCollider[] playerCapsules = playerTransform.GetComponentsInChildren<CapsuleCollider>(true);
            foreach (CapsuleCollider cap in playerCapsules)
            {
                if (cap.transform != playerTransform)
                {
                    cap.enabled = false;
                }
            }
        }

        // 2. Harden House Floor collider (10m thick slab) & disable thin rug colliders
        Transform floorTrans = FindDeepChild(houseRoot.transform, "Floor") ?? FindDeepChild(houseRoot.transform, "HouseFloor");
        if (floorTrans != null)
        {
            BoxCollider floorBox = floorTrans.GetComponent<BoxCollider>();
            if (floorBox == null) floorBox = floorTrans.gameObject.AddComponent<BoxCollider>();
            floorBox.enabled = true;
            floorBox.isTrigger = false;
            floorBox.size = new Vector3(1f, 10f, 1f);
            floorBox.center = new Vector3(0f, -4.5f, 0f);
        }

        Transform livingRug = FindDeepChild(houseRoot.transform, "LivingRoomRug");
        if (livingRug != null && livingRug.TryGetComponent<Collider>(out Collider lrCol)) lrCol.enabled = false;
        Transform bedRug = FindDeepChild(houseRoot.transform, "BedroomRug");
        if (bedRug != null && bedRug.TryGetComponent<Collider>(out Collider brCol)) brCol.enabled = false;

        // 3. Keep bedroom container clean
        Transform bedroomGroup = FindDeepChild(houseRoot.transform, "03_Bedroom");
        if (bedroomGroup != null && bedroomGroup.localScale.sqrMagnitude < 0.001f)
        {
            bedroomGroup.localScale = Vector3.one;
        }

        Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // 4. Manual Bed Setup (Respect user's manual placement, rotation, and scale)
        if (manualBed != null)
        {
            bedTransform = manualBed;
            manualBed.gameObject.SetActive(true);

            // Clean up any oversized, duplicate, or broken box colliders
            Collider[] oldBedCols = manualBed.GetComponentsInChildren<Collider>(true);
            bool hasValidNormalCollider = false;
            for (int i = oldBedCols.Length - 1; i >= 0; i--)
            {
                Collider c = oldBedCols[i];
                if (c == null) continue;

                // If collider bounds are huge (e.g. the 136m x 165m bug), destroy it
                if (c.bounds.size.magnitude > 15f)
                {
                    if (Application.isPlaying) Destroy(c);
                    else DestroyImmediate(c);
                }
                else if (c is BoxCollider)
                {
                    c.enabled = true;
                    hasValidNormalCollider = true;
                }
            }

            // If no valid normal collider exists on the bed, add one fitted to the user's bed
            if (!hasValidNormalCollider)
            {
                BoxCollider bedCol = manualBed.GetComponent<BoxCollider>();
                if (bedCol == null) bedCol = manualBed.gameObject.AddComponent<BoxCollider>();
                bedCol.enabled = true;
                Vector3 lossy = manualBed.lossyScale;
                bedCol.size = new Vector3(
                    lossy.x > 0.0001f ? 1.40f / lossy.x : 1.40f,
                    lossy.y > 0.0001f ? 0.62f / lossy.y : 0.62f,
                    lossy.z > 0.0001f ? 1.95f / lossy.z : 1.95f
                );
                bedCol.center = new Vector3(0f, bedCol.size.y * 0.5f, 0f);
            }
        }

        // 5. Heal & Calibrate CRT TV 3D Model and Screen
        if (manualTV != null)
        {
            manualTV.SetActive(true);
            Transform tvModelTrans = manualTV.transform.Find("TV_Model");
            if (tvModelTrans != null)
            {
                tvModelTrans.gameObject.SetActive(true);

                Collider[] tvCols = tvModelTrans.GetComponentsInChildren<Collider>(true);
                for (int i = tvCols.Length - 1; i >= 0; i--)
                {
                    if (tvCols[i] != null && tvCols[i].bounds.size.magnitude > 10f)
                    {
                        if (Application.isPlaying) Destroy(tvCols[i]);
                        else DestroyImmediate(tvCols[i]);
                    }
                }
            }

            BoxCollider tvRootCol = manualTV.GetComponent<BoxCollider>();
            if (tvRootCol == null)
            {
                tvRootCol = manualTV.AddComponent<BoxCollider>();
                tvRootCol.size = new Vector3(0.56f, 0.44f, 0.40f);
                tvRootCol.center = new Vector3(0f, 0.22f, 0.02f);
            }
        }

        Physics.SyncTransforms();
    }

    private Transform FindBedTransform(Transform root)
    {
        if (root == null) return null;

        // 1. Exact match first: "Bed"
        Transform exact = FindDeepChildExact(root, "Bed");
        if (exact != null) return exact;

        // 2. Common variants
        string[] candidates = new string[] { "Bed_Double", "DoubleBed", "SingleBed", "PlayerBed", "Bed_Model", "BedMesh", "Lit" };
        foreach (string c in candidates)
        {
            Transform t = FindDeepChildExact(root, c);
            if (t != null) return t;
        }

        // 3. Fallback: Contains "bed" but exclude bedroom, rug, moonlight, light, lamp, table, mat
        Transform filtered = FindDeepChildWithFilter(root, name =>
        {
            string lower = name.ToLowerInvariant();
            if (!lower.Contains("bed")) return false;
            if (lower.Contains("bedroom") || lower.Contains("rug") || lower.Contains("moonlight") || lower.Contains("light") || lower.Contains("lamp") || lower.Contains("table") || lower.Contains("mat")) return false;
            return true;
        });
        if (filtered != null) return filtered;

        return null;
    }

    private Transform FindDeepChildExact(Transform parent, string exactName)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, exactName, StringComparison.OrdinalIgnoreCase))
                return child;
            Transform deep = FindDeepChildExact(child, exactName);
            if (deep != null) return deep;
        }
        return null;
    }

    private Transform FindDeepChildWithFilter(Transform parent, Predicate<string> filter)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (filter(child.name))
                return child;
            Transform deep = FindDeepChildWithFilter(child, filter);
            if (deep != null) return deep;
        }
        return null;
    }

    private Transform FindDeepChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                return child;
            Transform deep = FindDeepChild(child, name);
            if (deep != null) return deep;
        }
        return null;
    }

    private void BuildHouseEnvironment()
    {
        houseRoot = new GameObject("House_Prologue_Set");
        houseRoot.transform.position = houseRootOffset;

        // Spacious, realistic 2-room apartment: 9.6m wide (X), 3.0m high (Y), 8.6m deep (Z)
        const float w = 9.6f;
        const float h = 3.0f;
        const float d = 8.6f;

        // Load textured environment materials
        BackroomsRoomGenerator gen = FindFirstObjectByType<BackroomsRoomGenerator>();
        Material floorMat = gen != null && gen.floorMaterial != null ? gen.floorMaterial : Resources.Load<Material>("Carpet/Floor");
        if (floorMat == null) floorMat = AssetDatabaseLoadMat("Assets/_Project/Art/Environment/Backrooms/Textures/Carpet/Floor.mat");

        Material wallMat = gen != null && gen.wallMaterial != null ? gen.wallMaterial : Resources.Load<Material>("Level 0 Wall/Wall");
        if (wallMat == null) wallMat = AssetDatabaseLoadMat("Assets/_Project/Art/Environment/Backrooms/Textures/Level 0 Wall/Wall.mat");

        Material ceilingMat = gen != null && gen.ceilingMaterial != null ? gen.ceilingMaterial : Resources.Load<Material>("Ceilings/Ceilings");
        if (ceilingMat == null) ceilingMat = AssetDatabaseLoadMat("Assets/_Project/Art/Environment/Backrooms/Textures/Ceilings/Ceilings.mat");

        // Styling Materials
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        Material darkWoodMat = new Material(litShader);
        darkWoodMat.color = new Color(0.16f, 0.11f, 0.08f);

        Material tvCasingMat = new Material(litShader);
        tvCasingMat.color = new Color(0.10f, 0.10f, 0.12f);

        Material rugMat = new Material(litShader);
        rugMat.color = new Color(0.12f, 0.13f, 0.15f);

        Material bedroomRugMat = new Material(litShader);
        bedroomRugMat.color = new Color(0.16f, 0.12f, 0.11f);

        Material fabricWhiteMat = new Material(litShader);
        fabricWhiteMat.color = new Color(0.88f, 0.88f, 0.88f);

        Material clockDisplayMat = new Material(litShader);
        clockDisplayMat.color = Color.black;
        clockDisplayMat.EnableKeyword("_EMISSION");
        clockDisplayMat.SetColor("_EmissionColor", new Color(0.95f, 0.40f, 0.05f) * 1.8f);

        // 1. Floor (Textured carpet)
        CreateBox("HouseFloor", new Vector3(0f, -0.1f, 0f), new Vector3(w, 0.2f, d), floorMat, false);

        // 2. Ceiling (Textured ceiling tiles)
        CreateBox("HouseCeiling", new Vector3(0f, h + 0.1f, 0f), new Vector3(w, 0.2f, d), ceilingMat, true);

        // 3. Outer Walls (Textured wallpaper)
        CreateBox("Wall_North", new Vector3(0f, h * 0.5f, d * 0.5f), new Vector3(w, h, 0.2f), wallMat, false);
        CreateBox("Wall_South", new Vector3(0f, h * 0.5f, -d * 0.5f), new Vector3(w, h, 0.2f), wallMat, false);
        CreateBox("Wall_West", new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(0.2f, h, d), wallMat, false);
        CreateBox("Wall_East", new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(0.2f, h, d), wallMat, false);

        // 4. Room Divider with Doorway Opening
        // Left section (2.8m wide)
        CreateBox("Divider_Left", new Vector3(-3.4f, h * 0.5f, 0.0f), new Vector3(2.8f, h, 0.15f), wallMat, false);
        // Right section (4.6m wide)
        CreateBox("Divider_Right", new Vector3(2.5f, h * 0.5f, 0.0f), new Vector3(4.6f, h, 0.15f), wallMat, false);
        // Doorway header above opening (X from -2.0m to +0.2m = 2.2m wide walk-through doorway)
        CreateBox("Divider_Header", new Vector3(-0.9f, 2.6f, 0.0f), new Vector3(2.2f, 0.8f, 0.15f), wallMat, false);

        // Doorway Frame Trim
        CreateSubBox(houseRoot.transform, "DoorCasing_Left", new Vector3(-2.0f, 1.1f, 0f), new Vector3(0.08f, 2.2f, 0.18f), darkWoodMat);
        CreateSubBox(houseRoot.transform, "DoorCasing_Right", new Vector3(0.2f, 1.1f, 0f), new Vector3(0.08f, 2.2f, 0.18f), darkWoodMat);
        CreateSubBox(houseRoot.transform, "DoorCasing_Top", new Vector3(-0.9f, 2.24f, 0f), new Vector3(2.28f, 0.08f, 0.18f), darkWoodMat);

        // =====================================================================
        // BEDROOM (South side: Z in [-4.2, 0.0])
        // =====================================================================
        GameObject bedPrefab = Resources.Load<GameObject>("Decoration/Bed");
#if UNITY_EDITOR
        if (bedPrefab == null)
            bedPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Environment/Backrooms/Decoration/Bed.prefab");
        if (bedPrefab == null)
            bedPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Environment/Backrooms/Models/bed_with_extras.glb");
#endif

        if (bedPrefab != null)
        {
            GameObject bed = Instantiate(bedPrefab, houseRoot.transform, false);
            bed.name = "Bed";
            // Place bed in bedroom with headboard toward South wall (-Z)
            bed.transform.localPosition = new Vector3(-2.6f, 0f, -2.6f);
            bed.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            bedTransform = bed.transform;

            // Remove any legacy colliders to prevent physics issues
            Collider[] existingCols = bed.GetComponentsInChildren<Collider>(true);
            for (int c = existingCols.Length - 1; c >= 0; c--)
            {
                if (Application.isPlaying) Destroy(existingCols[c]);
                else DestroyImmediate(existingCols[c]);
            }

            // Realistic bed size: ~1.9m length, ~1.4m width, ~0.6m height
            Renderer[] rends = bed.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

                float currentLength = Mathf.Max(b.size.x, b.size.z);
                if (currentLength > 0.001f)
                {
                    const float targetLength = 1.95f;
                    float scaleFactor = targetLength / currentLength;
                    bed.transform.localScale *= scaleFactor;
                }

                // Adjust vertical height so bed legs rest flush on the carpet
                b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

                float floorY = houseRoot.transform.position.y;
                float bottomDelta = b.min.y - floorY;
                bed.transform.localPosition -= new Vector3(0f, bottomDelta, 0f);

                // Add clean BoxCollider
                b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

                BoxCollider col = bed.AddComponent<BoxCollider>();
                col.center = bed.transform.InverseTransformPoint(b.center);
                Vector3 worldSize = b.size;
                Vector3 lossy = bed.transform.lossyScale;
                col.size = new Vector3(
                    lossy.x != 0 ? Mathf.Abs(worldSize.x / lossy.x) : worldSize.x,
                    lossy.y != 0 ? Mathf.Abs(worldSize.y / lossy.y) : worldSize.y,
                    lossy.z != 0 ? Mathf.Abs(worldSize.z / lossy.z) : worldSize.z
                );
            }

            // Compact Bedside Nightstand
            CreateSubBox(houseRoot.transform, "Nightstand", new Vector3(-3.45f, 0.20f, -3.2f), new Vector3(0.38f, 0.40f, 0.38f), darkWoodMat);

            // Nightstand Details: Table Lamp + Glowing Digital Clock
            CreateSubBox(houseRoot.transform, "LampBase", new Vector3(-3.50f, 0.45f, -3.28f), new Vector3(0.08f, 0.10f, 0.08f), darkWoodMat);
            CreateSubBox(houseRoot.transform, "LampShade", new Vector3(-3.50f, 0.54f, -3.28f), new Vector3(0.14f, 0.10f, 0.14f), fabricWhiteMat);

            // Digital Alarm Clock (03:14 AM amber display)
            GameObject clockObj = new GameObject("AlarmClock");
            clockObj.transform.SetParent(houseRoot.transform, false);
            clockObj.transform.localPosition = new Vector3(-3.35f, 0.44f, -3.12f);
            clockObj.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
            CreateSubBox(clockObj.transform, "ClockBody", Vector3.zero, new Vector3(0.10f, 0.06f, 0.06f), tvCasingMat);
            CreateSubBox(clockObj.transform, "ClockScreen", new Vector3(0f, 0.005f, 0.031f), new Vector3(0.07f, 0.03f, 0.005f), clockDisplayMat);

            // Bedside Rug Runner
            CreateSubBox(houseRoot.transform, "BedroomRug", new Vector3(-1.8f, 0.005f, -2.6f), new Vector3(0.65f, 0.01f, 1.5f), bedroomRugMat);
        }
        else
        {
            // Procedural fallback
            GameObject bed = new GameObject("Bed");
            bed.transform.SetParent(houseRoot.transform, false);
            bed.transform.localPosition = new Vector3(-2.6f, 0f, -2.6f);
            bed.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            bedTransform = bed.transform;

            CreateSubBox(bed.transform, "BedBase", new Vector3(0f, 0.18f, 0f), new Vector3(1.6f, 0.36f, 2.1f), darkWoodMat);
            CreateSubBox(bed.transform, "Mattress", new Vector3(0f, 0.42f, 0f), new Vector3(1.5f, 0.16f, 2.0f), fabricWhiteMat);
            CreateSubBox(bed.transform, "Pillow", new Vector3(0f, 0.52f, -0.72f), new Vector3(1.1f, 0.12f, 0.45f), fabricWhiteMat);

            CreateSubBox(houseRoot.transform, "Nightstand", new Vector3(-3.45f, 0.20f, -3.2f), new Vector3(0.38f, 0.40f, 0.38f), darkWoodMat);
        }

        // =====================================================================
        // LIVING ROOM (North side: Z in [0.0, 4.2])
        // =====================================================================
        // 1. Living Room Area Rug
        CreateSubBox(houseRoot.transform, "LivingRoomRug", new Vector3(0.5f, 0.005f, 2.2f), new Vector3(1.6f, 0.01f, 1.4f), rugMat);

        // 2. Sofa (Facing North towards TV: rotated 180 so seat faces +Z)
        GameObject sofaPrefab = Resources.Load<GameObject>("Decoration/Canapé");
#if UNITY_EDITOR
        if (sofaPrefab == null) sofaPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Environment/Backrooms/Decoration/Canapé.prefab");
#endif
        if (sofaPrefab != null)
        {
            GameObject sofa = Instantiate(sofaPrefab, houseRoot.transform, false);
            sofa.name = "LivingRoomSofa";
            sofa.transform.localPosition = new Vector3(0.5f, 0f, 1.6f);
            sofa.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            sofa.transform.localScale = Vector3.one * 0.10f;
            sofaTransform = sofa.transform;
        }

        // 3. Compact Coffee Table (In front of sofa)
        CreateSubBox(houseRoot.transform, "CoffeeTableTop", new Vector3(0.5f, 0.22f, 2.35f), new Vector3(0.70f, 0.025f, 0.36f), darkWoodMat);
        CreateSubBox(houseRoot.transform, "CoffeeTableLeg1", new Vector3(0.20f, 0.10f, 2.22f), new Vector3(0.035f, 0.20f, 0.035f), darkWoodMat);
        CreateSubBox(houseRoot.transform, "CoffeeTableLeg2", new Vector3(0.80f, 0.10f, 2.22f), new Vector3(0.035f, 0.20f, 0.035f), darkWoodMat);
        CreateSubBox(houseRoot.transform, "CoffeeTableLeg3", new Vector3(0.20f, 0.10f, 2.48f), new Vector3(0.035f, 0.20f, 0.035f), darkWoodMat);
        CreateSubBox(houseRoot.transform, "CoffeeTableLeg4", new Vector3(0.80f, 0.10f, 2.48f), new Vector3(0.035f, 0.20f, 0.035f), darkWoodMat);

        // TV Remote Control on Coffee Table
        CreateSubBox(houseRoot.transform, "TV_Remote", new Vector3(0.46f, 0.245f, 2.35f), new Vector3(0.05f, 0.015f, 0.14f), tvCasingMat);
        CreateSubBox(houseRoot.transform, "RemotePowerBtn", new Vector3(0.46f, 0.254f, 2.40f), new Vector3(0.015f, 0.005f, 0.015f), clockDisplayMat);

        // TV Stand / Console Table (Against North Wall)
        GameObject tablePrefab = Resources.Load<GameObject>("Decoration/Table");
#if UNITY_EDITOR
        if (tablePrefab == null) tablePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Environment/Backrooms/Decoration/Table.prefab");
#endif
        if (tablePrefab != null)
        {
            GameObject table = Instantiate(tablePrefab, houseRoot.transform, false);
            table.name = "TVTable";
            table.transform.localPosition = new Vector3(0.5f, 0f, 3.65f);
            table.transform.localRotation = Quaternion.identity;
            table.transform.localScale = Vector3.one * 0.08f;
        }

        // =====================================================================
        // CRT TV (Faces South directly towards sofa and player!)
        // =====================================================================
        tvObject = new GameObject("LivingRoomTV");
        tvObject.transform.SetParent(houseRoot.transform, false);
        tvObject.transform.localPosition = new Vector3(0.5f, 0.44f, 3.65f);
        tvObject.transform.localRotation = Quaternion.identity; // Identity: -Z faces South toward sofa

        GameObject tvPrefab = Resources.Load<GameObject>("Decoration/CRT_TV");
#if UNITY_EDITOR
        if (tvPrefab == null)
            tvPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Environment/Backrooms/Decoration/CRT_TV.prefab");
        if (tvPrefab == null)
            tvPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Environment/Backrooms/Models/crt_tv.glb");
        if (tvPrefab == null)
            tvPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Environment/Backrooms/Decoration/TV 1.prefab");
#endif

        if (tvPrefab != null)
        {
            GameObject tvModel = Instantiate(tvPrefab, tvObject.transform, false);
            tvModel.name = "TV_Model";
            // TV model facing South directly towards sofa and player (rotated 180 deg)
            tvModel.transform.localRotation = Quaternion.identity;

            Collider[] cols = tvModel.GetComponentsInChildren<Collider>(true);
            for (int c = cols.Length - 1; c >= 0; c--)
            {
                if (Application.isPlaying) Destroy(cols[c]);
                else DestroyImmediate(cols[c]);
            }

            Renderer[] rends = tvModel.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

                // Proportional normalization to realistic compact CRT scale: ~0.52m width, ~0.42m height
                float currentWidth = Mathf.Max(b.size.x, b.size.z);
                if (currentWidth > 0.001f)
                {
                    const float targetWidth = 0.52f;
                    float factor = targetWidth / currentWidth;
                    tvModel.transform.localScale *= factor;
                }

                // Re-evaluate bounds after scale
                b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

                // Adjust vertical height so TV sits flush on the table stand
                float tableTopY = houseRoot.transform.position.y + 0.42f;
                float bottomDelta = b.min.y - tableTopY;
                tvModel.transform.localPosition -= new Vector3(0f, bottomDelta, 0f);

                // Fit BoxCollider
                b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                BoxCollider col = tvModel.AddComponent<BoxCollider>();
                col.center = tvModel.transform.InverseTransformPoint(b.center);
                Vector3 worldSize = b.size;
                Vector3 lossy = tvModel.transform.lossyScale;
                col.size = new Vector3(
                    lossy.x != 0 ? Mathf.Abs(worldSize.x / lossy.x) : worldSize.x,
                    lossy.y != 0 ? Mathf.Abs(worldSize.y / lossy.y) : worldSize.y,
                    lossy.z != 0 ? Mathf.Abs(worldSize.z / lossy.z) : worldSize.z
                );
            }
        }
        else
        {
            // TV Casing fallback (Dark matte plastic: 0.56m wide, 0.44m high, 0.38m deep)
            CreateSubBox(tvObject.transform, "TV_Casing", Vector3.zero, new Vector3(0.56f, 0.44f, 0.38f), tvCasingMat);
        }

        // TV Screen (Quad facing South toward player: localRotation = 180 deg around Y, positioned at front face Z = -0.191m)
        GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "TV_Screen";
        screen.transform.SetParent(tvObject.transform, false);
        screen.transform.localPosition = new Vector3(0f, 0.22f, -0.191f);
        screen.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        screen.transform.localScale = new Vector3(0.42f, 0.32f, 1f);

        // TV Screen Emission Material
        tvScreenMat = new Material(litShader);
        tvScreenMat.color = new Color(0.75f, 0.88f, 1.0f);
        tvScreenMat.EnableKeyword("_EMISSION");
        tvScreenMat.SetColor("_EmissionColor", new Color(0.60f, 0.82f, 1.0f) * 1.8f);
        tvScreenRenderer = screen.GetComponent<MeshRenderer>();
        tvScreenRenderer.material = tvScreenMat;

        // Dynamic TV Light: Positioned IN FRONT of the screen (shining South into the room onto player/sofa!)
        GameObject tvLightGo = new GameObject("TV_Light");
        tvLightGo.transform.SetParent(tvObject.transform, false);
        tvLightGo.transform.localPosition = new Vector3(0f, 0.22f, -0.55f);
        tvLight = tvLightGo.AddComponent<Light>();
        tvLight.type = LightType.Point;
        tvLight.range = 5.5f;
        tvLight.intensity = 2.2f;
        tvLight.color = new Color(0.72f, 0.86f, 1.0f);

        // TV Static Audio (disabled - TV is silent)
        // tvAudio = tvObject.AddComponent<AudioSource>();

        // Ambient night moonlight accents
        GameObject bedroomMoon = new GameObject("BedroomMoonlight");
        bedroomMoon.transform.SetParent(houseRoot.transform, false);
        bedroomMoon.transform.localPosition = new Vector3(-w * 0.35f, 2.2f, -2.4f);
        Light bMoon = bedroomMoon.AddComponent<Light>();
        bMoon.type = LightType.Point;
        bMoon.color = new Color(0.12f, 0.18f, 0.32f);
        bMoon.intensity = 0.40f;
        bMoon.range = 6.0f;

        GameObject livingMoon = new GameObject("LivingRoomMoonlight");
        livingMoon.transform.SetParent(houseRoot.transform, false);
        livingMoon.transform.localPosition = new Vector3(w * 0.35f, 2.2f, 2.5f);
        Light lMoon = livingMoon.AddComponent<Light>();
        lMoon.type = LightType.Point;
        lMoon.color = new Color(0.12f, 0.18f, 0.32f);
        lMoon.intensity = 0.40f;
        lMoon.range = 6.0f;
    }

    private void TeleportPlayerToHouse()
    {
        if (playerTransform == null) return;

        isSittingOnCouch = false;
        isTransitioningStandUp = false;

        float houseFloorY = houseRoot != null ? houseRoot.transform.position.y : houseRootOffset.y;

        if (manualStandingPoint != null)
        {
            standingPos = manualStandingPoint.position;
            standingPos.y = Mathf.Max(standingPos.y, houseFloorY + 0.15f);
            playerTransform.rotation = manualStandingPoint.rotation;
        }
        else if (manualCouchStartPoint != null)
        {
            Vector3 pt = manualCouchStartPoint.position;
            standingPos = new Vector3(pt.x, Mathf.Max(pt.y, houseFloorY + 0.15f), pt.z);
            playerTransform.rotation = manualCouchStartPoint.rotation;
        }
        else
        {
            // Player starts standing in living room facing CRT TV
            standingPos = new Vector3(0.5f, houseFloorY + 0.15f, 1.15f);
            playerTransform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        }

        sittingPos = standingPos;

        if (playerCc != null) playerCc.enabled = false;

        playerTransform.position = standingPos;
        Physics.SyncTransforms();

        if (playerCc != null) playerCc.enabled = true;

        if (playerFpc != null)
        {
            playerFpc.ResetMotionState();
            playerFpc.SetPitch(0f);
        }
    }

    private void BuildPrologueUI()
    {
        GameObject uiGo = new GameObject("Prologue_HUD");
        uiGo.transform.SetParent(transform, false);

        uiDoc = uiGo.AddComponent<UIDocument>();
        PanelSettings settings = Resources.Load<PanelSettings>("MenuPanelSettings");
        if (settings != null) uiDoc.panelSettings = settings;

        uiRoot = uiDoc.rootVisualElement;
        uiRoot.style.position = Position.Absolute;
        uiRoot.style.width = Length.Percent(100);
        uiRoot.style.height = Length.Percent(100);

        // 1. Fullscreen Black Fade Overlay
        fadeOverlay = new VisualElement();
        fadeOverlay.style.position = Position.Absolute;
        fadeOverlay.style.width = Length.Percent(100);
        fadeOverlay.style.height = Length.Percent(100);
        fadeOverlay.style.backgroundColor = Color.black;
        fadeOverlay.style.opacity = 1f; // Starts fully faded in from black
        fadeOverlay.pickingMode = PickingMode.Ignore;
        uiRoot.Add(fadeOverlay);

        // 2. Center Narrative / Action Prompt
        promptCard = new VisualElement();
        promptCard.style.position = Position.Absolute;
        promptCard.style.bottom = Length.Percent(22);
        promptCard.style.alignSelf = Align.Center;
        promptCard.style.flexDirection = FlexDirection.Row;
        promptCard.style.alignItems = Align.Center;
        promptCard.style.backgroundColor = new Color(0.05f, 0.05f, 0.07f, 0.88f);
        promptCard.style.borderLeftColor = new Color(0.9f, 0.8f, 0.3f);
        promptCard.style.borderLeftWidth = 3f;
        promptCard.style.paddingLeft = 16f;
        promptCard.style.paddingRight = 18f;
        promptCard.style.paddingTop = 10f;
        promptCard.style.paddingBottom = 10f;
        promptCard.style.borderTopRightRadius = 4f;
        promptCard.style.borderBottomRightRadius = 4f;

        promptKeyLabel = new Label(PlatformUtils.IsMobile ? "[TAP]" : "[E]");
        promptKeyLabel.style.color = new Color(0.9f, 0.8f, 0.3f);
        promptKeyLabel.style.fontSize = 17f;
        promptKeyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        promptKeyLabel.style.marginRight = 10f;

        promptActionLabel = new Label("STAND UP FROM COUCH");
        promptActionLabel.style.color = Color.white;
        promptActionLabel.style.fontSize = 15f;
        promptActionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

        promptCard.Add(promptKeyLabel);
        promptCard.Add(promptActionLabel);
        promptCard.style.display = DisplayStyle.None;
        promptCard.RegisterCallback<PointerDownEvent>(evt =>
        {
            cardTapped = true;
            cardTappedFrame = Time.frameCount;
        });
        uiRoot.Add(promptCard);

        // Fade in from black over 2.5 seconds
        StartCoroutine(InitialHouseFadeIn());
    }

    private IEnumerator InitialHouseFadeIn()
    {
        yield return new WaitForSeconds(0.4f);
        float elapsed = 0f;
        const float dur = 2.4f;
        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            fadeOverlay.style.opacity = Mathf.Lerp(1f, 0f, elapsed / dur);
            yield return null;
        }
        fadeOverlay.style.opacity = 0f;
    }

    private void Update()
    {
        if (!isPrologueActive || isSleeping || playerTransform == null) return;

        // Safety rescue: if the player ever drops below the house floor during the prologue, immediately restore them inside the house
        float houseFloorY = houseRoot != null ? houseRoot.transform.position.y : houseRootOffset.y;
        if (playerTransform.position.y < houseFloorY - 1.2f)
        {
            if (playerCc != null) playerCc.enabled = false;
            playerTransform.position = new Vector3(standingPos.x, Mathf.Max(standingPos.y, houseFloorY + 0.15f), standingPos.z);
            Physics.SyncTransforms();
            if (playerFpc != null) playerFpc.ResetMotionState();
            if (playerCc != null) playerCc.enabled = true;
        }

        bool isMobile = PlatformUtils.IsMobile;

        bool canInteractTV = IsNearAndFacing(playerTransform, tvObject != null ? tvObject.transform : null, 2.0f, 0.3f);
        
        if (bedTransform == null && houseRoot != null)
        {
            bedTransform = FindBedTransform(houseRoot.transform);
        }
        bool canInteractBed = CanInteractWithBed(playerTransform, bedTransform);

        if (tvIsOn)
        {
            // TV is running
            if (canInteractTV)
            {
                SetPrompt(true, isMobile ? "[TAP]" : "[E]", "TURN OFF TV", isInteractive: true);

                if (IsInteractPressed())
                {
                    TurnOffTV();
                }
            }
            else
            {
                SetPrompt(false);
            }
        }
        else
        {
            // TV is off, objective is to sleep
            if (canInteractBed)
            {
                SetPrompt(true, isMobile ? "[TAP]" : "[E]", "GO TO SLEEP", isInteractive: true);

                if (IsInteractPressed())
                {
                    GoToSleep();
                }
            }
            else
            {
                SetPrompt(true, "●", "GO TO BED... YOU NEED REST", isInteractive: false);
            }
        }
    }

    private void SetPrompt(bool visible, string key = "", string action = "", bool isInteractive = true)
    {
        if (promptCard == null) return;
        promptCard.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (visible)
        {
            if (promptKeyLabel != null) promptKeyLabel.text = key;
            if (promptActionLabel != null) promptActionLabel.text = action;

            if (isInteractive)
            {
                promptCard.style.borderLeftColor = new Color(0.2f, 0.95f, 0.4f);
                promptCard.style.backgroundColor = new Color(0.06f, 0.12f, 0.08f, 0.94f);
                if (promptKeyLabel != null) promptKeyLabel.style.color = new Color(0.2f, 0.95f, 0.4f);
            }
            else
            {
                promptCard.style.borderLeftColor = new Color(0.7f, 0.7f, 0.7f, 0.6f);
                promptCard.style.backgroundColor = new Color(0.05f, 0.05f, 0.07f, 0.88f);
                if (promptKeyLabel != null) promptKeyLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            }
        }
    }

    private bool CanInteractWithBed(Transform player, Transform bed)
    {
        if (player == null || bed == null) return false;

        Camera cam = Camera.main;
        Vector3 eyePos = cam != null ? cam.transform.position : player.position + Vector3.up * 1.6f;
        Vector3 camForward = cam != null ? cam.transform.forward : player.forward;

        // 1. Direct crosshair raycast: if reticle points at any collider on the bed from up to 3.5m
        if (Physics.Raycast(eyePos, camForward, out RaycastHit hit, 3.5f))
        {
            if (hit.transform == bed || hit.transform.IsChildOf(bed))
            {
                return true;
            }
        }

        // 2. Measure closest point across all colliders on bed
        Collider[] cols = bed.GetComponentsInChildren<Collider>();
        Vector3 closestPoint = bed.position;
        float minDist = float.MaxValue;
        bool foundCollider = false;

        if (cols != null && cols.Length > 0)
        {
            for (int i = 0; i < cols.Length; i++)
            {
                Collider c = cols[i];
                if (c == null || !c.enabled) continue;
                Vector3 pt = c.ClosestPoint(eyePos);
                float d = Vector3.Distance(eyePos, pt);
                if (d < minDist)
                {
                    minDist = d;
                    closestPoint = pt;
                    foundCollider = true;
                }
            }
        }

        if (!foundCollider)
        {
            Renderer[] rends = bed.GetComponentsInChildren<Renderer>();
            if (rends != null && rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                closestPoint = b.ClosestPoint(eyePos);
                minDist = Vector3.Distance(eyePos, closestPoint);
            }
            else
            {
                closestPoint = bed.position;
                minDist = Vector3.Distance(eyePos, closestPoint);
            }
        }

        // Horizontal distance from player feet/center to closest point on bed
        Vector3 playerPos = player.position;
        Vector2 playerFlat = new Vector2(playerPos.x, playerPos.z);
        Vector2 bedFlat = new Vector2(closestPoint.x, closestPoint.z);
        float flatDist = Vector2.Distance(playerFlat, bedFlat);

        // Bed is a large piece of furniture: Allow interaction if within 3.2m 3D distance or within 2.6m horizontal distance
        if (minDist > 3.2f && flatDist > 2.6f)
        {
            return false;
        }

        // 3. Facing check: Player shouldn't be facing completely away from the bed
        Vector2 toBedFlat = (bedFlat - playerFlat).normalized;
        Vector2 camForwardFlat = new Vector2(camForward.x, camForward.z).normalized;
        float dotFlat = Vector2.Dot(camForwardFlat, toBedFlat);

        Vector3 toBed3D = (closestPoint - eyePos).normalized;
        float dot3D = Vector3.Dot(camForward, toBed3D);

        // If very close to bed (e.g. beside it, <= 1.8m flat), allow wide angle (-0.2f)
        if (flatDist <= 1.8f || minDist <= 2.2f)
        {
            if (dotFlat < -0.2f && dot3D < -0.2f)
            {
                return false;
            }
        }
        else
        {
            // At 1.8m - 2.6m flat distance, ensure player is generally facing toward the bed
            if (dotFlat < 0.1f && dot3D < 0.1f)
            {
                return false;
            }
        }

        // 4. Linecast obstacle check: make sure there isn't a solid wall between player and bed
        Vector3 rayStart = eyePos + camForward * 0.15f;
        if (Physics.Linecast(rayStart, closestPoint, out RaycastHit obstHit))
        {
            if (obstHit.transform != bed && !obstHit.transform.IsChildOf(bed) && !obstHit.transform.IsChildOf(player))
            {
                string obstName = obstHit.transform.name.ToLowerInvariant();
                // Ignore floor, rug, carpet, blanket, sheet, pillows
                bool isIgnored = obstName.Contains("rug") || obstName.Contains("carpet") ||
                                 obstName.Contains("floor") || obstName.Contains("pillow") ||
                                 obstName.Contains("blanket") || obstName.Contains("sheet");
                if (!isIgnored)
                {
                    // If blocked by a wall or major furniture obstacle
                    if (obstName.Contains("wall") || obstHit.distance < Vector3.Distance(rayStart, closestPoint) - 0.45f)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private bool IsNearAndFacing(Transform player, Transform target, float maxDist, float minDot = 0.25f)
    {
        if (player == null || target == null) return false;

        Camera cam = Camera.main;
        Vector3 eyePos = cam != null ? cam.transform.position : player.position + Vector3.up * 1.6f;
        Vector3 forward = cam != null ? cam.transform.forward : player.forward;

        // Use collider closest point if available for large objects like bed
        Collider col = target.GetComponentInChildren<Collider>();
        Vector3 targetPoint = col != null ? col.ClosestPoint(eyePos) : target.position;

        float dist = Vector3.Distance(eyePos, targetPoint);
        if (dist > maxDist) return false;

        // Facing direction check: player must be looking towards the object
        Vector3 toTarget = (targetPoint - eyePos).normalized;
        float dot = Vector3.Dot(forward, toTarget);
        if (dot < minDot) return false;

        // Linecast to ensure player isn't on the other side of a wall
        Vector3 rayStart = eyePos + forward * 0.2f;
        if (Physics.Linecast(rayStart, targetPoint, out RaycastHit hit))
        {
            if (hit.transform != target && !hit.transform.IsChildOf(target) && !hit.transform.IsChildOf(player))
            {
                // Obstructed by wall
                return false;
            }
        }

        return true;
    }

    private IEnumerator StandUpRoutine()
    {
        isTransitioningStandUp = true;
        if (promptCard != null) promptCard.style.display = DisplayStyle.None;

        // Play subtle rustle / footstep audio
        AudioClip standStep = Resources.Load<AudioClip>("SFX/Footsteps/Player_Footstep_01");
        if (standStep != null && DirectorAudio != null)
        {
            DirectorAudio.PlayOneShot(standStep, 0.7f);
        }

        // Smooth natural stand up camera motion over 0.55s
        float elapsed = 0f;
        const float dur = 0.55f;
        Vector3 start = playerTransform.position;
        Vector3 dest = standingPos;

        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / dur);
            playerTransform.position = Vector3.Lerp(start, dest, t);
            yield return null;
        }

        playerTransform.position = dest;
        Physics.SyncTransforms();
        if (playerFpc != null) playerFpc.ResetMotionState();
        if (playerCc != null) playerCc.enabled = true;

        isSittingOnCouch = false;
        isTransitioningStandUp = false;
    }

    private bool cardTapped = false;
    private int cardTappedFrame = -1;

    private void LateUpdate()
    {
        if (cardTappedFrame < Time.frameCount - 1)
        {
            cardTapped = false;
        }
    }

    private bool IsInteractPressed()
    {
        if (cardTapped || (cardTappedFrame >= Time.frameCount - 2 && cardTappedFrame > 0))
        {
            cardTapped = false;
            cardTappedFrame = -1;
            return true;
        }

        Keyboard kb = Keyboard.current;
        if (kb != null && (kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
        {
            return true;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && promptCard != null && promptCard.style.display == DisplayStyle.Flex)
        {
            return true;
        }

        Gamepad gp = Gamepad.current;
        if (gp != null && (gp.buttonWest.wasPressedThisFrame || gp.buttonSouth.wasPressedThisFrame))
        {
            return true;
        }

        Touchscreen touch = Touchscreen.current;
        if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
        {
            return true;
        }

        return false;
    }

    private void TurnOffTV()
    {
        tvIsOn = false;
        hasWatchedTV = true;

        // Stop TV light and static
        if (tvLight != null) tvLight.enabled = false;
        if (tvAudio != null && tvAudio.isPlaying) tvAudio.Stop();

        // Mechanical TV click off audio
        if (tvClickOffClip != null)
        {
            DirectorAudio.PlayOneShot(tvClickOffClip, 1.0f);
        }

        // Screen black & change material to none so TV appears shut off
        if (tvScreenRenderer != null)
        {
            tvScreenRenderer.material = null;
            tvScreenRenderer.sharedMaterial = null;
            tvScreenRenderer.enabled = false;
        }
        else if (tvScreenMat != null)
        {
            tvScreenMat.SetColor("_EmissionColor", Color.black);
            tvScreenMat.color = Color.black;
        }

        // Also check any other mesh renderers specifically named Screen on the TV model
        if (tvObject != null)
        {
            Renderer[] allRends = tvObject.GetComponentsInChildren<Renderer>(true);
            foreach (var r in allRends)
            {
                if (r != null && r.gameObject.name.IndexOf("Screen", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    r.material = null;
                    r.sharedMaterial = null;
                    r.enabled = false;
                }
            }
        }

        // Room plunges into darkness
        promptActionLabel.text = "GO TO BED... YOU NEED REST";
    }

    private void GoToSleep()
    {
        isSleeping = true;
        promptCard.style.display = DisplayStyle.None;

        StartCoroutine(SleepAndNoclipSequence());
    }

    private IEnumerator SleepAndNoclipSequence()
    {
        // 1. Lock movement as player lies down
        if (playerFpc != null) playerFpc.enabled = false;

        // 2. Smoothly rotate camera downwards towards pillow & fade to black
        float layDownTime = 2.2f;
        float elapsed = 0f;
        Vector3 startPos = playerTransform.position;
        Vector3 bedPillowPos = bedTransform != null ? bedTransform.position + Vector3.up * 0.85f : houseRootOffset + new Vector3(-2.6f, 0.85f, -3.3f);

        while (elapsed < layDownTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / layDownTime;
            fadeOverlay.style.opacity = Mathf.Clamp01(t * 1.1f);
            if (playerFpc != null) playerFpc.SetPitch(Mathf.Lerp(0f, -40f, t));
            yield return null;
        }

        fadeOverlay.style.opacity = 1f;

        // 3. Complete silence for a moment while asleep (heartbeat / breathing)
        yield return new WaitForSeconds(2.0f);

        // 4. SUDDEN NOCLIP TRANSITION:
        // Screeching reality rupture, heavy analog glitch burst, noclip falling sound
        if (noclipGlitchClip != null)
        {
            DirectorAudio.PlayOneShot(noclipGlitchClip, 1.0f);
        }
        CameraVHSGlitch.TriggerKillBurst();

        // 5. Build Backrooms Level 0 below
        OnBackroomsAwakened?.Invoke();

        // 6. Player wakes up in the Backrooms face-down on the yellow carpet
        yield return new WaitForSeconds(1.6f);

        // Clean up house model (deactivate if manual scene object so it isn't deleted from the scene!)
        if (houseRoot != null)
        {
            if (isManualSet) houseRoot.SetActive(false);
            else Destroy(houseRoot);
        }
        isPrologueActive = false;

        // 7. Waking up in Backrooms: slowly open eyes from black (fade from 1.0 to 0.0)
        elapsed = 0f;
        const float wakeDur = 3.2f;
        if (playerFpc != null)
        {
            playerFpc.SetPitch(-55f); // Looking down at the damp carpet
            playerFpc.enabled = true;
        }

        while (elapsed < wakeDur)
        {
            elapsed += Time.deltaTime;
            fadeOverlay.style.opacity = Mathf.Lerp(1f, 0f, elapsed / wakeDur);
            // Slowly raise head
            if (playerFpc != null)
            {
                playerFpc.SetPitch(Mathf.Lerp(-55f, 0f, elapsed / wakeDur));
            }
            yield return null;
        }

        fadeOverlay.style.opacity = 0f;

        // Destroy prologue HUD
        if (uiDoc != null) Destroy(uiDoc.gameObject);
        Destroy(gameObject);
    }

    private IEnumerator TVFlickerRoutine()
    {
        while (tvIsOn)
        {
            if (tvLight != null)
            {
                tvLight.intensity = UnityEngine.Random.Range(1.8f, 3.1f);
                tvLight.color = Color.Lerp(new Color(0.7f, 0.85f, 1.0f), new Color(0.85f, 0.95f, 1.0f), UnityEngine.Random.value);
            }
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.04f, 0.12f));
        }
    }

    // ---------------- Box Primitive Helpers ----------------
    private void CreateBox(string name, Vector3 localPos, Vector3 size, Material mat, bool flipNormal)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(houseRoot.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        if (mat != null) go.GetComponent<MeshRenderer>().material = mat;
    }

    private void CreateSubBox(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        if (mat != null) go.GetComponent<MeshRenderer>().material = mat;
    }

    private Material AssetDatabaseLoadMat(string path)
    {
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
#else
        return null;
#endif
    }
}
