using System;
using MinecraftClone.Core;

namespace MinecraftClone.Generation
{
    // Цветы на поверхности, грибы в пещерах и редко на поверхности.
    // Вызывается после TreeGenerator. Работает только внутри одного чанка и использует
    // сид чанка, поэтому у сервера и клиентов результат одинаковый.
    public static class PlantGenerator
    {
        // ---- настройки, их можно крутить ----
        private const int MaxFlowerPatchesPerChunk = 2;      // 0..N кустов цветов на чанк
        private const int FlowersPerPatch = 10;             // попыток поставить цветок в одном кусте
        private const double SurfaceMushroomChance = 0.08;  // шанс гриба на поверхности в чанке (редко)
        private const double CaveMushroomChance = 0.006;    // шанс гриба на каждую подходящую клетку пола пещеры
        private const int CaveMushroomsMax = 4;             // потолок грибов в пещерах на чанк
        private const int CaveRoof = 6;                     // сколько блоков породы минимум над грибом
        private const int CaveMinY = 11;                    // ниже лавы грибы не растут

        public static void GeneratePlants(ChunkData chunk)
        {
            if (chunk == null) return;

            int seed = chunk.Config.Seed.GetHashCode() ^
                       (chunk.Position.x * 1597334677) ^
                       (chunk.Position.y * 668265263);
            Random r = new Random(seed);

            GenerateFlowers(chunk, r);
            GenerateSurfaceMushroom(chunk, r);
            GenerateCaveMushrooms(chunk, r);
        }

        // ---------- Цветы ----------

        private static void GenerateFlowers(ChunkData c, Random r)
        {
            int patches = r.Next(0, MaxFlowerPatchesPerChunk + 1);
            for (int p = 0; p < patches; p++)
            {
                BlockType flower = r.Next(2) == 0 ? BlockType.Dandelion : BlockType.Poppy;
                int cx = r.Next(0, c.Width);
                int cz = r.Next(0, c.Width);

                for (int i = 0; i < FlowersPerPatch; i++)
                {
                    int x = cx + r.Next(-3, 4);
                    int z = cz + r.Next(-3, 4);
                    TryPlaceOnSurface(c, x, z, flower, false);
                }
            }
        }

        // ---------- Грибы на поверхности (редко) ----------

        private static void GenerateSurfaceMushroom(ChunkData c, Random r)
        {
            // r.Next вызываем всегда, чтобы последовательность случайных чисел не зависела от результата
            double roll = r.NextDouble();
            int x = r.Next(0, c.Width);
            int z = r.Next(0, c.Width);
            BlockType mushroom = r.Next(2) == 0 ? BlockType.BrownMushroom : BlockType.RedMushroom;

            if (roll < SurfaceMushroomChance)
                TryPlaceOnSurface(c, x, z, mushroom, true);
        }

        // ---------- Грибы в пещерах ----------

        private static void GenerateCaveMushrooms(ChunkData c, Random r)
        {
            int placed = 0;

            for (int x = 0; x < c.Width && placed < CaveMushroomsMax; x++)
            {
                for (int z = 0; z < c.Width && placed < CaveMushroomsMax; z++)
                {
                    int top = TopSolidY(c, x, z);
                    int maxY = Math.Min(top - CaveRoof, c.Height - 3);

                    for (int y = CaveMinY; y <= maxY; y++)
                    {
                        if (c.GetBlock(x, y, z) != BlockType.Air) continue;
                        if (!IsMushroomFloor(c.GetBlock(x, y - 1, z))) continue;

                        // random вызываем на каждую подходящую клетку: порядок стабилен
                        double roll = r.NextDouble();
                        BlockType mushroom = r.Next(2) == 0 ? BlockType.BrownMushroom : BlockType.RedMushroom;

                        if (roll < CaveMushroomChance)
                        {
                            c.SetBlock(x, y, z, mushroom);
                            placed++;
                            if (placed >= CaveMushroomsMax) break;
                        }
                    }
                }
            }
        }

        private static bool IsMushroomFloor(BlockType b)
        {
            return b == BlockType.Stone || b == BlockType.Dirt || b == BlockType.Gravel || b == BlockType.Cobblestone;
        }

        // ---------- Общее ----------

        // Верхний твёрдый блок колонки (воздух, воду и лаву пропускаем)
        private static int TopSolidY(ChunkData c, int x, int z)
        {
            for (int y = c.Height - 1; y >= 0; y--)
            {
                BlockType b = c.GetBlock(x, y, z);
                if (b != BlockType.Air && b != BlockType.Water && b != BlockType.Lava) return y;
            }
            return -1;
        }

        // allowDirt: грибам подходит и земля, цветам только трава
        private static bool TryPlaceOnSurface(ChunkData c, int x, int z, BlockType plant, bool allowDirt)
        {
            if (c.IsOutOfBounds(x, 0, z)) return false;

            int y = TopSolidY(c, x, z);
            if (y < TerrainGenerator.SeaLevel || y + 1 >= c.Height) return false;   // не под водой

            BlockType ground = c.GetBlock(x, y, z);
            bool okGround = ground == BlockType.Grass || (allowDirt && ground == BlockType.Dirt);
            if (!okGround) return false;

            if (c.GetBlock(x, y + 1, z) != BlockType.Air) return false;

            c.SetBlock(x, y + 1, z, plant);
            return true;
        }
    }
}