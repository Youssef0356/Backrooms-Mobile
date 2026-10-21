using UnityEditor;
using UnityEngine;

/// One-click, mobile-friendly import settings for every audio clip in Assets/_Project/Audio/Resources/SFX.
///
/// - Short sounds  -> Decompress On Load (instant playback for SFX)
/// - Long tracks   -> Streaming (no big memory spike on mobile)
/// - Compression   -> Vorbis (small build size) at quality 0.7
///
/// Usage: Tools > Backrooms > Fix SFX Import Settings (Mobile)
public static class MobileAudioImportUtility
{
    const string SfxFolder = "Assets/_Project/Audio/Resources/SFX";
    const float StreamingLengthSeconds = 60f;   // clips at least this long stream from disk

    [MenuItem("Tools/Backrooms/Fix SFX Import Settings (Mobile)")]
    public static void ApplyMobileSettings()
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { SfxFolder });
        if (guids.Length == 0)
        {
            Debug.LogWarning($"MobileAudioImportUtility: no audio clips found in {SfxFolder}.");
            return;
        }

        int streamed = 0;
        int inMemory = 0;
        int skipped = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) continue;

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            AudioClipLoadType desiredLoad =
                (clip != null && clip.length >= StreamingLengthSeconds)
                    ? AudioClipLoadType.Streaming
                    : AudioClipLoadType.DecompressOnLoad;

            var settings = importer.defaultSampleSettings;
            if (settings.loadType == desiredLoad && settings.compressionFormat == AudioCompressionFormat.Vorbis)
            {
                skipped++;
                continue;                                  // already correct, avoid a needless reimport
            }

            settings.loadType = desiredLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();

            if (desiredLoad == AudioClipLoadType.Streaming) streamed++;
            else inMemory++;
        }

        Debug.Log($"MobileAudioImportUtility: updated {streamed} streaming (music) + " +
                  $"{inMemory} in-memory (short sounds), {skipped} already correct.");
    }
}
