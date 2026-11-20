using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;

namespace MinecraftClone.Fluid
{
    public class FluidSystem : MonoBehaviour
    {
        public static FluidSystem Instance { get; private set; }

        [Header("Настройки жидкости")]
        [SerializeField] private float tickInterval = 0.08f;
        [SerializeField] private float lavaExtraDelay = 0.4f; // лава медленнее воды
        [SerializeField] private int maxWaterDistance = 7;
        [SerializeField] private int maxLavaDistance = 4;
        [SerializeField] private int maxNodesPerTick = 250;

        [Header("Защита производительности")]
        [SerializeField] private int maxQueueSize = 2500;
        [SerializeField] private float minChunkRebuildInterval = 0.15f;

        private struct FluidTask
        {
            public Vector3Int Pos;
            public BlockType FluidType;
            public float ReadyTime;

            public FluidTask(Vector3Int pos, BlockType fluidType, float lavaDelay)
            {
                Pos = pos;
                FluidType = fluidType;
                ReadyTime = fluidType == BlockType.Lava ? Time.time + lavaDelay : 0f;
            }
        }

        private readonly Queue<FluidTask> updateQueue = new Queue<FluidTask>();
        private readonly HashSet<Vector3Int> inQueue = new HashSet<Vector3Int>();
        private readonly Dictionary<Vector3Int, int> fluidLevels = new Dictionary<Vector3Int, int>();
        private readonly Dictionary<Vector2Int, float> chunkRebuildTimers = new Dictionary<Vector2Int, float>();

        private readonly HashSet<Vector2Int> pendingRebuild = new HashSet<Vector2Int>();
        private readonly List<Vector2Int> rebuiltNow = new List<Vector2Int>();

        private const int Falling = -1;

        private static readonly Vector3Int[] NeighborOffsets = {
            Vector3Int.up,
            Vector3Int.down,
            Vector3Int.left,
            Vector3Int.right,
            Vector3Int.forward,
            Vector3Int.back
        };

        private static readonly Vector3Int[] HorizontalOffsets = {
            Vector3Int.left, Vector3Int.right, Vector3Int.forward, Vector3Int.back
        };

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            StartCoroutine(FluidSimulationRoutine());
        }

        public void ScheduleFluidUpdate(Vector3Int pos, BlockType fluidType, int level = 0)
        {
            if (updateQueue.Count >= maxQueueSize) return;

            if (!fluidLevels.ContainsKey(pos))
            {
                fluidLevels[pos] = level;
            }

            if (inQueue.Add(pos))
            {
                updateQueue.Enqueue(new FluidTask(pos, fluidType, lavaExtraDelay));
            }
        }

        public void NotifyBlockChanged(Vector3Int pos)
        {
            CheckAndSchedule(pos);

            foreach (var offset in NeighborOffsets)
            {
                CheckAndSchedule(pos + offset);
            }
        }

        private void CheckAndSchedule(Vector3Int pos)
        {
            if (WorldManager.Instance == null) return;

            BlockType type = WorldManager.Instance.GetBlockAtWorldPosition(pos.x, pos.y, pos.z);
            if (type == BlockType.Water || type == BlockType.Lava)
            {
                if (inQueue.Add(pos))
                {
                    updateQueue.Enqueue(new FluidTask(pos, type, lavaExtraDelay));
                }
            }
            else
            {
                fluidLevels.Remove(pos);
            }
        }

        private IEnumerator FluidSimulationRoutine()
        {
            WaitForSeconds wait = new WaitForSeconds(tickInterval);
            HashSet<Vector2Int> dirtyChunks = new HashSet<Vector2Int>();

            while (true)
            {
                yield return wait;

                if (updateQueue.Count > 0)
                {
                    dirtyChunks.Clear();
                    int batch = Mathf.Min(updateQueue.Count, maxNodesPerTick);
                    for (int i = 0; i < batch; i++)
                    {
                        FluidTask task = updateQueue.Dequeue();
                        if (task.ReadyTime > Time.time) { updateQueue.Enqueue(task); continue; }
                        inQueue.Remove(task.Pos);
                        ProcessFluidNode(task, dirtyChunks);
                    }
                    foreach (var c in dirtyChunks) pendingRebuild.Add(c);
                }

                if (pendingRebuild.Count == 0) continue;

                float now = Time.time;
                rebuiltNow.Clear();
                foreach (var c in pendingRebuild)
                {
                    if (!chunkRebuildTimers.TryGetValue(c, out float last) || now - last >= minChunkRebuildInterval)
                    {
                        WorldManager.Instance.RebuildChunkMesh(c);
                        chunkRebuildTimers[c] = now;
                        rebuiltNow.Add(c);
                    }
                }
                foreach (var c in rebuiltNow) pendingRebuild.Remove(c);
            }
        }

        private void ProcessFluidNode(FluidTask task, HashSet<Vector2Int> dirtyChunks)
        {
            var wm = WorldManager.Instance;
            if (wm == null) return;

            BlockType currentType = wm.GetBlockAtWorldPosition(task.Pos.x, task.Pos.y, task.Pos.z);
            if (currentType != task.FluidType)
            {
                if (fluidLevels.ContainsKey(task.Pos))
                {
                    fluidLevels.Remove(task.Pos);
                    PropagateDrying(task.Pos, dirtyChunks);
                }
                return;
            }

            int level = GetFluidLevel(task.Pos);      // 0 = источник, -1 = падающая, 1..N = течение
            bool isSource = level == 0;
            int maxDist = (task.FluidType == BlockType.Lava) ? maxLavaDistance : maxWaterDistance;

            // Высыхание: течение без питания исчезает
            if (!isSource)
            {
                if (!HasValidFluidSource(task.Pos, task.FluidType, level))
                {
                    wm.ApplyBlockChange(task.Pos.x, task.Pos.y, task.Pos.z, BlockType.Air, false, dirtyChunks);
                    fluidLevels.Remove(task.Pos);
                    PropagateDrying(task.Pos, dirtyChunks);
                    return;
                }

                // Если сверху жидкость — клетка становится падающей
                Vector3Int up = task.Pos + Vector3Int.up;
                if (level != Falling && wm.GetBlockAtWorldPosition(up.x, up.y, up.z) == task.FluidType)
                {
                    fluidLevels[task.Pos] = Falling;
                    level = Falling;
                }
            }

            BlockType oppositeType = (task.FluidType == BlockType.Water) ? BlockType.Lava : BlockType.Water;
            foreach (var offset in NeighborOffsets)
            {
                Vector3Int nPos = task.Pos + offset;
                BlockType nType = wm.GetBlockAtWorldPosition(nPos.x, nPos.y, nPos.z);

                if (nType == oppositeType)
                {
                    BlockType resultStone = (task.FluidType == BlockType.Lava && offset == Vector3Int.down)
                        ? BlockType.Stone
                        : BlockType.Cobblestone;

                    Vector3Int lavaPos = (task.FluidType == BlockType.Lava) ? task.Pos : nPos;
                    fluidLevels.Remove(lavaPos);
                    wm.ApplyBlockChange(lavaPos.x, lavaPos.y, lavaPos.z, resultStone, false, dirtyChunks);

                    if (lavaPos == task.Pos) return;
                }
            }

            // 1. ВНИЗ: новая клетка — падающая (при приземлении растечётся с полной силой)
            Vector3Int downPos = task.Pos + Vector3Int.down;
            if (CanFlowInto(downPos, task.FluidType))
            {
                FlowTo(downPos, task.FluidType, Falling, dirtyChunks);
                return;
            }

            // 2. В СТОРОНЫ: только источник или клетка на твёрдой опоре (не на жидкости)
            BlockType below = wm.GetBlockAtWorldPosition(downPos.x, downPos.y, downPos.z);
            bool blockedBelow = below != BlockType.Water && below != BlockType.Lava;
            if (!isSource && !blockedBelow) return;

            int spreadLevel = (level == Falling ? 0 : level) + 1;
            if (spreadLevel > maxDist) return;

            foreach (var offset in HorizontalOffsets)
            {
                Vector3Int nPos = task.Pos + offset;

                if (CanFlowInto(nPos, task.FluidType))
                {
                    FlowTo(nPos, task.FluidType, spreadLevel, dirtyChunks);
                }
                else if (wm.GetBlockAtWorldPosition(nPos.x, nPos.y, nPos.z) == task.FluidType)
                {
                    // более сильное течение перезаписывает более слабое
                    int nl = GetFluidLevel(nPos);
                    if (nl > spreadLevel)
                    {
                        fluidLevels[nPos] = spreadLevel;
                        if (inQueue.Add(nPos))
                            updateQueue.Enqueue(new FluidTask(nPos, task.FluidType, lavaExtraDelay));
                    }
                }
            }
        }

        private bool HasValidFluidSource(Vector3Int pos, BlockType type, int level)
        {
            var wm = WorldManager.Instance;

            Vector3Int up = pos + Vector3Int.up;
            if (wm.GetBlockAtWorldPosition(up.x, up.y, up.z) == type) return true;
            if (level == Falling) return false;

            foreach (var offset in HorizontalOffsets)
            {
                Vector3Int n = pos + offset;
                if (wm.GetBlockAtWorldPosition(n.x, n.y, n.z) != type) continue;

                int nl = GetFluidLevel(n);
                if (nl == Falling) nl = 0;
                if (nl < level) return true;
            }
            return false;
        }

        private void PropagateDrying(Vector3Int pos, HashSet<Vector2Int> dirtyChunks)
        {
            foreach (var offset in NeighborOffsets)
            {
                Vector3Int nPos = pos + offset;
                BlockType type = WorldManager.Instance.GetBlockAtWorldPosition(nPos.x, nPos.y, nPos.z);
                if (type == BlockType.Water || type == BlockType.Lava)
                {
                    if (inQueue.Add(nPos))
                    {
                        updateQueue.Enqueue(new FluidTask(nPos, type, lavaExtraDelay));
                    }
                }
            }
        }

        private int GetFluidLevel(Vector3Int pos)
        {
            return fluidLevels.TryGetValue(pos, out int level) ? level : 0;
        }

        private bool CanFlowInto(Vector3Int target, BlockType fluidType)
        {
            if (WorldManager.Instance == null || WorldManager.Instance.Config == null) return false;

            if (target.y < 0 || target.y >= WorldManager.Instance.Config.ChunkHeight)
                return false;

            if (!WorldManager.Instance.IsChunkLoadedAtWorldPos(target.x, target.z))
                return false;

            if (fluidType == BlockType.Water && WorldManager.Instance.IsNearSponge(target)) return false;

            BlockType targetType = WorldManager.Instance.GetBlockAtWorldPosition(target.x, target.y, target.z);

            return targetType == BlockType.Air || targetType.IsPlant();
        }

        private void FlowTo(Vector3Int targetPos, BlockType fluidType, int level, HashSet<Vector2Int> dirtyChunks)
        {
            fluidLevels[targetPos] = level;
            WorldManager.Instance.ApplyBlockChange(targetPos.x, targetPos.y, targetPos.z, fluidType, false, dirtyChunks);

            if (inQueue.Add(targetPos))
            {
                updateQueue.Enqueue(new FluidTask(targetPos, fluidType, lavaExtraDelay));
            }
        }

        public void WakeWaterAround(Vector3Int c, int rx, int ry, int rz)
        {
            if (WorldManager.Instance == null) return;
            for (int dx = -rx; dx <= rx; dx++)
                for (int dy = -ry; dy <= ry; dy++)
                    for (int dz = -rz; dz <= rz; dz++)
                    {
                        Vector3Int p = new Vector3Int(c.x + dx, c.y + dy, c.z + dz);
                        if (WorldManager.Instance.GetBlockAtWorldPosition(p.x, p.y, p.z) == BlockType.Water
                            && inQueue.Add(p))
                        {
                            updateQueue.Enqueue(new FluidTask(p, BlockType.Water, lavaExtraDelay));
                        }
                    }
        }
    }
}