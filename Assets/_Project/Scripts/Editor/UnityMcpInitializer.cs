#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Services.Transport.Transports;

[InitializeOnLoad]
public static class UnityMcpInitializer
{
    static UnityMcpInitializer()
    {
        EditorApplication.delayCall += InitializeMcpBridge;
    }

    [MenuItem("Tools/Backrooms/MCP/Connect MCP Bridge", priority = 100)]
    public static void InitializeMcpBridge()
    {
        // 1. Configure MCP to use Stdio transport so mcpforunityserver can connect
        EditorPrefs.SetBool("MCPForUnity.UseHttpTransport", false);

        // 2. Start the Stdio bridge host if not running
        if (!StdioBridgeHost.IsRunning)
        {
            StdioBridgeHost.StartAutoConnect();
            Debug.Log($"<color=#00FF88><b>[Unity MCP]</b> Stdio Bridge Host started on port {StdioBridgeHost.GetCurrentPort()}</color>");
        }
        else
        {
            Debug.Log($"<color=#00FF88><b>[Unity MCP]</b> Stdio Bridge Host already active on port {StdioBridgeHost.GetCurrentPort()}</color>");
        }
    }
}
#endif
