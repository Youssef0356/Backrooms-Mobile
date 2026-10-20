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
    }

    void Start()
    {
        if (playOnStart) Play();
    }

    public void Play()
    {
        if (playing) return;
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
        if (source != null) source.Stop();
        playing = false;
    }

    IEnumerator PlayRoutine()
    {
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
