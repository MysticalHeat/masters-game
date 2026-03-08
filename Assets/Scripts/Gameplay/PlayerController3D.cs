using UnityEngine;
using UnityEngine.InputSystem;

namespace MastersGame.Gameplay
{
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerController3D : MonoBehaviour
    {
        [SerializeField] private Transform cameraPitchRoot;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private float moveSpeed = 4.5f;
        [SerializeField] private float sprintMultiplier = 1.5f;
        [SerializeField] private float lookSensitivity = 0.14f;
        [SerializeField] private float gravity = -18f;
        [SerializeField] private float pitchMin = -70f;
        [SerializeField] private float pitchMax = 75f;

        private CharacterController characterController;
        private PlayerInput playerInput;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction sprintAction;
        private float verticalVelocity;
        private float pitch;
        private bool inputEnabled = true;

        public Camera PlayerCamera => playerCamera;

        public void ConfigureSceneReferences(Transform pitchTransform, Camera controlledCamera)
        {
            cameraPitchRoot = pitchTransform;
            playerCamera = controlledCamera;
        }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            playerInput = GetComponent<PlayerInput>();
            moveAction = playerInput.actions["Move"];
            lookAction = playerInput.actions["Look"];
            sprintAction = playerInput.actions["Sprint"];
        }

        private void Start()
        {
            SetCursorLocked(true);
        }

        private void Update()
        {
            if (!inputEnabled)
            {
                ApplyGravity();
                return;
            }

            var moveInput = moveAction.ReadValue<Vector2>();
            var lookInput = lookAction.ReadValue<Vector2>();
            var sprintMultiplierValue = sprintAction != null && sprintAction.IsPressed() ? sprintMultiplier : 1f;

            UpdateLook(lookInput);
            UpdateMovement(moveInput, sprintMultiplierValue);
        }

        public void SetInputEnabled(bool value)
        {
            inputEnabled = value;
        }

        public void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void UpdateMovement(Vector2 moveInput, float speedMultiplier)
        {
            var desiredMove = (transform.forward * moveInput.y) + (transform.right * moveInput.x);
            desiredMove.y = 0f;

            if (desiredMove.sqrMagnitude > 1f)
            {
                desiredMove.Normalize();
            }

            var horizontalVelocity = desiredMove * (moveSpeed * speedMultiplier);
            ApplyGravity();
            horizontalVelocity.y = verticalVelocity;

            characterController.Move(horizontalVelocity * Time.deltaTime);
        }

        private void ApplyGravity()
        {
            if (characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }

            if (!inputEnabled)
            {
                var gravityStep = new Vector3(0f, verticalVelocity, 0f);
                characterController.Move(gravityStep * Time.deltaTime);
            }
        }

        private void UpdateLook(Vector2 lookInput)
        {
            if (lookInput.sqrMagnitude <= 0f)
            {
                return;
            }

            var yaw = lookInput.x * lookSensitivity;
            var pitchDelta = lookInput.y * lookSensitivity;

            transform.Rotate(0f, yaw, 0f, Space.Self);

            if (cameraPitchRoot == null)
            {
                return;
            }

            pitch = Mathf.Clamp(pitch - pitchDelta, pitchMin, pitchMax);
            cameraPitchRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}