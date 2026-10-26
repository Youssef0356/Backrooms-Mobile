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

    [Header("State")]
    public bool isPrologueActive = true;
    public bool tvIsOn = true;
    public bool hasWatchedTV = false;
    public bool isSleeping = false;

    // References created procedurally
    private GameObject houseRoot;
    private GameObject tvObject;
    private Light tvLight;
    private AudioSource tvAudio;
    private Material tvScreenMat;

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

        // 1. Build the atmospheric house set
        BuildHouseEnvironment();

        // 2. Teleport player into house bedroom/living room
        TeleportPlayerToHouse();

        // 3. Build Prologue UI
        BuildPrologueUI();

        // 4. Start TV static flicker routine
        StartCoroutine(TVFlickerRoutine());
    }

    private void BuildHouseEnvironment()
    {
        houseRoot = new GameObject("House_Prologue_Set");
        houseRoot.transform.position = houseRootOffset;

        // Spacious, realistic 2-room apartment: 9.6m wide (X), 3.0m high (Y), 8.6m deep (Z)
        const float w = 9.6f;
        const float h = 3.0f;
        const float d = 8.6f;

        // Load materials
        Material floorMat = Resources.Load<Material>("Carpet/Floor");
        if (floorMat == null) floorMat = AssetDatabaseLoadMat("Assets/_Project/Art/Environment/Backrooms/Textures/Carpet/Floor.mat");

        Material wallMat = Resources.Load<Material>("Level 0 Wall/Wall");
        if (wallMat == null) wallMat = AssetDatabaseLoadMat("Assets/_Project/Art/Environment/Backrooms/Textures/Level 0 Wall/Wall.mat");

        Material ceilingMat = Resources.Load<Material>("Ceilings/Ceilings");
        if (ceilingMat == null) ceilingMat = AssetDatabaseLoadMat("Assets/_Project/Art/Environment/Backrooms/Textures/Ceilings/Ceilings.mat");

        // 1. Floor (Warm apartment carpet)
        CreateBox("HouseFloor", new Vector3(0f, -0.1f, 0f), new Vector3(w, 0.2f, d), floorMat, false);

        // 2. Ceiling
        CreateBox("HouseCeiling", new Vector3(0f, h + 0.1f, 0f), new Vector3(w, 0.2f, d), ceilingMat, true);

        // 3. Outer Walls
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

        // =====================================================================
        // BEDROOM (South side: Z in [-4.2, 0.0])
        // =====================================================================
        GameObject bed = new GameObject("Bed");
        bed.transform.SetParent(houseRoot.transform, false);
        bed.transform.localPosition = new Vector3(-2.8f, 0f, -2.4f);
        bed.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        bedTransform = bed.transform;

        // Bed frame & mattress (proportional human queen bed: 1.6m x 2.1m)
        CreateSubBox(bed.transform, "BedBase", new Vector3(0f, 0.18f, 0f), new Vector3(1.6f, 0.36f, 2.1f), wallMat);
        CreateSubBox(bed.transform, "Mattress", new Vector3(0f, 0.42f, 0f), new Vector3(1.5f, 0.16f, 2.0f), ceilingMat);
        CreateSubBox(bed.transform, "Pillow", new Vector3(0f, 0.52f, 0.72f), new Vector3(1.1f, 0.12f, 0.45f), ceilingMat);

        // Bedroom Nightstand Table
        CreateSubBox(houseRoot.transform, "Nightstand", new Vector3(-4.0f, 0.28f, -2.4f), new Vector3(0.55f, 0.56f, 0.55f), wallMat);

        // =====================================================================
        // LIVING ROOM (North side: Z in [0.0, 4.2])
        // =====================================================================
        // Sofa (Facing North towards the TV with comfortable distance)
        GameObject sofaPrefab = Resources.Load<GameObject>("Decoration/Canapé");
#if UNITY_EDITOR
        if (sofaPrefab == null) sofaPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Environment/Backrooms/Decoration/Canapé.prefab");
#endif
        if (sofaPrefab != null)
        {
            GameObject sofa = Instantiate(sofaPrefab, houseRoot.transform, false);
            sofa.name = "LivingRoomSofa";
            sofa.transform.localPosition = new Vector3(0.5f, 0f, 1.6f);
            // Rotated 180 deg so the sofa seats face forward directly towards the TV (+Z)
            sofa.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            // Calibrated scale: 0.10f yields realistic ~0.82m height and ~1.65m width
            sofa.transform.localScale = Vector3.one * 0.10f;
            sofaTransform = sofa.transform;
        }

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
            table.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            // Proportional table stand size: ~0.42m height, ~0.85m width
            table.transform.localScale = Vector3.one * 0.08f;
        }

        // CRT TV (Centered directly on the table stand)
        // Built with clean proportional procedural geometry so it never clips walls or embeds furniture
        tvObject = new GameObject("LivingRoomTV");
        tvObject.transform.SetParent(houseRoot.transform, false);
        tvObject.transform.localPosition = new Vector3(0.5f, 0.44f, 3.65f);
        tvObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        // TV Casing (Realistic compact CRT: 0.52m wide, 0.42m high, 0.36m deep)
        CreateSubBox(tvObject.transform, "TV_Casing", Vector3.zero, new Vector3(0.52f, 0.42f, 0.36f), wallMat);

        // TV Screen (Dark curved glass display facing South towards the player)
        GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "TV_Screen";
        screen.transform.SetParent(tvObject.transform, false);
        screen.transform.localPosition = new Vector3(0f, 0.02f, -0.185f);
        screen.transform.localRotation = Quaternion.identity;
        screen.transform.localScale = new Vector3(0.44f, 0.34f, 1f);

        // TV Screen Emission Material
        tvScreenMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        tvScreenMat.color = new Color(0.75f, 0.88f, 1.0f);
        tvScreenMat.EnableKeyword("_EMISSION");
        tvScreenMat.SetColor("_EmissionColor", new Color(0.55f, 0.78f, 1.0f) * 1.5f);
        screen.GetComponent<MeshRenderer>().material = tvScreenMat;

        // TV Static Audio Source (3D humming & analog static)
        tvAudio = tvObject.AddComponent<AudioSource>();
        tvAudio.playOnAwake = false;
        tvAudio.loop = true;
        tvAudio.spatialBlend = 1.0f; // 3D positioned right at TV
        tvAudio.minDistance = 0.5f;
        tvAudio.maxDistance = 5.0f;
        tvAudio.rolloffMode = AudioRolloffMode.Logarithmic;
        tvAudio.volume = 0.18f; // Soft, realistic analog buzz
        if (tvStaticClip != null)
        {
            tvAudio.clip = tvStaticClip;
            tvAudio.Play();
        }

        // Bedroom Window Moonlight (Dim midnight blue through bedroom window)
        GameObject bedroomMoon = new GameObject("BedroomMoonlight");
        bedroomMoon.transform.SetParent(houseRoot.transform, false);
        bedroomMoon.transform.localPosition = new Vector3(-w * 0.42f, 2.1f, -2.4f);
        Light bMoon = bedroomMoon.AddComponent<Light>();
        bMoon.type = LightType.Point;
        bMoon.color = new Color(0.15f, 0.22f, 0.38f);
        bMoon.intensity = 0.45f;
        bMoon.range = 6.5f;

        // Living Room Window Moonlight
        GameObject livingMoon = new GameObject("LivingRoomMoonlight");
        livingMoon.transform.SetParent(houseRoot.transform, false);
        livingMoon.transform.localPosition = new Vector3(w * 0.42f, 2.1f, 2.5f);
        Light lMoon = livingMoon.AddComponent<Light>();
        lMoon.type = LightType.Point;
        lMoon.color = new Color(0.15f, 0.22f, 0.38f);
        lMoon.intensity = 0.45f;
        lMoon.range = 6.5f;
    }

    private void TeleportPlayerToHouse()
    {
        if (playerTransform == null) return;

        if (playerCc != null) playerCc.enabled = false;

        // Place player standing in living room looking at the glowing TV
        Vector3 livingRoomPos = houseRootOffset + new Vector3(0f, 1.05f, 0.2f);
        playerTransform.position = livingRoomPos;
        playerTransform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

        if (playerFpc != null)
        {
            playerFpc.SetPitch(0f);
        }

        if (playerCc != null) playerCc.enabled = true;
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

        promptKeyLabel = new Label("[E]");
        promptKeyLabel.style.color = new Color(0.9f, 0.8f, 0.3f);
        promptKeyLabel.style.fontSize = 17f;
        promptKeyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        promptKeyLabel.style.marginRight = 10f;

        promptActionLabel = new Label("TURN OFF TV & GO TO SLEEP");
        promptActionLabel.style.color = Color.white;
        promptActionLabel.style.fontSize = 15f;
        promptActionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

        promptCard.Add(promptKeyLabel);
        promptCard.Add(promptActionLabel);
        promptCard.style.display = DisplayStyle.None;
        promptCard.RegisterCallback<PointerDownEvent>(evt =>
        {
            cardTapped = true;
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

        float distToTV = tvObject != null ? Vector3.Distance(playerTransform.position, tvObject.transform.position) : 99f;
        float distToBed = bedTransform != null ? Vector3.Distance(playerTransform.position, bedTransform.position) : 99f;

        bool isMobile = PlatformUtils.IsMobile;

        if (tvIsOn)
        {
            // TV is running
            if (distToTV < 3.2f)
            {
                promptCard.style.display = DisplayStyle.Flex;
                promptKeyLabel.text = isMobile ? "[TAP]" : "[E]";
                promptActionLabel.text = "TURN OFF TV";

                if (IsInteractPressed())
                {
                    TurnOffTV();
                }
            }
            else
            {
                promptCard.style.display = DisplayStyle.None;
            }
        }
        else
        {
            // TV is off, objective is to sleep
            if (distToBed < 2.8f)
            {
                promptCard.style.display = DisplayStyle.Flex;
                promptKeyLabel.text = isMobile ? "[TAP]" : "[E]";
                promptActionLabel.text = "GO TO SLEEP";

                if (IsInteractPressed())
                {
                    GoToSleep();
                }
            }
            else
            {
                promptCard.style.display = DisplayStyle.Flex;
                promptKeyLabel.text = "●";
                promptActionLabel.text = "GO TO BED... YOU NEED REST";
            }
        }
    }

    private bool cardTapped = false;

    private void LateUpdate()
    {
        cardTapped = false;
    }

    private bool IsInteractPressed()
    {
        if (cardTapped)
        {
            cardTapped = false;
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

        // Screen black
        if (tvScreenMat != null)
        {
            tvScreenMat.SetColor("_EmissionColor", Color.black);
            tvScreenMat.color = Color.black;
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
        Vector3 bedPillowPos = bedTransform.position + Vector3.up * 0.85f;

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

        // Clean up house model
        if (houseRoot != null) Destroy(houseRoot);
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
