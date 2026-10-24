using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages atmospheric monster composition audio:
/// 1. Spawns 3D spatial audio emitters at random spots across the Backrooms map with
///    distance attenuation (Logarithmic rolloff, min/max distance).
/// 2. Periodically plays eerie monster composition clips from random spots (or the villain's
///    actual location), with smooth fade-in and fade-out on every playback.
/// 3. Provides a random composition audio clip with fade-in/fade-out for the final villain catch.
/// </summary>
public class MonsterCompositionAudio : MonoBehaviour
{
    public static MonsterCompositionAudio Instance { get; private set; }

    [Header("Composition Clips")]
    [Tooltip("Clips placed in Resources/Composition. Auto-loaded if left empty.")]
    public AudioClip[] compositionClips;

    [Header("Spatial Audio (Attenuation)")]
    [Tooltip("Full 3D spatial audio for realistic direction and distance falloff (1.0 = 3D).")]
    [Range(0f, 1f)] public float spatialBlend = 1.0f;
    [Tooltip("Attenuation curve rolloff mode.")]
    public AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;
    [Tooltip("Minimum distance: sound remains at 100% volume within this radius (meters).")]
    public float minDistance = 2.5f;
    [Tooltip("Maximum distance: sound attenuates down to silence at this radius (meters).")]
    public float maxDistance = 24.0f;
    [Tooltip("3D sound spread in degrees (makes ambient sound feel wider).")]
    [Range(0f, 360f)] public float spread = 45f;
    [Tooltip("Base volume for spatial spots before Master Volume adjustment.")]
    [Range(0f, 1f)] public float spotVolume = 0.85f;
    [Tooltip("Maximum number of distinct spatial spot emitters placed across the map.")]
    public int maxSpots = 12;

    [Header("Playback Timing")]
    public bool enablePeriodicPlayback = true;
    [Tooltip("Delay before the first ambient composition sound begins (seconds).")]
    public float initialDelay = 6f;
    [Tooltip("Minimum silent interval between spot audio events (seconds).")]
    public float minInterval = 14f;
    [Tooltip("Maximum silent interval between spot audio events (seconds).")]
    public float maxInterval = 35f;
    [Tooltip("Chance to emit the audio from the villain's live position instead of a map spot.")]
    [Range(0f, 1f)] public float villainLocationChance = 0.35f;

    [Header("Fading")]
    [Tooltip("Fade-in duration in seconds for spatial spot sounds.")]
    public float fadeInDuration = 1.8f;
    [Tooltip("Fade-out duration in seconds for spatial spot sounds.")]
    public float fadeOutDuration = 2.4f;

    readonly List<AudioSource> spotSources = new List<AudioSource>();
    AudioSource villainAudioSource;
    Transform villainTransform;
    Coroutine playbackLoopRoutine;
    int lastPlayedClipIndex = -1;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        LoadClipsIfEmpty();
    }

    void Start()
    {
        FindVillain();

        if (enablePeriodicPlayback)
        {
            playbackLoopRoutine = StartCoroutine(PlaybackLoop());
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void LoadClipsIfEmpty()
    {
        if (compositionClips == null || compositionClips.Length == 0)
        {
            compositionClips = Resources.LoadAll<AudioClip>("Composition");
            if (compositionClips == null || compositionClips.Length == 0)
            {
                // Fallback check in case path is Audio/Composition
                compositionClips = Resources.LoadAll<AudioClip>("SFX");
            }
        }
    }

    /// <summary>
    /// Returns a random monster composition clip from the pool (avoids immediate repeats).
    /// Safe to call statically from anywhere, e.g. GameOverController.
    /// </summary>
    public static AudioClip GetRandomCompositionClip()
    {
        if (Instance != null && Instance.compositionClips != null && Instance.compositionClips.Length > 0)
        {
            return Instance.PickNextClip();
        }

        // Direct fallback loading from Resources if Instance isn't ready
        AudioClip[] loaded = Resources.LoadAll<AudioClip>("Composition");
        if (loaded != null && loaded.Length > 0)
        {
            return loaded[Random.Range(0, loaded.Length)];
        }

        return null;
    }

    AudioClip PickNextClip()
    {
        if (compositionClips == null || compositionClips.Length == 0) return null;
        if (compositionClips.Length == 1) return compositionClips[0];

        int index = Random.Range(0, compositionClips.Length);
        if (index == lastPlayedClipIndex)
        {
            index = (index + 1 + Random.Range(0, compositionClips.Length - 1)) % compositionClips.Length;
        }
        lastPlayedClipIndex = index;
        return compositionClips[index];
    }

    /// <summary>
    /// Initializes spatial audio emitter spots at diverse walkable positions across the labyrinth.
    /// Called by BackroomsRoomGenerator after layout generation.
    /// </summary>
    public void InitializeSpots(List<Vector3> candidatePositions)
    {
        // Clear any old spots
        foreach (AudioSource s in spotSources)
        {
            if (s != null) Destroy(s.gameObject);
        }
        spotSources.Clear();

        if (candidatePositions == null || candidatePositions.Count == 0) return;

        // Shuffle candidate positions to pick varied spots across rooms and corridors
        List<Vector3> pool = new List<Vector3>(candidatePositions);
        for (int i = 0; i < pool.Count; i++)
        {
            int r = Random.Range(i, pool.Count);
            (pool[i], pool[r]) = (pool[r], pool[i]);
        }

        int count = Mathf.Min(maxSpots, pool.Count);
        GameObject container = new GameObject("MonsterSpatialAudioSpots");
        container.transform.SetParent(transform, false);

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = pool[i] + Vector3.up * 1.5f; // eye/head level

            GameObject spotObj = new GameObject($"MonsterAudioSpot_{i:D2}");
            spotObj.transform.position = pos;
            spotObj.transform.SetParent(container.transform, true);

            AudioSource source = spotObj.AddComponent<AudioSource>();
            ConfigureSpatialAudioSource(source);
            spotSources.Add(source);
        }
    }

    void ConfigureSpatialAudioSource(AudioSource source)
    {
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = spatialBlend; // 1.0 = 3D sound
        source.rolloffMode = rolloffMode;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.dopplerLevel = 0f;
        source.spread = spread;
        source.volume = 0f; // Controlled via fade in / out
    }

    void FindVillain()
    {
        if (villainTransform != null && villainAudioSource != null) return;

        VillainAI ai = FindAnyObjectByType<VillainAI>();
        if (ai != null)
        {
            villainTransform = ai.transform;
            villainAudioSource = ai.GetComponent<AudioSource>();
            if (villainAudioSource == null)
            {
                villainAudioSource = ai.gameObject.AddComponent<AudioSource>();
                ConfigureSpatialAudioSource(villainAudioSource);
                // Villain can be heard slightly further away
                villainAudioSource.maxDistance = maxDistance * 1.25f;
            }
        }
    }

    IEnumerator PlaybackLoop()
    {
        yield return new WaitForSeconds(initialDelay);

        while (true)
        {
            float delay = Random.Range(minInterval, maxInterval);
            yield return new WaitForSeconds(delay);

            FindVillain();

            // Decide which source to play through
            AudioSource chosenSource = null;

            bool emitFromVillain = (villainAudioSource != null && Random.value < villainLocationChance);
            if (emitFromVillain && !villainAudioSource.isPlaying)
            {
                chosenSource = villainAudioSource;
            }
            else if (spotSources.Count > 0)
            {
                // Find a spot source that is not currently playing
                List<AudioSource> idleSources = spotSources.FindAll(s => s != null && !s.isPlaying);
                if (idleSources.Count > 0)
                {
                    chosenSource = idleSources[Random.Range(0, idleSources.Count)];
                }
            }

            if (chosenSource != null)
            {
                AudioClip clip = PickNextClip();
                if (clip != null)
                {
                    float effectiveVolume = spotVolume * GameSettings.MasterVolume;
                    StartCoroutine(PlayClipWithFadeRoutine(chosenSource, clip, effectiveVolume, fadeInDuration, fadeOutDuration));
                }
            }
        }
    }

    /// <summary>
    /// Smoothly plays an audio clip with fade-in at the start and fade-out at the end.
    /// Handles any duration gracefully.
    /// </summary>
    public static IEnumerator PlayClipWithFadeRoutine(AudioSource source, AudioClip clip, float targetVolume, float fadeInTime, float fadeOutTime)
    {
        if (source == null || clip == null) yield break;

        float clipDuration = clip.length;

        // Auto-scale fade times for shorter clips to prevent overlap
        if (clipDuration < (fadeInTime + fadeOutTime))
        {
            fadeInTime = clipDuration * 0.35f;
            fadeOutTime = clipDuration * 0.45f;
        }

        float sustainTime = Mathf.Max(0f, clipDuration - fadeInTime - fadeOutTime);

        source.clip = clip;
        source.volume = 0f;
        source.Play();

        // 1. Fade in
        float timer = 0f;
        while (timer < fadeInTime)
        {
            if (source == null) yield break;
            timer += Time.deltaTime;
            source.volume = Mathf.Lerp(0f, targetVolume, Mathf.Clamp01(timer / fadeInTime));
            yield return null;
        }

        if (source != null) source.volume = targetVolume;

        // 2. Sustain
        if (sustainTime > 0f)
        {
            yield return new WaitForSeconds(sustainTime);
        }

        // 3. Fade out
        timer = 0f;
        while (timer < fadeOutTime)
        {
            if (source == null) yield break;
            timer += Time.deltaTime;
            source.volume = Mathf.Lerp(targetVolume, 0f, Mathf.Clamp01(timer / fadeOutTime));
            yield return null;
        }

        if (source != null)
        {
            source.volume = 0f;
            source.Stop();
        }
    }
}
