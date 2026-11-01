using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.ScriptableObjects;

namespace MinecraftClone.Generation
{
    public class TerrainGenerator : MonoBehaviour
    {
        public const int SeaLevel = 63;
        public const int LavaLevel = 10;

        public void GenerateChunkTerrain(ChunkData chunkData)
        {
            WorldConfigSO config = chunkData.Config;

            int width = chunkData.Width;   // 16
            int height = chunkData.Height; // 128
            int totalBlocks = width * height * width;

            NativeArray<BlockType> blockResults = new NativeArray<BlockType>(totalBlocks, Allocator.TempJob);

            BetaTerrainParallelJob terrainJob = new BetaTerrainParallelJob
            {
                ChunkPosition = new int2(chunkData.Position.x, chunkData.Position.y),
                Width = width,
                Height = height,
                Seed = config.Seed,
                Results = blockResults
            };

            JobHandle jobHandle = terrainJob.Schedule(width * width, 16);
            jobHandle.Complete();

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < width; z++)
                {
                    int xzOffset = (x + z * width) * height;
                    for (int y = 0; y < height; y++)
                    {
                        chunkData.Blocks[x, y, z] = blockResults[xzOffset + y];
                    }
                }
            }

            blockResults.Dispose();

            CarveLavaLakes(chunkData);
            CarveWaterLakes(chunkData);
            OreGenerator.GenerateOres(chunkData);
        }

        #region Job System: Burst-Compiled Beta 1.7.3 Parallel Pipeline

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
        public struct BetaTerrainParallelJob : IJobParallelFor
        {
            public int2 ChunkPosition;
            public int Width;
            public int Height;
            public int Seed;

            [NativeDisableParallelForRestriction]
            [WriteOnly]
            public NativeArray<BlockType> Results;

            public unsafe void Execute(int index)
            {
                int x = index % Width;
                int z = index / Width;

                int worldX = ChunkPosition.x * Width + x;
                int worldZ = ChunkPosition.y * Width + z;

                float continental = GetOctave2D(worldX, worldZ, 0.001f, 3, Seed);
                float beachWidthNoise = GetOctave2D(worldX, worldZ, 0.05f, 2, Seed + 500);
                float maxBeachWidth = math.lerp(3.0f, 6.0f, (beachWidthNoise + 1.0f) * 0.5f);

                float targetHeight;
                float stretchFactor;

                if (continental > 0.0f)
                {
                    float t = math.smoothstep(0.0f, 0.8f, continental);
                    targetHeight = math.lerp(64.5f, 98.0f, t);
                    stretchFactor = math.lerp(14.0f, 26.0f, t);
                }
                else
                {
                    float t = math.smoothstep(-0.8f, 0.0f, continental);
                    targetHeight = math.lerp(54.0f, 62.5f, t);
                    stretchFactor = 10.0f;
                }

                float* densities = stackalloc float[17];

                for (int gy = 0; gy < 17; gy++)
                {
                    int worldY = gy * 8;
                    float noise3D = GetOctave3D(worldX, worldY, worldZ, 0.012f, 3, Seed + 100);
                    float density = noise3D - (worldY - targetHeight) / stretchFactor;

                    if (worldY > 112)
                    {
                        float cap = (worldY - 112) / 16.0f;
                        density = math.lerp(density, -10f, cap);
                    }

                    densities[gy] = density;
                }

                int estimatedSurfaceY = SeaLevel;
                for (int checkY = Height - 1; checkY >= 0; checkY--)
                {
                    int cY = checkY / 8;
                    float tY = (checkY % 8) / 8.0f;

                    if (cY >= 16)
                    {
                        cY = 15;
                        tY = 1.0f;
                    }

                    int nextCY = math.min(cY + 1, 16);
                    float d = math.lerp(densities[cY], densities[nextCY], tY);

                    if (d > 0f)
                    {
                        estimatedSurfaceY = checkY;
                        break;
                    }
                }

                int surfaceState = 0;
                bool isSandHeader = false;
                int columnOffset = (x + z * Width) * Height;

                for (int y = Height - 1; y >= 0; y--)
                {
                    int cellY = y / 8;
                    float ty = (y % 8) / 8.0f;

                    if (cellY >= 16)
                    {
                        cellY = 15;
                        ty = 1.0f;
                    }

                    int nextCellY = math.min(cellY + 1, 16);
                    float density = math.lerp(densities[cellY], densities[nextCellY], ty);
                    int blockIndex = columnOffset + y;

                    if (y == 0)
                    {
                        Results[blockIndex] = BlockType.Bedrock;
                        continue;
                    }

                    float bedrockRand = GetBlockRandom(worldX, y, worldZ, Seed);
                    if (y <= 2)
                    {
                        float chance = (y == 1) ? 0.80f : 0.20f;
                        Results[blockIndex] = (bedrockRand < chance) ? BlockType.Bedrock : BlockType.Stone;
                        continue;
                    }

                    if (IsCaveBlock(worldX, y, worldZ, Seed, estimatedSurfaceY))
                    {
                        Results[blockIndex] = (y <= LavaLevel) ? BlockType.Lava : BlockType.Air;
                        continue;
                    }

                    BlockType baseBlock = BlockType.Air;

                    if (density > 0f)
                    {
                        if (surfaceState == 0)
                        {
                            surfaceState = 1;
                            bool isOceanBorder = continental <= 0.05f;
                            bool isWaterHeight = (y >= SeaLevel && y <= SeaLevel + 2);

                            if (isOceanBorder && isWaterHeight)
                            {
                                float distToOcean = math.abs(continental) * 100f;
                                if (distToOcean <= maxBeachWidth)
                                {
                                    isSandHeader = true;
                                    baseBlock = BlockType.Sand;
                                }
                                else
                                {
                                    isSandHeader = false;
                                    baseBlock = BlockType.Grass;
                                }
                            }
                            else if (y < SeaLevel)
                            {
                                isSandHeader = false;
                                float bottomNoise = GetOctave2D(worldX, worldZ, 0.04f, 2, Seed + 300);
                                baseBlock = (bottomNoise < -0.2f) ? BlockType.Sand : (bottomNoise < 0.2f ? BlockType.Gravel : BlockType.Dirt);
                            }
                            else
                            {
                                isSandHeader = false;
                                baseBlock = BlockType.Grass;
                            }
                        }
                        else if (surfaceState > 0 && surfaceState <= 3)
                        {
                            baseBlock = isSandHeader ? BlockType.Sand : BlockType.Dirt;
                            surfaceState++;
                        }
                        else
                        {
                            float dirtVein = GetOctave3D(worldX, y, worldZ, 0.05f, 2, Seed + 700);
                            float gravelBase = GetOctave3D(worldX, y, worldZ, 0.08f, 2, Seed + 800);

                            if (dirtVein > 0.72f)
                                baseBlock = BlockType.Dirt;
                            else if (gravelBase > 0.84f)
                                baseBlock = BlockType.Gravel;
                            else
                                baseBlock = BlockType.Stone;
                        }
                    }
                    else
                    {
                        surfaceState = 0;
                        isSandHeader = false;
                        if (y <= SeaLevel)
                        {
                            baseBlock = BlockType.Water;
                        }
                    }

                    Results[blockIndex] = baseBlock;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool IsCaveBlock(int globalX, int y, int globalZ, int seed, int surfaceY)
            {
                if (y <= 4 || y >= surfaceY - 7) return false;
                if (y >= SeaLevel - 6 && y <= SeaLevel + 3) return false;

                float3 posA = new float3((globalX + seed) * 0.010f, y * 0.030f, globalZ * 0.010f);
                float3 posB = new float3((globalX - seed + 500) * 0.010f, y * 0.030f, (globalZ + 500) * 0.010f);

                float caveA = noise.cnoise(posA);
                float caveB = noise.cnoise(posB);

                float tunnelValue = (caveA * caveA) + (caveB * caveB);
                float caveThreshold = 0.004f;

                if (y > SeaLevel)
                {
                    float heightFactor = math.smoothstep((float)SeaLevel, 90f, (float)y);
                    caveThreshold = math.lerp(caveThreshold, 0.001f, heightFactor);
                }

                return tunnelValue < caveThreshold;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float GetOctave2D(float x, float z, float frequency, int octaves, int seedOffset)
            {
                float total = 0f;
                float amplitude = 1f;
                float maxAmplitude = 0f;
                float freq = frequency;

                for (int i = 0; i < octaves; i++)
                {
                    float2 sample = new float2((x + seedOffset * 17) * freq, (z + seedOffset * 31) * freq);
                    total += noise.cnoise(sample) * amplitude;
                    maxAmplitude += amplitude;
                    amplitude *= 0.5f;
                    freq *= 2.0f;
                }

                return total / maxAmplitude;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float GetOctave3D(float x, float y, float z, float frequency, int octaves, int seedOffset)
            {
                float total = 0f;
                float amplitude = 1f;
                float maxAmplitude = 0f;
                float freq = frequency;

                for (int i = 0; i < octaves; i++)
                {
                    float3 sample = new float3((x + seedOffset * 13) * freq, (y + seedOffset * 19) * freq, (z + seedOffset * 23) * freq);
                    total += noise.cnoise(sample) * amplitude;
                    maxAmplitude += amplitude;
                    amplitude *= 0.5f;
                    freq *= 2.0f;
                }

                return total / maxAmplitude;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float GetBlockRandom(int x, int y, int z, int seed)
            {
                uint h = (uint)(x * 374761393 ^ y * 668265263 ^ z * 2246822519 ^ seed * 3266489917);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (float)(h ^ (h >> 16)) / 4294967295f;
            }
        }

        #endregion

        #region Feature Pass: Safe Lava Lakes & Lowland Water Lakes

        private void CarveLavaLakes(ChunkData chunkData)
        {
            WorldConfigSO config = chunkData.Config;
            int chunkSeed = config.Seed.GetHashCode() ^ (chunkData.Position.x * 73856093) ^ (chunkData.Position.y * 19349663);
            System.Random random = new System.Random(chunkSeed);

            if (random.NextDouble() > 0.035) return;

            int centerX = random.Next(3, chunkData.Width - 3);
            int centerZ = random.Next(3, chunkData.Width - 3);
            int lakeBaseY = random.Next(5, 12);

            float radiusX = (float)(random.NextDouble() * 1.5 + 2.2);
            float radiusY = (float)(random.NextDouble() * 0.8 + 1.2);
            float radiusZ = (float)(random.NextDouble() * 1.5 + 2.2);

            int minX = Mathf.Clamp(centerX - Mathf.CeilToInt(radiusX) - 1, 0, chunkData.Width - 1);
            int maxX = Mathf.Clamp(centerX + Mathf.CeilToInt(radiusX) + 1, 0, chunkData.Width - 1);
            int minZ = Mathf.Clamp(centerZ - Mathf.CeilToInt(radiusZ) - 1, 0, chunkData.Width - 1);
            int maxZ = Mathf.Clamp(centerZ + Mathf.CeilToInt(radiusZ) + 1, 0, chunkData.Width - 1);

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int dy = -Mathf.CeilToInt(radiusY) - 1; dy <= 1; dy++)
                    {
                        int y = lakeBaseY + dy;
                        if (y <= 2 || y >= chunkData.Height) continue;

                        float dx = x - centerX;
                        float dz = z - centerZ;
                        float dist = (dx * dx) / (radiusX * radiusX) + (dy * dy) / (radiusY * radiusY) + (dz * dz) / (radiusZ * radiusZ);

                        if (dist <= 1.2f)
                        {
                            BlockType b = chunkData.GetBlock(x, y, z);
                            if (b == BlockType.Air || b == BlockType.Water) return;
                        }
                    }
                }
            }

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int dy = -Mathf.CeilToInt(radiusY); dy <= Mathf.CeilToInt(radiusY); dy++)
                    {
                        int y = lakeBaseY + dy;
                        if (y <= 2 || y >= chunkData.Height) continue;

                        float dx = x - centerX;
                        float dz = z - centerZ;
                        float dist = (dx * dx) / (radiusX * radiusX) + (dy * dy) / (radiusY * radiusY) + (dz * dz) / (radiusZ * radiusZ);

                        if (dist <= 1.0f)
                        {
                            BlockType b = chunkData.GetBlock(x, y, z);
                            if (b == BlockType.Bedrock) continue;

                            chunkData.SetBlock(x, y, z, (dy <= 0) ? BlockType.Lava : BlockType.Air);
                        }
                    }
                }
            }
        }

        private void CarveWaterLakes(ChunkData chunkData)
        {
            WorldConfigSO config = chunkData.Config;
            int chunkSeed = config.Seed.GetHashCode() ^ (chunkData.Position.x * 341873128) ^ (chunkData.Position.y * 132897987);
            System.Random random = new System.Random(chunkSeed);

            if (random.NextDouble() > 0.03) return;

            int originX = random.Next(4, chunkData.Width - 4);
            int originZ = random.Next(4, chunkData.Width - 4);

            int surfaceY = -1;
            for (int y = chunkData.Height - 1; y >= SeaLevel; y--)
            {
                if (chunkData.GetBlock(originX, y, originZ) == BlockType.Grass)
                {
                    surfaceY = y;
                    break;
                }
            }

            if (surfaceY < SeaLevel + 2 || surfaceY > SeaLevel + 7) return;

            int lakeY = surfaceY - 1;
            float radiusX = (float)(random.NextDouble() * 1.5 + 2.0);
            float radiusY = (float)(random.NextDouble() * 0.8 + 1.0);
            float radiusZ = (float)(random.NextDouble() * 1.5 + 2.0);

            int checkDepthY = lakeY - Mathf.CeilToInt(radiusY) - 1;
            for (int dx = -Mathf.CeilToInt(radiusX); dx <= Mathf.CeilToInt(radiusX); dx++)
            {
                for (int dz = -Mathf.CeilToInt(radiusZ); dz <= Mathf.CeilToInt(radiusZ); dz++)
                {
                    int lx = originX + dx;
                    int lz = originZ + dz;
                    if (!chunkData.IsOutOfBounds(lx, checkDepthY, lz))
                    {
                        if (chunkData.GetBlock(lx, checkDepthY, lz) == BlockType.Air) return;
                    }
                }
            }

            for (int dx = -Mathf.CeilToInt(radiusX); dx <= Mathf.CeilToInt(radiusX); dx++)
            {
                for (int dy = -Mathf.CeilToInt(radiusY); dy <= Mathf.CeilToInt(radiusY); dy++)
                {
                    for (int dz = -Mathf.CeilToInt(radiusZ); dz <= Mathf.CeilToInt(radiusZ); dz++)
                    {
                        int lx = originX + dx;
                        int ly = lakeY + dy;
                        int lz = originZ + dz;

                        if (chunkData.IsOutOfBounds(lx, ly, lz)) continue;

                        float distSq = (dx * dx) / (radiusX * radiusX) + (dy * dy) / (radiusY * radiusY) + (dz * dz) / (radiusZ * radiusZ);

                        if (distSq <= 1.0f)
                        {
                            BlockType current = chunkData.GetBlock(lx, ly, lz);
                            if (current != BlockType.Bedrock)
                            {
                                chunkData.SetBlock(lx, ly, lz, (dy <= 0) ? BlockType.Water : BlockType.Air);
                            }
                        }
                    }
                }
            }
        }

        #endregion
    }
}