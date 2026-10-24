using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Procedurally generates a detailed 3D Almond Water bottle mesh and retro Backrooms label texture.
/// Features authentic liminal styling: amber glass/plastic, aluminum cap, and aged M.E.G. paper label.
/// </summary>
public static class AlmondWaterMeshBuilder
{
    private static Mesh _cachedBottleMesh;
    private static Material _cachedBottleMaterial;
    private static Material _cachedLabelMaterial;
    private static Texture2D _cachedLabelTexture;

    public static Mesh GetOrCreateBottleMesh()
    {
        if (_cachedBottleMesh != null) return _cachedBottleMesh;

        Mesh mesh = new Mesh { name = "AlmondWater_Bottle_Mesh" };

        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();

        List<int> bodyTriangles = new List<int>();
        List<int> labelTriangles = new List<int>();

        int radialSegments = 18;

        // Bottle profile profile points: (radius, y)
        // 0. Base bottom: 0.040, 0.00
        // 1. Lower body:  0.044, 0.02
        // 2. Mid body:    0.045, 0.14
        // 3. Shoulder:    0.043, 0.16
        // 4. Neck start:  0.020, 0.20
        // 5. Neck top:    0.018, 0.23
        // 6. Lip rim:     0.021, 0.24
        // 7. Cap top:     0.020, 0.26
        // 8. Cap crown:   0.000, 0.26

        Vector2[] profile = new Vector2[]
        {
            new Vector2(0.000f, 0.000f), // Bottom center
            new Vector2(0.038f, 0.002f), // Bottom rim
            new Vector2(0.044f, 0.015f), // Lower curve
            new Vector2(0.045f, 0.030f), // Body start (label bottom)
            new Vector2(0.045f, 0.135f), // Body end (label top)
            new Vector2(0.043f, 0.155f), // Shoulder
            new Vector2(0.022f, 0.190f), // Neck base
            new Vector2(0.020f, 0.225f), // Under cap
            new Vector2(0.022f, 0.228f), // Cap rim
            new Vector2(0.022f, 0.255f), // Cap top edge
            new Vector2(0.000f, 0.255f)  // Cap top center
        };

        // Build main bottle lathe
        for (int p = 0; p < profile.Length; p++)
        {
            float r = profile[p].x;
            float y = profile[p].y;

            for (int s = 0; s <= radialSegments; s++)
            {
                float angle = (float)s / radialSegments * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * r;
                float z = Mathf.Sin(angle) * r;

                vertices.Add(new Vector3(x, y, z));
                normals.Add(r > 0.001f ? new Vector3(Mathf.Cos(angle), 0.15f, Mathf.Sin(angle)).normalized : Vector3.up);
                uvs.Add(new Vector2((float)s / radialSegments, y / 0.26f));
            }
        }

        int segStride = radialSegments + 1;
        for (int p = 0; p < profile.Length - 1; p++)
        {
            int p0 = p * segStride;
            int p1 = (p + 1) * segStride;

            for (int s = 0; s < radialSegments; s++)
            {
                int a = p0 + s;
                int b = p0 + s + 1;
                int c = p1 + s;
                int d = p1 + s + 1;

                // Two triangles per quad
                bodyTriangles.Add(a);
                bodyTriangles.Add(c);
                bodyTriangles.Add(b);

                bodyTriangles.Add(b);
                bodyTriangles.Add(c);
                bodyTriangles.Add(d);
            }
        }

        // Build outer Label sleeve around the waist: y in [0.035, 0.130]
        float labelR = 0.0458f; // slightly proud of bottle surface to avoid z-fighting
        float labelY0 = 0.032f;
        float labelY1 = 0.132f;
        int labelBaseIndex = vertices.Count;

        for (int s = 0; s <= radialSegments; s++)
        {
            float angle = (float)s / radialSegments * Mathf.PI * 2f;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);

            // Bottom edge of label
            vertices.Add(new Vector3(x * labelR, labelY0, z * labelR));
            normals.Add(new Vector3(x, 0f, z));
            uvs.Add(new Vector2((float)s / radialSegments, 0f));

            // Top edge of label
            vertices.Add(new Vector3(x * labelR, labelY1, z * labelR));
            normals.Add(new Vector3(x, 0f, z));
            uvs.Add(new Vector2((float)s / radialSegments, 1f));
        }

        for (int s = 0; s < radialSegments; s++)
        {
            int i0 = labelBaseIndex + s * 2;
            int i1 = labelBaseIndex + s * 2 + 1;
            int i2 = labelBaseIndex + (s + 1) * 2;
            int i3 = labelBaseIndex + (s + 1) * 2 + 1;

            labelTriangles.Add(i0);
            labelTriangles.Add(i1);
            labelTriangles.Add(i2);

            labelTriangles.Add(i2);
            labelTriangles.Add(i1);
            labelTriangles.Add(i3);
        }

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);

        mesh.subMeshCount = 2;
        mesh.SetTriangles(bodyTriangles, 0);
        mesh.SetTriangles(labelTriangles, 1);

        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        _cachedBottleMesh = mesh;
        return mesh;
    }

    public static Material GetOrCreateBottleMaterial()
    {
        if (_cachedBottleMaterial != null) return _cachedBottleMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material mat = new Material(shader) { name = "AlmondWater_GlassMat" };
        // Warm honey/amber glass tint
        mat.color = new Color(0.92f, 0.76f, 0.44f, 0.92f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.88f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.15f);

        _cachedBottleMaterial = mat;
        return mat;
    }

    public static Material GetOrCreateLabelMaterial()
    {
        if (_cachedLabelMaterial != null) return _cachedLabelMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material mat = new Material(shader) { name = "AlmondWater_LabelMat" };
        Texture2D labelTex = GetOrCreateLabelTexture();
        mat.mainTexture = labelTex;
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.05f);

        _cachedLabelMaterial = mat;
        return mat;
    }

    public static Texture2D GetOrCreateLabelTexture()
    {
        if (_cachedLabelTexture != null) return _cachedLabelTexture;

        int width = 512;
        int height = 256;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, true);
        tex.name = "AlmondWater_LabelTex";

        Color paperBase = new Color(0.92f, 0.87f, 0.77f, 1f);      // aged parchment
        Color paperDark = new Color(0.85f, 0.78f, 0.65f, 1f);      // worn paper
        Color borderWine = new Color(0.48f, 0.12f, 0.14f, 1f);     // vintage crimson
        Color inkText = new Color(0.18f, 0.14f, 0.12f, 1f);        // typewriter charcoal
        Color goldAccent = new Color(0.78f, 0.62f, 0.26f, 1f);

        Color[] pixels = new Color[width * height];

        // 1. Parchment background with subtle grain
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float noise = Mathf.PerlinNoise(x * 0.04f, y * 0.04f) * 0.12f;
                float vignette = 1f - (Mathf.Abs(x - width * 0.5f) / (width * 0.5f)) * 0.18f;
                Color col = Color.Lerp(paperDark, paperBase, vignette - noise);

                // Margin bands
                if (y < 12 || y > height - 13) col = Color.Lerp(col, borderWine, 0.85f);
                else if (y < 18 || y > height - 19) col = goldAccent;
                else if (y == 24 || y == height - 25) col = borderWine * 0.7f;

                // Center decorative crest box
                if (x >= width * 0.18f && x <= width * 0.82f)
                {
                    if (y >= 45 && y <= 50) col = borderWine;
                    if (y >= height - 55 && y <= height - 50) col = borderWine;
                }

                pixels[y * width + x] = col;
            }
        }

        tex.SetPixels(pixels);

        // 2. Draw pixelated graphic text for "ALMOND WATER" and "M.E.G."
        DrawLabelText(tex, width, height, inkText, borderWine, goldAccent);

        tex.Apply(true, false);
        _cachedLabelTexture = tex;
        return tex;
    }

    private static void DrawLabelText(Texture2D tex, int w, int h, Color ink, Color wine, Color gold)
    {
        // Simple 5x7 bitmap font renderer for clean, crisp, legible procedural label text
        // "M.E.G. RATION - LEVEL 1"
        DrawBitmapString(tex, "M.E.G. LEVEL 1 RATION", w / 2, h - 38, wine, 2, true);

        // Main Title: "ALMOND WATER" (bold large)
        DrawBitmapString(tex, "ALMOND WATER", w / 2, h / 2 + 10, ink, 4, true);

        // Subtitle: "RESTORES SANITY & VITALITY"
        DrawBitmapString(tex, "RESTORES SANITY & STAMINA", w / 2, h / 2 - 25, wine, 2, true);

        // Bottom tag: "500 ML - SWEET AROMA"
        DrawBitmapString(tex, "SWEET ALMOND ESSENCE - 500 ML", w / 2, 34, ink * 0.8f, 1, true);

        // Draw small droplet / flask icon in center top
        int cx = w / 2;
        int cy = h - 68;
        for (int dy = -8; dy <= 8; dy++)
        {
            int r = Mathf.RoundToInt(6f * (1f - (dy + 8) / 18f));
            for (int dx = -r; dx <= r; dx++)
            {
                tex.SetPixel(cx + dx, cy + dy, gold);
            }
        }
    }

    private static void DrawBitmapString(Texture2D tex, string text, int centerX, int centerY, Color col, int scale, bool center)
    {
        int charW = 6 * scale;
        int totalW = text.Length * charW;
        int startX = center ? centerX - totalW / 2 : centerX;

        for (int i = 0; i < text.Length; i++)
        {
            char c = char.ToUpperInvariant(text[i]);
            byte[] glyph = GetGlyphBitmap(c);
            int gx = startX + i * charW;

            for (int row = 0; row < 7; row++)
            {
                byte b = glyph[row];
                for (int colBit = 0; colBit < 5; colBit++)
                {
                    if ((b & (1 << (4 - colBit))) != 0)
                    {
                        for (int sy = 0; sy < scale; sy++)
                        {
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int px = gx + colBit * scale + sx;
                                int py = centerY + (6 - row) * scale + sy;
                                if (px >= 0 && px < tex.width && py >= 0 && py < tex.height)
                                {
                                    tex.SetPixel(px, py, col);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private static byte[] GetGlyphBitmap(char c)
    {
        // 5x7 standard retro bitmap font definitions
        switch (c)
        {
            case 'A': return new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 };
            case 'B': return new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E };
            case 'C': return new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E };
            case 'D': return new byte[] { 0x1C, 0x12, 0x11, 0x11, 0x11, 0x12, 0x1C };
            case 'E': return new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F };
            case 'F': return new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 };
            case 'G': return new byte[] { 0x0E, 0x11, 0x10, 0x13, 0x11, 0x11, 0x0E };
            case 'H': return new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 };
            case 'I': return new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E };
            case 'J': return new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C };
            case 'K': return new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 };
            case 'L': return new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F };
            case 'M': return new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 };
            case 'N': return new byte[] { 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11 };
            case 'O': return new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E };
            case 'P': return new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 };
            case 'R': return new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 };
            case 'S': return new byte[] { 0x0E, 0x11, 0x10, 0x0E, 0x01, 0x11, 0x0E };
            case 'T': return new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 };
            case 'U': return new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E };
            case 'V': return new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 };
            case 'W': return new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A };
            case 'Y': return new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04 };
            case '0': return new byte[] { 0x0E, 0x13, 0x15, 0x19, 0x11, 0x11, 0x0E };
            case '1': return new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E };
            case '5': return new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E };
            case '.': return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x0C };
            case '-': return new byte[] { 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00 };
            case '&': return new byte[] { 0x0C, 0x12, 0x14, 0x08, 0x15, 0x12, 0x0D };
            default:  return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        }
    }
}
