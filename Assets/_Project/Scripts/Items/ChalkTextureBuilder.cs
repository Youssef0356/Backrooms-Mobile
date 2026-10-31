using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally generates authentic chalk mark textures (X, Arrow, Checkmark) and audio
/// with realistic powdery edges, chalk dust opacity falloff, and lime-wash plaster textures.
/// </summary>
public static class ChalkTextureBuilder
{
    public enum MarkType
    {
        Cross,      // X (Danger / Dead End)
        Arrow,      // -> (Safe Route / Exit Direction)
        Checkmark   // V (Cleared / Visited Room)
    }

    private static readonly Dictionary<MarkType, Texture2D> _cachedTextures = new Dictionary<MarkType, Texture2D>();
    private static readonly Dictionary<MarkType, Material> _cachedMaterials = new Dictionary<MarkType, Material>();
    private static AudioClip _cachedScratchSFX;

    public static Material GetOrCreateChalkMaterial(MarkType type)
    {
        if (_cachedMaterials.TryGetValue(type, out Material mat) && mat != null)
            return mat;

        Texture2D tex = GetOrCreateChalkTexture(type);

        Shader shader = Shader.Find("Backrooms/ChalkDecal");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Mobile/Particles/Alpha Blended");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");

        Material m = new Material(shader) { name = $"M_Chalk_{type}" };

        Color chalkTint = new Color(0.96f, 0.95f, 0.90f, 0.96f);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", chalkTint);
        if (m.HasProperty("_Color")) m.SetColor("_Color", chalkTint);

        // Configure transparency for URP Unlit
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f); // Transparent
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);     // Alpha blend
        if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);       // Two-sided (Cull Off) - NEVER culled!
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 100;

        _cachedMaterials[type] = m;
        return m;
    }

    public static Texture2D GetOrCreateChalkTexture(MarkType type)
    {
        if (_cachedTextures.TryGetValue(type, out Texture2D tex) && tex != null)
            return tex;

        int size = 256;
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = $"Tex_Chalk_{type}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;
        t.SetPixels(pixels);

        Color chalkWhite = new Color(0.96f, 0.95f, 0.92f, 1.0f);

        switch (type)
        {
            case MarkType.Cross:
                DrawChalkStroke(t, 45, 45, 210, 210, chalkWhite, 14);
                DrawChalkStroke(t, 45, 210, 210, 45, chalkWhite, 14);
                break;

            case MarkType.Arrow:
                // Main stem
                DrawChalkStroke(t, 45, 128, 205, 128, chalkWhite, 14);
                // Arrowhead
                DrawChalkStroke(t, 205, 128, 140, 185, chalkWhite, 14);
                DrawChalkStroke(t, 205, 128, 140, 71, chalkWhite, 14);
                break;

            case MarkType.Checkmark:
                DrawChalkStroke(t, 45, 120, 105, 55, chalkWhite, 14);
                DrawChalkStroke(t, 105, 55, 215, 205, chalkWhite, 15);
                break;
        }

        t.Apply(false, true);
        _cachedTextures[type] = t;
        return t;
    }

    private static void DrawChalkStroke(Texture2D tex, int x0, int y0, int x1, int y1, Color col, int thickness)
    {
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int steps = Mathf.Max(dx, dy) * 2;

        int half = thickness / 2;

        for (int s = 0; s <= steps; s++)
        {
            float t = (float)s / Mathf.Max(steps, 1);
            int cx = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
            int cy = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));

            for (int ox = -half; ox <= half; ox++)
            {
                for (int oy = -half; oy <= half; oy++)
                {
                    int px = cx + ox;
                    int py = cy + oy;
                    if (px < 0 || px >= tex.width || py < 0 || py >= tex.height) continue;

                    float distSq = (ox * ox + oy * oy);
                    float maxDistSq = half * half;
                    if (distSq > maxDistSq) continue;

                    // Rough powdery grain using fast hash noise
                    float grain = Mathf.PerlinNoise(px * 0.45f, py * 0.45f);
                    float edgeFalloff = 1f - Mathf.Sqrt(distSq / maxDistSq);
                    float alpha = edgeFalloff * Mathf.SmoothStep(0.15f, 0.95f, grain) * 0.90f;

                    if (alpha > 0.02f)
                    {
                        Color current = tex.GetPixel(px, py);
                        Color blended = Color.Lerp(current, col, Mathf.Max(current.a, alpha));
                        blended.a = Mathf.Clamp01(current.a + alpha);
                        tex.SetPixel(px, py, blended);
                    }
                }
            }
        }
    }

    public static AudioClip GetOrCreateScratchSFX()
    {
        if (_cachedScratchSFX != null) return _cachedScratchSFX;

        int rate = 44100;
        float duration = 0.32f;
        int count = (int)(rate * duration);

        AudioClip clip = AudioClip.Create("ChalkScratch", count, 1, rate, false);
        float[] samples = new float[count];

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / rate;
            float progress = t / duration;

            // Dry high-frequency friction friction + irregular skipping scrape
            float noise = (Random.value * 2f - 1f) * 0.55f;
            float scrape = Mathf.Sin(2f * Mathf.PI * 1800f * t) * 0.25f +
                           Mathf.Sin(2f * Mathf.PI * 3400f * t) * 0.15f;

            // Envelope: quick attack, textured body, crisp release
            float env = Mathf.Sin(progress * Mathf.PI);
            samples[i] = (noise + scrape) * env * 0.75f;
        }

        clip.SetData(samples, 0);
        _cachedScratchSFX = clip;
        return clip;
    }
}
