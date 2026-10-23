using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Triggers atmospheric horror events where the Backrooms power fluctuates,
/// flickers violently, and plunges the map into total blackout darkness.
/// Leaves the player dependent solely on their flashlight!
/// </summary>
public class BackroomsBlackoutEvent : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Min seconds between blackout events.")]
    public float minInterval = 40f;
    [Tooltip("Max seconds between blackout events.")]
    public float maxInterval = 80f;
    [Tooltip("How long the complete pitch-black darkness lasts.")]
    public float blackoutDuration = 10f;
    [Tooltip("Flickering warning time before the power cuts completely.")]
    public float flickerDuration = 2.2f;

    [Header("Atmosphere During Blackout")]
    [Tooltip("Color the ambient light drops to during blackout (near total darkness).")]
    public Color blackoutAmbientColor = new Color(0.015f, 0.012f, 0.01f);
    [Tooltip("Fog color during blackout.")]
    public Color blackoutFogColor = new Color(0.02f, 0.015f, 0.015f);

    [Header("Audio (Optional)")]
    public AudioClip powerDownSfx;
    public AudioClip powerUpSfx;

    public bool IsBlackoutActive { get; private set; }

    List<Light> panelLights = new List<Light>();
    Color originalAmbientColor;
    Color originalFogColor;
    Material lightPanelMaterial;
    Color originalEmissionColor = new Color(2.4f, 2.3f, 2.0f);
    AudioSource audioSource;
    Coroutine eventRoutine;

    void Start()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f; // 2D ambient impact

        originalAmbientColor = RenderSettings.ambientLight;
        originalFogColor = RenderSettings.fogColor;

        BackroomsRoomGenerator generator = Object.FindAnyObjectByType<BackroomsRoomGenerator>();
        if (generator != null && generator.lightPanelMaterial != null)
        {
            lightPanelMaterial = generator.lightPanelMaterial;
            if (lightPanelMaterial.HasProperty("_EmissionColor"))
                originalEmissionColor = lightPanelMaterial.GetColor("_EmissionColor");
        }

        eventRoutine = StartCoroutine(BlackoutCycleRoutine());
    }

    void OnDestroy()
    {
        if (eventRoutine != null) StopCoroutine(eventRoutine);
        RestoreLighting();
    }

    IEnumerator BlackoutCycleRoutine()
    {
        // Initial grace period at the start of the level
        yield return new WaitForSeconds(Random.Range(20f, 35f));

        while (true)
        {
            float waitTime = Random.Range(minInterval, maxInterval);
            yield return new WaitForSeconds(waitTime);

            if (!GameOverController.IsGameOver && !PauseMenuController.IsPaused)
            {
                yield return StartCoroutine(ExecuteBlackoutRoutine());
            }
        }
    }

    [ContextMenu("Trigger Blackout Now")]
    public void TriggerImmediateBlackout()
    {
        if (IsBlackoutActive) return;
        StartCoroutine(ExecuteBlackoutRoutine());
    }

    IEnumerator ExecuteBlackoutRoutine()
    {
        IsBlackoutActive = true;
        CachePanelLights();

        // 1. WARNING PHASE: Fluorescent tubes flicker violently
        float warningTimer = 0f;
        while (warningTimer < flickerDuration)
        {
            float flickerDelay = Random.Range(0.04f, 0.18f);
            bool lightsState = Random.value > 0.45f;
            SetAllPanelLights(lightsState);
            SetEmission(lightsState ? originalEmissionColor * 0.7f : Color.black);

            if (lightsState && panelLights.Count > 0 && Random.value < 0.4f)
            {
                Light rndLight = panelLights[Random.Range(0, panelLights.Count)];
                if (rndLight != null) BackroomsParticles.TriggerSparks(rndLight.transform.position, Random.Range(6, 12));
            }

            warningTimer += flickerDelay;
            yield return new WaitForSeconds(flickerDelay);
        }

        // 2. BLACKOUT CUT: Power goes dead
        SetAllPanelLights(false);
        SetEmission(Color.black);

        RenderSettings.ambientLight = blackoutAmbientColor;
        RenderSettings.fogColor = blackoutFogColor;

        PlaySfx(powerDownSfx, CreateDefaultPowerCutSound());

        // Alert villain to the sound of popping transformers
        PlayerNoiseEmitter.EmitNoise(transform.position, 12f);

        // 3. DARKNESS DURATION
        yield return new WaitForSeconds(blackoutDuration);

        // 4. RESTORATION PHASE: Transformer pops back to life
        PlaySfx(powerUpSfx, CreateDefaultPowerRestoreSound());

        // A few sparks before coming on solid
        if (panelLights.Count > 0)
        {
            Light rndLight = panelLights[Random.Range(0, panelLights.Count)];
            if (rndLight != null) BackroomsParticles.TriggerSparks(rndLight.transform.position, 16);
        }
        SetAllPanelLights(true);
        yield return new WaitForSeconds(0.06f);
        SetAllPanelLights(false);
        yield return new WaitForSeconds(0.12f);
        SetAllPanelLights(true);
        yield return new WaitForSeconds(0.05f);
        SetAllPanelLights(false);
        yield return new WaitForSeconds(0.15f);

        // 5. RESTORED
        RestoreLighting();
        IsBlackoutActive = false;
    }

    void RestoreLighting()
    {
        SetAllPanelLights(true);
        SetEmission(originalEmissionColor);

        RenderSettings.ambientLight = originalAmbientColor;
        RenderSettings.fogColor = originalFogColor;
    }

    void CachePanelLights()
    {
        panelLights.Clear();
        Light[] allLights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        foreach (Light l in allLights)
        {
            // Skip the player's flashlight
            if (l.type == LightType.Point && !l.CompareTag("Player") && !l.name.ToLowerInvariant().Contains("flashlight"))
            {
                panelLights.Add(l);
            }
        }
    }

    void SetAllPanelLights(bool on)
    {
        for (int i = 0; i < panelLights.Count; i++)
        {
            if (panelLights[i] != null) panelLights[i].enabled = on;
        }
    }

    void SetEmission(Color color)
    {
        if (lightPanelMaterial != null && lightPanelMaterial.HasProperty("_EmissionColor"))
        {
            lightPanelMaterial.SetColor("_EmissionColor", color);
        }
    }

    void PlaySfx(AudioClip clip, AudioClip fallbackProcedural)
    {
        AudioClip chosen = clip != null ? clip : fallbackProcedural;
        if (chosen != null && audioSource != null)
        {
            audioSource.PlayOneShot(chosen, 0.75f * GameSettings.MasterVolume);
        }
    }

    AudioClip CreateDefaultPowerCutSound()
    {
        int sampleRate = 22050;
        int length = sampleRate / 2; // 0.5s
        float[] samples = new float[length];
        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            float freq = Mathf.Lerp(120f, 30f, t / 0.5f);
            samples[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * (1f - (float)i / length) * 0.4f;
        }
        AudioClip clip = AudioClip.Create("PowerCut", length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    AudioClip CreateDefaultPowerRestoreSound()
    {
        int sampleRate = 22050;
        int length = (int)(sampleRate * 0.6f);
        float[] samples = new float[length];
        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            // Pop followed by 60Hz hum surge
            float pop = i < 400 ? (Random.value * 2f - 1f) * 0.8f : 0f;
            float hum = Mathf.Sin(2f * Mathf.PI * 60f * t) * Mathf.Clamp01(t * 2.5f) * 0.35f;
            samples[i] = pop + hum;
        }
        AudioClip clip = AudioClip.Create("PowerRestore", length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
