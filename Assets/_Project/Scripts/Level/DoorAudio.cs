using UnityEngine;

/// <summary>
/// Realistic acoustic sound generator for Backrooms doors and lockers.
/// Produces natural tactile latch clicks, wooden frame impacts, and friction creaks.
/// Prefers loading authored WAV assets from Resources/SFX/Doors/ if available,
/// falling back to runtime procedural acoustic synthesis.
/// </summary>
public static class DoorAudio
{
    private const int SampleRate = 22050;

    private static AudioClip doorOpenClip;
    private static AudioClip doorCloseClip;
    private static AudioClip doorRattleClip;
    private static AudioClip doorSlamClip;
    private static AudioClip lockerOpenClip;
    private static AudioClip lockerCloseClip;

    public static AudioClip DoorOpen => doorOpenClip ??= (Resources.Load<AudioClip>("SFX/Doors/Door_Open") ?? GenerateDoorOpen());
    public static AudioClip DoorClose => doorCloseClip ??= (Resources.Load<AudioClip>("SFX/Doors/Door_Close") ?? GenerateDoorClose());
    public static AudioClip DoorRattle => doorRattleClip ??= (Resources.Load<AudioClip>("SFX/Doors/Door_Rattle") ?? GenerateDoorRattle());
    public static AudioClip DoorSlam => doorSlamClip ??= (Resources.Load<AudioClip>("SFX/Doors/Door_Slam") ?? GenerateDoorSlam());
    public static AudioClip LockerOpen => lockerOpenClip ??= (Resources.Load<AudioClip>("SFX/Doors/Locker_Open") ?? GenerateLockerOpen());
    public static AudioClip LockerClose => lockerCloseClip ??= (Resources.Load<AudioClip>("SFX/Doors/Locker_Close") ?? GenerateLockerClose());

    /// <summary>
    /// Door Open: Crisp brass latch unclicking followed by subtle natural wooden hinge movement.
    /// </summary>
    private static AudioClip GenerateDoorOpen()
    {
        float duration = 0.70f;
        int n = (int)(SampleRate * duration);
        float[] d = new float[n];
        System.Random rng = new System.Random(777);

        int clickSamples = (int)(SampleRate * 0.08f);
        for (int i = 0; i < clickSamples; i++)
        {
            float t = i / (float)SampleRate;
            float clickNoise = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 120f);
            float latchRing = Mathf.Sin(2f * Mathf.PI * 2400f * t) * Mathf.Exp(-t * 90f) * 0.4f;
            float springSnap = Mathf.Sin(2f * Mathf.PI * 950f * t) * Mathf.Exp(-t * 60f) * 0.3f;
            d[i] += (clickNoise * 0.5f + latchRing + springSnap) * 0.75f;
        }

        int creakStart = (int)(SampleRate * 0.06f);
        int creakEnd = (int)(SampleRate * 0.55f);
        float slipAccum = 0f;

        for (int i = creakStart; i < creakEnd; i++)
        {
            float t = (i - creakStart) / (float)SampleRate;
            float creakLen = (creakEnd - creakStart) / (float)SampleRate;
            float p = t / creakLen;
            float env = Mathf.Sin(p * Mathf.PI);

            float pulseRate = 140f + Mathf.Sin(p * 9f) * 45f;
            slipAccum += (pulseRate / SampleRate) * (1f + (float)(rng.NextDouble() * 0.3 - 0.15));
            float pulse = slipAccum % 1f;
            float pulseSharp = Mathf.Sin(pulse * 2f * Mathf.PI) * Mathf.Exp(-pulse * 4f);

            float woodRes = Mathf.Sin(2f * Mathf.PI * 175f * t) * 0.25f;
            float whoosh = (float)(rng.NextDouble() * 0.4 - 0.2) * Mathf.Sin(p * Mathf.PI) * 0.15f;

            d[i] += (pulseSharp * 0.35f + woodRes * 0.25f + whoosh) * env * 0.65f;
        }

        float maxVal = 0.001f;
        for (int i = 0; i < n; i++) maxVal = Mathf.Max(maxVal, Mathf.Abs(d[i]));
        for (int i = 0; i < n; i++) d[i] = (d[i] / maxVal) * 0.85f;

        AudioClip clip = AudioClip.Create("DoorOpen_Realistic", n, 1, SampleRate, false);
        clip.SetData(d, 0);
        return clip;
    }

    /// <summary>
    /// Door Close: Heavy solid wooden door frame impact + spring-loaded strike plate latch snap (th-clack!).
    /// </summary>
    private static AudioClip GenerateDoorClose()
    {
        float duration = 0.60f;
        int n = (int)(SampleRate * duration);
        float[] d = new float[n];
        System.Random rng = new System.Random(888);

        int impactIdx = (int)(SampleRate * 0.08f);

        for (int i = 0; i < impactIdx; i++)
        {
            float t = i / (float)SampleRate;
            float w = Mathf.Pow(t / 0.08f, 2f);
            d[i] = (float)(rng.NextDouble() * 0.3 - 0.15) * w * 0.2f;
        }

        for (int i = impactIdx; i < n; i++)
        {
            float t = (i - impactIdx) / (float)SampleRate;

            float woodThud = Mathf.Sin(2f * Mathf.PI * 145f * t) * Mathf.Exp(-t * 32f) * 0.75f;
            float woodSub = Mathf.Sin(2f * Mathf.PI * 88f * t) * Mathf.Exp(-t * 22f) * 0.45f;
            float woodSlap = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 85f) * 0.4f;

            float latchClick = 0f;
            if (t >= 0.015f)
            {
                float lt = t - 0.015f;
                float latchMetal = Mathf.Sin(2f * Mathf.PI * 2100f * lt) * Mathf.Exp(-lt * 80f) * 0.5f;
                float latchSnap = Mathf.Sin(2f * Mathf.PI * 1250f * lt) * Mathf.Exp(-lt * 55f) * 0.35f;
                float latchNoise = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-lt * 100f) * 0.3f;
                latchClick = latchMetal + latchSnap + latchNoise;
            }

            d[i] = woodThud + woodSub + woodSlap + latchClick;
        }

        float maxVal = 0.001f;
        for (int i = 0; i < n; i++) maxVal = Mathf.Max(maxVal, Mathf.Abs(d[i]));
        for (int i = 0; i < n; i++) d[i] = (d[i] / maxVal) * 0.90f;

        AudioClip clip = AudioClip.Create("DoorClose_Realistic", n, 1, SampleRate, false);
        clip.SetData(d, 0);
        return clip;
    }

    /// <summary>
    /// Door Rattle: Mechanical shuddering against the strike plate.
    /// </summary>
    private static AudioClip GenerateDoorRattle()
    {
        float duration = 0.80f;
        int n = (int)(SampleRate * duration);
        float[] d = new float[n];
        System.Random rng = new System.Random(1111);

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float p = t / duration;
            float env = Mathf.Sin(p * Mathf.PI);

            float pulse = Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 18f * t));
            pulse *= pulse;

            float strikeNoise = (float)(rng.NextDouble() * 1.6 - 0.8) * pulse;
            float strikeRes = Mathf.Sin(2f * Mathf.PI * 520f * t) * 0.4f * pulse;
            float woodJamb = Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.3f * pulse;

            d[i] = (strikeNoise + strikeRes + woodJamb) * env;
        }

        float maxVal = 0.001f;
        for (int i = 0; i < n; i++) maxVal = Mathf.Max(maxVal, Mathf.Abs(d[i]));
        for (int i = 0; i < n; i++) d[i] = (d[i] / maxVal) * 0.82f;

        AudioClip clip = AudioClip.Create("DoorRattle_Realistic", n, 1, SampleRate, false);
        clip.SetData(d, 0);
        return clip;
    }

    /// <summary>
    /// Door Slam: Violent heavy wooden door slam with sub-blast and reverberant frame impact.
    /// </summary>
    private static AudioClip GenerateDoorSlam()
    {
        float duration = 0.85f;
        int n = (int)(SampleRate * duration);
        float[] d = new float[n];
        System.Random rng = new System.Random(999);

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float impactTransient = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 70f) * 0.85f;
            float woodSlam = Mathf.Sin(2f * Mathf.PI * 120f * t) * Mathf.Exp(-t * 18f) * 0.9f;
            float subBlast = Mathf.Sin(2f * Mathf.PI * 65f * t) * Mathf.Exp(-t * 12f) * 0.7f;
            float metalStrike = Mathf.Sin(2f * Mathf.PI * 1800f * t) * Mathf.Exp(-t * 45f) * 0.45f;
            float reverb = (float)(rng.NextDouble() * 0.3 - 0.15) * Mathf.Exp(-t * 6f) * 0.25f;

            d[i] = impactTransient + woodSlam + subBlast + metalStrike + reverb;
        }

        float maxVal = 0.001f;
        for (int i = 0; i < n; i++) maxVal = Mathf.Max(maxVal, Mathf.Abs(d[i]));
        for (int i = 0; i < n; i++) d[i] = (d[i] / maxVal) * 0.95f;

        AudioClip clip = AudioClip.Create("DoorSlam_Realistic", n, 1, SampleRate, false);
        clip.SetData(d, 0);
        return clip;
    }

    /// <summary>
    /// Locker Open: Sheet-metal latch pop and tin squeak.
    /// </summary>
    private static AudioClip GenerateLockerOpen()
    {
        float duration = 0.60f;
        int n = (int)(SampleRate * duration);
        float[] d = new float[n];
        System.Random rng = new System.Random(2222);

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float latch = (t < 0.05f) ? (Mathf.Sin(2f * Mathf.PI * 1400f * t) * Mathf.Exp(-t * 80f) + (float)(rng.NextDouble() * 0.6 - 0.3) * Mathf.Exp(-t * 90f)) : 0f;
            float p = t / duration;
            float env = Mathf.Sin(p * Mathf.PI);
            float tinSqueak = Mathf.Sin(2f * Mathf.PI * (450f + Mathf.Sin(p * 8f) * 60f) * t) * 0.25f * env;

            d[i] = latch + tinSqueak;
        }

        float maxVal = 0.001f;
        for (int i = 0; i < n; i++) maxVal = Mathf.Max(maxVal, Mathf.Abs(d[i]));
        for (int i = 0; i < n; i++) d[i] = (d[i] / maxVal) * 0.80f;

        AudioClip clip = AudioClip.Create("LockerOpen_Realistic", n, 1, SampleRate, false);
        clip.SetData(d, 0);
        return clip;
    }

    /// <summary>
    /// Locker Close: Hollow metallic locker door clang and latch click.
    /// </summary>
    private static AudioClip GenerateLockerClose()
    {
        float duration = 0.50f;
        int n = (int)(SampleRate * duration);
        float[] d = new float[n];
        System.Random rng = new System.Random(3333);

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float clang = Mathf.Sin(2f * Mathf.PI * 210f * t) * Mathf.Exp(-t * 24f) * 0.7f;
            float hollowTin = Mathf.Sin(2f * Mathf.PI * 135f * t) * Mathf.Exp(-t * 18f) * 0.5f;
            float latchClick = Mathf.Sin(2f * Mathf.PI * 1800f * t) * Mathf.Exp(-t * 60f) * 0.4f;
            float contactNoise = (float)(rng.NextDouble() * 1.0 - 0.5) * Mathf.Exp(-t * 70f) * 0.35f;

            d[i] = clang + hollowTin + latchClick + contactNoise;
        }

        float maxVal = 0.001f;
        for (int i = 0; i < n; i++) maxVal = Mathf.Max(maxVal, Mathf.Abs(d[i]));
        for (int i = 0; i < n; i++) d[i] = (d[i] / maxVal) * 0.85f;

        AudioClip clip = AudioClip.Create("LockerClose_Realistic", n, 1, SampleRate, false);
        clip.SetData(d, 0);
        return clip;
    }
}
