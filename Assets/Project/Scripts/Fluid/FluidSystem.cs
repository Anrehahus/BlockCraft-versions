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

            public FluidTask(Vector3Int pos, BlockType fluidType)
            {
                Pos = pos;
                FluidType = fluidType;
            }
        }

        private readonly Queue<FluidTask> updateQueue = new Queue<FluidTask>();
        private readonly HashSet<Vector3Int> inQueue = new HashSet<Vector3Int>();
        private readonly Dictionary<Vector3Int, int> fluidLevels = new Dictionary<Vector3Int, int>();
        private readonly Dictionary<Vector2Int, float> chunkRebuildTimers = new Dictionary<Vector2Int, float>();

        private readonly HashSet<Vector2Int> pendingRebuild = new HashSet<Vector2Int>();
        private readonly List<Vector2Int> rebuiltNow = new List<Vector2Int>();

        private static readonly Vector3Int[] NeighborOffsets = {
            Vector3Int.up,
            Vector3Int.down,
            Vector3Int.left,
            Vector3Int.right,
            Vector3Int.forward,
            Vector3Int.back
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
                updateQueue.Enqueue(new FluidTask(pos, fluidType));
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
                    updateQueue.Enqueue(new FluidTask(pos, type));
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
            if (WorldManager.Instance == null) return;

            BlockType currentType = WorldManager.Instance.GetBlockAtWorldPosition(task.Pos.x, task.Pos.y, task.Pos.z);

            if (currentType != task.FluidType)
            {
                if (fluidLevels.ContainsKey(task.Pos))
                {
                    fluidLevels.Remove(task.Pos);
                    PropagateDrying(task.Pos, dirtyChunks);
                }
                return;
            }

            int currentLevel = GetFluidLevel(task.Pos);
            int maxDist = (task.FluidType == BlockType.Lava) ? maxLavaDistance : maxWaterDistance;

            if (currentLevel > 0 && !HasValidFluidSource(task.Pos, task.FluidType, currentLevel))
            {
                WorldManager.Instance.ApplyBlockChange(task.Pos.x, task.Pos.y, task.Pos.z, BlockType.Air, false, dirtyChunks);
                fluidLevels.Remove(task.Pos);
                PropagateDrying(task.Pos, dirtyChunks);
                return;
            }

            BlockType oppositeType = (task.FluidType == BlockType.Water) ? BlockType.Lava : BlockType.Water;
            foreach (var offset in NeighborOffsets)
            {
                Vector3Int nPos = task.Pos + offset;
                BlockType nType = WorldManager.Instance.GetBlockAtWorldPosition(nPos.x, nPos.y, nPos.z);

                if (nType == oppositeType)
                {
                    BlockType resultStone = (task.FluidType == BlockType.Lava && offset == Vector3Int.down)
                        ? BlockType.Stone
                        : BlockType.Cobblestone;

                    Vector3Int lavaPos = (task.FluidType == BlockType.Lava) ? task.Pos : nPos;
                    fluidLevels.Remove(lavaPos);
                    WorldManager.Instance.ApplyBlockChange(lavaPos.x, lavaPos.y, lavaPos.z, resultStone, false, dirtyChunks);

                    if (lavaPos == task.Pos) return;
                }
            }

            // 1. ПОТОК ВНИЗ
            Vector3Int downPos = task.Pos + Vector3Int.down;
            if (CanFlowInto(downPos))
            {
                FlowTo(downPos, task.FluidType, 1, dirtyChunks);
                return;
            }

            // 2. ПОТОК В СТОРОНЫ
            if (currentLevel >= maxDist) return;

            Vector3Int[] horizontalNeighbors = {
                task.Pos + Vector3Int.left,
                task.Pos + Vector3Int.right,
                task.Pos + Vector3Int.forward,
                task.Pos + Vector3Int.back
            };

            foreach (var nPos in horizontalNeighbors)
            {
                if (CanFlowInto(nPos))
                {
                    int targetLevel = currentLevel + 1;
                    if (!fluidLevels.TryGetValue(nPos, out int existingLevel) || targetLevel < existingLevel)
                    {
                        FlowTo(nPos, task.FluidType, targetLevel, dirtyChunks);
                    }
                }
            }
        }

        private bool HasValidFluidSource(Vector3Int pos, BlockType type, int currentLevel)
        {
            Vector3Int upPos = pos + Vector3Int.up;
            if (WorldManager.Instance.GetBlockAtWorldPosition(upPos.x, upPos.y, upPos.z) == type)
                return true;

            Vector3Int[] neighbors = {
                pos + Vector3Int.left,
                pos + Vector3Int.right,
                pos + Vector3Int.forward,
                pos + Vector3Int.back
            };

            foreach (var nPos in neighbors)
            {
                if (WorldManager.Instance.GetBlockAtWorldPosition(nPos.x, nPos.y, nPos.z) == type)
                {
                    int neighborLevel = GetFluidLevel(nPos);
                    if (neighborLevel < currentLevel)
                        return true;
                }
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
                        updateQueue.Enqueue(new FluidTask(nPos, type));
                    }
                }
            }
        }

        private int GetFluidLevel(Vector3Int pos)
        {
            return fluidLevels.TryGetValue(pos, out int level) ? level : 0;
        }

        private bool CanFlowInto(Vector3Int target)
        {
            if (WorldManager.Instance == null || WorldManager.Instance.Config == null) return false;

            if (target.y < 0 || target.y >= WorldManager.Instance.Config.ChunkHeight)
                return false;

            if (!WorldManager.Instance.IsChunkLoadedAtWorldPos(target.x, target.z))
                return false;

            BlockType targetType = WorldManager.Instance.GetBlockAtWorldPosition(target.x, target.y, target.z);

            return targetType == BlockType.Air || targetType == BlockType.OakSapling;
        }

        private void FlowTo(Vector3Int targetPos, BlockType fluidType, int level, HashSet<Vector2Int> dirtyChunks)
        {
            fluidLevels[targetPos] = level;
            WorldManager.Instance.ApplyBlockChange(targetPos.x, targetPos.y, targetPos.z, fluidType, false, dirtyChunks);

            if (inQueue.Add(targetPos))
            {
                updateQueue.Enqueue(new FluidTask(targetPos, fluidType));
            }
        }
    }
}