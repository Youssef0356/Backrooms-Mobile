using System;
using UnityEngine;
using StarterAssets;

/// <summary>
/// Defines a flooded / wet carpet room in Level 0, reproducing the iconic flooded sectors from Inside the Backrooms.
/// In flooded zones:
/// 1. The floor is covered in ankle-deep murky, stagnant water reflecting fluorescent ceiling lights.
/// 2. Footsteps switch from muffled carpet thuds to loud wet splashes.
/// 3. Splashing water radiates excessive noise through walls, alerting Bacteria and Smily Hound from farther away.
/// </summary>
[DisallowMultipleComponent]
public class FloodedZone : MonoBehaviour
{
    private static int _activeZoneCount = 0;
    public static bool IsPlayerInWater { get; private set; }
    public static event Action<bool> OnPlayerWaterStateChanged;

    private static AudioClip[] _splashClips;
    private static Material _cachedWaterMaterial;

    private BoxCollider _triggerCollider;

    private void Awake()
    {
        _triggerCollider = GetComponent<BoxCollider>();
        if (_triggerCollider == null) _triggerCollider = gameObject.AddComponent<BoxCollider>();
        _triggerCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsPlayer(other))
        {
            _activeZoneCount++;
            UpdateWaterState();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (IsPlayer(other))
        {
            _activeZoneCount = Mathf.Max(0, _activeZoneCount - 1);
            UpdateWaterState();
        }
    }

    private void OnDisable()
    {
        // Safety cleanup if object disabled while player inside
        if (_activeZoneCount > 0)
        {
            _activeZoneCount = 0;
            UpdateWaterState();
        }
    }

    private static bool IsPlayer(Collider col)
    {
        if (col == null) return false;
        if (col.CompareTag("Player")) return true;
        if (col.GetComponent<FirstPersonController>() != null) return true;
        if (col.GetComponent<CharacterController>() != null && col.name.ToLowerInvariant().Contains("player")) return true;
        return false;
    }

    private static void UpdateWaterState()
    {
        bool inWater = _activeZoneCount > 0;
        if (inWater != IsPlayerInWater)
        {
            IsPlayerInWater = inWater;
            OnPlayerWaterStateChanged?.Invoke(inWater);
        }
    }

    // -------------------------------------------------------------------------
    // PROCEDURAL WATER SURFACE & WET CARPET BUILDER
    // -------------------------------------------------------------------------

    private static Texture2D _cachedRippleNormal;

    public static Texture2D GetOrCreateRippleNormalMap()
    {
        if (_cachedRippleNormal != null) return _cachedRippleNormal;

        string diskPath = System.IO.Path.Combine(Application.dataPath, "_Project/Art/Environment/Backrooms/Textures/Water_Ripples_Normal.png");
        if (System.IO.File.Exists(diskPath))
        {
            byte[] bytes = System.IO.File.ReadAllBytes(diskPath);
            Texture2D tex = new Texture2D(512, 512, TextureFormat.RGBA32, true, true);
            if (tex.LoadImage(bytes))
            {
                tex.name = "Water_Ripples_Normal_Loaded";
                tex.wrapMode = TextureWrapMode.Repeat;
                _cachedRippleNormal = tex;
                return tex;
            }
        }

        // Calm flat normal map fallback (Color(0.5, 0.5, 1.0))
        int size = 128;
        Texture2D genTex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        genTex.name = "Water_Ripples_Normal_Calm";
        genTex.wrapMode = TextureWrapMode.Repeat;
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color(0.5f, 0.5f, 1f, 1f);
        }
        genTex.SetPixels(pixels);
        genTex.Apply(false);
        _cachedRippleNormal = genTex;
        return genTex;
    }

    private static Texture2D _cachedWetCarpetAlbedo;
    private static Material _cachedWetFloorMat;

    public static Texture2D GetOrCreateWetCarpetAlbedo()
    {
        if (_cachedWetCarpetAlbedo != null) return _cachedWetCarpetAlbedo;

        // Try Resources first
        _cachedWetCarpetAlbedo = Resources.Load<Texture2D>("Textures/Carpet/Wet_Carpet_Albedo");
        if (_cachedWetCarpetAlbedo != null) return _cachedWetCarpetAlbedo;

        // Disk fallback
        string diskPath = System.IO.Path.Combine(Application.dataPath, "_Project/Art/Environment/Backrooms/Textures/Carpet/Wet_Carpet_Albedo.png");
        if (System.IO.File.Exists(diskPath))
        {
            byte[] bytes = System.IO.File.ReadAllBytes(diskPath);
            Texture2D tex = new Texture2D(2048, 2048, TextureFormat.RGB24, true);
            if (tex.LoadImage(bytes))
            {
                tex.name = "Wet_Carpet_Albedo_Loaded";
                tex.wrapMode = TextureWrapMode.Repeat;
                _cachedWetCarpetAlbedo = tex;
                return tex;
            }
        }
        return null;
    }

    public static Material GetOrCreateWetCarpetMaterial(Material baseFloorMat, float width, float height, float floorTextureMeters = 2.0f)
    {
        if (_cachedWetFloorMat != null) return _cachedWetFloorMat;

        Shader shader = Shader.Find("Backrooms/OrganicWetPuddle");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material m = new Material(shader) { name = "M_Flooded_OrganicWetCarpet" };

        Texture2D albedo = GetOrCreateWetCarpetAlbedo();
        if (albedo != null)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo);
        }
        else if (baseFloorMat != null)
        {
            Texture baseTex = baseFloorMat.HasProperty("_BaseMap") ? baseFloorMat.GetTexture("_BaseMap") : baseFloorMat.mainTexture;
            if (baseTex != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", baseTex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", baseTex);
            }
        }

        // Use base floor's normal map if present
        if (baseFloorMat != null && baseFloorMat.HasProperty("_BumpMap") && baseFloorMat.GetTexture("_BumpMap") != null)
        {
            m.SetTexture("_BumpMap", baseFloorMat.GetTexture("_BumpMap"));
            m.EnableKeyword("_NORMALMAP");
        }

        if (m.HasProperty("_FloorTextureMeters")) m.SetFloat("_FloorTextureMeters", floorTextureMeters);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.46f);
        if (m.HasProperty("_DarkenFactor")) m.SetFloat("_DarkenFactor", 0.78f);
        if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", 1.0f);
        if (m.HasProperty("_EdgeFeather")) m.SetFloat("_EdgeFeather", 0.32f);
        if (m.HasProperty("_WobbleAmount")) m.SetFloat("_WobbleAmount", 0.08f);

        _cachedWetFloorMat = m;
        return m;
    }

    public static Material CreateWaterSurfaceMaterial(float width, float height)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material m = new Material(shader) { name = "M_Flooded_WaterSurface" };

        // Very clear translucent water film with subtle warm tint (10% opacity)
        // Shallow puddle water is crystal clear; the wet carpet below is fully visible!
        Color waterColor = new Color(0.35f, 0.32f, 0.22f, 0.10f);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", waterColor);
        else if (m.HasProperty("_Color")) m.SetColor("_Color", waterColor);

        // Glassy wet reflection properties: reflects fluorescent tube lights brightly
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.98f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.02f);

        // Assign calm ripple normal map with very gentle bump scale (NO zebra waves!)
        Texture2D rippleTex = GetOrCreateRippleNormalMap();
        if (rippleTex != null && m.HasProperty("_BumpMap"))
        {
            m.SetTexture("_BumpMap", rippleTex);
            m.EnableKeyword("_NORMALMAP");
            m.SetFloat("_BumpScale", 0.04f);
            m.SetTextureScale("_BumpMap", new Vector2(width * 0.4f, height * 0.4f));
        }

        // Full transparent setup for URP Lit
        m.SetFloat("_Surface", 1f); // Transparent
        m.SetFloat("_Blend", 0f);   // Alpha
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 20;

        return m;
    }

    /// <summary>
    /// Spawns a reflective stagnant water pool plane and trigger covering the room floor.
    /// </summary>
    public static GameObject CreateFloodedRoomZone(Vector3 roomCenter, float widthMeters, float heightMeters, Transform parent, Material baseFloorMat = null, float floorTextureMeters = 2.0f)
    {
        GameObject zoneObj = new GameObject("FloodedZone_Sector");
        zoneObj.transform.SetParent(parent, false);
        zoneObj.transform.position = roomCenter;

        // 1. Organic Wet Carpet Puddle Surface at Y = 0.002m
        // Uses Backrooms/OrganicWetPuddle shader which samples carpet texture in world coordinates
        // and feathers smoothly to 0% opacity at the borders with natural organic seepage contours.
        // There are NO straight box outlines and NO floating slabs.
        GameObject carpetObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        carpetObj.name = "OrganicWetCarpet_FloorQuad";
        carpetObj.transform.SetParent(zoneObj.transform, false);
        carpetObj.transform.localPosition = new Vector3(0f, 0.002f, 0f);
        carpetObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        carpetObj.transform.localScale = new Vector3(widthMeters * 1.02f, heightMeters * 1.02f, 1f);

        Collider carpetCol = carpetObj.GetComponent<Collider>();
        if (carpetCol != null) Destroy(carpetCol);

        MeshRenderer carpetMR = carpetObj.GetComponent<MeshRenderer>();
        if (carpetMR != null)
        {
            carpetMR.sharedMaterial = GetOrCreateWetCarpetMaterial(baseFloorMat, widthMeters, heightMeters, floorTextureMeters);
            carpetMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            carpetMR.receiveShadows = true;
        }

        // 2. Room-wide Trigger Collider for player detection
        BoxCollider box = zoneObj.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = new Vector3(0f, 0.45f, 0f);
        box.size = new Vector3(widthMeters * 0.96f, 0.90f, heightMeters * 0.96f);

        zoneObj.AddComponent<FloodedZone>();

        // 4. Subtle spatial water droplet audio source
        AudioSource dripSource = zoneObj.AddComponent<AudioSource>();
        dripSource.playOnAwake = true;
        dripSource.loop = true;
        dripSource.spatialBlend = 0.85f;
        dripSource.minDistance = 2f;
        dripSource.maxDistance = 14f;
        dripSource.volume = 0.28f * GameSettings.MasterVolume;
        dripSource.clip = CreateAmbientDripSFX();
        dripSource.Play();

        return zoneObj;
    }

    // -------------------------------------------------------------------------
    // PROCEDURAL AUDIO SYNTHESIZERS (SPLASH FOOTSTEPS & DRIPPING)
    // -------------------------------------------------------------------------

    public static AudioClip[] GetOrCreateSplashSFX()
    {
        if (_splashClips != null && _splashClips.Length > 0) return _splashClips;

        // Load authentic Freesound shallow water / wet carpet footstep recordings
        AudioClip[] wetFootsteps = Resources.LoadAll<AudioClip>("SFX/Footsteps/Wet");
        if (wetFootsteps != null && wetFootsteps.Length > 0)
        {
            _splashClips = wetFootsteps;
            return _splashClips;
        }

        _splashClips = new AudioClip[4];
        for (int i = 0; i < 4; i++)
        {
            _splashClips[i] = GenerateSplashFootstepClip(i);
        }

        return _splashClips;
    }

    private static AudioClip GenerateSplashFootstepClip(int variant)
    {
        int rate = 44100;
        float duration = 0.38f + variant * 0.04f;
        int count = (int)(rate * duration);

        AudioClip clip = AudioClip.Create($"WaterSplash_{variant + 1}", count, 1, rate, false);
        float[] samples = new float[count];

        float pitchBase = 180f + variant * 35f;

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / rate;
            float progress = t / duration;

            // 1. Initial heavy foot strike into shallow water (low resonant slosh)
            float lowImpact = Mathf.Sin(2f * Mathf.PI * pitchBase * t) * Mathf.Exp(-t * 22f) * 0.65f;

            // 2. High-frequency turbulent water spray & droplet scattering (800Hz - 3200Hz)
            float sprayNoise = (UnityEngine.Random.value * 2f - 1f) * Mathf.Exp(-t * 14f) * 0.50f;

            // 3. Sloshing liquid ripple resonance
            float ripple = Mathf.Sin(2f * Mathf.PI * (pitchBase * 1.8f) * t) * Mathf.Exp(-t * 9f) * 0.25f;

            float env = Mathf.Sin(Mathf.Clamp01(progress * 1.5f) * Mathf.PI);
            samples[i] = Mathf.Clamp((lowImpact + sprayNoise + ripple) * env, -1f, 1f);
        }

        clip.SetData(samples, 0);
        return clip;
    }

    private static AudioClip CreateAmbientDripSFX()
    {
        int rate = 44100;
        float duration = 3.2f;
        int count = (int)(rate * duration);

        AudioClip clip = AudioClip.Create("FloodedAmbientDrip", count, 1, rate, false);
        float[] samples = new float[count];

        float[] dripTimes = new float[] { 0.3f, 1.1f, 1.85f, 2.7f };
        float[] dripFreqs = new float[] { 1450f, 1850f, 1250f, 1650f };

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / rate;
            float s = 0f;

            for (int d = 0; d < dripTimes.Length; d++)
            {
                float dt = t - dripTimes[d];
                if (dt >= 0f && dt < 0.12f)
                {
                    float tone = Mathf.Sin(2f * Mathf.PI * dripFreqs[d] * dt) * Mathf.Exp(-dt * 45f);
                    s += tone * 0.35f;
                }
            }

            samples[i] = Mathf.Clamp(s, -1f, 1f);
        }

        clip.SetData(samples, 0);
        return clip;
    }
}

/// <summary>
/// Gently scrolls fluid ripple normal map offset to simulate living water movement.
/// </summary>
public class FloodedWaterMotion : MonoBehaviour
{
    private MeshRenderer _mr;
    private Material _mat;
    private Vector2 _offset;

    private void Awake()
    {
        _mr = GetComponent<MeshRenderer>();
        if (_mr != null) _mat = _mr.material;
    }

    private void Update()
    {
        if (_mat != null)
        {
            _offset.x = (Time.time * 0.003f) % 1.0f;
            _offset.y = (Time.time * 0.002f) % 1.0f;
            if (_mat.HasProperty("_BumpMap")) _mat.SetTextureOffset("_BumpMap", _offset);
            if (_mat.HasProperty("_BaseMap")) _mat.SetTextureOffset("_BaseMap", _offset * 0.2f);
        }
    }
}
