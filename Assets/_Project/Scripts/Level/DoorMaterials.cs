using UnityEngine;

/// <summary>
/// Runtime-generated materials and procedural textures for Backrooms doors and lockers.
/// Renders authentic institutional Backrooms doors (woodgrain/office laminate with brass handle)
/// and vintage industrial metal lockers with scratch speckles.
/// </summary>
public static class DoorMaterials
{
    private static Material doorWood;
    private static Material doorFrame;
    private static Material doorHandle;
    private static Material lockerMetal;
    private static Material lockerInterior;

    public static Material DoorWood => doorWood ??= CreateDoorWoodMaterial();
    public static Material DoorFrame => doorFrame ??= CreateDoorFrameMaterial();
    public static Material DoorHandle => doorHandle ??= CreateDoorHandleMaterial();
    public static Material LockerMetal => lockerMetal ??= CreateLockerMetalMaterial();
    public static Material LockerInterior => lockerInterior ??= CreateLockerInteriorMaterial();

    private static Material CreateDoorWoodMaterial()
    {
        Texture2D tex = MakeDoorWoodTexture();
        Material m = CreateLitMaterial("Door_Institutional_Wood", tex, 0.15f, 0.45f, Color.black);
        return m;
    }

    private static Material CreateDoorFrameMaterial()
    {
        Texture2D tex = MakeDoorFrameTexture();
        Material m = CreateLitMaterial("Door_Frame_Trim", tex, 0.25f, 0.35f, Color.black);
        return m;
    }

    private static Material CreateDoorHandleMaterial()
    {
        Material m = CreateLitMaterial("Door_Handle_Brass", null, 0.88f, 0.72f, Color.black);
        if (m.HasProperty("_BaseColor"))
            m.SetColor("_BaseColor", new Color(0.85f, 0.75f, 0.42f));
        return m;
    }

    private static Material CreateLockerMetalMaterial()
    {
        Texture2D tex = MakeLockerTexture();
        Material m = CreateLitMaterial("Locker_Institutional_Metal", tex, 0.75f, 0.38f, Color.black);
        return m;
    }

    private static Material CreateLockerInteriorMaterial()
    {
        Material m = CreateLitMaterial("Locker_Interior_Shadow", null, 0.3f, 0.15f, new Color(0.015f, 0.015f, 0.015f));
        if (m.HasProperty("_BaseColor"))
            m.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.07f));
        return m;
    }

    private static Material CreateLitMaterial(string matName, Texture2D tex, float metallic, float smoothness, Color emission)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        Material m = new Material(sh) { name = matName };

        if (tex != null)
        {
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        }

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

    private static Texture2D MakeDoorWoodTexture()
    {
        const int size = 256;
        Color32[] px = new Color32[size * size];
        float seed = 29.4f;

        // Liminal office door: slightly aged yellowish-brown veneer with vertical woodgrain fibers
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;

                // Long vertical grain
                float grain = Mathf.PerlinNoise(seed + u * 35f, seed + v * 3.5f) - 0.5f;
                float fine = Mathf.PerlinNoise(seed * 2f + u * 120f, seed * 2f + v * 12f) - 0.5f;
                float broad = Mathf.PerlinNoise(seed * 3f + u * 4f, seed * 3f + v * 4f);
                float grime = Mathf.PerlinNoise(seed * 5f + u * 6f, seed * 5f + v * 6f);

                // Base warm institutional honey-brown / yellowed wood
                float lum = 0.52f + grain * 0.12f + fine * 0.05f + (broad - 0.5f) * 0.08f - (1f - grime) * 0.12f;
                Color baseC = new Color(lum * 1.05f, lum * 0.88f, lum * 0.65f);

                // Subtle edge darkening / panel chamfer look
                float edgeX = Mathf.Min(u, 1f - u);
                float edgeY = Mathf.Min(v, 1f - v);
                float edge = Mathf.Min(edgeX, edgeY);
                baseC *= Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(edge * 14f));

                px[y * size + x] = baseC;
            }
        }

        return Bake(px, size);
    }

    private static Texture2D MakeDoorFrameTexture()
    {
        const int size = 128;
        Color32[] px = new Color32[size * size];
        float seed = 44.1f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;

                float noise = Mathf.PerlinNoise(seed + u * 12f, seed + v * 12f);
                float lum = 0.28f + (noise - 0.5f) * 0.06f;
                px[y * size + x] = new Color(lum * 1.1f, lum * 0.95f, lum * 0.85f);
            }
        }

        return Bake(px, size);
    }

    private static Texture2D MakeLockerTexture()
    {
        const int size = 256;
        Color32[] px = new Color32[size * size];
        float seed = 58.7f;

        // Vintage office locker: pale institutional olive-grey metal with scratches & dirt
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;

                float baseN = Mathf.PerlinNoise(seed + u * 5f, seed + v * 5f);
                float scratches = Mathf.PerlinNoise(seed * 2f + u * 80f, seed * 2f + v * 80f) - 0.5f;
                float grime = Mathf.PerlinNoise(seed * 3f + u * 3.5f, seed * 3f + v * 3.5f);
                float rustMask = Mathf.PerlinNoise(seed * 4f + u * 8f, seed * 4f + v * 8f);

                float lum = 0.44f + (baseN - 0.5f) * 0.08f + scratches * 0.04f - (1f - grime) * 0.15f;
                // Institutional aged olive-grey tint
                Color c = new Color(lum * 0.88f, lum * 0.95f, lum * 0.85f);

                // Rust specks
                float rust = Mathf.Clamp01((rustMask - 0.65f) * 3.5f);
                c = Color.Lerp(c, new Color(0.38f, 0.22f, 0.12f), rust * 0.7f);

                px[y * size + x] = c;
            }
        }

        return Bake(px, size);
    }

    private static Texture2D Bake(Color32[] px, int size)
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
