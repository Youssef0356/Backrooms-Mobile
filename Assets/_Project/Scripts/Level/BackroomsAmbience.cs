using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// Plays the backrooms SFX automatically when the level starts.
///
/// Easiest setup: put your audio files in "Assets/_Project/Audio/Resources/SFX"
/// and they are loaded and played with zero configuration.
///
/// If your files live elsewhere (e.g. "Assets/_Project/Audio/Resources/SFX"), select this object
/// in the Editor and use the component's context menu
/// "Auto-Fill Clips From Resources/SFX" to assign them.
public class BackroomsAmbience : MonoBehaviour
{
    [Header("Clips (empty = auto-load everything from Resources/SFX)")]
    public AudioClip[] clips;

    [Header("Main soundtrack")]
    [Tooltip("Plays on loop instead of shuffling the list. Leave empty to auto-find it by name among the clips.")]
    public AudioClip mainClip;
    [Tooltip("Name fragment used to find the main soundtrack when Main Clip is empty.")]
    public string mainClipName = "backrooms level0";
    [Tooltip("Loop the main soundtrack forever instead of playing it once.")]
    public bool loopMainClip = true;

    [Header("Playback")]
    public bool playOnStart = true;
    public bool loopList = true;               // keep cycling forever
    public bool randomOrder = true;            // shuffle vs. file order
    public float minGap = 0f;                  // silent seconds between clips
    public float maxGap = 0f;
    [Range(0f, 1f)] public float volume = 0.25f;    // kept low so ambience stays in the background
    [Range(0f, 1f)] public float spatialBlend = 0f; // 0 = 2D room tone, 1 = 3D positional

    AudioSource source;
    bool playing;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        if (source == null) source = gameObject.AddComponent<AudioSource>();

        source.playOnAwake = false;
        source.loop = false;                   // the script chains one clip at a time
        source.spatialBlend = spatialBlend;
        source.volume = PlayerPrefs.GetFloat(GameSettings.MusicVolumeKey, volume);  // menu music slider overrides the default

        if (clips == null || clips.Length == 0)
            clips = Resources.LoadAll<AudioClip>("SFX");

        // The level always opens on the backrooms level 0 track; the rest of the
        // SFX folder is only used as a fallback / by the menu.
        if (mainClip == null && !string.IsNullOrEmpty(mainClipName))
            mainClip = FindClipByName(mainClipName);
    }

    void Start()
    {
        if (playOnStart) Play();
    }

    public void Play()
    {
        if (playing) return;

        // Main soundtrack: one looping track, no shuffling.
        if (mainClip != null)
        {
            source.clip = mainClip;
            source.loop = loopMainClip;
            source.Play();
            playing = true;
            return;
        }

        if (clips == null || clips.Length == 0)
        {
            Debug.LogWarning("BackroomsAmbience: no audio clips found. " +
                             "Put files in Assets/_Project/Audio/Resources/SFX or assign them in the Inspector.");
            return;
        }
        playing = true;
        StartCoroutine(PlayRoutine());
    }

    public void Stop()
    {
        StopAllCoroutines();
        if (source != null)
        {
            source.Stop();
            source.loop = false;
        }
        playing = false;
    }

    AudioClip FindClipByName(string fragment)
    {
        if (clips == null) return null;
        foreach (AudioClip c in clips)
        {
            if (c != null && c.name.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return c;
        }
        return null;
    }

    /// Live-updates the playback volume (used by the pause menu's MUSIC slider).
    public void SetVolume(float value)
    {
        volume = Mathf.Clamp01(value);
        if (source != null) source.volume = volume;
    }

    IEnumerator PlayRoutine()
    {
        source.loop = false;                   // the shuffle chains clips one at a time
        int remaining = clips.Length;          // one full pass when loopList is off

        while (loopList || remaining > 0)
        {
            int i = randomOrder ? Random.Range(0, clips.Length) : clips.Length - remaining;
            remaining--;

            AudioClip clip = clips[i];
            if (clip != null)
            {
                source.clip = clip;
                source.Play();
                yield return new WaitForSeconds(clip.length + Random.Range(minGap, maxGap));
            }
            else
            {
                yield return null;
            }
        }

        playing = false;
    }

#if UNITY_EDITOR
    /// Assigns every audio clip found in Assets/_Project/Audio/Resources/SFX (run in the Editor only).
    [ContextMenu("Auto-Fill Clips From Resources/SFX")]
    void AutoFillClipsFromSfxFolder()
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_Project/Audio/Resources/SFX" });
        clips = new AudioClip[guids.Length];
        for (int i = 0; i < guids.Length; i++)
            clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guids[i]));

        EditorUtility.SetDirty(this);
        Debug.Log($"BackroomsAmbience: assigned {clips.Length} clip(s) from Assets/_Project/Audio/Resources/SFX.", this);
    }
#endif
}
