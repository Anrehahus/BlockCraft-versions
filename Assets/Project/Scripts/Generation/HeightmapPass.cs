// Scripts/Generation/HeightmapPass.cs
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.ScriptableObjects;

namespace MinecraftClone.Generation
{
    public class HeightmapPass : ITerrainPass
    {
        public const int WaterLevel = 63;

        public void Process(ChunkData chunkData)
        {
            WorldConfigSO config = chunkData.Config;
            int worldChunkX = chunkData.Position.x * config.ChunkWidth;
            int worldChunkZ = chunkData.Position.y * config.ChunkWidth;

            for (int x = 0; x < config.ChunkWidth; x++)
            {
                for (int z = 0; z < config.ChunkWidth; z++)
                {
                    int globalX = worldChunkX + x;
                    int globalZ = worldChunkZ + z;

                    int surfaceHeight = CalculateSurfaceHeight(globalX, globalZ, config);

                    for (int worldY = 0; worldY < config.ChunkHeight; worldY++)
                    {
                        if (worldY == 0)
                        {
                            chunkData.SetBlock(x, worldY, z, BlockType.Bedrock);
                        }
                        else if (worldY < surfaceHeight - 4)
                        {
                            chunkData.SetBlock(x, worldY, z, BlockType.Stone);
                        }
                        else if (worldY < surfaceHeight)
                        {
                            chunkData.SetBlock(x, worldY, z, BlockType.Dirt);
                        }
                        else if (worldY == surfaceHeight)
                        {
                            BlockType topBlock = (surfaceHeight < WaterLevel) ? BlockType.Dirt : BlockType.Grass;
                            chunkData.SetBlock(x, worldY, z, topBlock);
                        }
                        else
                        {
                            BlockType filler = (worldY <= WaterLevel) ? BlockType.Water : BlockType.Air;
                            chunkData.SetBlock(x, worldY, z, filler);
                        }
                    }
                }
            }
        }

        public static int CalculateSurfaceHeight(int x, int z, WorldConfigSO config)
        {
            float seed = config.Seed;
            float biomeNoise = Mathf.PerlinNoise((x + seed) * 0.003f, (z + seed) * 0.003f);

            float plainsHeight = 63f + Mathf.PerlinNoise((x + seed + 100) * 0.012f, (z + seed + 100) * 0.012f) * 5f;
            float hillsHeight = 65f + (Mathf.PerlinNoise((x + seed + 200) * 0.02f, (z + seed + 200) * 0.02f) * 16f);

            float finalHeight = Mathf.Lerp(plainsHeight, hillsHeight, biomeNoise);
            return Mathf.Clamp(Mathf.FloorToInt(finalHeight), 1, config.ChunkHeight - 2);
        }
    }
}