using UnityEngine;
using UnityEngine.InputSystem;
using System;

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
        [SerializeField] private float acceleration = 16f;
        [SerializeField] private float deceleration = 20f;

        private CharacterController _controller;
        private Vector2 _virtualInput;
        private Vector3 _velocity;
        private float _verticalVelocity;
        private float _interactionTimer;
        private readonly Collider[] _interactionHits = new Collider[16];

        public event Action InteractionChanged;
        public NpcActor NearbyNpc { get; private set; }
        public LoreActor NearbyLore { get; private set; }

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

        public void SetVirtualInput(Vector2 input)
        {
            _virtualInput = Vector2.ClampMagnitude(input, 1f);
        }

        public void Interact()
        {
            if (NearbyNpc != null)
            {
                InteractionChanged?.Invoke();
                return;
            }

            if (NearbyLore != null)
            {
                InteractionChanged?.Invoke();
            }
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            Vector2 input = _virtualInput;
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;

            if (keyboard != null && input.sqrMagnitude < 0.01f)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) { input.y += 1f; }
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) { input.y -= 1f; }
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) { input.x -= 1f; }
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) { input.x += 1f; }
            }

            if (gamepad != null && input.sqrMagnitude < 0.01f)
            {
                var stick = gamepad.leftStick.ReadValue();
                input += stick;
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

            Vector3 desiredVelocity = move * speed;
            float response = move.sqrMagnitude > 0.01f ? acceleration : deceleration;
            _velocity = Vector3.MoveTowards(_velocity, desiredVelocity, response * Time.deltaTime);
            if (_controller.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -1f;
            }

            _verticalVelocity += Physics.gravity.y * Time.deltaTime;
            _controller.Move((_velocity + Vector3.up * _verticalVelocity) * Time.deltaTime);

            if (move.sqrMagnitude > 0.01f)
            {
                var targetRotation = Quaternion.LookRotation(new Vector3(move.x, 0f, move.z));
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
            }

            ClampToRegion();
            FollowWithCamera();
            ScanForInteractions();
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

            Vector3 target = transform.position + Vector3.up * 1.5f;
            Vector3 desired = transform.position + cameraOffset;
            var direction = desired - target;
            if (Physics.Raycast(target, direction.normalized, out var hit, direction.magnitude, ~0, QueryTriggerInteraction.Ignore) &&
                !hit.transform.IsChildOf(transform))
            {
                desired = hit.point - direction.normalized * 0.25f;
            }

            followCamera.position = Vector3.Lerp(followCamera.position, desired, 1f - Mathf.Exp(-10f * Time.deltaTime));
            followCamera.LookAt(target);
        }

        private void ScanForInteractions()
        {
            _interactionTimer -= Time.deltaTime;
            if (_interactionTimer > 0f)
            {
                return;
            }

            _interactionTimer = 0.25f;
            NpcActor nearestNpc = null;
            LoreActor nearestLore = null;
            float npcDistance = float.MaxValue;
            float loreDistance = float.MaxValue;
            int count = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up, 2.5f, _interactionHits,
                ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var hit = _interactionHits[i];
                if (hit == null) continue;
                float distance = (hit.transform.position - transform.position).sqrMagnitude;
                var npc = hit.GetComponentInParent<NpcActor>();
                if (npc != null && distance < npcDistance)
                {
                    nearestNpc = npc;
                    npcDistance = distance;
                }

                var lore = hit.GetComponentInParent<LoreActor>();
                if (lore != null && distance < loreDistance)
                {
                    nearestLore = lore;
                    loreDistance = distance;
                }
            }

            if (nearestNpc != NearbyNpc || nearestLore != NearbyLore)
            {
                NearbyNpc = nearestNpc;
                NearbyLore = nearestLore;
                InteractionChanged?.Invoke();
            }
        }
    }
}
