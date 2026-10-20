#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// Temporary diagnostic: prints how the world space menu panel is actually sized.
[InitializeOnLoad]
public static class MenuDiagnostics
{
    static MenuDiagnostics()
    {
        EditorApplication.delayCall += Report;
    }

    static void Report()
    {
        UIDocument doc = Object.FindAnyObjectByType<UIDocument>();
        if (doc == null)
        {
            Debug.Log("[Diag] no UIDocument in the open scene");
            return;
        }

        VisualElement root = doc.rootVisualElement;
        IPanel panel = root.panel;

        string panelInfo = panel == null
            ? "panel: null"
            : $"panel visualTree layout={panel.visualTree.layout} rect={panel.visualTree.contentRect}";

        Camera cam = doc.transform.parent != null ? doc.transform.parent.GetComponent<Camera>() : null;
        string camInfo = "no parent camera";
        if (cam != null)
        {
            float d = Vector3.Dot(doc.transform.position - cam.transform.position, cam.transform.forward);
            float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float w = h * cam.aspect;
            camInfo = $"camera fov={cam.fieldOfView} aspect={cam.aspect} dist={d:F3} frustum={w:F3}x{h:F3}m";
        }

        Debug.Log($"[Diag] {panelInfo}\n" +
                  $"[Diag] root resolved={root.resolvedStyle.width}x{root.resolvedStyle.height} layout={root.layout}\n" +
                  $"[Diag] worldBound={root.worldBound}\n" +
                  $"[Diag] worldSpaceSize={doc.worldSpaceSize} mode={doc.worldSpaceSizeMode} pivot={doc.pivot}/{doc.pivotReferenceSize}\n" +
                  $"[Diag] transform parent={doc.transform.parent?.name} localPos={doc.transform.localPosition} localScale={doc.transform.localScale}\n" +
                  $"[Diag] {camInfo}\n" +
                  $"[Diag] screen={Screen.width}x{Screen.height}");
    }
}
#endif
