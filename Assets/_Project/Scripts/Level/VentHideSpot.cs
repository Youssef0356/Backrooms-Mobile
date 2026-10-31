using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// A crawl-through wall vent: a louvered grille set into a hole in the wall, with a dark sheet-metal
/// duct pocket behind it. The player can crawl in while the villain hunts him, and watch the corridor
/// through the grille bars until the creature gives up.
///
/// The vent builds its own geometry (frame, hinged grille, duct) from a handful of boxes that are
/// merged into three meshes, so it costs three draw calls and zero physics colliders except the grille.
///
/// Local space: origin = centre of the hole on the ROOM-SIDE face of the wall at floor height,
/// +Z points INTO the wall (towards the duct), +X is the player's right when facing the wall.
/// </summary>
public class VentHideSpot : MonoBehaviour
{
    public static readonly List<VentHideSpot> All = new List<VentHideSpot>();

    // ---------------- Dimensions (metres) ----------------
    public const float HoleWidth = 0.90f;
    public const float HoleBottom = 0.12f;
    public const float HoleTop = 0.87f;
    public const float DuctHalfWidth = 0.55f;
    public const float DuctTop = 0.92f;
    public const float FlapOpenAngle = 100f;

    float wallThickness;
    float ductDepth;

    Transform flapPivot;
    BoxCollider flapCollider;
    AudioSource audioSource;

    public float WallThickness => wallThickness;

    /// <summary>World position a few steps in front of the grille where the player kneels before crawling in.</summary>
    public Vector3 FrontPoint => transform.TransformPoint(new Vector3(0f, 0f, -0.85f));

    /// <summary>World position inside the duct where the player stays hidden.</summary>
    public Vector3 HidePoint => transform.TransformPoint(new Vector3(0f, 0f, wallThickness + Mathf.Min(0.75f, ductDepth - 0.5f)));

    /// <summary>World position the villain stares at while waiting for the player.</summary>
    public Vector3 GrillePoint => transform.TransformPoint(new Vector3(0f, 0.5f, 0f));

    /// <summary>Unit vector from the room into the duct (flattened).</summary>
    public Vector3 IntoWall
    {
        get
        {
            Vector3 f = transform.forward;
            f.y = 0f;
            return f.normalized;
        }
    }

    /// <summary>Height (above floor) of the player's eyes while crouching/crawling.</summary>
    public const float CrawlEyeHeight = 0.52f;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // =====================================================================
    // Construction
    // =====================================================================

    /// <summary>Builds a vent at <paramref name="innerFacePos"/> facing into the wall along <paramref name="dirIntoWall"/>.</summary>
    public static VentHideSpot Create(Transform parent, Vector3 innerFacePos, Vector3 dirIntoWall,
                                      float wallThickness, float ductDepth, Material metalOverride, Material ductOverride)
    {
        GameObject go = new GameObject("Vent");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(innerFacePos.x, 0f, innerFacePos.z);
        go.transform.rotation = Quaternion.LookRotation(dirIntoWall, Vector3.up);

        VentHideSpot spot = go.AddComponent<VentHideSpot>();
        spot.wallThickness = wallThickness;
        spot.ductDepth = ductDepth;
        spot.Build(metalOverride != null ? metalOverride : VentMaterials.Metal,
                   ductOverride != null ? ductOverride : VentMaterials.Duct);

        // Keep the NavMesh clear of the vent parts (the grille collider would otherwise carve it)
        NavMeshModifier mod = go.AddComponent<NavMeshModifier>();
        mod.ignoreFromBuild = true;

        return spot;
    }

    static Mesh cubeMesh;

    static Mesh CubeMesh
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

    static void AddBox(List<CombineInstance> list, Vector3 center, Vector3 size)
    {
        CombineInstance ci = new CombineInstance();
        ci.mesh = CubeMesh;
        ci.transform = Matrix4x4.TRS(center, Quaternion.identity, size);
        list.Add(ci);
    }

    GameObject MakeMeshObject(string objName, Transform parent, List<CombineInstance> parts, Material mat)
    {
        GameObject g = new GameObject(objName);
        g.transform.SetParent(parent, false);

        Mesh m = new Mesh();
        m.name = objName;
        m.CombineMeshes(parts.ToArray(), true, true);
        m.RecalculateBounds();

        g.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return g;
    }

    void Build(Material metal, Material duct)
    {
        float t = wallThickness;
        float halfW = HoleWidth * 0.5f;
        float holeH = HoleTop - HoleBottom;

        // ---------- Static metal: frame on the room face + sleeve lining the hole ----------
        List<CombineInstance> metalParts = new List<CombineInstance>();

        const float bar = 0.07f;
        float frameZ = -0.012f;
        AddBox(metalParts, new Vector3(0f, HoleTop + bar * 0.5f, frameZ), new Vector3(HoleWidth + bar * 2f, bar, 0.03f));       // top
        AddBox(metalParts, new Vector3(0f, HoleBottom - bar * 0.5f, frameZ), new Vector3(HoleWidth + bar * 2f, bar, 0.03f));    // bottom
        float sideH = holeH + bar * 2f;
        float sideY = HoleBottom - bar + sideH * 0.5f;
        AddBox(metalParts, new Vector3(-(halfW + bar * 0.5f), sideY, frameZ), new Vector3(bar, sideH, 0.03f));                  // left
        AddBox(metalParts, new Vector3((halfW + bar * 0.5f), sideY, frameZ), new Vector3(bar, sideH, 0.03f));                   // right

        // sleeve through the wall thickness
        AddBox(metalParts, new Vector3(0f, HoleBottom - 0.005f, t * 0.5f), new Vector3(HoleWidth, 0.012f, t + 0.02f));
        AddBox(metalParts, new Vector3(0f, HoleTop + 0.005f, t * 0.5f), new Vector3(HoleWidth, 0.012f, t + 0.02f));
        AddBox(metalParts, new Vector3(-halfW - 0.005f, (HoleBottom + HoleTop) * 0.5f, t * 0.5f), new Vector3(0.012f, holeH, t + 0.02f));
        AddBox(metalParts, new Vector3(halfW + 0.005f, (HoleBottom + HoleTop) * 0.5f, t * 0.5f), new Vector3(0.012f, holeH, t + 0.02f));

        MakeMeshObject("VentFrame", transform, metalParts, metal);

        // ---------- Static duct pocket behind the wall ----------
        List<CombineInstance> ductParts = new List<CombineInstance>();
        float z0 = t;
        float ductH = DuctTop - HoleBottom;
        float cy = HoleBottom + ductH * 0.5f;
        float cz = z0 + ductDepth * 0.5f;
        float w = DuctHalfWidth * 2f + 0.06f;
        const float skin = 0.03f;

        AddBox(ductParts, new Vector3(0f, HoleBottom - skin * 0.5f, cz), new Vector3(w, skin, ductDepth));                      // floor
        AddBox(ductParts, new Vector3(0f, DuctTop + skin * 0.5f, cz), new Vector3(w, skin, ductDepth));                         // ceiling
        AddBox(ductParts, new Vector3(-(DuctHalfWidth + skin * 0.5f), cy, cz), new Vector3(skin, ductH, ductDepth));            // left
        AddBox(ductParts, new Vector3((DuctHalfWidth + skin * 0.5f), cy, cz), new Vector3(skin, ductH, ductDepth));             // right
        AddBox(ductParts, new Vector3(0f, cy, z0 + ductDepth + skin * 0.5f), new Vector3(w, ductH + skin * 2f, skin));          // back

        // stiffening ribs / seams so the duct reads as riveted sheet metal
        for (int i = 0; i < 3; i++)
        {
            float rz = z0 + ductDepth * (0.22f + 0.28f * i);
            float rib = 0.035f;
            AddBox(ductParts, new Vector3(0f, HoleBottom + rib * 0.5f, rz), new Vector3(DuctHalfWidth * 2f, rib, 0.05f));
            AddBox(ductParts, new Vector3(0f, DuctTop - rib * 0.5f, rz), new Vector3(DuctHalfWidth * 2f, rib, 0.05f));
            AddBox(ductParts, new Vector3(-(DuctHalfWidth - rib * 0.5f), cy, rz), new Vector3(rib, ductH, 0.05f));
            AddBox(ductParts, new Vector3((DuctHalfWidth - rib * 0.5f), cy, rz), new Vector3(rib, ductH, 0.05f));
        }

        MakeMeshObject("VentDuct", transform, ductParts, duct);

        // ---------- Hinged grille (top hinge, swings out into the room) ----------
        GameObject pivotGo = new GameObject("VentGrilleHinge");
        flapPivot = pivotGo.transform;
        flapPivot.SetParent(transform, false);
        flapPivot.localPosition = new Vector3(0f, HoleTop + 0.005f, -0.030f);

        List<CombineInstance> flapParts = new List<CombineInstance>();
        float fh = holeH + 0.01f;                      // grille height, hangs down from hinge
        float fw = HoleWidth - 0.01f;
        const float rail = 0.04f;
        AddBox(flapParts, new Vector3(0f, -rail * 0.5f, 0f), new Vector3(fw, rail, 0.02f));                      // top rail
        AddBox(flapParts, new Vector3(0f, -fh + rail * 0.5f, 0f), new Vector3(fw, rail, 0.02f));                 // bottom rail
        AddBox(flapParts, new Vector3(-(fw * 0.5f - rail * 0.5f), -fh * 0.5f, 0f), new Vector3(rail, fh, 0.02f));// left
        AddBox(flapParts, new Vector3((fw * 0.5f - rail * 0.5f), -fh * 0.5f, 0f), new Vector3(rail, fh, 0.02f)); // right
        AddBox(flapParts, new Vector3(0f, -fh * 0.34f, 0f), new Vector3(fw, 0.016f, 0.014f));                    // cross bars
        AddBox(flapParts, new Vector3(0f, -fh * 0.67f, 0f), new Vector3(fw, 0.016f, 0.014f));

        const int bars = 11;                           // thin vertical bars: ~25% occlusion so you can see out
        float inner = fw - rail * 2f;
        for (int i = 0; i < bars; i++)
        {
            float x = -inner * 0.5f + inner * (i + 0.5f) / bars;
            AddBox(flapParts, new Vector3(x, -fh * 0.5f, 0.002f), new Vector3(0.013f, fh - rail * 2f, 0.013f));
        }

        // tiny hinge barrel + screw heads
        AddBox(flapParts, new Vector3(0f, 0.004f, 0.0f), new Vector3(fw + 0.02f, 0.014f, 0.024f));
        for (int i = 0; i < 4; i++)
        {
            float sx = (i < 2 ? -1f : 1f) * (fw * 0.5f - rail * 0.5f);
            float sy = (i % 2 == 0 ? -0.08f : -fh + 0.08f);
            AddBox(flapParts, new Vector3(sx, sy, -0.012f), new Vector3(0.018f, 0.018f, 0.008f));
        }

        MakeMeshObject("VentGrille", flapPivot, flapParts, metal);

        flapCollider = pivotGo.AddComponent<BoxCollider>();
        flapCollider.center = new Vector3(0f, -fh * 0.5f, 0f);
        flapCollider.size = new Vector3(fw, fh, 0.05f);

        // ---------- Audio ----------
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0.7f;
        audioSource.minDistance = 1.5f;
        audioSource.maxDistance = 14f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;

        SetFlapAngle(0f);
    }

    // =====================================================================
    // Animation / state
    // =====================================================================

    /// <summary>Swings the grille. 0 = closed, <see cref="FlapOpenAngle"/> = fully open.</summary>
    public void SetFlapAngle(float degrees)
    {
        if (flapPivot == null) return;
        flapPivot.localRotation = Quaternion.Euler(degrees, 0f, 0f);
        // The closed grille blocks the hole physically; open it up so the player can pass
        if (flapCollider != null) flapCollider.enabled = degrees < 8f;
    }

    public void PlayCreak(float volume = 0.28f)
    {
        Play(VentAudio.Creak, volume, Random.Range(0.95f, 1.05f));
    }

    public void PlayThunk(float volume = 0.32f)
    {
        Play(VentAudio.Thunk, volume, Random.Range(0.96f, 1.04f));
    }

    void Play(AudioClip clip, float volume, float pitch)
    {
        if (audioSource == null || clip == null) return;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip, volume);
    }

    /// <summary>Returns the closest vent to a position (flat distance to its front point), or null.</summary>
    public static VentHideSpot FindNearest(Vector3 position, float maxDistance)
    {
        VentHideSpot best = null;
        float bestSqr = maxDistance * maxDistance;
        for (int i = 0; i < All.Count; i++)
        {
            VentHideSpot v = All[i];
            if (v == null) continue;
            Vector3 d = v.FrontPoint - position;
            d.y = 0f;
            float s = d.sqrMagnitude;
            if (s < bestSqr)
            {
                bestSqr = s;
                best = v;
            }
        }
        return best;
    }
}

/// <summary>Runtime-generated sheet-metal materials, so vents look right without any imported art.</summary>
public static class VentMaterials
{
    static Material metal;
    static Material duct;

    /// <summary>Brushed galvanised steel with grime and light rust speckle.</summary>
    public static Material Metal
    {
        get
        {
            if (metal == null) metal = CreateMaterial("Vent Galvanised Metal", MakeMetalTexture(), 0.82f, 0.42f, Color.black);
            return metal;
        }
    }

    /// <summary>Dark, rust-streaked duct interior with a barely-there emissive so it is never pure black.</summary>
    public static Material Duct
    {
        get
        {
            if (duct == null) duct = CreateMaterial("Vent Duct Interior", MakeDuctTexture(), 0.6f, 0.22f, new Color(0.030f, 0.034f, 0.034f));
            return duct;
        }
    }

    static Material CreateMaterial(string matName, Texture2D tex, float metallic, float smoothness, Color emission)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        Material m = new Material(sh);
        m.name = matName;
        m.mainTexture = tex;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        return m;
    }

    static Texture2D MakeMetalTexture()
    {
        const int size = 256;
        Color32[] px = new Color32[size * size];
        float seed = 17.3f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;

                // Brushed steel: long horizontal streaks
                float brushed = Mathf.PerlinNoise(seed + u * 4f, seed + v * 160f) - 0.5f;
                float fine = Mathf.PerlinNoise(seed + u * 60f, seed + v * 60f) - 0.5f;
                float grime = Mathf.PerlinNoise(seed * 2f + u * 3f, seed * 2f + v * 3f);       // large dirty blotches
                float rustMask = Mathf.PerlinNoise(seed * 3f + u * 9f, seed * 3f + v * 9f);
                float speckle = Mathf.PerlinNoise(seed * 5f + u * 90f, seed * 5f + v * 90f);

                float lum = 0.60f + brushed * 0.16f + fine * 0.06f - (1f - grime) * 0.22f;
                Color c = new Color(lum * 0.97f, lum * 1.0f, lum * 0.98f);

                // rust speckle where the blotch mask is high
                float rust = Mathf.Clamp01((rustMask - 0.62f) * 4f) * Mathf.Clamp01((speckle - 0.35f) * 3f);
                c = Color.Lerp(c, new Color(0.42f, 0.22f, 0.11f), rust * 0.75f);

                // dark edge grime so tiled seams are less obvious
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                c *= Mathf.Lerp(0.82f, 1f, Mathf.Clamp01(edge * 12f));

                px[y * size + x] = c;
            }
        }
        return Bake(px, size);
    }

    static Texture2D MakeDuctTexture()
    {
        const int size = 256;
        Color32[] px = new Color32[size * size];
        float seed = 41.7f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;

                float baseN = Mathf.PerlinNoise(seed + u * 5f, seed + v * 5f);
                float streak = Mathf.PerlinNoise(seed * 2f + u * 24f, seed * 2f + v * 2.2f);   // vertical drip streaks
                float soot = Mathf.PerlinNoise(seed * 3f + u * 2.5f, seed * 3f + v * 2.5f);
                float speckle = Mathf.PerlinNoise(seed * 4f + u * 80f, seed * 4f + v * 80f) - 0.5f;

                float lum = 0.26f + (baseN - 0.5f) * 0.10f + speckle * 0.06f - soot * 0.08f;
                Color c = new Color(lum, lum * 1.02f, lum * 1.02f);

                float rust = Mathf.Clamp01((streak - 0.55f) * 3.2f);
                c = Color.Lerp(c, new Color(0.36f, 0.19f, 0.09f), rust * 0.6f);

                px[y * size + x] = c;
            }
        }
        return Bake(px, size);
    }

    static Texture2D Bake(Color32[] px, int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.anisoLevel = 2;
        tex.SetPixels32(px);
        tex.Apply(true, false);
        return tex;
    }
}

/// <summary>Procedurally synthesised vent sounds (grille creak and the dull clang of it shutting).</summary>
public static class VentAudio
{
    const int Rate = 22050;
    static AudioClip creak;
    static AudioClip thunk;

    public static AudioClip Creak
    {
        get
        {
            if (creak == null) creak = Resources.Load<AudioClip>("SFX/Vents/Vent_Creak") ?? MakeCreak();
            return creak;
        }
    }

    public static AudioClip Thunk
    {
        get
        {
            if (thunk == null) thunk = Resources.Load<AudioClip>("SFX/Vents/Vent_Thunk") ?? MakeThunk();
            return thunk;
        }
    }

    static AudioClip MakeCreak()
    {
        // Gentle, soft brushed metal sliding friction: zero harsh synthesizer waves
        float len = 0.55f;
        int n = (int)(Rate * len);
        float[] d = new float[n];
        System.Random rng = new System.Random(42);
        float lp = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float progress = t / len;

            // Soft low-pass filtered metal brush
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * 0.12f;

            // Subtle warm metallic body resonance
            float resonance = Mathf.Sin(2f * Mathf.PI * 340f * t) * 0.15f +
                              Mathf.Sin(2f * Mathf.PI * 520f * t) * 0.08f;

            float env = Mathf.Sin(Mathf.PI * progress);
            d[i] = (lp * 0.7f + resonance) * env * 0.45f;
        }

        AudioClip c = AudioClip.Create("VentCreak", n, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }

    static AudioClip MakeThunk()
    {
        // Quiet, muted hollow tin latch click: soft damped tap
        float len = 0.28f;
        int n = (int)(Rate * len);
        float[] d = new float[n];
        System.Random rng = new System.Random(88);

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;

            // Damped hollow duct body thump
            float thump = Mathf.Sin(2f * Mathf.PI * 85f * t) * Mathf.Exp(-t * 24f) * 0.5f;

            // Soft metallic latch contact
            float latch = Mathf.Sin(2f * Mathf.PI * 460f * t) * Mathf.Exp(-t * 40f) * 0.25f;

            // Quick initial tactile click
            float click = (t < 0.015f) ? (float)(rng.NextDouble() * 2.0 - 1.0) * (1f - t / 0.015f) * 0.2f : 0f;

            d[i] = Mathf.Clamp(thump + latch + click, -1f, 1f) * 0.45f;
        }

        AudioClip c = AudioClip.Create("VentThunk", n, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }
}
