using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.ScriptableObjects;

namespace MinecraftClone.Physics
{
    public class GravityBlockSystem : MonoBehaviour
    {
        public static GravityBlockSystem Instance { get; private set; }

        [Header("Настройки гравитации")]
        [SerializeField] private float fallDelay = 0.05f;

        private readonly HashSet<Vector3Int> activeFallingBlocks = new HashSet<Vector3Int>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void NotifyBlockChanged(Vector3Int pos)
        {
            CheckAndSchedule(pos);
            CheckAndSchedule(pos + Vector3Int.up);
        }

        private void CheckAndSchedule(Vector3Int pos)
        {
            if (WorldManager.Instance == null) return;

            BlockType type = WorldManager.Instance.GetBlockAtWorldPosition(pos.x, pos.y, pos.z);
            if (!IsGravityBlock(type)) return;

            Vector3Int downPos = pos + Vector3Int.down;
            if (CanFallThrough(downPos))
            {
                if (activeFallingBlocks.Add(pos))
                {
                    StartCoroutine(FallRoutine(pos, type));
                }
            }
        }

        public bool IsGravityBlock(BlockType type)
        {
            if (WorldManager.Instance == null || WorldManager.Instance.BlockDatabase == null) return false;

            if (WorldManager.Instance.BlockDatabase.TryGetValue(type, out BlockDataSO data))
            {
                return data != null && data.IsGravityAffected;
            }

            return false;
        }

        private bool CanFallThrough(Vector3Int target)
        {
            if (WorldManager.Instance == null || WorldManager.Instance.Config == null) return false;

            if (target.y < 0 || target.y >= WorldManager.Instance.Config.ChunkHeight)
                return false;

            if (!WorldManager.Instance.IsChunkLoadedAtWorldPos(target.x, target.z))
                return false;

            BlockType targetType = WorldManager.Instance.GetBlockAtWorldPosition(target.x, target.y, target.z);

            return targetType == BlockType.Air ||
                   targetType == BlockType.Water ||
                   targetType == BlockType.Lava ||
                   targetType.IsPlant();
        }

        private IEnumerator FallRoutine(Vector3Int startPos, BlockType blockType)
        {
            yield return new WaitForSeconds(fallDelay);

            Vector3Int currentPos = startPos;

            while (true)
            {
                BlockType selfType = WorldManager.Instance.GetBlockAtWorldPosition(currentPos.x, currentPos.y, currentPos.z);
                if (selfType != blockType)
                {
                    activeFallingBlocks.Remove(startPos);
                    yield break;
                }

                Vector3Int downPos = currentPos + Vector3Int.down;

                if (!CanFallThrough(downPos))
                {
                    activeFallingBlocks.Remove(startPos);

                    // Обновляем окончательную позицию и запрашиваем пересборку меша
                    WorldManager.Instance.ApplyBlockChange(currentPos.x, currentPos.y, currentPos.z, blockType, rebuildMesh: true);

                    CheckAndSchedule(currentPos + Vector3Int.up);
                    yield break;
                }

                // Быстрая смена блоков в массиве ChunkData без триггера физических систем
                SetBlockDirect(currentPos, BlockType.Air);
                currentPos = downPos;
                SetBlockDirect(currentPos, blockType);

                yield return new WaitForSeconds(fallDelay);
            }
        }

        private void SetBlockDirect(Vector3Int pos, BlockType type)
        {
            ChunkData chunk = WorldManager.Instance.GetChunkAtWorldPos(pos.x, pos.z);
            if (chunk != null)
            {
                int localX = WorldManager.Instance.GetLocalCoord(pos.x, chunk.Width);
                int localZ = WorldManager.Instance.GetLocalCoord(pos.z, chunk.Width);
                chunk.SetBlock(localX, pos.y, localZ, type);
                WorldManager.Instance.BroadcastBlock(pos, type);
            }
        }
    }
}