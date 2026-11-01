// Scripts/Generation/SurfacePass.cs
using UnityEngine;
using MinecraftClone.Core;

namespace MinecraftClone.Generation
{
    public class SurfacePass : ITerrainPass
    {
        public void Process(ChunkData chunkData)
        {
            var config = chunkData.Config;
            int worldChunkX = chunkData.Position.x * config.ChunkWidth;
            int worldChunkZ = chunkData.Position.y * config.ChunkWidth;

            for (int x = 0; x < config.ChunkWidth; x++)
            {
                for (int z = 0; z < config.ChunkWidth; z++)
                {
                    int globalX = worldChunkX + x;
                    int globalZ = worldChunkZ + z;

                    int surfaceY = HeightmapPass.CalculateSurfaceHeight(globalX, globalZ, config);

                    // Если точка под водой — засыпаем дно гравием (~65%)
                    if (surfaceY < HeightmapPass.WaterLevel)
                    {
                        float noise = Mathf.PerlinNoise((globalX + config.Seed + 888) * 0.12f, (globalZ + config.Seed + 888) * 0.12f);
                        if (noise > 0.35f)
                        {
                            chunkData.SetBlock(x, surfaceY, z, BlockType.Gravel);
                        }
                    }
                }
            }
        }
    }
}