using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.ScriptableObjects;
using MinecraftClone.Effects;
using MinecraftClone.Physics;
using MinecraftClone.UI;
using MinecraftClone.Networking;

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

        public static readonly BlockType[] HotbarBlocks =
        {
            BlockType.Stone, BlockType.Dirt, BlockType.Sponge,
            BlockType.OakPlanks, BlockType.OakSapling, BlockType.OakLog,
            BlockType.OakLeaves, BlockType.Glass, BlockType.Gravel
        };

        private int _selectedBlockIndex = 0;
        private WorldManager _worldManager;
        private Vector3Int _selectedBlockPos;
        private bool _hasTargetBlock;
        private Collider _playerCollider;
        private PauseMenuUI _pauseMenu;
        private NetworkPlayer _netPlayer;

        private float _actionTimer = 0f; // Таймер для зажатия клавиш

        public BlockType CurrentSelectedBlock => HotbarBlocks[_selectedBlockIndex];
        public int SelectedSlot => _selectedBlockIndex;

        private void Start()
        {
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

            // Если игра на паузе, открыт чат или меню — полностью отключаем выделение блоков и взаимодействие
            if (Time.timeScale == 0f || ChatUI.IsOpen || (_pauseMenu != null && _pauseMenu.IsPaused))
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
            if (scroll > 0f) _selectedBlockIndex = (_selectedBlockIndex + HotbarBlocks.Length - 1) % HotbarBlocks.Length;
            else if (scroll < 0f) _selectedBlockIndex = (_selectedBlockIndex + 1) % HotbarBlocks.Length;

            for (int i = 0; i < HotbarBlocks.Length; i++)
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

                    if (_hasTargetBlock)
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
            if (cameraTransform == null || _worldManager == null) return;

            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
            if (!VoxelRaycast(ray, reachDistance, true, out Vector3Int hit, out Vector3Int normal)) return;

            Vector3Int placePos = hit + normal;
            BlockType target = _worldManager.GetBlockAtWorldPosition(placePos.x, placePos.y, placePos.z);
            if (target == BlockType.Air || target == BlockType.Water || target == BlockType.Lava)
                PlaceBlock(placePos);
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

        private void PlaceBlock(Vector3Int blockPos)
        {
            BlockType selectedType = CurrentSelectedBlock;

            if (_playerCollider != null && _worldManager.BlockDatabase.TryGetValue(selectedType, out BlockDataSO data))
            {
                if (data.IsSolid)
                {
                    Bounds blockBounds = new Bounds(blockPos + new Vector3(0.5f, 0.5f, 0.5f), Vector3.one * 0.9f);
                    if (_playerCollider.bounds.Intersects(blockBounds)) return;
                    if (NetworkPlayer.IsBlockOverlappingAnyPlayer(blockPos)) return;
                }
            }

            _worldManager.SetBlockAtWorldPosition(blockPos.x, blockPos.y, blockPos.z, selectedType);
        }
    }
}