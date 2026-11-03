using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.UI;

namespace MinecraftClone.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement Settings")]
        public float walkSpeed = 4.3f;
        public float runSpeed = 5.6f;
        public float jumpHeight = 1.25f;
        public float gravity = -20f;

        [Header("Mouse Look Settings")]
        public float mouseSensitivity = 2.0f;
        public float minPitch = -89f;
        public float maxPitch = 89f;

        [Header("Water Physics Settings")]
        public float waterSpeedMultiplier = 0.8f;
        public float waterUpAcceleration = 25f;
        public float waterGravity = -6f;
        public float maxSwimUpSpeed = 3.2f;
        public float maxSwimDownSpeed = -2.5f;

        [Header("Lava Physics Settings")]
        public float lavaSpeedMultiplier = 0.45f;
        public float lavaUpAcceleration = 15f;
        public float lavaGravity = -10f;
        public float maxLavaSwimUpSpeed = 1.8f;
        public float maxLavaSwimDownSpeed = -2.0f;

        [Header("Offsets & Check Points")]
        public float feetOffset = 0.1f;
        public float waistOffset = 0.9f;
        public float eyesOffset = 1.6f;

        private CharacterController controller;
        private Camera playerCamera;
        private Vector3 velocity;
        private bool isGrounded;
        private float cameraPitch = 0f;

        private bool isFeetInWater, isWaistInWater, isEyesInWater;
        private bool isFeetInLava, isWaistInLava, isEyesInLava;

        private PauseMenuUI _pauseMenu;
        private bool isFrozen = true;

        public bool IsFrozen => isFrozen;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            playerCamera = GetComponentInChildren<Camera>();
        }

        private void Start()
        {
            _pauseMenu = PauseMenuUI.Instance != null ? PauseMenuUI.Instance : FindFirstObjectByType<PauseMenuUI>();
            SetFrozen(true);
        }

        public void SetFrozen(bool freeze)
        {
            isFrozen = freeze;
            velocity = Vector3.zero;

            if (controller != null)
            {
                controller.enabled = !freeze;
            }

            UpdateCursorState(freeze);
        }

        private void Update()
        {
            if (isFrozen)
            {
                UpdateCursorState(true);
                return;
            }

            if (controller == null || !controller.enabled)
            {
                return;
            }

            if (_pauseMenu == null)
            {
                _pauseMenu = PauseMenuUI.Instance != null ? PauseMenuUI.Instance : FindFirstObjectByType<PauseMenuUI>();
            }

            bool isLoading = RuntimeLoadingScreen.IsShown;
            bool isPaused = (_pauseMenu != null && _pauseMenu.IsPaused) || Time.timeScale == 0f || isLoading || ChatUI.IsOpen || InventoryUI.IsOpen;

            UpdateCursorState(isPaused);

            if (!isPaused)
            {
                HandleMouseLook();
            }

            CheckFluidStates();
            HandleMovement(isPaused);
        }

        private void UpdateCursorState(bool forceUnlock = false)
        {
            if (forceUnlock)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void HandleMouseLook()
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * GameSettings.Sensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * GameSettings.Sensitivity;

            transform.Rotate(Vector3.up * mouseX);

            cameraPitch -= mouseY;
            cameraPitch = Mathf.Clamp(cameraPitch, minPitch, maxPitch);

            if (playerCamera != null)
            {
                playerCamera.transform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
            }
        }

        private void CheckFluidStates()
        {
            if (WorldManager.Instance == null) return;

            Vector3 pos = transform.position;

            Vector3 feetPos = pos + Vector3.up * feetOffset;
            Vector3 waistPos = pos + Vector3.up * waistOffset;
            Vector3 eyesPos = pos + Vector3.up * eyesOffset;

            isFeetInWater = GetBlockTypeAt(feetPos) == BlockType.Water;
            isWaistInWater = GetBlockTypeAt(waistPos) == BlockType.Water;
            isEyesInWater = GetBlockTypeAt(eyesPos) == BlockType.Water;

            isFeetInLava = GetBlockTypeAt(feetPos) == BlockType.Lava;
            isWaistInLava = GetBlockTypeAt(waistPos) == BlockType.Lava;
            isEyesInLava = GetBlockTypeAt(eyesPos) == BlockType.Lava;
        }

        private BlockType GetBlockTypeAt(Vector3 worldPos)
        {
            int x = Mathf.FloorToInt(worldPos.x);
            int y = Mathf.FloorToInt(worldPos.y);
            int z = Mathf.FloorToInt(worldPos.z);

            return WorldManager.Instance.GetBlockAtWorldPosition(x, y, z);
        }

        private void HandleMovement(bool isPaused)
        {
            if (!controller.enabled) return;

            isGrounded = controller.isGrounded;

            float inputX = isPaused ? 0f : (GameSettings.Held(GameSettings.Bind.Right) ? 1f : 0f) - (GameSettings.Held(GameSettings.Bind.Left) ? 1f : 0f);
            float inputZ = isPaused ? 0f : (GameSettings.Held(GameSettings.Bind.Forward) ? 1f : 0f) - (GameSettings.Held(GameSettings.Bind.Back) ? 1f : 0f);

            Vector3 moveInput = (transform.right * inputX + transform.forward * inputZ).normalized;

            bool inLava = isFeetInLava || isWaistInLava || isEyesInLava;
            bool inWater = isFeetInWater || isWaistInWater || isEyesInWater;

            Vector3 finalMoveDirection = Vector3.zero;

            if (inLava)
            {
                finalMoveDirection = CalculateFluidPhysics(moveInput, lavaSpeedMultiplier, lavaUpAcceleration, lavaGravity, maxLavaSwimUpSpeed, maxLavaSwimDownSpeed, isPaused);
            }
            else if (inWater)
            {
                finalMoveDirection = CalculateFluidPhysics(moveInput, waterSpeedMultiplier, waterUpAcceleration, waterGravity, maxSwimUpSpeed, maxSwimDownSpeed, isPaused);
            }
            else
            {
                if (isGrounded && velocity.y < 0)
                {
                    velocity.y = -2f;
                }

                float targetSpeed = (!isPaused && Input.GetKey(KeyCode.LeftShift)) ? runSpeed : walkSpeed;
                if (isFeetInWater) targetSpeed *= waterSpeedMultiplier;
                if (isFeetInLava) targetSpeed *= lavaSpeedMultiplier;

                Vector3 horizontalVelocity = moveInput * targetSpeed;

                if (!isPaused && GameSettings.Pressed(GameSettings.Bind.Jump) && isGrounded && !isFeetInWater && !isFeetInLava)
                {
                    velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
                }

                velocity.y += gravity * Time.deltaTime;
                finalMoveDirection = horizontalVelocity + Vector3.up * velocity.y;
            }

            controller.Move(finalMoveDirection * Time.deltaTime);
        }

        private Vector3 CalculateFluidPhysics(
            Vector3 moveInput,
            float speedMultiplier,
            float upAcceleration,
            float fluidGravity,
            float maxUpSpeed,
            float maxDownSpeed,
            bool isPaused)
        {
            Vector3 targetHorizontalVelocity = moveInput * (walkSpeed * speedMultiplier);

            if (!isPaused && GameSettings.Pressed(GameSettings.Bind.Jump) && isGrounded)
            {
                velocity.y = maxUpSpeed;
            }

            if (!isPaused && GameSettings.Held(GameSettings.Bind.Jump))
            {
                velocity.y += upAcceleration * Time.deltaTime;
            }

            velocity.y += fluidGravity * Time.deltaTime;
            velocity.y = Mathf.Clamp(velocity.y, maxDownSpeed, maxUpSpeed);

            return targetHorizontalVelocity + Vector3.up * velocity.y;
        }

        public void ResetVelocity()
        {
            velocity = Vector3.zero;
            cameraPitch = 0f;
            if (playerCamera != null)
            {
                playerCamera.transform.localRotation = Quaternion.identity;
            }
        }

        public void SetRotation(Vector2 rot)
        {
            transform.rotation = Quaternion.Euler(0f, rot.y, 0f);
            cameraPitch = Mathf.Clamp(rot.x, minPitch, maxPitch);
            if (playerCamera != null)
            {
                playerCamera.transform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
            }
        }
    }
}