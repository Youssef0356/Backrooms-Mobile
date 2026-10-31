#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// Phase 0 (Level 0 Overhaul): Real-time diagnostic HUD for Editor and Development builds.
/// Press F3 to toggle overlay showing:
/// - Bacteria (VillainAI) state, world & walking distance, and teleport timer
/// - Smily Hound (SmilyHoundAI) state, detection meter, and line-of-sight
/// - NavMesh walking-path distance to the Exit Portal
/// </summary>
public class Level0DebugHUD : MonoBehaviour
{
    public bool showHud = false;

    private Transform player;
    private Transform exitZone;
    private float nextPathUpdateTime;
    private float portalWalkDistance = -1f;
    private float villainWalkDistance = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreateInDevBuilds()
    {
        if (FindAnyObjectByType<Level0DebugHUD>() == null)
        {
            GameObject go = new GameObject("Level0DebugHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<Level0DebugHUD>();
        }
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
        {
            showHud = !showHud;
        }

        if (!showHud) return;

        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }

        if (exitZone == null)
        {
            var ez = FindAnyObjectByType<BackroomsExitZone>();
            if (ez != null) exitZone = ez.transform;
        }

        if (Time.unscaledTime >= nextPathUpdateTime && player != null)
        {
            nextPathUpdateTime = Time.unscaledTime + 0.5f;
            NavMeshPath path = new NavMeshPath();

            if (exitZone != null &&
                NavMesh.SamplePosition(player.position, out NavMeshHit pHit, 3f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(exitZone.position, out NavMeshHit eHit, 4f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(pHit.position, eHit.position, NavMesh.AllAreas, path))
            {
                portalWalkDistance = MeasurePath(path);
            }

            if (VillainAI.Instance != null &&
                NavMesh.SamplePosition(player.position, out NavMeshHit vpHit, 3f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(VillainAI.Instance.transform.position, out NavMeshHit vHit, 4f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(vpHit.position, vHit.position, NavMesh.AllAreas, path))
            {
                villainWalkDistance = MeasurePath(path);
            }
        }
    }

    private static float MeasurePath(NavMeshPath path)
    {
        if (path == null || path.corners == null || path.corners.Length < 2) return 0f;
        float sum = 0f;
        for (int i = 1; i < path.corners.Length; i++)
        {
            sum += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }
        return sum;
    }

    private void OnGUI()
    {
        if (!showHud) return;

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 13,
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(10, 10, 8, 8)
        };
        boxStyle.normal.textColor = new Color(0.85f, 1.0f, 0.85f);

        string vStatus = "Missing";
        if (VillainAI.Instance != null)
        {
            var v = VillainAI.Instance;
            float dist = player != null ? Vector3.Distance(player.position, v.transform.position) : 0f;
            vStatus = $"{v.gameObject.name} | State: {v.CurrentState} | Scale: {v.transform.localScale.x:F1}x\n" +
                      $"  Dist: {dist:F1}m (Walk: {villainWalkDistance:F1}m) | Teleport: {v.TeleportTimer:F0}s / {v.NextTeleportThreshold:F0}s";
        }

        string hStatus = "Missing";
        if (SmilyHoundAI.Instance != null)
        {
            var h = SmilyHoundAI.Instance;
            float dist = player != null ? Vector3.Distance(player.position, h.transform.position) : 0f;
            hStatus = $"State: {h.CurrentState} | Sight: {h.PlayerInSight} | Detection: {(h.detectionMeter * 100f):F0}% | Dist: {dist:F1}m";
        }

        string pStatus = exitZone != null
            ? $"Linear: {(player != null ? Vector3.Distance(player.position, exitZone.position) : 0f):F1}m | Walk Path: {portalWalkDistance:F1}m"
            : "Not spawned yet";

        string text = $"<b>[LEVEL 0 DIAGNOSTICS - F3]</b>\n" +
                      $"• <b>Bacteria:</b> {vStatus}\n" +
                      $"• <b>Smily Hound:</b> {hStatus}\n" +
                      $"• <b>Exit Portal:</b> {pStatus}";

        GUI.Box(new Rect(14f, 14f, 520f, 105f), text, boxStyle);
    }
}
#endif
