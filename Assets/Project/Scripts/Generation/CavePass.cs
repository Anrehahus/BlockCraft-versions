// Scripts/Generation/CavePass.cs
using System;
using UnityEngine;
using MinecraftClone.Core;

namespace MinecraftClone.Generation
{
    public class CavePass : ITerrainPass
    {
        public void Process(ChunkData chunkData)
        {
            var config = chunkData.Config;
            int currentChunkX = chunkData.Position.x;
            int currentChunkZ = chunkData.Position.y;

            int chunkSeed = config.Seed.GetHashCode() ^ (currentChunkX * 341873128) ^ (currentChunkZ * 132897987);
            System.Random random = new System.Random(chunkSeed);

            if (random.NextDouble() > 0.05) return; // 5% шанс создания входа пещеры в чанке

            int startX = random.Next(0, config.ChunkWidth);
            int startZ = random.Next(0, config.ChunkWidth);
            int startY = random.Next(12, 45);

            Vector3 startPos = new Vector3(startX, startY, startZ);
            CarveTunnel(chunkData, startPos, random);
        }

        private void CarveTunnel(ChunkData chunkData, Vector3 startPos, System.Random random)
        {
            Vector3 currentPos = startPos;
            int length = random.Next(40, 80);

            for (int step = 0; step < length; step++)
            {
                int cx = Mathf.RoundToInt(currentPos.x);
                int cy = Mathf.RoundToInt(currentPos.y);
                int cz = Mathf.RoundToInt(currentPos.z);

                for (int x = -2; x <= 2; x++)
                {
                    for (int y = -2; y <= 2; y++)
                    {
                        for (int z = -2; z <= 2; z++)
                        {
                            if (x * x + y * y + z * z <= 4)
                            {
                                int bx = cx + x;
                                int by = cy + y;
                                int bz = cz + z;

                                if (bx >= 0 && bx < chunkData.Width &&
                                    by > 1 && by < chunkData.Height - 2 &&
                                    bz >= 0 && bz < chunkData.Width)
                                {
                                    if (chunkData.GetBlock(bx, by, bz) != BlockType.Bedrock &&
                                        chunkData.GetBlock(bx, by, bz) != BlockType.Water)
                                    {
                                        chunkData.SetBlock(bx, by, bz, BlockType.Air);
                                    }
                                }
                            }
                        }
                    }
                }

                currentPos += new Vector3(
                    (float)(random.NextDouble() - 0.5) * 1.5f,
                    (float)(random.NextDouble() - 0.5) * 0.8f,
                    (float)(random.NextDouble() - 0.5) * 1.5f
                );
            }
        }
    }
}