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
    /// </summary>
    void Apply()
    {
        if (document == null) return;
        if (targetCamera == null) targetCamera = GetComponentInParent<Camera>();
        if (targetCamera == null) return;

        if (transform.parent != targetCamera.transform)
            transform.SetParent(targetCamera.transform, false);

        Vector3 wantedPosition = new Vector3(0f, 0f, distance);
        if (transform.localPosition != wantedPosition) transform.localPosition = wantedPosition;
        if (transform.localRotation != Quaternion.identity) transform.localRotation = Quaternion.identity;

        float aspect = CurrentAspect(targetCamera);

        // Virtual resolution of the menu. It never gets bigger than the render
        // target, otherwise the panel would not fit inside the UI Toolkit panel.
        float pixelHeight = targetCamera.pixelHeight;
        if (pixelHeight <= 0f) pixelHeight = referenceHeight;
        pixelHeight = Mathf.Min(referenceHeight, pixelHeight);
        float pixelWidth = pixelHeight * aspect;

        // keep the virtual resolution in sync with the screen so nothing is stretched
        if (document.worldSpaceSizeMode == WorldSpaceSizeMode.Fixed &&
            (document.worldSpaceSize.x != pixelWidth || document.worldSpaceSize.y != pixelHeight))
        {
            document.worldSpaceSize = new Vector2(pixelWidth, pixelHeight);
        }

        float viewHeight = 2f * distance * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float viewWidth = viewHeight * aspect;

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
