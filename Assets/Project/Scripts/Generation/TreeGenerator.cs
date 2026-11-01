using System;
using UnityEngine;
using MinecraftClone.Core;

namespace MinecraftClone.Generation
{
    public static class TreeGenerator
    {
        public static void GenerateTrees(ChunkData chunkData)
        {
            if (chunkData == null) return;

            // Сид для воспроизводимости деревьев в чанке
            int chunkSeed = chunkData.Config.Seed.GetHashCode() ^
                            (chunkData.Position.x * 9123841) ^
                            (chunkData.Position.y * 5891231);

            System.Random random = new System.Random(chunkSeed);

            // Количество деревьев на чанк (от 2 до 5)
            int treeCount = random.Next(2, 6);

            for (int i = 0; i < treeCount; i++)
            {
                // Отступаем по 2 блока от границ чанка для корректной генерации кроны
                int localX = random.Next(2, chunkData.Width - 2);
                int localZ = random.Next(2, chunkData.Width - 2);

                // Ищем верхнюю точку рельефа
                int surfaceY = FindSurfaceY(chunkData, localX, localZ);

                // Валидация точки спавна по высоте
                if (surfaceY < TerrainGenerator.SeaLevel || surfaceY >= chunkData.Height - 8)
                    continue;

                BlockType surfaceBlock = chunkData.GetBlock(localX, surfaceY, localZ);

                // Дерево сажается на траву или землю
                if (surfaceBlock != BlockType.Grass && surfaceBlock != BlockType.Dirt)
                    continue;

                // Строим дерево
                BuildOakTree(chunkData, localX, surfaceY + 1, localZ, random);
            }
        }

        private static int FindSurfaceY(ChunkData chunkData, int x, int z)
        {
            for (int y = chunkData.Height - 1; y >= 0; y--)
            {
                BlockType b = chunkData.GetBlock(x, y, z);
                if (b != BlockType.Air && b != BlockType.Water && b != BlockType.Lava)
                {
                    return y;
                }
            }
            return -1;
        }

        private static void BuildOakTree(ChunkData chunkData, int rootX, int rootY, int rootZ, System.Random random)
        {
            int treeHeight = random.Next(4, 7); // Высота ствола 4-6 блоков

            // 1. Создаем ствол
            for (int h = 0; h < treeHeight; h++)
            {
                int y = rootY + h;
                if (!chunkData.IsOutOfBounds(rootX, y, rootZ))
                {
                    chunkData.SetBlock(rootX, y, rootZ, BlockType.OakLog);
                }
            }

            // Меняем под деревом блок на Dirt
            chunkData.SetBlock(rootX, rootY - 1, rootZ, BlockType.Dirt);

            // 2. Создаем крону (Листву)
            int leafStart = rootY + treeHeight - 3;
            int leafEnd = rootY + treeHeight + 1;

            for (int y = leafStart; y <= leafEnd; y++)
            {
                int yOffset = y - (rootY + treeHeight);
                int radius = (yOffset >= 0) ? 1 : 2;

                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        // Срезаем углы у листвы
                        if (Math.Abs(dx) == radius && Math.Abs(dz) == radius)
                        {
                            if (yOffset < 0 && random.NextDouble() > 0.5)
                                continue;
                            if (yOffset >= 0)
                                continue;
                        }

                        int lx = rootX + dx;
                        int lz = rootZ + dz;

                        if (chunkData.IsOutOfBounds(lx, y, lz)) continue;

                        // Ставим листву только поверх Air (чтобы не затереть ствол)
                        if (chunkData.GetBlock(lx, y, lz) == BlockType.Air)
                        {
                            chunkData.SetBlock(lx, y, lz, BlockType.OakLeaves);
                        }
                    }
                }
            }
        }
    }
}