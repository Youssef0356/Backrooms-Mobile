using UnityEngine;

/// <summary>
/// Controls the atmospheric lighting, surreal sky tint, fog, and music box ambience
/// characteristic of Backrooms Level 94 ("Motion").
/// </summary>
public class Level94Atmosphere : MonoBehaviour
{
    [Header("Lighting")]
    [SerializeField] Color sunColor = new Color(1.0f, 0.96f, 0.90f);
    [SerializeField] float sunIntensity = 1.35f;
    [SerializeField] Vector3 sunAngles = new Vector3(38f, -40f, 0f);

    [Header("Fog & Horizon (Linear, Crisp Foreground, Ethereal Blue Horizon)")]
    [SerializeField] bool enableFog = true;
    [SerializeField] Color fogColor = new Color(0.68f, 0.82f, 0.98f); // Pure liminal atmospheric blue
    [SerializeField] float fogStartDistance = 120f;
    [SerializeField] float fogEndDistance = 650f;

    [Header("Liminal Sky")]
    [SerializeField] Material skyboxMaterial;
    [SerializeField] Color skyTint = new Color(0.20f, 0.55f, 0.95f); // Rich surreal sky blue
    [SerializeField] Color groundTint = new Color(0.60f, 0.75f, 0.92f); // Atmospheric soft blue horizon (no green mud)
    [SerializeField] float atmosphereThickness = 0.78f;
    [SerializeField] float exposure = 1.20f;

    [Header("Audio")]
    [SerializeField] AudioClip musicBoxClip;
    [Range(0f, 1f)]
    [SerializeField] float musicVolume = 0.6f;

    private AudioSource audioSource;
    private Light directionalSun;

    void Awake()
    {
        SetupLighting();
        SetupSkybox();
        SetupFog();
        SetupAudio();
        HealPlayerRig();
    }

    void Start()
    {
        HealPlayerRig();
    }

    private void HealPlayerRig()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) player = GameObject.Find("PlayerCapsule");
        if (player == null) return;

        // Ensure PlayerCameraRoot is a direct child of PlayerCapsule at (0, 1.375, 0)
        Transform cameraRoot = player.transform.Find("PlayerCameraRoot");
        if (cameraRoot == null)
        {
            GameObject existingRoot = GameObject.Find("PlayerCameraRoot");
            if (existingRoot != null)
            {
                cameraRoot = existingRoot.transform;
                cameraRoot.SetParent(player.transform, false);
            }
        }

        if (cameraRoot != null)
        {
            cameraRoot.localPosition = new Vector3(0f, 1.375f, 0f);
            cameraRoot.localRotation = Quaternion.identity;
        }

        // Tune CharacterController for smooth hill climbing, curb stepping and standard doorway passage (0.44m diameter, 1.55m height)
        var cc = player.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.radius = 0.22f;
            cc.height = 1.55f;
            cc.center = new Vector3(0f, 0.78f, 0f);
            cc.slopeLimit = 60.0f;
            cc.stepOffset = 0.45f;
            cc.skinWidth = 0.025f;
            cc.minMoveDistance = 0.0f;
        }

        // Configure FirstPersonController ground detection
        var fpc = player.GetComponent<StarterAssets.FirstPersonController>();
        if (fpc != null)
        {
            if (cameraRoot != null) fpc.CinemachineCameraTarget = cameraRoot.gameObject;
            fpc.GroundLayers = ~(1 << 8); // Detect everything except Player
        }

        // Fix Cinemachine virtual camera target
        var vCam = Object.FindAnyObjectByType<Unity.Cinemachine.CinemachineCamera>();
        if (vCam != null && cameraRoot != null)
        {
            vCam.Target.TrackingTarget = cameraRoot;
            vCam.Target.LookAtTarget = null;
            vCam.Target.CustomLookAtTarget = false;
        }

        // Ensure Realistic Head Bob & Running Head Shake
        if (cameraRoot != null && cameraRoot.GetComponent<PlayerHeadBobMotion>() == null)
        {
            cameraRoot.gameObject.AddComponent<PlayerHeadBobMotion>();
        }

        // Ensure PlayerStamina & Exhaustion Blinking Effect
        if (player.GetComponent<StarterAssets.PlayerStamina>() == null)
        {
            player.AddComponent<StarterAssets.PlayerStamina>();
        }
        if (player.GetComponent<PlayerExhaustionBlinkEffect>() == null)
        {
            player.AddComponent<PlayerExhaustionBlinkEffect>();
        }

        // Ensure Stamina HUD on Director
        if (GetComponent<PlayerStaminaHUD>() == null)
        {
            gameObject.AddComponent<PlayerStaminaHUD>();
        }

        // Ensure 1-minute (60 seconds) Day/Night Cycle on Director
        var dnc = GetComponent<Level94DayNightCycle>();
        if (dnc == null)
        {
            dnc = gameObject.AddComponent<Level94DayNightCycle>();
            dnc.cycleDuration = 60f;
            dnc.timeOfDay = 0.20f;
        }
        else
        {
            dnc.cycleDuration = 60f;
        }

        // Ensure Expansive Horizon Landscape (Fixes floating platform perception)
        if (GetComponent<Level94HorizonLandscape>() == null)
        {
            gameObject.AddComponent<Level94HorizonLandscape>();
        }
    }

    [ContextMenu("Apply Atmosphere Settings")]
    public void ApplySettings()
    {
        SetupLighting();
        SetupSkybox();
        SetupFog();
    }

    void SetupLighting()
    {
        // Find existing directional light or create one
        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var l in lights)
        {
            if (l.type == LightType.Directional)
            {
                directionalSun = l;
                break;
            }
        }

        if (directionalSun == null)
        {
            GameObject sunGo = new GameObject("Level94_Sun");
            directionalSun = sunGo.AddComponent<Light>();
            directionalSun.type = LightType.Directional;
        }

        directionalSun.color = sunColor;
        directionalSun.intensity = sunIntensity;
        directionalSun.transform.rotation = Quaternion.Euler(sunAngles);
        directionalSun.shadows = LightShadows.Soft;

        // Liminal ambient lighting (warm sun / pure blue sky / neutral ground fill)
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.70f, 0.84f, 0.98f);
        RenderSettings.ambientEquatorColor = new Color(0.82f, 0.86f, 0.92f);
        RenderSettings.ambientGroundColor = new Color(0.40f, 0.46f, 0.52f);
    }

    void SetupSkybox()
    {
        if (skyboxMaterial == null)
        {
            Shader liminalShader = Shader.Find("Custom/LiminalSkybox") ?? Shader.Find("Skybox/Procedural");
            if (liminalShader != null)
            {
                skyboxMaterial = new Material(liminalShader);
                skyboxMaterial.name = "Level94_LiminalSky";
            }
        }

        if (skyboxMaterial != null)
        {
            if (skyboxMaterial.HasProperty("_TopColor")) skyboxMaterial.SetColor("_TopColor", skyTint);
            if (skyboxMaterial.HasProperty("_HorizonColor")) skyboxMaterial.SetColor("_HorizonColor", fogColor);
            if (skyboxMaterial.HasProperty("_BottomColor")) skyboxMaterial.SetColor("_BottomColor", groundTint);

            if (skyboxMaterial.HasProperty("_SkyTint")) skyboxMaterial.SetColor("_SkyTint", skyTint);
            if (skyboxMaterial.HasProperty("_GroundColor")) skyboxMaterial.SetColor("_GroundColor", groundTint);
            if (skyboxMaterial.HasProperty("_AtmosphereThickness")) skyboxMaterial.SetFloat("_AtmosphereThickness", atmosphereThickness);
            if (skyboxMaterial.HasProperty("_Exposure")) skyboxMaterial.SetFloat("_Exposure", exposure);

            RenderSettings.skybox = skyboxMaterial;
            DynamicGI.UpdateEnvironment();
        }
    }

    void SetupFog()
    {
        RenderSettings.fog = enableFog;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = fogStartDistance;
        RenderSettings.fogEndDistance = fogEndDistance;
    }

    void SetupAudio()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.playOnAwake = false;
        }

        if (musicBoxClip == null)
        {
            musicBoxClip = Resources.Load<AudioClip>("SFX/juanjo_ sound - paradise");
            if (musicBoxClip == null)
                musicBoxClip = Resources.Load<AudioClip>("SFX/juanjo_ sound - Pseudoliminality");
        }

        if (musicBoxClip != null && !audioSource.isPlaying)
        {
            audioSource.clip = musicBoxClip;
            audioSource.volume = musicVolume * GameSettings.MusicVolume;
            audioSource.Play();
        }
    }

    void Update()
    {
        if (audioSource != null)
        {
            audioSource.volume = musicVolume * GameSettings.MusicVolume;
        }
    }
}
