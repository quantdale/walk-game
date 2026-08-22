using UnityEngine;
using UnityEngine.InputSystem;

namespace WalkGame.World
{
    /// <summary>
    /// Minimal third-person controller for Explore View. Movement is camera-relative,
    /// the character stays inside the bounded region, and there is no combat or physics
    /// trickery - Explore exists to walk through the restored region.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class ExploreCharacterController : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 4.5f;
        [SerializeField] private float runSpeed = 7.5f;
        [SerializeField] private float turnSpeed = 12f;
        [SerializeField] private Transform followCamera;
        [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 6f, -8f);
        [SerializeField] private Vector2 regionBounds = new Vector2(31f, 31f);

        private CharacterController _controller;

        public void SetBounds(Vector2 bounds)
        {
            regionBounds = bounds;
        }

        public void TeleportTo(Vector3 worldPosition)
        {
            _controller.enabled = false;
            transform.position = new Vector3(worldPosition.x, 0.1f, worldPosition.z);
            _controller.enabled = true;
        }

        public void AttachCamera(Transform cameraTransform)
        {
            followCamera = cameraTransform;
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            Vector2 input = Vector2.zero;
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;
            var touchscreen = Touchscreen.current;

            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) { input.y += 1f; }
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) { input.y -= 1f; }
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) { input.x -= 1f; }
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) { input.x += 1f; }
            }

            if (gamepad != null)
            {
                var stick = gamepad.leftStick.ReadValue();
                input += stick;
            }

            if (touchscreen != null && touchscreen.primaryTouch.press.isPressed)
            {
                // Simple mobile drag-to-walk: touch position relative to screen center.
                var pos = touchscreen.primaryTouch.position.ReadValue();
                var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                var delta = pos - center;
                input = delta.sqrMagnitude > 100f
                    ? new Vector2(delta.x / (Screen.width * 0.35f), delta.y / (Screen.height * 0.35f))
                    : Vector2.zero;
                input = Vector2.ClampMagnitude(input, 1f);
            }

            bool running = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            float speed = running ? runSpeed : moveSpeed;

            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;
            if (followCamera != null)
            {
                Vector3 flatForward = followCamera.forward;
                flatForward.y = 0f;
                if (flatForward.sqrMagnitude > 0.001f)
                {
                    forward = flatForward.normalized;
                }

                right = Quaternion.Euler(0f, 90f, 0f) * forward;
            }

            Vector3 move = (forward * Mathf.Clamp(input.y, -1f, 1f)) + (right * Mathf.Clamp(input.x, -1f, 1f));
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            _controller.Move(move * (speed * Time.deltaTime) + Physics.gravity * Time.deltaTime);

            if (move.sqrMagnitude > 0.01f)
            {
                var targetRotation = Quaternion.LookRotation(new Vector3(move.x, 0f, move.z));
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
            }

            ClampToRegion();
            FollowWithCamera();
        }

        private void ClampToRegion()
        {
            var p = transform.position;
            p.x = Mathf.Clamp(p.x, 1f, regionBounds.x);
            p.z = Mathf.Clamp(p.z, 1f, regionBounds.y);
            if (p.y < -5f)
            {
                p.y = 0.1f; // safety net against falling through gray-box ground
            }

            transform.position = p;
        }

        private void FollowWithCamera()
        {
            if (followCamera == null)
            {
                return;
            }

            followCamera.position = transform.position + cameraOffset;
            followCamera.LookAt(transform.position + Vector3.up * 1.5f);
        }
    }
}
