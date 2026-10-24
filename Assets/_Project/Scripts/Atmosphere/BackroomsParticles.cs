using System.Collections;
using UnityEngine;

/// <summary>
/// Atmospheric particle systems for the Backrooms:
/// 1. Floating dust motes / spores in the air around the player.
/// 2. Anomalous shadow miasma / static aura around the entity (Clark).
/// 3. Electrical ceiling light sparks during flicker / blackout events.
/// </summary>
public class BackroomsParticles : MonoBehaviour
{
    [Header("Dust Motes")]
    public bool enableDustMotes = true;
    public int maxDustParticles = 100;
    public float dustRadius = 8f;
    public Color dustColor = new Color(0.95f, 0.92f, 0.75f, 0.45f);

    [Header("Entity Miasma Aura")]
    public bool enableEntityAura = true;
    public Color miasmaColor = new Color(0.05f, 0.04f, 0.08f, 0.55f);

    [Header("Ceiling Sparks")]
    public bool enableSparks = true;

    Transform player;
    Transform villain;

    ParticleSystem dustSystem;
    ParticleSystem miasmaSystem;
    ParticleSystem sparkSystem;

    Material dustMaterial;
    Material miasmaMaterial;
    Material sparkMaterial;

    Texture2D glowTexture;

    public static BackroomsParticles Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        glowTexture = CreateSoftRadialTexture();
    }

    void Start()
    {
        FindActors();

        if (enableDustMotes) SetupDustMotes();
        if (enableEntityAura) SetupEntityAura();
        if (enableSparks) SetupSparks();
    }

    void OnDestroy()
    {
        if (glowTexture != null) Destroy(glowTexture);
        if (dustMaterial != null) Destroy(dustMaterial);
        if (miasmaMaterial != null) Destroy(miasmaMaterial);
        if (sparkMaterial != null) Destroy(sparkMaterial);
    }

    void FindActors()
    {
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
            else
            {
                var cc = FindAnyObjectByType<CharacterController>();
                if (cc != null) player = cc.transform;
            }
        }

        if (villain == null)
        {
            VillainAI ai = FindAnyObjectByType<VillainAI>();
            if (ai != null) villain = ai.transform;
        }
    }

    Texture2D CreateSoftRadialTexture()
    {
        int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "SoftRadialGlow";
        tex.wrapMode = TextureWrapMode.Clamp;

        float center = (size - 1) * 0.5f;
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / center;
                float alpha = Mathf.SmoothStep(1f, 0f, dist);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    Material CreateParticleMaterial(BlendMode blendMode)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Mobile/Particles/Alpha Blended");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
        {
            Debug.LogWarning("BackroomsParticles: No particle/unlit shader available.");
            return null;
        }

        Material mat = new Material(shader);
        mat.mainTexture = glowTexture;

        if (blendMode == BlendMode.Additive)
        {
            mat.SetFloat("_Surface", 1); // Transparent
            mat.SetFloat("_Blend", 1);   // Additive
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        }
        else
        {
            mat.SetFloat("_Surface", 1); // Transparent
            mat.SetFloat("_Blend", 0);   // Alpha
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        mat.SetInt("_ZWrite", 0);
        return mat;
    }

    enum BlendMode { Alpha, Additive }

    void SetupDustMotes()
    {
        if (player == null) return;

        GameObject dustObj = new GameObject("AtmosphericDustMotes");
        dustObj.transform.SetParent(player, false);
        dustObj.transform.localPosition = Vector3.up * 1.2f;

        dustSystem = dustObj.AddComponent<ParticleSystem>();
        ParticleSystemRenderer rend = dustObj.GetComponent<ParticleSystemRenderer>();

        dustMaterial = CreateParticleMaterial(BlendMode.Additive);
        if (dustMaterial != null) rend.material = dustMaterial;

        var main = dustSystem.main;
        main.maxParticles = maxDustParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // stays in world space as player moves
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        main.startColor = dustColor;

        var emission = dustSystem.emission;
        emission.rateOverTime = 14f;

        var shape = dustSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = dustRadius;

        // Gentle Perlin noise turbulence so particles float and drift
        var noise = dustSystem.noise;
        noise.enabled = true;
        noise.strength = 0.18f;
        noise.frequency = 0.35f;
        noise.scrollSpeed = 0.1f;

        // Smooth fade in & out
        var col = dustSystem.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.25f),
                new GradientAlphaKey(1f, 0.75f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        col.color = grad;

        dustSystem.Play();
    }

    void SetupEntityAura()
    {
        if (villain == null) return;

        GameObject auraObj = new GameObject("Entity_AnomalousAura");
        auraObj.transform.SetParent(villain, false);
        auraObj.transform.localPosition = Vector3.up * 1.0f;

        miasmaSystem = auraObj.AddComponent<ParticleSystem>();
        ParticleSystemRenderer rend = auraObj.GetComponent<ParticleSystemRenderer>();

        miasmaMaterial = CreateParticleMaterial(BlendMode.Alpha);
        if (miasmaMaterial != null) rend.material = miasmaMaterial;

        var main = miasmaSystem.main;
        main.maxParticles = 50;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.45f);
        main.startColor = miasmaColor;

        var emission = miasmaSystem.emission;
        emission.rateOverTime = 16f;

        var shape = miasmaSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.7f;

        var noise = miasmaSystem.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.6f;

        var sizeOverLifetime = miasmaSystem.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve curve = new AnimationCurve();
        curve.AddKey(0f, 0.4f);
        curve.AddKey(0.4f, 1.0f);
        curve.AddKey(1f, 0.0f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, curve);

        var col = miasmaSystem.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(new Color(0.1f, 0.05f, 0.15f), 1f) },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.7f, 0.3f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        col.color = grad;

        miasmaSystem.Play();
    }

    void SetupSparks()
    {
        GameObject sparkObj = new GameObject("CeilingSparksSystem");
        sparkObj.transform.SetParent(transform, false);

        sparkSystem = sparkObj.AddComponent<ParticleSystem>();
        ParticleSystemRenderer rend = sparkObj.GetComponent<ParticleSystemRenderer>();

        sparkMaterial = CreateParticleMaterial(BlendMode.Additive);
        if (sparkMaterial != null) rend.material = sparkMaterial;

        var main = sparkSystem.main;
        main.maxParticles = 80;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
        main.startColor = new Color(1f, 0.85f, 0.35f, 1f);
        main.gravityModifier = 3.5f; // sparks fall downward

        var emission = sparkSystem.emission;
        emission.rateOverTime = 0f; // burst on command

        var shape = sparkSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.1f;
        shape.rotation = new Vector3(90f, 0f, 0f); // shoot downwards
    }

    /// <summary>
    /// Spawns a burst of electrical sparks at a flickering or exploding light fixture.
    /// </summary>
    public static void TriggerSparks(Vector3 position, int count = 14)
    {
        if (Instance == null || Instance.sparkSystem == null) return;

        Instance.sparkSystem.transform.position = position;
        Instance.sparkSystem.Emit(count);
    }

    void Update()
    {
        if (player == null || villain == null)
        {
            FindActors();
            if (dustSystem == null && player != null && enableDustMotes) SetupDustMotes();
            if (miasmaSystem == null && villain != null && enableEntityAura) SetupEntityAura();
        }

        // Modulate villain aura based on chase state
        if (miasmaSystem != null && villain != null)
        {
            VillainAI ai = villain.GetComponent<VillainAI>();
            if (ai != null)
            {
                var emission = miasmaSystem.emission;
                emission.rateOverTime = (ai.CurrentState == VillainAI.State.Chase) ? 36f : 16f;
            }
        }
    }
}
