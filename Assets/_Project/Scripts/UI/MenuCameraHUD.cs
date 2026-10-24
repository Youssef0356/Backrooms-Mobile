using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Keeps the world space UI panel glued to the camera and stretched so that it
/// covers the camera frustum exactly, whatever the screen aspect ratio is.
/// The menu therefore feels like a screen that lives inside the level instead
/// of a flat overlay floating on top of it.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(UIDocument))]
public class MenuCameraHUD : MonoBehaviour
{
    [Tooltip("Camera the menu is attached to. Empty = the parent camera.")]
    [SerializeField] Camera targetCamera;

    [Tooltip("How far in front of the camera the panel sits (metres).")]
    [Min(0.05f)]
    [SerializeField] float distance = 1.5f;

    [Tooltip("Height of the UI in virtual pixels. The width follows the screen aspect.")]
    [Min(180f)]
    [SerializeField] float referenceHeight = 1080f;

    [Tooltip("Must match 'Pixels Per Unit' of the Panel Settings asset used by the UI document.")]
    [Min(1f)]
    [SerializeField] float pixelsPerUnit = 100f;

    UIDocument document;

    void OnEnable()
    {
        document = GetComponent<UIDocument>();
        if (targetCamera == null) targetCamera = GetComponentInParent<Camera>();

        if (document != null)
        {
            // the panel is drawn centred on the GameObject origin
            document.pivot = Pivot.Center;
            document.pivotReferenceSize = PivotReferenceSize.Layout;
        }

        Apply();
    }

    void LateUpdate()
    {
        Apply();
    }

    /// <summary>
    /// Parents the panel to the camera and matches its size to the frustum.
    /// Uses camera ViewportToWorldPoint to guarantee perfect edge fitting across all
    /// physical lens settings, aspect ratios, and mobile resolutions.
    /// </summary>
    void Apply()
    {
        if (document == null) return;
        if (targetCamera == null) targetCamera = GetComponentInParent<Camera>();
        if (targetCamera == null) return;

        if (transform.parent != targetCamera.transform)
            transform.SetParent(targetCamera.transform, false);

        // Calculate exact viewport bounds at panel distance
        Vector3 blWorld = targetCamera.ViewportToWorldPoint(new Vector3(0f, 0f, distance));
        Vector3 trWorld = targetCamera.ViewportToWorldPoint(new Vector3(1f, 1f, distance));

        Vector3 blLocal = targetCamera.transform.InverseTransformPoint(blWorld);
        Vector3 trLocal = targetCamera.transform.InverseTransformPoint(trWorld);

        float viewWidth = Mathf.Abs(trLocal.x - blLocal.x);
        float viewHeight = Mathf.Abs(trLocal.y - blLocal.y);
        Vector3 centerLocal = (blLocal + trLocal) * 0.5f;

        // Apply a 1% overscan so the world space layer stretches seamlessly to all edges
        viewWidth *= 1.01f;
        viewHeight *= 1.01f;

        float screenAspect = viewWidth / Mathf.Max(0.001f, viewHeight);
        float pixelHeight = targetCamera.pixelHeight > 0f ? targetCamera.pixelHeight : referenceHeight;
        float pixelWidth = pixelHeight * screenAspect;

        if (document.worldSpaceSizeMode == WorldSpaceSizeMode.Fixed &&
            (document.worldSpaceSize.x != pixelWidth || document.worldSpaceSize.y != pixelHeight))
        {
            document.worldSpaceSize = new Vector2(pixelWidth, pixelHeight);
        }

        document.pivot = Pivot.Center;
        document.pivotReferenceSize = PivotReferenceSize.Layout;

        if (transform.localPosition != centerLocal) transform.localPosition = centerLocal;
        if (transform.localRotation != Quaternion.identity) transform.localRotation = Quaternion.identity;

        Vector3 wantedScale = new Vector3(
            viewWidth * pixelsPerUnit / pixelWidth,
            viewHeight * pixelsPerUnit / pixelHeight,
            1f);

        if (transform.localScale != wantedScale) transform.localScale = wantedScale;
    }

    /// <summary>
    /// Aspect of the camera view. The camera is the authority here, it knows the
    /// real aspect of the render target (screen aspect on mobile).
    /// </summary>
    static float CurrentAspect(Camera camera)
    {
        float aspect = camera.aspect;
        if (aspect <= 0f || float.IsNaN(aspect)) return 16f / 9f;
        return aspect;
    }
}
