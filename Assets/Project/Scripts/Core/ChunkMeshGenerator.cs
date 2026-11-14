using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.ScriptableObjects;

namespace MinecraftClone.Rendering
{
    // Чтение блоков по мировым координатам. Его реализуют WorldManager (основной поток)
    // и ChunkSnapshot (безопасно из фонового потока).
    public interface IBlockReader
    {
        BlockType GetBlockAtWorldPosition(int worldX, int worldY, int worldZ);
    }

    // Снимок чанка и его 8 соседей: ссылки на массивы блоков, собранные на ОСНОВНОМ потоке.
    // Фоновый поток читает только их и не трогает словари WorldManager.
    public sealed class ChunkSnapshot : IBlockReader
    {
        private readonly ChunkData[] _chunks = new ChunkData[9];
        private readonly int[] _versions = new int[9];
        private int _cx, _cz, _w, _h;

        public void Fill(ChunkData center, Dictionary<Vector2Int, ChunkData> loaded)
        {
            _cx = center.Position.x; _cz = center.Position.y;
            _w = center.Width; _h = center.Height;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    int i = (dx + 1) * 3 + (dz + 1);
                    loaded.TryGetValue(new Vector2Int(_cx + dx, _cz + dz), out ChunkData c);
                    _chunks[i] = c;
                    _versions[i] = c != null ? c.Version : 0;
                }
        }

        // true, если после снимка кто-то менял блоки в этих чанках: результат устарел
        public bool IsStale()
        {
            for (int i = 0; i < 9; i++)
                if (_chunks[i] != null && _chunks[i].Version != _versions[i]) return true;
            return false;
        }

        public BlockType GetBlockAtWorldPosition(int worldX, int worldY, int worldZ)
        {
            if (worldY < 0 || worldY >= _h) return BlockType.Air;
            int dx = FloorDiv(worldX, _w) - _cx;
            int dz = FloorDiv(worldZ, _w) - _cz;
            if (dx < -1 || dx > 1 || dz < -1 || dz > 1) return BlockType.Air;
            ChunkData c = _chunks[(dx + 1) * 3 + (dz + 1)];
            if (c == null) return BlockType.Air;
            return c.Blocks[worldX - (_cx + dx) * _w, worldY, worldZ - (_cz + dz) * _w];
        }

        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }
    }

    // UV-прямоугольники блоков. Таблица строится на основном потоке (нужны tex.name),
    // потом её читают любые потоки. Никаких строк и словарей в горячем пути.
    public static class ChunkUV
    {
        private const int Faces = 8;
        private const int MaxTypes = 64;
        private static Rect[] _table = NewTable();

        private static Rect[] NewTable()
        {
            var t = new Rect[MaxTypes * Faces];
            for (int i = 0; i < t.Length; i++) t[i] = new Rect(0, 0, 1, 1);
            return t;
        }

        public static void Build(Dictionary<BlockType, BlockDataSO> db, Dictionary<string, Rect> uvMap)
        {
            var t = NewTable();
            if (db != null && uvMap != null)
            {
                foreach (BlockType type in System.Enum.GetValues(typeof(BlockType)))
                {
                    if ((int)type >= MaxTypes) continue;
                    if (!db.TryGetValue(type, out BlockDataSO data) || data == null) continue;

                    for (int face = 0; face < 6; face++)
                    {
                        Texture2D tex = data.SideTexture;
                        if (face == 0 && data.TopTexture != null) tex = data.TopTexture;
                        else if (face == 1 && data.BottomTexture != null) tex = data.BottomTexture;

                        if (tex != null && uvMap.TryGetValue(tex.name, out Rect rect))
                            t[((int)type << 3) | face] = rect;
                    }
                }
            }
            _table = t;
        }

        public static Rect Get(BlockType type, int face) => _table[((int)type << 3) | face];
    }

    // Строит данные меша чанка. Обычный C#-класс без вызовов Unity API (только структуры
    // Vector3/Rect и т.п.), поэтому Build() можно запускать в фоновом потоке.
    public class ChunkMeshGenerator
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Color32> Colors = new List<Color32>();    // «освещение» граней как в Майнкрафте
        public readonly List<int> SolidTriangles = new List<int>();       // непрозрачные блоки (без alpha clip)
        public readonly List<int> CutoutTriangles = new List<int>();      // листья, стекло, растения, таблички (с alpha clip)
        public readonly List<int> CollisionTriangles = new List<int>();   // только твёрдая физика
        public readonly List<int> PlantTriangles = new List<int>();       // хитбокс саженцев для клика
        public readonly List<int> WaterTriangles = new List<int>();
        public readonly List<int> LavaTriangles = new List<int>();
        public readonly Dictionary<Vector3Int, BlockType> SignsNow = new Dictionary<Vector3Int, BlockType>();
        public ulong SolidHash, PlantHash;

        public void Build(ChunkData chunkData, IBlockReader world)
        {
            Vertices.Clear(); Uvs.Clear(); Normals.Clear(); Colors.Clear(); SignsNow.Clear();
            SolidTriangles.Clear(); CutoutTriangles.Clear(); CollisionTriangles.Clear(); PlantTriangles.Clear();
            WaterTriangles.Clear(); LavaTriangles.Clear();
            SolidHash = 0; PlantHash = 0;

            int width = chunkData.Width;
            int height = chunkData.Height;
            var blocks = chunkData.Blocks;

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    for (int z = 0; z < width; z++)
                    {
                        BlockType type = blocks[x, y, z];
                        if (type == BlockType.Air) continue;
                        GenerateBlockMesh(x, y, z, type, chunkData, world);
                    }
        }

        private static readonly Vector3Int[] Directions =
        {
            Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right,
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };

        // Нормали граней (порядок как у FaceVertsTable) и яркость граней Майнкрафта:
        // верх 1.0, низ 0.5, стороны X (запад/восток) 0.6, стороны Z (север/юг) 0.8
        private static readonly Vector3[] FaceNormals =
        {
            new Vector3(0, 1, 0), new Vector3(0, -1, 0), new Vector3(-1, 0, 0),
            new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1)
        };
        private static readonly byte[] FaceShade = { 255, 128, 153, 153, 204, 204 };

        private static readonly Color32 White = new Color32(255, 255, 255, 255);

        private static Color32 ShadeFromNormal(Vector3 n)
        {
            float s;
            if (n.y > 0.5f) s = 1f;
            else if (n.y < -0.5f) s = 0.5f;
            else s = 0.6f * n.x * n.x + 0.8f * n.z * n.z;
            byte b = (byte)(s * 255f + 0.5f);
            return new Color32(b, b, b, 255);
        }

        private static readonly Vector3[][] FaceVertsTable =
        {
            new[]{ new Vector3(0,1,0), new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,1,0) }, // 0 верх
            new[]{ new Vector3(0,0,1), new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1) }, // 1 низ
            new[]{ new Vector3(0,0,1), new Vector3(0,1,1), new Vector3(0,1,0), new Vector3(0,0,0) }, // 2 -X
            new[]{ new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(1,0,1) }, // 3 +X
            new[]{ new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1), new Vector3(0,0,1) }, // 4 +Z
            new[]{ new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,0,0) }, // 5 -Z
        };

        private void GenerateBlockMesh(
        int x, int y, int z,
        BlockType type,
        ChunkData chunkData,
        IBlockReader world)
        {
            if (type.IsSign())
            {
                AddSignData(x, y, z, type);
                return;
            }

            if (IsPlantBlock(type))
            {
                AddPlantData(x, y, z, type);
                return;
            }

            bool isFluid = type == BlockType.Water || type == BlockType.Lava;
            int baseWX = chunkData.Position.x * chunkData.Width + x;
            int baseWZ = chunkData.Position.y * chunkData.Width + z;
            float fluidTop = isFluid ? FluidTop(world, baseWX, y, baseWZ) : 1f;

            Vector3Int[] directions = Directions;

            int width = chunkData.Width;
            int height = chunkData.Height;

            for (int i = 0; i < directions.Length; i++)
            {
                Vector3Int dir = directions[i];
                int neighborX = x + dir.x;
                int neighborY = y + dir.y;
                int neighborZ = z + dir.z;

                BlockType neighborType;

                if (neighborX >= 0 && neighborX < width &&
                    neighborY >= 0 && neighborY < height &&
                    neighborZ >= 0 && neighborZ < width)
                {
                    neighborType = chunkData.Blocks[neighborX, neighborY, neighborZ];
                }
                else
                {
                    Vector2Int chunkPos = chunkData.Position;
                    int worldX = chunkPos.x * width + neighborX;
                    int worldZ = chunkPos.y * width + neighborZ;
                    neighborType = world.GetBlockAtWorldPosition(worldX, neighborY, worldZ);
                }

                bool drawFace = ShouldRenderFace(type, neighborType)
                    || (isFluid && i == 0 && fluidTop < 1f && !IsFluidType(neighborType))
                    || (type == BlockType.StoneSlab && (i == 0 || (i == 1 && neighborType == BlockType.StoneSlab)));

                if (drawFace)
                {
                    AddFaceData(x, y, z, i, type, fluidTop);
                }
                else if (isFluid && i >= 2 && neighborType == type)
                {
                    // сосед той же жидкости ниже (у него сверху пусто, а у нас блок): закрываем щель
                    float neighborTop = FluidTop(world, baseWX + dir.x, y, baseWZ + dir.z);
                    if (fluidTop > neighborTop + 0.001f)
                        AddFaceData(x, y, z, i, type, fluidTop, neighborTop);
                }
            }
        }

        private bool IsPlantBlock(BlockType type)
        {
            return type.IsPlant();
        }

        // Блоки с прозрачными пикселями в текстуре: им нужен alpha clip, поэтому они идут
        // в отдельный сабмеш. Если какой-то блок показывает чёрные/белые пятна на месте
        // «дырок» текстуры, добавьте его сюда.
        private static bool IsCutoutType(BlockType t)
        {
            return t == BlockType.OakLeaves || t == BlockType.Glass || t.IsPlant() || t.IsSign();
        }

        private bool ShouldRenderFace(BlockType current, BlockType neighbor)
        {
            bool seeThrough = neighbor == BlockType.Air || IsPlantBlock(neighbor) || neighbor.IsSign();

            if (current == BlockType.Water)
                return seeThrough || neighbor == BlockType.Lava;

            if (current == BlockType.Lava)
                return seeThrough || neighbor == BlockType.Water;

            if (neighbor == BlockType.Glass) return current != BlockType.Glass;

            return seeThrough || neighbor == BlockType.Water || neighbor == BlockType.Lava
                || (neighbor == BlockType.StoneSlab && current != BlockType.StoneSlab);
        }

        private void AddFaceData(
            int x, int y, int z,
            int faceIndex,
            BlockType type,
            float topY = 1f,
            float bottomY = 0f)
        {
            int vCount = Vertices.Count;
            Vector3 blockPos = new Vector3(x, y, z);

            byte shade = (type == BlockType.Water || type == BlockType.Lava) ? (byte)255 : FaceShade[faceIndex];
            Color32 faceColor = new Color32(shade, shade, shade, 255);
            Vector3 faceNormal = FaceNormals[faceIndex];

            Vector3[] faceVerts = GetFaceVertices(faceIndex);
            for (int i = 0; i < 4; i++)
            {
                Vector3 fv = faceVerts[i];
                if (type == BlockType.Water || type == BlockType.Lava)
                {
                    if (fv.y > 0.5f) fv.y = topY;               // верх опускается до 0.8
                    else if (faceIndex >= 2) fv.y = bottomY;      // у боковой грани-«заплатки» низ поднят до уровня соседа
                }
                else if (type == BlockType.StoneSlab && fv.y > 0.5f) fv.y = 0.5f;

                Vertices.Add(blockPos + fv);
                Normals.Add(faceNormal);
                Colors.Add(faceColor);
            }

            if (type == BlockType.Water)
            {
                WaterTriangles.Add(vCount);
                WaterTriangles.Add(vCount + 1);
                WaterTriangles.Add(vCount + 2);
                WaterTriangles.Add(vCount + 2);
                WaterTriangles.Add(vCount + 3);
                WaterTriangles.Add(vCount);
            }
            else if (type == BlockType.Lava)
            {
                LavaTriangles.Add(vCount);
                LavaTriangles.Add(vCount + 1);
                LavaTriangles.Add(vCount + 2);
                LavaTriangles.Add(vCount + 2);
                LavaTriangles.Add(vCount + 3);
                LavaTriangles.Add(vCount);
            }
            else
            {
                List<int> tris = IsCutoutType(type) ? CutoutTriangles : SolidTriangles;
                tris.Add(vCount);
                tris.Add(vCount + 1);
                tris.Add(vCount + 2);
                tris.Add(vCount + 2);
                tris.Add(vCount + 3);
                tris.Add(vCount);

                CollisionTriangles.Add(vCount);
                CollisionTriangles.Add(vCount + 1);
                CollisionTriangles.Add(vCount + 2);
                CollisionTriangles.Add(vCount + 2);
                CollisionTriangles.Add(vCount + 3);
                CollisionTriangles.Add(vCount);

                SolidHash = SolidHash * 1099511628211UL + (ulong)(((x * 256 + y) * 256 + z) * 8 + faceIndex + 1);
            }

            Rect uvRect = ChunkUV.Get(type, faceIndex);
            if (type == BlockType.StoneSlab && faceIndex >= 2)    // бока: нижняя половина текстуры
                uvRect = new Rect(uvRect.x, uvRect.y, uvRect.width, uvRect.height * 0.5f);

            Uvs.Add(new Vector2(uvRect.xMin, uvRect.yMin));
            Uvs.Add(new Vector2(uvRect.xMin, uvRect.yMax));
            Uvs.Add(new Vector2(uvRect.xMax, uvRect.yMax));
            Uvs.Add(new Vector2(uvRect.xMax, uvRect.yMin));
        }

        private void AddPlantData(
            int x, int y, int z,
            BlockType type)
        {
            PlantHash = PlantHash * 1099511628211UL + (ulong)((x * 256 + y) * 256 + z + 1);

            Vector3 blockPos = new Vector3(x, y, z);
            Rect uvRect = ChunkUV.Get(type, 0);

            // Визуальный X-образный меш (двусторонний с ровными текстурами)
            // Диагональ 1
            AddQuad(blockPos, new Vector3(0, 0, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1), new Vector3(0, 1, 0), uvRect);
            AddQuad(blockPos, new Vector3(1, 0, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0), new Vector3(1, 1, 1), uvRect);

            // Диагональ 2
            AddQuad(blockPos, new Vector3(0, 0, 1), new Vector3(1, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 1), uvRect);
            AddQuad(blockPos, new Vector3(1, 0, 0), new Vector3(0, 1, 1), new Vector3(0, 0, 1), new Vector3(1, 1, 0), uvRect);
        }

        private void AddQuad(Vector3 offset, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Rect uvRect)
        {
            int vCount = Vertices.Count;

            Vertices.Add(offset + v0);
            Vertices.Add(offset + v1);
            Vertices.Add(offset + v2);
            Vertices.Add(offset + v3);

            for (int k = 0; k < 4; k++)
            {
                Normals.Add(Vector3.up);
                Colors.Add(White);
            }

            CutoutTriangles.Add(vCount);
            CutoutTriangles.Add(vCount + 1);
            CutoutTriangles.Add(vCount + 2);

            CutoutTriangles.Add(vCount);
            CutoutTriangles.Add(vCount + 3);
            CutoutTriangles.Add(vCount + 1);

            Uvs.Add(new Vector2(uvRect.xMin, uvRect.yMin));
            Uvs.Add(new Vector2(uvRect.xMax, uvRect.yMax));
            Uvs.Add(new Vector2(uvRect.xMax, uvRect.yMin));
            Uvs.Add(new Vector2(uvRect.xMin, uvRect.yMax));
        }

        private Vector3[] GetFaceVertices(int faceIndex) => FaceVertsTable[faceIndex];

        private void AddSignData(int x, int y, int z, BlockType type)
        {
            SignsNow[new Vector3Int(x, y, z)] = type;

            Rect tex = ChunkUV.Get(type, 4);          // SideTexture блока-таблички
            Rect front = SubRect(tex, 2, 2, 24, 12);            // лицо доски: 24x12 пикселей
            Rect edge = SubRect(tex, 0, 2, 2, 12);              // торец
            Rect topBottom = SubRect(tex, 2, 0, 24, 2);         // верх и низ
            Rect stick = SubRect(tex, 2, 16, 2, 14);            // палка

            Vector3 origin = new Vector3(x, y, z);
            Vector3 f = type.SignFacing();

            AddSignBox(origin + SignGeometry.BoardCenter(type), f,
                       SignGeometry.HalfW, SignGeometry.HalfH, SignGeometry.HalfD, front, edge, topBottom);

            if (type.IsStandingSign())
                AddSignBox(origin + new Vector3(0.5f, 0.25f, 0.5f), f,
                           SignGeometry.StickHalf, SignGeometry.StickHalfH, SignGeometry.StickHalf, stick, stick, stick);
        }

        // Часть текстуры по пикселям (отсчёт от левого верхнего угла, текстура 32x32)
        private static Rect SubRect(Rect tex, int px, int py, int pw, int ph)
        {
            float u0 = px / 32f, u1 = (px + pw) / 32f;
            float v1 = 1f - py / 32f, v0 = 1f - (py + ph) / 32f;
            return new Rect(tex.x + u0 * tex.width, tex.y + v0 * tex.height, (u1 - u0) * tex.width, (v1 - v0) * tex.height);
        }

        private void AddSignBox(Vector3 c, Vector3 f, float hw, float hh, float hd, Rect front, Rect edge, Rect topBottom)
        {
            Vector3 up = Vector3.up;
            Vector3 r = Vector3.Cross(up, -f);   // «вправо» для того, кто читает табличку

            AddSignFace(c + f * hd, f, up, hw, hh, front);      // лицо
            AddSignFace(c - f * hd, -f, up, hw, hh, front);     // обратная сторона
            AddSignFace(c + r * hw, r, up, hd, hh, edge);       // торцы
            AddSignFace(c - r * hw, -r, up, hd, hh, edge);
            AddSignFace(c + up * hh, up, f, hw, hd, topBottom); // верх и низ
            AddSignFace(c - up * hh, -up, f, hw, hd, topBottom);
        }

        private void AddSignFace(Vector3 center, Vector3 normal, Vector3 upAxis, float halfRight, float halfUp, Rect uv)
        {
            Vector3 right = Vector3.Cross(upAxis, -normal);
            int v = Vertices.Count;

            Vertices.Add(center - right * halfRight - upAxis * halfUp);   // левый нижний
            Vertices.Add(center - right * halfRight + upAxis * halfUp);   // левый верхний
            Vertices.Add(center + right * halfRight + upAxis * halfUp);   // правый верхний
            Vertices.Add(center + right * halfRight - upAxis * halfUp);   // правый нижний

            Color32 signColor = ShadeFromNormal(normal);
            for (int k = 0; k < 4; k++)
            {
                Normals.Add(normal);
                Colors.Add(signColor);
            }

            Uvs.Add(new Vector2(uv.xMin, uv.yMin));
            Uvs.Add(new Vector2(uv.xMin, uv.yMax));
            Uvs.Add(new Vector2(uv.xMax, uv.yMax));
            Uvs.Add(new Vector2(uv.xMax, uv.yMin));

            CutoutTriangles.Add(v); CutoutTriangles.Add(v + 1); CutoutTriangles.Add(v + 2);
            CutoutTriangles.Add(v); CutoutTriangles.Add(v + 2); CutoutTriangles.Add(v + 3);
        }

        private static bool IsFluidType(BlockType t) => t == BlockType.Water || t == BlockType.Lava;

        // Высота жидкости в клетке: 1, если сверху жидкость или погружённый в неё блок, иначе 0.8
        private float FluidTop(IBlockReader wm, int wx, int y, int wz)
        {
            BlockType above = wm.GetBlockAtWorldPosition(wx, y + 1, wz);

            if (IsFluidType(above)) return 1f;   // столб жидкости

            bool blockAbove = above != BlockType.Air && !IsPlantBlock(above) && !above.IsSign();
            if (blockAbove)
            {
                // блок погружён: жидкость над ним или рядом с ним на его уровне
                if (IsFluidType(wm.GetBlockAtWorldPosition(wx, y + 2, wz))
                    || IsFluidType(wm.GetBlockAtWorldPosition(wx + 1, y + 1, wz))
                    || IsFluidType(wm.GetBlockAtWorldPosition(wx - 1, y + 1, wz))
                    || IsFluidType(wm.GetBlockAtWorldPosition(wx, y + 1, wz + 1))
                    || IsFluidType(wm.GetBlockAtWorldPosition(wx, y + 1, wz - 1)))
                    return 1f;
            }

            return 0.8f;   // сверху воздух, цветок или блок над уровнем воды
        }
    }
}