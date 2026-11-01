using System;
using UnityEngine;
using MinecraftClone.Core;

namespace MinecraftClone.Generation
{
    public static class OreGenerator
    {
        public static void GenerateOres(ChunkData chunkData)
        {
            int seed = chunkData.Config.Seed;
            int chunkX = chunkData.Position.x;
            int chunkZ = chunkData.Position.y;

            // Детерминированный Random для конкретного чанка
            int chunkSeed = seed.GetHashCode() ^ (chunkX * 86668887) ^ (chunkZ * 3578611);
            System.Random random = new System.Random(chunkSeed);

            // 1. УГОЛЬ: Y = 0..127, 20 попыток, макс 17 блоков (среднее ~130-140 на чанк)
            GenerateCoal(chunkData, random);

            // 2. ЖЕЛЕЗО: Y = 0..63 (редко 64..67), 20 попыток, макс 9 блоков (среднее ~75-77 на чанк)
            GenerateIron(chunkData, random);

            // 3. ЗОЛОТО: Y = 0..31 (пик на 11..15), 20 попыток (с шансом успешного спавна), макс 9 блоков (среднее ~8-9 на чанк)
            GenerateGold(chunkData, random);
        }

        private static void GenerateCoal(ChunkData chunkData, System.Random random)
        {
            int attempts = 20;
            for (int i = 0; i < attempts; i++)
            {
                int startX = random.Next(0, chunkData.Width);
                int startZ = random.Next(0, chunkData.Width);
                int startY = random.Next(0, 128);

                // Случайный размер жилы от 6 до 17 блоков
                int veinSize = random.Next(6, 18);

                GenerateSingleVein(chunkData, random, startX, startY, startZ, veinSize, 17, BlockType.CoalOre);
            }
        }

        private static void GenerateIron(ChunkData chunkData, System.Random random)
        {
            int attempts = 20;
            for (int i = 0; i < attempts; i++)
            {
                int startX = random.Next(0, chunkData.Width);
                int startZ = random.Next(0, chunkData.Width);

                // Основной спавн 0..63, очень редко (5% шанс) поднимается до 64..67
                int startY = (random.NextDouble() < 0.05) ? random.Next(64, 68) : random.Next(0, 64);

                // Случайный размер жилы от 3 до 9 блоков
                int veinSize = random.Next(3, 10);

                GenerateSingleVein(chunkData, random, startX, startY, startZ, veinSize, 9, BlockType.IronOre);
            }
        }

        private static void GenerateGold(ChunkData chunkData, System.Random random)
        {
            int attempts = 20;
            for (int i = 0; i < attempts; i++)
            {
                // Чтобы получить среднее 8-9 блоков на чанк при 20 попытках, 
                // ограничим успешный спавн жилы ~12% шансом на попытку
                if (random.NextDouble() > 0.12) continue;

                int startX = random.Next(0, chunkData.Width);
                int startZ = random.Next(0, chunkData.Width);

                // Распределение высоты с пиком на 11..15
                int startY;
                double heightRoll = random.NextDouble();

                if (heightRoll < 0.60) // 60% жил спавнятся в узком целевом слое 11..15
                {
                    startY = random.Next(11, 16);
                }
                else // 40% распределяются по остальному диапазону 0..31
                {
                    startY = random.Next(0, 32);
                }

                // Размер жилы от 4 до 9 блоков
                int veinSize = random.Next(4, 10);

                GenerateSingleVein(chunkData, random, startX, startY, startZ, veinSize, 9, BlockType.GoldOre);
            }
        }

        /// <summary>
        /// Алгоритм генерации жилы с лимитом максимального количества блоков
        /// </summary>
        private static void GenerateSingleVein(
            ChunkData chunkData,
            System.Random random,
            double x,
            double y,
            double z,
            int targetBlocks,
            int maxAllowedBlocks,
            BlockType oreType)
        {
            float angle = (float)(random.NextDouble() * Math.PI);

            double x1 = x + Math.Sin(angle) * targetBlocks / 8.0;
            double x2 = x - Math.Sin(angle) * targetBlocks / 8.0;
            double z1 = z + Math.Cos(angle) * targetBlocks / 8.0;
            double z2 = z - Math.Cos(angle) * targetBlocks / 8.0;

            double y1 = y + random.Next(-1, 2);
            double y2 = y + random.Next(-1, 2);

            int placedBlocks = 0;

            for (int i = 0; i <= targetBlocks; i++)
            {
                if (placedBlocks >= maxAllowedBlocks) break;

                double progress = (double)i / targetBlocks;

                double currentX = x1 + (x2 - x1) * progress;
                double currentY = y1 + (y2 - y1) * progress;
                double currentZ = z1 + (z2 - z1) * progress;

                double radius = random.NextDouble() * targetBlocks / 16.0;
                double size = (Math.Sin(progress * Math.PI) + 1.0) * radius + 1.0;

                int minX = (int)Math.Floor(currentX - size / 2.0);
                int minY = (int)Math.Floor(currentY - size / 2.0);
                int minZ = (int)Math.Floor(currentZ - size / 2.0);

                int maxX = (int)Math.Floor(currentX + size / 2.0);
                int maxY = (int)Math.Floor(currentY + size / 2.0);
                int maxZ = (int)Math.Floor(currentZ + size / 2.0);

                for (int vx = minX; vx <= maxX; vx++)
                {
                    double xDist = (vx + 0.5 - currentX) / (size / 2.0);
                    if (xDist * xDist >= 1.0) continue;

                    for (int vy = minY; vy <= maxY; vy++)
                    {
                        double yDist = (vy + 0.5 - currentY) / (size / 2.0);
                        if (xDist * xDist + yDist * yDist >= 1.0) continue;

                        for (int vz = minZ; vz <= maxZ; vz++)
                        {
                            double zDist = (vz + 0.5 - currentZ) / (size / 2.0);
                            if (xDist * xDist + yDist * yDist + zDist * zDist >= 1.0) continue;

                            if (chunkData.GetBlock(vx, vy, vz) == BlockType.Stone)
                            {
                                chunkData.SetBlock(vx, vy, vz, oreType);
                                placedBlocks++;

                                if (placedBlocks >= maxAllowedBlocks) return;
                            }
                        }
                    }
                }
            }
        }
    }
}