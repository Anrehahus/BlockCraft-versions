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

    // Поиск факелов (источников света) в чанке и его 8 соседях, в мировых координатах
    public interface ITorchSource
    {
        void CollectTorches(int chunkX, int chunkZ, List<Vector3Int> result);
    }

    // Размеры и поворот факела (в блоках), общие для меша и хитбокса
    public static class TorchGeometry
    {
        public const float Half = 0.0625f;      // 2 пикселя из 16
        public const float Height = 0.625f;     // 10 пикселей
        public const float TiltDegrees = 20f;   // наклон факела на стене (в оригинале 22.5°)
        private const float BaseY = 0.22f;      // высота центра низа настенного факела

        private static float Rad => TiltDegrees * Mathf.Deg2Rad;

        // центр низа факела в клетке (ось поворота). У настенного низ утоплен в блок-опору примерно наполовину
        public static Vector3 Base(BlockType t)
        {
            if (!t.IsWallTorch()) return new Vector3(0.5f, 0f, 0.5f);
            float d = 0.5f;   // ось на плоскости стены: задняя половина низа внутри блока
            return new Vector3(0.5f, BaseY, 0.5f) - t.TorchLean() * d;
        }

        // Точка факела в координатах клетки. dx, dz: смещение от оси факела (оси мира), y: высота вдоль факела.
        // Настенный факел поворачивается целиком (а не скашивается), как в оригинале.
        public static Vector3 Point(BlockType t, float dx, float y, float dz)
        {
            Vector3 b = Base(t);
            if (!t.IsWallTorch()) return b + new Vector3(dx, y, dz);

            Vector3 lean = t.TorchLean();
            Vector3 side = new Vector3(Mathf.Abs(lean.z), 0f, Mathf.Abs(lean.x));
            float u = dx * lean.x + dz * lean.z;    // вдоль наклона
            float w = dx * side.x + dz * side.z;    // вбок
            float c = Mathf.Cos(Rad), s = Mathf.Sin(Rad);
            return b + lean * (u * c + y * s) + Vector3.up * (y * c - u * s) + side * w;
        }

        public static void Bounds(BlockType t, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(1f, 1f, 1f);
            max = Vector3.zero;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = Point(t, (i & 1) == 0 ? -Half : Half, (i & 2) == 0 ? 0f : Height, (i & 4) == 0 ? -Half : Half);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            const float pad = 0.08f;   // чуть шире, чтобы легче целиться
            min = new Vector3(Mathf.Max(0f, min.x - pad), Mathf.Max(0f, min.y), Mathf.Max(0f, min.z - pad));
            max = new Vector3(Mathf.Min(1f, max.x + pad), Mathf.Min(1f, max.y), Mathf.Min(1f, max.z + pad));
        }
    }

    // Снимок чанка и его 8 соседей: ссылки на массивы блоков, собранные на ОСНОВНОМ потоке.
    // Фоновый поток читает только их и не трогает словари WorldManager.
    public sealed class ChunkSnapshot : IBlockReader, ITorchSource
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

        public void CollectTorches(int chunkX, int chunkZ, List<Vector3Int> result)
        {
            for (int i = 0; i < 9; i++)
                if (_chunks[i] != null) _chunks[i].CollectTorches(result);
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
        private const int MaxTypes = 128;
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
        public readonly List<int> SolidTriangles = new List<int>();        // непрозрачные блоки (без alpha clip)
        public readonly List<int> CutoutTriangles = new List<int>();      // листья, стекло, растения, таблички (с alpha clip)
        public readonly List<int> CollisionTriangles = new List<int>();   // только твёрдая физика
        public readonly List<int> PlantTriangles = new List<int>();       // хитбокс саженцев для клика
        public readonly List<int> WaterTriangles = new List<int>();
        public readonly List<int> LavaTriangles = new List<int>();
        public readonly Dictionary<Vector3Int, BlockType> SignsNow = new Dictionary<Vector3Int, BlockType>();
        public readonly List<Vector3Int> FiresNow = new List<Vector3Int>();   // огонь чанка (локальные координаты)
        public readonly List<Vector3Int> LavasNow = new List<Vector3Int>();   // лава рядом с горючим (локальные координаты)
        public ulong SolidHash, PlantHash;

        public byte[] ChunkLight;      // свет клеток самого чанка, индекс (y*w+z)*w+x
        public int LightWidth;

        // ---------- Освещение (уровни 0..15) ----------
        public const int MaxLight = 15;      // небо, лава, огонь
        public const int TorchLight = 14;
        private const int LightReach = MaxLight;
        public static readonly float[] LightBrightness =
            { 0.20f, 0.24f, 0.28f, 0.33f, 0.37f, 0.43f, 0.48f, 0.53f, 0.59f, 0.64f, 0.70f, 0.76f, 0.82f, 0.88f, 0.94f, 1.00f };

        private byte[] _opac, _light;        // непрозрачность и уровень света региона (чанк + 9 блоков вокруг)
        private int[] _top;                  // верхний блок, перекрывающий небо, по колонкам региона (-1 = открыто)
        private readonly List<int> _lightQueue = new List<int>();
        private readonly List<Vector3Int> _torches = new List<Vector3Int>();
        private int _rw, _rh, _rx0, _rz0, _ox, _oz;
        private float _curLight = 1f;

        // 0 = пропускает свет без потерь, 1 = гасит на 1 дополнительно, 2 = не пропускает
        private static byte Opacity(BlockType t)
        {
            if (t == BlockType.Air || t == BlockType.Glass || t.IsPlant() || t.IsSign() || t.IsTorch() || t.IsFire()) return 0;
            if (t == BlockType.Water || t == BlockType.Lava || t == BlockType.OakLeaves || t == BlockType.StoneSlab) return 1;
            return 2;
        }

        private byte OpacAt(int rx, int y, int rz, IBlockReader world)
        {
            int i = (y * _rw + rz) * _rw + rx;
            byte o = _opac[i];
            if (o == 255)
            {
                o = Opacity(world.GetBlockAtWorldPosition(_rx0 + rx, y, _rz0 + rz));
                _opac[i] = o;
            }
            return o;
        }

        private void ComputeLight(ChunkData cd, IBlockReader world)
        {
            int w = cd.Width, h = cd.Height;
            _rw = w + LightReach * 2; _rh = h;
            _rx0 = cd.Position.x * w - LightReach;
            _rz0 = cd.Position.y * w - LightReach;
            int cols = _rw * _rw, total = cols * h;

            if (_opac == null || _opac.Length != total)
            {
                _opac = new byte[total]; _light = new byte[total]; _top = new int[cols];
            }
            for (int i = 0; i < total; i++) { _opac[i] = 255; _light[i] = 0; }

            // 1. высота «потолка» каждой колонки: выше неё небо, уровень MaxLight
            for (int rz = 0; rz < _rw; rz++)
                for (int rx = 0; rx < _rw; rx++)
                {
                    int y = h - 1;
                    while (y >= 0 && OpacAt(rx, y, rz, world) == 0) y--;
                    _top[rz * _rw + rx] = y;
                }

            // 2. затравка: освещённые ячейки рядом с затенёнными
            _lightQueue.Clear();
            for (int rz = 0; rz < _rw; rz++)
                for (int rx = 0; rx < _rw; rx++)
                {
                    int t = _top[rz * _rw + rx], maxN = t;
                    if (rx > 0) maxN = Mathf.Max(maxN, _top[rz * _rw + rx - 1]);
                    if (rx < _rw - 1) maxN = Mathf.Max(maxN, _top[rz * _rw + rx + 1]);
                    if (rz > 0) maxN = Mathf.Max(maxN, _top[(rz - 1) * _rw + rx]);
                    if (rz < _rw - 1) maxN = Mathf.Max(maxN, _top[(rz + 1) * _rw + rx]);
                    int yEnd = Mathf.Min(h - 1, Mathf.Max(maxN, t + 1));
                    for (int y = t + 1; y <= yEnd; y++) _lightQueue.Add((y * _rw + rz) * _rw + rx);
                }

            // 2b. источники света: факел 14, лава и огонь 15
            _torches.Clear();
            if (world is ITorchSource ts) ts.CollectTorches(cd.Position.x, cd.Position.y, _torches);
            for (int k = 0; k < _torches.Count; k++)
            {
                Vector3Int p = _torches[k];
                int emit = Emission(world.GetBlockAtWorldPosition(p.x, p.y, p.z));
                if (emit == 0) continue;
                int trx = p.x - _rx0, trz = p.z - _rz0;
                if (trx < 0 || trz < 0 || trx >= _rw || trz >= _rw || p.y < 0 || p.y >= h) continue;
                if (p.y > _top[trz * _rw + trx]) continue;          // над землёй там и так MaxLight
                int ti = (p.y * _rw + trz) * _rw + trx;
                if (emit > _light[ti]) _light[ti] = (byte)emit;
                _lightQueue.Add(ti);
            }

            // 3. растекание: -1 за каждый блок
            for (int head = 0; head < _lightQueue.Count; head++)
            {
                int i = _lightQueue[head];
                int rx = i % _rw, rz = (i / _rw) % _rw, y = i / cols;
                int L = y > _top[rz * _rw + rx] ? MaxLight : _light[i];
                if (L <= 1) continue;

                for (int d = 0; d < 6; d++)
                {
                    int nx = rx + Directions[d].x, ny = y + Directions[d].y, nz = rz + Directions[d].z;
                    if (nx < 0 || nx >= _rw || nz < 0 || nz >= _rw || ny < 0 || ny >= h) continue;
                    if (ny > _top[nz * _rw + nx]) continue;          // небо, там и так MaxLight
                    byte o = OpacAt(nx, ny, nz, world);
                    if (o == 2) continue;
                    int nl = L - 1 - o;
                    int ni = (ny * _rw + nz) * _rw + nx;
                    if (nl > _light[ni]) { _light[ni] = (byte)nl; _lightQueue.Add(ni); }
                }
            }

            int len = w * h * w;
            if (ChunkLight == null || ChunkLight.Length != len) ChunkLight = new byte[len];
            LightWidth = w;
            for (int y = 0; y < h; y++)
                for (int z = 0; z < w; z++)
                    for (int x = 0; x < w; x++)
                        ChunkLight[(y * w + z) * w + x] = (byte)LightAt(_ox + x, y, _oz + z);
        }

        private static int Emission(BlockType t)
        {
            if (t == BlockType.Lava || t.IsFire()) return MaxLight;
            if (t.IsTorch()) return TorchLight;
            return 0;
        }

        // уровень света клетки в мировых координатах
        private int LightAt(int wx, int y, int wz)
        {
            if (y >= _rh) return MaxLight;
            if (y < 0) return 0;
            int rx = wx - _rx0, rz = wz - _rz0;
            if (rx < 0 || rz < 0 || rx >= _rw || rz >= _rw) return MaxLight;
            if (y > _top[rz * _rw + rx]) return MaxLight;
            return _light[(y * _rw + rz) * _rw + rx];
        }

        public void Build(ChunkData chunkData, IBlockReader world)
        {
            Vertices.Clear(); Uvs.Clear(); Normals.Clear(); Colors.Clear(); SignsNow.Clear(); FiresNow.Clear(); LavasNow.Clear();
            SolidTriangles.Clear(); CutoutTriangles.Clear(); CollisionTriangles.Clear(); PlantTriangles.Clear();
            WaterTriangles.Clear(); LavaTriangles.Clear();
            SolidHash = 0; PlantHash = 0;

            int width = chunkData.Width;
            int height = chunkData.Height;
            var blocks = chunkData.Blocks;

            _ox = chunkData.Position.x * width;
            _oz = chunkData.Position.y * width;
            ComputeLight(chunkData, world);

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
            if (type == BlockType.Lava && LavaNearFlammable(x, y, z, world))
                LavasNow.Add(new Vector3Int(x, y, z));

            if (type.IsSign())
            {
                AddSignData(x, y, z, type);
                return;
            }

            if (type.IsTorch())
            {
                AddTorchData(x, y, z, type);
                return;
            }

            if (type.IsFire())
            {
                FiresNow.Add(new Vector3Int(x, y, z));
                AddFireData(x, y, z, world);
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

        private bool LavaNearFlammable(int x, int y, int z, IBlockReader world)
        {
            int wx = _ox + x, wz = _oz + z;
            if (world.GetBlockAtWorldPosition(wx, y + 1, wz) == BlockType.Lava) return false;   // лава под лавой ничего не поджигает
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = 0; dy <= 2; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (world.GetBlockAtWorldPosition(wx + dx, y + dy, wz + dz).IsFlammable()) return true;
            return false;
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
            return t == BlockType.OakLeaves || t == BlockType.Glass || t.IsPlant() || t.IsSign() || t.IsFire();
        }

        private bool ShouldRenderFace(BlockType current, BlockType neighbor)
        {
            bool seeThrough = neighbor == BlockType.Air || IsPlantBlock(neighbor) || neighbor.IsSign() || neighbor.IsTorch() || neighbor.IsFire();

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

            Vector3Int fd = Directions[faceIndex];
            float lb = LightBrightness[LightAt(_ox + x + fd.x, y + fd.y, _oz + z + fd.z)];
            float baseShade = (type == BlockType.Water || type == BlockType.Lava) ? 255f : FaceShade[faceIndex];
            if (type == BlockType.Lava) lb = 1f;                        // лава светится сама
            byte shade = (byte)(baseShade * lb + 0.5f);
            Color32 faceColor = new Color32(shade, shade, shade, 255);
            Vector3 faceNormal = FaceNormals[faceIndex];

            Vector3[] faceVerts = GetFaceVertices(faceIndex);
            for (int i = 0; i < 4; i++)
            {
                Vector3 fv = faceVerts[i];
                if (type == BlockType.Water || type == BlockType.Lava)
                {
                    if (fv.y > 0.5f) fv.y = topY;                // верх опускается до 0.8
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

            byte pb = (byte)(255f * LightBrightness[LightAt(_ox + x, y, _oz + z)] + 0.5f);
            Color32 pcol = new Color32(pb, pb, pb, 255);

            // Визуальный X-образный меш (двусторонний с ровными текстурами)
            // Диагональ 1
            AddQuad(blockPos, new Vector3(0, 0, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1), new Vector3(0, 1, 0), uvRect, pcol);
            AddQuad(blockPos, new Vector3(1, 0, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0), new Vector3(1, 1, 1), uvRect, pcol);

            // Диагональ 2
            AddQuad(blockPos, new Vector3(0, 0, 1), new Vector3(1, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 1), uvRect, pcol);
            AddQuad(blockPos, new Vector3(1, 0, 0), new Vector3(0, 1, 1), new Vector3(0, 0, 1), new Vector3(1, 1, 0), uvRect, pcol);
        }

        private void AddFireData(int x, int y, int z, IBlockReader world)
        {
            Vector3 p = new Vector3(x, y, z);
            Rect a = ChunkUV.Get(BlockType.Fire, 0);   // fire_layer_0
            Rect b = ChunkUV.Get(BlockType.Fire, 4);   // fire_layer_1
            BlockType below = world.GetBlockAtWorldPosition(_ox + x, y - 1, _oz + z);
            bool solidBelow = below != BlockType.Air && below != BlockType.Water && below != BlockType.Lava
                              && !below.IsPlant() && !below.IsSign() && !below.IsTorch();
            bool pX = world.GetBlockAtWorldPosition(_ox + x + 1, y, _oz + z).IsFlammable();
            bool nX = world.GetBlockAtWorldPosition(_ox + x - 1, y, _oz + z).IsFlammable();
            bool pZ = world.GetBlockAtWorldPosition(_ox + x, y, _oz + z + 1).IsFlammable();
            bool nZ = world.GetBlockAtWorldPosition(_ox + x, y, _oz + z - 1).IsFlammable();
            bool up = world.GetBlockAtWorldPosition(_ox + x, y + 1, _oz + z).IsFlammable();
            bool sides = pX || nX || pZ || nZ || up;
            // на твёрдом основании (или совсем без соседей): крест из диагоналей
            if (solidBelow || !sides)
            {
                AddQuad(p, new Vector3(0, 0, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1), new Vector3(0, 1, 0), a, White);
                AddQuad(p, new Vector3(1, 0, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0), new Vector3(1, 1, 1), a, White);
                AddQuad(p, new Vector3(0, 0, 1), new Vector3(1, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 1), b, White);
                AddQuad(p, new Vector3(1, 0, 0), new Vector3(0, 1, 1), new Vector3(0, 0, 1), new Vector3(1, 1, 0), b, White);
            }
            // на боку горючего блока: плоскость вплотную к его стене
            const float e = 0.04f;
            if (pX) AddFirePlane(p, new Vector3(1 - e, 0, 0), new Vector3(1 - e, 1, 1), new Vector3(1 - e, 0, 1), new Vector3(1 - e, 1, 0), a);
            if (nX) AddFirePlane(p, new Vector3(e, 0, 0), new Vector3(e, 1, 1), new Vector3(e, 0, 1), new Vector3(e, 1, 0), b);
            if (pZ) AddFirePlane(p, new Vector3(0, 0, 1 - e), new Vector3(1, 1, 1 - e), new Vector3(1, 0, 1 - e), new Vector3(0, 1, 1 - e), b);
            if (nZ) AddFirePlane(p, new Vector3(0, 0, e), new Vector3(1, 1, e), new Vector3(1, 0, e), new Vector3(0, 1, e), a);
            // под нижней стороной горючего блока: плоскость вплотную к потолку (два слоя)
            if (up)
            {
                float u1 = 1f - e, u2 = 1f - 2f * e;
                AddFirePlane(p, new Vector3(0, u1, 0), new Vector3(1, u1, 1), new Vector3(1, u1, 0), new Vector3(0, u1, 1), a);
                AddFirePlane(p, new Vector3(0, u2, 0), new Vector3(1, u2, 1), new Vector3(1, u2, 0), new Vector3(0, u2, 1), b);
            }
        }

        // двусторонняя плоскость огня
        private void AddFirePlane(Vector3 o, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Rect uv)
        {
            AddQuad(o, v0, v1, v2, v3, uv, White);
            AddQuad(o, v2, v3, v0, v1, uv, White);
        }

        private void AddQuad(Vector3 offset, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Rect uvRect, Color32 col)
        {
            int vCount = Vertices.Count;

            Vertices.Add(offset + v0);
            Vertices.Add(offset + v1);
            Vertices.Add(offset + v2);
            Vertices.Add(offset + v3);

            for (int k = 0; k < 4; k++)
            {
                Normals.Add(Vector3.up);
                Colors.Add(col);
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
            _curLight = LightBrightness[LightAt(_ox + x, y, _oz + z)];

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

        // Часть текстуры факела по пикселям (сетка 16x16, отсчёт от левого верхнего угла)
        private static Rect Sub16(Rect tex, int px, int py, int pw, int ph)
        {
            float u0 = px / 16f, u1 = (px + pw) / 16f;
            float v1 = 1f - py / 16f, v0 = 1f - (py + ph) / 16f;
            return new Rect(tex.x + u0 * tex.width, tex.y + v0 * tex.height, (u1 - u0) * tex.width, (v1 - v0) * tex.height);
        }

        private void AddTorchData(int x, int y, int z, BlockType type)
        {
            Vector3 origin = new Vector3(x, y, z);
            float hw = TorchGeometry.Half, H = TorchGeometry.Height;

            // угол: ix/iz = 0|1, iy = 0 (низ) | 1 (верх); настенный факел повёрнут целиком
            Vector3 C(int ix, int iy, int iz) =>
                origin + TorchGeometry.Point(type, ix == 0 ? -hw : hw, iy == 0 ? 0f : H, iz == 0 ? -hw : hw);

            Rect tex = ChunkUV.Get(BlockType.Torch, 4);
            Rect side = Sub16(tex, 7, 6, 2, 10);
            Rect top = Sub16(tex, 7, 6, 2, 2);

            AddTorchQuad(C(0, 0, 0), C(0, 1, 0), C(1, 1, 0), C(1, 0, 0), side);   // -Z
            AddTorchQuad(C(1, 0, 1), C(1, 1, 1), C(0, 1, 1), C(0, 0, 1), side);   // +Z
            AddTorchQuad(C(0, 0, 1), C(0, 1, 1), C(0, 1, 0), C(0, 0, 0), side);   // -X
            AddTorchQuad(C(1, 0, 0), C(1, 1, 0), C(1, 1, 1), C(1, 0, 1), side);   // +X
            AddTorchQuad(C(0, 1, 0), C(0, 1, 1), C(1, 1, 1), C(1, 1, 0), top);    // верх
        }

        private void AddTorchQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Rect uv)
        {
            int v = Vertices.Count;
            Vertices.Add(p0); Vertices.Add(p1); Vertices.Add(p2); Vertices.Add(p3);
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p1).normalized;
            for (int k = 0; k < 4; k++) { Normals.Add(n); Colors.Add(White); }   // факел светится сам: полная яркость
            Uvs.Add(new Vector2(uv.xMin, uv.yMin)); Uvs.Add(new Vector2(uv.xMin, uv.yMax));
            Uvs.Add(new Vector2(uv.xMax, uv.yMax)); Uvs.Add(new Vector2(uv.xMax, uv.yMin));
            CutoutTriangles.Add(v); CutoutTriangles.Add(v + 1); CutoutTriangles.Add(v + 2);
            CutoutTriangles.Add(v + 2); CutoutTriangles.Add(v + 3); CutoutTriangles.Add(v);
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

            Color32 sc = ShadeFromNormal(normal);
            byte sb = (byte)(sc.r * _curLight + 0.5f);
            Color32 signColor = new Color32(sb, sb, sb, 255);
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

            bool blockAbove = above != BlockType.Air && !IsPlantBlock(above) && !above.IsSign() && !above.IsTorch();
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

    public static class LightTint
    {
        private static readonly HashSet<string> _warned = new HashSet<string>();

        // яркость 0..1 в точке мира
        public static float Sample(Vector3 pos)
        {
            var wm = MinecraftClone.Management.WorldManager.Instance;
            if (wm == null) return 1f;
            return ChunkMeshGenerator.LightBrightness[wm.GetLightAtWorldPosition(pos)];
        }

        // яркость 0..1 для эффектов в только что освободившейся клетке: максимум по клетке и её соседям
        public static float SampleOpen(Vector3 pos)
        {
            var wm = MinecraftClone.Management.WorldManager.Instance;
            if (wm == null) return 1f;
            int best = wm.GetLightAtWorldPosition(pos);
            best = Mathf.Max(best, wm.GetLightAtWorldPosition(pos + Vector3.up));
            best = Mathf.Max(best, wm.GetLightAtWorldPosition(pos + Vector3.down));
            best = Mathf.Max(best, wm.GetLightAtWorldPosition(pos + Vector3.left));
            best = Mathf.Max(best, wm.GetLightAtWorldPosition(pos + Vector3.right));
            best = Mathf.Max(best, wm.GetLightAtWorldPosition(pos + Vector3.forward));
            best = Mathf.Max(best, wm.GetLightAtWorldPosition(pos + Vector3.back));
            return ChunkMeshGenerator.LightBrightness[best];
        }

        public static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            bool ok = false;
            if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", c); ok = true; }
            if (m.HasProperty("_Color")) { m.SetColor("_Color", c); ok = true; }
            if (!ok && _warned.Add(m.shader.name))
                Debug.LogWarning("[LightTint] У шейдера " + m.shader.name + " нет _Color/_BaseColor: затемнение не сработает");
        }
    }
}