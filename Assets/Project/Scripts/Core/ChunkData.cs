using UnityEngine;
using MinecraftClone.ScriptableObjects;

namespace MinecraftClone.Core
{
    public class ChunkData
    {
        public Vector2Int Position { get; }
        public BlockType[,,] Blocks { get; }
        public WorldConfigSO Config { get; }

        // Удобные геттеры для быстрого доступа к размерам чанка
        public int Width => Config.ChunkWidth;
        public int Height => Config.ChunkHeight;

        public ChunkData(Vector2Int position, WorldConfigSO config)
        {
            Position = position;
            Config = config;
            Blocks = new BlockType[config.ChunkWidth, config.ChunkHeight, config.ChunkWidth];
        }

        public BlockType GetBlock(int x, int y, int z)
        {
            if (IsOutOfBounds(x, y, z))
                return BlockType.Air;

            return Blocks[x, y, z];
        }

        public void SetBlock(int x, int y, int z, BlockType type)
        {
            if (IsOutOfBounds(x, y, z))
                return;

            Blocks[x, y, z] = type;
        }

        public bool IsOutOfBounds(int x, int y, int z)
        {
            return x < 0 || x >= Config.ChunkWidth ||
                   y < 0 || y >= Config.ChunkHeight ||
                   z < 0 || z >= Config.ChunkWidth;
        }
    }
}