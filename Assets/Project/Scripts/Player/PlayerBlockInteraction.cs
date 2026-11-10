using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.ScriptableObjects;
using MinecraftClone.Effects;
using MinecraftClone.Physics;
using MinecraftClone.UI;
using MinecraftClone.Networking;
using MinecraftClone.Mobs;
using MinecraftClone.Rendering;

namespace MinecraftClone.Player
{
    public class PlayerBlockInteraction : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float reachDistance = 5f;
        [SerializeField] private float actionInterval = 0.25f; // Интервал между действиями (0.25 сек)
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private BlockOutlineRenderer outlineRenderer;

        [Header("Layer Masks")]
        [SerializeField] private LayerMask blockLayerMask; // Слой блоков (больше не используется)
        [SerializeField] private LayerMask waterLayerMask; // Слой воды/жидкостей (больше не используется)

        // Креатив. false = выживание: инвентарь и хотбар пустые. Для возврата креатива поставить true.
        public static bool CreativeInventory = false;

        // Старый набор креатива сохранён, он понадобится при возврате режима
        private static readonly BlockType[] CreativeHotbar =
        {
    BlockType.Stone, BlockType.Dirt, BlockType.Sponge,
    BlockType.OakPlanks, BlockType.OakSapling, BlockType.OakLog,
    BlockType.OakLeaves, BlockType.Glass, BlockType.Gravel
};

        // Хотбар статический и живёт между мирами, поэтому сбрасываем его при входе
        public static void ResetHotbar()
        {
            PlayerInventory.Reset(CreativeInventory ? CreativeHotbar : null);
        }

        private int _selectedBlockIndex = 0;
        private WorldManager _worldManager;
        private Vector3Int _selectedBlockPos;
        private bool _hasTargetBlock;
        private Collider _playerCollider;
        private PauseMenuUI _pauseMenu;
        private NetworkPlayer _netPlayer;
        private PlayerHealth _health;

        private float _actionTimer = 0f; // Таймер для зажатия клавиш

        public BlockType CurrentSelectedBlock => PlayerInventory.HotbarType(_selectedBlockIndex);
        public int SelectedSlot => _selectedBlockIndex;

        private void Start()
        {
            ResetHotbar();
            if (cameraTransform == null)
            {
                Camera cam = GetComponentInChildren<Camera>();
                if (cam != null) cameraTransform = cam.transform;
                else if (Camera.main != null) cameraTransform = Camera.main.transform;
            }

            _worldManager = FindFirstObjectByType<WorldManager>();
            _playerCollider = GetComponent<Collider>();
            _pauseMenu = PauseMenuUI.Instance != null ? PauseMenuUI.Instance : FindFirstObjectByType<PauseMenuUI>();
            _netPlayer = GetComponent<NetworkPlayer>();
            _health = GetComponent<PlayerHealth>();

            if (outlineRenderer == null)
            {
                GameObject outlineGO = new GameObject("BlockOutline");
                outlineRenderer = outlineGO.AddComponent<BlockOutlineRenderer>();
            }
        }

        private void Update()
        {
            if (_pauseMenu == null)
            {
                _pauseMenu = PauseMenuUI.Instance != null ? PauseMenuUI.Instance : FindFirstObjectByType<PauseMenuUI>();
            }

            // Если игра на паузе, открыт чат, инвентарь или меню — полностью отключаем выделение блоков и взаимодействие
            if (Time.timeScale == 0f || ChatUI.IsOpen || InventoryUI.IsOpen || (_pauseMenu != null && _pauseMenu.IsPaused || (_health != null && _health.IsDead)))
            {
                if (outlineRenderer != null) outlineRenderer.Hide();
                _hasTargetBlock = false;
                return;
            }

            // Уменьшаем таймер задержки каждый кадр
            if (_actionTimer > 0f)
            {
                _actionTimer -= Time.deltaTime;
            }

            HandleScrollWheel();
            UpdateTargetBlock();
            HandleInput();
        }

        private void HandleScrollWheel()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll > 0f) _selectedBlockIndex = (_selectedBlockIndex + PlayerInventory.HotbarSize - 1) % PlayerInventory.HotbarSize;
            else if (scroll < 0f) _selectedBlockIndex = (_selectedBlockIndex + 1) % PlayerInventory.HotbarSize;

            for (int i = 0; i < PlayerInventory.HotbarSize; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) _selectedBlockIndex = i;
        }

        private bool VoxelRaycast(Ray ray, float maxDist, bool solidOnly, out Vector3Int hit, out Vector3Int normal)
        {
            hit = default;
            normal = default;

            Vector3 o = ray.origin, d = ray.direction;
            int x = Mathf.FloorToInt(o.x), y = Mathf.FloorToInt(o.y), z = Mathf.FloorToInt(o.z);
            int sx = d.x > 0f ? 1 : -1, sy = d.y > 0f ? 1 : -1, sz = d.z > 0f ? 1 : -1;

            float tdx = d.x != 0f ? Mathf.Abs(1f / d.x) : float.PositiveInfinity;
            float tdy = d.y != 0f ? Mathf.Abs(1f / d.y) : float.PositiveInfinity;
            float tdz = d.z != 0f ? Mathf.Abs(1f / d.z) : float.PositiveInfinity;

            float tmx = d.x != 0f ? (d.x > 0f ? x + 1 - o.x : o.x - x) * tdx : float.PositiveInfinity;
            float tmy = d.y != 0f ? (d.y > 0f ? y + 1 - o.y : o.y - y) * tdy : float.PositiveInfinity;
            float tmz = d.z != 0f ? (d.z > 0f ? z + 1 - o.z : o.z - z) * tdz : float.PositiveInfinity;

            Vector3Int last = Vector3Int.zero;
            float t = 0f;

            while (t <= maxDist)
            {
                BlockType b = _worldManager.GetBlockAtWorldPosition(x, y, z);
                if (last != Vector3Int.zero && b != BlockType.Air && b != BlockType.Water && b != BlockType.Lava)
                {
                    bool ok = true;
                    if (solidOnly)
                    {
                        ok = _worldManager.BlockDatabase != null
                           && _worldManager.BlockDatabase.TryGetValue(b, out BlockDataSO data)
                           && data != null && data.IsSolid;
                    }
                    if (ok)
                    {
                        hit = new Vector3Int(x, y, z);
                        normal = -last;
                        return true;
                    }
                }

                if (tmx < tmy && tmx < tmz) { x += sx; t = tmx; tmx += tdx; last = new Vector3Int(sx, 0, 0); }
                else if (tmy < tmz) { y += sy; t = tmy; tmy += tdy; last = new Vector3Int(0, sy, 0); }
                else { z += sz; t = tmz; tmz += tdz; last = new Vector3Int(0, 0, sz); }
            }
            return false;
        }

        private void UpdateTargetBlock()
        {
            if (cameraTransform == null || _worldManager == null) return;

            Vector3Int camCell = Vector3Int.FloorToInt(cameraTransform.position);
            BlockType camBlock = _worldManager.GetBlockAtWorldPosition(camCell.x, camCell.y, camCell.z);
            if (camBlock != BlockType.Air && camBlock != BlockType.Water && camBlock != BlockType.Lava
                && !camBlock.IsPlant() && !camBlock.IsSign())
            {
                _selectedBlockPos = camCell;     // камера внутри блока: целимся именно в него
                _hasTargetBlock = true;
                if (outlineRenderer != null) outlineRenderer.Hide();
                return;
            }

            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
            if (VoxelRaycast(ray, reachDistance, false, out Vector3Int hit, out _))
            {
                _selectedBlockPos = hit;
                _hasTargetBlock = true;
                if (outlineRenderer != null) outlineRenderer.Show(hit);
                return;
            }

            _hasTargetBlock = false;
            if (outlineRenderer != null) outlineRenderer.Hide();
        }

        private void HandleInput()
        {
            // Мгновенно сбрасываем таймер при отпускании кнопок, чтобы одиночные клики не задерживались
            if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1))
            {
                _actionTimer = 0f;
            }

            // ЛКМ — Разрушение (работает при зажатии GetMouseButton)
            if (Input.GetMouseButton(0))
            {
                if (_actionTimer <= 0f)
                {
                    if (_netPlayer != null) _netPlayer.NotifySwing();

                    if (!TryAttackMob() && _hasTargetBlock)
                    {
                        BreakBlock(_selectedBlockPos);
                    }
                    _actionTimer = actionInterval; // Сброс таймера на 0.25 сек
                }
            }

            // ПКМ — Установка (работает при зажатии GetMouseButton)
            if (Input.GetMouseButton(1))
            {
                if (_actionTimer <= 0f)
                {
                    if (_netPlayer != null) _netPlayer.NotifySwing();

                    TryPlaceBlock();
                    _actionTimer = actionInterval; // Сброс таймера на 0.25 сек
                }
            }
        }

        private void TryPlaceBlock()
        {
            if (CurrentSelectedBlock == BlockType.Air) return;   // пустой слот: ничего не ставим
            if (cameraTransform == null || _worldManager == null) return;

            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
            if (!VoxelRaycast(ray, reachDistance, true, out Vector3Int hit, out Vector3Int normal)) return;

            Vector3Int placePos = hit + normal;
            BlockType target = _worldManager.GetBlockAtWorldPosition(placePos.x, placePos.y, placePos.z);
            if (target == BlockType.Air || target == BlockType.Water || target == BlockType.Lava)
                PlaceBlock(placePos, normal);
        }

        private void BreakBlock(Vector3Int blockPos)
        {
            BlockType brokenBlockType = _worldManager.GetBlockAtWorldPosition(blockPos.x, blockPos.y, blockPos.z);
            if (brokenBlockType == BlockType.Air) return;

            if (_worldManager.BlockDatabase != null && _worldManager.BlockDatabase.TryGetValue(brokenBlockType, out BlockDataSO blockData))
            {
                if (!blockData.IsBreakable) return;
            }

            _worldManager.SetBlockAtWorldPosition(blockPos.x, blockPos.y, blockPos.z, BlockType.Air);
        }

        private void PlaceBlock(Vector3Int blockPos, Vector3Int normal)
            {
                BlockType selectedType = CurrentSelectedBlock;
                // пока в этой клетке проходит падающий блок, ставить нельзя
                if (GravityBlockSystem.Instance != null && GravityBlockSystem.Instance.IsOccupiedByFalling(blockPos)) return;

            if (selectedType.IsSign())
                {
                    if (normal.y < 0) return;   // на потолок нельзя
                    if (_worldManager.GetBlockAtWorldPosition(blockPos.x, blockPos.y, blockPos.z) != BlockType.Air) return;

                    if (normal.y > 0)
                    {
                        // на верхнюю грань: стоит на палке и смотрит на игрока, шаг 45°
                        Vector3 toPlayer = cameraTransform.position - (blockPos + new Vector3(0.5f, 0.5f, 0.5f));
                        float yaw = Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg;
                        selectedType = BlockTypeUtil.StandingSignFromYaw(yaw);
                    }
                    else
                    {
                        selectedType = BlockTypeUtil.WallSignFromNormal(normal);   // на боковую грань
                    }

                if (_worldManager.SetBlockAtWorldPosition(blockPos.x, blockPos.y, blockPos.z, selectedType)) ConsumeSelected();
                return;
            }

            if (_worldManager.BlockDatabase.TryGetValue(selectedType, out BlockDataSO data) && data.IsSolid)
            {
                if (NetworkPlayer.IsBlockOverlappingAnyPlayer(blockPos)) return;   // точная коробка 0.6 x 1.8
            }

            if (_worldManager.SetBlockAtWorldPosition(blockPos.x, blockPos.y, blockPos.z, selectedType)) ConsumeSelected();
        }

        private void ConsumeSelected()
        {
            if (!CreativeInventory) PlayerInventory.ConsumeFromHotbar(_selectedBlockIndex, 1);
        }

        // true, если перед игроком моб (тогда блок за ним не ломаем)
        private bool TryAttackMob()
        {
            if (cameraTransform == null) return false;

            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
            RaycastHit[] hits = UnityEngine.Physics.RaycastAll(ray, reachDistance, ~0, QueryTriggerInteraction.Collide);
            if (hits.Length == 0) return false;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit h in hits)
            {
                Collider c = h.collider;
                if (c.transform.IsChildOf(transform)) continue;   // свои коллайдеры

                Mob mob = c.GetComponentInParent<Mob>();
                if (mob != null)
                {
                    if (_netPlayer != null) _netPlayer.AttackMob(mob);
                    else mob.ServerTakeDamage(NetworkPlayer.AttackDamage, transform.position, GetComponent<PlayerHealth>());
                    return true;
                }

                if (c.isTrigger && c.GetComponent<ChunkRenderer>() != null) continue;   // невидимый хитбокс цветов
                return false;   // сначала упёрлись в стену
            }
            return false;
        }
    }
}