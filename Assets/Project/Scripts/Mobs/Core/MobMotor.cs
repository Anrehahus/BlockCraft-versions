using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.ScriptableObjects;

namespace MinecraftClone.Mobs
{
    // Физика моба: гравитация, вода, прыжок, столкновения с блоками.
    // Считает по данным блоков, а не по коллайдерам Unity, поэтому работает и вдали от хоста.
    public class MobMotor
    {
        private const float Eps = 0.001f;
        private const float Gravity = -20f;
        private const float WaterSpeedMultiplier = 0.8f;   // значения как у игрока
        private const float WaterGravity = -6f;
        private const float WaterUpAcceleration = 25f;
        private const float MaxSwimUp = 3.2f;
        private const float MaxSwimDown = -2.5f;

        private readonly float _halfWidth, _height, _jumpHeight;
        private float _vy;

        public bool Grounded { get; private set; }
        public bool HitHoriz { get; private set; }
        public bool InFluid { get; private set; }

        private static WorldManager Wm => WorldManager.Instance;

        public MobMotor(MobDataSO data)
        {
            _halfWidth = data.Width * 0.5f;
            _height = data.Height;
            _jumpHeight = data.JumpHeight;
        }

        // pos = точка у ног, wishVel = желаемая горизонтальная скорость
        public Vector3 Move(Vector3 pos, Vector3 wishVel, float dt)
        {
            if (Wm == null) return pos;

            bool moving = wishVel.sqrMagnitude > 0.0001f;
            InFluid = IsFluid(pos + Vector3.up * 0.1f) || IsFluid(pos + Vector3.up * 0.9f);
            bool headInFluid = IsFluid(pos + Vector3.up * (_height * 0.75f));

            Vector3 vel = wishVel * (InFluid ? WaterSpeedMultiplier : 1f);

            if (InFluid)
            {
                // плывёт как игрок с зажатым пробелом: всплывает, пока голова под водой,
                // и подпрыгивает у берега, чтобы выбраться
                _vy += WaterGravity * dt;
                if (headInFluid || (HitHoriz && moving)) _vy += WaterUpAcceleration * dt;
                _vy = Mathf.Clamp(_vy, MaxSwimDown, MaxSwimUp);
            }
            else
            {
                _vy += Gravity * dt;
                if (Grounded && HitHoriz && moving) _vy = Mathf.Sqrt(_jumpHeight * -2f * Gravity);
            }

            Vector3 delta = new Vector3(vel.x, _vy, vel.z) * dt;
            pos = MoveBox(pos, delta, out bool hx, out bool hy, out bool hz);

            HitHoriz = hx || hz;
            Grounded = hy && delta.y < 0f;
            if (hy) _vy = 0f;

            return pos;
        }

        // ---------- Блоки ----------

        public bool Solid(int x, int y, int z)
        {
            WorldManager wm = Wm;
            if (!wm.IsChunkLoadedAtWorldPos(x, z)) return true;   // чанк не загружен: стоим на месте
            BlockType b = wm.GetBlockAtWorldPosition(x, y, z);
            if (b == BlockType.Air || b == BlockType.Water || b == BlockType.Lava) return false;
            if (wm.BlockDatabase != null && wm.BlockDatabase.TryGetValue(b, out BlockDataSO d) && d != null)
                return d.IsSolid;
            return true;
        }

        private bool IsFluid(Vector3 p)
        {
            BlockType b = Wm.GetBlockAtWorldPosition(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
            return b == BlockType.Water || b == BlockType.Lava;
        }

        // есть ли прямая видимость между двумя точками
        public bool LineClear(Vector3 a, Vector3 b)
        {
            if (Wm == null) return false;
            float dist = Vector3.Distance(a, b);
            int steps = Mathf.CeilToInt(dist / 0.4f);
            for (int i = 1; i < steps; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)steps);
                if (Solid(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z))) return false;
            }
            return true;
        }

        private bool Collides(Vector3 p)
        {
            int x0 = Mathf.FloorToInt(p.x - _halfWidth + Eps), x1 = Mathf.FloorToInt(p.x + _halfWidth - Eps);
            int z0 = Mathf.FloorToInt(p.z - _halfWidth + Eps), z1 = Mathf.FloorToInt(p.z + _halfWidth - Eps);
            int y0 = Mathf.FloorToInt(p.y + Eps), y1 = Mathf.FloorToInt(p.y + _height - Eps);

            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                        if (Solid(x, y, z)) return true;
            return false;
        }

        // двигаем коробку по осям маленькими шагами, чтобы не проскакивать сквозь блоки
        private Vector3 MoveBox(Vector3 pos, Vector3 delta, out bool hitX, out bool hitY, out bool hitZ)
        {
            hitX = hitY = hitZ = false;

            float maxAbs = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y), Mathf.Abs(delta.z));
            int steps = Mathf.Max(1, Mathf.CeilToInt(maxAbs / 0.2f));
            Vector3 s = delta / steps;

            for (int i = 0; i < steps; i++)
            {
                if (s.x != 0f)
                {
                    Vector3 n = pos; n.x += s.x;
                    if (Collides(n)) hitX = true; else pos = n;
                }
                if (s.z != 0f)
                {
                    Vector3 n = pos; n.z += s.z;
                    if (Collides(n)) hitZ = true; else pos = n;
                }
                if (s.y != 0f)
                {
                    Vector3 n = pos; n.y += s.y;
                    if (Collides(n))
                    {
                        hitY = true;
                        if (s.y < 0f) pos.y = Mathf.Floor(n.y + Eps) + 1f;   // встали на верх блока
                        s.y = 0f;
                    }
                    else pos = n;
                }
            }
            return pos;
        }
    }
}