using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace WalkGame.World
{
    /// <summary>
    /// Builder camera: pan, pinch zoom, rotate, and tap-select against BuildingActors.
    /// Input System based; all placement mutations go through the placement service in
    /// the App layer - this controller only produces camera/selection intent.
    /// </summary>
    public sealed class BuilderCameraController : MonoBehaviour
    {
        [SerializeField] private Camera cameraComponent;
        [SerializeField] private float panSpeed = 12f;
        [SerializeField] private float zoomSpeed = 10f;
        [SerializeField] private float minZoom = 8f;
        [SerializeField] private float maxZoom = 60f;
        [SerializeField] private Vector2 areaBounds = new Vector2(34f, 34f);

        public System.Action<WalkGame.World.BuildingActor> BuildingTapped;
        public System.Action<Vector3> GroundTapped;

        public Camera CameraComponent => cameraComponent != null ? cameraComponent : GetComponent<Camera>();

        private Vector3 _focusPoint = new Vector3(16f, 0f, 16f);
        private float _distance = 30f;
        private float _orbitYaw;

        private Vector2 _previousPointerPos;
        private bool _dragging;
        private float _previousPinchDistance;

        public bool IsDragging => _dragging;

        private void Awake()
        {
            if (cameraComponent == null)
            {
                cameraComponent = GetComponent<Camera>();
            }
        }

        private void OnEnable()
        {
            ApplyTransform();
        }

        private void Update()
        {
            HandleTouchAndMouse();
            ClampFocus();
            ApplyTransform();
        }

        private void HandleTouchAndMouse()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var primary = touchscreen.primaryTouch;
                var pos = touchscreen.primaryTouch.position.ReadValue();
                if (primary.press.wasPressedThisFrame)
                {
                    _previousPointerPos = pos;
                    _dragging = false;
                }

                if (primary.press.isPressed)
                {
                    if (touchscreen.secondaryTouch.isPressed)
                    {
                        // Pinch zoom with two touches. Use the full screen-space
                        // distance so portrait and landscape behave consistently.
                        float pinch = Vector2.Distance(
                            primary.position.ReadValue(),
                            touchscreen.secondaryTouch.position.ReadValue());
                        if (_previousPinchDistance > 0f)
                        {
                            _distance = Mathf.Clamp(_distance - (pinch - _previousPinchDistance) * zoomSpeed * 0.05f, minZoom, maxZoom);
                        }

                        _previousPinchDistance = pinch;
                        return;
                    }

                    _previousPinchDistance = 0f;
                    var delta = pos - _previousPointerPos;
                    if (delta.sqrMagnitude > 4f)
                    {
                        _dragging = true;
                        Pan(delta);
                        _previousPointerPos = pos;
                    }
                }

                if (primary.press.wasReleasedThisFrame)
                {
                    if (!_dragging)
                    {
                        TapAt(pos, primary.touchId.ReadValue());
                    }

                    _dragging = false;
                    _previousPinchDistance = 0f;
                }

                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (mouse.scroll.ReadValue().y != 0f)
            {
                _distance = Mathf.Clamp(_distance - mouse.scroll.ReadValue().y * zoomSpeed * 0.01f, minZoom, maxZoom);
            }

            if (mouse.leftButton.isPressed)
            {
                var pos = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    _previousPointerPos = pos;
                    _dragging = false;
                }

                var delta = pos - _previousPointerPos;
                if (delta.sqrMagnitude > 4f)
                {
                    _dragging = true;
                    Pan(delta);
                    _previousPointerPos = pos;
                }
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (!_dragging)
                {
                    TapAt(mouse.position.ReadValue(), -1);
                }

                _dragging = false;
            }
        }

        private void Pan(Vector2 screenDelta)
        {
            // Screen-space drag pans the focus across the ground plane.
            Vector3 right = Quaternion.Euler(0f, _orbitYaw, 0f) * Vector3.right;
            Vector3 forward = Quaternion.Euler(0f, _orbitYaw, 0f) * Vector3.forward;
            _focusPoint -= right * (screenDelta.x * panSpeed * 0.02f * _distance / 30f);
            _focusPoint += forward * (screenDelta.y * panSpeed * 0.02f * _distance / 30f);
        }

        private void TapAt(Vector2 screenPosition, int pointerId)
        {
            var cam = CameraComponent;
            if (cam == null || BuildingTapped == null)
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId))
            {
                return;
            }

            Ray ray = cam.ScreenPointToRay(new Vector3(screenPosition.x, screenPosition.y, 0f));
            if (Physics.Raycast(ray, out var hit, 500f))
            {
                var actor = hit.collider.GetComponentInParent<BuildingActor>();
                if (actor != null)
                {
                    BuildingTapped.Invoke(actor);
                    return;
                }

                GroundTapped?.Invoke(hit.point);
                BuildingTapped.Invoke(null);
                return;
            }

            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out var distance))
            {
                GroundTapped?.Invoke(ray.GetPoint(distance));
            }
            BuildingTapped.Invoke(null);
        }

        private void ClampFocus()
        {
            _focusPoint.x = Mathf.Clamp(_focusPoint.x, -4f, areaBounds.x);
            _focusPoint.z = Mathf.Clamp(_focusPoint.z, -4f, areaBounds.y);
        }

        public void SetOrbitYaw(float yawDegrees)
        {
            _orbitYaw = yawDegrees;
        }

        public void SetAreaBounds(Vector2 bounds)
        {
            areaBounds = bounds;
            _focusPoint = new Vector3(bounds.x * 0.5f, 0f, bounds.y * 0.5f);
        }

        private void ApplyTransform()
        {
            var cam = CameraComponent;
            if (cam == null)
            {
                return;
            }

            Quaternion rotation = Quaternion.Euler(50f, _orbitYaw, 0f);
            cam.transform.position = _focusPoint + rotation * new Vector3(0f, 0f, -_distance);
            cam.transform.rotation = rotation;
        }
    }
}
