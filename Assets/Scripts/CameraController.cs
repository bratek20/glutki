using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [SerializeField] private float scrollZoomSpeed = 0.01f;
    [SerializeField] private float minOrthographicSize = 2f;
    [SerializeField] private float maxOrthographicSize = 15f;

    private Camera cam;

    private bool isDragging;
    private Vector3 dragAnchorWorld;

    private bool isPinching;
    private float pinchStartDistance;
    private float pinchStartOrthoSize;
    private Vector3 pinchAnchorWorld;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
    }

    private void OnDisable()
    {
        EnhancedTouchSupport.Disable();
    }

    private void Update()
    {
        var touches = Touch.activeTouches;

        if (touches.Count >= 2)
        {
            isDragging = false;
            UpdatePinch(touches[0], touches[1]);
            return;
        }

        isPinching = false;

        if (touches.Count == 1)
        {
            Touch touch = touches[0];
            bool released = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
            UpdateDrag(touch.screenPosition, touch.phase == TouchPhase.Began, released, held: true);
        }
        else
        {
            UpdateMouse();
        }
    }

    private void UpdateMouse()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        UpdateDrag(mouse.position.ReadValue(), mouse.leftButton.wasPressedThisFrame, mouse.leftButton.wasReleasedThisFrame, mouse.leftButton.isPressed);

        float scroll = mouse.scroll.ReadValue().y;
        if (!Mathf.Approximately(scroll, 0f))
        {
            ZoomAroundScreenPoint(-scroll * scrollZoomSpeed, mouse.position.ReadValue());
        }
    }

    private void UpdateDrag(Vector2 screenPosition, bool pressed, bool released, bool held)
    {
        if (pressed)
        {
            isDragging = true;
            dragAnchorWorld = cam.ScreenToWorldPoint(screenPosition);
        }
        else if (isDragging && held)
        {
            Vector3 currentWorld = cam.ScreenToWorldPoint(screenPosition);
            transform.position += dragAnchorWorld - currentWorld;
        }

        if (released || !held) isDragging = false;
    }

    private void UpdatePinch(Touch touchA, Touch touchB)
    {
        float currentDistance = Vector2.Distance(touchA.screenPosition, touchB.screenPosition);
        Vector2 midpoint = (touchA.screenPosition + touchB.screenPosition) * 0.5f;

        bool justStarted = !isPinching || touchA.phase == TouchPhase.Began || touchB.phase == TouchPhase.Began;
        if (justStarted)
        {
            isPinching = true;
            pinchStartDistance = Mathf.Max(currentDistance, 0.01f);
            pinchStartOrthoSize = cam.orthographicSize;
            pinchAnchorWorld = cam.ScreenToWorldPoint(midpoint);
            return;
        }

        float ratio = pinchStartDistance / Mathf.Max(currentDistance, 0.01f);
        cam.orthographicSize = Mathf.Clamp(pinchStartOrthoSize * ratio, minOrthographicSize, maxOrthographicSize);

        Vector3 midpointWorldNow = cam.ScreenToWorldPoint(midpoint);
        transform.position += pinchAnchorWorld - midpointWorldNow;
    }

    private void ZoomAroundScreenPoint(float sizeDelta, Vector2 screenPosition)
    {
        Vector3 anchorWorldBefore = cam.ScreenToWorldPoint(screenPosition);
        cam.orthographicSize = Mathf.Clamp(cam.orthographicSize + sizeDelta, minOrthographicSize, maxOrthographicSize);
        Vector3 anchorWorldAfter = cam.ScreenToWorldPoint(screenPosition);
        transform.position += anchorWorldBefore - anchorWorldAfter;
    }
}
