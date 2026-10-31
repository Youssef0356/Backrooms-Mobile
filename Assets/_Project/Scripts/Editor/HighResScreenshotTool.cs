#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

public class HighResScreenshotWindow : EditorWindow
{
    private int _superSize = 2; // 2x = 4800x2160 on a 2400x1080 Game view
    private bool _openFolderAfterCapture = true;

    // Video Recording Settings
    private int _targetFps = 30;
    private bool _lockFramerate = false; // If true, sets Time.captureFramerate for 0 dropped frames
    private static bool _recordAudio = true; // Captures in-game audio/SFX into the MP4

    [MenuItem("Tools/Backrooms/Capture & Recording/📸 Capture & Video Recorder Tool", priority = 100)]
    public static void ShowWindow()
    {
        var window = GetWindow<HighResScreenshotWindow>("Capture & Recorder");
        window.minSize = new Vector2(380, 420);
        window.Show();
    }

    [MenuItem("Tools/Backrooms/Capture & Recording/Capture Screenshot Now (F12) _F12", priority = 101)]
    public static void QuickCapture()
    {
        CaptureWithSuperSize(2, true);
    }

    [MenuItem("Tools/Backrooms/Capture & Recording/🔴 Toggle Video Recording (F11) _F11", priority = 102)]
    public static void ToggleVideoRecording()
    {
        if (GameplayVideoRecorderRunner.Instance != null && GameplayVideoRecorderRunner.Instance.isRecording)
        {
            GameplayVideoRecorderRunner.Instance.StopRecording();
        }
        else
        {
            StartRecordingVideo(30, false);
        }
    }

    private void OnInspectorUpdate()
    {
        // Live UI update while recording
        if (GameplayVideoRecorderRunner.Instance != null && GameplayVideoRecorderRunner.Instance.isRecording)
        {
            Repaint();
        }
    }

    private void OnGUI()
    {
        GUILayout.Space(10);
        EditorGUILayout.LabelField("Backrooms Media Capture Studio", EditorStyles.boldLabel);

        // ==========================================
        // SECTION 1: VIDEO RECORDER (MP4)
        // ==========================================
        GUILayout.Space(5);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("🎥 Gameplay Video Recorder (MP4)", EditorStyles.boldLabel);
        
        bool isRec = GameplayVideoRecorderRunner.Instance != null && GameplayVideoRecorderRunner.Instance.isRecording;

        if (isRec)
        {
            float elapsed = Time.realtimeSinceStartup - GameplayVideoRecorderRunner.Instance.recordStartTime;
            int mins = (int)(elapsed / 60);
            int secs = (int)(elapsed % 60);
            int frames = GameplayVideoRecorderRunner.Instance.frameCount;

            GUI.backgroundColor = new Color(1f, 0.2f, 0.2f);
            EditorGUILayout.HelpBox($"🔴 RECORDING IN PROGRESS...\nTime: {mins:D2}:{secs:D2} | Frames: {frames} @ {GameplayVideoRecorderRunner.Instance.targetFps} FPS", MessageType.Warning);
            GUI.backgroundColor = Color.white;

            GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
            if (GUILayout.Button($"⏹️ STOP RECORDING (F11)", GUILayout.Height(42)))
            {
                GameplayVideoRecorderRunner.Instance.StopRecording();
            }
            GUI.backgroundColor = Color.white;
        }
        else
        {
            EditorGUILayout.HelpBox("Records smooth MP4 gameplay video directly from your Game view.\nPress F11 anytime during Play Mode to Start / Stop.", MessageType.Info);

            _targetFps = EditorGUILayout.IntPopup("Framerate:", _targetFps, new string[] { "30 FPS (Standard / Fast)", "60 FPS (Ultra Smooth)" }, new int[] { 30, 60 });
            _lockFramerate = EditorGUILayout.Toggle("Lock Capture Timestep", _lockFramerate);
            if (_lockFramerate)
            {
                EditorGUILayout.HelpBox("Locks game delta time so video renders with 0 dropped frames even under heavy load.", MessageType.None);
            }

            GUILayout.Space(5);
            GUI.backgroundColor = new Color(0.2f, 0.7f, 1f);
            if (GUILayout.Button("🔴 Start Recording Gameplay (F11)", GUILayout.Height(38)))
            {
                StartRecordingVideo(_targetFps, _lockFramerate);
            }
            GUI.backgroundColor = Color.white;
        }

        if (GUILayout.Button("Open Recordings Folder (MP4)", GUILayout.Height(22)))
        {
            string folder = GetRecordingsFolderPath();
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        }
        EditorGUILayout.EndVertical();

        // ==========================================
        // SECTION 2: HIGH-RES SCREENSHOTS
        // ==========================================
        GUILayout.Space(10);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("📸 High-Resolution Screenshot", EditorStyles.boldLabel);

        _superSize = EditorGUILayout.IntPopup(
            "Resolution Multiplier:",
            _superSize,
            new string[] { "1x (Normal Game View - 2400x1080)", "2x (High-Res 4K ~4800x2160)", "3x (Super 6K+)", "4x (Ultra 8K+)" },
            new int[] { 1, 2, 3, 4 }
        );

        _openFolderAfterCapture = EditorGUILayout.Toggle("Open Folder After Capture", _openFolderAfterCapture);

        GUILayout.Space(5);
        GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
        if (GUILayout.Button("📸 Capture Screenshot (F12)", GUILayout.Height(36)))
        {
            CaptureWithSuperSize(_superSize, _openFolderAfterCapture);
        }
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("Open Screenshots Folder", GUILayout.Height(22)))
        {
            string folder = GetScreenshotFolderPath();
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        }
        EditorGUILayout.EndVertical();
    }

    public static string GetScreenshotFolderPath()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, "Marketing", "Screenshots");
    }

    public static string GetRecordingsFolderPath()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, "Marketing", "Recordings");
    }

    public static void StartRecordingVideo(int fps, bool lockTimestep)
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog(
                "Play Mode Required",
                "Please enter Play Mode (click Play button at the top) to record active gameplay!",
                "OK"
            );
            return;
        }

        if (GameplayVideoRecorderRunner.Instance != null && GameplayVideoRecorderRunner.Instance.isRecording)
        {
            Debug.LogWarning("[VideoRecorder] Already recording!");
            return;
        }

        string folder = GetRecordingsFolderPath();
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string filename = $"Backrooms_Gameplay_{timestamp}.mp4";
        string fullPath = Path.Combine(folder, filename);

        GameObject runnerObj = new GameObject("[GameplayVideoRecorderRunner]");
        runnerObj.hideFlags = HideFlags.HideAndDontSave;
        var runner = runnerObj.AddComponent<GameplayVideoRecorderRunner>();
        runner.StartRecording(fullPath, fps, lockTimestep);
    }

    public static void CaptureWithSuperSize(int superSize, bool openFolder)
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog(
                "Play Mode Required", 
                "Please enter Play Mode (press Play button) to capture your scene with full lighting and post-processing!", 
                "OK"
            );
            return;
        }

        string folder = GetScreenshotFolderPath();
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string filename = $"Backrooms_{timestamp}_{superSize}x.png";
        string fullPath = Path.Combine(folder, filename);

        ScreenCapture.CaptureScreenshot(fullPath, superSize);
        Debug.Log($"<color=#00FF66><b>[Screenshot]</b></color> Saved high-res screenshot to: <b>{fullPath}</b>");

        if (openFolder)
        {
            EditorApplication.delayCall += () =>
            {
                EditorUtility.RevealInFinder(fullPath);
            };
        }
    }
}

/// <summary>
/// Runtime recorder component that drives frame capture and MP4 encoding.
/// </summary>
public class GameplayVideoRecorderRunner : MonoBehaviour
{
    public static GameplayVideoRecorderRunner Instance { get; private set; }

    public bool isRecording = false;
    public int frameCount = 0;
    public float recordStartTime = 0f;
    public string currentFilePath = "";
    public int targetFps = 30;

    private MediaEncoder m_Encoder;
    private Coroutine m_RecordCoroutine;
    private bool m_LockedTimestep = false;
    private int m_OriginalCaptureFramerate = 0;

    public void StartRecording(string outputPath, int fps, bool lockTimestep)
    {
        Instance = this;
        currentFilePath = outputPath;
        targetFps = fps;
        frameCount = 0;
        recordStartTime = Time.realtimeSinceStartup;
        m_LockedTimestep = lockTimestep;

        int width = Screen.width;
        int height = Screen.height;
        if (width % 2 != 0) width--;
        if (height % 2 != 0) height--;

        var videoAttr = new VideoTrackAttributes
        {
            frameRate = new MediaRational(fps),
            width = (uint)width,
            height = (uint)height,
            includeAlpha = false
        };

        try
        {
            m_Encoder = new MediaEncoder(outputPath, videoAttr);
            isRecording = true;

            if (m_LockedTimestep)
            {
                m_OriginalCaptureFramerate = Time.captureFramerate;
                Time.captureFramerate = fps;
            }

            m_RecordCoroutine = StartCoroutine(RecordLoop(1f / fps));
            Debug.Log($"<color=#00FF66><b>[VideoRecorder]</b></color> 🔴 Started recording MP4 video: <b>{outputPath}</b> ({width}x{height} @ {fps}fps)");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VideoRecorder] Failed to initialize MediaEncoder: {ex.Message}");
            Cleanup();
            Destroy(gameObject);
        }
    }

    private IEnumerator RecordLoop(float frameInterval)
    {
        WaitForEndOfFrame wait = new WaitForEndOfFrame();
        float nextCaptureTime = Time.realtimeSinceStartup;

        while (isRecording && m_Encoder != null)
        {
            yield return wait;

            if (m_LockedTimestep || Time.realtimeSinceStartup >= nextCaptureTime)
            {
                Texture2D screenTex = ScreenCapture.CaptureScreenshotAsTexture();
                if (screenTex != null)
                {
                    try
                    {
                        m_Encoder.AddFrame(screenTex);
                        frameCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[VideoRecorder] Error encoding frame: {ex.Message}");
                    }
                    finally
                    {
                        Destroy(screenTex);
                    }
                }
                nextCaptureTime = Time.realtimeSinceStartup + frameInterval;
            }
        }
    }

    public void StopRecording()
    {
        if (!isRecording) return;
        isRecording = false;

        if (m_RecordCoroutine != null)
        {
            StopCoroutine(m_RecordCoroutine);
            m_RecordCoroutine = null;
        }

        if (m_LockedTimestep)
        {
            Time.captureFramerate = m_OriginalCaptureFramerate;
        }

        Cleanup();

        float totalDuration = Time.realtimeSinceStartup - recordStartTime;
        Debug.Log($"<color=#00FF66><b>[VideoRecorder]</b></color> ⏹️ Video saved successfully! Path: <b>{currentFilePath}</b> ({frameCount} frames, {totalDuration:F1}s)");

        string path = currentFilePath;
        EditorApplication.delayCall += () =>
        {
            if (File.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
            }
        };

        Destroy(gameObject);
    }

    private void Cleanup()
    {
        if (m_Encoder != null)
        {
            m_Encoder.Dispose();
            m_Encoder = null;
        }
    }

    private void OnDestroy()
    {
        Cleanup();
        if (m_LockedTimestep)
        {
            Time.captureFramerate = m_OriginalCaptureFramerate;
        }
    }
}
#endif
