using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.UI;
using MinecraftClone.ScriptableObjects;

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

        private Vector3 _knock;
        private const float KnockDecay = 20f;

        [Header("Stuck In Blocks")]
        public float stuckSpeedMultiplier = 0.5f;
        private const float EmbedFeetSample = 0.3f;   // точка проверки «ног»; запас, чтобы стоя на земле не было ложных срабатываний
        private bool _headInBlock;
        private BlockType _headBlockType;

        private const float McGravity = 32f;        // 0.08 бл/тик² * 20²
        private const float McDrag = 0.98f;         // сопротивление за тик (20 тиков/с)
        private const float McJumpVelocity = 8.4f;  // 0.42 бл/тик * 20
        private const float McTerminal = -78.4f;    // предельная скорость падения
        private float _pendingVelY;
        public float VerticalVelocity => velocity.y;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            playerCamera = GetComponentInChildren<Camera>();
            if (controller != null)
            {
                controller.radius = 0.3f;                       // ширина 0.6
                controller.height = 1.8f;
                controller.center = new Vector3(0f, 0.9f, 0f);  // pivot игрока у ног
                controller.skinWidth = 0.03f;                   // маленький «зазор», чтобы не было ложного отступа от стен
            }
        }

        private void Start()
        {
            _pauseMenu = PauseMenuUI.Instance != null ? PauseMenuUI.Instance : FindFirstObjectByType<PauseMenuUI>();
            SetFrozen(true);
        }

        public void SetFrozen(bool freeze)
        {
            _headInBlock = false;   // эта строка только если вы добавляли правку про застревание
            isFrozen = freeze;
            _knock = Vector3.zero;

            if (freeze) velocity = Vector3.zero;
            else if (_pendingVelY != 0f) { velocity.y = _pendingVelY; _pendingVelY = 0f; }

            if (controller != null) controller.enabled = !freeze;
            UpdateCursorState(freeze);
        }

        public void RestoreVelocityY(float v)
        {
            v = Mathf.Clamp(v, McTerminal, 50f);
            if (isFrozen) _pendingVelY = v; else velocity.y = v;
        }

        public void AddKnockback(Vector3 horizontal, float hop)
        {
            _knock = horizontal;
            velocity.y = Mathf.Max(velocity.y, hop);
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
            if (HandleEmbedded(isPaused)) return;
            HandleMovement(isPaused);

        }

        private static readonly Vector3Int[] PushDirs =
{
    Vector3Int.up, Vector3Int.right, Vector3Int.left,
    new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1), Vector3Int.down
};

        private bool IsSolidBlockType(BlockType t)
        {
            if (t == BlockType.Air || t == BlockType.Water || t == BlockType.Lava || t.IsPlant() || t.IsSign()) return false;
            var db = WorldManager.Instance.BlockDatabase;
            if (db != null && db.TryGetValue(t, out BlockDataSO data) && data != null) return data.IsSolid;
            return true;
        }

        private bool IsSolidAt(Vector3 p) => IsSolidBlockType(GetBlockTypeAt(p));

        // есть ли место для игрока (2 блока высотой) с ногами в точке feet
        private bool HasClearance(Vector3 feet)
        {
            return !IsSolidAt(feet + Vector3.up * EmbedFeetSample)
                && !IsSolidAt(feet + Vector3.up * waistOffset)
                && !IsSolidAt(feet + Vector3.up * eyesOffset)
                && !IsSolidAt(feet + Vector3.up * 1.8f);
        }

        // true = мы сами обработали движение в этом кадре (вытолкнули или игрок застрял)
        private bool HandleEmbedded(bool isPaused)
        {
            _headInBlock = false;
            if (WorldManager.Instance == null) return false;

            Vector3 pos = transform.position;
            if (!WorldManager.Instance.IsChunkLoadedAtWorldPos(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.z)))
                return false;

            bool feetSolid = IsSolidAt(pos + Vector3.up * EmbedFeetSample) || IsSolidAt(pos + Vector3.up * waistOffset);
            bool headSolid = IsSolidAt(pos + Vector3.up * eyesOffset);
            if (!feetSolid && !headSolid) return false;

            if (TryPushOut(pos)) return true;

            // ---- выхода нет, игрок застрял ----
            if (headSolid)
            {
                // голова в блоке: полностью замираем, пока блок не сломают
                velocity = Vector3.zero;
                _knock = Vector3.zero;
                _headBlockType = GetBlockTypeAt(pos + Vector3.up * eyesOffset);
                _headInBlock = true;
                return true;
            }

            // только ноги: можно идти и прыгать, гравитация не затягивает глубже
            float inputX = isPaused ? 0f : (GameSettings.Held(GameSettings.Bind.Right) ? 1f : 0f) - (GameSettings.Held(GameSettings.Bind.Left) ? 1f : 0f);
            float inputZ = isPaused ? 0f : (GameSettings.Held(GameSettings.Bind.Forward) ? 1f : 0f) - (GameSettings.Held(GameSettings.Bind.Back) ? 1f : 0f);
            Vector3 move = (transform.right * inputX + transform.forward * inputZ).normalized * (walkSpeed * stuckSpeedMultiplier);

            if (!isPaused && GameSettings.Pressed(GameSettings.Bind.Jump)) velocity.y = McJumpVelocity;
            else if (velocity.y > 0f) velocity.y -= McGravity * Time.deltaTime;
            else velocity.y = 0f;

            MoveSubstepped((move + Vector3.up * velocity.y) * Time.deltaTime);
            return true;
        }

        private bool TryPushOut(Vector3 pos)
        {
            int fx = Mathf.FloorToInt(pos.x);
            int fz = Mathf.FloorToInt(pos.z);
            int fy = Mathf.FloorToInt(pos.y + EmbedFeetSample);

            bool found = false;
            Vector3 best = pos;
            float bestDist = float.MaxValue;

            foreach (Vector3Int d in PushDirs)
            {
                int cy = fy + d.y;
                Vector3 target = (d.x == 0 && d.z == 0)
                    ? new Vector3(pos.x, cy + 0.02f, pos.z)                       // вверх/вниз: X и Z не меняем
                    : new Vector3(fx + d.x + 0.5f, cy + 0.02f, fz + d.z + 0.5f);  // в сторону: центр соседней клетки

                if (!HasClearance(target)) continue;

                float dist = (target - pos).sqrMagnitude;
                if (dist < bestDist) { bestDist = dist; best = target; found = true; }
            }

            if (!found) return false;

            controller.enabled = false;
            transform.position = best;
            controller.enabled = true;
            UnityEngine.Physics.SyncTransforms();   // именно с UnityEngine., иначе C# найдёт пространство имён MinecraftClone.Physics
            velocity = Vector3.zero;
            _knock = Vector3.zero;
            return true;
        }

        private void OnGUI()
        {
            if (!_headInBlock || Event.current.type != EventType.Repaint) return;
            if (RuntimeLoadingScreen.IsShown || WorldManager.Instance == null) return;

            Texture2D tex = null;
            var db = WorldManager.Instance.BlockDatabase;
            if (db != null && db.TryGetValue(_headBlockType, out BlockDataSO data) && data != null)
            {
                tex = data.SideTexture;
                if (tex == null) tex = data.TopTexture;
            }
            if (tex == null) tex = Texture2D.blackTexture;

            GUI.depth = 100;                                   // позади интерфейса
            GUI.color = new Color(0.08f, 0.08f, 0.08f, 1f);    // «очень-очень тёмная» текстура
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), tex, ScaleMode.StretchToFill);
            GUI.color = Color.white;
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
                    velocity.y = McJumpVelocity;
                }

                if (!isPaused && isGrounded && !isFeetInWater && !isFeetInLava
                    && WorldManager.Instance != null
                    && WorldManager.Instance.GameMode == WorldGameMode.Classic024SurvivalTest
                    && moveInput.sqrMagnitude > 0f)
                {
                    Vector3 low = transform.position + Vector3.up * 0.4f;
                    Vector3 high = transform.position + Vector3.up * 1.1f;
                    if (Physics.Raycast(low, moveInput, 0.55f, ~0, QueryTriggerInteraction.Ignore)
                        && !Physics.Raycast(high, moveInput, 0.55f, ~0, QueryTriggerInteraction.Ignore))
                        velocity.y = McJumpVelocity;
                }

                velocity.y -= McGravity * Time.deltaTime;
                velocity.y *= Mathf.Pow(McDrag, Time.deltaTime * 20f);
                velocity.y = Mathf.Max(velocity.y, McTerminal);
                finalMoveDirection = horizontalVelocity + Vector3.up * velocity.y;
            }

            finalMoveDirection += _knock;
            _knock = Vector3.MoveTowards(_knock, Vector3.zero, KnockDecay * Time.deltaTime);
            MoveSubstepped(finalMoveDirection * Time.deltaTime);
        }

        private void MoveSubstepped(Vector3 delta)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(delta.y) / 0.5f), 1, 8);
            Vector3 part = delta / steps;
            for (int i = 0; i < steps; i++)
            {
                controller.Move(part);
                CollisionFlags f = controller.collisionFlags;
                if ((f & CollisionFlags.Above) != 0 && velocity.y > 0f) velocity.y = 0f;
                if ((f & (CollisionFlags.Above | CollisionFlags.Below)) != 0) break;
            }
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
            _knock = Vector3.zero;
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
