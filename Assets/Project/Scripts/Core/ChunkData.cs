using UnityEngine;
using MinecraftClone.ScriptableObjects;

namespace MinecraftClone.Core
{
    public class ChunkData
    {
        public Vector2Int Position { get; }
        public BlockType[,,] Blocks { get; }
        public WorldConfigSO Config { get; }
        public int Version { get; private set; }
        public void Touch() { Version++; }
        public int Width { get; }
        public int Height { get; }

        public ChunkData(Vector2Int position, WorldConfigSO config)
        {
            Position = position;
            Config = config;
            Width = config.ChunkWidth;
            Height = config.ChunkHeight;
            Blocks = new BlockType[Width, Height, Width];
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
            Version++;
        }

        public bool IsOutOfBounds(int x, int y, int z)
        {
            return x < 0 || x >= Width ||
                   y < 0 || y >= Height ||
                   z < 0 || z >= Width;
        }

        // мировые координаты всех факелов чанка (для расчёта света)
        public void CollectTorches(System.Collections.Generic.List<Vector3Int> result)
        {
            int ox = Position.x * Width, oz = Position.y * Width;
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    for (int z = 0; z < Width; z++)
                        if (Blocks[x, y, z] >= BlockType.Torch)   // факелы идут последними в enum
                            result.Add(new Vector3Int(ox + x, y, oz + z));
        }
    }
}