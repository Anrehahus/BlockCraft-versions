using System;
using UnityEngine;

namespace MinecraftClone.Mobs
{
    // Определяет «диагональный шов»: точка находится там, где два твёрдых блока соприкасаются только ребром/углом,
    // а две другие клетки вокруг свободны. Сквозь такой шов пролетать нельзя.
    public static class VoxelSeam
    {
        public const float R = 0.15f;

        public static bool IsSeam(Vector3 p, Func<int, int, int, bool> solid)
        {
            int x0 = Mathf.FloorToInt(p.x - R), x1 = Mathf.FloorToInt(p.x + R);
            int y0 = Mathf.FloorToInt(p.y - R), y1 = Mathf.FloorToInt(p.y + R);
            int z0 = Mathf.FloorToInt(p.z - R), z1 = Mathf.FloorToInt(p.z + R);

            bool sx = x0 != x1, sy = y0 != y1, sz = z0 != z1;

            if (sx && sy)
                for (int z = z0; z <= z1; z++)
                    if (Diag(solid(x0, y0, z), solid(x1, y0, z), solid(x0, y1, z), solid(x1, y1, z))) return true;

            if (sx && sz)
                for (int y = y0; y <= y1; y++)
                    if (Diag(solid(x0, y, z0), solid(x1, y, z0), solid(x0, y, z1), solid(x1, y, z1))) return true;

            if (sy && sz)
                for (int x = x0; x <= x1; x++)
                    if (Diag(solid(x, y0, z0), solid(x, y1, z0), solid(x, y0, z1), solid(x, y1, z1))) return true;

            return false;
        }

        // a b / c d: твёрдые только по диагонали
        private static bool Diag(bool a, bool b, bool c, bool d)
            => (a && d && !b && !c) || (b && c && !a && !d);
    }
}