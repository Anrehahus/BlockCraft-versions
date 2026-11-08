using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;

namespace MinecraftClone.Physics
{
    // Падающий блок как объект. Сервер/одиночная игра: при приземлении ставит блок.
    // Клиент: только визуальная копия, блок придёт сообщением от сервера.
    public class FallingBlockEntity : MonoBehaviour
    {
        private const float Gravity = 16f;        // 0.04 бл/тик² * 20²
        private const float Drag = 0.98f;         // за тик
        private const float Terminal = -39.2f;    // предельная скорость

        private GravityBlockSystem _owner;
        private BlockType _type;
        private int _x, _z;
        private bool _authoritative;
        private float _vy;
        private bool _done;

        public void Init(GravityBlockSystem owner, BlockType type, Vector3Int pos, bool authoritative)
        {
            _owner = owner; _type = type; _x = pos.x; _z = pos.z; _authoritative = authoritative;

            var mf = gameObject.AddComponent<MeshFilter>();
            var mr = gameObject.AddComponent<MeshRenderer>();
            mf.sharedMesh = owner.GetBlockMesh(type);
            if (WorldManager.Instance != null) mr.sharedMaterial = WorldManager.Instance.BlocksMaterial;
        }

        private void Update()
        {
            if (_done) return;

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _vy = Mathf.Max((_vy - Gravity * dt) * Mathf.Pow(Drag, dt * 20f), Terminal);

            Vector3 p = transform.position;
            float newY = p.y + _vy * dt;

            // клетки, в которые заходит низ блока
            int fromCell = Mathf.FloorToInt(p.y);
            int toCell = Mathf.FloorToInt(newY);
            for (int c = fromCell - 1; c >= toCell; c--)
            {
                if (!_owner.CanFallThrough(new Vector3Int(_x, c, _z)))
                {
                    Land(c + 1);
                    return;
                }
            }

            transform.position = new Vector3(p.x, newY, p.z);
        }

        // сразу на место, куда блок упал бы (нужно перед сохранением мира)
        public void LandNow()
        {
            if (_done) return;
            int c = Mathf.FloorToInt(transform.position.y);
            while (c > 0 && _owner.CanFallThrough(new Vector3Int(_x, c - 1, _z))) c--;
            Land(c);
        }

        private void Land(int cell)
        {
            if (_done) return;
            _done = true;

            var wm = WorldManager.Instance;
            if (_authoritative && wm != null && wm.Config != null)
            {
                // клетку могли занять, пока блок падал: поднимаемся до свободной
                while (cell < wm.Config.ChunkHeight && !_owner.CanFallThrough(new Vector3Int(_x, cell, _z))) cell++;
                if (cell < wm.Config.ChunkHeight)
                    wm.ApplyBlockChange(_x, cell, _z, _type, rebuildMesh: true);
            }

            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_owner != null) _owner.Unregister(this);
        }

        // занимает ли падающий блок (его коробка по высоте [y, y+1]) эту клетку
        public bool Occupies(Vector3Int cell)
        {
            if (_done || cell.x != _x || cell.z != _z) return false;
            float y = transform.position.y;
            return y < cell.y + 1f && y + 1f > cell.y;
        }
    }
}