using UnityEngine;
using UnityEngine.EventSystems;
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
    [SerializeField] private float baseViewOrthographicSize = 5f;

    private Camera cam;

    private bool isDragging;
    private Vector3 dragAnchorWorld;

    private bool isPinching;
    private float pinchStartDistance;
    private float pinchStartOrthoSize;
    private Vector3 pinchAnchorWorld;

    private Vector3 savedWorldPosition;
    private float savedWorldOrthoSize;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
        ViewManager.ViewChanged += OnViewChanged;
    }

    private void OnDisable()
    {
        EnhancedTouchSupport.Disable();
        ViewManager.ViewChanged -= OnViewChanged;
    }

    private void OnViewChanged()
    {
        // Whatever click/touch triggered this view switch may still be held down on the next
        // frame. Without this, a stale drag/pinch anchor computed under the OLD camera framing
        // gets compared against the NEW one, producing a huge bogus delta that yanks the camera
        // away from center the instant the switch happens.
        isDragging = false;
        isPinching = false;

        if (ViewManager.CurrentView == ViewMode.Base)
        {
            savedWorldPosition = transform.position;
            savedWorldOrthoSize = cam.orthographicSize;

            CenterOnQueen(ViewManager.ViewedBase);
            cam.orthographicSize = baseViewOrthographicSize;
        }
        else
        {
            transform.position = savedWorldPosition;
            cam.orthographicSize = savedWorldOrthoSize;
        }
    }

    private void Update()
    {
        var touches = Touch.activeTouches;

        if (touches.Count >= 2)
        {
            isDragging = false;
            UpdatePinch(touches[0], touches[1]);
        }
        else
        {
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

        ClampToBaseViewBounds();
    }

    private void UpdateMouse()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        UpdateDrag(mouse.position.ReadValue(), mouse.leftButton.wasPressedThisFrame, mouse.leftButton.wasReleasedThisFrame, mouse.leftButton.isPressed);

        float scroll = mouse.scroll.ReadValue().y;
        if (!Mathf.Approximately(scroll, 0f) && !IsPointerOverUi())
        {
            ZoomAroundScreenPoint(-scroll * scrollZoomSpeed, mouse.position.ReadValue());
        }
    }

    // Same guard PlayerBase/BotBase use before acting on a click - without it, starting a drag on
    // a UI element (e.g. dragging AttackOrderPopup's Slider) also pans the camera underneath it.
    private static bool IsPointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private void UpdateDrag(Vector2 screenPosition, bool pressed, bool released, bool held)
    {
        if (pressed)
        {
            if (!IsPointerOverUi())
            {
                isDragging = true;
                dragAnchorWorld = cam.ScreenToWorldPoint(screenPosition);
            }
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

    // Center on the Queen's actual position rather than the InteriorCenter formula, so this stays
    // correct even if a base's interior layout ever changes. Only X/Y move - the camera's own Z
    // depth must be preserved, since the interior sits at world Z 0 same as every sprite.
    private void CenterOnQueen(PlayerBase viewedBase)
    {
        Vector3 center = viewedBase.Queen != null ? viewedBase.Queen.transform.position : viewedBase.InteriorCenter;
        transform.position = new Vector3(center.x, center.y, transform.position.z);
    }

    // Base interiors have a fixed footprint (Base.InteriorHalfSize) - keep the camera's own
    // viewport from panning past the room's edges while inspecting/managing that base.
    private void ClampToBaseViewBounds()
    {
        if (ViewManager.CurrentView != ViewMode.Base) return;

        PlayerBase viewedBase = ViewManager.ViewedBase;
        if (viewedBase == null) return;

        Vector2 viewportHalfSize = new Vector2(cam.orthographicSize * cam.aspect, cam.orthographicSize);
        Vector2 interiorHalfSize = viewedBase.InteriorHalfSize;
        Vector3 center = viewedBase.InteriorCenter;

        float clampX = Mathf.Max(interiorHalfSize.x - viewportHalfSize.x, 0f);
        float clampY = Mathf.Max(interiorHalfSize.y - viewportHalfSize.y, 0f);

        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, center.x - clampX, center.x + clampX);
        pos.y = Mathf.Clamp(pos.y, center.y - clampY, center.y + clampY);
        transform.position = pos;
    }
}
