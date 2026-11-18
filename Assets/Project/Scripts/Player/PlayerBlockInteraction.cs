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
using MinecraftClone.Items;

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

        // Старый набор креатив-предметов через их ID
        private static readonly int[] CreativeHotbar =
        {
            ItemRegistry.IdForBlock(BlockType.Stone),
            ItemRegistry.IdForBlock(BlockType.Dirt),
            ItemRegistry.IdForBlock(BlockType.Sponge),
            ItemRegistry.IdForBlock(BlockType.OakPlanks),
            ItemRegistry.IdForBlock(BlockType.OakSapling),
            ItemRegistry.IdForBlock(BlockType.OakLog),
            ItemRegistry.IdForBlock(BlockType.OakLeaves),
            ItemRegistry.IdForBlock(BlockType.Glass),
            ItemRegistry.IdForBlock(BlockType.Gravel)
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
        private bool _lmbFresh;   // true, пока не использован текущий клик ЛКМ

        private float _actionTimer = 0f; // Таймер для зажатия клавиш

        private BlockBreakOverlay _breakOverlay;
        private Vector3Int _breakPos;
        private BlockType _breakType;
        private bool _isBreaking;
        private float _breakProgress;
        private float _breakDelay;   // пауза после сломанного блока
        private float _mobLock;      // пока бьём моба, блок за ним не трогаем

        private int _sentStage = -1;
        private Vector3Int _sentPos;
        private float _sentTime;

        public BlockType CurrentSelectedBlock => PlayerInventory.HotbarDef(_selectedBlockIndex)?.Block ?? BlockType.Air;
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
            _health = GetComponent<PlayerHealth>();

            if (outlineRenderer == null)
            {
                GameObject outlineGO = new GameObject("BlockOutline");
                outlineRenderer = outlineGO.AddComponent<BlockOutlineRenderer>();
            }
            _breakOverlay = new GameObject("BlockBreakOverlay").AddComponent<BlockBreakOverlay>();
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
                ResetBreak();
                return;
            }

            // Уменьшаем таймер задержки каждый кадр
            if (_actionTimer > 0f)
            {
                _actionTimer -= Time.deltaTime;
            }

            if (_breakDelay > 0f) _breakDelay -= Time.deltaTime;
            if (_mobLock > 0f) _mobLock -= Time.deltaTime;

            HandleScrollWheel();
            if (GameSettings.Pressed(GameSettings.Bind.Drop)) DropHeldItem();
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
                        Vector3Int n = -last;
                        if (BlockHitbox.TryGetBounds(b, out Vector3 bmin, out Vector3 bmax))
                        {
                            Vector3 cell = new Vector3(x, y, z);
                            if (RayAabb(o, d, cell + bmin, cell + bmax, out float te, out Vector3Int an) && te <= maxDist)
                            {
                                if (an != Vector3Int.zero) n = an;
                            }
                            else ok = false;   // луч прошёл мимо коробки: идём дальше по клеткам
                        }
                        if (ok) { hit = new Vector3Int(x, y, z); normal = n; return true; }
                    }
                }

                if (tmx < tmy && tmx < tmz) { x += sx; t = tmx; tmx += tdx; last = new Vector3Int(sx, 0, 0); }
                else if (tmy < tmz) { y += sy; t = tmy; tmy += tdy; last = new Vector3Int(0, sy, 0); }
                else { z += sz; t = tmz; tmz += tdz; last = new Vector3Int(0, 0, sz); }
            }
            return false;
        }

        private static bool RayAabb(Vector3 o, Vector3 d, Vector3 bmin, Vector3 bmax, out float tEnter, out Vector3Int normal)
        {
            tEnter = 0f; normal = Vector3Int.zero;
            float t0 = 0f, t1 = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                float oi = o[i], di = d[i];
                if (Mathf.Abs(di) < 1e-8f)
                {
                    if (oi < bmin[i] || oi > bmax[i]) return false;
                    continue;
                }
                float inv = 1f / di;
                float ta = (bmin[i] - oi) * inv, tb = (bmax[i] - oi) * inv;
                if (ta > tb) { float s = ta; ta = tb; tb = s; }
                if (ta > t0) { t0 = ta; normal = Vector3Int.zero; normal[i] = di > 0 ? -1 : 1; }
                if (tb < t1) t1 = tb;
                if (t0 > t1) return false;
            }
            tEnter = t0;
            return true;
        }

        private void UpdateTargetBlock()
        {
            if (cameraTransform == null || _worldManager == null) return;

            Vector3Int camCell = Vector3Int.FloorToInt(cameraTransform.position);
            BlockType camBlock = _worldManager.GetBlockAtWorldPosition(camCell.x, camCell.y, camCell.z);
            if (camBlock != BlockType.Air && camBlock != BlockType.Water && camBlock != BlockType.Lava
                && !camBlock.IsPlant() && !camBlock.IsSign() && !camBlock.IsTorch())
            {
                _selectedBlockPos = camCell;      // камера внутри блока: целимся именно в него
                _hasTargetBlock = true;
                if (outlineRenderer != null) outlineRenderer.Hide();
                return;
            }

            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
            if (VoxelRaycast(ray, reachDistance, false, out Vector3Int hit, out _))
            {
                _selectedBlockPos = hit;
                _hasTargetBlock = true;
                if (outlineRenderer != null)
                {
                    BlockType tb = WorldManager.Instance.GetBlockAtWorldPosition(hit.x, hit.y, hit.z);
                    if (BlockHitbox.TryGetBounds(tb, out Vector3 bmin, out Vector3 bmax))
                        outlineRenderer.Show(hit, bmin, bmax);
                    else
                        outlineRenderer.Show(hit);
                }
                return;
            }

            _hasTargetBlock = false;
            if (outlineRenderer != null) outlineRenderer.Hide();
        }

        private void HandleInput()
        {
            if (Input.GetMouseButtonDown(0)) _lmbFresh = true;
            if (!Input.GetMouseButton(0)) _lmbFresh = false;
            // Мгновенно сбрасываем таймер при отпускании кнопок, чтобы одиночные клики не задерживались
            if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1))
            {
                _actionTimer = 0f;
            }

            // ЛКМ — удар по мобу / поэтапное ломание блока
            if (Input.GetMouseButton(0))
            {
                if (_actionTimer <= 0f)
                {
                    if (_netPlayer != null) _netPlayer.NotifySwing();
                    _actionTimer = actionInterval;
                    if (TryAttackMob()) { ResetBreak(); _mobLock = actionInterval; }
                }
                if (_mobLock <= 0f) ContinueBreaking();
            }
            else ResetBreak();

            // ПКМ — еда / огниво / установка блока (работает при зажатии)
            if (Input.GetMouseButton(1))
            {
                if (_actionTimer <= 0f)
                {
                    var def = PlayerInventory.HotbarDef(_selectedBlockIndex);
                    if (def != null && def.FoodHeal > 0)
                    {
                        if (_netPlayer != null) _netPlayer.NotifySwing();
                        PlayerHealth.Local.RequestEat(def.Id);
                        if (!CreativeInventory) PlayerInventory.ConsumeFromHotbar(_selectedBlockIndex, 1);
                        _actionTimer = 0.8f;               // пауза между порциями
                    }
                    else if (def != null && def.Tool == ToolKind.FlintAndSteel)
                    {
                        if (_netPlayer != null) _netPlayer.NotifySwing();
                        TryIgnite();
                        _actionTimer = actionInterval;
                    }
                    else
                    {
                        if (_netPlayer != null) _netPlayer.NotifySwing();
                        TryPlaceBlock();
                        _actionTimer = actionInterval;
                    }
                }
            }
        }

        private void ContinueBreaking()
        {
            if (!_hasTargetBlock) { ResetBreak(); return; }
            if (_breakDelay > 0f) return;

            Vector3Int pos = _selectedBlockPos;
            BlockType type = _worldManager.GetBlockAtWorldPosition(pos.x, pos.y, pos.z);
            if (type == BlockType.Air) { ResetBreak(); return; }
            if (type == BlockType.Tnt) { BreakBlock(pos); return; }   // поджиг по клику, как раньше

            if (_worldManager.BlockDatabase != null
                && _worldManager.BlockDatabase.TryGetValue(type, out BlockDataSO d) && d != null && !d.IsBreakable)
            { ResetBreak(); return; }

            if (!_isBreaking || pos != _breakPos || type != _breakType)
            { _isBreaking = true; _breakPos = pos; _breakType = type; _breakProgress = 0f; }

            float time = CreativeInventory ? 0f : ToolRules.BreakTime(type, PlayerInventory.HotbarDef(_selectedBlockIndex));
            _breakProgress += time <= 0f ? 1f : Time.deltaTime / time;

            if (_breakProgress >= 1f)
            {
                BreakBlock(pos);
                ResetBreak();
                _breakDelay = actionInterval;
                return;
            }

            if (_breakOverlay != null)
            {
                if (!BlockHitbox.TryGetBounds(type, out Vector3 bmin, out Vector3 bmax)) { bmin = Vector3.zero; bmax = Vector3.one; }
                _breakOverlay.Show(pos, bmin, bmax, _breakProgress);
            }

            int stage = Mathf.Clamp((int)(_breakProgress * 10f), 0, 9);
            if (stage != _sentStage || pos != _sentPos || Time.time - _sentTime > 1f)   // при смене стадии и раз в секунду
            {
                _sentStage = stage; _sentPos = pos; _sentTime = Time.time;
                BreakSync.Send(pos, stage);
            }
        }

        private void ResetBreak()
        {
            _isBreaking = false;
            _breakProgress = 0f;
            if (_breakOverlay != null) _breakOverlay.Hide();
            if (_sentStage >= 0) { BreakSync.Send(_sentPos, -1); _sentStage = -1; }   // сообщаем, что перестали ломать
        }

        private void TryIgnite()
        {
            if (cameraTransform == null || _worldManager == null) return;
            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
            if (!VoxelRaycast(ray, reachDistance, true, out Vector3Int hit, out Vector3Int normal)) return;

            if (_worldManager.GetBlockAtWorldPosition(hit.x, hit.y, hit.z) == BlockType.Tnt)
            { MinecraftClone.Physics.TntSystem.RequestIgnite(hit); return; }

            Vector3Int p = hit + normal;
            if (_worldManager.GetBlockAtWorldPosition(p.x, p.y, p.z) == BlockType.Air)
                _worldManager.SetBlockAtWorldPosition(p.x, p.y, p.z, BlockType.Fire);   // проверку опоры делает сам SetBlock
        }

        private void TryPlaceBlock()
        {
            var def = PlayerInventory.HotbarDef(_selectedBlockIndex);
            if (def == null || def.Block == BlockType.Air) return;
            if (cameraTransform == null || _worldManager == null) return;

            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
            if (!VoxelRaycast(ray, reachDistance, true, out Vector3Int hit, out Vector3Int normal)) return;

            if (def.Block == BlockType.StoneSlab && normal.y > 0
                && _worldManager.GetBlockAtWorldPosition(hit.x, hit.y, hit.z) == BlockType.StoneSlab)
            {
                if (NetworkPlayer.IsBlockOverlappingAnyPlayer(hit)) return;
                if (_worldManager.SetBlockAtWorldPosition(hit.x, hit.y, hit.z, BlockType.StoneSlabDouble)) ConsumeSelected();
                return;
            }

            Vector3Int placePos = hit + normal;
            BlockType target = _worldManager.GetBlockAtWorldPosition(placePos.x, placePos.y, placePos.z);
            if (target == BlockType.Air || target == BlockType.Water || target == BlockType.Lava)
                PlaceBlock(placePos, normal, def.Block);
        }

        private void BreakBlock(Vector3Int blockPos)
        {
            BlockType brokenBlockType = _worldManager.GetBlockAtWorldPosition(blockPos.x, blockPos.y, blockPos.z);
            if (brokenBlockType == BlockType.Air) return;

            if (brokenBlockType == BlockType.Tnt)
            {
                if (_lmbFresh)
                {
                    _lmbFresh = false;
                    MinecraftClone.Physics.TntSystem.RequestIgnite(blockPos);
                }
                return;   // ТНТ не ломается как обычный блок
            }

            if (_worldManager.BlockDatabase != null && _worldManager.BlockDatabase.TryGetValue(brokenBlockType, out BlockDataSO blockData))
            {
                if (!blockData.IsBreakable) return;
            }

            _worldManager.SetBlockAtWorldPosition(blockPos.x, blockPos.y, blockPos.z, BlockType.Air);
        }

        private void PlaceBlock(Vector3Int blockPos, Vector3Int normal, BlockType blockType)
        {
            BlockType selectedType = blockType;
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

            if (selectedType.IsTorch())
            {
                if (normal.y < 0) return;   // на потолок нельзя
                if (_worldManager.GetBlockAtWorldPosition(blockPos.x, blockPos.y, blockPos.z) != BlockType.Air) return;

                selectedType = normal.y > 0 ? BlockType.Torch : BlockTypeUtil.WallTorchFromNormal(normal);
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

                var tnt = c.GetComponentInParent<MinecraftClone.Physics.PrimedTntEntity>();
                if (tnt != null)
                {
                    if (_lmbFresh)
                    {
                        _lmbFresh = false;
                        MinecraftClone.Physics.TntSystem.RequestDefuse(tnt.Id);
                    }
                    return true;
                }

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

        private void DropHeldItem()
        {
            if (cameraTransform == null || ItemDropSystem.Instance == null) return;

            int itemId = PlayerInventory.HotbarItem(_selectedBlockIndex);
            if (itemId <= 0) return;

            // в креативе предметы бесконечные, в выживании забираем один из слота
            if (!CreativeInventory && !PlayerInventory.ConsumeFromHotbar(_selectedBlockIndex, 1)) return;

            Vector3 fwd = cameraTransform.forward;
            Vector3 pos = cameraTransform.position + fwd * 0.3f + Vector3.down * 0.2f;
            Vector3 vel = fwd * 4.5f + Vector3.up * 2f;

            if (_netPlayer != null) _netPlayer.NotifySwing();
            ItemDropSystem.Instance.RequestDrop(itemId, 1, pos, vel);
        }

        private void OnDestroy() { if (_breakOverlay != null) Destroy(_breakOverlay.gameObject); }
    }
}