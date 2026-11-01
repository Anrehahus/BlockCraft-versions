// Scripts/Generation/VeinPass.cs
using UnityEngine;
using MinecraftClone.Core;

namespace MinecraftClone.Generation
{
    public class VeinPass : ITerrainPass
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

                    for (int worldY = 1; worldY < 55; worldY++)
                    {
                        if (chunkData.GetBlock(x, worldY, z) == BlockType.Stone)
                        {
                            if (IsGravelVein(globalX, worldY, globalZ, config.Seed))
                            {
                                chunkData.SetBlock(x, worldY, z, BlockType.Gravel);
                            }
                        }
                    }
                }
            }
        }

        private bool IsGravelVein(int globalX, int worldY, int globalZ, float seed)
        {
            float scale = 0.02f;
            float xy = Mathf.PerlinNoise((globalX + seed) * scale, (worldY + seed) * scale);
            float xz = Mathf.PerlinNoise((globalX + seed + 500) * scale, (globalZ + seed + 500) * scale);
            float yz = Mathf.PerlinNoise((worldY + seed + 1000) * scale, (globalZ + seed + 1000) * scale);

            float vein3D = (xy + xz + yz) / 3.0f;
            float frequencyMask = Mathf.PerlinNoise((globalX + seed + 2345) * 0.012f, (globalZ + seed + 6789) * 0.012f);

            return frequencyMask > 0.76f && vein3D > 0.52f;
        }
    }
}